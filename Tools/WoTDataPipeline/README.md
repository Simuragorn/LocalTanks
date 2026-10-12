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

The tracked `vehicle_profiles.json` contains only stable LocalTanks IDs, names and global scale conversions. Raw characteristics come from the installed WoT client. `-Apply` refuses to continue when the canonical render manifest uses a stock or intermediate chassis, turret or gun, updates matching engine/gun/turret clip IDs, and writes magazine data when the top weapon has one. After applying, run Unity's combat-data importer so ScriptableObjects, prefabs and `Docs/TANK_BALANCE.md` are rebuilt.
