# Systems

Rules and data with no rendering dependency -- the most straightforward files to
port first since they were deliberately kept pygame-free in the Python original.

- `Upgrades.cs` <- `upgrades.py` (frozen dataclasses -> C# records; keep the same
  weighted-rarity-roll shape, including the injectable RNG for test determinism)
- `Items.cs` <- `items.py`. **Done and expanded** -- rarity, independent
  weighted F-S grade, slot-exclusive weighted affixes, grade upgrades,
  modifier rerolls, save migration, and one shared effect-scaling pipeline.
  It also owns the five path-bound Core-Forged packages. In Hard Mode, Epic,
  Legendary, and Mythical regular drops roll 10%, 20%, and 35% Core chances;
  Unique items are excluded. Core effects are fixed additions after normal
  rarity/grade scaling so exact bonuses such as Ache's +2 bullets remain exact.
- `Keybinds.cs` <- `keybinds.py` (action -> key map, persisted like GameProfile)
- `GameProfile.cs` <- `gameProfile.py` (swap JSON-on-disk for the same shape;
  consider `System.Text.Json` + a settings folder under `%AppData%`)
- `NewGamePlus.cs` owns the independent per-path NG+0-7 ladder. A clear
  unlocks only the next tier for that path; run reset captures the selected
  tier. Enemy health/effective damage use `1.5^tier`, completion rewards use
  `2^tier`, and item grade, rarity, and Core chances receive tier-aware rolls.
  Unlock and selection dictionaries are normalized for old or malformed saves.
- `StatTrack.cs` <- the shape shared by characterStats.py's
  `collectiveStats`/`collectiveAddStats`/`collectiveMultStats` dicts. **Done**
  -- one object (base value + additive stack + multiplicative stack) per
  upgrade stat, replacing three parallel dicts that always moved in lockstep.
- `RunState.cs` <- `characterStats.py`. **Done** -- level/exp/time
  progression, derived combat stats (via `Stats: Dictionary<string,
  StatTrack>`), entity holsters, equipment, `DreamState`/`BossAfflictions`
  (module dicts + free functions -> instance classes + methods, same
  cleanup as every other stateful module in this port), upgrade-collection
  tracking, `Reset()`. Answers the "one god-object or split up?" question
  above: kept as one class (it's genuinely one bounded context -- the
  current run's state), but internally organized into clearly-scoped
  properties and the nested `DreamState`/`BossAfflictions` helper classes
  rather than ~80 flat fields. `PlayerBuildSnapshot` (record) +
  `BuildSnapshot()` were added for `Entities/Rot.cs`'s Envy phase --
  ported from `characterStats.py`'s `player_build_snapshot()`.
  `HardMode` is captured from the profile at run reset. It suppresses passive
  vitality recovery, lifesteal, and healing from maximum-health changes;
  `FillHealthForMilestone` is the single gameplay heal used when buying a level.
- `GameSession.cs` <- `character.py`'s "handling*"/"update*"/"draw*" free
  functions + `resetAllStats()`/`combarinoPlayerStats()`/
  `handleLevelingProcess()`. **Done for the non-boss gameplay loop**: bullet
  firing/movement, non-boss enemy spawning (via `Entities/EnemyCatalog.cs`
  directly, not gamePaths.py's per-path wrapper) and update/draw, bullet-
  enemy collision and death handling, stored-XP pickup plus explicit paid
  leveling handoff, EXP-funded equipped-item reforging, loot
  crate pickup, player damage-taking. One session object owns the player,
  run state, battleground, camera, leveling screen, and (now)
  `UI/InformationSheet.cs`'s sidebar HUD, and orchestrates them each frame
  -- see its doc comment for the full, explicit list of deferred
  boss-specific branches (nothing was silently dropped). Every combined
  Python update-and-draw function was split into separate Update/Draw
  methods here, same reasoning as the rest of this port's entities: Python
  interleaved them purely to share one loop, and drawing order was never
  semantically significant, so splitting makes the spawn/collision/
  pressure-budget logic unit testable without a GraphicsDevice.
  `GameSession` also owns Camera re-centering against
  `InformationSheet.ArenaWidth` (constructor/`Resize`/`ResetAll` --
  see UI/README.md) and
  `SelectBountyTarget()`/`BountyInfo` (ported from
  character.py's `selectBountyTarget()`, feeds InformationSheet's
  objective panel). Now that `Entities/Beaudis.cs`/`Entities/Dissonance.cs`
  exist (see Entities/README.md), the natural level-10 and level-20 boss
  triggers really spawn (`SpawnBoss`, shared arena-clearing + placement
  prep mirroring `BossCatalog.spawn` -- with an optional forced spawn rect
  for Dissonance, which owns its entire arena and must land exactly at its
  center rather than going through the generic nearest-open-rect search),
  boss defeat sets `RunState.BeaudisDefeated`/`GameCompleted` per boss
  type, and `HandleBossDebugControls` ports the boss-practice hotkeys
  (phase-jump/relock/lock/force-stagger, plus Dissonance's "C" rune-cannon
  reset) for both boss types (duplicated per type rather than introducing
  a shared interface for two implementors -- see the method's shape).
  `MovePlayer` now also clamps the player inside `Dissonance.ArenaRadius`
  (Python's `elif hasattr(boss, "arenaRadius")` branch), and
  `HandleDamagingEnemies` routes portal-hit player bullets through
  `Dissonance.RoutePlayerBullet` before normal pierce/damage consumption.
  `RunState.BossDebugRequested`/`BossDebugInvincible` are back (dropped
  since the Player.cs/GameSession pass, promised to return once a boss
  existed) -- both are now fully wired (`BossDebugInvincible` in
  `HurtPlayer`, `BossDebugRequested` now spawns Dissonance, matching
  Python's debug hotkey always summoning the *final* boss, never Beaudis).
- Now that `Entities/Rot.cs` exists (see Entities/README.md), three more
  touch points are wired: `MovePlayer` computes
  `State.ActiveBoss is Rot rot ? rot.MovementObstacles() : null` and passes
  it to `Player.Move`'s new `obstacles` parameter (checked alongside the
  existing wall-collision test on both axes -- kept boss-type-awareness in
  `GameSession` only, per `Player.cs`'s own boss-agnostic doc comment);
  `HurtPlayer` applies `projectile.Affliction` to `State.BossAfflictions`
  on a colliding hit (Python's `hurtPlayer()` boss-affliction-projectile
  branch, previously deferred since nothing produced one); and
  `UpdateEnemies`'s `EnemyUpdateContext` now populates
  `Camera`/`BossAfflictions`/`PlayerBuildSnapshot` (`RunState.BuildSnapshot()`,
  ported from `characterStats.py`'s `player_build_snapshot()` -- an
  immutable summary of upgrade types/categories/stats/dominant-offense a
  boss may inspect without mutating the build) -- all three exist solely
  for `Rot`'s crystal-wall terrain and Envy-phase build-reading attack, but
  every `Enemy.Update` override still shares the one context shape.
- Now that `Entities/Malady.cs` exists (see Entities/README.md), two more
  `EnemyUpdateContext` fields are populated by `UpdateEnemies`:
  `PlayerBullets` (`State.BulletHolster`, for the Sabbath/REST phase's "did
  the player fire" check) and `DreamState` (`State.DreamState`, for a boss's
  own direct `AlterBelief` calls on rule violations/offering pickups, and
  for the dream-court field diagram's belief-driven intensity). Both are
  read-only from the boss's perspective except `DreamState.AlterBelief`,
  which the `PhantasiaBoss` family calls on itself exactly like Python's
  `cS.alter_belief(...)`.
- Now that `Core/RotBoiGame.cs` actually drives the game loop (see
  Core/README.md), `GameSession` gained the last few per-frame wrappers it
  needed: `DrawBackground` (owns a `World/ArenaRenderer.cs` instance,
  self-manages its own scissor-clipped `SpriteBatch.Begin`/`End` pair --
  see that class's doc comment), `RecoverPlayerHealth` (a one-line wrapper
  around `RunState.RecoverHealth()`, which already existed fully ported but
  had no per-frame caller until now), and `DrawBountyIndicator` + a public
  static `BountyArrowGeometry` helper (ported from character.py's
  `drawBountyIndicator`/`_bounty_arrow_geometry` -- calls
  `SelectBountyTarget()` fresh each time rather than caching, matching
  `DrawInformationSheet`'s existing no-caching precedent for the same
  lookup). This closes out the "explicitly deferred" list in this class's
  own doc comment except for gamePaths.py's per-path enemy
  identity/projectile tuning and per-path boss selection, which stay
  deferred for their own sake (scope, not a blocker) -- see
  `World/README.md`'s `GamePaths.cs` entry.
