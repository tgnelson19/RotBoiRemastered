# Path Boss Iteration Log

This log records five consecutive review → ranking → implementation cycles for
the composite Path boss roster. Malady and Dissonance are the reference
encounters: both protect phase declarations from burst skipping, clean obsolete
projectile fields during transitions, separate damage and survival beats, cap
active pressure, telegraph consequential attacks, change movement with phase,
announce narrative phase language, and end through readable death choreography.

The authored floor-five and floor-ten bosses already use that grammar in
sense-specific ways. Most implementation work therefore belongs to the
ordinary Path Guardian and its treasure-room mini-guardian variant.

## Iteration 1 — Establish a trustworthy difficulty baseline

### Review

- Authored bosses were passed through `GamePaths.ApplyEnemyIdentity`, even
  though their constructors already own boss health, damage, speed, scale, and
  sense identity.
- This multiplied boss health by `0.56×` in Sight and `2.15×` in
  Chemesthesis before the shared floor curve, with similarly large projectile
  mutations.
- Any phase comparison made on top of that range would tune five different
  stat accidents rather than five intentional fights.

### Ranked suggestions

1. **P0 — Remove ordinary-enemy stat transforms from authored bosses.**
2. **P0 — Preserve authored projectile timing, size, range, and damage.**
3. **P1 — Retain the shared Path/NG+ curve exactly once.**
4. **P2 — Audit natural midpoint/finale health bands after normalization.**

### Implemented

- Added a shared authored-boss classifier covering Beaudis, Dissonance,
  PathChaseBoss descendants, and Path Guardians.
- Authored bosses now retain constructor balance and receive only NG+ plus the
  current Path floor curve.
- Their projectiles receive content-path identity without a second stat pass.
- Added cross-sense baseline tests and natural roster tier-band coverage.

## Iteration 2 — Give guardians a real boss structure

### Review

- Guardians had health floors and three labels, but no survival beat,
  narrative phase metadata, committed-threat accounting, or explicit threat
  budget.
- Transition invulnerability was present but too small a contract to resemble
  Malady/Dissonance encounter structure.
- Every sense used switch fragments rather than one inspectable encounter
  profile.

### Ranked suggestions

1. **P0 — Add a protected survival/intermission between damage phases.**
2. **P0 — Count only successfully admitted attacks as phase declarations.**
3. **P0 — Cap guardian-owned active threats before committing a pattern.**
4. **P1 — Move identity, cadence, movement, and phase language into profiles.**
5. **P2 — Add transition/death lifecycle state usable by presentation.**

### Implemented

- Added five data-driven Guardian profiles with authored names, subtitles,
  phase labels/flavor, cadence, preferred distance, orbit, movement, secondary
  color, trial name, and trial duration.
- The second health threshold now opens a short invulnerable sense trial before
  final damage resumes.
- Every phase requires two successfully committed declarations.
- Added owner-local transition cleanup and a 62-threat soft cap.
- Added sense trials: a Sound safe-wedge chord, Touch pressure banks, Sight
  rotating lenses, Chemesthesis mine sectors, and Phantasia truth/illusion
  petals.

## Iteration 3 — Increase difficulty through mechanics, not opacity

### Review

- The shared floor curve already raises health, damage, and speed.
- Additional raw multipliers would recreate the Iteration 1 problem.
- Second-act cadence was faster, but its pattern grammar did not consistently
  communicate why the encounter was harder.
- Fast rays and persistent hazards needed explicit warning floors.

### Ranked suggestions

1. **P0 — Add bounded second-act pattern complexity per sense.**
2. **P0 — Enforce warning floors on high-consequence attacks.**
3. **P1 — Preserve first-act safe lanes and slower lesson cadence.**
4. **P1 — Verify every natural midpoint/finale remains in its authored tier.**
5. **P2 — Add repeatable pressure simulation for later telemetry tuning.**

### Implemented

- Floors six through nine add spokes, banks, rays, mines, or additional true
  petals depending on the active sense.
- Complexity remains below the same 62-threat cap.
- Added explicit warning time to Sound pulses, Sight rays/lasers, Touch banks,
  Chemesthesis mines, and true Phantasia petals.
- Tests verify second-act patterns are mechanically denser, capped, and
  telegraphed without another health/damage multiplier.

## Iteration 4 — Make mechanics readable before they hurt

### Review

- The Guardian's shared block body communicated phase color but not attack
  anticipation, survival countdown, arena rule, or death state.
- Treasure mini-guardians had no local health bar because they are not the
  floor's `ActiveBoss`.
- The global boss HUD exposed an internal key rather than the authored
  Guardian identity.

### Ranked suggestions

1. **P0 — Add pre-attack anticipation and trial countdown language.**
2. **P0 — Give each sense a recognizable body silhouette.**
3. **P1 — Present authored names and trial state in the boss HUD.**
4. **P1 — Add a local mini-guardian health bar.**
5. **P1 — Add protected death choreography and field cleanup.**

### Implemented

- Added anticipation rings, a rotating trial boundary, countdown arc, phase
  heading/flavor, transition shield treatment, and trial-colored HUD progress.
- Added speaker arcs, pressure hardware, lens fins, carrier pods, and orbiting
  prisms to the five Guardian silhouettes.
- The HUD now names each Guardian and labels its intermission as `TRIAL`.
- Treasure mini-guardians render a local health bar.
- Lethal damage starts a protected disassembly beat and clears the boss field.

## Iteration 5 — Remove lifecycle friction and prove the complete loop

### Review

- If burst damage reached a health floor before the second declaration, the
  player still had to land an extra hit after the required lesson completed.
- Immediate cleanup on the declaration frame would erase the demonstrated
  pattern too quickly.
- Debug/QA controls did not expose Guardian phases or trials directly.

### Ranked suggestions

1. **P0 — Auto-resolve a satisfied health gate without requiring another hit.**
2. **P0 — Preserve a short readability window before transition cleanup.**
3. **P1 — Expose phase/trial debug controls for repeatable QA.**
4. **P1 — Verify death, trial, threat, telegraph, and roster invariants.**
5. **P2 — Record remaining arena/content work without expanding this pass.**

### Implemented

- A previously reached health gate now schedules its transition as soon as the
  second committed declaration lands.
- A `0.68s` declaration window lets that pattern read before cleanup.
- Guardian debug controls support phases 1–3, phase reset, and direct trial
  entry.
- Automated coverage now exercises identity, profiles, health floors,
  declaration gates, survival trials, safe rules, second-act complexity,
  telegraphs, threat caps, anticipation, death choreography, and authored
  midpoint/finale tiers.

## Final backlog implementation pass

The five previously ranked items are now implemented:

1. Boss floors select between two collision-space obstacle arrangements for
   each sense. Every arrangement keeps the centered 9x9 spawn, three-tile
   cardinal routes, open perimeter, connected playable body, and at least four
   separated two-player-safe footprints.
2. Completed and failed encounters persist aggregate clear time, phase
   durations, damage taken, skipped optional-room count/threat, carried enemy
   pressure, floor, sense, and controller use. Only the latest 50 encounters
   are retained.
3. A procedural bit-audio bus supplies distinct declaration, trial, stagger,
   and death cues. Each sense uses its own pitch center; headless or missing
   audio hardware safely falls silent without affecting simulation.
4. Every Guardian sense has a bounded rare alternate in all three phases.
   Rare patterns retain the same 62-threat admission cap and longer
   consequence-appropriate telegraphs.
5. Controller B now activates boss and next-floor portals and swaps the nearby
   prompt. Generated boss rooms are validated across senses and seeds for
   analog-friendly cardinal lanes, connected movement, and multiple separated
   safe pockets suitable for a future second local player.
