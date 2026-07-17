# World

Arena/background rendering, camera, and coordinate transforms. Planned mapping:

- `Background.cs` <- `background.py` (tile rendering, biome palettes, room generation)
- `Camera.cs` <- the `world_to_screen`/`screen_to_world`/rotation pieces of `background.py`
- `GamePaths.cs` <- `gamePaths.py` (content-path data + enemy identity application)
- `SpatialHash.cs` <- `spatialHash.py`
- `Progression.cs` <- `progression.py`
