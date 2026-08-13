# RotBoi Remastered

A 2D arena roguelite built with C#, MonoGame, and .NET 9. Move through the arena,
aim with the mouse, collect experience, and draft upgrade cards that shape each
run into a focused build. The red edge-of-screen
bounty arrow points toward the highest-value living patrol or elite target.
Normal standalone arena paths target a complete 25-35 minute run; the campaign,
Dungeon, challenges, and NG+ provide longer-term mastery rather than prerequisites.

## Controls

- `WASD`: move
- Mouse / left click: aim and fire
- `Space`: dash (briefly avoids contact damage)
- Hold `Q` / `E`: smoothly rotate the arena clockwise / counter-clockwise
- `X`: reset camera rotation and zoom to the resolution-aware default
- `O` / `P` or mouse wheel: zoom the world camera out / in around the player (also available in The Mind)
- `I`: toggle autofire
- `Tab`: open or close the paused run dossier
- `1`, `2`, `3` or click: choose an upgrade card
- `R`: reroll the current card offer
- `A` / `D`, arrows, controller stick/D-pad, or click: select an unlocked NG+ tier while confirming a Mind arena/Dungeon portal
- `B`: hidden debug shortcut that clears the arena and summons the selected path's final boss
- `Y`: toggle player invincibility during boss practice
- `Space` / `F` / `Enter`: enter The Mind from the title screen
- `F` near a station in The Mind: open its vault, quest, or skill menu
- `F` at a challenge brazier in The Mind: toggle No Healing or No Extract
- `R` after the midpoint boss, or **Extract** while paused: bank the run and open its debrief
- `Escape`: pause during a run or in The Mind; quit from the title screen
- Click the glowing gold sidebar button when stored EXP is sufficient to buy a level; choose `REFORGE` to spend 5 collected Fragments on an equipped item's grade or modifier
- `F11`: switch between windowed and borderless fullscreen
- Controller: left stick moves, right stick aims/fires, `A` dashes/confirms, `B` interacts/backs out, `X` toggles autofire, View opens the dossier, and Start pauses

## Comfort and accessibility

The pause menu includes a casual assist, persistent autofire, contextual hints,
an aim guide, damage-number control, high-contrast hostile outlines, adjustable
screen shake, a 0-100% optional visual-effects intensity control, and independent
text, GUI, damage-text, and default-camera-zoom sizing. Full visual spectacle is
the default; reducing the effects control removes optional ambience, trails, and
debris without weakening attack telegraphs, shadows, or hit feedback.
World zoom starts from a resolution-aware baseline so high-resolution displays retain
the intended character and arena readability. Casual assist reduces incoming damage and hostile projectile speed
without reducing enemy variety. Preferences and best-run records are saved in the
per-user application-data folder (`%APPDATA%\RotBoiRemastered\profile.json` on Windows).

For visual development, the in-game console command
`/vfxgallery [0-100] [path] [easy|medium|hard]` places every player and
hostile projectile silhouette plus the selected Path-native enemy family
gallery around the player. Its overlay also previews the shared room-role
glyphs and the adaptive ambience, trail, and debris budgets.

The combat footer keeps health, dash, equipment, Fragments, selected stats, and the
level-up action visible without shrinking the arena. Press `Tab` for the paused run
dossier with exact weapon outcomes, build-family detail, objectives, loadout, and
recent cards. Fractional projectiles and pierce use plain-language bonus chances;
upgrade symbols and rarity colors remain consistent between drafting and the dossier.

## The Mind and permanent progression

Choose **Enter Mind** from the title screen to visit an authored sanctuary with
three distinct beats: a quiet southern chapel lined with purpose-built utility
shrines, a short passage where masonry dissolves into braided soul currents, and
a five-branch crown surrounding the always-open Dungeon portal. The Vault
Reliquary keeps ten permanent item slots and statistics for the ten
most recent extracted runs. The DPS effigy shows hit numbers, current rolling DPS,
session best, and the all-time record. The Vow Lectern's 24 objectives award Mind
Tokens; the Mind Rose spends those tokens on twelve simple, rankable permanent
upgrades. The Vestment Mirror offers persistent player Core and Edge colors plus
two-tone projectile palettes and projectile silhouettes.

Five compact walled branches lead to immediately available Sound, Touch, Sight,
Chemesthesis, and Phantasia arenas with their own procedural silhouettes and
ambient vocabulary. These are the primary standalone runs: completing an arena
awards its silver statue, a Mind Token, mastery, and the next NG+ tier. Cleared
paths wake permanent architectural details, additional mastery enriches them to
a bounded cap, and selected NG+ tiers add corruption seams and square motes. Optional
chapel dust, tunnel motes, and secondary branch effects follow the visual-effects
intensity setting without hiding portal silhouettes or interaction prompts.

Collecting all five silver statues opens the northern Body/Soul campaign gate.
Completing The Body continues immediately into The Soul as the second half of
the same expedition. The player keeps their current level, temporary build,
health, equipment, inventory, challenge flags, and elapsed time; The Soul has no
standalone entrance in The Mind. Each completed Soul finale awards its sense's
gold statue. Collecting all five gold statues opens Aphantasia beyond the next
northern wall.

After defeating a path's midpoint boss, the pause menu and `R` offer extraction;
the ten-floor Dungeon unlocks extraction after floor five, and expeditions unlock it
after their first guardian. Completing a path banks it automatically. The debrief
shows field/boss time, build, rewards, unlocks, and retained or lost gear; the Mind
Vault moves surviving equipment into permanent storage. Selecting a stored
item prepares that copy for the next run; it leaves storage when the run begins.
Dying destroys carried items. A confirmed restart retains the current loadout;
returning to the title leaves behind changes made since the last extraction,
completion, or restart sync. Mind-grid bonuses, vaulted equipment, and other
permanent progress remain intact. Hover equipment or nearby loot
for its symbolic stat card, rarity-scaled tradeoffs, status effects, and flavor text.

Enemies have a one-in-three chance to leave a gold Fragment pickup. Fragments
follow the same collection aura as EXP but use a separate run counter; every
grade increase and modifier reroll costs exactly five, leaving stored EXP solely
for purchasing levels.

The northern Mind station toggles Hard Mode for future runs. Hard Mode disables
all healing except the full heal granted when EXP is spent on a level, and path
completion awards two Mind Tokens instead of one. Epic and Legendary drops can
become path-bound Core-Forged items at 10% and 20% respectively; Mythical drops
use a 35% chance, while named Unique items remain unforgeable. Core-Forged gear
adds a fixed path-specific stat package, glows in inventories and loot crates,
and contributes a matching concentric aura while equipped.

Every Mind arena portal also has a per-path New Game Plus selector. Completing
the normal path unlocks NG+1 for that path; completing each tier unlocks only
the next, through NG+7. Each tier multiplies enemy health and incoming enemy
damage by 1.5, doubles the direct path-clear Mind Token reward, and shifts item
rarity and F-S grade rolls upward. Core-Forged chances also rise at every NG+
tier. Hard Mode remains an independent toggle and its reward multiplier stacks
with NG+.

## The Dungeon

The central Dungeon portal is available from the first visit to The Mind. It is
a ten-floor free-play run through every sense and does not award campaign
statues, path mastery, clear tokens, or new NG+ unlocks; retained equipment,
run history, and relevant quest counters still follow the normal extraction and
completion rules. Floors
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
discovery rules. World fog is suspended inside Grand Arenas and boss rooms so
their combat remains smooth and fully readable; map discovery remains intact.

The Options screen exposes a persisted 30-360 FPS cap in five-FPS increments
and a Vertical Sync toggle. The cap controls the fixed game cadence, while
VSync synchronizes presentation to the display when supported by the graphics
driver.

Ordinary enemies use a substantially lower equipment-drop table in this mode;
forge Fragments and experience still work normally. Treasure encounters do not
roll ordinary enemy equipment at all. Their cleared rooms instead contain
large guaranteed chests with at least two rolled items, making the optional
dangerous branches the reliable source of equipment during a composite run.

The six-pass dungeon review, completed work, and ranked future backlog are
tracked in [`docs/path-dungeon-iterations.md`](docs/path-dungeon-iterations.md).
The five-pass Malady/Dissonance-based boss review and its completed final
implementation pass are tracked separately in
[`docs/path-boss-iterations.md`](docs/path-boss-iterations.md).

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
