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

## Kage and Rot — Path of Chemesthesis

Kage and Rot replace the Chemesthesis path's three-phase placeholders with a shared
seven-sins projectile language. Their attacks favor long-lived mines, orbiting
hazards, bombs, curved volleys, and telegraphed lasers so the path continues to feel
like a durable organism contaminating the battlefield.

Kage has four composite phases:

1. **Feast** combines Gluttony and Greed in a spreading mine meal.
2. **Provocation** combines Wrath and Pride in an aimed volley and retaliatory beam.
3. **Stagnant Mirror** combines Sloth and Envy in slow curved copies and lingering mines.
4. **Lure** combines Lust and Avarice in wide tempting lanes and a targeted bomb.

Rot has seven phases, one per sin: **Crown** (Pride), **Hoard** (Greed), **Pull**
(Lust), **Borrowed Shape** (Envy), **Consumption** (Gluttony), **Retort** (Wrath),
and **The Rot** (Sloth). The final phase recalls simplified Pride, Gluttony, and Lust
patterns over an expanding persistent mine field.

Both fights advance phases at evenly divided health thresholds. Number-key boss
practice supports all four Kage phases and all seven Rot phases through the shared
debug phase interface.

Rot is stationary and divides its seven sins into three cinematic acts. Act I,
**Appetite**, contains Pride and Greed. Act II, **Temptation**, contains Lust and
Envy. Act III, **Saturation**, contains Gluttony, Wrath, and the Sloth finale. Each
act transition clears the existing hazard field, briefly protects Rot, and announces
the next terrain vocabulary. Health gates prevent burst-damage builds from skipping
a sin or its transition.

Chemesthesis hazards can apply **Exposure**, a temporary movement-pressure meter
that decays after avoiding contaminated terrain. Pull hazards briefly draw the player
toward Rot, while Sloth hazards apply a direct slow. These effects never reduce
movement below 58%, and dashing overrides pull, preserving a difficult escape option.

Rot's Envy phase reads an immutable snapshot of the current build. Critical builds
receive a telegraphed beam imitation, tempo builds receive fast narrow volleys,
precision builds receive compact high-speed lanes, and volley or power builds receive
scaled projectile fans. The snapshot cannot alter the player's upgrades.

The encounters use a restrained visual grammar intended to preserve bullet clarity:
faint chemical diagrams identify each sin beneath the boss, sparse spores rise from
the reaction field, phase-colored ampoules fill as health gates are crossed, and
seven orbiting reaction vessels show Rot's progress. Exposure adds a dark red edge
vignette rather than a center-screen filter, leaving precise terrain routes visible.

Each phase also has a bespoke geometric sin sigil. Pride rises into a crown, Greed
closes into a faceted vessel, Lust converges into a sharp heart, Envy duplicates,
Gluttony opens into a maw, Wrath fractures into crossed bolts, and Sloth descends
into an incomplete spiral. Kage's four sigils combine strokes from the paired sins.
Symbols are supersampled for crisp edges, progressively draw their strokes on phase
entry, rotate the previous mark away, and appear on the boss ampoule, field diagram,
and cinematic act card.

### Further Chemesthesis directions

Four cleansing vents surround Rot. Entering a ready vent removes Exposure, puts that
vent on a twelve-second cooldown, and crystallizes the corresponding inner route for
seven seconds. Greed and Sloth also grow temporary solid crystal walls. These walls
participate in player movement collision, are capped to prevent an unsolvable field,
and are cleared at phase transitions.

Pride and Wrath derive their cardinal lanes from the camera orientation, alternating
screen-horizontal and screen-vertical pressure in world space. Rot reports three
stable challenge hooks: `clean_traversal` for staying at or below 3 Exposure,
`vent_discipline` for using at most one vent, and `uncontaminated` for effectively
never accumulating Exposure.

### Additional directions

Crystals now have brittle and reinforced variants. Brittle walls are registered as
targetable boss parts and break after sustained player fire; reinforced walls cannot
be damaged and must expire. Gluttony consumes the oldest active wall when attacking,
draining 25% of Rot's stagger meter and producing a visible absorption pulse.

Act transitions draw a clean-route preview between ready vents. During Sloth,
opposite reinforced walls appear beyond the active field, telegraph for 2.5 seconds,
then become solid and compress inward at a fixed rate. Compression repeats every
twelve seconds and stops before eliminating the central traversal channel.

### New recommendations

- Allow critical hits to fracture brittle walls into two short-lived shrapnel lanes,
  making destruction powerful but not automatically safe.
- Give vents one of two visible reagents per run: cooling reagent could grant brief
  slow immunity, while caustic reagent could increase damage during Exposure.
- Add an accessibility toggle that increases crystal-warning contrast and extends
  compression telegraphs without changing projectile damage.
- Record a route-efficiency result based on distance traveled versus phase duration,
  rewarding deliberate navigation rather than constant perimeter circling.
- Let Kage reveal dormant, nonfunctional versions of vents and crystals so players
  learn their silhouettes before Rot makes them mechanically important.
- Add act sound layers once the project has a shared audio mixer, volume preference,
  and accessibility option. Suggested motifs remain glass resonance for Appetite,
  breath/heartbeat for Temptation, and low bubbling pressure for Saturation.

## Hypno and Malady — Path of Phantasia

Hypno now teaches five paired commandment concepts through **Idol**, **Spoken Rule**,
**Inheritance**, **Chosen**, and **Offering**. Malady expands that language into ten
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

## Bair and Sting — Path of Touch

Bair previews the ten plagues as five paired phases: **River**, **Swarm**, **Blight**,
**Ruin**, and **Silence**. Sting separates them into ten health-gated phases:

1. **Blood — Corruption:** slow radial contamination.
2. **Frogs — Overrun:** clustered heavy landing bombs.
3. **Gnats — Infestation:** a dense but slow radial swarm.
4. **Flies — Invasion:** broad heavy fans and square-gate crossfire.
5. **Pestilence:** a deliberate expanding ring.
6. **Boils — Affliction:** large targeted blast zones.
7. **Hail — Impact:** descending heavy lanes and gate volleys.
8. **Locusts — Devour:** a wide consuming projectile wall.
9. **Darkness:** narrow, slow pressure with moving gates.
10. **Firstborn — Severance:** radial closure plus a high-damage central judgment.

Each plague has a geometric sigil derived from its defining word. Touch portals are
square iron gates rather than Dissonance's fluid mouths. They march around the square
arena boundary, require eight separate hits to disable, and fire paired slow heavy
shots. Portal phases currently include Frogs, Flies, Hail, Darkness, and Firstborn.

Sight now has meaningful **Glimpse**, **Blink**, and **Flash** symbols: an eye, a
closing mirrored aperture, and a lightning stroke. These are a visual placeholder for
a later full Sight phase expansion.

### Recommended polish order

1. Playtest arena dimensions and moving-boundary containment before tuning damage.
2. Give Sight a full phase structure next; its current symbols and triangular arena
   establish the language, but Chronos remains mechanically shallower than the others.
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
