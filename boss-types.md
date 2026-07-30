# Boss encounters

Bosses are registered through `BOSS_CATALOG`. A boss exposes the normal enemy
combat contract plus phase metadata for the shared boss HUD. Every path has a
level-10 lesson and a level-20 examination built from the same pacing grammar:

| Path | Level 10: HP / contact / phases | Its one survival | Level 20: HP / contact / phases | Survival phases |
| --- | --- | --- | --- | --- |
| Sound | Beaudis: 50,000 / 220 / 5 | Endure, 14s at 50% HP | Dissonance: 150,000 / 550 / 9 | 20s at 67%, 20s at 33%, Jera 40s at 0% |
| Touch | Bair: 110,000 / 380 / 5 | Ruin, 14s at 50% HP | Rot: 330,000 / 980 / 7 | Choking Stillness 22s at 50%, Burial 35s at 0% |
| Sight | Ishe: 75,000 / 300 / 4 | Flash, 12s at 50% HP | Chronos: 310,000 / 880 / 7 | Still Second 20s at 50%, King's Attrition 35s at 0% |
| Chemesthesis | Kage: 93,000 / 340 / 4 | Stagnant Mirror, 14s at 50% HP | Ache: 305,000 / 880 / 8 | Reflex Storm 20s at 50%, Overload 30s at 0% |
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

Its four damage movements teach Dissonance's sound grammar without reproducing the
final boss's portal-routing puzzle:

1. **Awaken** declares a direct call with two slower sine-wave echoes.
2. **Answer** creates a cross-arena response from two lateral echo origins.
3. **Press** leaves a player-aligned opening in a ten-note radial phrase, then
   answers through that opening with a three-shot fan.
4. **Persist** layers a five-shot fan over an offset six-note pulse.

Endure occurs once at half health and lasts 14 seconds. Four slow portals appear and
take turns firing two-projectile volleys while Beaudis is invulnerable. Completion
opens Press, followed by Persist for the last quarter. Reducing that remaining half
to zero begins a three-second fade. It has no finale-collapse animation; the italic line
*“You can't escape me...”* remains visible until the fade completes and the XP drop
is released.

Every damage movement must complete two phrases before its health gate can advance.
Beaudis pauses new declarations once 36 of its threats remain active; measured peaks
remain at or below 48 even after a just-admitted phrase. Base balance: 50,000 HP,
220 contact damage, 0.38 movement speed, 90 stagger, and 240 XP.
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
so their formations remain a constant threat for the entire survival timer. Disabling
a portal now contributes one third of the stagger bar; committing to three portal
breaks earns a five-second Resonant Stagger rather than functioning only as a
firepower tax.

Every damage rune must commit at least two attack phrases before its act gate or
36-second rotation can advance. A one-HP floor protects the final gate until this
condition is met, and Dissonance cannot be removed by the live session before Jera
and its ten-second collapse complete. New threats are staged as a complete frame
phrase and admitted only when the resulting encounter-owned count remains at or
below 132. Bomb fragments reserve eight additional slots, keeping the fight below
the global 150-projectile emergency limit without silently cutting a volley in half.

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
not reused by Beaudis. Jera now assembles a cosmetic grand staff across the arena:
five stable stave lines, expanding chord wavefronts, and one remembered rune at a
time until all nine phases form a complete perimeter. These visuals do not consume
the hostile-projectile budget.

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

Kage is Chemesthesis's 93,000-health midpoint lesson, introducing the path through four
composite-sin patterns. Ache completes the path in a larger jagged arena as one
deliberately small orange-and-blue oscillating core.
Exactly three blue perspective cubes orbit it as unruly arms. Their connectors, depth
sorting, unequal orbit speed, and widened survival constellation make the silhouette
feel powerful but incapable of following a common command.

Ache's collision-born origin is reflected in overlapping visual systems rather than an
ancestral court: orange and blue never fully blend, its old sin-sigil labels have become
Phantom, Border, Recoil, Reflex, Trespass, Splinter, Static, and Unbound, and each attack
appears to answer a threat or territorial violation that may not exist.

Kage has three damage movements around one midpoint survival:

1. **Feast** combines Gluttony and Greed in a spreading mine meal, with one slow morsel
   claiming the player's current route.
2. **Provocation** combines Wrath and Pride in a finite aimed volley and fully warned
   retaliatory beam.
3. **Stagnant Mirror** combines Sloth and Envy in a real 14-second invulnerable
   half-health survival, using two curved reflections, a player-claiming mirror, and
   settling mines.
4. **Lure** combines Lust and Avarice in finite wide lanes and a marked four-fragment bomb.

Kage hesitates at thirty-six active threats and remains inside a measured fifty-projectile
envelope. Feast, Provocation, and Lure must each present at least two complete composite
declarations before their health gate can advance or end them.

Ache has six damage movements and two survival movements. **Misfire**, **Crossed
Nerves**, and **Wrong Way** lead into the 20-second **Reflex Storm** at half health;
**Aftershock**, **Fracture**, and **White Ache** lead into the 30-second **Overload**
at zero health. Every damage movement receives at least two complete declarations before
its next gate. During Overload, three uncommanded phantom nodes multiply into a twelve-node
orange-and-blue neural constellation that expands behind active hazards without consuming
the projectile budget. Completing Overload begins a 10-second expanding constellation collapse.

Ache selects each attack from a phase-authored family without exposing a learnable
rotation, but remembers its previous mistake and cannot repeat it immediately. At least
once every three resolved attacks it must answer the player directly. Wrong-way debris
drifts slowly and settles into mines, lazy clusters appear in unrelated arena regions,
and corner pockets close three random sides of the player while leaving one telegraphed
escape. Later movements can answer an apparent dodge with a delayed offset lash. Bombs
land around rather than directly on the player, contamination pools announce themselves
for 1.25 seconds; fully drawn
lashes provide the heaviest warning. Primary hazards are authored at 190–230 damage
before Chemesthesis path modifiers, producing roughly five-to-eight-hit lethality for a
standard level-20 defensive baseline. Ache uses no projectile portals.

The active field is deliberately sparse. Ache hesitates at thirty-six active threats
and switches away from mine-building at twenty persistent hazards; delayed reactions
can briefly raise the simulated envelope to fifty total projectiles and twenty-eight
persistent hazards. Random placement, long mine memory, and contradictory counterattacks
create the pressure; raw bullet count does not.

Four cleansing vents sit on the outer jagged court. A vent removes Exposure but raises
a short-lived inner border on the same route. After half health, false-alarm walls begin
at the outer court with a 2.5-second warning and compress toward the core. Brittle walls
can be shot apart; reinforced walls must be routed around. These borders expire instead
of accumulating, keeping Ache's nervous territorial overreaction distinct from Rot's
patient geological occupation.

Breaking three brittle borders grounds the uncommanded core: the existing stagger system
fills immediately, disabling Ache for its normal 2.5-second fracture window and preserving
the established 25% stagger damage reward. The progress survives individual phase changes,
so aimed terrain interaction is a path-mastery option rather than a one-pattern trick.

Chemesthesis hazards can apply **Exposure**, a temporary movement-pressure meter that
decays after avoiding contaminated terrain. These effects never reduce movement below
58%, and dashing remains a reliable escape option.

## Hypno and Malady — Path of Phantasia

Hypno is Phantasia's 107,000-health, 360-contact midpoint lesson. Its four damage
movements teach how to distinguish harmless spectacle from real intention:

1. **Idol** places one truthful shrine among two illusory copies.
2. **Spoken Rule** sometimes contradicts its banner, revealing a separate true sigil.
3. **Inheritance** sends three bifurcating lineages beside one direct claim.
4. **Offering** alternates real and illusory rings around an unavoidable debt fan.

Idol, Spoken Rule, and Inheritance occupy the opening half at 75%, 62.5%, and 50%
health gates. Each must complete two suggestions before advancing. **Chosen** then
seals vitality for fourteen seconds, pairing a real aimed volley with a harmless
illusory cage, before Offering receives the remaining half. Hypno budgets future
split descendants as well as currently visible shots, admitting no new phrase when
its projected pressure would exceed 48 threats.

Malady expands the path into ten flowing
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

Hypno retains Phantasia's shared **Belief** lesson, false rules, truth-marked
projectiles, and offerings.
Malady deliberately disables those inherited mechanics. Her identity is divine novelty,
not deception: every dangerous formation is real, every large hit is honestly marked,
and the challenge is navigating beauty without letting density hide the intended shore.

Malady's mechanics are portal-authored petal floods, rigid twin portal gears, slow
curved ribbon chains, splitting projectile tentacles, player-facing openings in radial
flowers, and fully telegraphed cathedral lasers with adjacent aisles left empty. Ground
pools stay on the inner 64% of the arena so the outer court always offers a less
threatened shore, but faster aimed portal phrases and arena-spanning volleys ensure that
the shoreline is never a stationary safe zone. Every damage movement must declare
two complete ideas before its ten-percent health gate can advance, including
Apotheosis before the zero-health seal. Each update stages its entire phrase and
admits it only below a 132-threat encounter budget, reserving eight additional slots
below the 150-projectile emergency ceiling for spawned descendants.
Apotheosis cycles floods, tentacles, portal phrases, and laser arches for thirty seconds.

Malady is a tall indigo cubic pillar moving freely above the court, with no solid
foundation to imply that she is rooted or that scenery belongs to her hitbox. A vertical
magenta-white inspiration slit and three luminous nodes keep the center legible, while
ten to eighteen independent perspective cubes compose a phase-specific body grammar:
blossom, spiral, engine lattice, ribbon, branches, empty ring, tide, cathedral columns,
and inward soul spiral. During Apotheosis those grammars recombine with each attack.
The finale begins with a six-petal inspiration mandala and progressively unfolds into
the full eighteen-cube double crown, surrounded by three expanding non-damaging rings.
This visual construction does not consume hostile-projectile capacity.
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

Bair is Touch's 110,000-health midpoint lesson, presented through **River**, **Swarm**,
**Blight**, **Ruin**, and **Silence**. River declares paired slow banks, then advances
the second pair half a lane so its first opening cannot be camped. Swarm combines a
complete player-facing opening with two square iron gates marching around the boundary.
Blight marks the player's current ground and both adjacent escape sides with three long-fuse
falling masses. Ruin is the single 14-second half-health survival: four gates join a
sequence of opened radial falls and alternating ground marks. Silence is the damageable
synthesis. Gates require eight separate hits to disable, visibly declare their paired
slow heavy shots for 0.55 seconds, and fire finite-lived projectiles rather than filling
the global budget. Every damage movement must make at least two complete declarations
before a health gate can advance or end it; stationary-edge simulations remain within
a sixty-projectile envelope.

Rot is Touch's complete level-20 encounter. Its seven stages of accumulation are
**Seep**, **Silt**, **Slump**, **Choking Stillness**, **Bloom**, **Miasma**, and **Burial**.
The fight uses only static and extremely slow pathed movement inside Touch's rigid
square arena. Slow edge-reaching fronts, orbiting clots, long-lived sludge pools,
spores, sweeps, and advancing mud banks progressively remove open ground. Mud banks
condense partway across the court as broad cracked slabs rather than arriving as
conventional bullet volleys. Their clean corridor turns with geological inertia every
few deposits instead of snapping to the player. Attack geometry always uses the real
player position even while Rot's body follows its extremely slow authored path.

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
for at least 1.65 seconds and occupy inner rings or settle along the square banks while
every formation omits a broad committed corridor. Slow sludge fronts use the same
opening, and three-second falling masses mark recently safe ground so remaining still
is eventually punished without hiding the adjacent escape.

Following three consecutive geological turns of the clean bank earns **Relief**: up to
ten of the oldest persistent pools, advancing banks, or falling masses are shed from the
arena, accompanied by a pale release ripple. This is a movement-mastery reward rather
than a weak point or damage multiplier; holding the original bank cannot earn it. As with
Bair, every damage movement receives at least two full declarations before its next gate.

**Choking Stillness** is a 22-second invulnerable half-health survival with Rot static
near the center. Reaching zero health unlocks **Burial** as a 35-second invulnerable
spectacle. Five enormous cracked square strata contract across the arena while a
translucent curtain of absorbed cubes falls behind the active hazards; these layers are
visual pressure and never conceal or alter the committed safe corridor. Completing Burial
clears hostile projectiles and begins the ten-second expanding collapse before the Touch
path reward is released.

## Ishe and Chronos — Path of Sight

Ishe introduces Sight's **Glimpse**, **Blink**, **Flash**, and **Afterglow** through
four short, fast movements. Glimpse declares a complete fan from Ishe's current position,
then fires it after the body has moved. Blink layers two captured positions on separate
beats. Flash is its one 12-second half-health survival: parallel strikes advance inward
from each triangle edge while three adjacent horizon lanes remain empty. Afterglow
combines present and former positions in one fully warned synthesis. Its late-pressure
simulation peaks at thirty-two active warnings and shots without touching the global
projectile ceiling. Every damage movement must present at least two complete declarations
before a health gate can advance or end it. Chronos inverts that lesson: the King of Attrition is
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
3. **Wearying Lash** declares two seven-segment flails, then reveals the rear route
   on a readable second beat before any segment strikes.
4. **Still Second** is a 20-second midpoint survival: ten possible arms are shown,
   but the three closest to the player's escape angle are always absent.
5. **Parallax** combines the learned opening with one large independent flail,
   declared a fraction later so the safe choice must be revised.
6. **Thorn of Time** alternates a twelve-arm crown with the fabled single eight-segment
   killing line and a weaker adjacent temporal echo. Both are fixed when declared and
   remain harmless for 2.35 seconds.
7. **King's Attrition** is the 35-second finale, cycling directive pairs, crowns, rear
   lashes, and the Thorn while periodically replaying a remembered declaration.

Every tentacle is a chain of five to eight finite laser segments. The full chain remains
harmless for 1.5–2.35 seconds, then every segment activates together for less than a
second. Chronos does not create pools, walls, mines, or surprise contact jumps. Its
pressure comes from 730–1,260 damage declared routes and choosing the empty route in time.
Chronos admits complete routes atomically under a 112-segment encounter budget, so
projectile pressure can postpone a declaration but can never erase half a warning. Its damage
movements likewise receive at least two complete declarations before their next health gate.
Correctly occupying three declared safe routes builds temporal insight and fractures
Chronos for 5.5 seconds, raising incoming damage by 18%. King's Attrition also leaves
fading, non-damaging histories of its recent routes behind the active declarations;
these memories are visual pressure only and do not consume the projectile budget.
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
