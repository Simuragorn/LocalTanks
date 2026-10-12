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
& .\Tools\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode List
```

Build every currently approved entry:

```powershell
& .\Tools\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode Build
```

Build selected entries while iterating:

```powershell
& .\Tools\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 `
  -Mode Build -Ids village_cowshed,rock_sandstone
```

Validate the generated library against the catalog:

```powershell
& .\Tools\WoTEnvironmentPipeline\Scripts\Invoke-WoTEnvironmentPipeline.ps1 -Mode Validate
```

## Output contract

- all object renders are strict orthographic top-down PNGs with transparent
  corners and the same 108 output pixels per metre used by generated tanks;
- object dimensions are natural and are never stretched to a common canvas;
- surfaces remain at their source resolution and are marked tileable;
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
