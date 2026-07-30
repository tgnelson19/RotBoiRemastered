# Path Dungeon Iteration Log

This is the working priority list for the composite ten-floor Path mode. It
records what each review found, what was promoted into the next pass, and why.

## Iteration 1 — Spatial foundation

### Review

- Every floor used the same eastbound chain and the same room coordinates.
- Combat rooms ranged from only 9–13 tiles in either dimension, making every
  encounter read as a small box despite different floor decoration.
- `Skirmish`, `Assault`, and `Elite` changed enemy count, but not the way a
  room asked the player to move.
- Rooms had no shape identity; generation, activation, decoration placement,
  and encounter locks all assumed a rectangle.
- Corridors were three-tile road strips through empty space with no enclosing
  architecture.
- Guardian bosses had distinct projectile grammars, but shared phase names,
  movement rhythm, and phase transition timing.

### Ranked recommendations

1. **P0 — Replace the fixed chain with several macro-layouts and large,
   shape-aware rooms.**
2. **P0 — Make encounter composition respond to room geometry instead of only
   spawning a larger count.**
3. **P1 — Give each sense a traversal grammar: sewer conduits, reservoirs,
   cloud bridges, cosmic courts, and broken apocalyptic districts.**
4. **P1 — Add meaningful traversal-room roles and landmark/reward side rooms.**
5. **P1 — Differentiate Guardian phase pacing and arena interactions by sense.**
6. **P2 — Add navigation/readability cues at forks and room thresholds.**
7. **P2 — Add rare macro-layout events after the core generator is stable.**

### Implemented

- Added four macro-layouts: Switchback, Grand Circuit, Procession, and
  Floodplain.
- Increased the floor height and introduced rooms several screen lengths long.
- Added Sanctuary, Long Hall, Grand Arena, Maze, Crossroads, Diamond, Ring,
  and Ruin silhouettes.
- Made room-shape membership authoritative for carving, activation, props, and
  movement locks.
- Added internal maze baffles and ring-room islands while preserving safe
  center routes.
- Enclosed generated corridors with real raised walls.

### Promoted into Iteration 2

- P0 geometry-aware encounters.
- P1 sense-specific traversal grammar.
- P1 stronger branch-room roles.

## Iteration 2 — Traversal and encounter grammar

### Review

- The new silhouettes created meaningful scale, but every connection still
  felt like the same anonymous strip of road.
- Waves filled the larger rooms but did not use their geometry: artillery
  could appear in maze pockets while melee pressure idled at the far end of a
  long hall.
- The only optional room role remained a free treasure room.
- Players received no explicit cue that a room was designed as a maze, ring,
  long hall, or arena.
- Normal combat rooms accidentally inherited a Chemesthesis crack signature
  when no special signature was defined.

### Ranked recommendations

1. **P0 — Bind corridor width/material rhythm and macro-layout weighting to
   the active sense.**
2. **P0 — Compose enemy roles and spawn formations around room geometry.**
3. **P1 — Add optional challenge branches with an earned enhanced chest.**
4. **P1 — Announce room role and silhouette briefly on first entry.**
5. **P1 — Give ordinary Guardians stronger phase identity and arena responses.**
6. **P2 — Improve route choice readability and reduce accidental corridor
   crossings.**
7. **P2 — Add rare traversal events and authored landmark combinations.**

### Implemented

- Added sense-specific traversal grammar: narrow Sewer Conduits, broad Tidal
  Causeways, Cloud Bridges with widened landings, Starwalk nodes, and broad
  Ruptures.
- Weighted macro-layout selection by sense so each environment retains variety
  while emphasizing a suitable spatial rhythm.
- Added corridor floor motifs that continue each theme between rooms.
- Made enemy role selection shape-aware and arranged spawns as hall banks,
  arena rings, and crossroads arms.
- Scaled wave population with actual room geometry.
- Added optional Challenge branches with two champions and an enhanced
  guaranteed chest on clear.
- Added short `ROOM TYPE // SHAPE` entry banners.
- Removed the unintended Chemesthesis crack signature from unrelated ordinary
  rooms.

### Promoted into Iteration 3

- P1 Guardian phase identity and difficulty curve.
- P2 route/fork readability and corridor-crossing refinement.
- P2 rare landmark/traversal events where they improve pacing.

## Iteration 3 — Pacing, navigation, and boss refinement

### Review

- The floor grammar and encounter formations now differ substantially, but
  larger switchbacks make route memory more demanding than the original
  five-box chain.
- Some one-bend corridors could cut through an unrelated room, creating an
  accidental shortcut and weakening the authored graph.
- Corridor motifs communicated material but not travel direction or room
  thresholds.
- Ordinary Guardians could be burst through a health threshold before showing
  the mechanic that distinguished that sense.
- Guardian names, transition durations, preferred distance, orbit behavior,
  and attack cadence still felt too similar even though their projectiles
  differed.

### Ranked recommendations

1. **P0 — Protect the authored room graph and make forks/thresholds readable.**
2. **P0 — Require each Guardian phase to demonstrate its mechanic before it
   can be skipped.**
3. **P1 — Give Guardian movement, cadence, phase language, and transition
   timing a sense-specific identity.**
4. **P1 — Add a compact discovered-floor map now that routes are larger and
   less linear.**
5. **P2 — Add rare floor modifiers and authored landmark combinations.**
6. **P2 — Add more optional room rewards beyond items once their progression
   economy is designed.**
7. **P3 — Consider room-local camera easing only after extended controller
   playtesting; current global camera behavior is consistent and reliable.**

### Implemented

- Corridor selection now scores multiple bends and dynamically generated
  doglegs, avoiding every unrelated room across the deterministic layout
  audit.
- Added directional floor chevrons and sense-colored threshold runes.
- Added a compact floor map showing actual room footprints, routed
  connections, branches, room state, and player position.
- Guardians now require two complete attacks in phases one and two before the
  next health gate can open; large hits cannot skip a phase.
- Added sense-specific phase language, preferred combat distance, orbit
  behavior, cadence, movement speed, and transition duration.

## Iteration 4 — Risk, reward, visibility, and material identity

### Review

- Treasure branches were reliable but free rewards, which made their decision
  trivial and concentrated equipment without a matching combat cost.
- One mandatory treasure room appeared on every floor; branch frequency could
  not create rare high-value floors or lean route-focused floors.
- Room locks contradicted the larger spatial layouts by forcing every
  encounter to resolve in isolation.
- The discovered-floor map conveyed route memory, but the world itself exposed
  rooms, threats, and treasure before the player established line of sight.
- Sense palettes were distinct, yet walls and floor tiles still repeated one
  material treatment too often between authored props.

### Ranked recommendations

1. **P0 — Turn treasure rooms into guardian-scale risk/reward encounters.**
2. **P0 — Remove room locks and let unfinished enemies create the cost of
   rushing.**
3. **P0 — Add persistent, corner-aware line-of-sight fog and hide unseen
   threats.**
4. **P1 — Make treasure frequency variable without allowing branch spam.**
5. **P1 — Add theme-specific floor/wall materials and landmark assemblies.**
6. **P2 — Preserve discovery rules on the minimap, bounty system, and boss
   interface.**

### Implemented

- Treasure branches now use chained 50% rolls and cap at three rooms.
- Every treasure chest is sealed behind a deterministic mini-guardian or
  guardian-strength horde; those enemies suppress ordinary item drops while
  the room chest still guarantees at least two items.
- Removed threshold movement locks. Multiple room encounters can remain active
  simultaneously, use extended pursuit ranges, and survive entry into the boss
  arena.
- Added persistent tile discovery and sub-tile LOS ray marching. Walls remain
  readable while occluding tiles behind them; explored areas darken after the
  player leaves. LOS now has no arbitrary range cutoff, visible corridor floor
  supports its bordering wall faces, and a non-cascading corner pass keeps
  convex wall turns from flashing dark when an adjoining wall remains visible.
- Hidden enemies, hostile pools/projectiles, bounties, boss health UI, room
  footprints, and routes now follow the same visibility contract.
- Added treasure seals, symmetric authored landmark groups, five new floor
  materials, five new raised landmarks, and sense-specific wall-face patterns.
- Confirmed the sidebar's existing live run timer and floor/sense display.

## Iteration 5 — Entrance and arena beautification

### Review

- Protected start rooms were safe and themed at the material level, but still
  read as lightly decorated boxes rather than authored entrances.
- `GrandArena` rooms inherited the same randomized prop scatter as smaller
  rooms, so their scale was not matched by a strong visual composition.
- The randomized composite Path was launched from the title menu, separating
  it from the five physical paths and leaving the Soul convergence without a
  final destination.

### Implemented

- Rebuilt every protected start as a ceremonial threshold: a large
  sense-specific crest, processional side motifs aimed at the first door,
  paired landmarks, sentinels, route mark, and extra ambient sources.
- Repainted every `GrandArena` with a non-colliding, sense-specific floor plan:
  Touch drainage races, Sight optical basins, Sound wave stages, Phantasia star
  courts, and Chemesthesis cinder plates/fault lines.
- Added a large arena centerpiece, eight-part material ring, six-position
  perimeter monument composition, and four-source atmospheric halo using each
  dungeon's existing visual vocabulary.
- Rebuilt the Soul branching point as a layered convergence dais with five
  illuminated spokes and a large five-color final portal.
- Moved randomized Path entry into that convergence portal, including its own
  confirmation and pull/fade sequence, and removed the title-screen button and
  keyboard shortcut.

## Iteration 6 — Performance and traversal smoothness

### Review

- Unlimited sub-tile fog refreshed by tracing every target independently and
  allocated short-lived neighbor iterators/support lists on every movement
  frame.
- Actor collision rebuilt rotated polygons and separating-axis collections
  several times per enemy per frame.
- Raised scenery was rendered in the background pass and then rendered again
  in the authoritative actor/scenery depth pass.
- Combat updates and HUD drawing repeatedly rebuilt derived room, projectile,
  hitbox, bounty, minimap, and painter-order collections.
- Fog masking submitted one ground sprite per hidden tile and rebuilt wall
  geometry arrays for every obscured wall in view.

### Implemented

- Flattened immutable fog topology and visibility state into cache-friendly
  arrays, retained exact sub-tile/unlimited LOS behavior, and made the
  presentation support passes allocation-free.
- Cached camera trigonometry, immutable layout/decor metadata, active-room
  state, build snapshots, hitboxes, and reusable frame scratch buffers.
- Added allocation-free screen-aligned rectangle collision and quad rendering
  for actors, projectiles, pickups, walls, and wall fog volumes.
- Removed the duplicate raised-scenery pass while preserving the combined
  actor/wall painter order.
- Coalesced adjacent fog tiles with the same discovery state into rotated row
  runs and retained raised-wall masking as a separate volume pass.
- Reused the spatial collision index and projectile tails, removed repeated
  LINQ/list materialization from active Path update/draw loops, and selected
  the frame bounty once for both HUD consumers.
- A representative 141x81 floor's LOS refresh improved from about 2.01 ms and
  67.6 KB allocated per update to about 0.40 ms with zero steady-state
  allocation in the local release probe.

### Hitch follow-up

- Removed recurring live allocations from raised dungeon landmarks, polygon
  rasterization, item/stat symbols, sprite-name normalization, and the
  always-visible information sheet. Reusable buffers and cached build summaries
  keep those draw paths from periodically forcing a managed collection.
- Suspended world fog, visibility culling, and fog masks inside `GrandArena`
  and boss rooms. Persistent exploration still feeds the minimap, and traversal
  fog resumes immediately after leaving an arena.
- Added persisted 30-360 FPS limiting and Vertical Sync options, with an
  explicit fixed update/draw cadence and runtime graphics synchronization.

## Future priority backlog

1. **P1 — Rare floor modifiers:** flooded conduits, stormfronts, astral
   convergence, structural collapse, and contamination should be occasional
   whole-floor events rather than more permanent visual noise.
2. **P1 — Expanded landmark library:** build on the first symmetric
   assemblies with rare pump stations, drowned observatories, bell towers,
   asteroid chapels, and evacuation camps.
3. **P2 — Branch economy:** introduce non-item rewards only after deciding how
   healing, temporary boons, reroll currency, and risk rooms should interact
   with the ten-floor difficulty curve.
4. **P2 — Encounter telemetry:** capture room clear time, damage taken, and
   build archetype to tune shape-specific wave counts from play data.
5. **P2 — Boss-room variants:** ordinary Guardians can use alternate arena
   obstacle arrangements; floors five and ten must remain compatible with the
   authored boss geometry.
6. **P3 — Room-local camera easing:** evaluate after controller testing and
   motion-accessibility review.
