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

The fifth and only survival phase lasts 14 seconds. Four slow portals appear for
this phase only and take turns firing two-projectile volleys. Beaudis is invulnerable
during survival. When the timer expires, the portals disappear and Beaudis fades for
three seconds. It has no finale-collapse animation; the italic line
*“You can't escape me...”* remains visible until the fade completes and the XP drop
is released.

Base balance: 260 HP, 2 contact damage, 0.38 movement speed, 90 stagger, and 240 XP.

## Dissonance — level 20

Dissonance is the true name and final form of the original nine-phase encounter.
It keeps the dedicated circular arena, portal interception/routing, rune disruption,
stagger and fracture systems, three survival phases, act transitions, Jera finale,
and ten-second death spectacle.

The final fight expects twenty drafted upgrades and is tuned to 1,350 HP, 5.2
contact damage, 0.72 movement speed, 360 stagger, a 1.3x final-boss projectile
damage modifier, and 900 reward XP. Sixty baseline
stagger hits are required to force a break, while coherent damage, critical, volley,
tempo, movement, and defense selections remain useful throughout its pattern pool.

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
