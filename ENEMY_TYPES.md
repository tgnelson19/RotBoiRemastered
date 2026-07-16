# Enemy Type Framework

Enemy creation is centralized in `enemyTypes.py`. The spawn system asks
`ENEMY_CATALOG` for an enemy appropriate to the current level; gameplay code does
not need a separate branch for each type.

## Adding a stat-based follower

Register one `EnemyDefinition` in `_register_defaults()`:

```python
EnemyDefinition(
    "heavy_runner", Enemy,
    weight=8,
    min_level=3,
    speed=.9,
    size=1.1,
    damage=1.4,
    health=1.8,
    experience=1.7,
    color=pygame.Color(180, 70, 55),
)
```

Weights are relative to every other type currently available. `min_level` can gate
a future type when desired; the current catalog uses level zero so every enemy can
appear from the beginning. Remaining values are base multipliers; the
catalog applies run-level scaling and per-enemy variation consistently.

## Adding a behavioral enemy

Subclass `Enemy` and implement only the contracts the type needs:

- `updateEnemy(player_world_x, player_world_y, projectile_sink)`
- `drawEnemy(screen)`
- `get_screen_hitboxes()` and `get_world_hitboxes()` for composite bodies
- `take_damage(amount, part_id)` for shields, weak points, armor, or phases
- `apply_knockback(delta_x, delta_y)` when special knockback behavior is needed

Register the subclass with an `EnemyDefinition`. Enemy-fired attacks should append
`EnemyProjectile` objects to `projectile_sink`; projectile movement, wall collision,
rendering, and player damage are handled centrally.

## Implemented special types

- `WanderingRangedEnemy`: wanders outside awareness range, approaches cautiously,
  and fires aimed diamond projectiles once in attack range.
- `ShotgunEnemy`: uses the same readable range states but fires four to seven pellets
  with per-pellet spread, size, speed, damage, and travel variation.
- `SnakeEnemy`: a weaving composite enemy with independently destructible segments.
  Remaining segments close their spacing automatically. Its shielded head rejects
  damage until every segment has been destroyed, then becomes the final target.

Bosses use these same contracts plus the dedicated registry and HUD metadata
documented in `BOSS_TYPES.md`.
