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

Weights are relative to every other type currently available. `min_level` and
`max_level` form an inclusive spawn window, so `available(level)` is the source of
truth for both unlocks and retirement. Remaining values are base multipliers; the
catalog applies run-level scaling, tier scaling, and per-enemy variation consistently.

Definitions can also set:

- `threat_cost`: pressure/population budget consumed by the enemy.
- `family`: shared key used for simultaneous-family limits.
- `max_active`: maximum members of that family admitted by random spawning.
- `max_level`: final level where the definition may be chosen.
- `progression_tier`: `easy`, `medium`, or `hard`; controls stat, reward, threat,
  animation, and attack-complexity scaling.

## Partitioned progression

Every regular family has three variants. Legacy keys such as `runner`, `parent`, and
`shotgunner` remain the easy variant; their medium and hard forms use `_medium` and
`_hard`. Volley, laser, and bomb retain their small/medium/large keys while mapping
those sizes to easy/medium/hard progression tiers.

| Family | Easy window | Medium window | Hard window |
| --- | ---: | ---: | ---: |
| Runner | 0-6 | 4-13 | 10-20 |
| Drifter | 0-7 | 4-14 | 10-20 |
| Skirmisher | 0-8 | 4-14 | 10-20 |
| Bulwark | 1-8 | 5-14 | 11-20 |
| Ranged wanderer | 0-8 | 4-14 | 10-20 |
| Shotgunner | 3-9 | 6-14 | 11-20 |
| Snake | 5-9 | 8-15 | 12-20 |
| Parent | 4-9 | 7-15 | 12-20 |
| Pillar | 4-9 | 7-15 | 12-20 |
| Volley | 0-8 | 6-14 | 12-20 |
| Laser | 2-8 | 7-14 | 13-20 |
| Bomb | 2-8 | 8-14 | 14-20 |
| Banner Captain | 2-8 | 6-14 | 11-20 |
| Rammer | 3-8 | 6-14 | 11-20 |
| Warder | 4-9 | 7-14 | 11-20 |
| Splitter | 3-8 | 6-14 | 10-20 |
| Collector | 2-8 | 6-14 | 11-20 |

Easy enemies are fully absent by level 10. Medium enemies bridge the midpoint and
are fully absent by level 16. Level 16 onward is therefore an exclusively hard
random pool leading into Dissonance.

## Path-exclusive variants

Ambient patrols also draw from two families exclusive to the active path. Each has
easy, medium, and hard definitions using the same overlapping run windows as the
shared roster, so its signature mechanic is introduced early and escalates rather
than disappearing.

| Path | Family | Path mechanic |
| --- | --- | --- |
| Sound | Echoer | Paired sine-wave notes widen and multiply by tier. |
| Sound | Resonator | Expanding radial note patterns gain additional spokes. |
| Touch | Clasper | Slow, telegraphed banks of compacted weight occupy broad lanes. |
| Touch | Mirekeeper | Persistent mire pools surround the player's last position. |
| Sight | Blinker | Small, rapid needle fans demand immediate close-range reads. |
| Sight | Lens | Brief sight-lines become paired sweeping beams at hard tier. |
| Chemesthesis | Cinderpod | Long-lived cinders seed a telegraphed minefield. |
| Chemesthesis | Sporecaster | Sine-drifting spores split, then reproduce again at hard tier. |
| Phantasia | Mirage | Ornate fans conceal one or two truth-marked shots among harmless illusions. |
| Phantasia | Dreamweaver | Orbiting dream courts form around the player's last position. |

Path-exclusive families participate in the ordinary role-composed patrol pool and
obey the same threat budgets, family caps, modifiers, active-pressure rules, and
path-wide stat/projectile tuning as shared enemies. Curated packages remain shared,
authored compositions.

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
- `ParentEnemy` (level 4, cost 3): fires slow heavy bursts. Higher tiers widen the
  burst and produce more children. Crossing 70%, 40%, and 15% health queues
  `ChildEnemy` chasers. The arena
  admits queued children only when both population limits have room.
- `PillarEnemy` (level 4, cost 4, maximum 2): telegraphs a destination for .7
  seconds, lands at least four tiles from the player, pauses for one second, then
  fires alternating radial volleys. Higher tiers add spokes and volleys.
- `VolleyEnemy`: small/medium/large tiers use levels 0-8/6-14/12-20.
  Higher tiers add pellets, spread, charge time, and recovery while lowering each
  pellet's fraction of base damage.
- `LaserEnemy`: small/medium/large tiers use levels 2-8/7-14/13-20.
  Beams cannot damage during their telegraph. Medium beams sweep; large enemies
  cast two opposing sweeping rays. No more than two laser enemies spawn together.
- `BombEnemy`: small/medium/large tiers use levels 2-8/8-14/14-20.
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
- `BannerCaptain`: creates an atomic squad of five, seven, or nine minions. Minions
  hold formation until a synchronized charge command; killing the Captain leaves
  surviving minions alive and increasingly aggressive. If the full formation cannot
  fit within body and threat budgets, neither leader nor minions are admitted.
- `RammerEnemy`: telegraphs a fixed charge lane, cannot steer while charging,
  damages and knocks back ordinary enemies in its path, and becomes vulnerable
  during a wall-impact recovery. Higher tiers charge faster and for longer.
- `WarderEnemy`: places a separately destructible shield on its player-facing side.
  Shield coverage grows by tier and is prioritized before bodies during bullet
  collision, allowing nearby allies behind it to benefit from the protection.
- `SplitterEnemy`: fires slow diamond shots that divide after a fixed, readable
  distance. Easy/medium shots split into two/three; hard shots split into four and
  their children divide once more at reduced damage and size.
- `CollectorEnemy`: seeks loose XP, stores it visibly by growing, then flees after
  collecting enough. Killing it returns all stolen XP with a 15% bonus, so it never
  permanently removes run progression.

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

## Combat-role roster

The catalog now exposes a primary role plus interaction tags on every spawned enemy:

- `pressure`: runners, drifters, skirmishers, and rammers force movement.
- `tank`: bulwarks and snakes consume attention and protect space.
- `artillery`: ranged wanderers, shotgunners, volleys, and splitters create lanes.
- `control`: pillars, lasers, and bombs restrict future positions.
- `support`: Warders protect other roles rather than supplying raw pressure.
- `squad`: Parents and Banner Captains turn one source into coordinated bodies.
- `economy`: Collectors alter XP-routing decisions.
- `elite`: mini-bosses remain milestone encounters outside random packages.

Roles are data used by modifiers and curated encounters; interaction tags such as
`shield`, `charge`, `splitting`, `summoner`, and `xp` document the actual mechanic.

## Behavioral modifiers

At most one modifier may appear on a randomly spawned enemy. A colored corner pip
communicates it before combat. Modifier chance begins at level 5 and rises gradually
to a 28% cap.

- `Hasty` (level 5, pressure/artillery): faster movement and attacks, 18% less HP,
  and 20% more XP.
- `Armored` (level 6, tank/support/squad): 75% more HP, slower movement, and 45%
  more XP.
- `Volatile` (level 8, pressure/control): drops a short-range telegraphed radial
  burst on death and grants 30% more XP.
- `Regenerating` (level 10, tank/artillery/support): heals slowly only while unaware
  of the player and grants 35% more XP.
- `Champion` (level 12, most combat roles): larger body, 55% more HP, 25% more
  damage, 50% more threat, and double XP.

Collectors deliberately receive none of these combat modifiers, preserving their
clear economy role. Bosses and guaranteed mini-bosses are also never modified.

## Curated encounter packages

From level 5 onward, a normal spawn event has a 24% chance to attempt a named
composition. Packages use the tier currently live for every requested family, obey
combined body/threat limits, allow one concurrent copy, and trigger an 18-second
package cooldown.

| Package | Levels | Composition |
| --- | ---: | --- |
| Shield Wall | 5-14 | Warder + two Shotgunners |
| Royal Procession | 6-14 | Banner Captain + Collector |
| Demolition Crew | 7-18 | Rammer + two Bomb enemies |
| Crossfire | 7-17 | Pillar + two Volley enemies |
| Brood Guard | 8-18 | Parent + Warder |
| Fractured Choir | 10-20 | two Splitters + Laser |
| Stampede | 11-20 | Banner Captain + Rammer |
| Salvage Team | 6-16 | Collector + Bulwark + Ranged Wanderer |

## Runtime encounter cohesion

Enemies no longer enter the world as unrelated random bodies. Every ordinary spawn
is either a named package or a dynamically composed patrol that selects complementary
roles. The runtime encounter remains attached after spawning and owns:

- atomic pressure admission, so part of a dangerous group cannot activate alone;
- a shared patrol anchor and formation slots rather than independent wandering;
- a shared alert pulse and activation state when the player enters encounter range;
- group-wide disengagement with a generous leash instead of individual state flicker;
- alternating flank sides for pressure units and staggered initial attack timers;
- front-line formation targets for tanks and Warders;
- range bands, retreating, and strafing for artillery and support units;
- encounter completion when its final member dies.

Ambient patrols are role-composed rather than randomly sampled in isolation. A
pressure lead requests tanks, artillery, or support; artillery requests pressure or
tanks; control requests a front line; and economy units request protection. Patrol
size begins at five enemies for levels 0-4, then rises to six at level 5, seven at
level 9, eight at level 13, and nine at level 17. Banner Captains are reserved for
named encounters so their atomic minions never silently inflate an ambient patrol.

## Bounty navigation

The HUD continuously evaluates living patrols and ungrouped elite targets. Patrol
value combines all remaining XP (including XP held by Collectors) with total threat;
guaranteed elites receive priority and an active boss always wins. A short, fat red
arrow is clamped to the edge of the 75%-width gameplay viewport and points through
the camera's current rotation toward the selected bounty. Its top margin expands
during bosses so it never collides with the boss health panel.

World clutter is also bounded by encounter count: three simultaneous patrols early,
four from level 8, and five from level 16. New encounters arrive roughly every seven
seconds early and 4.4 seconds late,
rather than using the old rapid singleton spawn cadence. Named-package probability
rises from 28% at level 5 to 46% near level 20, while the existing 18-second package
cooldown prevents authored fights from stacking continuously.

Bosses use these same contracts plus the dedicated registry and HUD metadata
documented in `BOSS_TYPES.md`.
