# Boss encounters

Bosses are registered through `BOSS_CATALOG`. A boss exposes the normal enemy
combat contract plus phase metadata for the shared boss HUD. Natural progression
uses two distinct encounters:

| Level | Boss | Role | Arena | Phases | Reward XP |
| --- | --- | --- | --- | ---: | ---: |
| 10 | Beaudis | Midpoint warning | Ordinary world | 5 | 240 |
| 20 | Dissonance | Final boss | Sealed boss arena | 9 | 900 |

## Beaudis — level 10

Beaudis is a deliberately weaker echo of the final encounter. Reaching level 10
clears active threats and pauses regular spawning, but does not create a boss arena,
mask the world, constrain player movement, or reposition the player.

Its first four phases are damage phases with slow, sparse patterns:

1. One aimed shot.
2. A three-shot fan.
3. A six-shot radial pulse.
4. A four-shot aimed fan.

The fifth survival pattern occurs at two-thirds, one-third, and zero health and lasts
14 seconds each time. Four slow portals appear for these intervals and take turns
firing two-projectile volleys. Beaudis is invulnerable during survival. The first two
completions return to a rotating damage-phase pool; after the final timer, Beaudis fades for
three seconds. It has no finale-collapse animation; the italic line
*“You can't escape me...”* remains visible until the fade completes and the XP drop
is released.

Base balance: 260 HP, 2 contact damage, 0.38 movement speed, 90 stagger, and 240 XP.
Its rendering is intentionally rigid and blocky: no idle bob, no arena spectacle,
and only coarse phase pips distinguish its state.

## Dissonance — level 20

Dissonance is the true name and final form of the original nine-phase encounter.
It keeps the dedicated circular arena, portal interception/routing, rune disruption,
stagger and fracture systems, three survival phases, act transitions, Jera finale,
and ten-second death spectacle.

Dissonance alone retains the fluid visual language. In addition to the rotating
perspective cube, rune interpolation, orbiting shards, arena waves, particles, and
cinematic transitions, it now leaves smoothly fading phase-colored motion echoes.
This effect is not shared with mini-bosses or Beaudis.

The final fight expects twenty drafted upgrades and is tuned to 1,350 HP, 5.2
contact damage, 0.72 movement speed, 360 stagger, a 1.3x final-boss projectile
damage modifier, and 900 reward XP. Sixty baseline
stagger hits are required to force a break, while coherent damage, critical, volley,
tempo, movement, and defense selections remain useful throughout its pattern pool.

During damage phases, Dissonance's portals require 15 separate player hits to disable
for the remainder of that phase. Disabled portals stop intercepting player shots and
fire reduced volleys. Survival-phase portals are untargetable and cannot be disabled,
so their formations remain a constant threat for the entire survival timer.

### Acts and survival phases

- Act I uses phases 1–3 and unlocks its 20-second survival at two-thirds health.
- Act II uses phases 4–6 and unlocks its 20-second survival at one-third health.
- Act III uses phases 7–9 and unlocks the 30-second Jera survival at zero health.

Only completing Jera begins Dissonance's existing finale collapse. This sequence is
not reused by Beaudis.

### Practice controls

`B` summons Dissonance in its arena. Number keys 1–9 jump to phases, `R` resets the
current phase, `L` locks automatic phase changes, `F` primes a stagger, `C` primes the
rune cannon, and `Y` toggles invincibility.

## Progression contract

Natural encounters are ordered and one-shot per run: Beaudis at level 10 must be
defeated before Dissonance can start at level 20. Defeating Dissonance marks the run
complete and leaves ordinary spawning disabled. Player progression is capped at 20.
Defense can never nullify hostile damage completely: enemy and boss hits retain the
greater of 0.25 damage or 10% of their pre-defense value (before casual-mode scaling).
