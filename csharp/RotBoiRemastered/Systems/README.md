# Systems

Rules and data with no rendering dependency -- the most straightforward files to
port first since they were deliberately kept pygame-free in the Python original.

- `Upgrades.cs` <- `upgrades.py` (frozen dataclasses -> C# records; keep the same
  weighted-rarity-roll shape, including the injectable RNG for test determinism)
- `Items.cs` <- `items.py`
- `Keybinds.cs` <- `keybinds.py` (action -> key map, persisted like GameProfile)
- `GameProfile.cs` <- `gameProfile.py` (swap JSON-on-disk for the same shape;
  consider `System.Text.Json` + a settings folder under `%AppData%`)
- `StatTrack.cs` <- the shape shared by characterStats.py's
  `collectiveStats`/`collectiveAddStats`/`collectiveMultStats` dicts. **Done**
  -- one object (base value + additive stack + multiplicative stack) per
  upgrade stat, replacing three parallel dicts that always moved in lockstep.
- `RunState.cs` <- `characterStats.py`. **Done** -- level/exp/time
  progression, derived combat stats (via `Stats: Dictionary<string,
  StatTrack>`), entity holsters, equipment, `DreamState`/`BossAfflictions`
  (module dicts + free functions -> instance classes + methods, same
  cleanup as every other stateful module in this port), upgrade-collection
  tracking, `Reset()`. Answers the "one god-object or split up?" question
  above: kept as one class (it's genuinely one bounded context -- the
  current run's state), but internally organized into clearly-scoped
  properties and the nested `DreamState`/`BossAfflictions` helper classes
  rather than ~80 flat fields.
- `GameSession.cs` <- `character.py`'s "handling*"/"update*"/"draw*" free
  functions + `resetAllStats()`/`combarinoPlayerStats()`/
  `handleLevelingProcess()`. **Done for the non-boss gameplay loop**: bullet
  firing/movement, non-boss enemy spawning (via `Entities/EnemyCatalog.cs`
  directly, not gamePaths.py's per-path wrapper) and update/draw, bullet-
  enemy collision and death handling, XP pickup and leveling handoff, loot
  crate pickup, player damage-taking. One session object owns the player,
  run state, battleground, camera, and leveling screen, and orchestrates
  them each frame -- see its doc comment for the full, explicit list of
  deferred boss-specific and HUD-dependent branches (nothing was silently
  dropped). Every combined Python update-and-draw function was split into
  separate Update/Draw methods here, same reasoning as the rest of this
  port's entities: Python interleaved them purely to share one loop, and
  drawing order was never semantically significant, so splitting makes the
  spawn/collision/pressure-budget logic unit testable without a
  GraphicsDevice.
