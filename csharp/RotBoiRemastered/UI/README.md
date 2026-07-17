# UI

HUD, menus, and shared drawing/theme helpers. Planned mapping:

- `UiTheme.cs` <- `uiTheme.py` (colors, draw_text/draw_button/draw_panel primitives,
  display_scale). **Done.** Nearly every other UI file calls into it.
- `InformationSheet.cs` <- `informationSheet.py` (sidebar HUD, equipment panel, loot panel)
- `Menus.cs` <- `menus.py` (pause/results screens, keybind rebind UI)
- `LevelingHandler.cs` <- `levelingHandler.py` (upgrade card screen)
- `StatCards.cs` <- `statCards.py`
- `ItemCards.cs` <- `itemCards.py`. **Done** -- procedural slot-type icons
  and rarity-backed mini card chrome. Known difference: pygame's
  `border_radius` (rounded corners) has no `Primitives2D` equivalent yet, so
  the card and armor-icon corners render sharp instead of rounded.
- Bars (`HpBar.cs`, `LevelBar.cs`, `DashBar.cs`) <- `hpBar.py`, `levelBar.py`, `dashBar.py`
