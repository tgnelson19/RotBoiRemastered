# RotBoi Remastered: Project Review and Direction

## Current identity

The project already has a readable arena-survival loop: move, aim, kite enemies,
collect experience, and choose one of three upgrades. The strongest path forward is
to keep that immediacy while making the choices between fights and during combat
meaningfully deck-driven. The current upgrade screen is a draft system, but it is
not yet a full deckbuilding system because cards never enter a draw pile, hand, or
discard cycle.

## Findings from the full code review

- Runtime speed was derived from the configured frame cap rather than actual elapsed
  time. A slow frame therefore slowed the whole simulation.
- Projectile collision checked every bullet against every enemy. This scales poorly
  precisely when a successful multi-shot build becomes exciting.
- Spawn selection scanned the entire room on every spawn attempt.
- Lists were mutated while being iterated, which could skip bullets and enemies.
- Contacting an enemy destroyed it and awarded experience. Taking a hit could become
  an optimal farming action instead of a positioning failure.
- Upgrade choices could duplicate the same stat, showed base rather than
  rarity-adjusted values, and had no relationship to the player's current build.
- A large experience pickup could grant several levels but only one card reward.
- Core rules, rendering, state, and input are coupled through module globals. A total
  rewrite now would be high-risk, but new rule modules can gradually form a clean
  boundary around the existing game.
- The packaged build omitted the `data` directory, and the project had no dependency
  declaration or meaningful setup documentation.

## Implemented foundation

- Actual delta-time movement with a safety clamp and time-correct legacy timers.
- Edge-triggered dash/toggles, keyboard card selection, dash invulnerability, contact
  cooldown, readable hit flash, and knockback separation.
- Constant-sized spatial buckets for projectile/enemy collision and cached,
  constant-time random spawn probing.
- Safe batch removal for bullets and defeated enemies.
- A standalone, testable upgrade catalog with distinct offers, explicit rarity odds,
  exact card values, build-family tags, and gentle synergy weighting.
- Queued card rewards when one pickup grants multiple levels.
- Packaging, dependency, repository hygiene, controls, and run documentation.

## Recommended game design

### 1. Make cards active decisions

Keep aiming and movement real-time, but give the player a small hand of active cards.
A good first model is a 10-card deck, 4-card hand, automatic draw every few seconds,
and one regenerating energy resource. Cards can modify the next volley, deploy a
zone, dash-strike, mark a target, or convert collected experience. Played cards go
to discard; the discard reshuffles when the deck empties. This creates sequencing
and timing decisions without losing the game's movement feel.

Passive cards can remain as permanent run upgrades, but should be a smaller reward
class. The player should often choose between adding a new active card, upgrading a
card already in the deck, or removing a weak card. Deck size must have a cost so
every reward is not an automatic pickup.

### 2. Build encounters, not an endless spawn stream

Organize a run into short rooms or timed waves with a visible threat budget. Follow
combat with one reward choice, then branch to combat, elite, shop, recovery, or event
nodes. End each act with a boss that tests a different axis: burst damage, movement,
crowd control, or deck consistency. This gives the rogue-lite a strategic run arc
and natural pacing breaks.

Enemy roles should be data-defined and visually telegraphed: chaser, ranged zoner,
shield support, splitter, and elite variants. Composition is more strategically
interesting than increasing every enemy statistic with player level.

### 3. Improve combat readability

Add attack windups, enemy health feedback, impact particles, a small screen shake on
critical hits, and distinct silhouettes before adding more raw content. Use a
limited palette where projectile ownership, danger, experience, and rarity each have
stable colors. Replace the full-height stat panel with a compact combat HUD and an
expandable run/deck view so the arena keeps more screen space.

### 4. Continue the architecture migration incrementally

The next structural boundary should be a `GameSession` object that owns player,
enemies, projectiles, rewards, and run RNG. Systems should update that session;
renderers should only read it. Move tuning values into data files after those models
stabilize. This removes the circular imports and reset duplication without pausing
game development for a speculative rewrite.

Suggested order:

1. Active deck, hand, discard, energy, and 12-15 authored cards.
2. Encounter director with three enemy roles and one elite.
3. Three-room act map plus shop/removal and one boss.
4. `GameSession` migration, deterministic seeded runs, and saveable meta progression.
5. Effects/audio pass, controller support, resolution settings, and profiling at a
   target of 120 FPS with 200 enemies and 1,000 projectiles.

## Performance guardrails

- Keep collision queries spatial and measure worst-case successful builds, not only
  level-one play.
- Pool particles and damage text if those counts become high; bullets do not need
  pooling until profiling shows allocation pressure.
- Cache static room surfaces and navigation data per room.
- Use seeded RNG in the session so balance failures can be reproduced.
- Add a headless simulation test for long runs and a frame-time overlay showing
  update, collision, and draw costs separately.
