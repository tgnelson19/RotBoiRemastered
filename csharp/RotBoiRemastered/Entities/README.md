# Entities

Runtime game objects. Mapping from the Python source:

- `Bullet.cs` <- `bullet.py`. **Done.**
- `Enemy.cs` <- `enemy.py` (base class + `HitResult`). **Done**, marked
  `virtual` throughout for the not-yet-ported subclass catalog to override.
- `EnemyProjectile.cs` <- `enemyProjectile.py`. **Done** -- all six paths
  (linear, sine, pool, laser, bomb, orbit), splitting, and shape drawing.
- `ExperienceBubble.cs` <- `experienceBubble.py`. **Done**, including
  celebration particles.
- `LootCrate.cs` <- `lootCrate.py`. **Done.**
- `DamageText.cs` <- `damageText.py`. **Done.**
- `ProjectilePortal.cs` <- `projectilePortal.py`. **Done** -- orbit/figure8/
  square/tornado/wave movement paths, burst/wave firing, disable/regen.

Every file above ported with the same Update/Draw split: Python combined
physics-and-drawing in one method (`updateAndDrawBullet`,
`drawAndUpdateDamageText`, etc.) -- Update now does all state mutation and
Draw only reads state, so movement/collision/expiry logic is unit testable
without a GraphicsDevice. See each file's doc comment for entity-specific
cleanup notes (dropped dead parameters, world-space vs. screen-space trail
storage, etc.).

- **`enemyTypes.py`'s full enemy catalog** -- ~20 archetypes, the
  `RuntimeEncounter` squad coordinator, and the `EnemyCatalog`
  registry/spawn-rule engine. **Done.** See "Enemy catalog" below for the
  file breakdown and the cleanups this pass made.

## Enemy catalog (enemyTypes.py)

- `EnemyCatalogData.cs` <- `TIER_BALANCE`, `FAMILY_IDENTITIES`,
  `MODIFIER_RULES`, `EncounterPackage`/`ENCOUNTER_PACKAGES`,
  `BASE_ENEMY_SPEED_SCALE`, `_normalise`. Pure data, no rendering dependency.
- `RuntimeEncounter.cs` <- `RuntimeEncounter`. Takes `screenHeight` as an
  explicit constructor parameter instead of reading `vH.sH` (same cleanup as
  `Enemy.AwarenessRange`).
- `WanderingRangedEnemy.cs` + `ShotgunEnemy.cs`/`VolleyEnemy.cs`/
  `LaserEnemy.cs`/`BombEnemy.cs` <- the kite-and-fire ranged family.
- `ChildEnemy.cs`, `ParentEnemy.cs`, `PillarEnemy.cs`, `SnakeEnemy.cs`,
  `BannerMinion.cs`, `BannerCaptain.cs`, `RammerEnemy.cs`, `WarderEnemy.cs`,
  `SplitterEnemy.cs`, `CollectorEnemy.cs`, `ArsenalMiniBoss.cs` <- one file
  each for the remaining named archetypes.
- `EnemyDefinition.cs` + `EnemyCatalog.cs` <- `EnemyDefinition`,
  `EnemyCatalog` (register/available/choose/create/apply_modifier/spawn/
  spawn_encounter/spawn_patrol), `_tier_color`/`_tiered_family`/
  `_register_defaults`.

### Cleanup vs. the Python original

- **`EnemyUpdateContext`** replaces loose `Update` parameters. Two enemy
  types reached into `characterStats.py`'s module-level `enemyHolster`/
  `experienceList` globals (`BannerCaptain` to command sibling minions,
  `CollectorEnemy` to steal nearby XP bubbles) -- rather than adding those
  as ignored parameters to the other ~18 overrides, every `Enemy.Update`
  takes one context object carrying player position, the battleground, the
  projectile sink, all live enemies, and all XP bubbles. See `Enemy.cs`'s
  doc comment.
- **`EnemyFactory` delegates replace `enemy_class: type` + an `options`
  dict forwarded as `**kwargs`.** Python's `create()` built a kwargs dict
  per definition and special-cased `definition.enemy_class is SnakeEnemy`
  to inject `segment_count`. Each `EnemyDefinition` now carries a factory
  closure built at registration time that already knows exactly which
  constructor to call and with what tier string/phase order/segment-count
  formula -- `create()` never branches on what concrete type it's building.
- **`Update`/`Draw` are split on every subclass**, same as the base `Enemy`
  cleanup -- see each file's doc comment for anything subclass-specific
  (dropped redundant double-decremented timers in `WanderingRangedEnemy`/
  `VolleyEnemy`/`BombEnemy`, `SnakeEnemy` segment ids as strings instead of
  ints so `TakeDamage`'s `partId` stays one real type across every enemy,
  etc).
- **`EnemyCatalog.Shared`** replaces the Python module-level `ENEMY_CATALOG`
  singleton (auto-populated as an import side effect) with an explicit
  `CreateDefault()` factory; a plain `new EnemyCatalog()` still gives an
  empty, unregistered catalog for tests that want an isolated roster.

### Known gaps (not scope creep -- genuinely out of this pass)

`gamePaths.py`'s `ApplyEnemyIdentity`/`ENCOUNTERS` (`_PathEnemyCatalog`)/
`RegisterExclusiveEncounter`/`TuneNewProjectiles` still apply per-path stat
multipliers and boss-catalog wiring on top of freshly created enemies --
deferred alongside `GamePaths.cs`'s existing boss-content gap (see
`World/README.md`).

- **`Player.cs`** <- the player-entity slice of `character.py` +
  `characterStats.py`. **Done** -- world position, movement/dash, wall
  collision, drawing. Player is deliberately thin: most player-facing stats
  (speed, dash timers, health) live on `Systems/RunState.cs` instead, since
  Move/Draw read and write them but Player itself doesn't own their
  identity. See `Systems/README.md` for `RunState.cs`/`GameSession.cs`,
  which together with this file cover the rest of `character.py` +
  `characterStats.py`'s non-boss gameplay loop.

- **`Beaudis.cs`** <- `bossTypes.py`'s `Beaudis` (the run's level-10
  midpoint boss; one of fifteen boss classes in that ~4750-line file --
  see "Explicitly deferred" below for the rest). **Done**, including its
  five-phase state machine (four damage phases + a finale survival phase
  with four orbiting `ProjectilePortal`s), stagger/phase-protection gating
  on `TakeDamage`, and the death fade. `GameSession.cs` now really spawns
  it on the natural level-10 trigger (previously a documented no-op), sets
  `BeaudisDefeated` on death, and has boss debug hotkeys
  (`HandleBossDebugControls`, pattern-matched against `Beaudis` since
  `RunState.ActiveBoss` is untyped and it's the only boss ported so far).
  `Entities/Enemy.cs`'s `TransitionCleanupRequested`/`TransitionCleanupOwner`
  (previously declared directly on `ArsenalMiniBoss`, its only prior
  setter) were promoted to the base class so `GameSession`'s cleanup logic
  works polymorphically for any boss/miniboss instead of one type-check per
  concrete type -- a null `TransitionCleanupOwner` now means "clear every
  live enemy projectile" (Beaudis's isolated encounter), not just "no
  owner set." See `Beaudis.cs`'s doc comment for the specific dead-in-this-
  port fields (`damagePhaseHistory`, `perfectStagger`,
  `staggerRecoveryRemaining`, `runeSilenceRemaining`, ...) shared in name
  only with Dissonance, dropped until that boss and the still-deferred
  `drawBossHealthBar` HUD function actually need a shared contract for them.

## Explicitly deferred (not in Entities/ yet)

- **The rest of `bossTypes.py`'s ~4750 lines** -- fourteen more boss
  classes beyond `Beaudis.cs`. Scoped down deliberately this pass rather
  than attempted in one shot:
  - **`Dissonance`** (~1780 lines on its own) -- the run's level-20 final
    boss. Nine phases across three "acts," each with bespoke attack
    patterns (rune cannon, portal relay, mirror-step teleport, rotating
    diamond field, crossfire carousel, event horizon, last-word callback
    cycling...), cinematic phase transitions, arena-boundary/mask
    rendering (a star-shaped exterior blackout), a death spectacle, and
    polarity-based player-bullet routing through its portals. By far the
    most complex single class in the codebase -- its own dedicated pass.
  - **`PathChaseBoss`** and its eight subclasses/pairs (`TouchPortal`/
    `PlagueTouchBoss`/`Bair`/`Sting`, `Ishe`/`Chronos`,
    `SinChemesthesisBoss`/`Kage`/`Rot`, `PhantasiaBoss`/`Hypno`/`Malady`)
    -- alternate mid/final bosses for non-"sound" content paths (see
    `gamePaths.py`'s `boss_key()`), each with its own arena-constraint
    shape and terrain/persistent-hazard mechanics. `Malady` alone
    (~680 lines) has a fully custom procedural "puppet" body with jointed
    limb rendering.
  - **`BossDefinition`/`BossCatalog`** -- straightforward once every boss
    behind them exists (same `register`/`spawn` shape as `EnemyCatalog`),
    but genuinely blocked on the above, not worth stubbing early.

  Until these exist, every boss-specific branch in
  `Player.cs`/`GameSession.cs` for anything beyond Beaudis (boss movement
  obstacles/arena constraints, arena-radius projectile clipping, the
  natural Dissonance spawn trigger, the "C" rune-cannon debug hotkey,
  portal-hit bullet routing, `gamePaths.py`'s per-path boss content-key
  selection) is a documented no-op -- see those files' doc comments for
  the specifics.
