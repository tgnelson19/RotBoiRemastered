# RotBoi Remastered

A 2D arena roguelite built with C#, MonoGame, and .NET 9. Move through the arena,
aim with the mouse, collect experience, and draft upgrade cards that shape each
run into a focused build. The red edge-of-screen
bounty arrow points toward the highest-value living patrol or elite target.

## Controls

- `WASD`: move
- Mouse / left click: aim and fire
- `Space`: dash (briefly avoids contact damage)
- Hold `Q` / `E`: smoothly rotate the arena clockwise / counter-clockwise
- `X`: reset the camera to 0 degrees during play
- `I`: toggle autofire
- `Tab`: toggle compact/detailed run information
- `1`, `2`, `3` or click: choose an upgrade card
- `R`: reroll the current card offer
- `A` / `D`, arrows, or click: select a content path on the title screen
- `B`: hidden debug shortcut that clears the arena and summons the selected path's final boss
- `Y`: toggle player invincibility during boss practice
- `F`: enter the Soul from the title screen
- `F` near a station in the Soul: open its extraction, quest, or skill menu
- `X` while paused after the midpoint boss: extract the current run and equipment
- `Escape`: pause during a run or in the Soul; quit from the title screen
- `F11`: switch between windowed and borderless fullscreen
- Controller: left stick moves, right stick aims/fires, `A` dashes, `X` toggles autofire, and Start pauses

## Comfort and accessibility

The pause menu includes a casual assist, persistent autofire, contextual hints,
an aim guide, damage-number control, high-contrast hostile outlines, and adjustable
screen shake. Casual assist reduces incoming damage and hostile projectile speed
without reducing enemy variety. Preferences and best-run records are saved locally
in `data/profile.json`.

The in-run information sidebar starts in a compact, action-focused mode. Press
`Tab` for additional weapon outcomes and build-family detail. Damage and fire rate
retain exact values; fractional projectiles and pierce are translated into plain
language such as “5 shots + 35% bonus.” Each upgrade has a stat symbol, a `+` corner
mark for flat bonuses or an `x` mark for multiplicative bonuses, and its rarity
color. The five most recent cards collect on the small table at the bottom of the
sidebar; hover one when you want its name and bonus type.

## The Soul and permanent progression

Choose **Enter Soul** from the title screen to visit a small walkable sanctuary.
Its extraction chest keeps ten permanent item slots and statistics for the ten
most recent extracted runs. The DPS effigy shows hit numbers, current rolling DPS,
session best, and the all-time record. A 24-tile quest grid awards Soul tokens;
the matching Soul grid spends those tokens on twelve simple, rankable permanent
upgrades. The physical wardrobe station offers persistent player Core and Edge
colors plus two-tone projectile palettes and projectile silhouettes.

After defeating a path's midpoint boss, the pause menu offers an extraction choice;
completing a path extracts automatically. The chest keeps the run summary and lets
the player salvage surviving equipment into permanent storage. Selecting a stored
item prepares that copy for the next run; it leaves storage when the run begins.
Dying, restarting, or abandoning the run destroys carried items, while Soul-grid
bonuses and other permanent progress remain intact. Hover equipment or nearby loot
for its symbolic stat card, rarity-scaled tradeoffs, status effects, and flavor text.

## Run locally

Requires the .NET 9 SDK. Restore the pinned MonoGame content tool once, then run:

```powershell
dotnet tool restore
dotnet run --project RotBoiRemastered/RotBoiRemastered.csproj
```

Run the tests with:

```powershell
dotnet test RotBoiRemastered.Tests/RotBoiRemastered.Tests.csproj
```

## Design direction

Cards are grouped into build families such as Volley, Critical, Harvest, and
Survival. Drafts remain varied, but become modestly more likely to support the
synergies already collected during a run. Gameplay rules for the draft live in
`upgrades.py`; presentation remains in `levelingHandler.py`.

Runs begin on one of five isolated content paths. Sound contains the original
arena, Beaudis, and Dissonance. Touch uses a dense sewer-prison, heavy Rotton
enemies, Bair, and Rot. Sight is an open blue-orange field of small quick
hunter enemies led by Ishe and Chronos. Chemesthesis scatters ruin fragments and
long-lived, mostly unaimed hazards around durable enemies, Kage, and Ache.
Phantasia uses broad dark-pink dream courts with a few ornate structures, Hypno,
and Malady.

The path boundary lives in `gamePaths.py`. Shared enemy profiles, projectile
rules, boss rosters, and path-exclusive encounter registration belong there so
new content does not branch the leveling, statistics, or HUD code.
