# WoT environment to LocalTanks sprite library

This tool builds a small, curated, local-only library of strict top-down
environment sprites and seamless ground textures from the installed World of
Tanks packages. The game remains 2D: source models are only Blender
intermediates.

`environment_catalog.json` is the single maintained source of truth. Each
object records its package roots, semantic map layer and gameplay flags. Each
surface records one exact DDS source. A successful build writes PNG files and
one generated `library_manifest.json` under
`Assets/LocalOnly/WoTEnvironmentGenerated` so a person or an AI map composer can
discover the same library without maintaining another registry.
Every build also refreshes `library_preview.png`, a contact sheet intended for
fast human review; it is not a second data source.

## Prerequisite

Run the existing vehicle sprite setup once:

```powershell
& .\Tools\WoTSpritePipeline\Scripts\Setup-WoTSpritePipeline.ps1
```

The environment tool intentionally reuses that pipeline's ignored local config,
pinned Blender version, BigWorld model importer and `texconv.exe`. It does not
duplicate those dependencies or unpack the whole package collection.

## Commands

List the curated catalog without extracting anything:

```powershell
& .\Automation\pipelines\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode List
```

Build every currently approved entry:

```powershell
& .\Automation\pipelines\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode Build
```

Build selected entries while iterating:

```powershell
& .\Automation\pipelines\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 `
  -Mode Build -Ids village_cowshed,rock_sandstone
```

Validate the generated library against the catalog:

```powershell
& .\Automation\pipelines\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode Validate
```

Build runtime ground for every catalogued arena (or pass one or more `-MapIds`):

```powershell
& .\Automation\pipelines\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode BuildGround -MapIds 10_hills
& .\Automation\pipelines\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode ValidateGround -MapIds 10_hills
```

Ground image processing uses Pillow. The wrapper installs its pinned compatible range into the ignored
`Local/PythonPackages` directory on first use; no global Python setup is required.

## Runtime ground recipes

`groundRecipes` in `environment_catalog.json` is the only map-specific configuration. A recipe names the
whole-arena macro surface, ordered height/material bands, selector and atlas resolutions, material repeat in
real metres, high-pass radius and default detail strength. Adding another arena must add data here, not a
branch or material name to `bake_ground_texture.py`.

`BuildGround` generates three local-only inputs and one manifest under
`Assets/LocalOnly/WoTEnvironmentGenerated/Ground`:

- the arena `global_AM` macro image preserves roads, fields, water and settlements;
- a padded high-pass atlas preserves close soil, stone and grass detail without repeating broad square stains;
- an RGB selector stores primary material, secondary material and their blend weight per pixel;
- `<map-id>_ground.json` records every path, parameter and SHA-256 used by Unity.

Unity's `WoTGroundMaterialBuilder` imports those files and creates one material for one ground renderer. The
shader repeats detail at a physical world scale and mirror-samples non-perfect source tiles, so sharpness no
longer depends on stretching one 4096 texture over the entire arena. Use `CaptureWotGroundDetailPreview` to
render zoom 4, 8 and 12 after changing a recipe; a whole-map image cannot prove close-range sharpness.

## Output contract

- all object renders are strict orthographic top-down PNGs with transparent
  corners and the same 108 output pixels per metre used by generated tanks;
- object dimensions are natural and are never stretched to a common canvas;
- surfaces remain at their source resolution and are marked tileable;
- runtime ground keeps macro layout and close material detail separate rather than baking a giant colour image;
- `library_manifest.json` records dimensions, world footprint, package/model
  provenance, SHA-256 and map semantics;
- `library_preview.png` shows every approved object and surface on one sheet;
- generated files, extracted intermediates and WoT packages remain ignored by
  Git and must not be published without permission from the rights holder.

The importer rejects known unsupported terrain-atlas shaders instead of
silently producing grey sprites. Current World of Tanks vegetation is mostly
SpeedTree `SRT 06.0.0`; the maintained Blender SRT importer targets v7 and does
not decode it. The first library therefore contains only vegetation available
as ordinary BigWorld models. Full living-tree coverage remains a separate
pipeline task.
