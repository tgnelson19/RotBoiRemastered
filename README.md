# RotBoi Remastered

A real-time strategy deckbuilding rogue-lite prototype built with Python and
Pygame. Move through the arena, aim with the mouse, collect experience, and draft
upgrade cards that shape each run into a focused build. The red edge-of-screen
bounty arrow points toward the highest-value living patrol or elite target.

## Controls

- `WASD`: move
- Mouse / left click: aim and fire
- `Space`: dash (briefly avoids contact damage)
- Hold `Q` / `E`: smoothly rotate the arena clockwise / counter-clockwise
- `I`: toggle autofire
- `Tab`: toggle compact/detailed run information
- `1`, `2`, `3` or click: choose an upgrade card
- `R`: reroll the current card offer
- `A` / `D`, arrows, or click: select a content path on the title screen
- `B`: hidden debug shortcut that clears the arena and summons the selected path's final boss
- `Y`: toggle player invincibility during boss practice
- `Escape`: pause during a run; quit from the title screen
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

## Run locally

Requires Python 3.11+ and Pygame 2.5+.

```powershell
python -m pip install -r requirements.txt
python main.py
```

Run the tests with:

```powershell
python -m unittest discover -s tests -v
```

## Design direction

Cards are grouped into build families such as Volley, Critical, Harvest, and
Survival. Drafts remain varied, but become modestly more likely to support the
synergies already collected during a run. Gameplay rules for the draft live in
`upgrades.py`; presentation remains in `levelingHandler.py`.

Runs begin on one of five isolated content paths. Sound contains the original
arena, Beaudis, and Dissonance. Touch uses a dense sewer-prison, heavy Rotton
enemies, Bair, and Sting. Sight is an open blue-orange field of small quick
hunter enemies led by Ishe and Chronos. Chemesthesis scatters ruin fragments and
long-lived, mostly unaimed hazards around durable enemies, Kage, and Rot.
Phantasia uses broad dark-pink dream courts with a few ornate structures, Hypno,
and Malady.

The path boundary lives in `gamePaths.py`. Shared enemy profiles, projectile
rules, boss rosters, and path-exclusive encounter registration belong there so
new content does not branch the leveling, statistics, or HUD code.
