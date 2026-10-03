# Visual audit — October 3, 2026

The biggest improvement would be a cohesive, stylized isometric naval look: shaded wooden hulls, cream sails, teal water with restrained motion, and islands with recognizable landmarks. Ships, water, and combat feedback should receive the first art pass because they dominate the screen during play.

## What I inspected

Reviewed the current working tree, including its staged changes, and built the MonoGame client successfully with zero warnings or errors. A temporary capture harness rendered the actual `GameClient.Draw` pipeline at 1280×720. The menu and fresh solo start are direct captures; the coastal, teammate, mortar, and docked scenes use deliberately arranged world state to expose the renderer. These are rendered snapshots, not a full gameplay or performance test. Game implementation files were not changed.

| Scene | Capture |
| --- | --- |
| Menu | [Screenshot](visual-audit/menu.png) |
| Fresh solo start | [Screenshot](visual-audit/start.png) |
| Coast, pirates, teammate, and mortar warning | [Screenshot](visual-audit/combat-coast.png) |
| Map after nearby discovery | [Screenshot](visual-audit/map.png) |
| Anchored shipyard | [Screenshot](visual-audit/shipyard.png) |
| Minimum zoom, 0.5 | [Screenshot](visual-audit/zoom-out.png) |
| Maximum zoom, 2.0 | [Screenshot](visual-audit/zoom-in.png) |

## Findings and priorities

| Priority | Current issue | Recommended improvement | Relative scope |
| --- | --- | --- | --- |
| 1 | Ocean grid lines appear across land and buildings. | Flush the water layer before islands; make the grid a debug option when water gains visual detail. | Small |
| 1 | Teammates have red health bars. | Distinguish local player, crew, and pirates with color plus a crew badge or pennant. Friendly-fire targetability should remain a separate signal. | Small |
| 1 | Ships look like flat arrowheads. The mast is a thin dark line and there are no sails. | Add shaded hull sides, a raised deck, sails, cannon ports, and a pennant. Give the bow and stern distinct silhouettes. | Medium |
| 1 | A fresh run fills nearly the whole view with an unmoving blue grid. | Add sparse animated wave highlights, bow foam, and speed-dependent wakes. Add an opening navigation cue toward a discovered port. | Medium |
| 2 | Cannonballs are small dark shapes, and only mortar impacts drive a dedicated temporary effect. | Add muzzle flashes, short smoke puffs, bright projectile highlights, impact splashes, brief hit flashes, and a sinking sequence. | Medium |
| 2 | Islands are concentric copies of convex outlines with a uniform grass center. | Add shoreline foam, sand variation, rocks, palms, vegetation clusters, and landmark differences. Give ports a dock and visible harbor identity. | Medium |
| 2 | Important combat and sailing state is in the window title. | Put pirates remaining, next-wave countdown, and speed in a compact HUD area; label the sail gauge and add contextual ability names. | Small–medium |
| 3 | UI combines pixel text, segmented digits, and hairline icons. | Share typography, panel styling, spacing, and icon stroke widths across menu, HUD, shop, and map. | Medium |
| 3 | The map is a large plain parchment diamond with jagged discovery edges and sparse labeling. | Add discovered island names, consistent port symbols, a compass/legend, and softer presentation of discovery boundaries. | Medium |

### Specific rendering defects

`WorldRenderer.DrawWater` queues grid lines, then `DrawIsland` queues land and building fills. `PrimitiveBatch.Flush` submits **all triangles before all lines**, so the earlier water lines appear on top of the later island fills. This is visible in the coastal capture. Introduce explicit layer boundaries rather than relying on the order of calls within a batch. The same rule matters when layering future deck details, vegetation, and effects.

`DrawHealthBar` chooses green only for `isLocal`; every other ship gets `HealthEnemy`. The brown teammate at the upper right of the coastal capture therefore has a red bar. Ownership and team should determine identity; firing-lane eligibility should determine targeting feedback.

Sources: [WorldRenderer](../src/ShipGame.Client/Rendering/WorldRenderer.cs), [PrimitiveBatch](../src/ShipGame.Client/Rendering/PrimitiveBatch.cs).

### Ships and sea should establish the style

Keep the existing 2:1 projection and collision footprint. Build hull volume above that footprint with a darker visible side and a lighter deck. Use consistent light from the upper left across ships, buildings, and trees. Cream sails will make ships recognizable and provide useful contrast against dark water; vary their fullness with throttle. Use pennants and trim to distinguish the player, crew, and pirates. Preserve material colors when a ship is in a firing lane and signal targeting with a rim or marker instead of repainting the whole hull red.

The nearest island centers are roughly 30 world units from the central spawn, so the default opening view contains no land. Water therefore needs to carry the scene on its own. Begin with low-contrast wave streaks and wakes based on actual interpolated movement, not throttle alone: ships can coast and drift. Wakes should fade while stationary, and anchored ships should have restrained ripples. Shore foam should follow the real coast so the navigable edge remains clear.

Use procedural geometry for the first pass; it already supports arbitrary headings and continuous zoom. A later illustrated sprite pass would need directional views or a more substantial rendering change. The current content manifest has no game art assets.

Sources: [WorldRenderer](../src/ShipGame.Client/Rendering/WorldRenderer.cs), [HullShape](../src/ShipGame.Shared/Simulation/HullShape.cs), [Archipelago](../src/ShipGame.Shared/Maps/Archipelago.cs), [content manifest](../src/ShipGame.Client/Content/Content.mgcb).

### Combat needs visible cause and effect

The current mortar warning communicates danger well; retain its readable area and progress. Add effects around the existing indicators without covering them. Show which side fired through a short flash/smoke burst along the correct beam. Use different impact treatments for water, land, and hull hits, and reserve persistent smoke for heavily damaged ships. A restrained sinking animation would make kills legible.

The event stream already includes `AbilityCast`, `ProjectileSpawned`, `ProjectileImpact`, `ShipSunk`, `ShipGrounded`, and `AreaStrikeImpact`. The client currently handles only `AreaStrikeImpact` for renderer effects. Impact and sinking events identify entities without carrying impact/sinking positions, so retain recent projectile and ship poses on the client to place effects after those entities are removed. Keep effects cosmetic and bounded in lifetime/count.

Sources: [GameClient event handling](../src/ShipGame.Client/GameClient.cs), [WorldEvents](../src/ShipGame.Shared/Simulation/WorldEvents.cs).

### HUD, menu, and map

Keep the useful bottom-center ability layout, split broadside cooldowns, screen-edge pirate indicators, and matching game/map orientation. Improve their hierarchy: identity and hazards strongest, interaction prompts next, scenery and ambient detail quietest. Group overlapping edge markers and use shape as well as color to distinguish threats and allies. Health bars currently scale with world zoom; assess a minimum readable size at 0.5 zoom while keeping them attached to the ship.

Use one readable number style for gold, waves, cooldowns, and island timers. Thin hardware lines remain approximately one raster pixel wide, so filled strokes would better match the scaled text. If keeping the pixel font, review fractional scales and resized windows; the current 1.5 font scale creates uneven pixel placement. A regular UI font is an alternative for body text and contract descriptions.

Give the menu an attractive harbor composition with an identifiable ship and subtle water motion. The current background is empty gridded sea. On the map, label discovered islands and use port symbols that stay legible in screen space. Keep undiscovered geography hidden. The working tree includes route-preview rendering, but the current shipyard panel exposes only plunder/upgrades and `GameClient` does not pass a route preview to the map; evaluate trade presentation once that flow is connected. Floating cargo also has no world draw path yet.

## Suggested implementation order

1. Fix water layer ordering and teammate identity. Capture the same coast scene to verify both changes.
2. Complete one ship and water art pass: shaded hull, sail, pennant, wake, bow foam, and restrained wave motion. Judge it in the opening view and at both zoom limits.
3. Add firing, hit, splash, and sinking feedback using the existing event stream.
4. Complete one representative port with a dock, vegetation, rocks, shore foam, and name marker, then extend the treatment to other islands.
5. Unify HUD/menu/map styling and move essential status into the game view.

Before multiplying scenery and particles, address the renderer's per-flush `ToArray` allocations, add visibility culling, and cache static island detail. These are implementation concerns identified in code, not measured performance failures. Profile with 12 players and dense projectile activity after the visual pass.

Acceptance checks should include all ship headings, both zoom limits, crew versus pirate identification, firing-lane and mortar visibility, stationary/coasting/anchored wakes, small and wide windows, and overlapping harbor objects. For raised scenery, sort individual above-water objects with ships; the present renderer draws every island before every ship, which would otherwise produce incorrect tree/building overlap.
