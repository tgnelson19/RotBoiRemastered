# Touch, Sight, and Chemesthesis Bullet-Hell Passes

This note records the three review, suggestion, and implementation passes for
the six Touch, Sight, and Chemesthesis encounters. “Stationary-safe” means a
position can survive a complete attack phrase without another movement; those
positions are intentionally removed while declared, temporary safe routes are
preserved.

## Pass 1 — collect stationary safe space

### Touch

- Review: Bair's River currents framed the player but left the center of the
  frame safe. Rot's rotating safe bank could remain usable for several casts.
- Suggestion: record the player's position, declare a radial line through it,
  and make lateral rotation the reliable response.
- Implementation: Bair now adds a seven-link `river_lock`. Rot adds a
  nine-link player-anchored pressure rake to every damage movement and an
  eleven-link rake during Choking Stillness. The alternating square/diamond
  links form a readable geological seam instead of an invisible area check.

### Sight

- Review: Ishe's Blink pair and Flash lanes could be solved with one early
  sidestep. Chronos often centered an intentionally safe gap on the player, but
  did not always collect that gap later.
- Suggestion: preserve the initial safe route long enough to read and reward,
  then strike its recorded center on a later beat.
- Implementation: Blink adds a delayed focus shot and Flash adds a focus
  volley after every horizon. Chronos schedules a telegraphed “second hand”
  down the recorded safe lane after its insight check, including during Still
  Second. Thorn of Time remains a direct, single-route threat and does not get
  a redundant closure.

### Chemesthesis

- Review: Kage already targeted the current player in every movement. Ache's
  intentionally wrong-way attacks, random deposits, and broad laser choices
  could combine into a full cast with no threat at the player's position.
- Suggestion: keep the uncommanded/random identity, but attach a small
  position-debt marker whenever the main choice is undirected.
- Implementation: Ache now drops a short-fuse `stationary_reflex` on every
  choice except Crossed Nerves, which already places a pool on the player. This
  includes predicted lasers—leaving their line is only the first dodge—and
  attacks made while the persistent-hazard soft cap is full.
  Kage's existing Feast claim, Provocation center shot, mirror claim, and Lure
  bomb remain the midpoint encounter's direct checks.

## Pass 2 — give each path a projectile grammar

### Touch

- Review: most Touch pressure used circles of the same diamond projectile.
- Suggestion: contrast heavy straight seams with lighter organic drift.
- Implementation: Bair gains paired sinusoidal spore curves in Swarm, Ruin,
  and Silence. Rot adds rotating sine-wave spore spirals in Bloom, Miasma, and
  Burial. Alternating square and diamond silhouettes make a pressure line
  readable before the player parses its velocity.

### Sight

- Review: Sight's laser language was strong, but too visually uniform.
- Suggestion: make afterimages refract and clocks shed curved fragments without
  replacing the path's precise line telegraphs.
- Implementation: Ishe's past/focus shots can split into three refracted
  children, while Afterglow echoes use real sine paths. Chronos adds curved,
  mixed-shape clock shards; the center shard splits after crossing part of the
  arena. The shards are lower-damage traversal pressure around the lethal
  tentacle routes.

### Chemesthesis

- Review: several projectiles were labelled as sine paths but had zero
  amplitude, so their motion was functionally linear.
- Suggestion: expose amplitude and frequency through the shared sin-projectile
  helper, then use unstable curves and late splinters as the path's motif.
- Implementation: Kage's Stagnant Mirror and Lure serpents now visibly weave.
  Feast fires a greed prism that splits after travelling. Ache's wrong-way
  misfire splinters into three and contamination debris follows a curved nerve
  path.

## Pass 3 — make survival phases actual dodge exams

### Touch

- Bair's Ruin layers five-link processions and curved spores over its four
  firing gates, rotating ring, and alternating ground mark.
- Rot's Choking Stillness emits a pressure rake every cast, adds a curved spore
  layer on alternating casts, and tightens from a 2.12-second to a 1.88-second
  cadence in its second half.
- Burial retains the rake, spore, bomb, pool, and advancing-front vocabulary
  while respecting the global projectile ceiling and Rot's burden soft cap.

### Sight

- Ishe's Flash begins with three adjacent safe lanes, then collapses to one
  safe lane after the third horizon. Its second-half cadence tightens to 1.32
  seconds, with focus volleys collecting old positions.
- Chronos's Still Second overlays four curved frozen-clock shards, checks the
  declared route for temporal insight, then closes it with the delayed second
  hand. Its cadence tightens from 2.08 to 1.82 seconds.
- King's Attrition now peaks at, but does not exceed, its 112-route authored
  soft cap and retains complete tentacle declarations rather than truncating
  attacks.

### Chemesthesis

- Kage's Stagnant Mirror keeps slow weaving reflections and settling mines,
  then marks the player with a short-fuse mirror snap and five-way release.
- Ache's Reflex Storm overlays a five-shot curved reflex spiral on every
  accepted main pattern. Its cadence tightens from a randomized 1.62–1.96
  seconds to 1.38–1.68 seconds in the second half.
- Ache still favors sparse, distributed hazards: the spiral and stationary
  marker supplement its random field instead of introducing portals or a
  petal-style screen blanket.

## Further bullet-hell ideas

These are recommendations, not part of the implemented passes:

- Add a graze meter that rewards passing close to declared projectiles. A
  visible graze spark would make intentional micro-dodging feel better without
  changing collision rules.
- Give each projectile family a fixed danger contract: cream outlines for
  telegraphs, solid saturated cores for active damage, hollow shapes for
  illusory/non-colliding warnings, and a unique sound envelope for bombs,
  lines, and persistent ground.
- Add a practice console that starts any named boss movement with damage
  disabled, displays peak projectile count, and can pin the player at chosen
  arena coordinates for anti-camp regression checks.
- Record heat maps for player position, hit position, and empty safe-space
  duration. Tune toward many short-lived routes rather than one permanent safe
  quadrant.
- Touch: add pressure valves the player can shoot to reverse one advancing
  bank, sludge droplets that harden into temporary walls, and square-edge
  “conveyor” patterns that rotate ninety degrees between beats.
- Sight: add gaze shutters that briefly hide alternate projectile rows,
  harmless future-ghost bullets that become solid on replay, and prism nodes
  that visibly refract one declared laser into two later routes.
- Chemesthesis: add chain-reaction mine nerves with clearly numbered pulses,
  crystal prisms that redirect a laser when broken, and two-color stimulus
  thresholds where touching one color temporarily makes the other color more
  dangerous.
- For every survival phase, author one recognizable four-to-eight-beat
  “spellcard” sequence. Random selection can remain between sequences, but each
  sequence should have a learnable opening, traversal, reversal, and release.
