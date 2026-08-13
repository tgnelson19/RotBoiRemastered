# UI

HUD, menus, and shared drawing/theme helpers. Mapping from the Python source:

- `UiTheme.cs` <- `uiTheme.py` (colors, draw_text/draw_button/draw_panel primitives,
  display_scale). **Done.** Nearly every other UI file calls into it.
- `StatCards.cs` <- `statCards.py`. **Done** -- per-upgrade-stat procedural
  icons (~15 branches) and rarity-backed mini card chrome, same shape as
  `ItemCards.cs`.
- `ItemCards.cs` <- `itemCards.py`. **Done** -- procedural slot-type icons
  and rarity-backed mini card chrome. Core-Forged cards retain rarity fill
  while adding a pulsing path-colored outline and badge. Known difference: pygame's
  `border_radius` (rounded corners) has no `Primitives2D` equivalent yet, so
  the card and armor-icon corners render sharp instead of rounded (also
  true of `StatCards.cs`'s card chrome).
- `LevelingHandler.cs` <- `levelingHandler.py`. **Done** -- upgrade-card
  draft screen, reroll button, stat-preview/recommendation logic.
- `ReforgeHandler.cs`. **Done** -- full-screen equipped-item selection with
  flat five-Fragment grade upgrades and modifier rerolls; rarity and Core
  Forge are read-only and remain attached through either operation. Stored EXP
  is reserved for level purchases and is never consumed here.
- `SoulHub.cs` includes the northern Hard Mode station. Its persisted toggle
  controls healing, completion rewards, and path-matched Core-Forged drops.
  Each arena portal's confirmation panel also provides mouse or A/D/arrow NG+
  selection, shows enemy and reward multipliers, and disables tiers that have
  not yet been unlocked by clearing the preceding tier on that path. The
  rebuilt convergence dais now holds the always-open five-color Dungeon portal.
  Completing all five standalone arenas raises their silver statues and opens
  the northern Body/Soul campaign gate. Body completion flows directly into the
  hostile Soul expedition with the live build intact; five Soul-finale gold
  statues open the separately sealed Aphantasia chamber.
- `Menus.cs` <- `menus.py`. **Done** -- pause screen (gameplay/options/
  keybinds tabs, rebind capture) and results screen.
- `InformationSheet.cs` <- `informationSheet.py` (sidebar HUD, equipment
  panel, loot panel, build identity, weapon stats, objective/bounty panel,
  recent-picks table, tooltip). **Done** -- see "InformationSheet.cs" below
  for design notes.
- `TitleScreen.cs` <- `character.py`'s `runTheTitleScreen()`. **Done** --
  ROTBOI header, one focused Mind-entry button, the static Field Manual
  control-legend panel, and the best-run tag. Individual and randomized run
  selection lives at physical Mind portals rather than on the main menu.
  Follows
  `Menus.cs`'s `Draw`/`HandleInput` shape (a `TitleAction` enum --
  `None`/`EnterSoul`/`Settings`/`Quit` -- instead of mutating state directly) rather
  than folding into `Core/RotBoiGame.cs`, matching every other screen.
  Dropped vs. Python: the best-run tag reads `GameProfile.Profile.BestLevel`/
  `BestKills` directly instead of `max(cS.highestLevel, profile["best_level"])`
  -- `GameProfile.RecordRun` already updates `BestLevel` synchronously on
  every defeat/completion, so the profile value is never stale by the time
  this screen is shown again.
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
  `LevelUpStatSnapshot` carries the leveling projection fields, while the
  immutable `RunResultReport` captures the complete debrief and exact reward
  deltas once at finalization. Same pattern as `Entities/EnemyUpdateContext`.
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
- The settings-first pause overlay uses a fixed category rail, one scrollable
  page, and a separate session-action rail. Display changes apply immediately;
  destructive session actions require confirmation.
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

## FooterHud.cs and InformationSheet.cs

- Takes `Systems.RunState` directly (see Systems/README.md) rather than a
  purpose-built snapshot type like `LevelUpStatSnapshot`/`RunResultReport`
  -- it reads nearly RunState's entire surface area (equipment, loot crates,
  combat stats, encounter pressure, upgrade history...), so a snapshot would
  just duplicate every field. This was the reason the file was deferred in
  the first place (paired with "how does Player.cs's data model work");
  now that `RunState`/`GameSession` exist, that pairing is resolved.
- `FooterHud` is the only persistent run HUD: health/dash, five read-only
  equipment slots, fragments, three normalized profile-selected stats, and
  the full XP/level action. It overlays the full combat viewport and exposes
  a footer-safe rectangle for other combat overlays.
- `InformationSheet` owns the paused, scrollable dossier and shared loadout
  transfer rules. The same equipment/stash interaction is reused beside the
  Soul Vault, including keyboard/controller confirm-to-pick/place and Back
  cancellation.
- **`DragSource` record hierarchy** (`EquipmentDragSource`/`CrateDragSource`)
  replaces Python's tagged tuple (`("equipment", key)` /
  `("crate", crate, index)`).
- **Camera re-centering is full-screen.** Construction, reset, resize,
  fullscreen changes, and Path-floor installation all lock against the full
  viewport. No sidebar-width dependency remains.
- The old `HudMode`, fixed sidebar, and configurable live Tab sections are
  gone. Tab/View opens the paused dossier; cosmetics remain in the Soul.
- **No implicit per-frame `_sync_layout()` self-check** -- `SyncLayout` is
  called explicitly from `GameSession.Resize`, matching
  `LevelingHandler.UpdateLayout`'s existing contract instead of re-deriving
  screen size from a hidden global every frame.
- **Pure derived-value helpers are `public static`** (`Rating`, `ShotText`,
  `PierceText`, `Pressure`, `BuildIdentity`, `FamilyCounts`,
  `BountyDetails`, `NextMilestone`), same reasoning as
  `LevelingHandler.ProjectedValue`/`Recommendation` -- unit testable
  without a `GraphicsDevice`. The rect-hit-testing drag paths still need a
  prior `Draw` call against a real `GraphicsDevice` to populate
  `_equipmentSlotRects`/`_lootPanelSlotRects`, so those are left to visual
  smoke testing, same as `Menus.cs`/`LevelingHandler.cs`'s mouse-click paths.
- **Bounty tracking** (`cS.currentBounty`/`selectBountyTarget()`) becomes
  `GameSession.SelectBountyTarget()` (a `Systems.BountyInfo` record) --
  it only reads `RunState`'s enemy/boss data, no HUD concern, so it lives
  on `GameSession` and is passed into `DrawSheet` explicitly rather than
  `InformationSheet` reaching into `GameSession` itself.
- **`updateCurrLevel()` is dropped.** Its Python body was `return None` (a
  no-op stub `character.py` called once per frame for no effect,
  confirmed by reading informationSheet.py:513-514) -- nothing is lost by
  omitting it.
- `getattr(enemy, "storedExperience", 0)`/`getattr(enemy, "bossName", ...)`
  in `selectBountyTarget()` are dropped too -- no current `Enemy` type sets
  either (both were always their Python default), so
  `GameSession.SelectBountyTarget` reads `ExpValue`/`Family` directly.
- Known rendering gap shared with `ItemCards.cs`/`StatCards.cs`: pygame's
  `border_radius` (rounded corners) has no `Primitives2D` equivalent yet,
  so equipment/loot slot boxes render sharp-cornered instead of rounded.

## Still not in UI/ (HUD-overlay functions layered on top of the sheet)

`character.py` has several more HUD functions that draw *alongside*
`informationSheet.py`'s sheet, not inside it. The bounty-arrow indicator
(`drawBountyIndicator`'s polygon-on-the-viewport-edge rendering) and the
title screen are now both done (see `Systems/GameSession.DrawBountyIndicator`
and `TitleScreen.cs` above -- the indicator lives on `GameSession` rather
than here since it's a thin wrapper around `SelectBountyTarget`, not a panel
of the sheet itself). Still deferred: the boss health bar, tutorial hints,
the low-health warning, and the run-complete banner -- polish overlays, not
required for the game loop to run, left for their own pass.
