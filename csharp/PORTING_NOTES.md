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
   natural companion), the full `enemyTypes.py` catalog (~20 archetypes,
   `RuntimeEncounter` squad coordination, `EnemyCatalog`'s registry/spawn-rule
   engine), and finally `Entities/Player.cs` (the player entity itself --
   world position, movement/dash, wall collision, drawing). **Done. Deferred:
   `bossTypes.py` (~4750 lines)** -- see `Entities/README.md`'s "Explicitly
   deferred" section. This pass also split every entity's combined Python
   update-and-draw method into separate Update/Draw calls (Update mutates
   state, Draw only reads it), so physics/collision/expiry logic is unit
   testable without a GraphicsDevice; added `Core/Simulation.cs`
   (frame-scale/timer-step clock) and grew `Core/Primitives2D.cs` to cover
   circles/ellipses/arcs/filled polygons. The enemy-catalog half introduced
   `EnemyUpdateContext` (replacing two enemy types' direct reads of
   `characterStats.py` globals) and `EnemyFactory` delegates (replacing
   `enemy_class: type` + a `**kwargs` options dict) -- see
   `Entities/README.md`'s "Enemy catalog" section.
5. **`UI/StatCards.cs`, `UI/LevelingHandler.cs`, `UI/Menus.cs`** -- the
   upgrade-card draft screen and pause/results screens. **Done.** Also
   confirmed `hpBar.py`/`levelBar.py`/`dashBar.py` as dead code (grepped,
   unreferenced anywhere) and skipped them rather than porting unused files.
   This pass introduced `LevelUpStatSnapshot`/`RunResultsSnapshot`
   (replacing direct `characterStats.py` reads, same pattern as
   `EnemyUpdateContext`) and a `MenuAction` enum (replacing direct
   `vH.state = ...` assignment) -- see `UI/README.md`'s "Cleanup vs. the
   Python original" section. `UI/InformationSheet.cs` (the sidebar HUD) was
   deferred at this point -- it reads dozens of fields across nearly the
   whole player/run state, enough that snapshotting it cleanly was really
   the same design question as `characterStats.py`'s data model; picked
   back up in step 7 below once that model existed.
6. **`Systems/RunState.cs` + `Systems/GameSession.cs`** -- the rest of
   `characterStats.py` (run-scoped state) and `character.py` (the
   non-boss gameplay loop: firing, enemy spawning/update, collision/damage,
   XP and loot pickup, leveling handoff). **Done for the non-boss loop** --
   see `Systems/README.md` and `GameSession.cs`'s doc comment for the full,
   explicit list of deferred boss-specific branches. Introduced
   `StatTrack.cs` (replacing three parallel dicts with one tracker object
   per upgrade stat) and finally answered `characterStats.py`'s "one
   god-object or split up?" open question from step 1: kept as one
   `RunState` class (it's genuinely one bounded context) but with the
   player-entity slice (world position, movement/drawing) broken out into
   `Entities/Player.cs` in step 4, and the orchestration functions
   (`character.py`'s "handling*"/"update*"/"draw*" free functions) moved onto
   `GameSession`, which owns the player/run-state/battleground/camera/leveling
   screen together as one run-in-progress object.
7. **`UI/InformationSheet.cs`** -- the sidebar HUD deferred in step 5,
   picked back up now that `RunState`/`GameSession` exist: it takes
   `RunState` directly rather than a snapshot type, since a snapshot would
   just duplicate nearly all of it. **Done** -- equipment pentagon with
   drag-and-drop (against `RunState.Equipment`/`NearbyCrate`/
   `LootCrateList`), the loot panel, build identity, weapon stat rows,
   objective/bounty panel, recent-picks table, tooltip. `GameSession` now
   also owns `InformationSheet` (Camera re-centering against
   `InformationSheet.ArenaWidth`) and a new `SelectBountyTarget()`/
   `BountyInfo` (ported from character.py's `selectBountyTarget()`) --
   see `UI/README.md`'s "InformationSheet.cs" section for the drag-gesture
   split (`DrawSheet` then `HandleDrag`, matching
   `LevelingHandler.DrawCards`/`PlayerClicked`) and everything else still
   deferred alongside it (the bounty-arrow/boss-health-bar/tutorial-hint/
   low-health-warning/run-complete-banner HUD overlays character.py layers
   on top of the sheet, and the title screen).
8. **`Entities/Beaudis.cs`** -- the first of `bossTypes.py`'s fifteen boss
   classes (~4750 lines total). Scoped to just the level-10 midpoint boss
   this pass, deliberately: `Dissonance` alone is ~1780 lines of bespoke
   nine-phase/three-act attack patterns and cinematic rendering, and the
   `PathChaseBoss` family (eight more subclasses for non-"sound" content
   paths, `Malady`'s ~680-line procedural puppet body among them) is its
   own separate scope again -- see `Entities/README.md`'s "Explicitly
   deferred" section for the full breakdown and why. **Beaudis: done** --
   five-phase state machine, stagger/phase-protection gating, and the
   finale survival phase's four orbiting `ProjectilePortal`s. `GameSession`
   now really spawns it on the natural level-10 trigger (previously a
   no-op), sets `BeaudisDefeated` on death, and gained
   `HandleBossDebugControls` (phase-jump/relock/lock/force-stagger
   hotkeys). `Entities/Enemy.cs`'s `TransitionCleanupRequested`/
   `TransitionCleanupOwner` (previously `ArsenalMiniBoss`-only) moved to
   the base class so that cleanup works polymorphically for any boss/
   miniboss. `RunState.BossDebugRequested`/`BossDebugInvincible` are back,
   as promised when they were dropped in step 6.
9. **`Entities/Dissonance.cs`** -- the run's level-20 final boss, ~1780
   lines on its own and by far the most complex single class in the
   codebase. **Done** -- the full nine-phase/three-act state machine (rune
   cannon, portal relay, mirror-step teleport, rotating diamond minefield,
   crossfire carousel, event horizon, last-word callback cycling,
   health-gated survival phases, cinematic transitions), stagger/fracture/
   rune-disruption gating, polarity-based player-bullet portal routing,
   and the full visual spectacle (rotating 3D cube + aura, motion trail,
   arena boundary/mask/rune inscription, death spectacle, phase
   announcement, act transition, perfect-break flash). Nearly every field
   is a public settable property, unlike `Beaudis.cs`'s curated surface --
   driven directly by how extensively the Python test oracle
   (`tests/test_beaudis_boss.py`, almost entirely about Dissonance despite
   its name) manipulates this boss's state to reach specific attack
   windows without waiting out real cooldowns; this port's own test suite
   needs the same access. `GameSession` gained: the natural level-20 spawn
   trigger and the hidden debug-summon hotkey (which, matching Python,
   always resolves to Dissonance, never Beaudis), an arena-radius player-
   movement clamp, portal-hit bullet routing ahead of normal bullet
   consumption, a `ComputeScreenShake` call (screen shake became an
   explicit per-frame value instead of a global write), `GameCompleted` on
   defeat, and the "C" rune-cannon debug hotkey. The `PathChaseBoss` family
   (eight more subclasses for non-"sound" content paths) and
   `BossDefinition`/`BossCatalog` remain deferred -- see
   `Entities/README.md`'s "Explicitly deferred" section.
10. Wire it all into `Core/RotBoiGame.cs`'s state switch last.

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
- **`EnemyUpdateContext` replaces loose Update parameters for every enemy
  type**, not just the two that need the extra fields. `BannerCaptain.
  updateEnemy` read `characterStats.py`'s module-level `enemyHolster`
  directly to find and command sibling minions; `CollectorEnemy.updateEnemy`
  read `experienceList` directly to steal nearby XP bubbles. Rather than
  adding `allEnemies`/`experienceBubbles` as ignored parameters to the other
  ~18 overrides (or, worse, letting those two reach into some shared
  static/global the way Python did), every `Enemy.Update` takes one context
  object. See `Entities/Enemy.cs`'s and `EnemyUpdateContext`'s doc comments.
- **`EnemyFactory` delegates replace `EnemyDefinition.enemy_class: type`
  plus an `options: dict` forwarded as `**kwargs`** (with a
  `definition.enemy_class is SnakeEnemy` identity check in `create()` to
  inject `segment_count`). Each definition's factory closure, built once at
  registration time, already knows which constructor to call and with what
  tier string/phase order/segment-count formula baked in -- `EnemyCatalog.
  Create()` never branches on what concrete type it's building. This is the
  main new pattern introduced while porting `enemyTypes.py`'s ~20 enemy
  subclasses and the `EnemyCatalog` registry.
- **`SnakeEnemy` segment ids are strings ("0", "1", ...), not ints.** Python
  identified segments by their `enumerate()` index and let
  `take_damage(amount, part_id="head")` accept either that int or the
  string "head"/"body" every other enemy uses, purely because Python never
  checks parameter types. Every `Enemy.TakeDamage(double, string partId)`
  now shares one real, statically-checked contract.
- **`WanderingRangedEnemy`/`VolleyEnemy`/`BombEnemy` dropped a
  double-decrement bug**: Python's `updateEnemy` decremented
  `self.wanderTimer` itself *and* called the inherited `_wander(.2)`, which
  decrements the same timer again internally -- harmless (just made
  disengaged wander-direction changes happen roughly twice as often as
  intended) but clearly unintentional, so the redundant top-level decrement
  was dropped rather than ported faithfully.
- **`RuntimeEncounter` takes `screenHeight` as an explicit constructor
  parameter** instead of reading `vH.sH * (.48 + ...)` -- same cleanup as
  `Enemy.AwarenessRange`, and for the same reason: no gameplay class should
  have an implicit dependency on the real display resolution.
- **`EnemyCatalog.Shared`** replaces the Python module-level `ENEMY_CATALOG`
  singleton (auto-populated by `_register_defaults()` purely as an import
  side effect) with an explicit `CreateDefault()` factory method. A plain
  `new EnemyCatalog()` still gives an empty, unregistered catalog -- useful
  for tests that want an isolated roster instead of the full ~20-definition
  default one.
- **`LevelUpStatSnapshot`/`RunResultsSnapshot` replace direct
  `characterStats.py` reads** in `LevelingHandler`/`Menus`. Both screens
  read run-state fields (`collectiveStats`, `upgradeCollection`,
  `healthPoints`, `currentLevel`, ...) that only exist once `characterStats.py`
  is ported (deferred alongside `Player.cs`) -- these snapshot types are the
  explicit seam, same idea as `Entities/EnemyUpdateContext`.
- **`MenuAction` enum replaces `vH.state = ...` assignment.**
  `menus.py`'s `handle_pause`/`handle_results` directly mutated the global
  game state and called `game.resetAllStats()` themselves. `Menus.
  HandlePause`/`HandleResults` return a `MenuAction` (`None`/`Resume`/
  `Restart`/`ReturnToTitle`) instead, leaving the actual transition to
  whatever ends up owning the main-loop state machine. Matches
  `LevelingHandler.PlayerClicked`'s existing return-a-result contract.
- **Mouse state joined `Core/InputState.cs`** (`MousePosition`/`MouseDown`/
  `MousePressed`, alongside the keyboard fields already there) -- but
  `Menus`/`LevelingHandler` never read it directly. Every draw/input method
  takes mouse position/state as explicit parameters instead, matching
  `UiTheme.DrawButton`'s existing shape; only the eventual game-loop entry
  point touches `InputState` at all.
- **`GameSession` owns the player, run state, battleground, camera, and
  leveling screen together**, orchestrating them via instance methods --
  replacing `character.py`'s free functions that each reached into
  `characterStats.py`'s module globals directly. Every combined Python
  update-and-draw function (`handlingEnemyUpdatesAndDrawing`,
  `handlingEnemyProjectileUpdating`, `updateDamageTexts`,
  `handleLevelingProcess`) is split into separate Update/Draw (or
  Draw-then-HandleInput, matching `Menus.cs`'s shape) methods here, for the
  same reason as every other entity in this port: Python interleaved them
  only to share one loop, and the split makes the underlying
  spawn/collision/pressure-budget logic unit testable without a
  GraphicsDevice.
- **`Player.cs` is deliberately thin.** Most player-facing stats (speed,
  dash timers, health, invulnerability) live on `RunState` rather than
  `Player` itself, since `Player.Move`/`Player.Draw` read and write them but
  don't define their identity -- `Player` owns only world position and the
  two methods that move/render it. `playerRect` (a screen-space rect at the
  camera lock position, cached every frame purely so `drawPlayer` could
  read it) is gone entirely; `Player.Draw` computes it fresh from
  `camera.Lock` on demand.
- **Every boss-specific branch in `Player.cs`/`GameSession.cs` is a
  documented no-op without `bossTypes.py`**: movement obstacles/arena-radius
  constraints in `Player.Move`, arena-radius projectile clipping and the
  boss-spawn branch in `GameSession`, the boss debug hotkeys (not ported at
  all), and portal-hit bullet routing (no current enemy type implements the
  duck-typed `route_player_bullet` Python checked for). `gamePaths.py`'s
  per-path enemy-identity/projectile-tuning wrapper is skipped the same way
  it was in the enemy-catalog pass -- `GameSession` spawns enemies through
  `EnemyCatalog.Shared` directly.
- **Several `characterStats.py`/`character.py` fields were dropped as
  confirmed dead** (assigned, never read anywhere in the repo, verified by
  grep): `baseExpNeededForNextLevel`, `enemyOneInFramesChance` (divided into
  itself every level-up but the result never consumed), `currTileX`/
  `currTileY`, `autoFlop`, `lastUpgrade`/`lastUpgradeAt`. `bossDebugRequested`/
  `bossDebugInvincible` are dropped for now specifically because they only
  matter for the boss debug hotkeys, which have no boss to debug yet --
  they're expected to come back with `bossTypes.py`, unlike the others.
- **Test parallelism**: xUnit runs different test *classes* in parallel by
  default (Python's unittest runs everything sequentially, so this never came
  up there). Any test class touching `GameProfile.Profile`/`SavePath`
  (directly, or indirectly through `Keybinds`) must carry
  `[Collection("GameProfileState")]` (see
  `RotBoiRemastered.Tests/Systems/GameProfileStateCollection.cs`) or it will
  intermittently fail from racing against other test classes mutating the
  same shared static state. Found this the hard way: an early version of
  `RecordRun`'s test failed about 1 run in 5 before the fix.
