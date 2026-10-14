---
name: render-wot-tank-sprite
description: Render a named World of Tanks vehicle from the locally installed 3D packages into the canonical LocalTanks top-down sprite bundle (separated hull and turret layers plus layout, measured track mask and manifest sidecars) under Assets/LocalOnly/WoTGenerated, preserving model geometry and shared scale. Use when a LocalTanks tank needs new or refreshed artwork, or when an existing render manifest still records stock or intermediate modules. Do not use for an attached raster reference, and do not use it for combat JSON, prefab or roster integration — that is add-localtanks-tank.
---

# Render a WoT tank sprite

Turn an ordinary vehicle name or internal WoT vehicle ID into the canonical LocalTanks vehicle-art bundle under `Assets/LocalOnly/WoTGenerated`: hull on the left, turret with its complete gun on the right, plus layout, measured track mask and provenance sidecars. The 3D model is an intermediate source only; the game stays 2D.

## Scope

- This skill owns artwork only. It produces the sprite, module/render manifest, Unity slice layout, the measured track mask and optional diagnostic Blender scene, and stops before combat JSON, prefabs or battle rosters. Those belong to `add-localtanks-tank`.
- Unless the user explicitly asks for another configuration, always render the current client's last/highest chassis, turret and gun. Never preserve stock indices merely because an older manifest used them.
- Preserve the imported mesh, proportions, textures and one shared pixels-per-metre scale. Produce Full HD-class output at the maintained 108 output pixels per model metre. Do not stretch the naturally sized transparent sheet to a fixed 16:9 canvas, do not repaint the vehicle, and do not scale hull and turret independently.
- Do not invoke this skill for a supplied raster image; handle that separate extraction task directly.

## Sources of truth

Read these before running anything, because the implementation wins over any summary here:

- `Automation/pipelines/WoTSpritePipeline/README.md` — maintained commands, outputs and scale contract.
- `Automation/pipelines/WoTSpritePipeline/Scripts/Invoke-WoTSpritePipeline.ps1` and `Setup-WoTSpritePipeline.ps1` — real parameters and gates.
- `Automation/pipelines/WoTSpritePipeline/Blender/wot_sprite_pipeline.py` — geometry and anchor projection.
- `Automation/pipelines/WoTSpritePipeline/config.local.json` — local game and Blender paths (ignored by Git).
- `Automation/pipelines/WoTSpritePipeline/Local/Reports/` — environment, search and render evidence.

Treat `Automation/pipelines/WoTSpritePipeline` as authoritative whenever it differs from this skill. Also read the project `AGENTS.md`, inspect `git status`, and preserve unrelated work before touching anything.

## Procedure

Run every command from the project root `C:\UnityProjects\LocalTanks`.

1. **Environment.** If the environment is unproven, run:

   ```powershell
   & .\Automation\pipelines\WoTSpritePipeline\Scripts\Setup-WoTSpritePipeline.ps1
   ```

   It checks Blender 4.3, clones the pinned WoT-Blender-Toolkit revision when missing, creates an isolated Blender add-on junction, writes `config.local.json`, creates local output directories and probes the catalog headlessly without unpacking the roughly 90 GB package collection.

2. **Probe gate.** Require a successful exit and a written `Automation\pipelines\WoTSpritePipeline\Local\Reports\environment.json` with a non-zero vehicle and package count. The established installation reports 1252 vehicles and 218 packages; changed counts are informational unless scanning failed.

3. **Resolve the name.** Search before guessing an internal ID:

   ```powershell
   & .\Automation\pipelines\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 `
     -Mode Search -Query "Tiger II"
   ```

   The result is written to `Local\Reports\search.json`. Choose in this order: exact requested nation and designation; the normal unlocked vehicle rather than tutorial, story, event, IGR, FL, captured or other suffixed variants; the expected tier and class; and an explicitly requested special variant over the normal vehicle. Ordinary Tiger II resolves to `G16_PzVIB_Tiger_II`, not its `_IGR`, `_F`, `_DIT`, onboarding, story-mode, frontline or captured variants. Ask the user only when the remaining candidates are materially different requested vehicles rather than internal duplicates.

4. **Render.**

   ```powershell
   & .\Automation\pipelines\WoTSpritePipeline\Scripts\Invoke-WoTSpritePipeline.ps1 `
     -Mode Render -VehicleId "G16_PzVIB_Tiger_II"
   ```

   Optional selectors are `-Nation`, `-Tier`, `-VehicleType`, `-ChassisIndex`, `-TurretIndex`, `-GunIndex`. Omit module indices for an ordinary render: the maintained default is the last available module of each type. Use explicit indices only for a user-requested alternate visual configuration, and confirm the resulting names in the manifest. Do not describe the top game configuration as the historical production configuration without confirming it.

5. **Validation gate.** Never report success from a Blender exit code. Inspect the final PNG at original resolution and require all of the following:
   - genuine transparent corners and a transparent gap between hull and turret;
   - hull on the left, turret/gun on the right, nothing clipped or touching the canvas edge;
   - the whole barrel and muzzle brake present and straight;
   - plausible proportions for tracks, hull, turret, mantlet and gun, compared against the 3D model bounds in the manifest rather than against unrelated tank art;
   - the source front and gun pointing downward;
   - `turret.pivot` at the physical ring inside the turret crop and `hull.turretMount` at the matching ring position on the hull, both normalized inside `[0, 1]` and both rectangles fitting the texture;
   - for Unity integration, a turret rotation of at least 90° leaves the turret body centred on the ring instead of orbiting the hull centre or the mantlet — a transform-only check is not enough;
   - no missing-texture white planes, magenta materials, black blocks, detached geometry, unexpected decals, excessive glare or opaque background patches;
   - the track diagnostic image `Local\Renders\<vehicle-id>\tracks_mask_diagnostic.png` shows both bands on the tracks of the hull layer, green on the side the model's own starboard direction projects to, red on the other, the front arrow at the machine's front and no band on the hull centre;
   - `tracks.source` is `trackMeshes` for a normal vehicle, and either fallback is explained rather than accepted silently: `hullSideBands` means the whole track is hidden from above by mudguards, which is a real property of some models and not automatically a bug;
   - `tracks.mask.width/height` equal the hull rectangle of the same layout, `tracks.forwardSignV` is exactly ±1, and `tracks.starboardImageXPixels` is non-zero;
   - `git status --short --ignored` shows every source and derived output still ignored.

   The established Tiger II proof is a 769×1079 texture with a 419×802 hull and a 278×1031 turret layer at 108 output pixels per metre; with the evaluated camera it resolves to approximately `hull.turretMount = (0.49503, 0.49005)`, `turret.pivot = (0.49857, 0.77531)` and `(0.0050, 0.0192)` as the rotated Unity child offset. These numbers validate one vehicle only — read each new layout instead of reusing its rectangles.

## Geometry invariants

- Hull and turret share one camera, projection, transform and intermediate pixels-per-metre value.
- Intermediate rendering uses 256 pixels per model metre; large vehicles expand the render resolution instead of being scaled down. Composition uses 108 output pixels per metre, so relative vehicle sizes stay comparable across the roster.
- Cropping may remove transparent margins only. It must not resize a component or change its aspect ratio.
- Turret, mantlet, muzzle brake and complete barrel stay one rigid layer.
- Remove every object from Blender's factory scene before importing vehicle geometry. The default cube, camera and light are not part of the vehicle hierarchy and otherwise survive visibility filtering as opaque rectangles around small tanks.
- After creating or moving the orthographic camera, call `bpy.context.view_layer.update()` before `world_to_camera_view`. Without that evaluation Blender may project the correct 3D joint through a stale camera matrix and return plausible but wrong coordinates.
- Project the imported hull `HP_turretJoint` once and convert it into two Unity bottom-left coordinates: `turret.pivot` inside the turret crop and `hull.turretMount` inside the hull crop. Never estimate them independently.
- Unity keeps the hull sprite pivot at its geometric centre for collider alignment, so its `TurretPivot` child must be translated from the hull centre to `hull.turretMount`, including the source sprite's 180° visual rotation. Setting the child to `(0, 0)` is valid only when the mount is exactly `(0.5, 0.5)`.
- The track mask is the surface a straight top-down view can see, so it comes from one camera ray per pixel over the track bounding box, not from a renderer pass. A holdout material is not an option: EEVEE's holdout is a screen-space stencil that also cuts geometry lying below the track, which erased nearly every vehicle completely.
- The mask shares the hull camera and resolution and is cropped with the hull alpha bounds, so its size must equal the hull rectangle of the layout. Any other size silently shifts the tread highlight against the hull sprite.
- Red always marks the left track and green the right one, proved against the projected starboard direction taken from the gun. A mirrored mask changes nothing that is visible in a still image and only shows up as tracks scrolling the wrong way in a turn.
- The blue channel carries how far a masked pixel sits from its own band edge, and the overlay multiplies the moving pattern by it. Without it a wide fallback band ends in a straight edge and the effect reads as a painted rectangle; with the fade removed entirely, a narrow band loses its core.
- A tread link pitch is a visual spacing, not a measurement: at 108 output pixels per metre a literal link would be about one pixel and would strobe. Keep the default near `0.22` m and judge the result with `Local Tanks > Diagnostics > Capture Track Effect Preview`, which renders the affected machines at the battle zoom and close up with the overlay off and on.
- The final background stays transparent with no ground plane and no cast shadow outside the vehicle.

If a constant intentionally changes, update the project config example, setup defaults, README, manifest behaviour, this skill and the relevant project documentation together.

## Outputs

For `<vehicle-id>` expect:

- `Automation\pipelines\WoTSpritePipeline\Local\Renders\<vehicle-id>\hull.png`
- `Automation\pipelines\WoTSpritePipeline\Local\Renders\<vehicle-id>\turret.png`
- `Automation\pipelines\WoTSpritePipeline\Local\Renders\<vehicle-id>\tracks.png` — the raw camera-ray mask at intermediate resolution
- `Automation\pipelines\WoTSpritePipeline\Local\Renders\<vehicle-id>\tracks_mask_diagnostic.png` — human-readable proof of the track recognition
- `Automation\pipelines\WoTSpritePipeline\Local\Renders\<vehicle-id>\render_metadata.json`
- `Automation\pipelines\WoTSpritePipeline\Local\Renders\<vehicle-id>\<vehicle-id>.blend` when diagnostic saving is enabled
- `Assets\LocalOnly\WoTGenerated\<vehicle-id>\<vehicle-id>_strip2.png`
- `Assets\LocalOnly\WoTGenerated\<vehicle-id>\<vehicle-id>_layout.json`
- `Assets\LocalOnly\WoTGenerated\<vehicle-id>\<vehicle-id>_tracks_mask.png` — red left track, green right track, alpha coverage, blue lateral weight
- `Assets\LocalOnly\WoTGenerated\<vehicle-id>\<vehicle-id>_manifest.json`

The layout uses Unity bottom-left rectangle coordinates and records both anchors plus the measured `tracks` block. The manifest records the catalog entry, exact selected module names and indices, Blender version, add-on revision, bounds, scale, material list and which track meshes were recognised. Read both instead of measuring rectangles or guessing anchors by hand.

## Recovery

- Catalog scan fails: verify `res\packages\scripts.pkg`, the local config paths, Blender 4.3, the add-on junction and the pinned add-on revision before changing code.
- Import fails: inspect the Blender output and the candidate's nation, tier and type. Try an alternate catalog variant only when the selected entry is genuinely unavailable or malformed; never silently switch historical vehicles.
- A component is absent: inspect the `wot_part` hierarchy and module indices instead of substituting a hand-drawn approximation.
- Geometry is clipped: keep pixels per metre fixed and increase the square intermediate resolution or safety margin.
- Parts disagree in scale or proportion: fix the shared camera and composition math. Never compensate by resizing the turret alone.
- The turret slides or orbits: do not hand-tune the anchors. Confirm the Blender view layer was updated after camera setup, that one projected 3D ring point produced both anchors, and that Unity applied the hull-mount offset together with the source-art rotation.
- Materials are wrong: use the saved `.blend` and the manifest material list to identify the offending material, then implement a reproducible fix in the tracked Blender pipeline. Never patch only the ignored third-party clone, paint over the final PNG or hide the failure with image generation.
- The track mask is empty or nearly empty: check `render_metadata.json` → `trackMask.rayHits`. A low count for a vehicle whose side bands clearly show tracks in the hull layer means the mudguards cover the belt; leave the fallback in place. A low count together with `ambiguousObjects` means the track meshes were never recognised, so extend the name pattern instead of accepting the fallback.
- The tread highlight appears on the mudguards instead of the tracks: the mask fell back to `hullSideBands`. Either accept it and say so, or switch the vehicle back with `forceFallback: false` in `Local/Tracks/<vehicle-id>.json` and re-run the compose step.
- After any tracked pipeline change, rerun PowerShell parsing, Python compilation, a catalog probe, one full render, a project-scoped `git diff --check` and visual inspection of both the sprite and the track diagnostic.

## Local-art boundary

World of Tanks packages and every derived asset are private local test material; technical isolation does not grant redistribution rights. `Assets\LocalOnly\WoTGenerated\<vehicle-id>` is the only canonical art location, and Unity prefabs and gameplay systems reference its `_strip2.png` in place. Never copy, rename or duplicate a generated vehicle image into `Assets\Game`, another tracked art folder or a second local cache.

Keep ignored: `Assets\LocalOnly\`, `Automation\pipelines\WoTSpritePipeline\ThirdParty\`, `Automation\pipelines\WoTSpritePipeline\Local\` and `Automation\pipelines\WoTSpritePipeline\config.local.json`. Confirm with `git ls-files Assets/LocalOnly` (must stay empty).

Preserve pre-existing dirty files, especially scenes. If nothing tracked changed, create no commit. If a reproducible pipeline fix was required, document it under project conventions and commit only those tracked files as `Simuragorn <simuragorn@gmail.com>`. Never stage generated vehicle art and never push unless asked.

## Report

Show the final sprite and the track diagnostic image, and report the ordinary name, resolved internal ID, chosen chassis/turret/gun, output dimensions, canonical sprite, mask and sidecar paths, the track mask source with its band separation and tread pitch, the validation results and every remaining anomaly. State explicitly whether gameplay integration is still pending, and state the visible share of the track surface whenever a fallback mask was used.
