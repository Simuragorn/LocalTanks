---
name: sync-wot-vehicle-data
description: Synchronize LocalTanks combat JSON for an already integrated vehicle with the exact top configuration of the locally installed World of Tanks client by reading scripts.pkg — armor, HP, damage, penetration, mobility, concealment, gun handling and magazine data — and regenerate the source snapshot document. Use when a vehicle was added or a WoT client version changed and the gameplay profile must be refreshed. Do not use it to add a new vehicle to the game; that is add-localtanks-tank.
---

# Sync WoT top profile into LocalTanks combat data

Read the installed client as the source and rewrite only the gameplay numbers it proves, together with the generated snapshot document.

## Sources of truth

- `Automation/pipelines/WoTDataPipeline/README.md` — maintained commands.
- `Automation/pipelines/WoTDataPipeline/Scripts/Sync-WoTTopProfiles.ps1` — extraction, module selection, conversion and `-Apply` behaviour.
- `Automation/pipelines/WoTDataPipeline/vehicle_profiles.json` — the stable LocalTanks id ↔ WoT vehicle id map plus the documented global conversion constants.
- `Docs/WOT_TOP_CONFIG_SNAPSHOT.md` — generated record of raw values, chosen modules and client version. Never hand-edit it.
- `Docs/TANK_REFERENCE.md` — historical context and every recorded deviation.
- `Docs/TANK_BALANCE.md` — generated cross-roster comparison; its freshness test must pass.
- `Docs/DECISIONS.md` — ADR-047 defines the top-configuration policy.

## Rules

- Always take the current client's last/highest chassis, turret and gun plus the highest-power compatible engine. Stock and intermediate modules are never the default and require an explicit user request.
- Never mix values from different modules or from different client versions.
- The script refuses `-Apply` when the canonical render manifest under `Assets/LocalOnly/WoTGenerated/<vehicle-id>/` was rendered from a stock or intermediate chassis, turret or gun. Resolve that mismatch first — usually by re-rendering the sprite with `render-wot-tank-sprite` — instead of forcing the profile through.
- Convert only where the scales differ, using the constants in `vehicle_profiles.json` and preserving relative differences. Store nominal plate thickness, never line-of-sight effective armour.
- A balance deviation from the WoT baseline is allowed only after comparing the vehicle with the roster in `Docs/TANK_BALANCE.md`, and it must record the raw WoT value, the final value and the reason in `Docs/TANK_REFERENCE.md`.

## Procedure

Run from the project root `C:\UnityProjects\LocalTanks`.

1. **Preview first** (no project changes):

   ```powershell
   powershell -ExecutionPolicy Bypass -File Automation/pipelines/WoTDataPipeline/Scripts/Sync-WoTTopProfiles.ps1
   ```

   Inspect the reported modules, raw values and converted values. Confirm they describe the intended vehicle and configuration. Diff the previewed `shell.speed`, `shell.maximumRange` and `shell.lifetimeSeconds` against the tracked shell JSON before using `-Apply`: until Sprint 010 P6 is complete, the pipeline still previews its old 55× visual speed divisor and 40-unit range, while tracked shells contain later manual range tuning.

2. **Apply:**

   ```powershell
   powershell -ExecutionPolicy Bypass -File Automation/pipelines/WoTDataPipeline/Scripts/Sync-WoTTopProfiles.ps1 -Apply
   ```

   `-Apply` updates the matching combat JSON, refreshes engine, gun and turret clip IDs from the audio mapping, writes magazine data when the top weapon has one, and regenerates `Docs/WOT_TOP_CONFIG_SNAPSHOT.md`. Do not apply while the ballistic preview differs from the tracked shell unless the task explicitly owns the common P6 migration or has documented the intended overwrite; never migrate one vehicle alone.

   If the task owns only WoT gun handling while P6 is pending, use `-ApplyGunHandling` instead. It updates
   only the tank `gunHandling` block and the generated snapshot, so the longer tracked shell range survives.

3. **Protect the audio block.** After applying, diff the tank JSON and verify the `audio` object still contains every field the importer requires — `engineClipId`, `tracksClipId`, `shotClipId`, `reloadClipId`, `turretClipId`, `destructionClipId` and their volume/pitch values. Re-add anything the sync dropped, or re-run `wot-tank-audio`.

4. **Rebuild Unity content.** Run `Local Tanks > Data > Reimport Combat Definitions` (or `Local Tanks > Build Combat Assets`) so ScriptableObjects, prefabs and `Docs/TANK_BALANCE.md` follow the JSON.

5. **Verify.** Edit Mode tests must pass, including the balance-document freshness test and the definition validator; the combat JSON must still pass validation with no unknown references.

## Hazards

- Applying profiles without a preview can silently replace a deliberate LocalTanks balance value.
- Until Sprint 010 P6 closes ADR-065, the tracked shells and the data-pipeline ballistic conversion intentionally disagree. This is a known project-wide migration boundary, not permission to normalize a single shell during unrelated work.
- The WoT client version is part of the evidence: record it in `Docs/WOT_TOP_CONFIG_SNAPSHOT.md` and mention it in the report. A client update can change module availability and even the top configuration.
- A tier or module that no longer matches the rendered sprite is a hard stop, not something to clamp or approximate.

## Report

Report the client version, the vehicle id, the selected chassis/turret/gun/engine, the previous and new values for every changed field, any balance deviation with its reason, whether the audio block survived, the regenerated documents, and the tests that were run.
