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
  `ActivateSelected`/`BossKey`/`IsTouch`. Deferred: `ApplyEnemyIdentity`,
  `ENCOUNTERS` (`_PathEnemyCatalog`), `RegisterExclusiveEncounter`, and
  `TuneNewProjectiles` all operate directly on `Enemy`/`EnemyProjectile`/
  `EnemyCatalog` instances, none of which exist yet -- port them alongside
  `Entities/`.

## Explicitly deferred (not in World/ yet)

The actual pixel rendering of tiles, raised walls, and decorations
(`background.py`'s `drawRepasteableBackground`, `_draw_floor_detail`,
`_draw_raised_decoration`, `_draw_camera_facing_wall`,
`moveAndDisplayBackground`, `drawRaisedScenery`, `_wall_screen_geometry`,
`_decoration_screen_rect`) is *not* ported yet. `Battleground`/`Camera` give
everything needed to place and collide with the world; drawing it is a
separate pass, more naturally paired with `Entities/` once there's something
to look at the arena *for*. Verified in the meantime with a temporary
diagnostic render (flat per-tile colors, no camera rotation/3D walls) that
confirmed all five generators produce the correct shapes -- see the git
history for this commit if you want to regenerate that check.
