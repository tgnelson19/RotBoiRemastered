# Boss encounters

Bosses are registered through `BOSS_CATALOG`. A boss exposes the normal enemy
combat contract plus phase metadata for the shared boss HUD. Every path has a
level-10 lesson and a level-20 examination built from the same pacing grammar:

| Path | Level 10: HP / contact / phases | Its one survival | Level 20: HP / contact / phases | Survival phases |
| --- | --- | --- | --- | --- |
| Sound | Beaudis: 50,000 / 220 / 5 | Endure, 14s at 50% HP | Dissonance: 150,000 / 550 / 9 | 20s at 67%, 20s at 33%, Jera 30s at 0% |
| Touch | Bair: 110,000 / 380 / 5 | Ruin, 14s at 50% HP | Rot: 330,000 / 980 / 7 | Burial, 30s at 0% |
| Sight | Ishe: 80,000 / 300 / 4 | Flash, 12s at 50% HP | Chronos: 240,000 / 780 / 7 | Still Second 18s at 50%, Afterimage 30s at 0% |
| Chemesthesis | Kage: 93,000 / 340 / 4 | Stagnant Mirror, 14s at 50% HP | Ache: 280,000 / 880 / 8 | Reflex Storm 20s at 50%, Overload 30s at 0% |
| Phantasia | Hypno: 107,000 / 360 / 5 | Chosen, 14s at 50% HP | Malady: 320,000 / 900 / 10 | The House 22s at 50%, Enough 32s at 0% |

Each midpoint has almost exactly one third of its paired finale's raw health,
less than half its contact pressure, fewer movements, and exactly one invulnerable
survival lesson. That lesson always occurs at half health and returns to a final
damage movement. A level-10 fight therefore never borrows the paired level-20
zero-health survival surprise.

Dissonance remains the true final boss rather than merely another health tier. Its
45% normal-damage intake makes 150,000 HP behave like roughly 333,000 HP before
stagger bonuses, and its three acts contain 70 total survival seconds, breakable
portals, rune disruption, stagger/fracture play, and the longest pattern exam.

## Beaudis — level 10

Beaudis is a deliberately weaker echo of the final encounter. Reaching level 10
clears active threats and pauses regular spawning, but does not create a boss arena,
mask the world, constrain player movement, or reposition the player.

Its four damage movements use slow, sparse patterns:

1. One aimed shot.
2. A three-shot fan.
3. A six-shot radial pulse.
4. A four-shot aimed fan after the survival lesson.

Endure occurs once at half health and lasts 14 seconds. Four slow portals appear and
take turns firing two-projectile volleys while Beaudis is invulnerable. Completion
opens Persist, the final damage movement. Reducing that remaining half to zero begins
a three-second fade. It has no finale-collapse animation; the italic line
*“You can't escape me...”* remains visible until the fade completes and the XP drop
is released.

Base balance: 50,000 HP, 220 contact damage, 0.38 movement speed, 90 stagger, and 240 XP.
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

The final fight expects twenty drafted upgrades and is tuned to 150,000 HP, 550
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

## Kage and Ache — Path of Chemesthesis

Kage introduces Chemesthesis through four composite-sin patterns. Ache completes the
path in a larger jagged arena as three orange cubes orbiting a split scarlet/blue core.
The cubes depth-sort across the core, spread on attacks, jolt with movement, and open
into a much wider, faster constellation during its survival spectacles.

Kage has three damage movements around one midpoint survival:

1. **Feast** combines Gluttony and Greed in a spreading mine meal.
2. **Provocation** combines Wrath and Pride in an aimed volley and retaliatory beam.
3. **Stagnant Mirror** combines Sloth and Envy in a 14-second half-health survival.
4. **Lure** combines Lust and Avarice in wide tempting lanes and a targeted bomb.

Ache has six damage movements and two survival movements. **Pinprick**, **Crossed
Nerves**, and **Misstep** lead into the 20-second **Reflex Storm** at half health;
**Aftershock**, **Fracture**, and **White Ache** lead into the 30-second **Overload**
at zero health. Completing Overload begins a 10-second constellation collapse.

Its small orange projectiles always launch as opposite-amplitude sinusoidal pairs,
forming moving helixes. These deal only 105–130 base damage and are intended to be
occasionally tanked. Straight laser walls and traveling lightning bolts deal 690–820
base damage. Lightning telegraphs its complete multi-angle route before its active head
travels through the bends, so large hits are avoidable but costly mistakes. Ache has
280,000 health—less than Rot or Malady—but is invulnerable during both
survival movements.

Chemesthesis hazards can apply **Exposure**, a temporary movement-pressure meter that
decays after avoiding contaminated terrain. These effects never reduce movement below
58%, and dashing remains a reliable escape option.

## Hypno and Malady — Path of Phantasia

Hypno now teaches five paired commandment concepts through **Idol**, **Spoken Rule**,
**Inheritance**, **Chosen**, and **Offering**. Chosen is its one 14-second half-health
survival, and Offering returns control to a damage finish. Malady expands that language into ten
health-gated phases: Authority, Image, Reverence, Rest, Lineage, Mercy, Fidelity,
Ownership, Truth, and Contentment.

Malady uses three theatrical acts: **The Doctrine** (phases 1–3), **The Covenant**
(phases 4–6), and **The Testimony** (phases 7–10). Curtain transitions clear the
projectile field, protect the boss, and draw the next commandment sigil before the
court returns.

Phantasia's shared meter is **Belief**. Real hostile projectiles carry a stable cream
center mark; visually dense false projectiles are harmless and use a hollow muted
center. Following false rules, breaking Rest, or accepting finale offerings raises
Belief. Correct interpretation grants Clarity, which accelerates Belief decay. Higher
Belief adds mask echoes, false halos, and extra court ornamentation without changing
the real-threat marker.

Malady's commandment mechanics include shifting authoritative projectile origins,
mirrored false courts, protected names, a six-beat attack/Rest rhythm, inherited
splitting projectiles, a harmless mercy procession, route-like Fidelity lasers,
build-scaled stolen volleys, one true witness among false lasers, and four optional
Contentment offerings that permanently intensify the finale pattern.

All ten commandments have supersampled geometric sigils. The court renders several
counter-rotating manuscript rings, floating diamonds, an ivory ceremonial mask, ten
orbiting halo seals, Belief-driven false masks, illuminated rule banners, and ornate
phase cards. This is intentionally the game's most visually chaotic boss, but its
collision truth remains consistent.

## Shared path-arena overhaul

All catalog-spawned bosses now enter at the nearest open position to the exact room
center. Every non-Sound path boss owns a ticking arena boundary and exposes the same
containment contract to player movement:

- Touch uses a rigid square prison with heavy corner seams.
- Sight uses a large triangle that emphasizes fast intercept routes.
- Chemesthesis uses a continuously changing twenty-eight-point boundary with seeded
  irregularity and moving sharp edges.
- Phantasia uses a fluid sixty-four-point loop surrounded by three counter-rotating
  atomic ellipses.

The illuminated portion of each boundary counts down against the current phase timer.
Each path also rotates between chase, static, and scripted-path locomotion. Touch moves
slowly along the square, Sight changes direction quickly, Chemesthesis uses irregular
offset paths, and Phantasia traces smooth ellipses and figure-eights.

Containment resolves all edges violated by one movement attempt before returning the
player position. Pressing diagonally into a corner therefore produces one stable inset
position instead of alternating between adjacent edges and vibrating the camera.

## Bair and Rot — Path of Touch

Bair previews Touch through five paired phases: **River**, **Swarm**, **Blight**,
**Ruin**, and **Silence**. Its square iron gates march around the arena boundary,
require eight separate hits to disable, and fire paired slow heavy shots.
Ruin is the single 14-second half-health survival; Silence is the damageable finish.

Rot is Touch's complete level-20 encounter. Its seven stages of accumulation are
**Seep**, **Silt**, **Slump**, **Bloom**, **Sinkhole**, **Miasma**, and **Burial**.
The fight uses only static and extremely slow pathed movement inside Touch's rigid
square arena. Slow edge-reaching fronts, orbiting clots, long-lived sludge pools,
spores, sweeps, and advancing mud banks progressively remove open ground.

Rot has 330,000 visible health, one body hitbox, no breakable appendages, no stagger
damage multiplier, and no alternate weak point. Its body is a shadowed semicircle of
vibrating cubes sinking through a mud shelf while detached cubes fall, shrink, and
dissolve. There is deliberately no bright core that reads as a damage target.

Mud banks are untargetable environmental geometry. They telegraph in cream for 1.8
seconds, become solid, and in Sinkhole and Burial advance toward the arena center
before stopping with a traversable channel. Active banks are capped at eight.

The first six Rot stages are damage movements. Reaching zero health unlocks **Burial**
as a 30-second invulnerable survival spectacle; completing it clears the mud and begins
the ten-second collapse. Bair's Ruin teaches the square-lane pressure without reusing
this finale.

## Ishe and Chronos — Path of Sight

Ishe introduces Sight's **Glimpse**, **Blink**, **Flash**, and **Afterglow** through
four short, fast movements. Flash is its one 12-second half-health survival; Afterglow
returns to a final aimed volley. Chronos completes the path as the smallest, fastest,
and lowest-health final boss: a light-blue portal cube whose body gradient, idle orbit, movement
trail, attack flare, and staggered afterimages are all made from square particles.

Chronos has five mobile damage movements and two survival movements:

1. **Tick** fires a narrow five-shot fan.
2. **Crosscut** rotates a dense radial volley with a consistent two-shot opening.
3. **Blink** overlaps two narrow fans while Chronos jumps between positions.
4. **Still Second** is the fight's only static movement, an 18-second midpoint
   survival built from rotating rings with visible openings.
5. **Quicken** uses seven small alternating sine shots.
6. **Parallax** crosses two predictions while leaving the exact aimed line open.
7. **Afterimage** is a 30-second high-speed chase. Every new volley marks its old
   firing origin and repeats the exact same angles 1.7 seconds later; the opening also
   replays up to eight volleys previously seen during the fight.

No Chronos attack creates a pool, wall, mine, laser, or other persistent field hazard.
Its pressure comes entirely from small 96–145 damage projectiles, short firing
intervals, pursuit, and readable gaps. At 240,000 health, it is deliberately more
fragile than the other path finales. Completing Afterimage starts a ten-second portal
collapse in which the cube shrinks while seventy-two bright square fragments spread
outward before the reward is released.

### Recommended polish order

1. Playtest arena dimensions and moving-boundary containment before tuning damage.
2. Playtest Chronos's radial openings at low movement speed and without dash upgrades;
   widen the gaps before reducing projectile density if baseline builds struggle.
3. Add themed portal subclasses for Chemesthesis and Phantasia. Chemical vents should
   move unpredictably along the jagged edge; dream apertures should follow atomic
   ellipses and mix real and illusory volleys.
4. Add shared act-transition cleanup and phase-timer rules to the common arena contract,
   then remove remaining duplicated transition code.
5. Profile projectile counts and alpha surfaces during Malady. Preserve its visual
   maximalism, but cap expensive translucent layers independently from gameplay shots.
6. Add portal-interception tests per path before allowing every themed portal to reroute
   player bullets.
7. Tune movement modes only after every boss can be practiced phase-by-phase; scripted
   paths should remain deterministic enough to learn even when their visuals are wild.
