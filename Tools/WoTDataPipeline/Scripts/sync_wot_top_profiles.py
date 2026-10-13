from __future__ import annotations

import argparse
import json
import math
import re
import struct
import xml.etree.ElementTree as ET
import zipfile
from io import BytesIO
from pathlib import Path


PACKED_HEADER = 0x62A14E45
META_TAGS = {"xmlns:xmlref", "userString", "shared"}


class PackedXmlReader:
    def __init__(self) -> None:
        self.stream: BytesIO | None = None
        self.dictionary: list[str] = []

    def read(self, data: bytes) -> ET.Element:
        if len(data) < 4 or struct.unpack("<I", data[:4])[0] != PACKED_HEADER:
            return ET.fromstring(data.decode("utf-8"))
        self.stream = BytesIO(data)
        self.dictionary = self._read_dictionary()
        root = ET.Element("root")
        self._read_element(root)
        self.stream = None
        return root

    def _read_dictionary(self) -> list[str]:
        assert self.stream is not None
        self.stream.seek(5)
        result: list[str] = []
        while True:
            value = bytearray()
            while True:
                byte = self.stream.read(1)
                if not byte or byte == b"\0":
                    break
                value.extend(byte)
            if not value:
                return result
            result.append(value.decode("utf-8", errors="ignore"))

    def _descriptor(self) -> tuple[int, int]:
        assert self.stream is not None
        raw = self.stream.read(4)
        if len(raw) != 4:
            raise ValueError("Truncated packed XML descriptor")
        value = struct.unpack("<I", raw)[0]
        return value >> 28, value & 0x0FFFFFFF

    def _read_element(self, parent: ET.Element) -> None:
        assert self.stream is not None
        child_count = struct.unpack("<H", self.stream.read(2))[0]
        root_descriptor = self._descriptor()
        children: list[tuple[int, tuple[int, int]]] = []
        for _ in range(child_count):
            name_index = struct.unpack("<H", self.stream.read(2))[0]
            children.append((name_index, self._descriptor()))
        offset = self._read_data(parent, 0, root_descriptor)
        for name_index, descriptor in children:
            child = ET.SubElement(parent, self.dictionary[name_index])
            offset = self._read_data(child, offset, descriptor)

    def _read_data(self, element: ET.Element, offset: int, descriptor: tuple[int, int]) -> int:
        assert self.stream is not None
        kind, end = descriptor
        length = end - offset
        if kind == 0:
            self._read_element(element)
        elif kind == 1:
            element.text = self.stream.read(length).decode("utf-8") if length else ""
        elif kind == 2:
            data = self.stream.read(length)
            formats = {0: None, 1: "b", 2: "<H", 4: "<I", 8: "<Q"}
            fmt = formats.get(length)
            if length == 0:
                element.text = "0"
            elif fmt is None:
                raise ValueError(f"Unsupported integer width {length}")
            else:
                element.text = str(struct.unpack(fmt, data)[0])
        elif kind == 3:
            values = [str(struct.unpack("f", self.stream.read(4))[0]) for _ in range(length // 4)]
            if len(values) == 12:
                for index in range(0, 12, 3):
                    row = ET.SubElement(element, f"row{index // 3}")
                    row.text = " ".join(values[index : index + 3])
            else:
                element.text = " ".join(values)
        elif kind == 4:
            if length not in (0, 1):
                raise ValueError(f"Unsupported boolean width {length}")
            element.text = "true" if length == 1 and self.stream.read(1) == b"\1" else "false"
        elif kind == 5:
            self.stream.read(length)
            element.text = ""
        else:
            raise ValueError(f"Unsupported packed XML value type {kind}")
        return end


def text(element: ET.Element | None, path: str, default: str = "") -> str:
    if element is None:
        return default
    found = element.find(path)
    return default if found is None or found.text is None else found.text.strip()


def number(element: ET.Element | None, path: str, default: float = 0.0) -> float:
    value = text(element, path)
    return float(value) if value else default


def overlay_number(override: ET.Element, base: ET.Element, path: str, default: float = 0.0) -> float:
    value = text(override, path)
    return float(value) if value else number(base, path, default)


def module_children(parent: ET.Element | None) -> list[ET.Element]:
    return [] if parent is None else [child for child in parent if child.tag not in META_TAGS]


def best_component(root: ET.Element, tag: str) -> ET.Element:
    matches = [item for item in root.iter() if item.tag == tag]
    if not matches:
        raise ValueError(f"Component '{tag}' was not found")
    return max(matches, key=lambda item: sum(1 for _ in item.iter()))


def round_value(value: float, digits: int = 4) -> float:
    return round(value, digits)


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def clip_id(prefix: str, event_name: str) -> str:
    normalized = re.sub(r"[^a-z0-9]+", "_", event_name.lower()).strip("_")
    if not normalized:
        raise ValueError(f"Cannot derive {prefix} clip ID from an empty WoT event")
    return f"{prefix}_{normalized}"


def validate_rendered_top_configuration(project_root: Path, profile: dict) -> None:
    vehicle_id = profile["vehicleId"]
    manifest_path = (
        project_root
        / "Assets/LocalOnly/WoTGenerated"
        / vehicle_id
        / f"{vehicle_id}_manifest.json"
    )
    if not manifest_path.is_file():
        raise FileNotFoundError(
            f"Canonical render manifest is missing for {vehicle_id}: {manifest_path}"
        )
    manifest = load_json(manifest_path)
    selected = manifest.get("selectedModules", {})
    expected = profile["topModules"]
    mismatches = [
        f"{key}: render={selected.get(key)!r}, top={expected[key]!r}"
        for key in ("chassis", "turret", "gun")
        if selected.get(key) != expected[key]
    ]
    if mismatches:
        raise ValueError(
            f"{vehicle_id} sprite does not use the current top configuration: "
            + "; ".join(mismatches)
            + ". Re-render it without explicit module indices."
        )


def extract_profile(archive: zipfile.ZipFile, entry: dict, conversion: dict) -> dict:
    reader = PackedXmlReader()
    nation = entry["nation"]
    vehicle_id = entry["vehicleId"]

    def load(internal: str) -> ET.Element:
        return reader.read(archive.read(internal))

    vehicle = load(f"scripts/item_defs/vehicles/{nation}/{vehicle_id}.xml")
    chassis = module_children(vehicle.find("chassis"))[-1]
    turret = module_children(vehicle.find("turrets0"))[-1]
    gun_override = module_children(turret.find("guns"))[-1]

    component_root = f"scripts/item_defs/vehicles/{nation}/components"
    gun_base = best_component(load(f"{component_root}/guns.xml"), gun_override.tag)
    engine_names = [item.tag for item in module_children(vehicle.find("engines"))]
    engines = load(f"{component_root}/engines.xml")
    engine = max(
        (best_component(engines, name) for name in engine_names),
        key=lambda item: number(item, "power"),
    )
    radio_name = module_children(vehicle.find("radios"))[-1].tag
    fuel_name = module_children(vehicle.find("fuelTanks"))[-1].tag
    radio = best_component(load(f"{component_root}/radios.xml"), radio_name)
    fuel = best_component(load(f"{component_root}/fuelTanks.xml"), fuel_name)

    shots = module_children(gun_base.find("shots"))
    shell_ref = max(shots, key=lambda item: number(item, "defaultPortion"))
    shell = best_component(load(f"{component_root}/shells.xml"), shell_ref.tag)

    gun_effect_name = text(gun_override, "effects", text(gun_base, "effects"))
    gun_effects = load("scripts/item_defs/vehicles/common/gun_effects.xml")
    gun_effect = best_component(gun_effects, gun_effect_name)
    shot_sound = next((text(item, "wwsoundPC") for item in gun_effect.iter() if item.find("wwsoundPC") is not None), "")

    primary_armor = text(vehicle, "hull/primaryArmor").split()
    if len(primary_armor) < 3:
        raise ValueError(f"{vehicle_id} does not expose three primary hull armor plates")
    armor_values = [number(vehicle, f"hull/armor/{name}") for name in primary_armor[:3]]

    hull_weight = number(vehicle, "hull/weight")
    module_weights = [
        number(chassis, "weight"),
        number(turret, "weight"),
        number(gun_base, "weight"),
        number(engine, "weight"),
        number(radio, "weight"),
        number(fuel, "weight"),
    ]
    mass_tonnes = (hull_weight + sum(module_weights)) / 1000.0
    engine_power = number(engine, "power")
    power_to_weight = engine_power / mass_tonnes

    forward_kmh = number(vehicle, "speedLimits/forward")
    reverse_kmh = number(vehicle, "speedLimits/backward")
    traverse = number(chassis, "rotationSpeed")
    turret_traverse = number(turret, "rotationSpeed")
    medium_resistance = float(text(chassis, "terrainResistance").split()[1])
    brake_force = number(chassis, "brakeForce")

    dispersion_metres = overlay_number(gun_override, gun_base, "shotDispersionRadius")
    base_dispersion_degrees = math.degrees(math.atan(dispersion_metres / 100.0))
    movement_factor = number(chassis, "shotDispersionFactors/vehicleMovement")
    hull_factor = number(chassis, "shotDispersionFactors/vehicleRotation")
    turret_factor = overlay_number(gun_override, gun_base, "shotDispersionFactors/turretRotation")
    after_shot_factor = overlay_number(gun_override, gun_base, "shotDispersionFactors/afterShot")
    movement_penalty = base_dispersion_degrees * movement_factor * forward_kmh / 10.0
    hull_penalty = base_dispersion_degrees * hull_factor * traverse / 10.0
    turret_penalty = base_dispersion_degrees * turret_factor * turret_traverse / 10.0
    shot_penalty = base_dispersion_degrees * after_shot_factor

    still_camo = number(vehicle, "invisibility/still")
    moving_camo = number(vehicle, "invisibility/moving")
    shot_camo_factor = overlay_number(gun_override, gun_base, "invisibilityFactorAtShot", 1.0)

    shell_speed = number(shell_ref, "speed")
    shell_penetration = [float(item) for item in text(shell_ref, "piercingPower").split()]
    caliber = number(shell, "caliber")
    damage = int(number(shell, "damage/armor"))
    magazine_size = max(1, int(number(gun_base, "clip/count", 1)))
    clip_rate = number(gun_base, "clip/rate")
    shot_interval = 60.0 / clip_rate if magazine_size > 1 and clip_rate > 0 else 0.0

    speed_scale = conversion["kilometresPerHourPerUnitySpeed"]
    projectile_divisor = conversion["projectileSpeedDivisor"]
    projectile_range = conversion["maximumProjectileRange"]
    projectile_speed = shell_speed / projectile_divisor

    return {
        "tankId": entry["tankId"],
        "vehicleId": vehicle_id,
        "nation": nation,
        "topModules": {
            "chassis": chassis.tag,
            "turret": turret.tag,
            "gun": gun_override.tag,
            "engine": engine.tag,
            "radio": radio.tag,
            "fuelTank": fuel.tag,
        },
        "raw": {
            "hitPoints": int(number(vehicle, "hull/maxHealth") + number(turret, "maxHealth")),
            "massTonnes": round_value(mass_tonnes, 3),
            "enginePowerHp": round_value(engine_power, 1),
            "powerToWeightHpPerTonne": round_value(power_to_weight, 3),
            "forwardSpeedKmh": forward_kmh,
            "reverseSpeedKmh": reverse_kmh,
            "hullTraverseDegreesPerSecond": traverse,
            "turretTraverseDegreesPerSecond": turret_traverse,
            "terrainResistance": [float(item) for item in text(chassis, "terrainResistance").split()],
            "brakeForce": brake_force,
            "armorFrontSideRearMm": armor_values,
            "viewRangeMetres": number(turret, "circularVisionRadius"),
            "concealmentStill": still_camo,
            "concealmentMoving": moving_camo,
            "gunReloadSeconds": overlay_number(gun_override, gun_base, "reloadTime"),
            "gunAimingSeconds": overlay_number(gun_override, gun_base, "aimingTime"),
            "dispersionMetresAt100m": dispersion_metres,
            "magazineSize": magazine_size,
            "burstSize": max(1, int(number(gun_base, "burst/count", 1))),
            "roundsPerMinuteInsideMagazine": clip_rate,
            "shell": shell_ref.tag,
            "caliberMm": caliber,
            "shellDamage": damage,
            "shellPenetrationAt50mMm": shell_penetration[0],
            "shellPenetrationAtMaximumRangeMm": shell_penetration[-1],
            "shellSpeedMetresPerSecond": shell_speed,
            "shellMaximumDistanceMetres": number(shell_ref, "maxDistance"),
            "ricochetAngleDegrees": number(shell, "ricochetAngle", 70.0),
            "engineAudioEvent": text(engine, "wwsoundPC"),
            "gunEffect": gun_effect_name,
            "gunAudioEvent": shot_sound,
            "turretAudioEvent": text(turret, "wwturretRotatorSoundManual"),
        },
        "final": {
            "maxHitPoints": int(number(vehicle, "hull/maxHealth") + number(turret, "maxHealth")),
            "massTonnes": round_value(mass_tonnes, 3),
            "enginePowerHp": round_value(engine_power, 1),
            "weaponId": entry["weaponId"],
            "mobility": {
                "maxForwardSpeed": round_value(forward_kmh / speed_scale),
                "maxReverseSpeed": round_value(reverse_kmh / speed_scale),
                "acceleration": round_value(power_to_weight / conversion["accelerationPowerToWeightDivisor"]),
                "groundResistance": round_value(medium_resistance * conversion["groundResistanceMultiplier"]),
                "braking": round_value(brake_force / (mass_tonnes * 1000.0) / 3.86),
                "hullTurnSpeed": round_value(traverse),
                "turretTurnSpeed": round_value(turret_traverse),
            },
            "armor": {
                "front": armor_values[0],
                "left": armor_values[1],
                "right": armor_values[1],
                "rear": armor_values[2],
            },
            "gunHandling": {
                "minimumDispersionDegrees": round_value(base_dispersion_degrees),
                "maximumDispersionDegrees": round_value(base_dispersion_degrees + movement_penalty + hull_penalty + turret_penalty),
                "aimingTimeSeconds": round_value(overlay_number(gun_override, gun_base, "aimingTime")),
                "movementDispersionDegrees": round_value(movement_penalty),
                "hullTraverseDispersionDegrees": round_value(hull_penalty),
                "turretTraverseDispersionDegrees": round_value(turret_penalty),
                "shotDispersionDegrees": round_value(shot_penalty),
            },
            # View range and the guaranteed detection radius are deliberately absent: they are derived from
            # the vehicle class and tier by the game (ADR-064, VisionRangeTable), so the file must not carry a
            # second copy of them. The importer rejects the fields by name if they come back.
            "vision": {
                "stationaryConcealment": round_value(still_camo),
                "movementRevealPenalty": round_value(max(0.0, still_camo - moving_camo)),
                "firingRevealPenalty": round_value(still_camo * (1.0 - shot_camo_factor)),
                "firingRevealDuration": conversion["firingRevealDuration"],
            },
            "weapon": {
                "id": entry["weaponId"],
                "displayName": entry["weaponDisplayName"],
                # Bore diameter decides which visual effect band a shot uses, so it travels with the gun
                # instead of being repeated in a per-machine visual table.
                "caliberMm": round_value(caliber, 1),
                "reloadSeconds": round_value(overlay_number(gun_override, gun_base, "reloadTime")),
                "magazineSize": magazine_size,
                "shotIntervalSeconds": round_value(shot_interval),
                "shellId": entry["shellId"],
            },
            "shell": {
                "id": entry["shellId"],
                "displayName": entry["shellDisplayName"],
                "damage": damage,
                "penetration": shell_penetration[0],
                "speed": round_value(projectile_speed),
                "radius": round_value(max(0.02, caliber / 1200.0), 3),
                "lifetimeSeconds": round_value(projectile_range / projectile_speed + 0.25),
                "maximumRange": projectile_range,
                "ricochetAngle": number(shell, "ricochetAngle", 70.0),
                "ricochetSpeedMultiplier": 0.7,
                "ricochetPenetrationMultiplier": 0.65,
                "maximumRicochets": 2,
            },
        },
    }


def apply_profile(project_root: Path, profile: dict) -> None:
    validate_rendered_top_configuration(project_root, profile)
    definitions = project_root / "Assets/Game/GameData/Definitions"
    tank_path = definitions / "Tanks" / f"{profile['tankId']}.json"
    tank = load_json(tank_path)
    final = profile["final"]
    for key in ("maxHitPoints", "massTonnes", "enginePowerHp", "weaponId", "mobility", "armor", "gunHandling", "vision"):
        tank[key] = final[key]
    audio = tank.get("audio")
    if audio is not None:
        raw = profile["raw"]
        audio["engineClipId"] = clip_id("engine", raw["engineAudioEvent"])
        audio["shotClipId"] = clip_id("gun", raw["gunAudioEvent"])
        audio["turretClipId"] = clip_id("turret", raw["turretAudioEvent"])
    write_json(tank_path, tank)

    weapon = {"schemaVersion": 1, **final["weapon"]}
    shell = {"schemaVersion": 1, **final["shell"]}
    write_json(definitions / "Weapons" / f"{weapon['id']}.json", weapon)
    write_json(definitions / "Shells" / f"{shell['id']}.json", shell)


def markdown(version: str, profiles: list[dict], conversion: dict) -> str:
    lines = [
        "# Снимок топовых конфигураций World of Tanks",
        "",
        "Документ генерируется `Tools/WoTDataPipeline/Scripts/Sync-WoTTopProfiles.ps1` из локального `scripts.pkg`. Числа вручную не редактируются.",
        "",
        f"Клиент-источник: World of Tanks {version}.",
        "",
        "| Танк | Топовые модули | HP | Масса, т | Мощность, л.с. | Скорость вперёд/назад, км/ч | Поворот корпуса/башни, °/с | Обзор, м |",
        "|---|---|---:|---:|---:|---|---|---:|",
    ]
    for item in profiles:
        raw = item["raw"]
        modules = item["topModules"]
        lines.append(
            f"| {item['vehicleId']} | {modules['chassis']}; {modules['turret']}; {modules['gun']}; {modules['engine']} | "
            f"{raw['hitPoints']} | {raw['massTonnes']:g} | {raw['enginePowerHp']:g} | "
            f"{raw['forwardSpeedKmh']:g} / {raw['reverseSpeedKmh']:g} | "
            f"{raw['hullTraverseDegreesPerSecond']:g} / {raw['turretTraverseDegreesPerSecond']:g} | {raw['viewRangeMetres']:g} |"
        )
    lines += [
        "",
        "| Танк | Орудие | Урон | Пробитие 50 м / предельная дистанция, мм | Перезарядка, с | Магазин | Интервал, с | Сведение, с | Разброс на 100 м |",
        "|---|---|---:|---|---:|---:|---:|---:|---:|",
    ]
    for item in profiles:
        raw = item["raw"]
        interval = item["final"]["weapon"]["shotIntervalSeconds"]
        lines.append(
            f"| {item['vehicleId']} | {item['topModules']['gun']} | {raw['shellDamage']} | "
            f"{raw['shellPenetrationAt50mMm']:g} / {raw['shellPenetrationAtMaximumRangeMm']:g} | "
            f"{raw['gunReloadSeconds']:g} | {raw['magazineSize']} | {interval:g} | "
            f"{raw['gunAimingSeconds']:g} | {raw['dispersionMetresAt100m']:g} |"
        )
    lines += [
        "",
        "## Преобразования LocalTanks",
        "",
        f"- скорость машины: WoT км/ч ÷ {conversion['kilometresPerHourPerUnitySpeed']:g};",
        "- обзор и радиус гарантированного обнаружения не переносятся: их выводит игра из класса и уровня"
        " техники (ADR-064, VisionRangeTable), а сырой circularVisionRadius остаётся в снимке справкой;",
        f"- визуальная скорость снаряда: WoT м/с ÷ {conversion['projectileSpeedDivisor']:g};",
        "- HP, броня, урон, начальное пробитие, перезарядка, сведение и скорости поворота переносятся напрямую;",
        "- масса — сумма выбранных модулей клиента; ускорение выводится из мощности на тонну, торможение — из brakeForce и массы;",
        "- базовый разброс переводится из метров на 100 м в градусы, а штрафы движения, корпуса и башни выводятся из коэффициентов WoT;",
        "- маскировка стоя переносится напрямую, штраф движения равен разнице между стоячей и движущейся маскировкой.",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--config", required=True)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    project_root = Path(args.project_root).resolve()
    config = load_json(Path(args.config))
    if config.get("schemaVersion") != 1:
        raise ValueError("Unsupported WoT data-pipeline schema")
    wot_root = Path(config["wotRoot"])
    package = wot_root / "res/packages/scripts.pkg"
    if not package.is_file():
        raise FileNotFoundError(f"WoT scripts package was not found: {package}")

    version_root = ET.parse(wot_root / "version.xml").getroot()
    version = (version_root.findtext("version") or "unknown").strip()
    with zipfile.ZipFile(package) as archive:
        profiles = [extract_profile(archive, item, config["conversion"]) for item in config["profiles"]]

    if args.apply:
        for profile in profiles:
            apply_profile(project_root, profile)
        snapshot = project_root / "Docs/WOT_TOP_CONFIG_SNAPSHOT.md"
        snapshot.write_text(markdown(version, profiles, config["conversion"]), encoding="utf-8")

    print(json.dumps({"clientVersion": version, "profiles": profiles}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
