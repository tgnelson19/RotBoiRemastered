# UI

HUD, menus, and shared drawing/theme helpers. Mapping from the Python source:

- `UiTheme.cs` <- `uiTheme.py` (colors, draw_text/draw_button/draw_panel primitives,
  display_scale). **Done.** Nearly every other UI file calls into it.
- `StatCards.cs` <- `statCards.py`. **Done** -- per-upgrade-stat procedural
  icons (~15 branches) and rarity-backed mini card chrome, same shape as
  `ItemCards.cs`.
- `ItemCards.cs` <- `itemCards.py`. **Done** -- procedural slot-type icons
  and rarity-backed mini card chrome. Known difference: pygame's
  `border_radius` (rounded corners) has no `Primitives2D` equivalent yet, so
  the card and armor-icon corners render sharp instead of rounded (also
  true of `StatCards.cs`'s card chrome).
- `LevelingHandler.cs` <- `levelingHandler.py`. **Done** -- upgrade-card
  draft screen, reroll button, stat-preview/recommendation logic.
- `Menus.cs` <- `menus.py`. **Done** -- pause screen (gameplay/options/
  keybinds tabs, rebind capture) and results screen.
- `InformationSheet.cs` <- `informationSheet.py` (sidebar HUD, equipment
  panel, loot panel). **Deferred** -- see below.
- ~~Bars (`HpBar.cs`, `LevelBar.cs`, `DashBar.cs`)~~ -- **not ported.**
  `hpBar.py`/`levelBar.py`/`dashBar.py` are confirmed dead code: grepped the
  whole repo, nothing constructs `HPBar`/`LevelBar`/`DashBar` or imports
  these modules anywhere. `informationSheet.py`'s `_bar`/`ui.draw_progress`
  replaced them; porting dead code isn't worth it just because a file
  exists.

## Cleanup vs. the Python original

- **Explicit snapshot types replace `characterStats.py` reads.**
  `levelingHandler.py`'s `_projected_value`/`_recommendation` and
  `menus.py`'s `draw_results` read `cS.*` fields directly -- but
  `characterStats.py` isn't ported yet (see "Explicitly deferred" below), so
  `LevelUpStatSnapshot` and `RunResultsSnapshot` carry exactly the fields
  each screen needs, built by whatever eventually owns real run state. Same
  pattern as `Entities/EnemyUpdateContext`.
- **`MenuAction` enum replaces direct `vH.state = ...` assignment.**
  `handle_pause`/`handle_results` return what the caller should do
  (`Resume`/`Restart`/`ReturnToTitle`/`None`) instead of mutating a state
  global directly or calling `game.resetAllStats()` themselves -- the
  not-yet-built main-loop/state-machine owns the actual transition. Matches
  `LevelingHandler.PlayerClicked`'s existing return-a-result contract.
- **Module globals become instance state**, same cleanup as every other
  stateful module ported so far: `menus.py`'s `_buttons`/`_settings_tab`/
  `_rebinding_action` are fields on `Menus`; `levelingHandler.py`'s mutable
  attributes are fields on `LevelingHandler`.
- **Mouse position/state are explicit method parameters** on every
  `Menus`/`LevelingHandler` draw/input method (matching `UiTheme.DrawButton`'s
  existing shape) rather than reads of `vH.mouseX`/`mouseY`/`mouseDown`.
  `Core/InputState.cs` grew `MousePosition`/`MouseDown`/`MousePressed`
  fields as the one place the eventual game loop reads real input from
  before passing it down explicitly.
- **Dropped confirmed-dead fields/methods** (verified by grep across the
  whole repo, not just the one file): `levelingHandler.py`'s
  `titleFont`/`descFont`/`smallFont` (every draw call resolves its font
  fresh through `UiTheme.Font`), `cardHoverColor` (`DrawPanel`'s own
  `hovered` flag already picks `PanelHover`), `upgradeRarity`/
  `upgradeTypesList`/`upgradeBasicTypesAdd`/`upgradeBasicTypesMult`/
  `frameRate`, the `_draw_centered` method (defined, never called), and
  `_sync_legacy_fields` (a "keep old attribute names working while callers
  migrate" shim with nothing left in this codebase to migrate).

## Explicitly deferred (not in UI/ yet)

- **`InformationSheet.cs`** <- `informationSheet.py` (533 lines). Far more
  deeply coupled to `characterStats.py` than `LevelingHandler`/`Menus` --
  it reads dozens of `cS.*` fields across nearly the entire player/run
  state (equipment, loot crates, combat stats, encounter pressure, bounty
  tracking, build identity...). Snapshotting that much surface area
  cleanly is really the same design question as "how does Player.cs's data
  model work," so this is paired with that future pass rather than
  guessed at now.
