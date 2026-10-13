from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import struct
import subprocess
import tempfile
import zipfile
from pathlib import Path


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("list", "build", "validate"), required=True)
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--ids", default="")
    return parser.parse_args()


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def flatten_alpha(path: Path) -> None:
    """Forces a PNG opaque, leaving its colour untouched.

    Terrain surfaces come from BC3 sources, which always carry an alpha channel that the map itself does
    not use. Left in place it multiplies the sprite and darkens the ground, so it is removed here rather
    than compensated for later by whoever draws it.
    """
    from PIL import Image

    with Image.open(path) as image:
        if image.mode not in ("RGBA", "LA", "P"):
            return
        converted = image.convert("RGBA")
        # Taking the colour and forcing alpha keeps it exact; compositing over an opaque colour would
        # itself darken or tint the result.
        opaque = Image.new("RGB", converted.size)
        opaque.paste(converted.convert("RGB"))
        opaque.save(path)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def png_dimensions(path: Path) -> tuple[int, int, int]:
    data = path.read_bytes()[:26]
    if len(data) < 26 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise RuntimeError(f"Not a PNG: {path}")
    width, height = struct.unpack(">II", data[16:24])
    return width, height, data[25]


def selected(entries: list[dict], ids: set[str]) -> list[dict]:
    return [entry for entry in entries if not ids or entry["id"] in ids]


def extract_prefix(package: Path, prefix: str, destination: Path) -> int:
    count = 0
    with zipfile.ZipFile(package, "r") as archive:
        normalized = prefix.rstrip("/") + "/"
        for info in archive.infolist():
            if info.is_dir() or not info.filename.startswith(normalized):
                continue
            target = destination / Path(info.filename)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(info) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
            count += 1
    return count


def promote_hd_textures(root: Path) -> None:
    for hd_path in root.rglob("*_hd.dds"):
        normal_name = hd_path.name[:-7] + ".dds"
        shutil.copy2(hd_path, hd_path.with_name(normal_name))


def build_object(
    entry: dict,
    package_root: Path,
    work_root: Path,
    output_root: Path,
    blender: Path,
    blender_user_scripts: Path,
    renderer: Path,
    config_path: Path,
    maximum_dimension: int,
) -> dict:
    with tempfile.TemporaryDirectory(prefix=f"{entry['id']}_", dir=work_root) as temp_name:
        extracted = Path(temp_name)
        extracted_count = 0
        for package_name in entry["sourcePackages"]:
            package = package_root / package_name
            if not package.is_file():
                raise RuntimeError(f"Missing package for {entry['id']}: {package}")
            extracted_count += extract_prefix(package, entry["sourceRoot"], extracted)
        if extracted_count == 0:
            raise RuntimeError(f"No files extracted for {entry['id']} from {entry['sourceRoot']}")
        promote_hd_textures(extracted)
        model = extracted / Path(entry["sourceModel"])
        if not model.is_file():
            raise RuntimeError(f"Source model is missing after extraction: {model}")

        objects_root = output_root / "Objects"
        objects_root.mkdir(parents=True, exist_ok=True)
        output = objects_root / f"{entry['id']}.png"
        metadata_path = output.with_suffix(".json")
        output.unlink(missing_ok=True)
        metadata_path.unlink(missing_ok=True)
        environment = os.environ.copy()
        environment["BLENDER_USER_SCRIPTS"] = str(blender_user_scripts)
        command = [
            str(blender),
            "--background",
            "--factory-startup",
            "--python",
            str(renderer),
            "--",
            "--config",
            str(config_path),
            "--model",
            str(model),
            "--output",
            str(output),
            "--source-package",
            entry["sourcePackages"][0],
            "--maximum-dimension",
            str(maximum_dimension),
            "--exclude-materials",
            ",".join(entry.get("excludedMaterials", [])),
        ]
        subprocess.run(command, check=True, env=environment)
        if not output.is_file() or not metadata_path.is_file():
            raise RuntimeError(f"Blender did not produce a complete sprite for {entry['id']}")
        metadata = read_json(metadata_path)
        width, height, color_type = png_dimensions(output)
        if color_type != 6:
            raise RuntimeError(f"Expected RGBA PNG for {entry['id']}, color type is {color_type}")
        return {
            **entry,
            "sprite": str(output.relative_to(output_root)).replace("\\", "/"),
            "metadata": str(metadata_path.relative_to(output_root)).replace("\\", "/"),
            "pixelSize": [width, height],
            "worldSizeMeters": metadata["worldSizeMeters"],
            "sha256": sha256(output),
        }


def build_surface(entry: dict, package_root: Path, work_root: Path, output_root: Path, texconv: Path) -> dict:
    package = package_root / entry["sourcePackage"]
    if not package.is_file():
        raise RuntimeError(f"Missing package for {entry['id']}: {package}")
    with tempfile.TemporaryDirectory(prefix=f"{entry['id']}_", dir=work_root) as temp_name:
        temporary = Path(temp_name)
        dds = temporary / Path(entry["sourcePath"]).name
        with zipfile.ZipFile(package, "r") as archive:
            try:
                with archive.open(entry["sourcePath"]) as source, dds.open("wb") as output:
                    shutil.copyfileobj(source, output)
            except KeyError as error:
                raise RuntimeError(f"Texture is absent from {package.name}: {entry['sourcePath']}") from error
        converted = temporary / "Converted"
        converted.mkdir()
        subprocess.run([str(texconv), "-ft", "png", "-o", str(converted), "-y", str(dds)], check=True)
        candidates = list(converted.glob("*.png"))
        if len(candidates) != 1:
            raise RuntimeError(f"Expected one converted PNG for {entry['id']}, found {len(candidates)}")
        surfaces_root = output_root / "Ground"
        surfaces_root.mkdir(parents=True, exist_ok=True)
        result = surfaces_root / f"{entry['id']}.png"
        shutil.copy2(candidates[0], result)
        # A terrain surface is ground, so it is opaque by definition. The client stores these as BC3, which
        # always carries an alpha channel, and texconv preserves it; Unity then multiplies the sprite by
        # that channel and the map renders dark wherever it happens to be low. On the Mines surface that is
        # most of the image, which turns a normal photograph into a black field with pale blotches. The
        # colour data is correct, so only the unused channel is forced opaque here.
        flatten_alpha(result)
        width, height, color_type = png_dimensions(result)
        return {
            **entry,
            "texture": str(result.relative_to(output_root)).replace("\\", "/"),
            "pixelSize": [width, height],
            "pngColorType": color_type,
            "sha256": sha256(result),
        }


def validate_catalog(catalog: dict) -> None:
    if catalog.get("schemaVersion") != 1:
        raise RuntimeError("Unsupported environment catalog schema")
    entries = catalog.get("objects", []) + catalog.get("surfaces", [])
    ids = [entry.get("id", "") for entry in entries]
    if any(not value for value in ids):
        raise RuntimeError("Every environment catalog entry needs a non-empty id")
    duplicates = sorted({value for value in ids if ids.count(value) > 1})
    if duplicates:
        raise RuntimeError(f"Duplicate environment ids: {', '.join(duplicates)}")
    object_ids = {entry["id"] for entry in catalog.get("objects", [])}
    for entry in catalog.get("objects", []):
        variant = entry.get("gameplay", {}).get("fallenVariantId")
        if variant and variant not in object_ids:
            raise RuntimeError(f"Unknown fallenVariantId for {entry['id']}: {variant}")


def validate_outputs(catalog: dict, output_root: Path, ids: set[str]) -> dict:
    manifest_path = output_root / "library_manifest.json"
    if not manifest_path.is_file():
        raise RuntimeError(f"Generated manifest is missing: {manifest_path}")
    manifest = read_json(manifest_path)
    expected = {entry["id"] for entry in selected(catalog["objects"], ids)} | {
        entry["id"] for entry in selected(catalog["surfaces"], ids)
    }
    actual = {entry["id"] for entry in manifest.get("objects", [])} | {
        entry["id"] for entry in manifest.get("surfaces", [])
    }
    missing = sorted(expected - actual)
    if missing:
        raise RuntimeError(f"Generated manifest is missing catalog entries: {', '.join(missing)}")
    for section, path_field in (("objects", "sprite"), ("surfaces", "texture")):
        for entry in manifest.get(section, []):
            if ids and entry["id"] not in ids:
                continue
            path = output_root / entry[path_field]
            if not path.is_file():
                raise RuntimeError(f"Generated file is missing for {entry['id']}: {path}")
            width, height, _ = png_dimensions(path)
            if [width, height] != entry["pixelSize"]:
                raise RuntimeError(f"Pixel dimensions drifted for {entry['id']}")
            if sha256(path) != entry["sha256"]:
                raise RuntimeError(f"Hash drifted for {entry['id']}")
    return manifest


def main() -> None:
    args = arguments()
    project_root = Path(args.project_root).resolve()
    pipeline_root = project_root / "Tools" / "WoTEnvironmentPipeline"
    catalog_path = pipeline_root / "environment_catalog.json"
    catalog = read_json(catalog_path)
    validate_catalog(catalog)
    requested = {value for value in args.ids.split(",") if value}
    known = {entry["id"] for entry in catalog["objects"] + catalog["surfaces"]}
    unknown = sorted(requested - known)
    if unknown:
        raise RuntimeError(f"Unknown environment ids: {', '.join(unknown)}")

    if args.mode == "list":
        for entry in selected(catalog["objects"], requested):
            print(f"object  {entry['id']:<24} {entry['layer']:<24} {entry['displayName']}")
        for entry in selected(catalog["surfaces"], requested):
            print(f"surface {entry['id']:<24} Ground                   {entry['displayName']}")
        return

    output_root = project_root / "Assets" / "LocalOnly" / "WoTEnvironmentGenerated"
    if args.mode == "validate":
        manifest = validate_outputs(catalog, output_root, requested)
        print(f"Validated {len(manifest['objects'])} objects and {len(manifest['surfaces'])} surfaces")
        return

    sprite_pipeline = project_root / "Tools" / "WoTSpritePipeline"
    config_path = sprite_pipeline / "config.local.json"
    if not config_path.is_file():
        raise RuntimeError("WoTSpritePipeline local config is missing; run its setup first")
    config = read_json(config_path)
    blender = Path(config["blenderExecutable"])
    wot_root = Path(config["wotRoot"])
    package_root = wot_root / "res" / "packages"
    blender_user_scripts = sprite_pipeline / "Local" / "BlenderUserScripts"
    renderer = pipeline_root / "Blender" / "environment_sprite_renderer.py"
    texconv = sprite_pipeline / "ThirdParty" / "WoT-Blender-Toolkit" / "texconv.exe"
    for required in (blender, package_root, blender_user_scripts, renderer, texconv):
        if not required.exists():
            raise RuntimeError(f"Required environment pipeline dependency is missing: {required}")

    local_root = pipeline_root / "Local"
    work_root = local_root / "Work"
    work_root.mkdir(parents=True, exist_ok=True)
    output_root.mkdir(parents=True, exist_ok=True)

    object_results = []
    for entry in selected(catalog["objects"], requested):
        print(f"Rendering object: {entry['id']}", flush=True)
        object_results.append(
            build_object(
                entry,
                package_root,
                work_root,
                output_root,
                blender,
                blender_user_scripts,
                renderer,
                config_path,
                int(catalog["maximumSpriteDimension"]),
            )
        )

    surface_results = []
    for entry in selected(catalog["surfaces"], requested):
        print(f"Converting surface: {entry['id']}", flush=True)
        surface_results.append(build_surface(entry, package_root, work_root, output_root, texconv))

    existing = {"objects": [], "surfaces": []}
    manifest_path = output_root / "library_manifest.json"
    if requested and manifest_path.is_file():
        existing = read_json(manifest_path)
    rendered_ids = {entry["id"] for entry in object_results}
    converted_ids = {entry["id"] for entry in surface_results}
    merged_objects = [entry for entry in existing.get("objects", []) if entry["id"] not in rendered_ids]
    merged_surfaces = [entry for entry in existing.get("surfaces", []) if entry["id"] not in converted_ids]
    merged_objects.extend(object_results)
    merged_surfaces.extend(surface_results)
    order = {entry["id"]: index for index, entry in enumerate(catalog["objects"] + catalog["surfaces"])}
    merged_objects.sort(key=lambda entry: order.get(entry["id"], 999999))
    merged_surfaces.sort(key=lambda entry: order.get(entry["id"], 999999))
    manifest = {
        "schemaVersion": 1,
        "sourceCatalog": str(catalog_path.relative_to(project_root)).replace("\\", "/"),
        "pixelsPerMeter": catalog["pixelsPerMeter"],
        "objects": merged_objects,
        "surfaces": merged_surfaces,
        "knownLimitations": [
            "World of Tanks living vegetation is primarily SpeedTree SRT 06.0.0 and is not yet supported by the maintained Blender importer.",
            "Terrain objects using PBS_tiled_atlas_global are rejected until their atlas material is reconstructed.",
        ],
    }
    write_json(manifest_path, manifest)
    validate_outputs(catalog, output_root, requested)
    print(f"Built {len(object_results)} objects and {len(surface_results)} surfaces into {output_root}")


if __name__ == "__main__":
    main()
