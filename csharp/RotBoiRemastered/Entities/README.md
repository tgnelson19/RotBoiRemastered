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

- **`Dissonance.cs`** <- `bossTypes.py`'s `Dissonance` (the run's level-20
  final boss; ~1780 lines on its own, by far the most complex single class
  in the codebase). **Done** -- the full nine-phase, three-act state
  machine (rune cannon, portal relay, mirror-step teleport with landing
  echoes, rotating diamond minefield, crossfire carousel, event horizon,
  last-word callback cycling, health-gated survival phases per act,
  cinematic phase transitions), stagger/fracture/rune-disruption gating on
  `TakeDamage`, polarity-based player-bullet routing through its portals
  (`RoutePlayerBullet`), and the full visual spectacle (rotating 3D-
  projected cube with aura, motion trail, arena boundary/mask/rune
  inscription, death spectacle, phase-announcement bubble, act-transition
  veil, perfect-break flash). `GameSession.cs` now spawns it on the natural
  level-20 trigger (and the hidden debug-summon hotkey, matching Python:
  the debug key always resolves to the *final* boss, never Beaudis),
  clamps player movement inside `ArenaRadius`, routes portal-hit player
  bullets before normal pierce/damage consumption, computes screen shake
  as an explicit per-frame value (`ComputeScreenShake`, see its doc
  comment) instead of writing a global, sets `GameCompleted` on death, and
  extended `HandleBossDebugControls` with the "C" rune-cannon-reset hotkey.
  See `Dissonance.cs`'s doc comment for the full list of design decisions
  (nearly every field is a public settable property, unlike `Beaudis.cs`'s
  curated surface -- driven by how extensively the Python test oracle
  manipulates this boss's state directly; `_arena_center()` becomes a
  cached field from an explicit `Battleground` constructor parameter;
  dead-in-this-class fields dropped).

- **`PathChaseBoss.cs`** <- bossTypes.py's `PathChaseBoss` ("Configurable
  three-phase placeholder for rapidly prototyping path bosses" -- the
  shared base for every alternate mid/final boss on non-"sound" content
  paths; see `gamePaths.py`'s `boss_key()`). **Done**, along with two of
  its four concrete families -- see "Explicitly deferred" below for the
  other two and why this pass stopped here. Python's ~24 overridable class
  attributes (`bossName`, `phaseLabels`, `bodyColor`, `movementSpeed`, ...)
  become one `PathChaseBossConfig` record each subclass builds (using a
  `with` expression against its parent's config to mirror Python's partial
  class-attribute override exactly) instead of ~24 C# virtual properties --
  calling virtual members from the base constructor to compute
  `size`/`speed`/etc. before `base(...)` runs is a well-known C# hazard,
  so subclasses build the record and pass it up explicitly instead. Arena
  shapes (circle/square/triangle/jagged/atomic), the player-position arena
  clamp (`ConstrainPlayerPosition`), and the arena-boundary/mask/timer-ring
  drawing are all ported. `Core/Primitives2D.cs` gained `DrawOutsideArena`
  (the star-shaped-exterior-blackout helper, previously private to
  `Dissonance.cs`, now shared since `PathChaseBoss` needs the identical
  technique for its own arena shapes).
  - **`Ishe.cs`/`Chronos.cs`** -- "THE NEAR HORIZON"/"THE LAST SECOND," a
    simple rush-pattern pair with no phase-attack override (stock
    `PathChaseBoss` dispatch) beyond a sight-symbol icon drawn over the
    body. The simplest possible full pair, useful as the base class's own
    test bed.
  - **`TouchPortal.cs`/`PlagueTouchBoss.cs`/`Bair.cs`/`Sting.cs`** -- the
    Touch content path's mid/final bosses ("THE FIRST LOCK"/"THE THING THE
    PRISON KEPT"). `TouchPortal` (a `ProjectilePortal` subclass -- which
    had to become unsealed and gained a first `virtual Draw` for this)
    marches along the arena wall and gates phase transitions.
    `PlagueTouchBoss` fully overrides `Update`/`Draw` (its own portal-driven
    combat and movement, not the base's chase-the-player behavior) but
    still calls `base.Draw` for the shared arena rendering. `PlagueSigils.All`
    holds the ten shared plague sigils Bair/Sting's `phaseSigils` index into.
- **`BossCatalog.cs`** <- `BossDefinition`/`BossCatalog`/`BOSS_CATALOG`.
  **Done** for the six bosses ported so far (`beaudis`/`dissonance`/
  `ishe`/`chronos`/`bair`/`sting`) -- same `EnemyFactory`-delegate/
  `CreateDefault()` cleanup as `EnemyCatalog.cs`. Not wired into
  `GameSession`'s actual spawn flow (which constructs `Beaudis`/`Dissonance`
  directly): `gamePaths.py`'s per-path boss-key selection -- the thing that
  would ever ask the catalog for `ishe`/`bair`/etc. -- isn't ported, so
  this stays standalone infrastructure ready for whenever that selection
  exists, same reasoning as `EnemyCatalog.cs` before `GameSession` existed.

## Explicitly deferred (not in Entities/ yet)

- **The rest of `bossTypes.py`'s ~4750 lines** -- two more `PathChaseBoss`
  families, deliberately not attempted this pass:
  - **`SinChemesthesisBoss`/`Kage`/`Rot`** ("THE FIRST REACTION"/"THE FIELD
    THAT REMAINS") -- a real stagger/fracture system (unlike Ishe/Bair/
    Sting, which use plain `Enemy.TakeDamage`), plus `Rot`'s crystal-wall
    terrain obstacles (their own hitbox/damage subsystem, `movement_obstacles()`)
    and direct integration with `characterStats.py`'s `bossAfflictions`
    (exposure/pull) and a `player_build_snapshot()` function that doesn't
    exist anywhere in this port yet. Genuinely a new subsystem, not just
    more config.
  - **`PhantasiaBoss`/`Hypno`/`Malady`** ("THE DREAM COURT"/final bosses) --
    integrates with `RunState.DreamState`'s belief/truth mechanics (which
    *does* already exist, unlike the Sin family) via true/false "commandment"
    rules, but `Malady` alone is ~680 lines of a fully custom procedural
    "puppet" body with jointed limb rendering -- its own dedicated pass.

  Until these exist, `Player.cs`/`GameSession.cs`'s boss-movement-obstacle
  branch for anything beyond `PathChaseBoss.ConstrainPlayerPosition`
  (`Rot`'s crystal-wall `movement_obstacles()`) and `gamePaths.py`'s
  per-path boss content-key selection (which boss variant counts as "the
  mid/final boss" on the active path -- always the "sound" path's
  Beaudis/Dissonance here) stay documented no-ops -- see those files' doc
  comments for the specifics.
