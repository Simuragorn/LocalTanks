"""Headless WoT package importer and orthographic sprite renderer for Blender 4.3."""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
from pathlib import Path

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector


def arguments() -> argparse.Namespace:
    raw = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("probe", "render"), required=True)
    parser.add_argument("--config", required=True)
    parser.add_argument("--vehicle-id", default="")
    parser.add_argument("--nation", default="")
    parser.add_argument("--tier", default="")
    parser.add_argument("--vehicle-type", default="")
    parser.add_argument("--chassis-index", type=int, default=-1)
    parser.add_argument("--turret-index", type=int, default=-1)
    parser.add_argument("--gun-index", type=int, default=-1)
    return parser.parse_args(raw)


def resolve(base: Path, value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else (base / path).resolve()


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")


def enable_addon(pipeline_root: Path, config: dict):
    addon_path = resolve(pipeline_root, config["addonPath"])
    if not (addon_path / "__init__.py").is_file():
        raise RuntimeError(f"WoT Blender add-on is missing: {addon_path}")

    bpy.ops.preferences.addon_enable(module="wot_blender_toolkit")
    module = sys.modules.get("wot_blender_toolkit")
    if module is None:
        raise RuntimeError("Blender enabled the add-on but its module is unavailable")

    preferences = bpy.context.preferences.addons["wot_blender_toolkit"].preferences
    preferences.wot_game_path = config["wotRoot"]
    if not module.scan_wot_packages(config["wotRoot"]):
        raise RuntimeError("The add-on could not scan res/packages/scripts.pkg")
    return module


def flatten_catalog(module) -> list[dict]:
    entries: list[dict] = []
    for tier, nations in module.tank_db.items():
        for nation, vehicle_types in nations.items():
            for vehicle_type, vehicles in vehicle_types.items():
                for vehicle_id, display_name, locked in vehicles:
                    entries.append(
                        {
                            "id": vehicle_id,
                            "displayName": display_name,
                            "tier": tier,
                            "nation": nation,
                            "type": vehicle_type,
                            "locked": bool(locked),
                        }
                    )
    return sorted(entries, key=lambda item: (item["nation"], item["tier"], item["id"]))


def git_revision(addon_path: Path) -> str:
    try:
        return subprocess.check_output(
            ["git", "-C", str(addon_path), "rev-parse", "HEAD"],
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip()
    except Exception:
        return "unknown"


def find_vehicle(entries: list[dict], args: argparse.Namespace) -> dict:
    if not args.vehicle_id:
        raise RuntimeError("Render mode requires --vehicle-id")
    matches = [item for item in entries if item["id"].casefold() == args.vehicle_id.casefold()]
    if args.nation:
        matches = [item for item in matches if item["nation"] == args.nation]
    if args.tier:
        matches = [item for item in matches if item["tier"] == args.tier.zfill(2)]
    if args.vehicle_type:
        matches = [item for item in matches if item["type"] == args.vehicle_type]
    if len(matches) != 1:
        candidates = ", ".join(f"{m['nation']}/{m['tier']}/{m['id']}" for m in matches)
        raise RuntimeError(f"Expected one vehicle match, found {len(matches)}: {candidates}")
    return matches[0]


def select_vehicle(module, vehicle: dict, args: argparse.Namespace) -> dict:
    scene = bpy.context.scene
    scene.wot_selected_tier = vehicle["tier"]
    scene.wot_selected_nation = vehicle["nation"]
    scene.wot_selected_type = vehicle["type"]
    module.update_tank_list(None, bpy.context)

    indices = [i for i, item in enumerate(scene.wot_tank_list) if item.tank_id == vehicle["id"]]
    if len(indices) != 1:
        raise RuntimeError(f"Vehicle disappeared from the add-on list: {vehicle['id']}")
    scene.wot_tank_list_index = indices[0]
    module.analyze_tank_structure(None, bpy.context)

    cache = module.tank_xml_cache
    chassis_count = len(cache.get("chassis", []))
    turret_count = len(cache.get("turrets", []))
    chassis_index = args.chassis_index if args.chassis_index >= 0 else max(chassis_count - 1, 0)
    turret_index = args.turret_index if args.turret_index >= 0 else max(turret_count - 1, 0)
    if chassis_index >= chassis_count or turret_index >= turret_count:
        raise RuntimeError("Requested chassis or turret module index is out of range")

    guns = cache["turrets"][turret_index].get("guns", []) if turret_count else []
    gun_index = args.gun_index if args.gun_index >= 0 else max(len(guns) - 1, 0)
    if not guns or gun_index >= len(guns):
        raise RuntimeError("Requested gun module index is out of range")

    scene.wot_chassis_index = str(chassis_index)
    scene.wot_turret_index = str(turret_index)
    scene.wot_gun_index = str(gun_index)
    scene.wot_model_state = "NORMAL"
    scene.wot_selected_skin = "default"
    scene.wot_selected_lod = "lod0"

    selected = {
        "chassisIndex": chassis_index,
        "chassis": cache["chassis"][chassis_index]["name"],
        "turretIndex": turret_index,
        "turret": cache["turrets"][turret_index]["name"],
        "gunIndex": gun_index,
        "gun": guns[gun_index]["name"],
        "skin": "default",
        "lod": "lod0",
    }
    result = bpy.ops.import_model.dummy_load()
    if "FINISHED" not in result:
        raise RuntimeError(f"Tank import did not finish: {result}")
    return selected


def descendants(root) -> list:
    found = []
    pending = list(root.children)
    while pending:
        item = pending.pop()
        found.append(item)
        pending.extend(item.children)
    return found


def mesh_objects(root) -> list:
    return [obj for obj in descendants(root) if obj.type == "MESH"]


def part_roots(root) -> dict:
    return {
        obj.get("wot_part"): obj
        for obj in descendants(root)
        if "wot_part" in obj
    }


def material_report(root) -> list[dict]:
    result = []
    for obj in mesh_objects(root):
        for slot in obj.material_slots:
            material = slot.material
            if material is None:
                continue
            images = []
            if material.use_nodes:
                for node in material.node_tree.nodes:
                    image = getattr(node, "image", None)
                    if image is not None:
                        images.append(
                            {
                                "name": image.name,
                                "path": bpy.path.abspath(image.filepath),
                                "loaded": bool(image.has_data),
                            }
                        )
            result.append(
                {
                    "object": obj.name,
                    "material": material.name,
                    "images": images,
                }
            )
    return result


def world_bounds(objects: list) -> tuple[Vector, Vector]:
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    if not points:
        raise RuntimeError("Imported vehicle contains no renderable meshes")
    return (
        Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points))),
        Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points))),
    )


def add_world_and_lights(shadowless: bool) -> None:
    world = bpy.data.worlds.new("SpriteWorld") if bpy.context.scene.world is None else bpy.context.scene.world
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.18, 0.18, 0.18, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8

    for name, energy, location, size in (
        ("Key", 1700.0, (6.0, -7.0, 12.0), 8.0),
        ("Fill", 900.0, (-7.0, 4.0, 8.0), 7.0),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light_data.use_shadow = not shadowless
        light = bpy.data.objects.new(name, light_data)
        bpy.context.collection.objects.link(light)
        light.location = location
        direction = Vector((0.0, 0.0, 0.0)) - light.location
        light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def render_layers(root, render_root: Path, config: dict) -> dict:
    scene = bpy.context.scene
    meshes = mesh_objects(root)
    minimum, maximum = world_bounds(meshes)
    center = (minimum + maximum) * 0.5
    pixels_per_meter = float(config["render"].get("intermediatePixelsPerMeter", 128))
    largest_extent = max(maximum.x - minimum.x, maximum.y - minimum.y) * 1.08
    minimum_resolution = int(config["render"]["resolution"])
    required_resolution = int(math.ceil(largest_extent * pixels_per_meter / 256.0) * 256)
    resolution = max(minimum_resolution, required_resolution)
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    scene.view_settings.look = "AgX - Medium High Contrast"
    add_world_and_lights(bool(config["render"].get("shadowless", True)))

    camera_data = bpy.data.cameras.new("SpriteCamera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = resolution / pixels_per_meter
    camera = bpy.data.objects.new("SpriteCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.location = (center.x, center.y, maximum.z + max(camera_data.ortho_scale, 10.0))
    # LocalTanks source sprites face down and are rotated 180 degrees by the
    # prefab builder. Match that convention so generated sprites are drop-in.
    camera.rotation_euler = (0.0, 0.0, math.pi)
    scene.camera = camera

    parts = part_roots(root)
    turret_root = parts.get("Turret")
    turret_pivot = None
    if turret_root is not None:
        normalized = world_to_camera_view(scene, camera, turret_root.matrix_world.translation)
        turret_pivot = {
            "xPixels": float(normalized.x * resolution),
            "yPixels": float((1.0 - normalized.y) * resolution),
        }

    part_for_object = {}
    for obj in descendants(root):
        current = obj
        part = None
        while current is not None and current != root:
            if "wot_part" in current:
                part = current["wot_part"]
                break
            current = current.parent
        part_for_object[obj] = part

    render_root.mkdir(parents=True, exist_ok=True)
    layers = {
        "hull": {"Chassis", "Hull"},
        "turret": {"Turret", "Gun"},
    }
    counts = {}
    for layer_name, visible_parts in layers.items():
        visible_count = 0
        for obj in descendants(root):
            visible = part_for_object.get(obj) in visible_parts
            obj.hide_render = not visible
            if visible and obj.type == "MESH":
                visible_count += 1
        if visible_count == 0:
            raise RuntimeError(f"No meshes were assigned to render layer: {layer_name}")
        output = render_root / f"{layer_name}.png"
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        counts[layer_name] = visible_count
    metadata = {
        "resolution": resolution,
        "intermediatePixelsPerMeter": pixels_per_meter,
        "orthographicScale": camera_data.ortho_scale,
        "worldBounds": {"min": list(minimum), "max": list(maximum)},
        "meshCounts": counts,
        "turretPivot": turret_pivot,
    }
    write_json(render_root / "render_metadata.json", metadata)
    return metadata


def main() -> None:
    args = arguments()
    config_path = Path(args.config).resolve()
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    pipeline_root = config_path.parent
    local_root = resolve(pipeline_root, config["localRoot"])
    output_root = resolve(pipeline_root, config["outputRoot"])
    addon_path = resolve(pipeline_root, config["addonPath"])
    module = enable_addon(pipeline_root, config)
    catalog = flatten_catalog(module)

    base_report = {
        "schemaVersion": 1,
        "blenderVersion": bpy.app.version_string,
        "addonRevision": git_revision(addon_path),
        "wotRoot": config["wotRoot"],
        "vehicleCount": len(catalog),
        "packageCount": len(list((Path(config["wotRoot"]) / "res" / "packages").glob("*.pkg"))),
        "catalogSamples": [item for item in catalog if "Tiger" in item["id"]][:10],
    }

    if args.mode == "probe":
        report_path = local_root / "Reports" / "environment.json"
        write_json(report_path, base_report)
        print(f"WOT_PIPELINE_PROBE_OK {report_path}")
        return

    vehicle = find_vehicle(catalog, args)
    selected = select_vehicle(module, vehicle, args)
    root = bpy.data.objects.get(vehicle["id"])
    if root is None:
        raise RuntimeError(f"Imported vehicle root was not found: {vehicle['id']}")
    render_root = local_root / "Renders" / vehicle["id"]
    render_info = render_layers(root, render_root, config)
    if bool(config["render"].get("saveBlend", True)):
        bpy.ops.wm.save_as_mainfile(filepath=str(render_root / f"{vehicle['id']}.blend"))
    manifest = {
        **base_report,
        "vehicle": vehicle,
        "selectedModules": selected,
        "render": render_info,
        "materials": material_report(root),
        "intermediateDirectory": str(render_root),
        "outputDirectory": str(output_root / vehicle["id"]),
    }
    write_json(output_root / vehicle["id"] / f"{vehicle['id']}_manifest.json", manifest)
    print(f"WOT_PIPELINE_RENDER_OK {vehicle['id']}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"WOT_PIPELINE_ERROR {error}", file=sys.stderr)
        raise
