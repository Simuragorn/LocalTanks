from __future__ import annotations

import argparse
import json
import math
import sys
import traceback
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector


def arguments() -> argparse.Namespace:
    raw = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--source-package", required=True)
    parser.add_argument("--maximum-dimension", type=int, default=4096)
    parser.add_argument("--exclude-materials", default="")
    return parser.parse_args(raw)


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    if not points:
        raise RuntimeError("The imported environment object contains no renderable meshes")
    return (
        Vector((min(point.x for point in points), min(point.y for point in points), min(point.z for point in points))),
        Vector((max(point.x for point in points), max(point.y for point in points), max(point.z for point in points))),
    )


def add_matching_light(center: Vector, span: float) -> None:
    world = bpy.data.worlds.new("SpriteWorld")
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.18, 0.18, 0.18, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8

    scale = max(span / 8.0, 1.0)
    for name, energy, offset, size in (
        ("Key", 1700.0, Vector((6.0, -7.0, 12.0)) * scale, 8.0 * scale),
        ("Fill", 900.0, Vector((-7.0, 4.0, 8.0)) * scale, 7.0 * scale),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light_data.use_shadow = False
        light = bpy.data.objects.new(name, light_data)
        bpy.context.collection.objects.link(light)
        light.location = center + offset
        light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()


def main() -> None:
    args = arguments()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    model_path = Path(args.model).resolve()
    output_path = Path(args.output).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.preferences.addon_enable(module="wot_blender_toolkit")
    bpy.context.preferences.addons["wot_blender_toolkit"].preferences.wot_game_path = config["wotRoot"]
    from wot_blender_toolkit.file_finder import WoTFileFinder
    from wot_blender_toolkit.import_bw_primitives_textured import load_bw_primitive_textured

    clear_scene()
    result = load_bw_primitive_textured(
        bpy.context.scene.collection,
        model_path,
        import_empty=True,
        finder=WoTFileFinder(),
        context={"last_pkg": args.source_package},
    )
    if "FINISHED" not in result:
        details = bpy.data.texts.get("WoT Import Log")
        raise RuntimeError(details.as_string() if details else f"Model import failed: {result}")

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    excluded_materials = {name for name in args.exclude_materials.split(",") if name}
    for obj in meshes:
        excluded_indices = {
            index
            for index, material in enumerate(obj.data.materials)
            if material is not None and material.name in excluded_materials
        }
        if not excluded_indices:
            continue
        editable = bmesh.new()
        editable.from_mesh(obj.data)
        rejected_faces = [face for face in editable.faces if face.material_index in excluded_indices]
        bmesh.ops.delete(editable, geom=rejected_faces, context="FACES")
        editable.to_mesh(obj.data)
        editable.free()
        obj.data.update()
    minimum, maximum = bounds(meshes)
    center = (minimum + maximum) * 0.5
    x_span = maximum.x - minimum.x
    y_span = maximum.y - minimum.y
    span = max(x_span, y_span)
    pixels_per_meter = float(config["render"].get("outputPixelsPerMeter", 108))
    padding = int(config["render"].get("paddingPixels", 24)) * 2
    resolution_x = int(math.ceil(max(256, x_span * pixels_per_meter + padding) / 8.0) * 8)
    resolution_y = int(math.ceil(max(256, y_span * pixels_per_meter + padding) / 8.0) * 8)
    if max(resolution_x, resolution_y) > args.maximum_dimension:
        raise RuntimeError(
            f"Sprite would be {resolution_x}x{resolution_y}, exceeding the curated-library limit "
            f"of {args.maximum_dimension}px. The source is probably an outland/background object."
        )

    materials = []
    for obj in meshes:
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            source_fx = slot.material.get("bw_custom_fx", "")
            if "pbs_tiled_atlas_global" in source_fx.lower():
                raise RuntimeError(f"Unsupported terrain-atlas shader in {model_path.name}: {source_fx}")
            materials.append({"object": obj.name, "material": slot.material.name, "sourceFx": source_fx})

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = resolution_x
    scene.render.resolution_y = resolution_y
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    scene.view_settings.look = "AgX - Medium High Contrast"
    add_matching_light(center, span)

    camera_data = bpy.data.cameras.new("SpriteCamera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = resolution_y / pixels_per_meter
    camera = bpy.data.objects.new("SpriteCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.location = (center.x, center.y, maximum.z + max(camera_data.ortho_scale, 10.0))
    camera.rotation_euler = (0.0, 0.0, math.pi)
    scene.camera = camera
    bpy.context.view_layer.update()

    scene.render.filepath = str(output_path)
    bpy.ops.render.render(write_still=True)
    output_path.with_suffix(".json").write_text(
        json.dumps(
            {
                "sourcePackage": args.source_package,
                "sourceModel": str(model_path),
                "camera": "strict-top-down-orthographic",
                "transparentBackground": True,
                "pixelsPerMeter": pixels_per_meter,
                "resolution": [resolution_x, resolution_y],
                "worldBounds": {"min": list(minimum), "max": list(maximum)},
                "worldSizeMeters": [x_span, y_span],
                "materials": materials,
            },
            indent=2,
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
