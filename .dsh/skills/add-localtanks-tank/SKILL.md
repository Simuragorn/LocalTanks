---
name: add-localtanks-tank
description: Integrate a named World of Tanks vehicle into the LocalTanks Unity game end to end — canonical WoTGenerated sprite, top-configuration gameplay parameters, local engine/gun/turret/tracks audio profile, combat JSON, playable prefab, national team registration, generated scenes, tests and documentation. Use when the user asks to add, integrate or make playable a tank by name. Do not create or repaint sprites inside this skill and do not publish local WoT assets.
whenToUse: Use for full vehicle integration; use render-wot-tank-sprite for artwork alone and wot-tank-audio for sound alone.
---

# Add a LocalTanks tank

Turn one tank designation into a researched, playable vehicle in `C:\UnityProjects\LocalTanks`. This skill owns parameters and game integration. It consumes finished artwork and audio; it never creates or edits tank pixels itself.

## Before editing

1. Read [references/localtanks-integration.md](references/localtanks-integration.md) completely and inspect the current project files it identifies. The repository is authoritative when it differs from the reference.
2. Inspect `git status` and preserve unrelated work. Never stage or rewrite user-owned changes.
3. Read `AGENTS.md`, `PROJECT_PLAN.md` and `Docs/STATUS.md` for the project rules, then `Docs/DATA_SCHEMA.md` and `Docs/ARCHITECTURE.md` for the live schema and integration points.
4. Read `Docs/TANK_BALANCE.md` and identify the closest existing vehicles by class, protection, gun and intended role. This generated comparison is the mandatory baseline for every new tank and every balance change.
5. Read `Docs/WOT_MEDIA_AUDIT.md` and inspect the current audio resolver and profile implementation before promising sound integration.
6. Resolve the exact WoT vehicle ID, then use its top researchable configuration: the last/highest chassis, turret and gun exposed by the current local client plus the highest-power compatible engine. Stock and intermediate modules are not valid defaults, even when an older render manifest selected them. Use another configuration only when the user explicitly requests it. Ask one concise question only when the designation genuinely identifies materially different vehicles.
7. Find the sprite before researching or changing Unity. Run [scripts/find_localtanks_sprite.ps1](scripts/find_localtanks_sprite.ps1) with the resolved designation and inspect its JSON result.

## Sprite acquisition contract

Use an existing valid `_strip2.png` when the resolver finds one clear best match under `Assets/LocalOnly/WoTGenerated/<vehicle-id>` and its manifest already records the current client's top chassis, turret and gun. Read the matching `_layout.json` and `_manifest.json` when present. A stock or intermediate manifest is stale for this workflow: load the `render-wot-tank-sprite` skill and use it to replace that render reproducibly. Inspect the PNG, but do not crop, recolour, resize, separate, repair, overwrite, rename, move or copy it. No second tank-art folder is supported.

If no suitable sprite exists, load the `render-wot-tank-sprite` skill, follow it, and use its validated output. Do not ask the user to run it separately, and do not substitute image generation or improvised artwork if rendering fails.

If several candidates remain equally plausible after exact-name, variant, manifest and folder matching, ask the user to choose before integrating. A merely similar tank is not a valid fallback.

WoT source packages, intermediate renders, layout, manifest, diagnostic files and the final sprite stay under ignored local directories. Reference the validated canonical `_strip2.png` directly from Unity. Never copy vehicle art into `Assets/Game`, stage generated vehicle files or publish them.

## WoT gameplay profile

Browse the web before assigning specifications. The baseline is the exact top World of Tanks configuration from the current local client, which must also be represented by the rendered model — not a reconstructed historical or stock stat block. Use sources in this order:

1. official Tankopedia and other official WoT pages;
2. data from the user's locally installed WoT client of the same version, preferably through `sync-wot-vehicle-data`;
3. reputable WoT databases when an official interface does not expose the field.

Use museum, archival and historical sources only to identify the variant, validate plausibility and explain production status. Do not replace available WoT gameplay values with historical estimates.

Record, and keep beside every conversion:

- the exact WoT tier of the rendered vehicle ID, preferably from its official Tankopedia page;
- exact vehicle ID, top chassis, turret, gun and highest-power compatible engine, plus production or prototype status;
- WoT HP, mass, forward and reverse speed, engine power, hull and turret traverse and other exposed mobility values;
- WoT nominal front, side and rear armour;
- main shell damage and penetration, reload, aiming time, accuracy and dispersion;
- view range and concealment;
- dimensions needed for sprite scale, muzzle position and hull collider;
- engine and main-gun Wwise events and media IDs resolved from the local installation;
- source URLs, client version, raw values, converted LocalTanks values and every conflict.

Copy compatible WoT values directly. Convert movement distance, view distance, concealment and accuracy through the one documented project-wide rule while preserving relative differences. Store nominal plate thickness, not pre-sloped effective armour, because incidence angle is handled at runtime. Keep the WoT source value beside every converted or approximated value in `Docs/TANK_REFERENCE.md`.

WoT values are the baseline, not an absolute ban on balancing. Before finalizing JSON, compare the proposed vehicle with at least two suitable entries from `Docs/TANK_BALANCE.md` when two peers exist. Change a sourced value only for clear LocalTanks balance, scale or mechanical compatibility, and record the raw value, the final value and the reason. Never tune silently from memory.

The WoT tier is game metadata, not a historical or LocalTanks balance estimate. Store it as the integer `tier` and cite the official WoT source. LocalTanks accepts tiers 1 through 10 only. If the exact vehicle is Tier XI or its tier is genuinely ambiguous, stop and ask the user instead of clamping, guessing from strength or silently choosing a neighbouring variant.

## Audio

Load the `wot-tank-audio` skill and follow it for the same selected configuration. Reuse shared clip IDs from the local cache instead of duplicating clips per vehicle, and store every clip ID plus volume and pitch tuning in the tank JSON `audio` object. Never commit extracted clips, their `.meta`, local manifests, banks or tool caches. If exact events cannot be resolved, preserve the discovered module/event mapping and report audio integration as incomplete; do not silently choose an unrelated sound, and document any deliberate class/calibre fallback with its reason.

## Integrate the vehicle

1. Validate the selected sprite: transparent background, hull and turret separated, complete barrel, correct source orientation, no clipped geometry and no obvious material failure. For a turretless vehicle, require one complete hull and do not invent a turret.
2. Obtain slice rectangles and turret pivot from the layout sidecar when available. Otherwise measure non-transparent component bounds and the mechanical turret-ring centre without modifying the PNG.
3. Add or reuse JSON definitions for the tank, weapon and shell. Every tank requires a valid `nation`, WoT `tier` from 1 through 10, and explicit `availableInGame`; every available tank also requires the complete `audio` object. Reuse an existing definition only when it represents the same modelled equipment. Use stable lowercase snake-case IDs.
4. Use only the vehicle's concise main designation for the player-facing `displayName`. Remove parenthetical manufacturer, armament, prototype, modernization and modification qualifiers such as `(Rheinmetall prototype)` or `(45 mm modernization)`. Keep the exact modelled variant and those qualifiers in `Docs/TANK_REFERENCE.md`, source notes and stable IDs instead — the project validator rejects parenthetical tank-name suffixes.
5. Register the sprite and tank in the current Editor build registry. Derive visual scale, muzzle distance and a minimally simplified hull outline from the finished sprite and real dimensions; take gameplay mass from the selected WoT profile. The `PolygonCollider2D` must follow the visible hull rather than a box.
6. Generate a functional prefab with the current runtime hierarchy and components, including `TankAudioController`. It must load local clips by stable ID at runtime; never serialize ignored clip GUIDs into the committed prefab. Handle turretless vehicles explicitly through hull aiming or existing turretless support.
7. Decide the `availableInGame` flag deliberately. It is the single switch for playability: a tank marked available is offered by the test-range selector and by both laboratories (`Battle_PlayerAiTestLab` and `Battle_AiDuelLab`) without any further registration, because `BattleDirector.SpawnableTankDefinitions` filters only by this flag and by the presence of a prefab binding. Setting it to false keeps the complete prefab and data but hides the tank everywhere.
8. Do not register the prefab by hand. Scene builders derive their `TankPrefabBinding` arrays from the combat database through `PrototypeSetup.FindTankPrefabPath`, so a playable tank is bound automatically and a scenario entry is bound as well. Adding a tank therefore needs one build spec entry, one JSON definition set and nothing else: never add a second prefab list, a second tank-id list or a scene edit. If a playable tank has no build spec, the scene builder fails loudly with the tank id instead of binding nothing.
9. The fixed battle roster stays hand-authored. `river_crossing_battle.json` lists exactly its own seven versus seven and must not be regenerated from every playable tank; a tank can be playable in the laboratories while staying out of that roster. If the user asks for a new tank to join the roster, update the scenario only on the team whose nation matches the tank, preserve one nation per team, the enforced team size, exactly one Team A player, valid bases, unique spawn slots and valid routes. Replace a duplicate of that nation's machine when a slot is needed; never introduce a mixed-nationality team or force an unavailable definition into the roster. A nation with no team in the scenario cannot join it without a separate decision.
10. Replace or extend brittle fixed-ID tests with registration, prefab-binding and scenario-coverage invariants when necessary. A test that requires the roster to cover every playable tank is wrong once laboratories and the roster diverge; assert instead that every roster entry is playable.
11. Run Unity import and builders through Editor APIs. Never hand-edit `.meta`, prefab YAML, scene YAML or generated ScriptableObject YAML.

## Verify

Run the repository's Edit Mode and Play Mode suites in batchmode and inspect the logs. At minimum prove that:

- the PNG imports as the intended hull and turret sprites with correct alpha, rectangles, pivot and forward direction;
- JSON validation and all cross-references succeed;
- the imported tier matches the cited exact WoT vehicle and lies between 1 and 10;
- the prefab has movement, aiming, weapon, health, armour, destruction, polygon collider and the required AI and team integrations;
- muzzle position and visual scale are plausible relative to existing vehicles;
- projectiles collide, armour and angle logic runs, damage applies and destruction works;
- the audio profile resolves to the selected WoT configuration, references valid local clips, reuses shared clip IDs without duplicate bytes and plays without Console errors;
- committed prefab YAML contains no `Assets/LocalOnly` audio GUID, and `git ls-files Assets/LocalOnly LocalData` stays empty;
- the test-range selector discovers the vehicle without tank-specific selector code;
- the base-battle scene spawns it at the correct team's base under AI control in at least one slot;
- every available scenario definition has a prefab binding, appears only on a matching national team, and every team stays nationally homogeneous;
- no new Console errors or warnings appear.

Run [scripts/validate_localtanks_tank.ps1](scripts/validate_localtanks_tank.ps1) with the final tank ID, actual sprite path and prefab path as an additional static check. It supplements Unity tests; it does not replace them.

## Documentation and version control

Update `Docs/TANK_REFERENCE.md` with the exact WoT configuration, official, local and fallback sources, raw WoT values, LocalTanks conversions, balance deviations, historical context and the audio event mapping. Update `Docs/STATUS.md`, `Docs/CHANGELOG.md`, `Docs/DECISIONS.md` and `Docs/KNOWN_ISSUES.md` when project conventions require it, and remove the finished item from `Docs/BACKLOG.md`.

Reimport combat definitions after any parameter change; the import updates `Docs/TANK_BALANCE.md`, and its Edit Mode freshness test must pass before committing. Never hand-edit generated balance values.

Commit only scoped tracked project files as `Simuragorn <simuragorn@gmail.com>`. Preserve unrelated dirty files. Never commit canonical WoTGenerated art or its Unity metadata. Do not push unless the user asks.

## Report

Report the exact vehicle ID and selected modules, WoT tier and sources, whether the sprite was reused or rendered, the sprite, layout and manifest paths, raw and final important parameters, engine, gun, turret, tracks, reload and destruction events with clip IDs, data and prefab paths, test-range and battle-roster registration, tests run, source links and the commit hash. Show the finished sprite. Explicitly identify every scale conversion, balance deviation, gameplay approximation, prototype-only fact, local-only asset limitation, unresolved audio mapping or material issue.
