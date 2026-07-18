# Core

Entry point, state machine, and the live input state other systems read from.

- `RotBoiGame.cs` <- `main.py`'s `main()`/`runGame()`/`runTitle()`/
  `runLeveling()`/`runPaused()`/`runResults()` + `baseInputCollection()`/
  `update_input_toggles()`/`update_camera_controls()`. **Done** -- the
  `GameState` switch that was a `// TODO` skeleton for most of this port now
  really drives `Systems/GameSession.cs`, `UI/Menus.cs`, `UI/TitleScreen.cs`,
  and `UI/LevelingHandler.cs` (via `GameSession`'s wrappers) together into
  one playable loop. Edge-triggered input (`KeysPressed`/`MousePressed`) is
  derived by diffing this frame's polled MonoGame keyboard/mouse state
  against last frame's -- there's no pygame event queue to drain here.
  Explicitly deferred (documented in the class's own doc comment, not
  silently dropped): the custom in-arena aiming reticle (the OS cursor stays
  visible everywhere instead of being hidden-and-replaced while aiming) and
  `hasBeenReset`'s two-call reset dance around the title screen (no
  observable effect once the title screen never reads run stats -- the next
  "start run" always freshly resets/constructs a `GameSession` anyway).
  Also still deferred: `gamePaths.py`'s per-path boss selection --
  `GameSession.HandleEnemyCreation` still hardcodes `Beaudis`/`Dissonance`
  regardless of the selected path (see `Systems/README.md`).
- `GameState.cs` <- `variableHolster.States`
- `InputState.cs` <- a minimal slice of `variableHolster.py` (`keyPressed`/
  `keys`/mouse position/mouse button state), now actually populated every
  frame by `RotBoiGame.CollectInput()`. Still doesn't cover controller state
  or screen dimensions -- deliberately kept to just what the wired game loop
  needs rather than porting the whole original module speculatively.
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
