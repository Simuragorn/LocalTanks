"""Bakes one ground texture for an imported arena from its surface image and material tiles.

Why this exists
---------------
Drawing the ground as a few thousand translucent sprite stamps leaves a visible grid: a
stamp is a flat quad with edges, and with hundreds of equal quads those edges line up
into lines. Varying size and opacity made it worse, not better, so the fix is to stop
compositing at runtime and produce the ground once, here.

What it does
------------
The surface image carries the arena's large shapes - roads, fields, water, the
settlement - at about five pixels per metre, which is soft under a camera close to the
ground. The material tiles carry the grain. Both are combined per pixel, choosing
material by terrain height, so the result has no object boundaries to show.

The material is decided from the height field rather than from a texture coordinate, so
the field's own grid is never visible: heights are sampled as a continuous surface, and
the choice between two materials is dithered over a band instead of cut at a line. The
dither uses a hash of the pixel position, so every rebuild produces the same ground.
"""

from __future__ import annotations

import argparse
import io
import json
import math
import zipfile
from pathlib import Path

from PIL import Image

# Side of the baked texture in pixels. The arena is about 209 by 176 world units, so this
# is roughly twenty pixels per unit: finer than the surface image it replaces by a factor
# of four, and small enough that the atlas lookups stay inexpensive.
IMAGE_SIDE = 4096

# Material atlas: six tiles of this side in a three by two grid.
TILE_SIDE = 1024

# Side of the coarse material field. Each cell is about two world units, small enough that
# the dither band below hides the steps.
FIELD_SIDE = 128

# How far a material reaches across the height range beyond its own band, in metres. Small
# values give crisp bands, large ones blend everything into mud.
TRANSITION_METRES = 6.0


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--map-id", default="10_hills")
    parser.add_argument("--output", default=None)
    return parser.parse_args()


def material_bands() -> list[tuple[str, float, float]]:
    """Material id and the height range it covers, in metres.

    These are the bands the scene builder used, kept identical so the baked ground and any
    future runtime preview agree about which material belongs where. The ranges come from
    the arena's own height distribution: on Mines the lowest ground is about sea level, the
    median is sixteen metres and the cliffs reach a hundred and sixty, so sand and cracked
    flats lie in the bottoms, ground and rubble on the flats, boulders on the slopes and
    bedrock above.
    """
    return [
        ("mines_sand", float("-inf"), 1.5),
        ("mines_cracked_earth", 1.5, 8.0),
        ("mines_ground", 8.0, 24.0),
        ("mines_rubble", 24.0, 50.0),
        ("mines_boulders", 50.0, 78.0),
        ("mines_rock", 78.0, float("inf")),
    ]


def hash_unit(x: int, y: int, salt: int) -> float:
    """Stable value in [0,1) from integer coordinates.

    Deliberately not a random source: the arena must look the same after every rebuild, or
    the scene diff becomes noise and a defect cannot be reproduced.
    """
    value = (x * 73856093) ^ (y * 19349663) ^ (salt * 83492791)
    value &= 0xFFFFFFFF
    value ^= value >> 13
    value = (value * 0x5BD1E995) & 0xFFFFFFFF
    value ^= value >> 15
    return (value & 0xFFFFFF) / float(0x1000000)


def load_surface(path: Path, side: int) -> Image.Image:
    with Image.open(path) as image:
        return image.convert("RGB").resize((side, side), Image.LANCZOS)


def build_material_atlas(folder: Path, ids: list[str]) -> Image.Image:
    atlas = Image.new("RGB", (TILE_SIDE * 3, TILE_SIDE * 2))
    for index, material_id in enumerate(ids):
        path = folder / f"{material_id}.png"
        if not path.is_file():
            raise RuntimeError(f"Material tile is missing: {path}")
        with Image.open(path) as tile:
            atlas.paste(tile.convert("RGB").resize((TILE_SIDE, TILE_SIDE), Image.LANCZOS),
                        ((index % 3) * TILE_SIDE, (index // 3) * TILE_SIDE))
    return atlas


def read_height_field(primitives_path: Path, side: int) -> list[list[float]]:
    """Terrain height per cell of a coarse grid, over the same area the surface image covers.

    Heights arrive on a regular grid with a known step, so the field is sampled bilinearly
    rather than nearest: a stepped field would put the material boundaries on its own grid
    and reintroduce exactly the artefact this script exists to remove.
    """
    document = json.loads(primitives_path.read_text(encoding="utf-8-sig"))
    terrain = document["terrain"]
    origin = terrain["originMeters"]
    step = terrain["stepMeters"]
    columns = terrain["columns"]
    rows = terrain["rows"]
    heights = terrain["heightsMeters"]
    bounds = document["playableBoundsMeters"]
    minimum = bounds["min"]
    maximum = bounds["max"]
    span_x = maximum[0] - minimum[0]
    span_z = maximum[1] - minimum[1]

    field: list[list[float]] = []
    for y in range(side):
        # The surface image is drawn with its own orientation: row zero of the image is the
        # far side of the map. Heights are stored south first, so the row is mirrored here.
        world_z = minimum[1] + (1.0 - (y + 0.5) / side) * span_z
        row: list[float] = []
        for x in range(side):
            world_x = minimum[0] + (x + 0.5) / side * span_x
            column = (world_x - origin[0]) / step - 0.5
            line = (world_z - origin[1]) / step - 0.5
            row.append(sample_bilinear(heights, columns, rows, column, line))
        field.append(row)
    return field


def sample_bilinear(heights: list, columns: int, rows: int, column: float, line: float) -> float:
    column = min(max(column, 0.0), columns - 1.001)
    line = min(max(line, 0.0), rows - 1.001)
    x0, y0 = int(column), int(line)
    x1, y1 = min(x0 + 1, columns - 1), min(y0 + 1, rows - 1)
    fx, fy = column - x0, line - y0
    top = heights[y0][x0] * (1 - fx) + heights[y0][x1] * fx
    bottom = heights[y1][x0] * (1 - fx) + heights[y1][x1] * fx
    return top * (1 - fy) + bottom * fy


def weight_for(height: float, low: float, high: float) -> float:
    """How strongly a material claims a height.

    Full inside its band and fading over TRANSITION_METRES outside it, so a boundary between
    two materials is a region where both are present rather than a line where one stops.
    """
    if math.isinf(low) and math.isinf(high):
        return 1.0
    if low == float("-inf"):
        distance = height - high
    elif high == float("inf"):
        distance = low - height
    else:
        distance = max(low - height, height - high)
    if distance <= 0:
        return 1.0
    return max(0.0, 1.0 - distance / TRANSITION_METRES)


def bake(project_root: Path, map_id: str, output: Path) -> dict:
    generated = project_root / "Assets" / "LocalOnly" / "WoTEnvironmentGenerated"
    surface_path = generated / "Ground" / "mines_aerial.png"
    materials_root = generated / "Ground"
    primitives = project_root / "Assets" / "LocalOnly" / "WoTMapPrimitives" / map_id / f"{map_id}_primitives.json"
    for required in (surface_path, primitives):
        if not required.is_file():
            raise RuntimeError(f"Required input is missing: {required}")

    bands = material_bands()
    ids = [band[0] for band in bands]
    surface = load_surface(surface_path, IMAGE_SIDE)
    atlas = build_material_atlas(materials_root, ids)
    field = read_height_field(primitives, FIELD_SIDE)

    surface_pixels = surface.load()
    atlas_pixels = atlas.load()
    scale = FIELD_SIDE / IMAGE_SIDE
    atlas_width = TILE_SIDE * 3

    result = Image.new("RGB", (IMAGE_SIDE, IMAGE_SIDE))
    result_pixels = result.load()

    for y in range(IMAGE_SIDE):
        field_y = min(int(y * scale), FIELD_SIDE - 1)
        height_row = field[field_y]
        for x in range(IMAGE_SIDE):
            height = height_row[min(int(x * scale), FIELD_SIDE - 1)]
            weights = [weight_for(height, low, high) for _, low, high in bands]
            total = sum(weights)
            if total <= 0.0001:
                result_pixels[x, y] = surface_pixels[x, y]
                continue

            # One material per pixel, chosen in proportion to the weights. Blending several tiles
            # together would average their grain into flat mud, where picking keeps every pixel a
            # real texture sample and turns the boundary into dither - the way terrain has always
            # been blended without a per-pixel shader.
            pick = hash_unit(x, y, 7) * total
            chosen = len(weights) - 1
            running = 0.0
            for index, weight in enumerate(weights):
                running += weight
                if pick <= running:
                    chosen = index
                    break

            tile_x = (x * 2) % TILE_SIDE
            tile_y = (y * 2) % TILE_SIDE
            tile = atlas_pixels[tile_x + (chosen % 3) * TILE_SIDE,
                                tile_y + (chosen // 3) * TILE_SIDE]
            base = surface_pixels[x, y]
            # Material on top of the surface image, at the same part strength the scene used, so
            # the picture keeps supplying roads and fields while the tile supplies grain.
            strength = 0.45
            result_pixels[x, y] = (
                int(base[0] * (1 - strength) + tile[0] * strength),
                int(base[1] * (1 - strength) + tile[1] * strength),
                int(base[2] * (1 - strength) + tile[2] * strength),
            )

    output.parent.mkdir(parents=True, exist_ok=True)
    result.save(output)
    return {
        "output": str(output),
        "size": [IMAGE_SIDE, IMAGE_SIDE],
        "materials": ids,
        "atlasWidth": atlas_width,
    }


def main() -> None:
    args = arguments()
    project_root = Path(args.project_root).resolve()
    output = Path(args.output) if args.output else (
        project_root / "Assets" / "LocalOnly" / "WoTEnvironmentGenerated" / "Ground" / "mines_ground_baked.png")
    report = bake(project_root, args.map_id, output)
    print(f"Baked ground written to {report['output']} at {report['size'][0]}x{report['size'][1]} "
          f"from {len(report['materials'])} materials")


if __name__ == "__main__":
    main()
