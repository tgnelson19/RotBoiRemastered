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

## Explicitly deferred (not in Entities/ yet)

- **`enemyTypes.py`'s enemy catalog and subclasses** (~1600 lines: tier
  balance tables, family/modifier identities, `RuntimeEncounter`
  coordination, and every named enemy archetype). `Enemy.cs` is ready to be
  subclassed (`Update`/`Draw`/`TakeDamage`/hitbox methods are all
  `virtual`), but the catalog itself is a large, mostly-independent unit of
  work better done as its own pass.
- **`Player.cs`** <- `character.py` + `characterStats.py`. This is the
  ~1550-line combined "player entity + entire run's mutable game state +
  main gameplay loop" god object (see `Systems/README.md`'s note on
  `characterStats.py`) -- porting it means also deciding how much of
  `character.py`'s loop logic belongs on Player vs. `Core/RotBoiGame.cs`'s
  state machine. Left for a dedicated pass once that architectural question
  is worth answering; not something to decide as a side effect of porting
  loot crates.
- **Boss types** <- `bossTypes.py` (~4750 lines). Its own future module --
  every boss references `ProjectilePortal`/`EnemyProjectile`/`Enemy`, all of
  which are now ported and ready for it to build on.
