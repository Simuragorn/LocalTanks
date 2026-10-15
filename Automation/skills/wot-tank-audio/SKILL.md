---
name: wot-tank-audio
description: Resolve, extract and wire local World of Tanks Wwise audio for LocalTanks — vehicle effects, map music and ambience, battle flow, base capture and localized quick commands — into stable grouped clip IDs under Assets/LocalOnly/Resources/WoTAudio. Use when a tank needs an audio profile, a map or universal event set is requested, a Wwise event must be re-resolved, or the local cache must be regenerated. Do not commit extracted clips, banks or manifests.
---

# WoT audio for LocalTanks

Extract only the Wwise events the project actually names, decode them to WAV in the ignored local Unity folder, and bind them to a tank through stable clip IDs. All raw and derived audio stays out of Git.

## Sources of truth

- `Automation/pipelines/WoTAudioPipeline/README.md` — maintained commands and storage contract.
- `Automation/pipelines/WoTAudioPipeline/audio_clips.json` — the single list of named vehicle, map and universal events; this is the only place a new event is declared.
- `Automation/pipelines/WoTAudioPipeline/Scripts/Invoke-WoTAudioPipeline.ps1` — extraction, bank-version detection, TXTP generation, decoding and manifest.
- `Automation/pipelines/WoTAudioPipeline/Scripts/inspect_wot_voice.py` — proves event and switch names by hash, and enumerates every voice bank the client ships.
- `Automation/pipelines/WoTAudioPipeline/Scripts/add_crew_voice_language.py` — adds or removes a whole national crew voice set (twenty clips) and can declare the language.
- `Automation/pipelines/WoTAudioPipeline/Scripts/Setup-WoTAudioPipeline.ps1` — pinned third-party tools.
- `Automation/pipelines/WoTAudioPipeline/wwnames.txt` — Wwise name hints copied into the work bank folder.
- `Docs/WOT_MEDIA_AUDIT.md` — how module → Wwise event mapping was proven for this installation.
- `Assets/Game/GameData/Definitions/Tanks/<tank_id>.json` — the per-vehicle `audio` object that Unity consumes.
- `Assets/Game/Resources/BattleAudioProfiles.json` — the per-arena audio profile: music states, ambience and battle signals plus their levels.
- `Assets/Game/Resources/CrewVoiceProfiles.json` — nation → voice language, role → clip ID pattern, and the crew voice level. This is the only place the language of a crew line is decided.
- `Assets/Game/Runtime/WotAudioClipLibrary.cs` — the runtime resolver that turns a stable clip ID into the local WAV path through the manifest.

Read the scripts before changing anything; the repository wins over this summary.

## Current local cache

The vehicle group currently declares thirteen clips. Reuse these IDs instead of declaring duplicates whenever the selected modules genuinely match; map and universal clips are catalogued in the same file and grouped separately:

| Kind | Clip IDs |
|---|---|
| Engine | `engine_eng_gaz_aa_pc` |
| Main gun | `gun_wpn_small_pc`, `gun_wpn_automatic_pc` |
| Turret mechanism | `turret_tur_man_trav_04`, `turret_tur_man_trav_05` |
| Tracks | `tracks_trds_med_npc`, `tracks_trds_chf_npc` |
| Reload | `reload_gun_rld_20_45mm` |
| Impact | `impact_small_pierced`, `impact_small_blocked`, `impact_small_ricochet` |
| Destruction | `destruction_expl_crew_death_s_npc` |
| Arena music | `mines_music_loading`, `mines_music_battle_relaxed`, `mines_music_battle_intensive`, `mines_music_result_victory`, `mines_music_result_draw`, `mines_music_result_defeat` |
| Ambience | `mines_ambient_wind_summer` |
| Battle flow | `base_capture_progress_loop`, `battle_end_by_last_kill`, `battle_end_by_base_capture` |
| Crew voice | `<role>_<lang>_<voice>` — ten roles × five languages (`ru`, `de`, `en_gb`, `fr`, `en_us`) × two voices (`male`, `female`) = 100 clips, e.g. `crew_ricochet_de_female`, `battle_start_ru_male` |
| Quick commands | `quick_command_affirmative_ru`, `quick_command_need_help_ru`, `quick_command_supporting_ru`, `quick_command_retreat_ru`, `quick_command_thanks_ru` |

MS-1 and Leichttraktor deliberately share `engine_eng_gaz_aa_pc`; their guns and turret mechanisms stay separate. One physical WAV per event, never one per vehicle.

Every national crew bank branches each battle line on the Wwise switch `SWITCH_ext_vo_gender` (`2680972683`) with the values `SWITCH_ext_vo_gender_male` (`1963974085`) and `SWITCH_ext_vo_gender_female` (`410933746`). Those are the client's own names (`scripts/client/SoundGroups.pyc`, `CREW_GENDER_SWITCHES`) and are proven by FNV-1 hash, which is what makes the male and female take of a line two declarations of one event rather than two events. Both names are in `Automation/pipelines/WoTAudioPipeline/wwnames.txt`, so wwiser prints the branch in the TXTP file name.

## Procedure

Run from the project root `C:\UnityProjects\LocalTanks`.

1. **Resolve the module/event mapping.** Work from the same top configuration used for the sprite and the combat profile — it is recorded in the canonical manifest under `Assets/LocalOnly/WoTGenerated/<vehicle-id>/<vehicle-id>_manifest.json` and in `Docs/WOT_TOP_CONFIG_SNAPSHOT.md`. Map WoT module IDs to Wwise events using the local client (`Docs/WOT_MEDIA_AUDIT.md` records the proven method and toolchain); do not guess an event name.

2. **Declare missing events once.** Add one entry per new event to `Automation/pipelines/WoTAudioPipeline/audio_clips.json` schema 2:

   ```json
   {
     "clipId": "gun_wpn_small_pc",
     "group": "Vehicles",
     "bank": "wpn",
     "event": "wpn_small_pc",
     "variantIndex": 0,
     "usage": "oneShot"
   }
   ```

   `clipId` must match `^[a-z0-9_]+$` and encode the role. `group` controls the output folder; `Vehicles` deliberately stays flat for the runtime provider, while map and universal material uses groups such as `Mines/Music` and `Universal/CrewVoice/German/Female`. Use `bankEntry` for nested localized banks, `languageId` for their numeric Wwise language ID, `mediaEntries` for multipart media, `mediaEntriesFrom` to reuse such a list, and `trimTrailingSilenceDb` only for proven long near-silent tails. Add `mediaBank` when media lives in a different set from the event bank.

   An event that branches on a Wwise switch has one TXTP variant per switch value, and the branch is the point of the clip: a crew line is a different recording for a male and for a female crew. Such a clip declares `variantMatch` (a match against the TXTP file name, normally the switch value name) **instead of** `variantIndex`:

   ```json
   {
     "clipId": "crew_ricochet_de_female",
     "group": "Universal/CrewVoice/German/Female",
     "bank": "germany_voiceover",
     "bankEntry": "germany/voiceover.bnk",
     "languageId": "665432976",
     "event": "vo_armor_ricochet_by_player",
     "variantMatch": "SWITCH_ext_vo_gender_female",
     "trimTrailingSilenceDb": -45,
     "usage": "voice"
   }
   ```

   The pipeline requires exactly one match and fails with the list of available variants otherwise; declaring both fields is an error. `variantIndex` is what a variant order would silently redefine, so it stays for events that have no switch at all.

3. **Run the pipeline.**

   ```powershell
   powershell -ExecutionPolicy Bypass -File Automation/pipelines/WoTAudioPipeline/Scripts/Setup-WoTAudioPipeline.ps1
   powershell -ExecutionPolicy Bypass -File Automation/pipelines/WoTAudioPipeline/Scripts/Invoke-WoTAudioPipeline.ps1
   ```

   Pass `-WoTRoot <path>` when the client is not in the default location and `-Force` to re-extract banks and clips after a mapping change. The script finds the WoT packages, detects the XOR-obfuscated Wwise bank version, extracts banks and media, generates TXTP for the declared events and decodes the selected variant to WAV.

4. **Wire the tank JSON.** Every `availableInGame: true` tank requires the full audio object, including the newer tracks, reload and destruction fields:

   ```json
   "audio": {
     "engineClipId": "engine_eng_gaz_aa_pc",
     "tracksClipId": "tracks_trds_med_npc",
     "shotClipId": "gun_wpn_small_pc",
     "reloadClipId": "reload_gun_rld_20_45mm",
     "turretClipId": "turret_tur_man_trav_04",
     "destructionClipId": "destruction_expl_crew_death_s_npc",
     "engineVolume": 0.42, "tracksVolume": 0.2, "shotVolume": 0.86,
     "reloadVolume": 0.36, "turretVolume": 0.18, "destructionVolume": 0.78,
     "enginePitch": 0.94, "tracksPitch": 0.94, "shotPitch": 0.94,
     "reloadPitch": 0.94, "turretPitch": 0.96, "destructionPitch": 0.96
   }
   ```

   Volumes are `0..1`, pitches `0.25..3`. `CombatDefinitionImporter` validates every field when the block is present, so an incomplete block fails the whole definition set.

5. **Wire a map that must sound.** Arena clips are not attached to a tank; they are named in `Assets/Game/Resources/BattleAudioProfiles.json`, which the runtime reads by itself:

   ```json
   {
     "mapId": "Battle_Mines",
     "displayName": "Рудники",
     "music": { "loading": "mines_music_loading", "relaxed": "...", "intensive": "...", "victory": "...", "draw": "...", "defeat": "..." },
     "ambienceClipId": "mines_ambient_wind_summer",
     "battleStartClipId": "battle_start_ru",
     "captureProgressClipId": "base_capture_progress_loop",
     "baseCapturedClipId": "base_captured_ru",
     "battleEndByLastKillClipId": "battle_end_by_last_kill",
     "battleEndByCaptureClipId": "battle_end_by_base_capture",
     "musicVolume": 0.45, "ambienceVolume": 0.3, "signalVolume": 0.6, "voiceVolume": 0.85
   }
   ```

   `mapId` must be the battle scene name reported by the runtime tactical descriptor — `Battle_Mines`, not the source ID `10_hills`. Then run `Local Tanks > Data/Validate Battle Audio Profiles`: it derives known arena IDs from `Assets/Game/Scenes/Maps`, fails with the valid list and the undeclared clip ID when either is wrong, and runs automatically when the file changes. No scene has to be edited: `BattleAudioDirector` attaches itself to the battle director and picks the profile for the loaded map.

6. **Add a language or a voice to the crew voice.** Crew lines live in localized banks (`audioww/<nation>/voiceover.bnk`), so the same role is a different recording per language, and every bank branches each line on the gender switch, so it is also a different recording per voice. The data decides which suffixes a machine asks for: `Assets/Game/Resources/CrewVoiceProfiles.json` schema 2 maps a nation to a `language` and a `voice`, and a role to a clip ID pattern with `{lang}` and `{voice}`.

   1. Find the bank and its language ID. All eleven national banks are already verified to share the ten roles and the gender switch, so a national set is a data edit and one pipeline run; `python Automation/pipelines/WoTAudioPipeline/Scripts/inspect_wot_voice.py` prints every `voiceover.bnk` with its language ID and whether it is declared, and `--show-special` adds the 172 nested commander, event and blogger banks. Language IDs are also tabulated in `Automation/pipelines/WoTAudioPipeline/README.md`.
   2. Add the whole set with one command instead of twenty hand-written entries. It copies the roles, switch values, folder shape and trimming from an extracted set, checks that the bank exists in the client, and can declare the language in the profile:
      ```powershell
      python Automation/pipelines/WoTAudioPipeline/Scripts/add_crew_voice_language.py `
          --language pl --folder Polish --bank-entry poland/voiceover.bnk `
          --language-id 2614272111 --display-name "Польский" [--dry-run]
      ```
      Declare clips by hand only for a different shape: one clip per role **per voice**, with `variantMatch` naming the switch value rather than `variantIndex`.
      ```json
      {
        "clipId": "crew_not_penetrated_de_female",
        "group": "Universal/CrewVoice/German/Female",
        "bank": "germany_voiceover",
        "bankEntry": "germany/voiceover.bnk",
        "languageId": "665432976",
        "event": "vo_armor_not_pierced_by_player",
        "variantMatch": "SWITCH_ext_vo_gender_female",
        "trimTrailingSilenceDb": -45,
        "usage": "voice"
      }
      ```
   3. Run the pipeline, then point the nation at the new language and voice in `CrewVoiceProfiles.json`. `Local Tanks > Data/Validate Battle Audio Profiles` fails until every clip of every language and voice in use is declared, so a half-finished set cannot ship as silence. A nation the game does not have yet cannot be bound: leave the language declared and unused, which the validator accepts, and add the nation to `TankNation` only when it is a gameplay decision.
   4. Keep the `<role>_{lang}_{voice}` naming and never inline a language or a voice in a role pattern: the validator rejects a pattern without `{lang}` or without `{voice}`.
   5. Remove a set with `--language <id> --remove`; it refuses while a nation still points at the language, and the next pipeline run deletes the WAVs the catalog no longer declares.
   6. For a nested commander or blogger bank the roles may be named differently and cover only part of the battle, so its event set must be proven by hash first (dump the bank, check candidates, add the confirmed names to `wwnames.txt`) — never assume it matches a national bank. `Docs/WOT_CREW_VOICE_LIBRARY.md`, section "Как добавить ещё одну озвучку", has the full recipe and the file map.

7. **Refresh Unity.** Run `Local Tanks > Build Combat Assets` (or reimport combat definitions) so `TankAudioProfile` and the prefabs are rebuilt from JSON.

## Hazards

- **`-Force` is required when an event mapping changes.** Without it, existing TXTP folders and WAV files are reused and the manifest keeps the old selection.
- **Never guess an event name; prove it by hash.** A bank stores only hashed names. Collect candidate names from the client's own packed XML (`scripts.pkg` — arena definitions and `item_defs/hints/battle_hints.xml` reference them as plain strings), then require the FNV-1 32-bit hash of the name to appear as a `CAkEvent` id in a wwiser dump. The same proof applies to switch names: `SWITCH_ext_vo_gender` and its two values are hashes of `ulGroupID`/`ulSwitchID`, and their spelling comes from the client's own `scripts/client/SoundGroups.pyc`. `Scripts/inspect_wot_voice.py` verifies both kinds of names, prints the language IDs of every nation's voice bank and reports which hints in `wwnames.txt` this dump proves; a name that is only a game hint identifier (for example `vo_hint_*`-style battle event ids) will not be in the bank, and registering it produces no clip at all.
- **An event name also has to be in `wwnames.txt`.** `wwiser -gf <event>` resolves the name through the name list; adding the clip to `audio_clips.json` without the hint name can produce no TXTP variants even though the hash exists. Switch value names belong there too — that is what makes the extracted branch readable in the TXTP file name, and therefore in the manifest.
- **Crew voice needs pacing, not just clips.** An automatic gun lands a dozen hits in seconds, so `CrewVoiceProfiles.json` carries `repeatCooldownSeconds` (the same phrase is skipped while it is still recent) and `speechCooldownSeconds` (the crew never talks over itself); losing the player's own machine ignores both. Never remove that pacing to "make the lines play", and keep the counters in `CrewVoiceController` so a spam complaint can be answered with numbers from F6.
- **A switched event needs `variantMatch`, not `variantIndex`.** Every crew line has two branches, and which one a clip wants is the clip. `variantIndex` selects by position in a sorted list, so an added or renamed branch silently swaps the male and female takes; `variantMatch` names the switch value and the pipeline requires exactly one match. Declaring both fields is an error. Record the chosen variant and its count in the manifest for reproducibility.
- **Bank-version detection uses a candidate list.** If it throws after a WoT client update, the client's bank version appeared outside the known candidates; extend the list deliberately rather than guessing.
- **`mediaBank` changes where media are extracted and decoded from.** `mediaEntries` may list multiple PCK parts; they are merged with hard links rather than copied. Nested localized banks also require their exact `languageId` or wwiser may silently select no usable event.
- **Some Wwise events are controls, not audio.** Do not register a generated WAV that is digital silence; for example `base_capture_2` in the current client is a stop/control event and is intentionally omitted.
- **A committed prefab must never hold a local `AudioClip` GUID.** `TankAudioController` resolves clips by stable ID through `ITankAudioClipProvider` at runtime. Missing local clips must degrade to silence, never to broken references.
- **Runtime resolution goes through the manifest, never through a path written in code.** `WotAudioClipLibrary` loads `Assets/LocalOnly/Resources/WoTAudio/audio_manifest.json` and follows each entry's `relativePath`, because map and universal clips live in group folders while vehicle clips stayed flat. An ID the manifest does not know falls back to the flat `Clips/<clipId>` path, and an absent manifest is one warning plus silence. Declaring the clip and running the pipeline must be enough — if you are about to hardcode a clip path in a controller, the manifest entry is what is actually missing.
- **The player has five buses, and arena audio uses two of them.** `PlayerSettingsStore` exposes `Master`, `Tank`, `Effects`, `Music` (music and ambience) and `Voice` (radio lines and battle signals). Arena clips must never scale with the effects multiplier, or the player cannot silence the guns without losing the capture warning.
- **`base_capture_1` is a loop, `base_capture_2` is a control event.** The runtime starts the loop and stops it when no base is being captured; do not register `base_capture_2` as a clip and do not wait for a stop clip to end the loop.
- **`TankAudioController.LoadClips` loads every configured ID.** An empty or missing clip ID reaching the provider is an error in tests with strict fake providers; keep the JSON block complete and consistent.

## Validation gate

1. The pipeline exits with code 0 and reports the detected bank version and generated clip count. Its native tools write to `LocalData/WoTAudioPipeline/Work/*.log`; only the exit code decides the outcome, because Windows PowerShell turns a chatty tool's error stream into an error of the pipeline.
2. `Assets/LocalOnly/Resources/WoTAudio/audio_manifest.json` lists every declared clip with `group`, `relativePath`, `variantCount`, selected TXTP, `variantMatch` and SHA-256; every file exists and matches its hash, no WAV in `Clips/` is missing from the manifest, and the file has no byte-order mark because `JsonUtility` reads it.
3. Every clip ID referenced by any tank JSON resolves to a manifest entry and a WAV file, and every clip ID referenced by an arena profile or by a crew voice language in use is declared in `audio_clips.json`. `Local Tanks > Data/Validate Battle Audio Profiles` reports no error and `BattleAudioProfileTests` passes; an unknown `mapId` is reported with the list of known arenas, and a nation pointed at a language without declared clips is reported with the languages that do exist.
4. Reimporting combat definitions succeeds; the Edit Mode audio definition tests pass.
5. Play Mode confirms the engine and gun sources load and react to an actual shot (see `TankAudioPlayModeTests`).
6. `git ls-files Assets/LocalOnly LocalData` is empty and the prefab YAML contains no `Assets/LocalOnly` GUID.

## Local-only boundary

Keep out of Git: extracted and generated clips, their `.meta` files, `audio_manifest.json`, extracted `.bnk`/`.pck`, TXTP folders, third-party binaries under `LocalData/WoTAudioPipeline`, and the audio work cache. Only tooling, `audio_clips.json`, schemas, documentation and the tank JSON profile may be committed, as `Simuragorn <simuragorn@gmail.com>`, preserving unrelated dirty files.

If an exact event cannot be resolved, keep the discovered module/event mapping in the report, leave the audio profile explicitly marked incomplete, and never substitute an unrelated sound. A deliberate class/calibre fallback is allowed only when it is documented with its reason.

## Report

Report the resolved events per role, added `audio_clips.json` entries, bank version, clip IDs and WAV paths, manifest hash summary, tank JSON changes, Unity verification results, and every unresolved or approximated mapping.
