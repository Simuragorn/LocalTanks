---
name: wot-tank-audio
description: Resolve, extract and wire local World of Tanks Wwise audio for a LocalTanks tank — engine, main gun, turret mechanism, tracks, reload, hit impacts and destruction — into stable clip IDs under Assets/LocalOnly/Resources/WoTAudio and the tank JSON audio profile, so TankAudioController can load them by ID at runtime. Use when a tank needs an audio profile, when a new Wwise event must be added or re-resolved, or when the local audio cache must be regenerated. Do not commit extracted clips, banks or manifests.
whenToUse: Use when adding or repairing sound for a vehicle whose gameplay parameters already exist or are being added in the same task; skip it when only sprite art or combat numbers change.
---

# WoT tank audio for LocalTanks

Extract only the Wwise events the project actually names, decode them to WAV in the ignored local Unity folder, and bind them to a tank through stable clip IDs. All raw and derived audio stays out of Git.

## Sources of truth

- `Tools/WoTAudioPipeline/README.md` — maintained commands and storage contract.
- `Tools/WoTAudioPipeline/audio_clips.json` — the single list of named events; this is the only place a new event is declared.
- `Tools/WoTAudioPipeline/Scripts/Invoke-WoTAudioPipeline.ps1` — extraction, bank-version detection, TXTP generation, decoding and manifest.
- `Tools/WoTAudioPipeline/Scripts/Setup-WoTAudioPipeline.ps1` — pinned third-party tools.
- `Tools/WoTAudioPipeline/wwnames.txt` — Wwise name hints copied into the work bank folder.
- `Docs/WOT_MEDIA_AUDIT.md` — how module → Wwise event mapping was proven for this installation.
- `Assets/Game/GameData/Definitions/Tanks/<tank_id>.json` — the per-vehicle `audio` object that Unity consumes.

Read the scripts before changing anything; the repository wins over this summary.

## Current local cache

`audio_clips.json` presently declares twelve clips. Reuse these IDs instead of declaring duplicates whenever the selected modules genuinely match:

| Kind | Clip IDs |
|---|---|
| Engine | `engine_eng_gaz_aa_pc` |
| Main gun | `gun_wpn_small_pc`, `gun_wpn_automatic_pc` |
| Turret mechanism | `turret_tur_man_trav_04`, `turret_tur_man_trav_05` |
| Tracks | `tracks_trds_med_npc`, `tracks_trds_chf_npc` |
| Reload | `reload_gun_rld_20_45mm` |
| Impact | `impact_small_pierced`, `impact_small_blocked`, `impact_small_ricochet` |
| Destruction | `destruction_expl_crew_death_s_npc` |

MS-1 and Leichttraktor deliberately share `engine_eng_gaz_aa_pc`; their guns and turret mechanisms stay separate. One physical WAV per event, never one per vehicle.

## Procedure

Run from the project root `C:\UnityProjects\LocalTanks`.

1. **Resolve the module/event mapping.** Work from the same top configuration used for the sprite and the combat profile — it is recorded in the canonical manifest under `Assets/LocalOnly/WoTGenerated/<vehicle-id>/<vehicle-id>_manifest.json` and in `Docs/WOT_TOP_CONFIG_SNAPSHOT.md`. Map WoT module IDs to Wwise events using the local client (`Docs/WOT_MEDIA_AUDIT.md` records the proven method and toolchain); do not guess an event name.

2. **Declare missing events once.** Add one entry per new event to `Tools/WoTAudioPipeline/audio_clips.json`:

   ```json
   {
     "clipId": "gun_wpn_small_pc",
     "bank": "wpn",
     "event": "wpn_small_pc",
     "variantIndex": 0,
     "usage": "oneShot"
   }
   ```

   `clipId` must match `^[a-z0-9_]+$` and encode the kind and event, so a shared engine or gun class is obvious from the ID. `usage` is `loop` or `oneShot`. Add `"mediaBank": "<bank>"` only when the media lives in a different bank than the event, as `destruction_expl_crew_death_s_npc` does with `streams`.

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

5. **Refresh Unity.** Run `Local Tanks > Build Combat Assets` (or reimport combat definitions) so `TankAudioProfile` and the prefabs are rebuilt from JSON.

## Hazards

- **`-Force` is required when an event mapping changes.** Without it, existing TXTP folders and WAV files are reused and the manifest keeps the old selection.
- **Variants differ per event.** `variantIndex` selects one decoded variant out of however many the event produced; the pipeline fails loudly when the requested index does not exist. Record the chosen variant and its count in the manifest for reproducibility.
- **Bank-version detection uses a candidate list.** If it throws after a WoT client update, the client's bank version appeared outside the known candidates; extend the list deliberately rather than guessing.
- **`mediaBank` changes where media are extracted and decoded from.** Setting it requires the corresponding `.pck`, which the script extracts with QuickBMS.
- **A committed prefab must never hold a local `AudioClip` GUID.** `TankAudioController` resolves clips by stable ID through `ITankAudioClipProvider` at runtime. Missing local clips must degrade to silence, never to broken references.
- **`TankAudioController.LoadClips` loads every configured ID.** An empty or missing clip ID reaching the provider is an error in tests with strict fake providers; keep the JSON block complete and consistent.

## Validation gate

1. The pipeline exits successfully and reports the detected bank version and generated clip count.
2. `Assets/LocalOnly/Resources/WoTAudio/audio_manifest.json` lists every declared clip with `variantCount`, selected TXTP and SHA-256; each `Clips/<clipId>.wav` exists and matches its hash.
3. Every clip ID referenced by any tank JSON resolves to a manifest entry and a WAV file.
4. Reimporting combat definitions succeeds; the Edit Mode audio definition tests pass.
5. Play Mode confirms the engine and gun sources load and react to an actual shot (see `TankAudioPlayModeTests`).
6. `git ls-files Assets/LocalOnly LocalData` is empty and the prefab YAML contains no `Assets/LocalOnly` GUID.

## Local-only boundary

Keep out of Git: extracted and generated clips, their `.meta` files, `audio_manifest.json`, extracted `.bnk`/`.pck`, TXTP folders, third-party binaries under `LocalData/WoTAudioPipeline`, and the audio work cache. Only tooling, `audio_clips.json`, schemas, documentation and the tank JSON profile may be committed, as `Simuragorn <simuragorn@gmail.com>`, preserving unrelated dirty files.

If an exact event cannot be resolved, keep the discovered module/event mapping in the report, leave the audio profile explicitly marked incomplete, and never substitute an unrelated sound. A deliberate class/calibre fallback is allowed only when it is documented with its reason.

## Report

Report the resolved events per role, added `audio_clips.json` entries, bank version, clip IDs and WAV paths, manifest hash summary, tank JSON changes, Unity verification results, and every unresolved or approximated mapping.
