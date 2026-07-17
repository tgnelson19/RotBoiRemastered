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
3. **`World/`** -- the camera/coordinate transforms and map generation, since
   entities need these to place and collide with themselves. **Done, minus
   pixel rendering of tiles/walls/decorations** (deferred to pair with
   `Entities/` -- see `World/README.md`'s "Explicitly deferred" section).
   This pass also did a real architectural cleanup, not just translation:
   `background.py`'s pile of module globals (reassigned via `global` in
   `configure_battleground`), `id()`-keyed caches that only ever held one
   entry, per-tile `[type_int, Rect]` lists, and raw tile-id ints/building-
   style strings all became proper instance classes, lazily-computed fields,
   a `TileType` enum, and a `BuildingStyle` enum respectively -- see
   `World/Battleground.cs`'s and `World/Camera.cs`'s doc comments.
4. **`Entities/`** -- bullets, enemy projectiles/portals, the `Enemy` base
   class, XP bubbles, loot crates, damage text (plus `UI/ItemCards.cs`, its
   natural companion). **Done for those; deferred: `enemyTypes.py`'s ~1600-line
   subclass catalog, `Player.cs` (`character.py` + `characterStats.py`, the
   ~1550-line combined player-entity/run-state/game-loop object), and
   `bossTypes.py` (~4750 lines)** -- see `Entities/README.md`'s "Explicitly
   deferred" section for the reasoning on each. This pass also split every
   entity's combined Python update-and-draw method into separate
   Update/Draw calls (Update mutates state, Draw only reads it), so physics/
   collision/expiry logic is unit testable without a GraphicsDevice; added
   `Core/Simulation.cs` (frame-scale/timer-step clock) and grew
   `Core/Primitives2D.cs` to cover circles/ellipses/arcs/filled polygons.
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
- **`Battleground.TileSize` is a compile-time constant (50)**, not an
  instance field, matching how `vH.tileSizeGlobal` is a true global constant
  for the whole game in the Python original (the Python test suite
  monkey-patches it to 10 purely for smaller test numbers -- not something
  C# can do to a `const`, so `BattlegroundTests`'s spawn/collision tests use
  a hand-built small room fixture at the real 50px size instead).
- **Dropped `find_nearest_open_rect`'s unused `size` parameter** -- the
  Python original took it but never referenced it in the function body
  (it only ever used `world_rect`'s own width/height). Porting dead
  parameters isn't worth preserving for its own sake.
- **`GamePaths.ActivateSelected()` returns the new `Battleground`** instead
  of mutating a hidden global in place (Python's `activate_selected()` calls
  `bG.configure_battleground(active_key)`, which reassigns
  `background.py`'s module-level `currRoomRects` etc.). There's no "current
  battleground" singleton in the C# port yet -- whatever ends up owning that
  (a future session/world container, once `Entities/` and
  `Core/RotBoiGame.cs`'s state machine are wired together) holds the
  returned reference itself.
- **Sound path's battleground isn't cached** -- `configure_battleground`
  special-cased `"sound"` to reuse a module-level `basicRoomRects` generated
  once at import, presumably as a minor performance optimization. All five
  generators are fully deterministic (their "noise" is hash-like arithmetic
  on tile coordinates, not actual RNG calls), so `CreateForPath("sound")`
  just regenerates it -- identical result, and cheap enough (sub-millisecond
  for a ~100x100 grid) that the caching complexity isn't worth carrying over.
- **Entity Update/Draw split**: every Python entity in the original combined
  physics/state mutation and rendering into one method (`updateAndDrawBullet`,
  `drawAndUpdateDamageText`, `updateBubble`, `updateAndDraw`, `drawEnemy`
  mutating `visualAttackTimer`, etc.). Every ported entity in `Entities/`
  splits this into `Update` (mutates state, no `SpriteBatch`) and `Draw`
  (reads state, never mutates) instead. This is the single cleanup pattern
  applied most broadly this pass -- see `Entities/README.md`.
- **No mesh/vertex renderer for filled shapes**: `Primitives2D`'s new
  `FillCircle`/`FillEllipse`/`FillPolygon` all rasterize via horizontal
  scanline `FillRect` strips (or, for polygons, an even-odd scanline fill).
  This is slower than a real mesh would be and has no anti-aliasing, but
  needed no new rendering infrastructure beyond the existing
  stretched-1x1-pixel technique -- fine for this game's entity counts.
  `Arc`/`CircleOutline`/`EllipseOutline`/`Polyline` sample points and
  connect them with `Line` calls the same way.
- **Camera/player position stay explicit parameters, not globals**: every
  entity's `Draw` takes `Camera camera, Vector2 playerWorldPosition, Vector2
  screenShake` explicitly (`bG.world_to_screen` read `playerPosX`/
  `playerPosY`/`screenShakeX`/`screenShakeY` as module globals in Python).
  Continues the same cleanup `World/Camera.cs` established -- there's still
  no "current player" singleton anywhere in the C# port; whatever owns that
  once `Player.cs` exists is what these parameters will come from.
- **`EnemyProjectile.Trail` stores world-space points**, not screen-space
  pixels like `enemyProjectile.py`'s `self.trail`. Python recomputed and
  appended a screen position once per frame immediately before drawing it,
  which is only correct because update-then-draw always happened
  back-to-back in the same frame. World-space points converted through the
  camera at Draw time have no such ordering dependency and stay correct
  even if the camera rotates between when a point was recorded and drawn.
- **Several entities had constructor/method parameters Python accepted but
  never read** (`DamageText.drawAndUpdateDamageText(pDX, pDY)`,
  `ExperienceBubble`'s constructor `frameRate` arg and `updateBubble`'s
  `pDX, pDY`, `Bullet`'s unused... none in Bullet, but see each file's doc
  comment) -- dropped rather than ported faithfully, consistent with
  `Battleground.FindNearestOpenRect`'s dropped `size` param from the World/
  pass. `ExperienceBubble`'s dead `frameRate` constructor slot was repurposed
  for an injectable `Random? rng`, matching this port's usual testability
  convention instead of adding a new unused parameter.
- **Test parallelism**: xUnit runs different test *classes* in parallel by
  default (Python's unittest runs everything sequentially, so this never came
  up there). Any test class touching `GameProfile.Profile`/`SavePath`
  (directly, or indirectly through `Keybinds`) must carry
  `[Collection("GameProfileState")]` (see
  `RotBoiRemastered.Tests/Systems/GameProfileStateCollection.cs`) or it will
  intermittently fail from racing against other test classes mutating the
  same shared static state. Found this the hard way: an early version of
  `RecordRun`'s test failed about 1 run in 5 before the fix.
