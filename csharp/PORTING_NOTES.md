# C# port (MonoGame)

Skeleton only right now -- no game logic has been ported yet. This is scaffolding
for the eventual C# rewrite of the Python/Pygame game in the repo root, tracked on
the `c#-port` branch.

## Why MonoGame

Closest analog to Pygame: open-source, cross-platform (Windows/Linux/macOS), a
low-level 2D API (`SpriteBatch`, manual game loop) rather than a scene-graph
engine. That should let most of the existing draw calls and the `main.py` state
machine translate fairly directly instead of needing to be restructured around
an editor/node system.

## Build & run

Requires the .NET SDK and the MonoGame templates (`dotnet new install
MonoGame.Templates.CSharp`) for template regeneration; the project itself only
needs the SDK once restored.

```powershell
cd csharp
dotnet build RotBoiRemastered.slnx
dotnet run --project RotBoiRemastered
```

## Layout

```
csharp/
  RotBoiRemastered.slnx
  RotBoiRemastered/
    Core/       entry point, state machine        (see Core/RotBoiGame.cs)
    Entities/   player, enemies, bullets, loot     (see Entities/README.md)
    World/      background, camera, content paths  (see World/README.md)
    UI/         HUD, menus, theming                (see UI/README.md)
    Systems/    upgrades, items, keybinds, profile  (see Systems/README.md)
    Content/    MonoGame content pipeline (Content.mgcb) + raw assets
```

Each subfolder's `README.md` lists the specific Python module(s) it's meant to
replace, so a porting session can pick a folder and know exactly what to look at
in the original codebase.

## Suggested porting order

Dependency order roughly follows the Python import graph:

1. **`Systems/`** first -- `upgrades.py`, `items.py`, `keybinds.py`,
   `gameProfile.py` are all deliberately pygame-free in the original, so they
   port to plain C# with no rendering dependency to untangle.
2. **`UI/UiTheme.cs`** (from `uiTheme.py`) -- the shared draw_text/draw_button/
   draw_panel primitives almost everything else calls into.
3. **`World/`** -- background rendering and the camera/coordinate transforms
   (`world_to_screen`/`screen_to_world`), since entities need these to place
   themselves on screen.
4. **`Entities/`** -- player, bullets, enemies, loot crates.
5. **`UI/InformationSheet.cs`, `UI/Menus.cs`, `UI/LevelingHandler.cs`** -- the
   HUD and menu screens, once the systems and entities they display are in place.
6. Wire it all into `Core/RotBoiGame.cs`'s state switch last.

## Known differences from the Python version to decide on during porting

- **Persistence**: `gameProfile.py` reads/writes `data/profile.json` next to the
  script. The C# equivalent should probably use `System.Text.Json` and a
  per-user app-data folder rather than a path relative to the executable.
- **Resolution/fullscreen**: the Python version defaults to native-resolution
  fullscreen (`variableHolster.py`). The current skeleton defaults to a
  1280x720 window for easier dev iteration -- revisit once `uiTheme.py`'s
  `display_scale` logic is ported.
- **RNG determinism**: several Python modules (`upgrades.py`, `items.py`)
  accept an injectable `rng` parameter specifically so tests can seed it. Keep
  that shape in C# (e.g. accept a `Random` instance) rather than reaching for
  `Random.Shared` everywhere, or the equivalent tests won't be reproducible.
