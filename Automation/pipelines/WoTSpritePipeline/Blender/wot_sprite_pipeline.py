"""Headless WoT package importer and orthographic sprite renderer for Blender 4.3."""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import subprocess
import sys
from array import array
from pathlib import Path

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

# WoT chassis meshes and materials name their track sides explicitly, for example
# `track_L_Shape` with `track_mat_L_skinned`. The trailing lookahead keeps names
# such as `tracklink` from being read as a left track.
TRACK_SIDE_PATTERN = re.compile(
    r"track(?:[\s_\-.]*mat)?[\s_\-.]*([lr])(?=[\s_\-.]|shape|split|skinned|\d|$)"
)


def arguments() -> argparse.Namespace:
    raw = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("probe", "search", "render"), required=True)
    parser.add_argument("--config", required=True)
    parser.add_argument("--query", default="")
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


def search_catalog(entries: list[dict], query: str) -> list[dict]:
    normalized_query = "".join(character.lower() if character.isalnum() else " " for character in query)
    tokens = [token for token in normalized_query.split() if token]
    if not tokens:
        raise RuntimeError("Search mode requires a non-empty --query")

    ranked = []
    for item in entries:
        searchable = " ".join((item["id"], item["displayName"], item["nation"], item["type"]))
        normalized = "".join(character.lower() if character.isalnum() else " " for character in searchable)
        if not all(token in normalized for token in tokens):
            continue
        compact_query = "".join(tokens)
        compact_id = "".join(character.lower() for character in item["id"] if character.isalnum())
        compact_name = "".join(character.lower() for character in item["displayName"] if character.isalnum())
        score = 0
        if compact_query == compact_name:
            score += 100
        if compact_query == compact_id:
            score += 90
        if compact_id.endswith(compact_query):
            score += 30
        if not item["locked"]:
            score += 10
        ranked.append((score, item))
    ranked.sort(key=lambda pair: (-pair[0], pair[1]["nation"], pair[1]["tier"], pair[1]["id"]))
    return [item for _, item in ranked[:50]]


def clear_factory_scene() -> None:
    """Remove Blender's startup cube, camera and light before vehicle import."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


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


def part_map(root) -> dict:
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
    return part_for_object


def track_side(name: str):
    """Return 'left', 'right' or None for one WoT object or material name."""
    match = TRACK_SIDE_PATTERN.search(name.casefold())
    if match is None:
        return None
    return "left" if match.group(1) == "l" else "right"


def classify_track_meshes(root, part_for_object: dict) -> tuple[list, list, list]:
    """Split chassis meshes into track sides by object name, then by material name."""
    left: list = []
    right: list = []
    ambiguous: list = []
    for obj in mesh_objects(root):
        if part_for_object.get(obj) not in {"Chassis", "Hull"}:
            continue
        side = track_side(obj.name)
        if side is None:
            for slot in obj.material_slots:
                material = slot.material
                if material is not None:
                    side = track_side(material.name)
                    if side is not None:
                        break
        if side == "left":
            left.append(obj)
        elif side == "right":
            right.append(obj)
        elif "track" in obj.name.casefold():
            ambiguous.append(obj.name)
    return left, right, ambiguous


def projected_direction(scene, camera, resolution: int, origin: Vector, direction: Vector) -> dict:
    """Project a world direction into image pixels with y measured downward from the top."""
    step = 0.5
    start = world_to_camera_view(scene, camera, origin)
    end = world_to_camera_view(scene, camera, origin + direction.normalized() * step)
    return {
        "xPixels": float((end.x - start.x) * resolution),
        "yPixels": float(-(end.y - start.y) * resolution),
    }


def measure_vehicle_axes(root, part_for_object: dict, scene, camera, resolution: int, turret_root) -> dict:
    """
    Derive the image-space forward and starboard directions from the model itself.

    Model axis conventions differ between imported vehicles, so neither direction is assumed:
    the gun points forward, and the starboard side follows from forward and the scene up axis.
    The result is projected through the camera, which is what decides the rendered sides.
    """
    if turret_root is None:
        return {"sideSource": "unavailable", "reason": "the imported vehicle has no turret part"}

    joint = turret_root.matrix_world.translation
    gun_objects = [obj for obj in mesh_objects(root) if part_for_object.get(obj) == "Gun"]
    if not gun_objects:
        return {"sideSource": "unavailable", "reason": "the imported vehicle has no gun part"}

    farthest = None
    farthest_distance = -1.0
    for obj in gun_objects:
        matrix = obj.matrix_world
        for vertex in obj.data.vertices:
            world = matrix @ vertex.co
            distance = math.hypot(world.x - joint.x, world.y - joint.y)
            if distance > farthest_distance:
                farthest_distance = distance
                farthest = world
    if farthest is None or farthest_distance <= 0.0:
        return {"sideSource": "unavailable", "reason": "the gun mesh has no usable vertices"}

    forward = Vector((farthest.x - joint.x, farthest.y - joint.y, 0.0)).normalized()
    # Right-handed world with Z up: the tank's starboard side is forward cross up.
    starboard = Vector((forward.y, -forward.x, 0.0))
    forward_image = projected_direction(scene, camera, resolution, joint, forward)
    starboard_image = projected_direction(scene, camera, resolution, joint, starboard)
    lateral = abs(starboard_image["xPixels"]) / max(
        math.hypot(starboard_image["xPixels"], starboard_image["yPixels"]), 1e-6
    )
    if lateral < 0.9:
        return {
            "sideSource": "unavailable",
            "reason": "the projected starboard direction is not lateral in the top view",
            "forwardImage": forward_image,
            "starboardImage": starboard_image,
        }
    return {
        "sideSource": "gunDirection",
        "turretJoint": [float(joint.x), float(joint.y), float(joint.z)],
        "gunMuzzle": [float(farthest.x), float(farthest.y), float(farthest.z)],
        "forwardImage": forward_image,
        "starboardImage": starboard_image,
    }


def projected_pixel_bounds(objects: list, scene, camera, resolution: int) -> tuple[int, int, int, int]:
    """Pixel rectangle (left, top, right, bottom) covering the projected objects, clamped."""
    minimum_x = float(resolution)
    minimum_y = float(resolution)
    maximum_x = -1.0
    maximum_y = -1.0
    for obj in objects:
        matrix = obj.matrix_world
        for corner in obj.bound_box:
            normalized = world_to_camera_view(scene, camera, matrix @ Vector(corner))
            x = normalized.x * resolution
            y = (1.0 - normalized.y) * resolution
            minimum_x = min(minimum_x, math.floor(x) - 1.0)
            maximum_x = max(maximum_x, math.ceil(x) + 1.0)
            minimum_y = min(minimum_y, math.floor(y) - 1.0)
            maximum_y = max(maximum_y, math.ceil(y) + 1.0)
    return (
        max(0, int(minimum_x)),
        max(0, int(minimum_y)),
        min(resolution - 1, int(maximum_x)),
        min(resolution - 1, int(maximum_y)),
    )


def build_track_mask(
    left: list,
    right: list,
    scene,
    camera,
    resolution: int,
    orthographic_scale: float,
    center: Vector,
    ceiling: float,
    output: Path,
) -> dict:
    """
    Mark every pixel whose first surface hit from above is a track mesh.

    A mudguard or a side skirt hides part of the track belt in a straight top-down view, and a
    tread highlight must not be drawn on top of it. A renderer holdout cannot answer that question:
    EEVEE's holdout is a screen-space stencil that also cuts geometry merely lying *below* the
    track, which erased almost every tracked vehicle completely. One ray per pixel from the camera
    gives the exact first hit instead, and the later bicubic crop to the sprite scale supplies the
    anti-aliased edge.
    """
    sides = {obj.name: "left" for obj in left}
    sides.update({obj.name: "right" for obj in right})
    bounds_left, bounds_top, bounds_right, bounds_bottom = projected_pixel_bounds(
        left + right, scene, camera, resolution
    )
    metres_per_pixel = orthographic_scale / resolution
    half = resolution * 0.5
    depsgraph = bpy.context.evaluated_depsgraph_get()
    pixels = array("f", bytes(4 * 4 * resolution * resolution))
    samples = 0
    hits = 0
    for y in range(bounds_top, bounds_bottom + 1):
        world_y = center.y + (y + 0.5 - half) * metres_per_pixel
        for x in range(bounds_left, bounds_right + 1):
            world_x = center.x - (x + 0.5 - half) * metres_per_pixel
            samples += 1
            result, _location, _normal, _index, hit, _matrix = scene.ray_cast(
                depsgraph, Vector((world_x, world_y, ceiling)), Vector((0.0, 0.0, -1.0))
            )
            if not result or hit is None:
                continue
            side = sides.get(hit.name)
            if side is None:
                continue
            hits += 1
            offset = ((resolution - 1 - y) * resolution + x) * 4
            if side == "left":
                pixels[offset] = 1.0
            else:
                pixels[offset + 1] = 1.0
            pixels[offset + 3] = 1.0

    image = bpy.data.images.new("LocalTanksTrackMask", width=resolution, height=resolution, alpha=True)
    image.pixels.foreach_set(pixels)
    image.filepath_raw = str(output)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    return {
        "image": output.name,
        "raySamples": samples,
        "rayHits": hits,
        "pixelBounds": [bounds_left, bounds_top, bounds_right, bounds_bottom],
    }


def render_track_mask(
    root, scene, camera, resolution: int, render_root: Path, part_for_object: dict, center: Vector, ceiling: float
) -> dict:
    """
    Identify the tracked meshes and mark the track surface that a top-down view can see.

    The mask shares the camera and resolution of the hull layer, so composing it with the same
    alpha bounds aligns it with the hull sprite pixel for pixel.
    """
    left, right, ambiguous = classify_track_meshes(root, part_for_object)
    info = {
        "detected": bool(left) and bool(right),
        "visibilityMethod": "firstSurfaceRaycast",
        "occlusionAware": True,
        "leftObjects": [obj.name for obj in left],
        "rightObjects": [obj.name for obj in right],
        "ambiguousObjects": ambiguous,
        "image": None,
    }
    if not info["detected"]:
        info["reason"] = (
            "no left and right track meshes were identified by object or material name; "
            "the exact-visible-track contract rejects this render until recognition is extended"
        )
        return info

    info.update(
        build_track_mask(
            left,
            right,
            scene,
            camera,
            resolution,
            camera.data.ortho_scale,
            center,
            ceiling,
            render_root / "tracks.png",
        )
    )
    return info


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
    # world_to_camera_view reads the evaluated camera matrix. Newly assigned
    # transforms are not guaranteed to be visible there until the view layer
    # has been updated explicitly.
    bpy.context.view_layer.update()

    parts = part_roots(root)
    turret_root = parts.get("Turret")
    turret_pivot = None
    if turret_root is not None:
        normalized = world_to_camera_view(scene, camera, turret_root.matrix_world.translation)
        turret_pivot = {
            "xPixels": float(normalized.x * resolution),
            "yPixels": float((1.0 - normalized.y) * resolution),
        }

    part_for_object = part_map(root)

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

    axes = measure_vehicle_axes(root, part_for_object, scene, camera, resolution, turret_root)
    track_info = {"detected": False, "image": None, "reason": "track mask rendering is disabled"}
    if bool(config["render"].get("trackMask", True)):
        track_info = render_track_mask(
            root, scene, camera, resolution, render_root, part_for_object, center, float(maximum.z) + 1.0
        )
    track_info.update(axes)

    metadata = {
        "resolution": resolution,
        "intermediatePixelsPerMeter": pixels_per_meter,
        "orthographicScale": camera_data.ortho_scale,
        "worldBounds": {"min": list(minimum), "max": list(maximum)},
        "meshCounts": counts,
        "turretPivot": turret_pivot,
        "trackMask": track_info,
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

    if args.mode == "search":
        matches = search_catalog(catalog, args.query)
        report_path = local_root / "Reports" / "search.json"
        write_json(report_path, {"query": args.query, "matchCount": len(matches), "matches": matches})
        print(json.dumps(matches, indent=2, ensure_ascii=False))
        print(f"WOT_PIPELINE_SEARCH_OK {report_path}")
        return

    vehicle = find_vehicle(catalog, args)
    clear_factory_scene()
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
