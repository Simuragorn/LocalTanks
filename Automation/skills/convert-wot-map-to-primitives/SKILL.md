---
name: convert-wot-map-to-primitives
description: Convert a named map from the user's locally installed World of Tanks client into a LocalTanks local-only semantic 2D primitive map with terrain, water, bases, houses, rocks, obstacles, trees and bushes. Use when importing, rebuilding, validating or extending a repeatable WoT arena layout conversion. Do not use for rendering environment art or manually editing an existing LocalTanks battle scene.
---

# Convert a WoT arena to primitive map data

Use `Automation/pipelines/WoTMapPrimitivePipeline` as the implementation source of truth. Read
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
- **vegetation never blocks movement or projectiles.** A tree is cover that
  conceals, and knocked down it is bush-like concealment; its `destructible` flag
  is for a future wreck or crushing rule and must not decide collision;
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

## Turning the converted map into a Unity arena

The JSON above is only half the work. `Docs/ARCHITECTURE.md` describes the Unity
side; the rules below were each learned from a defect that reached a playable
scene, so treat them as contracts rather than suggestions. Every one now has a
test in `Assets/Game/Tests`, because the faults were invisible to the compiler and
to each other.

## The arena floor

The ground is **one renderer with a layered material**, not a tilemap, sprite stamps or one
giant baked image. `global_AM.dds` remains the macro layer because it owns roads, fields,
water and settlements. Close sharpness comes from a separate high-pass atlas of the map's
own terrain materials, repeated at a physical metre scale by `LocalTanks/WoTGroundDetail`.

Every map-specific choice lives in `Automation/pipelines/WoTEnvironmentPipeline/environment_catalog.json`
under `groundRecipes`: macro surface, ordered height/material bands, transition width,
resolutions, repeat metres, high-pass radius and strength. The script must contain no Mines
names or thresholds. Run `BuildGround` and `ValidateGround` after the environment library,
then rebuild the scene. The generated RGB selector stores two material indices and their
blend weight; random per-pixel selection is forbidden because its cells become a visible
checkerboard at close zoom. Bilinear sampling is used while deriving the selector, while
the selector itself remains point-sampled so material indices cannot interpolate into
unrelated IDs.

Do not return to sprite stamps: repeated quads reveal their edges and cost thousands of
objects. Do not import the detail atlas as sRGB: its neutral byte value 128 is multiplicative
data, and gamma decoding would darken the whole arena. High-pass filtering removes broad
stains before repetition, padded atlas cells prevent mip bleeding, and mirror sampling makes
imperfect source tiles meet without seams. The result remains one draw and needs no 8K/16K
colour texture.

Two artefacts are already fixed and must not come back:

- **the ground must never be a tilemap of navigation cells.** A four-metre cell is about
  fifteen pixels of baked ground, so drawn over it the wash reads as a field of small
  dark squares. It sits behind `ShowNavigationOverlay`, off by default;
- **a BC3 source always carries an alpha channel** and texconv preserves it, so a terrain
  PNG must be flattened to opaque or the sprite is multiplied by that channel and the map
  renders dark. `flatten_alpha` in the environment pipeline does this for every surface;
  compositing over an opaque colour is not a substitute, it darkens or tints the result.

`GroundAppearanceTuner` changes `_DetailStrength` through a property block, so one generated
material stays shared and a scene does not create an accidental copy.

## Seeing the arena

Evidence about the ground comes from a rendered frame, not from scene data. There is an
operation for it, and it declares `requiresGraphics` because a null graphics device
discards every draw call:

```
./Tools/Unity/Invoke-UnityBackground.ps1 -Operation CaptureWotMapPreview -Run
```

It writes `Docs/Media/WoTMapArena.png`. Read that file and judge the result from it. The
same applies to any change to materials, shaders, transparency or the ground: a headless
run can prove data and cannot prove appearance.

For ground sharpness also run `CaptureWotGroundDetailPreview`. It writes zoom 4, 8 and 12
frames under `TestResults/WoTGroundDetail`; inspect all three. The whole-map preview
downsamples enough to hide an undersized or blurred texture.

## One predicate decides what counts as a barrier

`WoTMapAsset.IsSubstantial` is the single answer to "is this object large enough to
be a wall", used by collision (`IsSolid`), routing (`BlocksNavigation`) and the art
pass. Two thresholds once disagreed, so an object could be solid to a tank and
simultaneously crossable by a route, and the map looked arbitrary.

- the threshold is `BlockerMetres`, ten source metres: about two and a half hull
  lengths of the smallest machine in the game (the T1 Cunningham is 3.87 m);
- destructibility is **not** part of it. Tying solidity to it made a fifth of the
  houses on Mines drive-through while their identical neighbours were not;
- only obstacles at or above the threshold are drawn. A drawn object implies a
  wall, so the map shows exactly what a tank cannot cross; roughly fifteen hundred
  small solid objects on Mines are omitted deliberately.

## Colliders that must never stop a tank

- **Capture zones are triggers.** `CaptureBase` carries
  `[RequireComponent(typeof(CircleCollider2D))]`, and Unity creates a required
  collider **solid**, the size of the whole base, exactly where the sides spawn.
  Add the `CircleCollider2D` yourself with `isTrigger = true` before adding
  `CaptureBase`, and take its radius from the same constant as the drawn ring. A
  missing trigger trapped tanks inside their own spawn and read as broken AI.
- **Vegetation colliders are triggers**, and each plant carries a
  `ConcealmentZone`, which also fades the sprite as the player drives close so
  cover never hides the player's own machine.
- `CaptureBase` warns at load when its zone is solid; keep that warning.

## Sprite families never mix

The gallery manifest's own `category` is the authority on what a sprite depicts,
not a resource path. Obstacles draw only from buildings, rocks and destructible
cover; plants only from vegetation. `MapSpriteMatcher.OfFamily` performs the split
for both the scene builder and the content builder so the two cannot drift. Without
it a burnt tree stood on a boulder, promising an obstacle the art did not show. An
unclassified family counts as a structure, so a new library object appears on a
wall until someone classifies it instead of vanishing silently.

## Routes must be walkable and outrank the strategic layer

- `SceneNavigationBaker` runs before tanks spawn and re-rasterizes the navigation obstacle mask from
  the loaded scene's actual solid `Collider2D` geometry. Ignore triggers, vegetation, capture zones and
  dynamic bodies. The imported map supplies grid bounds and ground types, not the final position of walls;
  moving or deleting a rock in a generated scene must therefore need no manual navigation edit;
- treat generated lane paths as tactical hints. At runtime retain a small set of anchors, move a blocked
  anchor to nearby walkable ground, prove every leg with the current grid and let `NavigationAgent` build
  the detailed A* path. Never serialize a second hand-maintained route for a moved obstacle;
- drop route points whose navigation cell is closed and orient the lane by geometry
  from the team's own base; assuming the direction sends a team across the map from
  the enemy's side;
- route advance uses the navigation agent's own waypoint tolerance
  (`NavigationAgent.WaypointTolerance`), never a smaller constant, and skips points
  the tank already stands on. A route starting on the spawn point otherwise offers
  a destination the tank cannot satisfy, and it circles there for the whole battle
  while reporting a valid path;
- a route is a mission, so `ExecuteStrategicDirective` must not take the decision
  while a route is unfinished and no enemy has been detected. When both acted
  freely, the strategic layer overrode the route 240 times in 360 decisions and the
  two deadlocked, leaving bots motionless with a valid path.

## Verify by playing, and measure rather than reason

Every defect above was found by measurement, none by reading code. The arena checks
in `WoTMapScenePlayModeTests` load the scene and assert that no collider under
`Vegetation` is solid, that each plant conceals, and that no capture zone stops a
tank. `WoTMapPlayabilityTests` fights a battle and prints per-tank pace, route
index, path shape and stall recoveries; keep those diagnostics, because they are
what separated a routing fault from a collider fault after several wrong guesses.

Two habits worth keeping:

- **never edit non-ASCII source through PowerShell.** A round trip through
  `Set-Content` read the file as Windows-1251 and turned one short Russian label
  into nine thousand mojibake characters. Use the editor tooling instead;
- when a change produces no measurable effect, suspect that the code never runs.
  An advance fix once changed nothing because the route index it corrected was
  never reached, and counters proved that in a single battle;
- **delete code from `WoTMapSceneBuilder.cs` only with targeted edits.** Two attempts
  to remove dead ranges by searching for substrings cut live methods that sat between
  the markers, and both had to be reverted from Git. When a deletion is risky, leave
  the code behind a named switch and say why in a comment - that is how the superseded
  sprite-block ground and the navigation wash are kept.

## The order that works

The whole arena is reproducible from the client, and the steps depend on each other:

1. `Automation/pipelines/WoTMapPrimitivePipeline` builds the semantic map JSON, terrain and AI grid;
2. `Automation/pipelines/WoTEnvironmentPipeline` extracts the art: objects, plus the materials and the
   surface image the ground needs;
3. `BuildGround` creates the macro/atlas/selector manifest for the arena floor;
4. `ImportWotMap`, then `BuildWotMapTacticalAnalysis`, then `BuildWotMapScene` through
   the launcher, which is the only entry point for Unity;
5. `CaptureWotMapPreview` plus `CaptureWotGroundDetailPreview` to see the result, and the
   PlayMode arena checks to prove it.

Step 4 rebuilds the scene from the map asset, so any hand edit to the scene is lost. Put
changes in the builder, never in the scene file.
