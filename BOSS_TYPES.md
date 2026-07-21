# Boss encounters

Bosses are registered through `BOSS_CATALOG`. A boss exposes the normal enemy
combat contract plus phase metadata for the shared boss HUD. Every path has a
level-10 lesson and a level-20 examination built from the same pacing grammar:

| Path | Level 10: HP / contact / phases | Its one survival | Level 20: HP / contact / phases | Survival phases |
| --- | --- | --- | --- | --- |
| Sound | Beaudis: 50,000 / 220 / 5 | Endure, 14s at 50% HP | Dissonance: 150,000 / 550 / 9 | 20s at 67%, 20s at 33%, Jera 40s at 0% |
| Touch | Bair: 110,000 / 380 / 5 | Ruin, 14s at 50% HP | Rot: 330,000 / 980 / 7 | Choking Stillness 22s at 50%, Burial 40s at 0% |
| Sight | Ishe: 80,000 / 300 / 4 | Flash, 12s at 50% HP | Chronos: 240,000 / 780 / 7 | Still Second 20s at 50%, King's Attrition 40s at 0% |
| Chemesthesis | Kage: 93,000 / 340 / 4 | Stagnant Mirror, 14s at 50% HP | Ache: 280,000 / 880 / 8 | Reflex Storm 20s at 50%, Overload 28s at 0% |
| Phantasia | Hypno: 107,000 / 360 / 5 | Chosen, 14s at 50% HP | Malady: 320,000 / 900 / 10 | Intermission 18s at 50%, Apotheosis 30s at 0% |

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

Dissonance is the oldest ancient core and the true name of the original nine-phase encounter.
Sound once let humanity coordinate and build; repetition without intent reduced that gift
to a wearing drone. The encounter therefore presents Dissonance as a respectful warden
testing whether an emissary can still distinguish meaning from imitation.
It keeps the dedicated circular arena, portal interception/routing, rune disruption,
stagger and fracture systems, three survival phases, act transitions, Jera finale,
and ten-second death spectacle.

Dissonance is a composed purple-and-blue perspective cube around a deep black center.
Four much smaller cubes orbit slowly on a shallow three-dimensional ellipse. Rune
interpolation, arena waves, restrained particles, and smoothly fading echoes communicate
age and mastery, while the body palette remains stable through every phase. Phase color
belongs to the current rune and its warning trim; it never repaints the ancient itself.
This keeps Dissonance noble and readable rather than making it look volatile.

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

- Act I, **The First Chord**, uses Ancestral Hearth, Processional Road, and the
  20-second Kenaz Refrain at two-thirds health.
- Act II, **The Empty Drone**, uses Hagalaz Cacophony, Yew Overtone, and the
  20-second Sowilo Resonance at one-third health.
- Act III, **The Defense**, uses Tiwaz Defense, Meaningless Drone, and the
  40-second Jera Last Chord at zero health.

The revised names remove the former deception framing. Dissonance is defending
purposeful ancient resonance from modern repetition that has lost its meaning.

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
path in a larger jagged arena as one deliberately small orange-and-blue oscillating core.
Exactly three blue perspective cubes orbit it as unruly arms. Their connectors, depth
sorting, unequal orbit speed, and widened survival constellation make the silhouette
feel powerful but incapable of following a common command.

Ache's collision-born origin is reflected in overlapping visual systems rather than an
ancestral court: orange and blue never fully blend, its old sin-sigil labels have become
Phantom, Border, Recoil, Reflex, Trespass, Splinter, Static, and Unbound, and each attack
appears to answer a threat or territorial violation that may not exist.

Kage has three damage movements around one midpoint survival:

1. **Feast** combines Gluttony and Greed in a spreading mine meal.
2. **Provocation** combines Wrath and Pride in an aimed volley and retaliatory beam.
3. **Stagnant Mirror** combines Sloth and Envy in a 14-second half-health survival.
4. **Lure** combines Lust and Avarice in wide tempting lanes and a targeted bomb.

Ache has six damage movements and two survival movements. **Misfire**, **Crossed
Nerves**, and **Wrong Way** lead into the 20-second **Reflex Storm** at half health;
**Aftershock**, **Fracture**, and **White Ache** lead into the 28-second **Overload**
at zero health. Completing Overload begins a 10-second expanding constellation collapse.

Ache selects each attack independently from a phase-authored family instead of exposing
a learnable rotation. Wrong-way debris drifts slowly and settles into mines, lazy clusters
appear in unrelated arena regions, and corner pockets close three random sides of the
player while leaving one telegraphed escape. Bombs land around rather than directly on
the player, contamination pools announce themselves for 1.25 seconds, and fully drawn
lashes provide the heaviest warning. Primary hazards are authored at 190–230 damage
before Chemesthesis path modifiers, producing roughly five-to-eight-hit lethality for a
standard level-20 defensive baseline. Ache uses no projectile portals.

The active field is deliberately sparse. Ache hesitates at twenty-eight active threats
and switches away from mine-building at sixteen persistent hazards; delayed bomb bursts
can briefly raise the simulated envelope to forty total projectiles and twenty-six
persistent hazards. Random placement, long mine memory, and independent pattern choice
create the pressure; raw bullet count does not.

Chemesthesis hazards can apply **Exposure**, a temporary movement-pressure meter that
decays after avoiding contaminated terrain. These effects never reduce movement below
58%, and dashing remains a reliable escape option.

## Hypno and Malady — Path of Phantasia

Hypno remains Phantasia's midpoint lesson. Malady expands the path into ten flowing
movements: **Overture**, **Petal Flood**, **Impossible Engine**, **Ribbon Court**,
**Tentacle Garden**, **Intermission**, **Luminous Tide**, **Violet Cathedral**,
**Soul Incursion**, and **Apotheosis**.

As the youngest and most powerful ancient, Malady should look generative rather than
merely royal. Impossible Engine turns inspiration into novel machinery; Soul Incursion
makes her attempt to recover lost power through the player's imagination explicit.

Malady uses three theatrical acts: The First Idea, Invention, and The Human Soul.
Curtain transitions clear the projectile field, protect the boss, and redraw the
court before the next movement begins. Intermission is an 18-second half-health
survival; Apotheosis is the 30-second zero-health finale.

Hypno retains Phantasia's shared **Belief** lesson, false rules, Rest, and offerings.
Malady deliberately disables those inherited mechanics. Her identity is divine novelty,
not deception: every dangerous formation is real, every large hit is honestly marked,
and the challenge is navigating beauty without letting density hide the intended shore.

Malady's mechanics are portal-authored petal floods, rigid twin portal gears, slow
curved ribbon chains, splitting projectile tentacles, player-facing openings in radial
flowers, and fully telegraphed cathedral lasers with adjacent aisles left empty. Ground
pools stay on the inner 64% of the arena so the outer court always offers a less
threatened shore, but faster aimed portal phrases and arena-spanning volleys ensure that
the shoreline is never a stationary safe zone. Pattern counts are budgeted below the
150-projectile emergency ceiling so bullets reach the boundary instead of being trimmed
mid-flight.
Apotheosis cycles floods, tentacles, portal phrases, and laser arches for thirty seconds.

Malady is a tall indigo cubic pillar moving freely above the court, with no solid
foundation to imply that she is rooted or that scenery belongs to her hitbox. A vertical
magenta-white inspiration slit and three luminous nodes keep the center legible, while
ten to eighteen independent perspective cubes compose a phase-specific body grammar:
blossom, spiral, engine lattice, ribbon, branches, empty ring, tide, cathedral columns,
and inward soul spiral. During Apotheosis those grammars recombine with each attack.
Breathing elliptical auras and incomplete arcs suggest ideas radiating from the source.
The pillar barely leans when moving; attacks rearrange the constellation instead,
preserving the Empress's composure and scale.

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
**Seep**, **Silt**, **Slump**, **Choking Stillness**, **Bloom**, **Miasma**, and **Burial**.
The fight uses only static and extremely slow pathed movement inside Touch's rigid
square arena. Slow edge-reaching fronts, orbiting clots, long-lived sludge pools,
spores, sweeps, and advancing mud banks progressively remove open ground.

Rot's second-oldest status is expressed through inertia instead of ornament. The other
ancients made it a guardian because moving it would risk a battle, so none of its major
motions should look hurried: the puddle, room, and projectiles move while the burden stays.

Rot has 330,000 visible health, one body hitbox, no breakable appendages, no stagger
damage multiplier, and no alternate weak point. Its body is one enormous brown-green
cubic slab drawn behind a foreground pool so the lower half is visibly submerged.
Brown, red, and green cubes appear above it, fall, shrink, and vanish at the waterline;
absorption ripples and colored cracks imply that discarded matter feeds the ancient.
There is deliberately no bright core that reads as a weak point.

Poison-floor pools render beneath bosses, projectiles, and the player. They telegraph
for at least 1.65 seconds and occupy only inner rings while every formation omits a
broad wedge aimed toward the player's current outer corridor. Slow sludge fronts use
the same three-lane opening, and three-second bombs land on the threatened side of the
arena rather than inside that corridor.

**Choking Stillness** is a 22-second invulnerable half-health survival with Rot static
near the center. Reaching zero health unlocks **Burial** as a 40-second invulnerable
spectacle; completing it clears hostile projectiles and begins the ten-second expanding
collapse before the Touch path reward is released.

## Ishe and Chronos — Path of Sight

Ishe introduces Sight's **Glimpse**, **Blink**, **Flash**, and **Afterglow** through
four short, fast movements. Flash is its one 12-second half-health survival; Afterglow
returns to a final aimed volley. Chronos inverts that lesson: the King of Attrition is
slow, directive, and heavy. The body is one simple light-blue perspective cube, visually
related to the player's square body but enlarged and rotating on three axes. Breathing
elliptical auras and sparse square motes oscillate around it; there is no clock face or
literal lens to compete with attack telegraphs.

Chronos defeated Queen Valia by degrading reaction rather than concealing intent. The
phase sequence repeatedly shows complete routes, makes the player solve them while tired,
then introduces the same single Thorn of Time that pierced the former ruler's core.

Chronos has five damage movements and two survival movements:

1. **Directive** shows two segmented laser-tentacles around a central opening.
2. **Crosscut** bends two heavier routes across one another after a longer warning.
3. **Wearying Lash** declares three seven-segment flails before any segment strikes.
4. **Still Second** is a 20-second midpoint survival: ten possible arms are shown,
   but the three closest to the player's escape angle are always absent.
5. **Parallax** combines the learned opening with one large independent flail.
6. **Thorn of Time** alternates a twelve-arm crown with the fabled single eight-segment
   killing line. The Thorn is fixed when declared and remains harmless for 2.35 seconds.
7. **King's Attrition** is the 40-second finale, cycling directive pairs, crowns, rear
   lashes, and the Thorn until the player's reactions are tested one final time.

Every tentacle is a chain of five to eight finite laser segments. The full chain remains
harmless for 1.5–2.35 seconds, then every segment activates together for less than a
second. Chronos does not create pools, walls, mines, or surprise contact jumps. Its
pressure comes from 730–1,260 damage declared routes and choosing the empty route in time.
Completing King's Attrition begins a ten-second expanding square-fragment collapse before the
Sight path reward is released.

### Visual-direction comments and playtest suggestions

1. Keep each core's silhouette readable in grayscale: Dissonance's black center,
   Malady's tall pillar, Ache's three arms, Rot's submerged slab, and Chronos's lone cube
   should identify the boss before phase color is considered. The procedural renders now
   enforce those silhouettes directly.
2. Reserve irregular visual motion for Ache and fast outer motion for Malady. Dissonance's
   satellites, Rot's slab, and Chronos's body should remain composed so their authority
   comes from the arena and attacks rather than frantic animation.
3. Playtest Chronos's 2.35-second Thorn warning without dash upgrades. If it still feels
   unfair, widen the line's lateral escape rather than lowering its signature damage.
4. Profile Malady's eighteen-cube finale constellation on low-end hardware. Cosmetic
   cube count can be reduced independently of gameplay projectile count if necessary.
5. Sound is the clearest future polish opportunity: give Dissonance purposeful chords
   against a flat drone, Ache three disagreeing pulses, Rot a low sub-bass absorption,
   Malady resolving harmonic swells, and Chronos a long warning tone with one hard impact.
