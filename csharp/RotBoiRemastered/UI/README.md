# UI

HUD, menus, and shared drawing/theme helpers. Planned mapping:

- `UiTheme.cs` <- `uiTheme.py` (colors, draw_text/draw_button/draw_panel primitives,
  display_scale). **Done.** Nearly every other UI file calls into it.
- `InformationSheet.cs` <- `informationSheet.py` (sidebar HUD, equipment panel, loot panel)
- `Menus.cs` <- `menus.py` (pause/results screens, keybind rebind UI)
- `LevelingHandler.cs` <- `levelingHandler.py` (upgrade card screen)
- `StatCards.cs` / `ItemCards.cs` <- `statCards.py` / `itemCards.py`
- Bars (`HpBar.cs`, `LevelBar.cs`, `DashBar.cs`) <- `hpBar.py`, `levelBar.py`, `dashBar.py`
