#!/usr/bin/env python3
"""Find and verify World of Tanks Wwise voice events without guessing names.

The client never ships the Wwise project, so a bank stores only hashed event names. Guesswork is
therefore useless, and two facts have to be established before a voice line can be extracted:

1. which event *names* the game itself uses — they are plain strings inside the packed XML of
   ``res/packages/scripts.pkg`` (arena definitions and ``item_defs/hints/battle_hints.xml``);
2. whether a bank really contains that event — the FNV-1 32-bit hash of the name must appear as a
   ``CAkEvent`` id in a bank dump made by wwiser.

This script does both: it collects candidate names from the client scripts, reads the Wwise language
ID of every nation's voice bank straight from the package, and verifies the candidates against a bank
dump. It changes nothing, so it is safe to run at any time.

Produce the dump first (the audio pipeline already does this for the banks it extracts)::

    cd LocalData/WoTAudioPipeline/Work/Banks/audioww/ussr
    python ../../../../ThirdParty/wwiser.pyz ../Init.bnk voiceover.bnk -d txt \
        -nl ../wwnames.txt -nd ../../../../ThirdParty/wwnames.db3

Then::

    python Automation/pipelines/WoTAudioPipeline/Scripts/inspect_wot_voice.py \
        --dump LocalData/WoTAudioPipeline/Work/Banks/audioww/ussr/banks.txt
"""

from __future__ import annotations

import argparse
import json
import re
import struct
import sys
import zipfile
from pathlib import Path

NAME_PATTERN = re.compile(r"(?:ev_wt_)?vo_[a-z0-9_]+")
LANGUAGE_BANKS = ("voiceover.bnk", "inbattle_communication_pc.bnk")


def fnv1(name: str) -> int:
    """The hash Wwise uses for names: FNV-1, 32 bit, lowercase."""
    value = 2166136261
    for byte in name.lower().encode("utf-8"):
        value = (value * 16777619) & 0xFFFFFFFF
        value ^= byte
    return value


def collect_candidates(package: Path, pattern: re.Pattern[str]) -> dict[str, str]:
    """Candidate voice names from the client's packed XML, with one source entry per name."""
    found: dict[str, str] = {}
    with zipfile.ZipFile(package) as archive:
        for name in archive.namelist():
            if not name.lower().endswith(".xml"):
                continue
            text = archive.read(name).decode("utf-8", errors="ignore")
            for match in pattern.findall(text):
                found.setdefault(match, name)
    return found


def read_event_hashes(dump: Path) -> set[int]:
    """Event ids from a wwiser text dump. Every dump line starts with an address prefix."""
    text = dump.read_text(encoding="utf-8", errors="ignore")
    hashes: set[int] = set()
    for match in re.finditer(r"\[Event\]", text):
        tail = text[match.end():match.end() + 400]
        identifier = re.search(r"ulID = (\d+)", tail)
        if identifier:
            hashes.add(int(identifier.group(1)))
    return hashes


def read_switch_hashes(dump: Path) -> set[int]:
    """Switch group and switch value ids from a wwiser text dump.

    A crew voice line is not one recording: every national bank branches it on the Wwise switch that
    names the speaking crew's sex, so the two takes of a line are two switch values of one event. Those
    ids are hashes of names too, and are verified here the same way an event name is.
    """
    text = dump.read_text(encoding="utf-8", errors="ignore")
    hashes: set[int] = set()
    for match in re.finditer(r"(?:tid|sid)\s+(?:ulGroupID|ulSwitchID|ulDefaultSwitch|SwitchGroupID)\s*=\s*(\d+)", text):
        hashes.add(int(match.group(1)))
    return hashes


def read_hint_names(path: Path) -> list[str]:
    """Every name the project claims to know, from the hint file the pipeline copies into its work bank."""
    if not path.exists():
        return []
    return [line.strip() for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def read_language_ids(packages: list[Path]) -> dict[str, dict[str, tuple[int, int]]]:
    """Per bank folder: bank name -> (Wwise language id, media id), read from the BKHD header.

    The folder is every path segment between `audioww/` and the bank file, so a national bank is
    `ussr` and a commander or event bank is `bloggers/Cygan` or `HW25/HW25_de_DE_Mailand`. Those
    nested banks are the other place a crew voice can live, and they are enumerated here so that
    finding them does not depend on someone remembering their names.
    """
    found: dict[str, dict[str, tuple[int, int]]] = {}
    for package in packages:
        with zipfile.ZipFile(package) as archive:
            for info in archive.infolist():
                parts = info.filename.split("/")
                if len(parts) < 3 or parts[0] != "audioww" or parts[-1] not in LANGUAGE_BANKS:
                    continue
                with archive.open(info.filename) as stream:
                    header = stream.read(0x18)
                if header[:4] != b"BKHD":
                    continue
                _, media_id, language_id = struct.unpack_from("<III", header, 8)
                folder = "/".join(parts[1:-1])
                found.setdefault(folder, {})[parts[-1]] = (language_id, media_id)
    return found


def declared_clips(project_root: Path) -> tuple[set[str], set[str]]:
    """Clip ids and event names the project already declares."""
    path = project_root / "Automation/pipelines/WoTAudioPipeline/audio_clips.json"
    if not path.exists():
        return set(), set()
    catalog = json.loads(path.read_text(encoding="utf-8"))
    clips = {clip["clipId"] for clip in catalog["clips"]}
    events = {clip.get("event", "") for clip in catalog["clips"]}
    return clips, events


def declared_bank_entries(project_root: Path) -> set[str]:
    """Every bank entry some declared clip extracts from, which is what 'extracted' really means."""
    path = project_root / "Automation/pipelines/WoTAudioPipeline/audio_clips.json"
    if not path.exists():
        return set()
    catalog = json.loads(path.read_text(encoding="utf-8"))
    return {clip["bankEntry"] for clip in catalog["clips"] if clip.get("bankEntry")}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--wot-root", default=r"C:\Games\World_of_Tanks_EU")
    parser.add_argument("--project-root", default=str(Path(__file__).resolve().parents[4]))
    parser.add_argument("--dump", help="wwiser text dump of the bank whose events must be verified")
    parser.add_argument("--pattern", default=r"(?:ev_wt_)?vo_[a-z0-9_]+")
    parser.add_argument("--show-unverified", action="store_true")
    parser.add_argument("--show-special", action="store_true",
                        help="list the nested commander, event and blogger voice banks as well")
    arguments = parser.parse_args()

    wot_root = Path(arguments.wot_root)
    scripts_package = wot_root / "res/packages/scripts.pkg"
    if not scripts_package.exists():
        print(f"scripts.pkg was not found under {wot_root}", file=sys.stderr)
        return 2

    packages = sorted((wot_root / "res/packages").glob("audioww-part*.pkg"))
    if not packages:
        print(f"audioww packages were not found under {wot_root}", file=sys.stderr)
        return 2

    candidates = collect_candidates(scripts_package, re.compile(arguments.pattern))
    print(f"candidate names in client scripts: {len(candidates)}")

    clips, events = declared_clips(Path(arguments.project_root))
    print(f"already declared in audio_clips.json: {len(clips)} clips, {len(events)} events")

    if arguments.dump:
        dump = Path(arguments.dump)
        if not dump.exists():
            print(f"dump was not found: {dump}", file=sys.stderr)
            return 2
        hashes = read_event_hashes(dump)
        print(f"event objects in {dump.name}: {len(hashes)}")
        verified = sorted(name for name in candidates if fnv1(name) in hashes)
        print(f"verified as Wwise events: {len(verified)}")
        for name in verified:
            mark = "declared" if name in events else "not declared"
            print(f"  {fnv1(name):>10}  {name:<60} {mark}")
        if arguments.show_unverified:
            unverified = sorted(name for name in candidates if fnv1(name) not in hashes)
            print(f"--- candidates that are not events in this bank: {len(unverified)} ---")
            for name in unverified:
                print(f"  {name}")

        # The hint file is what makes a hash readable for wwiser, and every hint in it is a claim about
        # this installation: an event name or a switch name. Checking them here keeps that claim testable
        # instead of trusting the file, which matters because a crew line is two switch values, not one.
        switches = read_switch_hashes(dump)
        hints = read_hint_names(Path(arguments.project_root) / "Automation/pipelines/WoTAudioPipeline/wwnames.txt")
        print(f"--- hints in wwnames.txt: {len(hints)} ---")
        for name in hints:
            digest = fnv1(name)
            if digest in hashes:
                print(f"  {digest:>12}  {name:<60} event")
            elif digest in switches:
                print(f"  {digest:>12}  {name:<60} switch")
            else:
                print(f"  {digest:>12}  {name:<60} NOT in this dump")
        print(f"switch objects in {dump.name}: {len(switches)}")
    else:
        print("no --dump given: names were collected but not verified against a bank")

    language_ids = read_language_ids(packages)
    extracted = declared_bank_entries(Path(arguments.project_root))
    national = sorted(folder for folder in language_ids if "/" not in folder)
    special = sorted(folder for folder in language_ids if "/" in folder)
    print("--- national voice banks (Wwise language id / bank entry) ---")
    for folder in national:
        entry = language_ids[folder].get("voiceover.bnk")
        if entry is None:
            continue
        language_id, _ = entry
        bank_entry = f"{folder}/voiceover.bnk"
        mark = "declared" if bank_entry in extracted else ""
        print(f"  {folder:<10} languageId={language_id:<12} {bank_entry:<32} {mark}")
    print(f"--- other voice banks: {len(special)} commander, event and blogger sets ---")
    if arguments.show_special:
        for folder in special:
            entry = language_ids[folder].get("voiceover.bnk")
            if entry is None:
                continue
            language_id, _ = entry
            bank_entry = f"{folder}/voiceover.bnk"
            mark = "declared" if bank_entry in extracted else ""
            print(f"  {folder:<40} languageId={language_id:<12} {mark}")
    else:
        print("  pass --show-special to list them; they hold the same kind of localized crew lines "
              "under their own bank folders")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
