# Core

Entry point, state machine, and the live input state other systems read from.

- `RotBoiGame.cs` <- `main.py` (state dispatch)
- `GameState.cs` <- `variableHolster.States`
- `InputState.cs` <- a minimal slice of `variableHolster.py` (just `keyPressed`/
  `keys`, enough for `Systems/Keybinds.cs`). This will grow to cover the rest
  of that module (mouse/controller state, screen dimensions, frame timing) --
  deliberately kept small rather than porting the whole thing speculatively
  before anything needs it.
