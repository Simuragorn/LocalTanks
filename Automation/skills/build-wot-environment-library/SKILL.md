---
name: build-wot-environment-library
description: Build or extend the LocalTanks local-only top-down map-art library from the installed World of Tanks environment packages. Use when map objects or ground textures must be added, refreshed, validated or prepared for AI-assisted map composition. Do not use for tank sprites or for editing a Unity map layout by itself.
---

# Build the WoT environment library

Use `Automation/pipelines/WoTEnvironmentPipeline` as the implementation source of truth. Read
its `README.md`, `environment_catalog.json`, the project `AGENTS.md`, and inspect
Git status before changing or running the library.

The catalog is the only maintained registry. Add or change a catalog entry
instead of copying a generated PNG by hand. Build with
`Scripts/Invoke-WoTEnvironmentPipeline.ps1`, inspect every new sprite at its
original resolution and the refreshed `library_preview.png`, then run
`-Mode Validate`.

For a playable arena floor, add or update its `groundRecipes` entry and use `-Mode BuildGround -MapIds <id>`,
then `-Mode ValidateGround`. Do not copy a baked floor into Unity. The generated macro/atlas/selector manifest
is the hand-off to `WoTGroundMaterialBuilder`; rebuild the arena scene and inspect all three
`CaptureWotGroundDetailPreview` images at zoom 4, 8 and 12. A whole-map preview is not evidence of close detail.

Preserve these invariants:

- strict orthographic top-down camera with no perspective;
- the same 108 pixels per metre used by generated tanks;
- one shared lighting setup, natural aspect ratio and transparent background;
- ordinary objects are assigned semantic Unity map layers and explicit gameplay
  flags in the catalog;
- seamless terrain albedo stays at source resolution and is marked tileable;
- an arena ground recipe contains every map-specific material band and scale; the Python code contains none;
- runtime ground uses one renderer with a semantic macro image, high-pass material atlas and two-material blend
  selector; atlas data imports as linear, not sRGB;
- generated PNGs, DDS files, package contents and Blender intermediates remain
  below ignored local-only paths and never enter Git;
- unsupported WoT shaders or formats fail visibly instead of producing grey,
  white or opaque placeholder art.

Current WoT living vegetation is primarily SpeedTree `SRT 06.0.0`. Do not fake
support by changing its header to v7: the layouts differ and the maintained SRT
converter rejects the data. Until a compatible decoder exists, use only curated
ordinary BigWorld vegetation models and report the missing living-tree coverage.

When adding an object, prefer one useful silhouette and size over near-duplicate
variants. Reuse roots already present in the catalog when several models share
textures. Record `fallenVariantId` only when the target exists in the same
catalog. After tracked tooling or catalog changes, update the project status,
changelog and architecture/decision documentation as appropriate, and commit
only those scoped files as `Simuragorn <simuragorn@gmail.com>`.
