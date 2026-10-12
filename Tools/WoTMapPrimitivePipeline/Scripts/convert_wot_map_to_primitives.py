#!/usr/bin/env python3
"""Convert a locally installed WoT space into an AI-readable primitive map.

The output deliberately contains only semantic geometry and numeric metadata.
Original models, textures, packed XML and package bytes never leave the local
machine and are never copied to the generated directory.
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import io
import json
import math
import re
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile
import zlib
from pathlib import Path


if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


REQUIRED_SECTIONS = ["BWST", "BWT2", "BSMO", "BSMI", "SpTr", "BWWa"]


def fail(message: str) -> None:
    raise RuntimeError(message)


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def round_number(value: float, digits: int = 3) -> float:
    return round(float(value), digits)


def read_client_version(wot_root: Path) -> tuple[str, str]:
    root = ET.parse(wot_root / "version.xml").getroot()
    version = (root.findtext("version") or "").strip()
    realm = (root.findtext("meta/realm") or "EU").strip()
    if not version:
        fail(f"Не удалось прочитать версию клиента из {wot_root / 'version.xml'}")
    return version, realm


def unpack_arena_definition(data: bytes, parser_root: Path) -> ET.Element:
    sys.path.insert(0, str(parser_root))
    from compiled_space.xml_utils.XmlUnpacker import XmlUnpacker

    return XmlUnpacker().read(io.BytesIO(data))


def parse_vector(text: str | None) -> list[float] | None:
    if not text:
        return None
    return [float(item) for item in text.split()]


def parse_arena(root: ET.Element, preferred_gameplay_type: str) -> dict:
    bottom_left = parse_vector(root.findtext("boundingBox/bottomLeft"))
    upper_right = parse_vector(root.findtext("boundingBox/upperRight"))
    if not bottom_left or not upper_right:
        fail("В arena definition отсутствует boundingBox")

    gameplay = root.find(f"gameplayTypes/{preferred_gameplay_type}")
    if gameplay is None:
        fail(f"В arena definition нет режима {preferred_gameplay_type}")

    def first_position(path: str) -> list[float] | None:
        node = gameplay.find(path)
        if node is None:
            return None
        for child in list(node):
            value = parse_vector(child.text)
            if value:
                return value
        return parse_vector(node.text)

    bases = []
    for team in (1, 2):
        position = first_position(f"teamBasePositions/team{team}")
        if position:
            bases.append({"team": team, "positionMeters": [position[0], position[-1]]})

    spawns = []
    for team in (1, 2):
        position = first_position(f"teamSpawnPoints/team{team}")
        if position:
            spawns.append({"team": team, "positionMeters": [position[0], position[-1]]})
    if not spawns:
        spawns = [dict(item) for item in bases]

    return {
        "playableBoundsMeters": {
            "min": [bottom_left[0], bottom_left[1]],
            "max": [upper_right[0], upper_right[1]],
        },
        "bases": bases,
        "spawns": spawns,
    }


def decode_png_rgba8(data: bytes) -> tuple[int, int, bytes]:
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        fail("Неверная PNG-сигнатура heightmap")
    position = 8
    width = height = 0
    compressed = bytearray()
    while position + 12 <= len(data):
        length = struct.unpack_from(">I", data, position)[0]
        kind = data[position + 4 : position + 8]
        payload = data[position + 8 : position + 8 + length]
        position += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(
                ">IIBBBBB", payload
            )
            if (bit_depth, color_type, compression, filtering, interlace) != (8, 6, 0, 0, 0):
                fail(f"Неподдерживаемый формат heightmap PNG: depth={bit_depth}, color={color_type}")
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            break

    raw = zlib.decompress(bytes(compressed))
    stride = width * 4
    output = bytearray(height * stride)
    source = 0
    for y in range(height):
        filter_type = raw[source]
        source += 1
        row = bytearray(raw[source : source + stride])
        source += stride
        previous = output[(y - 1) * stride : y * stride] if y else bytes(stride)
        for index in range(stride):
            left = row[index - 4] if index >= 4 else 0
            up = previous[index]
            upper_left = previous[index - 4] if index >= 4 else 0
            if filter_type == 1:
                row[index] = (row[index] + left) & 255
            elif filter_type == 2:
                row[index] = (row[index] + up) & 255
            elif filter_type == 3:
                row[index] = (row[index] + ((left + up) // 2)) & 255
            elif filter_type == 4:
                estimate = left + up - upper_left
                pa, pb, pc = abs(estimate - left), abs(estimate - up), abs(estimate - upper_left)
                predictor = left if pa <= pb and pa <= pc else up if pb <= pc else upper_left
                row[index] = (row[index] + predictor) & 255
            elif filter_type != 0:
                fail(f"Неподдерживаемый PNG filter {filter_type}")
        output[y * stride : (y + 1) * stride] = row
    return width, height, bytes(output)


def decode_heightmap(raw: bytes) -> dict:
    if raw[:4] != b"hmp\0":
        fail("Неверная сигнатура terrain2/heights")
    width, height = struct.unpack_from("<II", raw, 4)
    png_start = raw.find(b"\x89PNG")
    if png_start < 0:
        fail("PNG не найден внутри terrain2/heights")
    png_width, png_height, rgba = decode_png_rgba8(raw[png_start:])
    if (width, height) != (png_width, png_height):
        fail("Размер heightmap header не совпадает с PNG")
    heights = []
    for offset in range(0, len(rgba), 4):
        quantized = struct.unpack_from("<i", rgba, offset)[0]
        heights.append(quantized * 0.001)
    return {"width": width, "height": height, "heights": heights}


def load_heightmaps(package: zipfile.ZipFile, bwt2: object, bwst: object, space_prefix: str) -> dict:
    result = {}
    for chunk in bwt2._data["cdatas"]:
        relative = bwst.get(chunk["resource_fnv"])
        if not relative:
            continue
        cdata_name = relative.split("/terrain2", 1)[0]
        path = f"{space_prefix}/{cdata_name}"
        try:
            cdata = package.read(path)
        except KeyError:
            continue
        with zipfile.ZipFile(io.BytesIO(cdata)) as nested:
            try:
                raw = nested.read("terrain2/heights")
            except KeyError:
                continue
        result[(chunk["loc_x"], chunk["loc_y"])] = decode_heightmap(raw)
    return result


def sample_height(heightmaps: dict, chunk_size: float, x: float, z: float) -> float | None:
    chunk_x = math.floor(x / chunk_size)
    chunk_z = math.floor(z / chunk_size)
    heightmap = heightmaps.get((chunk_x, chunk_z))
    if not heightmap:
        return None
    u = (x - chunk_x * chunk_size) / chunk_size
    v = (z - chunk_z * chunk_size) / chunk_size
    width, height = heightmap["width"], heightmap["height"]
    fx = 2.0 + max(0.0, min(1.0, u)) * (width - 5)
    fy = 2.0 + max(0.0, min(1.0, v)) * (height - 5)
    x0, y0 = min(int(fx), width - 2), min(int(fy), height - 2)
    dx, dy = fx - x0, fy - y0
    values = heightmap["heights"]
    h00 = values[y0 * width + x0]
    h10 = values[y0 * width + x0 + 1]
    h01 = values[(y0 + 1) * width + x0]
    h11 = values[(y0 + 1) * width + x0 + 1]
    return (h00 * (1 - dx) + h10 * dx) * (1 - dy) + (h01 * (1 - dx) + h11 * dx) * dy


def build_terrain_grid(heightmaps: dict, chunk_size: float, bounds: dict, step: float) -> dict:
    min_x, min_z = bounds["min"]
    max_x, max_z = bounds["max"]
    columns = int(round((max_x - min_x) / step)) + 1
    rows = int(round((max_z - min_z) / step)) + 1
    heights = []
    flat_values = []
    for row in range(rows):
        z = min_z + row * step
        values = []
        for column in range(columns):
            x = min_x + column * step
            value = sample_height(heightmaps, chunk_size, x, z)
            rounded = round_number(value or 0.0, 1)
            values.append(rounded)
            flat_values.append(rounded)
        heights.append(values)
    return {
        "originMeters": [min_x, min_z],
        "stepMeters": step,
        "columns": columns,
        "rows": rows,
        "minimumMeters": min(flat_values),
        "maximumMeters": max(flat_values),
        "heightsMeters": heights,
    }


def read_dds_bgra8(data: bytes) -> tuple[int, int, bytes]:
    if data[:4] != b"DDS ":
        fail("Неверная DDS-сигнатура water depth map")
    height, width = struct.unpack_from("<II", data, 12)
    pixels = data[128:]
    if len(pixels) < width * height * 4:
        fail("Water depth DDS короче ожидаемого BGRA8")
    return width, height, pixels[: width * height * 4]


def load_water_mask(package: zipfile.ZipFile, map_id: str, bwwa: object, bounds: dict, step: float) -> dict:
    depth_paths = sorted(
        name
        for name in package.namelist()
        if name.startswith(f"spaces/{map_id}/water/") and name.endswith("/depth.dds")
    )
    if not depth_paths or not bwwa._data.get("1"):
        return {"originMeters": bounds["min"], "stepMeters": step, "cells": []}

    raw_volume = struct.pack("<85I", *bwwa._data["1"][0])
    min_x, _min_y, min_z, max_x, _max_y, max_z = struct.unpack_from("<6f", raw_volume, 0)
    width, height, pixels = read_dds_bgra8(package.read(depth_paths[0]))
    play_min_x, play_min_z = bounds["min"]
    play_max_x, play_max_z = bounds["max"]
    columns = int(math.ceil((play_max_x - play_min_x) / step))
    rows = int(math.ceil((play_max_z - play_min_z) / step))
    cells = []
    for row in range(rows):
        z = play_min_z + (row + 0.5) * step
        v = (z - min_z) / (max_z - min_z)
        py = min(height - 1, max(0, int(v * height)))
        for column in range(columns):
            x = play_min_x + (column + 0.5) * step
            u = (x - min_x) / (max_x - min_x)
            if not 0 <= u < 1 or not 0 <= v < 1:
                continue
            px = min(width - 1, max(0, int(u * width)))
            red = pixels[(py * width + px) * 4 + 2]
            if red > 0:
                cells.append([column, row, red])
    return {
        "originMeters": [play_min_x, play_min_z],
        "stepMeters": step,
        "columns": columns,
        "rows": rows,
        "cells": cells,
        "gameplay": {
            "blocksMovement": True,
            "blocksProjectiles": False,
            "blocksVision": False,
            "notes": "В LocalTanks вода должна быть отдельной зоной проходимости, а не стеной для снарядов.",
        },
    }


def transform_point(matrix: list[float] | tuple[float, ...], point: tuple[float, float, float]) -> tuple[float, float, float]:
    x, y, z = point
    return (
        x * matrix[0] + y * matrix[4] + z * matrix[8] + matrix[12],
        x * matrix[1] + y * matrix[5] + z * matrix[9] + matrix[13],
        x * matrix[2] + y * matrix[6] + z * matrix[10] + matrix[14],
    )


def transformed_bounds(matrix: list[float] | tuple[float, ...], minimum: list[float], maximum: list[float]) -> tuple[float, float, float, float]:
    points = [
        transform_point(matrix, (x, y, z))
        for x in (minimum[0], maximum[0])
        for y in (minimum[1], maximum[1])
        for z in (minimum[2], maximum[2])
    ]
    return (
        min(point[0] for point in points),
        min(point[2] for point in points),
        max(point[0] for point in points),
        max(point[2] for point in points),
    )


def inside_bounds(x: float, z: float, bounds: dict, margin: float = 20.0) -> bool:
    return (
        bounds["min"][0] - margin <= x <= bounds["max"][0] + margin
        and bounds["min"][1] - margin <= z <= bounds["max"][1] + margin
    )


def match_rule(path: str, rules: list[dict]) -> dict | None:
    lowered = path.lower()
    for rule in rules:
        if any(token.lower() in lowered for token in rule["matchAny"]):
            return rule
    return None


def build_static_primitives(sections: dict, bounds: dict, rules: list[dict], minimum_footprint: float) -> tuple[list[dict], dict]:
    bwst, bsmi, bsmo = sections["BWST"], sections["BSMI"], sections["BSMO"]
    model_ids = list(bsmi.model_ids())
    primitives = []
    ignored = collections.Counter()
    unresolved = 0
    for index, (model_id, matrix) in enumerate(zip(model_ids, bsmi._data["transforms"])):
        collider = bsmo._data["models_colliders"][model_id]
        resource = bwst.get(collider["bsp_section_name_fnv"])
        if not resource:
            unresolved += 1
            continue
        rule = match_rule(resource, rules)
        if not rule or rule["action"] == "ignore":
            ignored[rule["id"] if rule else "unclassified"] += 1
            continue
        min_x, min_z, max_x, max_z = transformed_bounds(
            matrix, collider["collision_bounds_min"], collider["collision_bounds_max"]
        )
        center_x, center_z = (min_x + max_x) / 2, (min_z + max_z) / 2
        if not inside_bounds(center_x, center_z, bounds):
            ignored["outside_playable_bounds"] += 1
            continue
        width, depth = max_x - min_x, max_z - min_z
        if max(width, depth) < minimum_footprint:
            ignored["below_minimum_footprint"] += 1
            continue
        info = bsmo._data["model_info_items"][model_id]
        destructible = bool(info["type"] != 0 or rule.get("forceDestructible", False))
        if rule.get("forceIndestructible", False):
            destructible = False
        category = rule["category"]
        blocks_movement = category != "bridge"
        primitive = {
            "id": f"static-{index:05d}",
            "category": category,
            "shape": rule["shape"],
            "centerMeters": [round_number(center_x), round_number(center_z)],
            "sizeMeters": [round_number(max(width, 0.5)), round_number(max(depth, 0.5))],
            "rotationDegrees": round_number(math.degrees(math.atan2(matrix[2], matrix[0])), 1),
            "destructible": destructible,
            "blocksMovement": blocks_movement,
            "blocksProjectiles": blocks_movement,
            "blocksVision": blocks_movement,
            "sourceResource": resource,
        }
        primitives.append(primitive)
    return primitives, {"ignored": dict(ignored), "unresolvedResources": unresolved}


def build_vegetation(sections: dict, bounds: dict, config: dict, cluster_cell: float) -> tuple[list[list], list[dict]]:
    bwst, speedtrees = sections["BWST"], sections["SpTr"]._data["1"]
    tokens = [token.lower() for token in config["bushTokens"]]
    placements = []
    cluster_data: dict[tuple[int, int], dict] = {}
    origin_x, origin_z = bounds["min"]
    for item in speedtrees:
        matrix = item["transform"]
        x, z = matrix[12], matrix[14]
        if not inside_bounds(x, z, bounds, margin=0):
            continue
        resource = (bwst.get(item["spt_fnv"]) or "unknown").replace("\\", "/")
        lowered = resource.lower()
        is_bush = any(token in lowered for token in tokens)
        kind = "bush" if is_bush else "tree"
        scale_x = math.sqrt(matrix[0] ** 2 + matrix[1] ** 2 + matrix[2] ** 2)
        radius = config["bushRadiusMeters"] if is_bush else config["treeRadiusMeters"]
        radius *= max(0.5, min(2.0, scale_x))
        placements.append([round_number(x), round_number(z), round_number(radius, 2), kind, resource])
        cell = (int((x - origin_x) // cluster_cell), int((z - origin_z) // cluster_cell))
        cluster = cluster_data.setdefault(cell, {"tree": 0, "bush": 0, "xs": [], "zs": []})
        cluster[kind] += 1
        cluster["xs"].append(x)
        cluster["zs"].append(z)

    clusters = []
    for (column, row), data in sorted(cluster_data.items(), key=lambda item: (item[0][1], item[0][0])):
        count = data["tree"] + data["bush"]
        clusters.append(
            {
                "cell": [column, row],
                "centerMeters": [round_number(sum(data["xs"]) / count), round_number(sum(data["zs"]) / count)],
                "sizeMeters": [cluster_cell, cluster_cell],
                "treeCount": data["tree"],
                "bushCount": data["bush"],
                "dominant": "bush" if data["bush"] >= data["tree"] else "tree",
                "density": round_number(min(1.0, count / 12.0), 2),
            }
        )
    return placements, clusters


def build_ai_analysis_grid(
    bounds: dict,
    cell_size: float,
    terrain: dict,
    water: dict,
    static_primitives: list[dict],
    vegetation: list[list],
) -> dict:
    """Compress exact placement data into a small spatial vocabulary for an LLM."""
    min_x, min_z = bounds["min"]
    max_x, max_z = bounds["max"]
    columns = int(math.ceil((max_x - min_x) / cell_size))
    rows = int(math.ceil((max_z - min_z) / cell_size))
    cells = [
        {
            "cell": [column, row],
            "centerMeters": [
                round_number(min_x + (column + 0.5) * cell_size),
                round_number(min_z + (row + 0.5) * cell_size),
            ],
            "counts": collections.Counter(),
            "waterSamples": 0,
        }
        for row in range(rows)
        for column in range(columns)
    ]

    def find_cell(x: float, z: float) -> dict | None:
        column = int((x - min_x) // cell_size)
        row = int((z - min_z) // cell_size)
        if not 0 <= column < columns or not 0 <= row < rows:
            return None
        return cells[row * columns + column]

    for item in static_primitives:
        cell = find_cell(*item["centerMeters"])
        if cell:
            cell["counts"][item["category"]] += 1
            if item["destructible"]:
                cell["counts"]["destructible"] += 1
    for x, z, _radius, kind, _resource in vegetation:
        cell = find_cell(x, z)
        if cell:
            cell["counts"][kind] += 1
    for column, row, _depth in water["cells"]:
        x = water["originMeters"][0] + (column + 0.5) * water["stepMeters"]
        z = water["originMeters"][1] + (row + 0.5) * water["stepMeters"]
        cell = find_cell(x, z)
        if cell:
            cell["waterSamples"] += 1

    water_samples_per_cell = max(1, int(round(cell_size / water["stepMeters"])) ** 2)
    terrain_values = [value for row in terrain["heightsMeters"] for value in row]
    high_ground_threshold = sorted(terrain_values)[int(len(terrain_values) * 0.75)]
    analysis_cells = []
    for cell in cells:
        center_x, center_z = cell["centerMeters"]
        terrain_column = min(
            terrain["columns"] - 1,
            max(0, int(round((center_x - terrain["originMeters"][0]) / terrain["stepMeters"]))),
        )
        terrain_row = min(
            terrain["rows"] - 1,
            max(0, int(round((center_z - terrain["originMeters"][1]) / terrain["stepMeters"]))),
        )
        elevation = terrain["heightsMeters"][terrain_row][terrain_column]
        counts = cell["counts"]
        water_coverage = min(1.0, cell["waterSamples"] / water_samples_per_cell)
        hard_cover = counts["house"] * 2 + counts["rock"] * 1.5 + counts["obstacle"] * 0.5
        concealment = counts["bush"] + counts["tree"] * 0.35
        tags = []
        if water_coverage >= 0.4:
            tags.append("water")
        elif water_coverage > 0:
            tags.append("shore")
        if elevation >= high_ground_threshold:
            tags.append("high_ground")
        if counts["house"] >= 3:
            tags.append("settlement")
        if counts["rock"] >= 3:
            tags.append("rocky_cover")
        if hard_cover >= 8:
            tags.append("hard_cover_dense")
        if concealment >= 8:
            tags.append("concealment_dense")
        if counts["bush"] >= 5:
            tags.append("bush_line")
        if counts["tree"] >= 5:
            tags.append("woodland")
        if water_coverage >= 0.4:
            passability = "blocked_by_water"
        elif water_coverage > 0 or hard_cover >= 20:
            passability = "restricted"
        else:
            passability = "open"
        analysis_cells.append(
            {
                "cell": cell["cell"],
                "centerMeters": cell["centerMeters"],
                "elevationMeters": elevation,
                "waterCoverage": round_number(water_coverage, 2),
                "counts": dict(sorted(counts.items())),
                "hardCoverScore": round_number(hard_cover, 1),
                "concealmentScore": round_number(concealment, 1),
                "passability": passability,
                "tags": tags,
            }
        )
    return {
        "purpose": "Small spatial grid intended for LLM map analysis; exact arrays remain authoritative.",
        "originMeters": [min_x, min_z],
        "cellSizeMeters": cell_size,
        "columns": columns,
        "rows": rows,
        "cells": analysis_cells,
    }


def color_for_height(height: float, minimum: float, maximum: float) -> tuple[int, int, int]:
    t = 0 if maximum <= minimum else (height - minimum) / (maximum - minimum)
    return (int(76 + 65 * t), int(103 + 48 * t), int(58 + 32 * t))


class Raster:
    def __init__(self, width: int, height: int, color: tuple[int, int, int]):
        self.width, self.height = width, height
        self.pixels = bytearray(color * (width * height))

    def rectangle(self, x0: int, y0: int, x1: int, y1: int, color: tuple[int, int, int]) -> None:
        x0, x1 = max(0, min(x0, x1)), min(self.width, max(x0, x1))
        y0, y1 = max(0, min(y0, y1)), min(self.height, max(y0, y1))
        for y in range(y0, y1):
            start = (y * self.width + x0) * 3
            self.pixels[start : start + (x1 - x0) * 3] = bytes(color) * (x1 - x0)

    def circle(self, cx: int, cy: int, radius: int, color: tuple[int, int, int]) -> None:
        radius = max(1, radius)
        for y in range(max(0, cy - radius), min(self.height, cy + radius + 1)):
            span = int(math.sqrt(max(0, radius * radius - (y - cy) ** 2)))
            self.rectangle(cx - span, y, cx + span + 1, y + 1, color)

    def save_png(self, path: Path) -> None:
        raw = b"".join(b"\0" + self.pixels[y * self.width * 3 : (y + 1) * self.width * 3] for y in range(self.height))
        def chunk(kind: bytes, data: bytes) -> bytes:
            return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
        png = b"\x89PNG\r\n\x1a\n"
        png += chunk(b"IHDR", struct.pack(">IIBBBBB", self.width, self.height, 8, 2, 0, 0, 0))
        png += chunk(b"IDAT", zlib.compress(raw, 9))
        png += chunk(b"IEND", b"")
        path.write_bytes(png)


def build_preview(document: dict, path: Path) -> None:
    size = 1600
    raster = Raster(size, size, (80, 105, 62))
    bounds = document["playableBoundsMeters"]
    min_x, min_z = bounds["min"]
    max_x, max_z = bounds["max"]
    scale = size / max(max_x - min_x, max_z - min_z)

    def px(x: float) -> int:
        return int((x - min_x) * scale)

    def py(z: float) -> int:
        return int((max_z - z) * scale)

    terrain = document["terrain"]
    step = terrain["stepMeters"]
    for row, values in enumerate(terrain["heightsMeters"][:-1]):
        z = terrain["originMeters"][1] + row * step
        for column, height in enumerate(values[:-1]):
            x = terrain["originMeters"][0] + column * step
            color = color_for_height(height, terrain["minimumMeters"], terrain["maximumMeters"])
            raster.rectangle(px(x), py(z + step), px(x + step) + 1, py(z) + 1, color)

    water = document["water"]
    for column, row, depth in water["cells"]:
        x = water["originMeters"][0] + column * water["stepMeters"]
        z = water["originMeters"][1] + row * water["stepMeters"]
        blue = (47, 101 + int(depth * 0.12), 151 + int(depth * 0.15))
        raster.rectangle(px(x), py(z + water["stepMeters"]), px(x + water["stepMeters"]) + 1, py(z) + 1, blue)

    palette = {
        "rock": (73, 58, 45),
        "house": (104, 67, 42),
        "obstacle": (96, 72, 46),
        "bridge": (145, 116, 73),
    }
    for primitive in document["staticPrimitives"]:
        cx, cz = primitive["centerMeters"]
        width, depth = primitive["sizeMeters"]
        color = palette[primitive["category"]]
        if primitive["destructible"]:
            color = (205, 166, 54)
        if primitive["shape"] == "ellipse":
            raster.circle(px(cx), py(cz), max(2, int(max(width, depth) * scale * 0.35)), color)
        else:
            raster.rectangle(px(cx - width / 2), py(cz + depth / 2), px(cx + width / 2) + 1, py(cz - depth / 2) + 1, color)

    for x, z, radius, kind, _resource in document["vegetationPlacements"]:
        color = (52, 101, 45) if kind == "tree" else (82, 132, 57)
        raster.circle(px(x), py(z), max(1, int(radius * scale * 0.45)), color)

    for base in document["bases"]:
        x, z = base["positionMeters"]
        color = (79, 165, 103) if base["team"] == 1 else (178, 79, 77)
        raster.circle(px(x), py(z), int(18 * scale), color)
        raster.circle(px(x), py(z), int(10 * scale), (229, 224, 189))

    raster.rectangle(0, 0, size, 4, (35, 39, 31))
    raster.rectangle(0, size - 4, size, size, (35, 39, 31))
    raster.rectangle(0, 0, 4, size, (35, 39, 31))
    raster.rectangle(size - 4, 0, size, size, (35, 39, 31))
    raster.save_png(path)


def validate_document(document: dict) -> list[str]:
    errors = []
    categories = collections.Counter(item["category"] for item in document["staticPrimitives"])
    vegetation = collections.Counter(item[3] for item in document["vegetationPlacements"])
    for required in ("house", "rock", "obstacle"):
        if categories[required] == 0:
            errors.append(f"Нет примитивов категории {required}")
    for required in ("tree", "bush"):
        if vegetation[required] == 0:
            errors.append(f"Нет растительности категории {required}")
    if not document["water"]["cells"]:
        errors.append("Не извлечена вода")
    if len(document["bases"]) < 2:
        errors.append("Не извлечены две базы")
    if document["terrain"]["minimumMeters"] >= document["terrain"]["maximumMeters"]:
        errors.append("Рельеф не содержит перепада высот")
    return errors


def create_document(project_root: Path, wot_root: Path, parser_root: Path, catalog: dict, map_config: dict) -> dict:
    sys.path.insert(0, str(parser_root))
    from compiled_space import CompiledSpace

    version, realm = read_client_version(wot_root)
    package_path = wot_root / map_config["package"]
    arena_package_path = wot_root / map_config["arenaDefinitionPackage"]
    with zipfile.ZipFile(package_path) as package:
        space_bytes = package.read(map_config["spacePath"])
        space = CompiledSpace(io.BytesIO(space_bytes), version, realm, REQUIRED_SECTIONS)
        with zipfile.ZipFile(arena_package_path) as arena_package:
            arena_bytes = arena_package.read(map_config["arenaDefinitionPath"])
        arena = parse_arena(
            unpack_arena_definition(arena_bytes, parser_root), map_config["preferredGameplayType"]
        )
        bwt2 = space.sections["BWT2"]
        heightmaps = load_heightmaps(package, bwt2, space.sections["BWST"], f"spaces/{map_config['id']}")
        defaults = catalog["defaults"]
        terrain = build_terrain_grid(
            heightmaps,
            bwt2._data["settings"]["chunk_size"],
            arena["playableBoundsMeters"],
            defaults["terrainGridStepMeters"],
        )
        water = load_water_mask(
            package,
            map_config["id"],
            space.sections["BWWa"],
            arena["playableBoundsMeters"],
            defaults["waterGridStepMeters"],
        )

    static_primitives, diagnostics = build_static_primitives(
        space.sections,
        arena["playableBoundsMeters"],
        catalog["staticRules"],
        defaults["minimumStaticFootprintMeters"],
    )
    vegetation, clusters = build_vegetation(
        space.sections,
        arena["playableBoundsMeters"],
        catalog["vegetation"],
        defaults["vegetationClusterCellMeters"],
    )
    ai_analysis_grid = build_ai_analysis_grid(
        arena["playableBoundsMeters"],
        defaults["aiAnalysisCellMeters"],
        terrain,
        water,
        static_primitives,
        vegetation,
    )
    static_counts = collections.Counter(item["category"] for item in static_primitives)
    vegetation_counts = collections.Counter(item[3] for item in vegetation)
    return {
        "schemaVersion": 1,
        "pipelineVersion": catalog["pipelineVersion"],
        "mapId": map_config["id"],
        "displayName": map_config["displayName"],
        "englishName": map_config["englishName"],
        "coordinateSystem": {
            "units": "meters",
            "horizontalAxes": ["x", "z"],
            "north": "+z",
            "normalization": "Unity position = [x, z] after applying the chosen LocalTanks map scale.",
        },
        "source": {
            "clientVersion": version,
            "realm": realm,
            "package": map_config["package"],
            "spacePath": map_config["spacePath"],
            "spaceSha256": hashlib.sha256(space_bytes).hexdigest(),
            "rights": "Local prototype reference only. Do not publish WoT-derived output without permission.",
        },
        "playableBoundsMeters": arena["playableBoundsMeters"],
        "bases": arena["bases"],
        "spawns": arena["spawns"],
        "terrain": terrain,
        "water": water,
        "staticPrimitives": static_primitives,
        "vegetationPlacements": vegetation,
        "vegetationClusters": clusters,
        "aiAnalysisGrid": ai_analysis_grid,
        "aiSummary": {
            "purpose": "Compact semantic overview for an AI map composer; exact placement arrays remain authoritative.",
            "staticCounts": dict(sorted(static_counts.items())),
            "vegetationCounts": dict(sorted(vegetation_counts.items())),
            "vegetationClusterCount": len(clusters),
            "analysisCellCount": len(ai_analysis_grid["cells"]),
            "waterCellCount": len(water["cells"]),
            "terrainElevationRangeMeters": [terrain["minimumMeters"], terrain["maximumMeters"]],
            "gameplaySemantics": {
                "house": "solid cover; destructible flag decides whether it may be destroyed",
                "rock": "indestructible cover blocking movement, vision and projectiles",
                "obstacle": "yellow when destructible, otherwise solid cover",
                "bush": "concealment; does not block projectiles",
                "tree": "upright destructible obstacle; after falling it becomes bush-like concealment",
                "water": "impassable or strongly slowing for route planning; never blocks projectiles",
            },
        },
        "diagnostics": diagnostics,
    }


def write_report(document: dict, path: Path) -> None:
    summary = document["aiSummary"]
    lines = [
        f"# {document['displayName']} — primitive conversion report",
        "",
        f"- Source map: `{document['mapId']}` ({document['source']['clientVersion']})",
        f"- Playable bounds: `{document['playableBoundsMeters']['min']}` → `{document['playableBoundsMeters']['max']}` metres",
        f"- Elevation: `{summary['terrainElevationRangeMeters'][0]}` → `{summary['terrainElevationRangeMeters'][1]}` metres",
        f"- Water cells: `{summary['waterCellCount']}`",
        f"- Vegetation clusters: `{summary['vegetationClusterCount']}`",
        "",
        "## Primitive counts",
        "",
    ]
    for key, value in summary["staticCounts"].items():
        lines.append(f"- `{key}`: {value}")
    for key, value in summary["vegetationCounts"].items():
        lines.append(f"- `{key}`: {value}")
    lines += [
        "",
        "The JSON is the authoritative generated artifact. The PNG is only a human review preview.",
        "WoT-derived output is local-only and must not be published without permission from the rights holder.",
        "",
    ]
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-root", type=Path, required=True)
    parser.add_argument("--wot-root", type=Path, required=True)
    parser.add_argument("--parser-root", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--map", dest="map_id", required=True)
    parser.add_argument("--mode", choices=("build", "validate", "list"), required=True)
    args = parser.parse_args()

    catalog = load_json(args.catalog)
    maps = {item["id"]: item for item in catalog["maps"]}
    if args.mode == "list":
        for item in maps.values():
            print(f"{item['id']}: {item['displayName']} / {item['englishName']}")
        return 0
    if args.map_id not in maps:
        fail(f"Карта {args.map_id} отсутствует в catalog")

    output_root = args.project_root / catalog["defaults"]["outputRoot"] / args.map_id
    output_json = output_root / f"{args.map_id}_primitives.json"
    if args.mode == "build":
        output_root.mkdir(parents=True, exist_ok=True)
        document = create_document(args.project_root, args.wot_root, args.parser_root, catalog, maps[args.map_id])
        errors = validate_document(document)
        if errors:
            fail("Конвертация не прошла валидацию:\n- " + "\n- ".join(errors))
        output_json.write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding="utf-8")
        build_preview(document, output_root / f"{args.map_id}_preview.png")
        write_report(document, output_root / "conversion_report.md")
        print(json.dumps(document["aiSummary"], ensure_ascii=False, indent=2))
        print(f"Output: {output_json}")
        return 0

    if not output_json.is_file():
        fail(f"Сначала выполните Build: {output_json} не найден")
    document = load_json(output_json)
    errors = validate_document(document)
    if document.get("pipelineVersion") != catalog["pipelineVersion"]:
        errors.append("Generated pipelineVersion не совпадает с catalog")
    if errors:
        fail("Валидация не пройдена:\n- " + "\n- ".join(errors))
    print(f"Validated: {output_json}")
    print(json.dumps(document["aiSummary"], ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise
