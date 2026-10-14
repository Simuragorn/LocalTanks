# WoT map to LocalTanks primitive map

This local-only pipeline converts a compiled World of Tanks arena into a
semantic 2D map that a human, an AI map composer and a future Unity importer can
all understand. It does not render or copy WoT models. It extracts only
positions, physical footprints, terrain heights, water coverage, bases and
gameplay categories.

`map_conversion_catalog.json` is the single maintained source of truth for map
selection, classification rules and sampling resolution. Generated files are
written below `Assets/LocalOnly/WoTMapPrimitives/<map-id>` and are ignored by
Git.

## First setup

The setup command installs the pinned open-source `wot-space.bin-utils` parser
into the ignored `Local/ThirdParty` directory:

```powershell
& .\Automation\pipelines\WoTMapPrimitivePipeline\Scripts\Setup-WoTMapPrimitivePipeline.ps1
```

The World of Tanks path is reused from
`Automation/pipelines/WoTSpritePipeline/config.local.json`. It can also be supplied explicitly
with `-WoTRoot`.

## Commands

```powershell
# Available map recipes
& .\Automation\pipelines\WoTMapPrimitivePipeline\Scripts\Invoke-WoTMapPrimitivePipeline.ps1 -Mode List

# Build Mines / Рудники
& .\Automation\pipelines\WoTMapPrimitivePipeline\Scripts\Invoke-WoTMapPrimitivePipeline.ps1 `
  -Mode Build -Map 10_hills

# Validate existing local output against the current schema and catalog
& .\Automation\pipelines\WoTMapPrimitivePipeline\Scripts\Invoke-WoTMapPrimitivePipeline.ps1 `
  -Mode Validate -Map 10_hills
```

## Generated contract

The authoritative `<map-id>_primitives.json` contains:

- playable bounds, bases and spawn points in WoT metres;
- a sampled elevation grid for hills and slopes;
- an exact local water mask whose gameplay contract never blocks projectiles;
- houses, rocks, bridges and obstacles reduced to rectangles or ellipses using
  their physical bounds;
- exact compact tree/bush placements plus 25-metre vegetation clusters for
  AI-level reasoning;
- a 50-metre AI analysis grid with elevation, water coverage, cover and
  concealment scores, passability and tags such as `high_ground`, `settlement`,
  `rocky_cover`, `bush_line` and `woodland`;
- destructibility, movement, projectile and visibility flags;
- source version/hash, category counts and diagnostics.

`<map-id>_preview.png` is generated from the same JSON for human review. Brown
means solid buildings and obstacles, dark brown means rocks, yellow means
destructible geometry, blue means water, dark green means trees and light green
means bushes. The preview is not a second maintained map.

No package, model, texture or binary XML is copied. The generated semantic map
still derives from World of Tanks and therefore remains local-only; do not
publish it without permission from the rights holder.
