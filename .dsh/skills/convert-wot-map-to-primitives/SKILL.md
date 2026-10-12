---
name: convert-wot-map-to-primitives
description: Convert a named map from the user's locally installed World of Tanks client into a LocalTanks local-only semantic 2D primitive map with terrain, water, bases, houses, rocks, obstacles, trees and bushes. Use when importing, rebuilding, validating or extending a repeatable WoT arena layout conversion. Do not use for rendering environment art or manually editing an existing LocalTanks battle scene.
---

# Convert a WoT arena to primitive map data

Use `Tools/WoTMapPrimitivePipeline` as the implementation source of truth. Read
its complete `README.md`, `map_conversion_catalog.json`, project `AGENTS.md` and
inspect Git status before running or modifying the pipeline.

For an existing recipe, run `Scripts/Invoke-WoTMapPrimitivePipeline.ps1` in
`Build` mode, inspect the generated JSON statistics and preview, then run
`Validate`. For a new arena, add one map entry to the catalog instead of copying
or hand-editing generated output. Add or refine ordered classification rules in
the same catalog when source resource names introduce new semantic categories.

Preserve these invariants:

- generated JSON is the only authoritative converted map; previews and reports
  are derived from it;
- coordinates remain in source metres with explicit `x/z` axes and playable
  bounds;
- exact placements remain available for a deterministic importer, while
  clustered summaries and the 50-metre analysis grid remain small enough for
  AI reasoning;
- houses, rocks, obstacles, bridges, bushes, trees, water, terrain and both
  teams' bases must pass validation;
- water affects route planning but never blocks projectiles or vision;
- standing trees are destructible obstacles and their future fallen state is
  bush-like concealment;
- object category, destructibility and collision flags come from catalog rules
  and parsed physics metadata, not from preview colors;
- package bytes, models, textures, packed XML, third-party checkouts and all
  generated WoT-derived outputs remain under ignored local-only paths;
- never publish or stage WoT-derived output without explicit rights clearance.

The setup script pins `wot-space.bin-utils`; do not silently float its revision.
If a newer game client stops parsing, fail with a clear diagnostic, verify a
new parser revision separately, update `Docs/DEPENDENCIES.md`, then change the
pin. After tracked tooling or catalog changes, update project status/changelog
and commit only scoped files as `Simuragorn <simuragorn@gmail.com>`.
