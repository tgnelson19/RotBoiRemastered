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
  midpoint boss; one of ten boss classes across five families in that
  ~4750-line file -- all now ported, see the rest of this section).
  **Done**, including its
  five-phase state machine (four damage phases + a finale survival phase
  with four orbiting `ProjectilePortal`s), stagger/phase-protection gating
  on `TakeDamage`, and the death fade. `GameSession.cs` now really spawns
  it on the natural level-10 trigger (previously a documented no-op), sets
  `BeaudisDefeated` on death, and has boss debug hotkeys
  (`HandleBossDebugControls`, pattern-matched against `Beaudis` because
  `RunState.ActiveBoss` is untyped and this encounter retains dedicated
  convenience controls).
  `Entities/Enemy.cs`'s `TransitionCleanupRequested`/`TransitionCleanupOwner`
  (previously declared directly on `ArsenalMiniBoss`, its only prior
  setter) were promoted to the base class so `GameSession`'s cleanup logic
  works polymorphically for any boss/miniboss instead of one type-check per
  concrete type -- a null `TransitionCleanupOwner` now means "clear every
  live enemy projectile" (Beaudis's isolated encounter), not just "no
  owner set." See `Beaudis.cs`'s doc comment for the specific dead-in-this-
  port fields (`damagePhaseHistory`, `perfectStagger`,
  `staggerRecoveryRemaining`, `runeSilenceRemaining`, ...) shared in name
  only with Dissonance and intentionally kept local to the encounters that
  use them. The shared boss HUD now consumes the common enemy/boss contract.

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
  veil, perfect-break flash). Its stable purple/blue faces, deep black core,
  and four gently orbiting satellite cubes now identify the oldest, composed
  Keeper of the First Chord; phase accents stay on runes and warning trim.
  `GameSession.cs` now spawns it on the natural
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
  paths; see `gamePaths.py`'s `boss_key()`). **Done**, along with all four
  of its concrete families (the `PhantasiaBoss` family is a *separate*
  boss lineage entirely, not one of these four -- see "Explicitly deferred"
  below). Python's ~24 overridable class
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
  - **`Ishe.cs`/`Chronos.cs`** -- Ishe retains Sight's small rush-pattern
    lesson. Chronos is now a seven-movement slow/heavy encounter built from
    five-to-eight-segment laser tentacles. Each full route telegraphs for
    1.5–2.35 seconds, the half-health Still Second phase omits three adjacent
    arms as a safe opening, and King's Attrition is a 40-second finale. The
    literal clock/lens body was replaced by one player-like light-blue cube
    rotating in three dimensions inside slow oscillating aura bands.
  - **`TouchPortal.cs`/`PlagueTouchBoss.cs`/`Bair.cs`/`Sting.cs`** retain the
    original Touch midpoint and legacy final-boss implementation for old
    debug/profile compatibility. Natural Touch progression now ends with
    **`TouchRot.cs`**, a portal-free seven-phase Rot encounter using slow
    fronts, bombs, and ground-level sludge pools with explicit safe wedges.
    Its large brown-green slab is drawn behind a foreground pool so it reads
    as half-submerged while brown/red/green cubes fall into absorption ripples.
  - **`SinChemesthesisBoss.cs`/`Kage.cs`/`Rot.cs`** -- the Chemesthesis
    content path's midpoint and Ache finale (`Ache` remains in the historical
    `Rot.cs` filename so the identity change does not overwrite user work).
    Unlike the other path families, this family has a real
    stagger/fracture system: `SinChemesthesisBoss` re-adds the
    `Stagger`/`MaxStagger`/`IsStaggered`/etc. fields `PathChaseBoss.cs`
    dropped as dead-in-that-family (see that class's doc comment) since
    this is the one family that actually uses them. `TakeDamage` applies a
    1.25x bonus multiplier once staggered, accumulates `Stagger` per hit,
    and disables the boss (`Update` early-returns after just advancing age)
    for `StaggerDuration` once `Stagger` reaches `MaxStagger`. Python's
    `SinChemesthesisBoss.updateEnemy` calls `Enemy.updateEnemy` directly,
    skipping `PathChaseBoss.updateEnemy`'s own movement-mode dispatch
    entirely -- C# has no "call the grandparent" syntax, so `PathChaseBoss`
    gained a `protected ChaseUpdate(context) => base.Update(context)`
    wrapper this family calls instead of `base.Update`.
    `_shot`/`_fan`/`_radial`/`_bomb`/`_laser`/`_parallel_lanes` become
    protected `Shot`/`Fan`/`Radial`/`Bomb`/`Laser`/`ParallelLanes` helpers
    shared by every phase-pattern override. `_draw_sigil`'s progressive
    per-stroke reveal (`stroke_budget`/`segment_budget`) is ported
    faithfully as a real gameplay-relevant visual; Python's
    supersample+`smoothscale` anti-aliasing is dropped (this port's
    established "no AA anywhere in `Primitives2D`" simplification) --
    `DrawSigil` draws directly at final screen coordinates instead of an
    offscreen `pygame.Surface`, which is mathematically identical once
    there's no surface-level compositing effect to preserve (confirmed for
    `_draw_field_diagram`'s seven procedural sin-motif diagrams too, same
    reasoning). The historical `Rot.cs` also retains reusable persistent-
    terrain primitives: mutable
    `CrystalWall`/`CleansingVent` classes (not records -- fields like
    `Remaining`/`Warning`/`Rect` mutate every frame), `MovementObstacles()`
    (consumed by `Player.Move`'s new `obstacles` parameter, wired through
    `GameSession.MovePlayer`), a `"crystal:N"` hitbox/damage part-ID scheme
    (`GetScreenHitboxes`/`DamageCrystal`), and direct integration with the
    already-existing `RunState.BossAfflictions` (exposure/pull -- cleansing
    vents call `BossAfflictions.Reset()`) and the newly-added
    `RunState.BuildSnapshot()`/`PlayerBuildSnapshot` (ported from
    `characterStats.py`'s `player_build_snapshot()`). Ache's active encounter
    replaces the old readable sin rotation with independently selected
    wrong-way mines, offset bombs, contamination pools, and telegraphed
    lashes; Reflex Storm and Overload provide its invulnerable survival exams.
    Its compact orange core now has exactly three blue 3D orbiting arm-cubes;
    this widened, asymmetrical constellation is the visual contract for its
    refusal to follow any command.
    Those optional context fields live on `EnemyUpdateContext` (`Camera`/`BossAfflictions`/
    `PlayerBuildSnapshot`, all nullable -- see that type's doc comment) and
    are populated by `GameSession.UpdateEnemies`.
  - **`PhantasiaBoss.cs`/`Hypno.cs`/`Malady.cs`** -- the Phantasia content
    path's midpoint and Empress of Inspiration finale,
    the last of `bossTypes.py`'s five boss families. Shares
    `SinChemesthesisBoss`'s overall shape (per-family `Config`/`SigilConfig`
    records, act transitions, phase-health floor, abstract
    `FirePhantasiaPattern`) but with its own commandment-sigil system:
    `PhantasiaBoss.CommandmentSigils` is one shared 10-entry stroke array
    (ported from bossTypes.py's module-level `COMMANDMENT_SIGILS`), and each
    subclass's `PhantasiaSigilConfig.PhaseSigils` is a list of *indices*
    into it -- unlike `SinSigilConfig`, where each boss owns its own full
    stroke data. Integrates with `RunState.DreamState`'s belief/truth
    mechanics via illusion-vs-truth-marked shots (`ShotFrom`'s `illusion`
    parameter) and direct `DreamState.AlterBelief` calls for rule
    violations and offering pickups; `EnemyUpdateContext` gained a nullable
    `DreamState` field for this and a nullable `PlayerBullets` list (for
    the REST phase's "did the player fire" check). `_draw_dream_court`
    reads `cS.dreamState["belief"]` directly inside a Python *draw* method
    -- rather than give `Draw` an implicit `RunState` dependency,
    `UpdateSpecialRules` caches belief into a `CurrentBelief` field each
    Update tick and every Draw-side belief read uses that instead. Two
    confirmed-dead fields dropped from `Malady` (`vitalitySuppressed`,
    `puppetFacing` -- written throughout but never read by any method).
    Hypno keeps the belief/rule/illusion lesson. Malady explicitly disables
    that inherited rule UI and instead layers portal-authored petal floods,
    delay-queued flowing tentacles, radial safe wedges, and telegraphed laser
    aisles. Intermission blocks damage for eighteen seconds at half health,
    Apotheosis runs for thirty seconds at zero health, and a ten-second
    expanding collapse follows. It also owns a fully custom procedural
    floating indigo-pillar render with phase-authored cube constellations
    (`HasCustomDreamBody`/`DrawDreamBody` hooks on the base class -- ported
    from Python's `getattr(self, "_draw_dream_body", None)` duck-typed dispatch).
- **`BossCatalog.cs`** <- `BossDefinition`/`BossCatalog`/`BOSS_CATALOG`.
  **Done** and wired into `GameSession` for every selected path. The active
  level-20 mapping is Dissonance (Sound), Rot (Touch), Chronos (Sight), Ache
  (Chemesthesis), and Malady (Phantasia). `sting` remains registered only as
  a compatibility/debug key. Every mapped finale owns a configurable
  invulnerable spectacle, a 10-second harmless death animation, normal XP and
    loot release, and `MetaProgression.RecordExtraction` path completion.
  - Core-Forged loot gives `LootCrate` a pulsing path-colored glow and orbiting
    particles. Equipped Core Forge types render as distinct concentric rings
    around `Player`; duplicate pieces of the same Core share one ring.
  - `FragmentPickup.cs` is the gold forge-currency shard: it scatters from an
    enemy death, then follows the same player aura behavior as experience.
