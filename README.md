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
- `X`: reset camera rotation and zoom to the resolution-aware default
- `O` / `P` or mouse wheel: zoom the world camera out / in around the player (also available in the Soul)
- `I`: toggle autofire
- `Tab`: toggle compact/detailed run information
- `1`, `2`, `3` or click: choose an upgrade card
- `R`: reroll the current card offer
- `A` / `D`, arrows, or click: select an unlocked NG+ tier while confirming a Soul path portal
- `B`: hidden debug shortcut that clears the arena and summons the selected path's final boss
- `Y`: toggle player invincibility during boss practice
- `Space` / `F` / `Enter`: enter the Soul from the title screen
- `F` near a station in the Soul: open its extraction, quest, or skill menu
- `F` at the northern box in the Soul: toggle Hard Mode
- `X` while paused after the midpoint boss: extract the current run and equipment
- `Escape`: pause during a run or in the Soul; quit from the title screen
- Click the glowing gold sidebar button when stored EXP is sufficient to buy a level; choose `REFORGE` to spend 5 collected Fragments on an equipped item's grade or modifier
- `F11`: switch between windowed and borderless fullscreen
- Controller: left stick moves, right stick aims/fires, `A` dashes, `X` toggles autofire, and Start pauses

## Comfort and accessibility

The pause menu includes a casual assist, persistent autofire, contextual hints,
an aim guide, damage-number control, high-contrast hostile outlines, adjustable
screen shake, and independent text, GUI, damage-text, and default-camera-zoom sizing.
World zoom starts from a resolution-aware baseline so high-resolution displays retain
the intended character and arena readability. Casual assist reduces incoming damage and hostile projectile speed
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

Choose **Enter Soul** from the title screen to visit an authored sanctuary with
three distinct beats: a compact southern holdout for every permanent system, a
pulsing northbound ribbon tunnel, and a broad portal chamber where each path's
color and visual motifs bleed into the neutral Soul architecture. Its extraction
chest keeps ten permanent item slots and statistics for the ten
most recent extracted runs. The DPS effigy shows hit numbers, current rolling DPS,
session best, and the all-time record. A 24-tile quest grid awards Soul tokens;
the matching Soul grid spends those tokens on twelve simple, rankable permanent
upgrades. The physical wardrobe station offers persistent player Core and Edge
colors plus two-tone projectile palettes and projectile silhouettes.

The tunnel's pixel ribbons awaken in front of the player instead of running at
full strength from the start. Cleared paths grow block-built reliquaries and
floor scars beneath their portals, selected NG+ tiers increase the radius and
density of square corruption motes, and portals occasionally exchange packets
of reflected pixels. These effects use the same hard-edged primitive vocabulary
as combat rather than introducing a smoother visual-effects style.

After defeating a path's midpoint boss, the pause menu offers an extraction choice;
completing a path extracts automatically. The chest keeps the run summary and lets
the player salvage surviving equipment into permanent storage. Selecting a stored
item prepares that copy for the next run; it leaves storage when the run begins.
Dying, restarting, or abandoning the run destroys carried items, while Soul-grid
bonuses and other permanent progress remain intact. Hover equipment or nearby loot
for its symbolic stat card, rarity-scaled tradeoffs, status effects, and flavor text.

Enemies have a one-in-three chance to leave a gold Fragment pickup. Fragments
follow the same collection aura as EXP but use a separate run counter; every
grade increase and modifier reroll costs exactly five, leaving stored EXP solely
for purchasing levels.

The northern Soul station toggles Hard Mode for future runs. Hard Mode disables
all healing except the full heal granted when EXP is spent on a level, and path
completion awards two Soul tokens instead of one. Epic and Legendary drops can
become path-bound Core-Forged items at 10% and 20% respectively; Mythical drops
use a 35% chance, while named Unique items remain unforgeable. Core-Forged gear
adds a fixed path-specific stat package, glows in inventories and loot crates,
and contributes a matching concentric aura while equipped.

Every Soul path portal also has a per-path New Game Plus selector. Completing
the normal path unlocks NG+1 for that path; completing each tier unlocks only
the next, through NG+7. Each tier multiplies enemy health and incoming enemy
damage by 1.5, doubles the direct path-clear Soul-token reward, and shifts item
rarity and F-S grade rolls upward. Core-Forged chances also rise at every NG+
tier. Hard Mode remains an independent toggle and its reward multiplier stacks
with NG+.

## Composite Path mode

Enter the Soul and use the large convergence portal where the five sense
paths diverge to begin a ten-floor dungeon run through every sense. Floors
1-5 use one shuffled copy of Sound, Touch,
Sight, Chemesthesis, and Phantasia; floors 6-10 reshuffle all five and apply a
much harder second-act curve. The fifth floor ends with its sense's midpoint
boss, and the tenth ends with its sense's final boss. Every other floor ends
with a three-phase guardian that uses the current sense's projectile language.

Each floor selects a Switchback, Grand Circuit, Procession, or Floodplain
macro-layout. A protected start leads through varied long halls, grand arenas,
mazes, crossroads, rings, diamonds, and ruins before the centered boss arena.
Branches lead to treasure rooms and optional champion Challenges with enhanced
rewards. Treasure branches use chained 50% rolls, so a floor can contain zero
to three; each branch seals its chest behind either a mini-guardian or a
guardian-strength horde. Combat thresholds activate independently and never
lock player movement. Rushing can therefore accumulate pursuing encounters,
including inside the boss arena, while forfeiting skipped experience and
rewards. Boss defeat opens an interactable portal to the next generated floor.
The run sidebar shows the floor, current sense, act, and elapsed run time;
temporary banners identify both the sense and each new room.

Path floors use persistent line-of-sight fog: unexplored tiles and enemies are
hidden, explored areas remain mapped but darken when out of sight, and edging
past a doorway reveals space around the corner from the player's actual
position. Sight has no arbitrary distance cutoff, so an unobstructed hallway
remains readable to its far end. Visible floor also keeps its bordering wall
faces lit, and convex wall corners inherit visibility from a visible adjoining
wall to avoid harsh single-tile flicker. The compact floor map follows the same
discovery rules.

Ordinary enemies use a substantially lower equipment-drop table in this mode;
forge Fragments and experience still work normally. Treasure encounters do not
roll ordinary enemy equipment at all. Their cleared rooms instead contain
large guaranteed chests with at least two rolled items, making the optional
dangerous branches the reliable source of equipment during a composite run.

The six-pass dungeon review, completed work, and ranked future backlog are
tracked in [`docs/PATH_DUNGEON_ITERATIONS.md`](docs/PATH_DUNGEON_ITERATIONS.md).
The five-pass Malady/Dissonance-based boss review and its completed final
implementation pass are tracked separately in
[`docs/PATH_BOSS_ITERATIONS.md`](docs/PATH_BOSS_ITERATIONS.md).

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
