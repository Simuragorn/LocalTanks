#!/usr/bin/env python3
"""Add or remove one national crew voice set in the clip catalog and the crew profile.

A national crew voice set is twenty clips: ten roles in a male and a female take of the same Wwise
switch. Writing them by hand is where a set quietly loses a role, so the entries are derived from a
reference set that is already extracted — the catalog itself stays the source of truth for the roles,
the switch values, the folder shape and the silence trimming. A new set is appended to the end of the
crew block, so the file order stays predictable.

Adding a language does three things and refuses to guess at the rest: it appends the twenty clips,
it checks that the bank really exists in the installed client, and it can declare the language in
`CrewVoiceProfiles.json`. It never binds a nation: which language a nation speaks is a gameplay
decision, and a nation the game does not have yet cannot be bound at all.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import zipfile
from pathlib import Path

# Language display names are Cyrillic in this project, and a Windows console defaults to a legacy code
# page, where printing one raises instead of writing a question mark. Reporting a set must never be the
# thing that fails.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

PROJECT_ROOT = Path(__file__).resolve().parents[3]
CATALOG = PROJECT_ROOT / "Tools/WoTAudioPipeline/audio_clips.json"
PROFILE = PROJECT_ROOT / "Assets/Game/Resources/CrewVoiceProfiles.json"
CLIP_ID_PATTERN = re.compile(r"^[a-z0-9_]+$")
CREW_GROUP_PREFIX = "Universal/CrewVoice/"


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def save_json(path: Path, data: dict) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def crew_clips(catalog: dict) -> list[dict]:
    return [clip for clip in catalog["clips"] if str(clip.get("group", "")).startswith(CREW_GROUP_PREFIX)]


def templates(catalog: dict, reference: str) -> tuple[list[dict], list[dict]]:
    """The male and female entries of the reference language, in catalog order."""
    clips = crew_clips(catalog)
    male = [clip for clip in clips if clip["clipId"].endswith(f"_{reference}_male")]
    female = [clip for clip in clips if clip["clipId"].endswith(f"_{reference}_female")]
    return male, female


def folder_of(clip: dict) -> str:
    """`Universal/CrewVoice/Russian/Male` -> `Russian`."""
    parts = str(clip["group"]).split("/")
    if len(parts) < 4:
        raise SystemExit(f"clip '{clip['clipId']}' has an unusable group '{clip['group']}'")
    return parts[-2]


def bank_name(bank_entry: str) -> str:
    """`poland/voiceover.bnk` -> `poland_voiceover`, the logical bank the catalog records."""
    return Path(bank_entry).parent.name + "_" + Path(bank_entry).stem


def bank_exists(wot_root: Path, bank_entry: str) -> bool:
    wanted = f"audioww/{bank_entry}".lower()
    for package in sorted((wot_root / "res/packages").glob("audioww-part*.pkg")):
        try:
            with zipfile.ZipFile(package) as archive:
                for name in archive.namelist():
                    if name.lower() == wanted:
                        return True
        except zipfile.BadZipFile:
            continue
    return False


def build_entries(male: list[dict], female: list[dict], language: str, reference: str, folder: str,
                  bank_entry: str, language_id: str) -> list[dict]:
    entries: list[dict] = []
    for voice, template in (("male", male), ("female", female)):
        tail = f"_{reference}_{voice}"
        for clip in template:
            old_id = clip["clipId"]
            if not old_id.endswith(tail):
                raise SystemExit(f"reference clip '{old_id}' does not end with '{tail}'")
            new_id = old_id[: -len(tail)] + f"_{language}_{voice}"
            if not CLIP_ID_PATTERN.match(new_id):
                raise SystemExit(f"derived clip id '{new_id}' is not a valid clip id")
            entry = dict(clip)
            entry["clipId"] = new_id
            entry["group"] = clip["group"].replace(f"/{folder_of(clip)}/", f"/{folder}/")
            entry["bank"] = bank_name(bank_entry)
            entry["bankEntry"] = bank_entry
            entry["languageId"] = language_id
            entries.append(entry)
    return entries


def add(arguments: argparse.Namespace) -> int:
    catalog = load_json(CATALOG)
    male, female = templates(catalog, arguments.reference)
    if not male or not female:
        raise SystemExit(f"reference language '{arguments.reference}' has no crew clips to copy from")
    if len(male) != len(female):
        raise SystemExit(f"reference language '{arguments.reference}' has {len(male)} male and "
                         f"{len(female)} female clips; the sets must match")
    if arguments.folder in {folder_of(clip) for clip in crew_clips(catalog)}:
        raise SystemExit(f"folder '{arguments.folder}' is already used by another voice set")

    existing = {clip["clipId"] for clip in catalog["clips"]}
    entries = build_entries(male, female, arguments.language, arguments.reference, arguments.folder,
                            arguments.bank_entry, arguments.language_id)
    duplicates = [entry["clipId"] for entry in entries if entry["clipId"] in existing]
    if duplicates:
        raise SystemExit(f"language '{arguments.language}' is already declared: {', '.join(duplicates)}")

    if not bank_exists(Path(arguments.wot_root), arguments.bank_entry):
        raise SystemExit(f"bank '{arguments.bank_entry}' was not found in the installed client under "
                         f"{arguments.wot_root}; check the entry, not the extraction")

    profile = load_json(PROFILE)
    declared = {language["id"] for language in profile.get("languages", [])}
    declare = bool(arguments.display_name)
    if declare and arguments.language in declared:
        raise SystemExit(f"CrewVoiceProfiles.json already declares language '{arguments.language}'")
    if declare:
        profile.setdefault("languages", []).append({
            "id": arguments.language,
            "displayName": arguments.display_name,
            "wwiseLanguageId": arguments.language_id,
            "bankEntry": arguments.bank_entry,
        })

    print(f"{'would add' if arguments.dry_run else 'adding'} {len(entries)} clips from the "
          f"'{arguments.reference}' set: {len(male)} roles x 2 voices")
    for entry in entries:
        print(f"  {entry['clipId']:<44} {entry['group']:<42} {entry['variantMatch']}")
    if declare:
        print(f"{'would declare' if arguments.dry_run else 'declaring'} language "
              f"'{arguments.language}' ({arguments.display_name}) in {PROFILE.relative_to(PROJECT_ROOT)}")

    if arguments.dry_run:
        print("dry run: nothing was written")
        return 0

    insert_at = max(index for index, clip in enumerate(catalog["clips"])
                    if str(clip.get("group", "")).startswith(CREW_GROUP_PREFIX)) + 1
    catalog["clips"][insert_at:insert_at] = entries
    save_json(CATALOG, catalog)
    if declare:
        save_json(PROFILE, profile)

    print(f"catalog now declares {len(catalog['clips'])} clips")
    print("next: run Tools/WoTAudioPipeline/Scripts/Invoke-WoTAudioPipeline.ps1, then bind the language "
          "to a nation in CrewVoiceProfiles.json (language and voice) if the game has that nation")
    return 0


def remove(arguments: argparse.Namespace) -> int:
    catalog = load_json(CATALOG)
    suffix = f"_{arguments.language}_"
    doomed = [clip for clip in crew_clips(catalog)
              if clip["clipId"].endswith(suffix + "male") or clip["clipId"].endswith(suffix + "female")]
    if not doomed:
        raise SystemExit(f"no crew clips of language '{arguments.language}' are declared")

    profile = load_json(PROFILE)
    bound = [entry["nation"] for entry in profile.get("nations", [])
             if entry.get("language") == arguments.language]
    if bound:
        raise SystemExit(f"language '{arguments.language}' is bound to {', '.join(bound)}; "
                         "point those nations elsewhere before removing it")

    print(f"{'would remove' if arguments.dry_run else 'removing'} {len(doomed)} clips of "
          f"language '{arguments.language}'")
    if arguments.dry_run:
        print("dry run: nothing was written")
        return 0

    removed = {clip["clipId"] for clip in doomed}
    catalog["clips"] = [clip for clip in catalog["clips"] if clip["clipId"] not in removed]
    save_json(CATALOG, catalog)

    languages = [entry for entry in profile.get("languages", []) if entry.get("id") != arguments.language]
    if len(languages) != len(profile.get("languages", [])):
        profile["languages"] = languages
        save_json(PROFILE, profile)
        print(f"removed language '{arguments.language}' from {PROFILE.relative_to(PROJECT_ROOT)}")

    print(f"catalog now declares {len(catalog['clips'])} clips")
    print("next: run the pipeline; it deletes the WAV files the catalog no longer declares")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--language", required=True, help="language id, e.g. pl")
    parser.add_argument("--folder", help="catalog folder name, e.g. Polish (required to add)")
    parser.add_argument("--bank-entry", help="exact bank entry, e.g. poland/voiceover.bnk")
    parser.add_argument("--language-id", help="Wwise language id from the BKHD header")
    parser.add_argument("--display-name", help="declare the language in CrewVoiceProfiles.json")
    parser.add_argument("--reference", default="ru", help="already extracted set to copy from")
    parser.add_argument("--wot-root", default=r"C:\Games\World_of_Tanks_EU")
    parser.add_argument("--remove", action="store_true", help="remove the language instead of adding it")
    parser.add_argument("--dry-run", action="store_true")
    arguments = parser.parse_args()

    if arguments.remove:
        return remove(arguments)

    missing = [name for name in ("folder", "bank_entry", "language_id") if not getattr(arguments, name)]
    if missing:
        parser.error("adding requires " + ", ".join("--" + name.replace("_", "-") for name in missing))
    if not CLIP_ID_PATTERN.match(arguments.language):
        parser.error(f"language id '{arguments.language}' must be lowercase letters, digits or underscores")
    if not arguments.language_id.isdigit():
        parser.error(f"language id '{arguments.language_id}' must be the numeric Wwise language id")
    return add(arguments)


if __name__ == "__main__":
    raise SystemExit(main())
