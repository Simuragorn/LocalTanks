"""Builds runtime ground inputs for every catalogued World of Tanks arena.

The arena's global_AM image is the macro layer: roads, fields, water and the settlement.
It is intentionally not enlarged and baked together with detail, because a 4096 image over
an 800 metre arena can never stay sharp at the player's closest zoom. Instead this tool
produces two compact derived textures:

* a padded atlas of the arena's own terrain materials, normalised around neutral grey so a
  shader can multiply their grain over the macro layer without washing its colours away;
* a point-sampled material selector containing two material indices and their blend weight,
  chosen from the semantic height field without blocky random transitions.

Unity combines all three textures on one quad. Detail therefore remains at a physical world
scale, while there are no sprite blocks, tilemap seams or giant 8K/16K colour textures.
Every map-specific choice lives in environment_catalog.json; this script contains no Mines
material names or height thresholds.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

from PIL import Image, ImageChops, ImageFilter, ImageStat


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--mode", choices=("list", "build", "validate"), default="build")
    parser.add_argument(
        "--map-id",
        action="append",
        default=[],
        help="Map recipe to process. Repeat for several maps; omission processes every recipe.",
    )
    return parser.parse_args()


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def recipe_hash(recipe: dict) -> str:
    canonical = json.dumps(recipe, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def selected_recipes(catalog: dict, requested: set[str]) -> list[dict]:
    recipes = catalog.get("groundRecipes", [])
    known = {recipe["mapId"] for recipe in recipes}
    unknown = sorted(requested - known)
    if unknown:
        raise RuntimeError(f"Unknown ground map ids: {', '.join(unknown)}")
    return [recipe for recipe in recipes if not requested or recipe["mapId"] in requested]


def validate_recipe(recipe: dict, surfaces: dict[str, dict]) -> None:
    map_id = recipe.get("mapId", "")
    if not map_id:
        raise RuntimeError("Every ground recipe needs a non-empty mapId")
    macro_id = recipe.get("macroSurfaceId", "")
    if macro_id not in surfaces:
        raise RuntimeError(f"Ground recipe {map_id} references unknown macro surface {macro_id}")
    if surfaces[macro_id].get("tileable", True):
        raise RuntimeError(f"Ground recipe {map_id} macro surface must cover the whole map")

    materials = recipe.get("materials", [])
    if not 1 <= len(materials) <= 255:
        raise RuntimeError(f"Ground recipe {map_id} needs between 1 and 255 materials")
    previous_high: float | None = None
    for index, material in enumerate(materials):
        surface_id = material.get("surfaceId", "")
        if surface_id not in surfaces:
            raise RuntimeError(f"Ground recipe {map_id} references unknown material {surface_id}")
        if not surfaces[surface_id].get("tileable", False):
            raise RuntimeError(f"Ground recipe {map_id} material {surface_id} is not tileable")
        low = material.get("minimumHeightMeters")
        high = material.get("maximumHeightMeters")
        if low is None and index != 0:
            raise RuntimeError(f"Only the first band of {map_id} may omit minimumHeightMeters")
        if high is None and index != len(materials) - 1:
            raise RuntimeError(f"Only the last band of {map_id} may omit maximumHeightMeters")
        if low is not None and high is not None and float(low) >= float(high):
            raise RuntimeError(f"Ground recipe {map_id} has an empty height band for {surface_id}")
        if previous_high is not None and low is not None and not math.isclose(float(low), previous_high):
            raise RuntimeError(f"Ground recipe {map_id} has a gap or overlap before {surface_id}")
        previous_high = None if high is None else float(high)

    for field in ("selectorResolution", "atlasTileResolution", "atlasPaddingPixels", "atlasColumns"):
        if int(recipe.get(field, 0)) <= 0:
            raise RuntimeError(f"Ground recipe {map_id} needs positive {field}")
    if int(recipe["atlasColumns"]) > len(materials):
        raise RuntimeError(f"Ground recipe {map_id} has more atlas columns than materials")
    if float(recipe.get("detailTileMeters", 0)) <= 0:
        raise RuntimeError(f"Ground recipe {map_id} needs positive detailTileMeters")
    strength = float(recipe.get("detailStrength", -1))
    if not 0 <= strength <= 1:
        raise RuntimeError(f"Ground recipe {map_id} detailStrength must be in [0, 1]")
    if float(recipe.get("atlasHighPassRadiusPixels", 0)) < 0:
        raise RuntimeError(f"Ground recipe {map_id} atlasHighPassRadiusPixels cannot be negative")


def sample_bilinear(heights: list, columns: int, rows: int, column: float, line: float) -> float:
    column = min(max(column, 0.0), columns - 1.001)
    line = min(max(line, 0.0), rows - 1.001)
    x0, y0 = int(column), int(line)
    x1, y1 = min(x0 + 1, columns - 1), min(y0 + 1, rows - 1)
    fx, fy = column - x0, line - y0
    top = heights[y0][x0] * (1 - fx) + heights[y0][x1] * fx
    bottom = heights[y1][x0] * (1 - fx) + heights[y1][x1] * fx
    return top * (1 - fy) + bottom * fy


def weight_for(height: float, low: float, high: float, transition: float) -> float:
    if low == float("-inf"):
        distance = height - high
    elif high == float("inf"):
        distance = low - height
    else:
        distance = max(low - height, height - high)
    if distance <= 0:
        return 1.0
    return max(0.0, 1.0 - distance / max(transition, 0.001))


def normalise_tile(path: Path, side: int, high_pass_radius: float) -> tuple[Image.Image, list[float]]:
    with Image.open(path) as source:
        tile = source.convert("RGB").resize((side, side), Image.Resampling.LANCZOS)

    source_means = ImageStat.Stat(tile).mean[:3]
    # These textures become a multiplicative detail layer. Removing broad colour/value changes prevents
    # each repeat from reading as a large square while preserving stones, cracks, soil and grass fibres.
    if high_pass_radius > 0:
        low_frequency = tile.filter(ImageFilter.GaussianBlur(radius=high_pass_radius))
        tile = ImageChops.subtract(tile, low_frequency, scale=0.75, offset=128)

    means = ImageStat.Stat(tile).mean[:3]
    channels = []
    for channel, mean in zip(tile.split(), means):
        offset = 127.5 - float(mean)
        channels.append(channel.point(lambda value, add=offset: max(0, min(255, round(value + add)))))
    return Image.merge("RGB", channels), [round(float(value), 3) for value in source_means]


def wrap_padding(tile: Image.Image, padding: int) -> Image.Image:
    side = tile.width
    tiled = Image.new("RGB", (side * 3, side * 3))
    for row in range(3):
        for column in range(3):
            tiled.paste(tile, (column * side, row * side))
    return tiled.crop((side - padding, side - padding, side * 2 + padding, side * 2 + padding))


def build_atlas(recipe: dict, ground_root: Path, output: Path) -> tuple[dict, list[dict]]:
    tile_side = int(recipe["atlasTileResolution"])
    high_pass_radius = float(recipe.get("atlasHighPassRadiusPixels", 0))
    padding = int(recipe["atlasPaddingPixels"])
    columns = int(recipe["atlasColumns"])
    materials = recipe["materials"]
    rows = math.ceil(len(materials) / columns)
    cell_side = tile_side + padding * 2
    atlas = Image.new("RGB", (cell_side * columns, cell_side * rows), (128, 128, 128))
    metadata = []
    for index, material in enumerate(materials):
        source = ground_root / f"{material['surfaceId']}.png"
        if not source.is_file():
            raise RuntimeError(f"Ground material is missing: {source}")
        tile, average = normalise_tile(source, tile_side, high_pass_radius)
        atlas.paste(wrap_padding(tile, padding), ((index % columns) * cell_side, (index // columns) * cell_side))
        metadata.append({"index": index, "surfaceId": material["surfaceId"], "sourceAverageRgb": average})
    output.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(output)
    return {
        "columns": columns,
        "rows": rows,
        "tilePixels": tile_side,
        "paddingPixels": padding,
        "cellPixels": cell_side,
        "highPassRadiusPixels": high_pass_radius,
        "pixelSize": [atlas.width, atlas.height],
    }, metadata


def build_selector(recipe: dict, primitives: dict, output: Path) -> dict:
    side = int(recipe["selectorResolution"])
    transition = float(recipe["transitionMeters"])
    terrain = primitives["terrain"]
    origin = terrain["originMeters"]
    step = float(terrain["stepMeters"])
    columns = int(terrain["columns"])
    rows = int(terrain["rows"])
    heights = terrain["heightsMeters"]
    bounds = primitives["playableBoundsMeters"]
    minimum = bounds["min"]
    maximum = bounds["max"]
    span_x = maximum[0] - minimum[0]
    span_z = maximum[1] - minimum[1]
    bands = []
    for material in recipe["materials"]:
        low = float(material["minimumHeightMeters"]) if "minimumHeightMeters" in material else float("-inf")
        high = float(material["maximumHeightMeters"]) if "maximumHeightMeters" in material else float("inf")
        bands.append((low, high))

    # R and G are exact byte material indices; B is the contribution of G. Keeping indices
    # point-sampled avoids invalid in-between ids, while the explicit weight gives smooth transitions
    # without the visible checkerboard created by stochastic material selection.
    values = bytearray(side * side * 3)
    counts = [0] * len(bands)
    for y in range(side):
        world_z = minimum[1] + (1.0 - (y + 0.5) / side) * span_z
        line = (world_z - origin[1]) / step - 0.5
        for x in range(side):
            world_x = minimum[0] + (x + 0.5) / side * span_x
            column = (world_x - origin[0]) / step - 0.5
            height = sample_bilinear(heights, columns, rows, column, line)
            weights = [weight_for(height, low, high, transition) for low, high in bands]
            ranked = sorted(range(len(weights)), key=lambda item: weights[item], reverse=True)
            primary = ranked[0]
            secondary = ranked[1] if len(ranked) > 1 and weights[ranked[1]] > 0 else primary
            pair_total = weights[primary] + weights[secondary]
            secondary_share = weights[secondary] / pair_total if secondary != primary and pair_total > 0 else 0.0
            offset = (y * side + x) * 3
            values[offset] = primary
            values[offset + 1] = secondary
            values[offset + 2] = max(0, min(255, round(secondary_share * 255)))
            counts[primary] += 1

    selector = Image.frombytes("RGB", (side, side), bytes(values))
    output.parent.mkdir(parents=True, exist_ok=True)
    selector.save(output)
    return {
        "pixelSize": [side, side],
        "format": "primaryIndex-secondaryIndex-secondaryWeight",
        "dominantMaterialPixelCounts": counts,
    }


def generated_paths(project_root: Path, map_id: str) -> dict[str, Path]:
    root = project_root / "Assets" / "LocalOnly" / "WoTEnvironmentGenerated" / "Ground"
    return {
        "manifest": root / f"{map_id}_ground.json",
        "atlas": root / f"{map_id}_ground_atlas.png",
        "selector": root / f"{map_id}_ground_selector.png",
    }


def asset_path(project_root: Path, path: Path) -> str:
    return str(path.relative_to(project_root)).replace("\\", "/")


def build_recipe(project_root: Path, catalog: dict, recipe: dict) -> dict:
    map_id = recipe["mapId"]
    generated_root = project_root / "Assets" / "LocalOnly" / "WoTEnvironmentGenerated"
    ground_root = generated_root / "Ground"
    macro = ground_root / f"{recipe['macroSurfaceId']}.png"
    primitives_path = (
        project_root / "Assets" / "LocalOnly" / "WoTMapPrimitives" / map_id / f"{map_id}_primitives.json"
    )
    for required in (macro, primitives_path):
        if not required.is_file():
            raise RuntimeError(f"Required ground input is missing: {required}")
    primitives = read_json(primitives_path)
    if primitives.get("mapId") != map_id:
        raise RuntimeError(f"Ground recipe {map_id} received primitives for {primitives.get('mapId')}")

    paths = generated_paths(project_root, map_id)
    atlas_info, materials = build_atlas(recipe, ground_root, paths["atlas"])
    selector_info = build_selector(recipe, primitives, paths["selector"])
    document = {
        "schemaVersion": 1,
        "mapId": map_id,
        "displayName": recipe.get("displayName", map_id),
        "sourceRecipeSha256": recipe_hash(recipe),
        "macroTexture": asset_path(project_root, macro),
        "materialAtlas": asset_path(project_root, paths["atlas"]),
        "materialSelector": asset_path(project_root, paths["selector"]),
        "macroMaxTextureSize": int(recipe["macroMaxTextureSize"]),
        "detailTileMeters": float(recipe["detailTileMeters"]),
        "detailStrength": float(recipe["detailStrength"]),
        "atlas": atlas_info,
        "selector": selector_info,
        "materials": materials,
        "hashes": {
            "macro": sha256(macro),
            "atlas": sha256(paths["atlas"]),
            "selector": sha256(paths["selector"]),
            "primitives": sha256(primitives_path),
        },
        "rights": "Local prototype reference only. Do not publish WoT-derived output without permission.",
    }
    write_json(paths["manifest"], document)
    return document


def validate_generated(project_root: Path, recipe: dict) -> dict:
    paths = generated_paths(project_root, recipe["mapId"])
    if not paths["manifest"].is_file():
        raise RuntimeError(f"Generated ground manifest is missing: {paths['manifest']}")
    document = read_json(paths["manifest"])
    if document.get("schemaVersion") != 1 or document.get("mapId") != recipe["mapId"]:
        raise RuntimeError(f"Generated ground manifest has the wrong identity: {paths['manifest']}")
    if document.get("sourceRecipeSha256") != recipe_hash(recipe):
        raise RuntimeError(f"Generated ground for {recipe['mapId']} is stale; rebuild it")
    for key, hash_key in (("atlas", "atlas"), ("selector", "selector")):
        path = paths[key]
        if not path.is_file():
            raise RuntimeError(f"Generated ground texture is missing: {path}")
        if sha256(path) != document["hashes"][hash_key]:
            raise RuntimeError(f"Generated ground texture hash drifted: {path}")
    macro = project_root / document["macroTexture"]
    if not macro.is_file() or sha256(macro) != document["hashes"]["macro"]:
        raise RuntimeError(f"Ground macro texture is missing or changed: {macro}")
    return document


def main() -> None:
    args = arguments()
    project_root = Path(args.project_root).resolve()
    catalog_path = Path(__file__).resolve().parents[1] / "environment_catalog.json"
    catalog = read_json(catalog_path)
    if catalog.get("schemaVersion") != 2:
        raise RuntimeError("Ground material builder requires environment catalog schema 2")
    surfaces = {entry["id"]: entry for entry in catalog.get("surfaces", [])}
    recipes = selected_recipes(catalog, set(args.map_id))
    if not recipes:
        raise RuntimeError("Environment catalog contains no ground recipes")
    for recipe in recipes:
        validate_recipe(recipe, surfaces)

    if args.mode == "list":
        for recipe in recipes:
            print(f"ground  {recipe['mapId']:<24} {recipe.get('displayName', recipe['mapId'])}")
        return

    for recipe in recipes:
        if args.mode == "build":
            document = build_recipe(project_root, catalog, recipe)
            print(
                f"Built runtime ground for {recipe['mapId']}: "
                f"{len(document['materials'])} materials, selector "
                f"{document['selector']['pixelSize'][0]}x{document['selector']['pixelSize'][1]}"
            )
        validated = validate_generated(project_root, recipe)
        print(f"Validated runtime ground for {validated['mapId']}")


if __name__ == "__main__":
    main()
