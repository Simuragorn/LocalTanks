# WoT model to LocalTanks sprite pipeline

This tooling uses a World of Tanks 3D vehicle model only as a local intermediate
and produces the two transparent layers used by LocalTanks: hull/chassis and
turret/gun. Both layers are rendered by the same orthographic camera at the same
scale. The final `_strip2.png` places the hull on the left and turret on the right.

## Local-only boundary

World of Tanks packages, extracted files, Blender scenes, textures, renders and
derived sprites are deliberately ignored by Git. They live under `Local/` and
`Assets/LocalOnly/`. Do not move generated assets into a tracked folder and do
not publish them without permission from the rights holder.

## First-time setup

Run from PowerShell:

```powershell
& .\Tools\WoTSpritePipeline\Scripts\Setup-WoTSpritePipeline.ps1
```

The setup script checks Blender 4.3, clones the pinned Blender add-on when it is
missing, creates an isolated Blender add-on folder, creates `config.local.json`,
and performs a headless catalog probe. It does not unpack the roughly 90 GB
package directory.

## Commands

Environment and vehicle-catalog check:

```powershell
& .\Tools\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 -Mode Probe
```

Render a vehicle to local transparent PNG files:

```powershell
& .\Tools\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 `
  -Mode Render -VehicleId G16_PzVIB_Tiger_II
```

Optional selectors `-Nation`, `-Tier`, `-VehicleType`, `-ChassisIndex`,
`-TurretIndex`, and `-GunIndex` make a particular in-game configuration
repeatable. Indices default to the last available module, usually the top
configuration. A JSON manifest records the exact selected modules and source
tool versions beside every result.

Intermediate renders use a fixed 128 pixels per model metre. The composed
source uses 36 pixels per metre, close to the existing hand-prepared tank art.
Large vehicles increase the square intermediate resolution automatically
instead of being scaled down, so relative vehicle dimensions remain stable.

## Outputs

- `Local/Reports/environment.json` — setup and catalog proof.
- `Local/Renders/<vehicle id>/` — full-resolution intermediate renders.
- `Assets/LocalOnly/WoTGenerated/<vehicle id>/<vehicle id>_strip2.png` — local
  Unity-ready source image.
- `.../<vehicle id>_manifest.json` — provenance, module selection and render
  settings.
- `.../<vehicle id>_layout.json` — Unity bottom-left slice rectangles and the
  calculated turret-ring pivot.

The pipeline intentionally stops short of registering a generated sprite in
game data. That integration will be a separate, explicit step so local derived
assets cannot accidentally enter a commit.
