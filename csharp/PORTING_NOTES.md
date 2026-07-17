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
   port to plain C# with no rendering dependency to untangle. **Done**:
   `Upgrades.cs`, `Items.cs`, `GameProfile.cs`, `Keybinds.cs` are all ported
   with test coverage. `Keybinds.cs` turned out not to be pygame-free the way
   the other three are (it needs real key constants and live input state),
   so it pulled in a small `Core/InputState.cs` -- see `Core/README.md`.
2. **`UI/UiTheme.cs`** (from `uiTheme.py`) -- the shared draw_text/draw_button/
   draw_panel primitives almost everything else calls into. **Done**, including
   a new `Core/Primitives2D.cs` (MonoGame has no `pygame.draw.rect`/`line`
   equivalent, so every future rendering module needs this) and a FontStashSharp
   dependency for text -- see "Known differences" below.
3. **`World/`** -- background rendering and the camera/coordinate transforms
   (`world_to_screen`/`screen_to_world`), since entities need these to place
   themselves on screen.
4. **`Entities/`** -- player, bullets, enemies, loot crates.
5. **`UI/InformationSheet.cs`, `UI/Menus.cs`, `UI/LevelingHandler.cs`** -- the
   HUD and menu screens, once the systems and entities they display are in place.
6. Wire it all into `Core/RotBoiGame.cs`'s state switch last.

## Known differences from the Python version

- **Persistence**: ported using `System.Text.Json` against a strongly-typed
  `GameProfileData` POCO rather than Python's loosely-typed dict --
  `JsonSerializer`'s default behavior (missing properties keep their class
  defaults, unknown JSON properties are ignored) already reproduces the exact
  merge-over-defaults logic `gameProfile.py` implemented by hand, so no manual
  merge step was needed. Still defaults to a path relative to the working
  directory (`data/profile.json`, mirroring the Python original) via a
  *mutable* `GameProfile.SavePath`, not a per-user app-data folder yet --
  revisit when the game is actually packaged for distribution.
- **`gameProfile.toggle(key)`**: Python's version works on any dict entry
  whose value happens to be a bool, keyed by string. `GameProfile.Toggle(name)`
  reproduces that generic string-keyed capability via reflection over
  `GameProfileData`'s properties (PascalCase names, not the old snake_case
  JSON keys) -- kept generic rather than per-field setters because the
  not-yet-ported pause menu drives its toggle rows from a data-driven list of
  field names (`menus.py`'s `_GAMEPLAY_OPTIONS`).
- **Resolution/fullscreen**: the Python version defaults to native-resolution
  fullscreen (`variableHolster.py`). The current skeleton defaults to a
  1280x720 window for easier dev iteration -- revisit once that module's
  screen-setup piece is ported.
- **Font rendering**: pygame renders TrueType/OpenType fonts at any continuous
  pixel size at runtime, which is what powers the text-size accessibility
  setting and `display_scale`-driven UI scaling. MonoGame's built-in
  `SpriteFont` only supports fonts pre-baked at fixed sizes at build time --
  using it would have been a real capability regression. Added
  [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp)
  (`FontStashSharp.MonoGame` NuGet package) instead: it rasterizes TTF/OTF
  fonts dynamically at any size, much closer to pygame's behavior, and its
  own per-size glyph caching mirrors `uiTheme.py`'s `_font_cache`. The font
  file is read directly as raw bytes (`UiTheme.Initialize`), bypassing the
  MonoGame content pipeline entirely -- see the `.csproj` comment on the
  `Content/Fonts/coolveticarg.otf` item. Italic/bold are accepted in
  `UiTheme.Font()`'s signature for parity with `uiTheme.py` (used by
  `bossTypes.py`, not yet ported) but not implemented yet -- there's only one
  font file and no synthetic style synthesis wired up, so both currently
  render at regular weight/slant. Revisit when boss dialogue text is ported.
- **No `pygame.draw.rect`/`line` equivalent**: `Core/Primitives2D.cs` is new
  infrastructure the Python original didn't need (pygame provides these for
  free). Backed by a single 1x1 white pixel texture, tinted/stretched per
  call -- the standard MonoGame technique. Every future rendering module
  will lean on this, not just `UiTheme.cs`.
- **RNG determinism**: several Python modules (`upgrades.py`, `items.py`)
  accept an injectable `rng` parameter specifically so tests can seed it. Kept
  that shape in C# (`Random? rng = null`, defaulting to `Random.Shared`)
  rather than reaching for `Random.Shared` everywhere, so the equivalent
  tests stay reproducible. Note C# and Python use different PRNG algorithms,
  so "reproducible" means the same seed gives the same result *within* one
  language, not an identical sequence across both.
- **Test parallelism**: xUnit runs different test *classes* in parallel by
  default (Python's unittest runs everything sequentially, so this never came
  up there). Any test class touching `GameProfile.Profile`/`SavePath`
  (directly, or indirectly through `Keybinds`) must carry
  `[Collection("GameProfileState")]` (see
  `RotBoiRemastered.Tests/Systems/GameProfileStateCollection.cs`) or it will
  intermittently fail from racing against other test classes mutating the
  same shared static state. Found this the hard way: an early version of
  `RecordRun`'s test failed about 1 run in 5 before the fix.
