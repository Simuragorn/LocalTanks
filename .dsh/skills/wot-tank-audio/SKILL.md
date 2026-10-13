---
name: wot-tank-audio
description: Resolve, extract and wire local World of Tanks Wwise audio for LocalTanks — vehicle effects, map music and ambience, battle flow, base capture and localized quick commands — into stable grouped clip IDs under Assets/LocalOnly/Resources/WoTAudio. Use when a tank needs an audio profile, a map or universal event set is requested, a Wwise event must be re-resolved, or the local cache must be regenerated. Do not commit extracted clips, banks or manifests.
whenToUse: Use for any WoT Wwise extraction or integration task in LocalTanks; skip it when only sprite art or combat numbers change.
---

# WoT audio for LocalTanks

Extract only the Wwise events the project actually names, decode them to WAV in the ignored local Unity folder, and bind them to a tank through stable clip IDs. All raw and derived audio stays out of Git.

## Sources of truth

- `Tools/WoTAudioPipeline/README.md` — maintained commands and storage contract.
- `Tools/WoTAudioPipeline/audio_clips.json` — the single list of named vehicle, map and universal events; this is the only place a new event is declared.
- `Tools/WoTAudioPipeline/Scripts/Invoke-WoTAudioPipeline.ps1` — extraction, bank-version detection, TXTP generation, decoding and manifest.
- `Tools/WoTAudioPipeline/Scripts/Setup-WoTAudioPipeline.ps1` — pinned third-party tools.
- `Tools/WoTAudioPipeline/wwnames.txt` — Wwise name hints copied into the work bank folder.
- `Docs/WOT_MEDIA_AUDIT.md` — how module → Wwise event mapping was proven for this installation.
- `Assets/Game/GameData/Definitions/Tanks/<tank_id>.json` — the per-vehicle `audio` object that Unity consumes.
- `Assets/Game/Resources/BattleAudioProfiles.json` — the per-arena audio profile: music states, ambience, battle flow and voice clips plus their levels.
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
| Battle flow | `battle_start_ru`, `base_capture_progress_loop`, `base_captured_ru`, `battle_end_by_last_kill`, `battle_end_by_base_capture` |
| Quick commands | `quick_command_affirmative_ru`, `quick_command_need_help_ru`, `quick_command_supporting_ru`, `quick_command_retreat_ru`, `quick_command_thanks_ru` |

MS-1 and Leichttraktor deliberately share `engine_eng_gaz_aa_pc`; their guns and turret mechanisms stay separate. One physical WAV per event, never one per vehicle.

## Procedure

Run from the project root `C:\UnityProjects\LocalTanks`.

1. **Resolve the module/event mapping.** Work from the same top configuration used for the sprite and the combat profile — it is recorded in the canonical manifest under `Assets/LocalOnly/WoTGenerated/<vehicle-id>/<vehicle-id>_manifest.json` and in `Docs/WOT_TOP_CONFIG_SNAPSHOT.md`. Map WoT module IDs to Wwise events using the local client (`Docs/WOT_MEDIA_AUDIT.md` records the proven method and toolchain); do not guess an event name.

2. **Declare missing events once.** Add one entry per new event to `Tools/WoTAudioPipeline/audio_clips.json` schema 2:

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

   `clipId` must match `^[a-z0-9_]+$` and encode the role. `group` controls the output folder; `Vehicles` deliberately stays flat for the runtime provider, while map and universal material uses groups such as `Mines/Music` and `Universal/QuickCommands/Russian`. Use `bankEntry` for nested localized banks, `languageId` for their numeric Wwise language ID, `mediaEntries` for multipart media, `mediaEntriesFrom` to reuse such a list, and `trimTrailingSilenceDb` only for proven long near-silent tails. Add `mediaBank` when media lives in a different set from the event bank.

3. **Run the pipeline.**

   ```powershell
   powershell -ExecutionPolicy Bypass -File Tools/WoTAudioPipeline/Scripts/Setup-WoTAudioPipeline.ps1
   powershell -ExecutionPolicy Bypass -File Tools/WoTAudioPipeline/Scripts/Invoke-WoTAudioPipeline.ps1
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
     "mapId": "10_hills_tactical",
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

   `mapId` must be the ID the tactical descriptor reports — `10_hills_tactical`, not `10_hills`. Then run `Local Tanks > Data/Validate Battle Audio Profiles`: it fails with the list of known arena IDs and the undeclared clip ID when either is wrong, and the same check runs automatically when the file changes. No scene has to be edited: `BattleAudioDirector` attaches itself to the battle director and picks the profile for the loaded map.

6. **Refresh Unity.** Run `Local Tanks > Build Combat Assets` (or reimport combat definitions) so `TankAudioProfile` and the prefabs are rebuilt from JSON.

## Hazards

- **`-Force` is required when an event mapping changes.** Without it, existing TXTP folders and WAV files are reused and the manifest keeps the old selection.
- **Variants differ per event.** `variantIndex` selects one decoded variant out of however many the event produced; the pipeline fails loudly when the requested index does not exist. Record the chosen variant and its count in the manifest for reproducibility.
- **Bank-version detection uses a candidate list.** If it throws after a WoT client update, the client's bank version appeared outside the known candidates; extend the list deliberately rather than guessing.
- **`mediaBank` changes where media are extracted and decoded from.** `mediaEntries` may list multiple PCK parts; they are merged with hard links rather than copied. Nested localized banks also require their exact `languageId` or wwiser may silently select no usable event.
- **Some Wwise events are controls, not audio.** Do not register a generated WAV that is digital silence; for example `base_capture_2` in the current client is a stop/control event and is intentionally omitted.
- **A committed prefab must never hold a local `AudioClip` GUID.** `TankAudioController` resolves clips by stable ID through `ITankAudioClipProvider` at runtime. Missing local clips must degrade to silence, never to broken references.
- **Runtime resolution goes through the manifest, never through a path written in code.** `WotAudioClipLibrary` loads `Assets/LocalOnly/Resources/WoTAudio/audio_manifest.json` and follows each entry's `relativePath`, because map and universal clips live in group folders while vehicle clips stayed flat. An ID the manifest does not know falls back to the flat `Clips/<clipId>` path, and an absent manifest is one warning plus silence. Declaring the clip and running the pipeline must be enough — if you are about to hardcode a clip path in a controller, the manifest entry is what is actually missing.
- **The player has five buses, and arena audio uses two of them.** `PlayerSettingsStore` exposes `Master`, `Tank`, `Effects`, `Music` (music and ambience) and `Voice` (radio lines and battle signals). Arena clips must never scale with the effects multiplier, or the player cannot silence the guns without losing the capture warning.
- **`base_capture_1` is a loop, `base_capture_2` is a control event.** The runtime starts the loop and stops it when no base is being captured; do not register `base_capture_2` as a clip and do not wait for a stop clip to end the loop.
- **`TankAudioController.LoadClips` loads every configured ID.** An empty or missing clip ID reaching the provider is an error in tests with strict fake providers; keep the JSON block complete and consistent.

## Validation gate

1. The pipeline exits successfully and reports the detected bank version and generated clip count.
2. `Assets/LocalOnly/Resources/WoTAudio/audio_manifest.json` lists every declared clip with `group`, `relativePath`, `variantCount`, selected TXTP and SHA-256; every file exists and matches its hash.
3. Every clip ID referenced by any tank JSON resolves to a manifest entry and a WAV file, and every clip ID referenced by an arena profile is declared in `audio_clips.json`. `Local Tanks > Data/Validate Battle Audio Profiles` reports no error and `BattleAudioProfileTests` passes; an unknown `mapId` is reported with the list of known arenas.
4. Reimporting combat definitions succeeds; the Edit Mode audio definition tests pass.
5. Play Mode confirms the engine and gun sources load and react to an actual shot (see `TankAudioPlayModeTests`).
6. `git ls-files Assets/LocalOnly LocalData` is empty and the prefab YAML contains no `Assets/LocalOnly` GUID.

## Local-only boundary

Keep out of Git: extracted and generated clips, their `.meta` files, `audio_manifest.json`, extracted `.bnk`/`.pck`, TXTP folders, third-party binaries under `LocalData/WoTAudioPipeline`, and the audio work cache. Only tooling, `audio_clips.json`, schemas, documentation and the tank JSON profile may be committed, as `Simuragorn <simuragorn@gmail.com>`, preserving unrelated dirty files.

If an exact event cannot be resolved, keep the discovered module/event mapping in the report, leave the audio profile explicitly marked incomplete, and never substitute an unrelated sound. A deliberate class/calibre fallback is allowed only when it is documented with its reason.

## Report

Report the resolved events per role, added `audio_clips.json` entries, bank version, clip IDs and WAV paths, manifest hash summary, tank JSON changes, Unity verification results, and every unresolved or approximated mapping.
