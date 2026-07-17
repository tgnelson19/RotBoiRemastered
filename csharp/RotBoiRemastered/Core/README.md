# Core

Entry point, state machine, and the live input state other systems read from.

- `RotBoiGame.cs` <- `main.py` (state dispatch)
- `GameState.cs` <- `variableHolster.States`
- `InputState.cs` <- a minimal slice of `variableHolster.py` (just `keyPressed`/
  `keys`, enough for `Systems/Keybinds.cs`). This will grow to cover the rest
  of that module (mouse/controller state, screen dimensions) -- deliberately
  kept small rather than porting the whole thing speculatively before
  anything needs it.
- `Simulation.cs` <- the timing slice of `variableHolster.py`
  (`tileSizeGlobal`, `frameRate`, `REFERENCE_FPS`, `get_frame_scale`,
  `get_timer_step`, `set_delta_time`). **Done.** Every Entities/ Update
  method reads this for frame-rate-independent movement, same as the
  Python original.
- `Primitives2D.cs` -- grew well past the original rect/line-only scope
  while porting Entities/: added `FillCircle`/`CircleOutline`,
  `FillEllipse`/`EllipseOutline`, `Arc`, `FillPolygon`/`PolygonOutline`, and
  `Polyline`, covering the rest of pygame's `draw` module that entity
  rendering leans on (circles, ellipses, arcs, filled polygons, multi-point
  lines). `FillPolygon`/`FillEllipse`/`FillCircle` all rasterize via
  horizontal scanline `FillRect` strips -- no vertex/mesh renderer, just
  more of the same stretched-pixel technique.
