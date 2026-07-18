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
