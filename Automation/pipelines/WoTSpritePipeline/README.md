# WoT model to LocalTanks sprite pipeline

This tooling uses a World of Tanks 3D vehicle model only as a local intermediate
and produces the two transparent layers used by LocalTanks: hull/chassis and
turret/gun. Both layers are rendered by the same orthographic camera at the same
scale. The final `_strip2.png` places the hull on the left and turret on the right.

The project skill `render-wot-tank-sprite` in
`C:\UnityProjects\LocalTanks\Automation\skills\` documents and orchestrates the complete
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
& .\Automation\pipelines\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 -Mode Probe
```

Find the internal vehicle ID from an ordinary name:

```powershell
& .\Automation\pipelines\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 `
  -Mode Search -Query "Tiger II"
```

Render a vehicle to local transparent PNG files:

```powershell
& .\Automation\pipelines\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 `
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
- `.../<vehicle id>_tracks_mask.png` — per-pixel track mask for the tread
  overlay: red marks the left track, green the right one, the alpha channel
  carries the surface coverage and the blue channel carries how far a pixel sits
  from its own band edge.
- `Local/Renders/<vehicle id>/tracks_mask_diagnostic.png` — the mask drawn over
  the hull layer with the measured band centres, the tank's front and the tread
  feature spacing, so a human can confirm the recognition without opening Unity.

Layout schema 2 projects the same 3D turret-joint point into both cropped
layers. Unity keeps the hull sprite centred for collider alignment and moves its
`TurretPivot` child to `hull.turretMount`; using the hull centre unconditionally
causes off-centre turrets to orbit when they rotate.

Layout schema 3 adds a `tracks` block measured from the model and from the mask
pixels that were actually written:

| Field | Meaning |
|---|---|
| `source` | `trackMeshes` when the model's own track meshes were found, `hullSideBands` when the safe fallback was used |
| `maskFile`, `mask` | Mask file name and its size, which must equal the hull rectangle |
| `sideSource` | `gunDirection` when the projected gun direction proved the sides, `cameraConvention` when only the camera convention was available, `meshNames` when nothing could be proved |
| `geometryVerified`, `channelsSwapped` | Whether the sides were proved, and whether the two channels had to be exchanged to match the model |
| `starboardImageXPixels` | Where the model's starboard direction projects to; this is what proves the green channel |
| `forwardSignV` | `+1` when the machine's front points towards increasing sprite V, `-1` otherwise |
| `bandSeparationPixels/Metres` | Distance between the two mask bands, used to drive the tracks as a differential |
| `bandWidthPixels/Metres` | Mean width of a masked band, for review |
| `treadPitchMetres` | Distance between two tread features; see below |
| `left`, `right`, `checks` | Per-side pixel statistics and the symmetry checks that were applied |
| `diagnostic` | Repository-relative path of the diagnostic image |
| `fallbackReason` | Present only when the mesh mask was rejected; states why |

The mask itself is drawn with a lateral weight in the blue channel, so the
overlay can fade a band out towards its own edge. The diagnostic image shows the
result for a human.

## Track mask

`tracks.png` is not a render. For every pixel of the track bounding box the
pipeline casts one ray from the camera and asks which surface it hits first, so
the mask describes the track surface a straight top-down view can actually see.

A renderer holdout looks like the obvious tool for that and is wrong here: EEVEE's
holdout is a screen-space stencil that also cuts geometry merely lying *below* the
track, so it erased nearly every vehicle completely. Ray casting answers the
question exactly and costs about one second per vehicle.

The result matters, because in a straight top-down view the mudguards of these
machines cover most of the track belt. MS-1 keeps 6.9% of its track projection
visible, the Leichttraktor 11.3% and the T1 Cunningham 10.7%; the Tiger II keeps
0.02%, so its mask is rejected and the safe side bands are used and recorded as
`hullSideBands`. Removing the Tiger II track meshes changes 19 of 118,524 sampled
pixels of its hull layer, which is the measurement behind that verdict.

`treadPitchMetres` is the distance between two tread links in model metres and
defaults to `render.trackFeaturePitchMetres` (0.22 m), which is a little coarser
than a real link on purpose: the shader draws the fine link pattern only while it
can be resolved, and at 108 pixels per metre a literal link pitch would strobe
long before that. A per-vehicle override lives in `Local/Tracks/<vehicle id>.json`:

```json
{ "swapSides": false, "forceFallback": false, "bandFraction": 0.18, "treadPitchMetres": 0.22 }
```

Every field is optional. `forceFallback` switches a machine whose track meshes are
hidden under mudguards to the outer hull bands on purpose, and `swapSides` flips
the two channels for a model whose own naming is mirrored. The pipeline records
whatever it used, so the effect is never silently different from the diagnostic
image.

The blue channel is what keeps a wide fallback band from ending in a straight
edge: the pipeline measures, per row and per side, where that band starts and
ends, and ramps the weight from 1 in its middle to 0 on the silhouette. A narrow
band keeps a solid core, because fading it across its whole width would erase it.

The Blender view layer is updated explicitly after the orthographic camera is
configured and before the joint is projected. `world_to_camera_view` may
otherwise use a stale camera matrix and produce coordinates that are inside both
sprites but do not coincide with the visible turret ring.

The pipeline intentionally stops short of registering a generated sprite in
game data. The integration step references this canonical output in place so
local derived assets cannot accidentally enter a commit or diverge into copies.
