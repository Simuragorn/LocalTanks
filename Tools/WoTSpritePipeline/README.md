# WoT model to LocalTanks sprite pipeline

This tooling uses a World of Tanks 3D vehicle model only as a local intermediate
and produces the two transparent layers used by LocalTanks: hull/chassis and
turret/gun. Both layers are rendered by the same orthographic camera at the same
scale. The final `_strip2.png` places the hull on the left and turret on the right.

The personal Codex skill `render-wot-tank-sprite` in
`C:\Users\kamidashi\.codex\skills\` documents and orchestrates the complete
name-to-sprite workflow, validation gate, recovery rules and Git boundary.

Before importing a vehicle, render mode removes Blender's factory-startup cube,
camera and light. This is required for small vehicles: otherwise the default
cube can appear as a large opaque rectangle around the separated tank layers.

## Canonical vehicle-art location

World of Tanks packages, extracted files, Blender scenes, textures, renders and
derived sprites are deliberately ignored by Git. They live under `Local/` and
`Assets/LocalOnly/`. `Assets/LocalOnly/WoTGenerated/<vehicle id>` is the only
vehicle-art source used by Unity: gameplay prefabs reference its final sprite
directly. Do not copy generated sprites into `Assets/Game/Art/Tanks` or another
folder, and do not publish them without permission from the rights holder.

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

Find the internal vehicle ID from an ordinary name:

```powershell
& .\Tools\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 `
  -Mode Search -Query "Tiger II"
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

Intermediate renders use a fixed 256 pixels per model metre. The composed
source uses 108 pixels per metre, which gives Tiger II a 1080-pixel-tall sprite
sheet and establishes Full HD-class detail for subsequent vehicles. The output
canvas keeps the natural aspect ratio required by the separated hull and turret
rather than stretching every vehicle to 1920×1080. Large vehicles increase the
square intermediate resolution automatically instead of being scaled down, so
relative vehicle dimensions remain stable.

## Outputs

- `Local/Reports/environment.json` — setup and catalog proof.
- `Local/Renders/<vehicle id>/` — full-resolution intermediate renders.
- `Assets/LocalOnly/WoTGenerated/<vehicle id>/<vehicle id>_strip2.png` — local
  Unity-ready source image.
- `.../<vehicle id>_manifest.json` — provenance, module selection and render
  settings.
- `.../<vehicle id>_layout.json` — Unity bottom-left slice rectangles and the
  two calculated turret-ring anchors: `turret.pivot` inside the turret layer
  and `hull.turretMount` at the matching position on the hull.

Layout schema 2 projects the same 3D turret-joint point into both cropped
layers. Unity keeps the hull sprite centred for collider alignment and moves its
`TurretPivot` child to `hull.turretMount`; using the hull centre unconditionally
causes off-centre turrets to orbit when they rotate.

The Blender view layer is updated explicitly after the orthographic camera is
configured and before the joint is projected. `world_to_camera_view` may
otherwise use a stale camera matrix and produce coordinates that are inside both
sprites but do not coincide with the visible turret ring.

The pipeline intentionally stops short of registering a generated sprite in
game data. The integration step references this canonical output in place so
local derived assets cannot accidentally enter a commit or diverge into copies.
