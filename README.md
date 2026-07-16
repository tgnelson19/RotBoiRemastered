# RotBoi Remastered

A real-time strategy deckbuilding rogue-lite prototype built with Python and
Pygame. Move through the arena, aim with the mouse, collect experience, and draft
upgrade cards that shape each run into a focused build.

## Controls

- `WASD`: move
- Mouse / left click: aim and fire
- `Space`: dash (briefly avoids contact damage)
- `I`: toggle autofire
- `1`, `2`, `3` or click: choose an upgrade card
- `R`: reroll the current card offer
- `B`: clear the arena and summon Beaudis for boss debugging
- `I`: toggle player damage during the Beaudis debug encounter
- `Escape`: quit

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
