# WoT top-profile synchronization

`Sync-WoTTopProfiles.ps1` reads the current local `scripts.pkg`, always selects the last/highest chassis, turret and gun plus the highest-power compatible engine, and extracts the exact gameplay profile. Stock and intermediate modules are never the default.

Preview without changing the project:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/WoTDataPipeline/Scripts/Sync-WoTTopProfiles.ps1
```

Apply the converted values to combat JSON and regenerate `Docs/WOT_TOP_CONFIG_SNAPSHOT.md`:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/WoTDataPipeline/Scripts/Sync-WoTTopProfiles.ps1 -Apply
```

Before `-Apply`, compare the previewed shell speed, range and lifetime with the tracked shell JSON. Sprint 010 P6 has not yet migrated ballistics to ADR-065: the pipeline still computes the legacy `projectileSpeedDivisor = 55` and 40-unit range, while tracked shells contain later manual range tuning. Do not overwrite one vehicle during an unrelated sync; either preserve the tracked ballistic values or perform the common P6 migration for every shell and its tests.

The tracked `vehicle_profiles.json` contains only stable LocalTanks IDs, names and global scale conversions. Raw characteristics come from the installed WoT client. `-Apply` refuses to continue when the canonical render manifest uses a stock or intermediate chassis, turret or gun, updates matching engine/gun/turret clip IDs, and writes magazine data when the top weapon has one. After applying, run Unity's combat-data importer so ScriptableObjects, prefabs and `Docs/TANK_BALANCE.md` are rebuilt.
