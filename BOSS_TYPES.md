# Boss Framework

Bosses are registered in `bossTypes.py` through `BOSS_CATALOG`. A boss subclasses
the same `Enemy` contract as regular enemies, so player bullets, contact damage,
knockback, death rewards, and composite hitboxes continue to work normally.

Boss-specific additions are intentionally small:

- `bossName`, `phase`, and `phaseLabel` feed the shared boss HUD.
- `updateEnemy(...)` owns explicit phase transitions and pattern timers.
- Boss projectile patterns use the shared `EnemyProjectile` paths: `linear`, `sine`,
  `mine`, `orbit`, `laser`, and `bomb`.
- Projectiles can carry an `owner` label, allowing a boss's arena hazards to be
  cleaned up when the boss dies.
- `ProjectilePortal` is a boss-owned arena emitter with its own orbit, cadence,
  spread, and projectile ownership. Beaudis begins phase one with three portals
  circling the arena and firing staggered shotguns toward its center.

## Beaudis

Beaudis is the run's level-10 boss encounter. Reaching level 10 clears the current
arena, disables regular spawning, and starts the fight once per run. The hidden
`B` debug shortcut remains available at any level and does not consume the natural
level-10 trigger.

### Rune choreography

Every act uses a chase, anchor, and revolution movement phase. Othala opens the fight
as a tightening hearth-storm; Raidho holds the crossroads; Kenaz corkscrews like a
torch vortex. Hagalaz advances in diagonal hail-steps; Eihwaz blinks between mirrored
positions; Sowilo traces a lightning-shaped revolution. Tiwaz intercepts escape lanes;
Dagaz commands the center; Jera is revealed last and curves after the player like a
harvesting blade.

Portals inherit phase-specific wave, square, figure-eight, and tornado paths. Their
standard projectiles are enlarged and persist across the full boss arena, while mines
remain intentionally local. The arena is 32/3 tiles in radius. The base stagger
threshold is thirty direct player hits, resets at every phase, and cannot build during
the five-second transition breather. Ordinary hits deal 45% chip damage; staggered
hits receive a stronger punish multiplier while Beaudis and every portal stop firing.

Rune lasers spend one second drawing a visible firing lane, then blast continuously
for three seconds. Rune bombs arc toward the player's location, sit through a two-second
fuse, and burst into eight radial projectiles. These attacks alternate across the phase
roster so their telegraphs remain readable amid the denser portal volleys.

All ordinary Beaudis and portal projectiles carry twice the prior range and are culled
at the ritual boundary rather than fading before reaching it. Survival phases add a
second ring of six boundary portals firing sparse, variable-speed inward lanes; the
final survival uses eight. Every survival projectile has infinite logical range and is
removed only at the ritual edge. Phases three and six automatically advance after 20
seconds. Phase nine becomes a 30-second Jera survival whose completion kills Beaudis
and begins a 10-second active collapse of splitting faces, harmless lasers, fireworks,
shock rings, particles, and restrained screen shake before releasing the boss reward
in a final burst of varied-size pixel particles.

Tiwaz now raises four independently rotating projectile diamonds. General portal counts
are reduced to keep formations legible, and portal aim-preview lines are suppressed
while true portal-to-portal ritual connections remain. Lasers appear throughout every
phase. Bosses and portals can also emit variable-count comet trains whose leading shot
is fast and whose increasingly large tail shots move progressively slower.

While Beaudis lives, an opaque arena mask hides the surrounding map. The thicker seal
uses orbiting particles, traveling packets, and three counter-moving sinusoidal rings.

Beaudis has 480 base HP to account for the upgrades accumulated before level 10.

### Visual identity

Beaudis is rendered as a continuously yawing, gently pitching pseudo-3D cube with a
glowing Elder Futhark rune suspended in its central void. The rune spins into place
when a phase begins and pulses afterward. Phase one begins with Jera; subsequent
phases use Raidho, Kenaz, Hagalaz, Eihwaz, Sowilo, Tiwaz, Dagaz, and Othala. The cube
faces, rune glow, and portal formation inherit the current phase accent color.
Its animation layers include phase-speed rotation, transition spin bursts, breathing
face highlights, counter-rotating energy arcs, orbiting rune shards, a pulsing inner
chamber, and unstable shake/electrical cracks while staggered.
The spectacle layer remains deliberately pixel-native: ambient square motes orbit the
core, portal breaks implode into block fragments, stagger and rune interrupts produce
phase-colored shatter bursts, damage flashes the cube, and act boundaries display
cinematic glitching title cards for The Fracture and The Return.
Portal tethers carry animated square energy packets toward the arena core, reinforcing
the sense that every formation is actively channeling power into the rune machine.

### Portal counterplay and rune interrupts

Portals are targetable through the boss's composite hitbox. Three hits disable a
portal for the rest of the current phase: player shots then pass through it, while its
volleys contain roughly half as many projectiles. Portal hits do not add boss stagger.
Every phase restores all portals to full interception and firepower. Each portal carries
the current phase's line-drawn rune in its core, retains its motion trail, and uses
charge rings or targeting lines to communicate incoming attacks.

For 0.75 seconds after phases 2-9 begin, hits also disrupt the newly forming rune and
grant 50% bonus stagger. Building 18 disruption during that window breaks the rune,
silencing Beaudis and every portal for 2.5 seconds. The HUD displays the remaining
silence duration.

Portals also carry visible positive/negative polarity marks and can follow circular,
square, or figure-eight paths depending on the rune phase. Player bullets that enter
an active portal are routed through its opposite partner, gain damage, and respond to
the destination polarity: positive exits empower the shot, while negative exits
reverse it. A short routing lockout prevents loops.

Stagger now exposes three visible buildup tiers. Breaking Beaudis during a rune's
formation creates a Perfect Break, extending vulnerability by 2 seconds. Continued
damage during vulnerability builds capped fracture, increasing subsequent damage by
2% per fracture point. Recovery ends with a one-second no-attack reconstruction pulse
instead of resuming instantly. Later acts also inscribe the active rune across the
arena floor, with its strokes revealing during the phase entrance.

Revolve onward can invoke the Rune Cannon: every active portal visibly
channels into one marked receiver for 1.4 seconds before it releases a nine-shot fan.
Breaking the receiver during the channel backfires, grants 20 stagger, and silences
the formation briefly.

The encounter records stable challenge hooks for future reward UI and drops: finishing
without breaking portals, never allowing stagger to decay, interrupting at least three
runes, and landing at least three Perfect Breaks. These are exposed through
`challenge_results()` without coupling the boss to a not-yet-existing achievement or
boss-loot screen.

The fight has three acts, each divided into three equal health bands:

Every phase also has an 18-second enrage limit. If the player has not dealt enough
Each damage phase lasts up to 36 seconds. Phases 1, 2, 4, 5, 7, and 8 form one
pseudorandom attack pool. When a damage-phase timer or stagger window ends, Beaudis
chooses from that pool while excluding the three most recently used damage phases.
HP reaching 2/3, 1/3, and zero takes priority and unlocks survival phases 3, 6, and 9
in order; random selection and timers can never enter, skip, or regress those gates.

## Stagger and damage windows

Each ordinary hit adds at least 6 stagger to a 240-point meter. After two seconds
without damaging Beaudis, accumulated stagger drains gradually at 16 points per second.
Filling the meter clears hostile projectiles, freezes all boss and portal attacks, and
opens a five-second damage window without changing the current phase. When that window
ends, Beaudis becomes invulnerable, clears projectiles again, moves to arena center, and
presents the next phase for five seconds before combat resumes.

### Act I: Defend (100%-67%)

1. **Bolster:** pursues to attack range while three portals fire inward shotguns;
   the portals also send decelerating mines inward while Beaudis adds sinusoidal fans.
2. **Prepare:** portals stop aiming inward and alternate tangential
   shotguns, turning the arena perimeter into a rotating lane puzzle.
3. **Retaliate:** the portals accelerate and draw their orbit inward, steadily
   compressing the player's safe routes.

### Act II: Static (67%-33%)

4. **Contemplation:** four portals form a rotating cross. Opposite pairs draw narrow
   projectile chords across the arena while alternating portals fire aimed sine waves.
5. **Mirror:** Beaudis jumps toward the player and leaves a visible portal at
   both its departure and landing point; the two echoes fire offset radial bursts.
6. **Dominate:** sends a visible transfer shot around a four-portal circuit. Each
   receiving portal redirects it as a shotgun along its incoming path.

### Act III: Madness (33%-0%)

7. **Revolve:** returns to arena center and constructs a rotating mine field.
   Five breathing-radius portals draw pentagram edges and take turns firing sine fans.
8. **Display:** accelerates the mine field while rotating pairs of constellation
   portals fire through the core, producing danger diameters with readable gaps.
9. **Grandeur:** dismisses the field, summons six fast portals, and layers their
   inward volleys with rapid radial rings that always preserve a two-shot escape gap.
   It periodically recalls Prepare tangents, Contemplation chords, and Dominate
   transfers, turning the finale into a test of previously learned patterns.

The in-run `[B] BOSS` control clears current enemies and hostile projectiles, disables
normal spawning during the encounter, grants a short entry grace period, and summons
Beaudis with ordinary player damage enabled. Pressing `Y` toggles invincibility. This is a
debug encounter and does not grant rewards for the enemies it clears.

The debug summon now centers Beaudis and the player inside a dedicated 32/3-tile
radius combat ring, focusing the camera and encounter on the boss rather than the
larger generated room. Beaudis spends three seconds assembling its independently
separated cube faces before attacks begin. A lethal hit starts a 3.2-second collapse:
portals power down, faces drift apart, rune particles burst repeatedly, and camera
shake decays before the boss is finally removed.

All portal formations and the persistent glowing floor rune scale with the doubled
arena. The boundary now carries rotating pixel packets and an inward energy wave;
portals use nested counter-rotating rings, and hostile projectiles retain short block
trails. Diamond and mine shadows match their silhouettes, while Beaudis renders one
offset shadow polygon per projected cube face instead of a rectangular backdrop.

Hostile linear movement runs at 70% of its authored speed. Stagger is tuned to 240
points with a minimum gain of 6 per ordinary hit, producing 40 baseline hits to break
Beaudis before portal-break bonuses.

Phases 3, 6, and 9 are survival finales. Boss-body hits do not build stagger or deal
HP damage during survival, and portal breaks no longer add boss stagger there.
Surviving phases 3 and 6 advances to the next act. Reaching zero HP begins phase 9
rather than killing Beaudis; surviving it starts the death collapse.

After a stagger's five-second punish window, the encounter selects an eligible random
damage phase, or enters the unlocked survival phase at an HP gate. Phase
announcements identify each pattern with its uppercase English rune name instead of a
numeric phase prefix.

Every phase change now requests a complete hostile-projectile cleanup, removes the
old formation, smoothly draws Beaudis to arena center, hides the replacement formation, and
holds the new flavor declaration for five seconds before attacks resume.

Boss practice controls are available while Beaudis is active: number keys 1-9 jump
directly to a phase, R restarts the current phase, L locks/unlocks phase progression,
F forces a stagger, and C readies Rune Cannon. Phase jumps and restarts clear hostile
projectiles so patterns can be evaluated in isolation.
