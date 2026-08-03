# World

Arena/background generation, camera, and coordinate transforms. Mapping from
the Python source:

- `Progression.cs` <- `progression.py`. **Done.**
- `SpatialHash.cs` <- `spatialHash.py`, genericized as `SpatialHash<T>` since
  the Python version was already duck-typed. **Done.**
- `TileType.cs` <- background.py's raw tile-id ints (0-5) and RAISED_TILES/
  SOLID_TILES sets. **Done.**
- `BiomePalette.cs` <- background.py's `*_PALETTES` tuples. **Done.**
- `Camera.cs` <- the `world_to_screen`/`screen_to_world`/rotation pieces of
  `background.py`. **Done**, as an instance class rather than module globals
  -- see the class doc comment for the cleanup rationale.
- `Battleground.cs` <- the rest of `background.py`: the tile grid, wall-
  collision queries (`rect_hits_wall`, `find_spawn_rect`, etc.), and the five
  procedural map generators (`generate_battleground`,
  `generate_touch_battleground`, etc.). **Done**, also as an instance class
  -- see the class doc comment for the cleanup rationale (dropped the
  per-tile Rect storage, the id()-keyed caches, and the raw tile-id/style
  strings).
- `GamePaths.cs` <- `gamePaths.py`. **Done, data/selection portion only**:
  `EnemyStyle`, `GamePath`, the `Paths` table, `Select`/`Cycle`/
  `ActivateSelected`/`BossKey`/`IsTouch` -- all wired into
  `Core/RotBoiGame.cs`'s title screen and run-start/restart flow. Still
  deferred: `ApplyEnemyIdentity`, `ENCOUNTERS` (`_PathEnemyCatalog`),
  `RegisterExclusiveEncounter`, and `TuneNewProjectiles` (per-path enemy
  stat reskinning/spawn tables) and per-path boss selection
  (`GameSession.HandleEnemyCreation` still hardcodes `Beaudis`/`Dissonance`
  regardless of the active path) -- `Entities/` exists now, so nothing
  structural blocks these anymore, they just weren't in scope for the
  game-loop wiring pass; see `Systems/README.md`.
- `ArenaRenderer.cs` <- `background.py`'s pixel rendering:
  `drawRepasteableBackground`/`_draw_floor_detail`/`_raised_scenery`/
  `moveAndDisplayBackground`/`drawRaisedScenery`/`_wall_screen_geometry`/
  `_draw_camera_facing_wall`/`_decoration_screen_rect`/
  `_draw_raised_decoration`. **Done** -- see its own doc comment for the one
  real design decision: the floor plane is still baked once per
  `Battleground` into a `RenderTarget2D` (Python bakes for the same reason
  `Core/Primitives2D.cs`'s `FillPolygon` stays per-frame-only for walls/
  decorations -- one `SpriteBatch.Draw` call per scanline row is far too
  many draw calls for thousands of floor tiles every frame), but Python's
  elaborate downsample/cache/rotate/rescale pipeline on top of that bake is
  dropped entirely: MonoGame's `SpriteBatch.Draw` rotation is a single
  hardware-accelerated call regardless of source texture size, so the baked
  texture is just drawn rotated directly, every frame, no caching needed.
  Viewport clipping uses `GraphicsDevice.ScissorRectangle` in place of
  pygame's `screen.set_clip`/restore. `ComputeRaisedScenery`/
  `WallScreenGeometry`/`VisibleWallFaces` are public static pure functions
  (no `GraphicsDevice` needed) specifically so the wall-face-culling/
  decoration-selection logic has direct unit test coverage --
  `RotBoiRemastered.Tests/World/ArenaRendererTests.cs`.
- `WorldLighting.cs` owns the combat atmosphere pass. It darkens only the
  world viewport, restores a path-colored player light, and derives emissive
  sources from authored raised landmarks. Standalone and instanced arenas
  receive deterministic, non-colliding light posts on nearby walkable tiles.
  The pass runs before `PathFogOfWar`, so lighting never reveals unexplored
  space; radial falloff is a cached texture rather than per-frame scanline
  geometry. Theme coverage, fixture placement, and continuous flicker curves
  are covered by `RotBoiRemastered.Tests/World/WorldLightingTests.cs`.
- `PathFloorGenerator.cs` builds composite Path-mode dungeon floors from the
  existing tile and palette vocabulary. Every floor now has a protected start,
  seven mandatory combat-room modules, and then its boss room. The seven are
  drawn without replacement from an eight-shape module deck (chamber, hall,
  arena, maze, crossroads, diamond, ring, and ruin) and placed into one of four
  non-overlapping route silhouettes, so room scale and pacing remain uniform
  while combinations vary by seed. The boss route cannot activate until all
  seven mandatory rooms are cleared. Optional treasure and challenge wings
  remain branches rather than shortcuts. Treasure count uses chained 50% rolls
  capped at three, and their enlarged footprints support guardian-strength
  encounters. Sense-specific corridors use an authored dog-leg router with a
  bounded grid fallback that avoids unrelated rooms and carries directional/
  threshold marks. The boss room stays at the battleground's exact center so every
  existing sense boss can reuse its authored arena geometry. Each sense owns
  two boss-room obstacle silhouettes; `EvaluateBossArenaSafety` verifies their
  centered 9x9 spawn, three-tile cardinal lanes, connected analog-stick
  traversal, and separated safe footprints for two simultaneous players.
- `PathFloorBlueprints.cs` owns macro-layout coordinates and separates spatial
  silhouette (`PathRoomShape`) from gameplay purpose (`PathRoomType`). It owns
  the shared shuffled module deck, seven-room encounter cadence, compact route
  sockets, and branch sockets. This is what lets an Assault be an arena on one
  floor and a maze on another without changing its combat contract.
- `PathFogOfWar.cs` owns persistent discovery and current line of sight for
  generated floors. Rays use the player's real sub-tile position and traverse
  grid boundaries without a range cap. A non-cascading presentation pass keeps
  walls beside visible floor lit and reveals true L-shaped wall corners when
  either adjoining wall is visible, avoiding corridor wedges and corner-tile
  flicker without revealing the sealed space behind them. Immutable topology,
  flat visibility buffers, and reusable corner support storage keep moving LOS
  refreshes allocation-free. `GameSession` suspends world fog and visibility
  culling inside Grand Arenas and boss rooms while retaining persistent
  discovery for the minimap, then resumes fog during traversal.
- `PathThemeVisuals.cs` supplies the generated floors' semantic scenery
  contract. Touch uses sewer channels, grates, pumps, and sludge; Sight uses
  water, caustics, lens buoys, and drowned ruins; Sound uses cloud banks, wind
  lanes, chimes, and lightning rods; Phantasia uses star fields, nebulae,
  asteroids, and orbit shrines; Chemesthesis uses fractured earth, rot,
  barricades, dead trees, and ruin slabs. A second material pass adds sewer
  brick runes and pressure tanks, drowned mosaics and mirror arches, resonance
  tiles and organ stacks, dream glyphs and lantern spires, plus cinder plates
  and furnace idols. Treasure seals and symmetric landmark assemblies give
  reward/boss spaces an authored silhouette. `ArenaRenderer` bakes floor/low
  motifs, varies wall-face materials by sense, painter-sorts raised props with
  walls, and leaves ambient emitters to `GameSession`; floors six through ten
  add deterioration, stronger atmospheric density, and a darker material
  grade. The live Path draw uses one combined raised-scenery/actor pass,
  allocation-free wall quads, retained painter buffers, and coalesced fog runs
  instead of redrawing the raised layer or submitting one mask per tile.
  `Battleground` partitions animated floor, raised, and ambient decoration
  arrays once during construction, so the longer seven-room floors do not
  re-filter static scenery every frame. Each shuffled module also receives a
  small shape-authored arrangement in its sense vocabulary: halls get an aisle,
  crossroads get cardinal machinery, rings and diamonds get ritual markers,
  mazes get navigation anchors, and ruins get asymmetric relic clusters.
  Protected starts are now full ceremonial thresholds with a
  sense-specific central crest, processional floor axis, paired landmarks, and
  denser ambience. Every `GrandArena` room has also been recomposed around a
  large sense emblem, an eight-part material ring, perimeter monuments, and an
  atmospheric halo; `PathFloorGenerator` reinforces those compositions with
  distinct non-colliding floor plans for the five themes.
