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
a future type when desired. Specialist tiers and mini-bosses now use level gates,
so `available(level)` is the source of truth for the current spawn pool. Remaining values are base multipliers; the
catalog applies run-level scaling and per-enemy variation consistently.

Definitions can also set:

- `threat_cost`: pressure/population budget consumed by the enemy.
- `family`: shared key used for simultaneous-family limits.
- `max_active`: maximum members of that family admitted by random spawning.

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
- `ShotgunEnemy` (level 3): uses the same readable range states but fires four to seven pellets
  with per-pellet spread, size, speed, damage, and travel variation.
- `SnakeEnemy` (level 5): a weaving composite enemy with independently destructible segments.
  Remaining segments close their spacing automatically. Its shielded head rejects
  damage until every segment has been destroyed, then becomes the final target.
- `ParentEnemy` (level 4, cost 3): fires three-shot slow heavy bursts. Crossing
  70%, 40%, and 15% health queues two cost-.5 `ChildEnemy` chasers. The arena
  admits queued children only when both population limits have room.
- `PillarEnemy` (level 4, cost 4, maximum 2): telegraphs a destination for .7
  seconds, lands at least four tiles from the player, pauses for one second, then
  fires six four-way volleys alternating cardinal and diagonal directions.
- `VolleyEnemy`: small/medium/large tiers unlock at levels 0/6/12 and cost 1.5/3/5.
  Higher tiers add pellets, spread, charge time, and recovery while lowering each
  pellet's fraction of base damage.
- `LaserEnemy`: small/medium/large tiers unlock at levels 2/7/13 and cost 1.5/3/5.
  Beams cannot damage during their telegraph. Medium beams sweep; large enemies
  cast two opposing sweeping rays. No more than two laser enemies spawn together.
- `BombEnemy`: small/medium/large tiers unlock at levels 2/8/14 and cost 1.5/3/5.
  Bombs are harmless while travelling and arming, display their true damage
  radius, and briefly expose that radius on detonation. Large enemies deploy three
  separated zones, and all bomb enemies retreat after attacking.
- `ArsenalMiniBoss`: two variants are placed into the ordinary world once per run
  at levels 5 and 15, cost 12 and 13, and share volley/laser/bomb phases in different
  orders. They are excluded from weighted random spawning. Neither creates an arena,
  moves the player, nor pauses normal spawning, so an unexplored mini-boss can be
  skipped. Their initial placement is beyond the normal disengagement radius.
  Crossing two-thirds and one-third health starts a .8-second invulnerable
  transition and removes only projectiles owned by that mini-boss.

## Arena population and awareness

The arena uses three complementary controls:

- 50 physical enemy bodies, rising to 60 from levels 11–20.
- 60 total population-threat points, rising to 78 in the second half.
- 36 active-pressure points, rising to 48 and assigned closest-first while retaining already
  alerted enemies where practical.

Every regular enemy uses a shared awareness radius equal to half the viewport
height: the player's screen-space distance to the top edge. Outside that radius it
wanders at roughly 12-20% combat speed (the immobile pillar occasionally relocates
instead). Enemies enter `alerted` inside the radius, use `disengaging` between 100%
and 125% of it, and return to `wandering` beyond 125%. The arena scheduler can hold
an enemy in wandering when waking it would exceed the active-pressure budget.

Enemy-created enemies should be appended to `self.spawnedEnemies`. The gameplay
loop owns admission and clears that queue after processing it; summoners therefore
do not need to import character state or duplicate cap logic.

Bosses use these same contracts plus the dedicated registry and HUD metadata
documented in `BOSS_TYPES.md`.
