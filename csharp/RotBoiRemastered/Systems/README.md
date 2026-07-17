# Systems

Rules and data with no rendering dependency -- the most straightforward files to
port first since they were deliberately kept pygame-free in the Python original.

- `Upgrades.cs` <- `upgrades.py` (frozen dataclasses -> C# records; keep the same
  weighted-rarity-roll shape, including the injectable RNG for test determinism)
- `Items.cs` <- `items.py`
- `Keybinds.cs` <- `keybinds.py` (action -> key map, persisted like GameProfile)
- `GameProfile.cs` <- `gameProfile.py` (swap JSON-on-disk for the same shape;
  consider `System.Text.Json` + a settings folder under `%AppData%`)
- `CharacterStats.cs` <- `characterStats.py` (the big pile of run-scoped mutable
  state -- consider whether this stays one god-object or gets split up during
  the port, unlike the direct 1:1 mapping used elsewhere in this skeleton)
