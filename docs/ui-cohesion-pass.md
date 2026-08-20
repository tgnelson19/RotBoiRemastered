# UI Cohesion Pass

A read-through of every menu, HUD, Mind-hub screen, options surface, and splash/title-card system in `RotBoiRemastered/UI`, `Presentation`, and the relevant `Systems`/`Entities` drawing code, looking specifically for places where the presentation drifts from a single shared visual language rather than for gameplay bugs.

## The good news first

The game already has a real design system, not just scattered draw calls. `UI/UiTheme.cs` is the single source of truth for palette, font resolution, panel chrome, and — importantly — the accessibility/display-scale pipeline (`DisplayScale`, `GuiScale`, `TextSize`). Nearly every screen (Title, Settings, Pause/Results, Reforge, the dossier, the HUD) draws its panels through `DrawPanel`/`DrawFramedPanel`/`DrawButton` and reads its colors from the same eleven named `Color` fields, so buttons, borders, and shadows already look like one family everywhere they're used correctly. `Presentation/VisualLanguage.cs` extends that further: boss banners, Soul stations, and the title screen's rose all pull the same per-Path accent colors, so the thread from "which Sense/Path you're in" to "what color things glow" is genuinely consistent from the title screen through to a boss fight.

The gaps below are narrower than "the UI feels inconsistent" — they're specific places where a screen quietly opted out of the shared system (a literal pixel size instead of a scaled one, a locally-invented color instead of a theme constant, a corner radius nobody agreed on). Every one of them is a small, mechanical fix because the shared system to fix them *into* already exists.

## Findings, ranked by how visible they are to a player

### 1. Three different "dim the screen behind a modal" treatments

The same visual job — darken the world so a confirmation box reads on top of it — is implemented three separate times with three different colors:

- `UI/TitleScreen.cs:114` (quit confirmation): `new Color(0, 0, 0, 190)`
- `UI/SettingsMenu.cs:65` (pause/settings backdrop): `new Color(3, 5, 8, 205)`
- `UI/SettingsMenu.cs:342` (destructive-action confirmation, e.g. "Restart this run?"): `new Color(0, 0, 0, 185)`

They're close enough that a player won't consciously clock the difference in isolation, but the settings backdrop has a visible blue-black tint the other two don't, and all three land within a 20-point alpha band of each other for no reason. Since this is purely a scrim with no other job, it's a good candidate for one shared helper.

**Fix:** add a single `UiTheme.DrawScrim(SpriteBatch, Rectangle)` (or a named `UiTheme.ScrimColor` constant) and point all three call sites at it.

### 2. "The Mind" hub mixes scaled and unscaled text sizes in the same view

`SoulHub.cs` (the class behind both `SoulHub` and `MindHub`) defines its own scaling helpers at the top: `Px(value)` and `Fs(value)`, both multiplying by `_uiScale = UiTheme.DisplayScale(...)`. Panel-based UI in the same file uses them correctly — e.g. the hub's own header, `"THE MIND"` at `Fs(27)` (`SoulHub.cs:1027`), or the portal confirmation modal at `Fs(22)`/`Fs(11)`/`Fs(9)` (`SoulHub.cs:1650-1667`).

But several world-space callouts in that *same file* skip `Fs()` and hardcode a raw pixel size instead:

- The training-dummy DPS readout: `"THE EFFIGY REMEMBERS"` at a bare `10`, the live DPS number at `34`, `"DAMAGE PER SECOND"` at `9`, the session/record line at `8` (`SoulHub.cs:558-561`).
- The Dungeon portal callout: `"THE DUNGEON"` at a bare `12`, its subtext at `8`, the interact prompt at `10` (`SoulHub.cs:1609-1620`).

These sit a few dozen pixels from properly-scaled panel text in the same screen, so a player who raises the GUI Scale or Text Size accessibility sliders will see the hub's panel headers grow while these particular in-world labels stay pinned at their base size — the mismatch gets *more* visible the more a player leans on those settings, which is exactly the audience it should be working hardest for.

**Fix:** route every `DrawText` call in `SoulHub.cs` through `Fs(...)`, no exceptions. A quick grep for `DrawText(spriteBatch, "` with a literal (non-`Fs`) number as the size argument will find the rest.

### 3. The level-up draft screen doesn't participate in "GUI Scale" at all

Every other full-screen system derives its layout from `UiTheme.DisplayScale(screenWidth, screenHeight)`, which folds in the player's `GuiScale` accessibility preference on top of resolution. `LevelingHandler.cs` — the three-card "choose your upgrade" screen, arguably the single most-seen screen in a run — instead derives its entire layout from:

```csharp
_tileSize = Math.Min(screenWidth, screenHeight) / 20f;   // LevelingHandler.cs:80
```

`_tileSize` drives every font size and every card dimension on this screen (`_tileSize * 0.34`, `_tileSize * 0.58`, card width/height, etc. — `LevelingHandler.cs:139-230`). It's resolution-aware but completely blind to `GameProfile.Profile.GuiScale`. (There *is* a `Px()` helper in the same file that correctly multiplies by `DisplayScale`, but it's only used for a handful of border/inset pixel widths, not the primary layout unit.)

Practically: a player who turns the "GUI SCALE" slider up or down in Settings to fit their couch/monitor will see every menu in the game resize — except the upgrade cards, which stay exactly the same size. That's the one screen in the whole game where the setting silently doesn't apply.

**Fix:** derive `_tileSize` from `UiTheme.DisplayScale(screenWidth, screenHeight)` (e.g. `_tileSize = ReferenceTileSize * DisplayScale(...)`) instead of raw `min(width, height)/20`.

### 4. Two un-unified corner-radius languages

`UiTheme.DrawPanel`/`DrawFramedPanel` — the backbone of almost all menu chrome (Settings, Title, Pause/Results, Reforge, Leveling, Soul panels) — is deliberately hard-cornered; it has no radius parameter at all. Meanwhile `ItemCards.cs`, `StatCards.cs`, several `FooterHud.cs` icon slots, `InformationSheet.cs`'s icon boxes, and one Soul station (`SoulVisualRenderer.cs:636`) all round their corners via `Primitives2D.FillRoundedRect`/`RoundedRectOutline` — but every file invents its own radius formula independently:

- `ItemCards.cs:177`: `rect.Width / 8`
- `ItemCards.cs:207`: `badgeSize / 5`
- `InformationSheet.cs:870/1580/1614`: `Px(3)` / `Px(4)` / `Px(4)`
- `SoulVisualRenderer.cs:636`: a flat `16`
- `StatCards.cs:45`: `radius` (its own separately-computed value)

None of these share a constant, so the "roundedness" of a card, a badge, an icon slot, and a Soul mirror all read as subtly different design decisions rather than one deliberate style. Worth noting: `UI/README.md` (lines ~13-15 and ~149-151) still documents this as *"pygame's border_radius has no Primitives2D equivalent yet, so ... corners render sharp instead of rounded"* — that's now out of date (the primitive clearly exists and is used in a dozen places), so it's worth fixing the doc too, or the next person doing a UI pass will avoid a treatment that's already built.

**Fix:** pick two named radius tokens on `UiTheme` (e.g. `CornerSmall`/`CornerLarge`, both `Px`-scaled) and have every rounded-corner call site reference one of them instead of computing its own. Decide deliberately whether hard-cornered `DrawPanel` chrome should ever adopt rounding, or whether "sharp panels, rounded iconography" is the intended split — right now it reads as accidental rather than intended either way.

### 5. The two "title card" moments don't share a visual family

The two places the game announces a name at full-width, dramatic scale are:

- `ModeEntrySplash.cs` — shown entering a Sense/Path, the Dungeon, or Aphantasia. A hand-jittered double-draw "glitch" title over a single accent underline, no border or bracket treatment (`ModeEntrySplash.cs:35-47`).
- `GameSession.DrawBossHealthBar` — the boss name/health banner shown at encounter start. Uses `UiTheme.DrawLivingPanel`, the game's established motif of clipped corner brackets plus animated per-Sense segment ticks (`GameSession.cs:4425-4474`).

Both are conceptually the same beat — "here is an important name, framed dramatically" — but they don't share any chrome. The splash is a flat band with no border at all; the boss banner is built entirely out of the game's signature bracket-and-segment "living panel" language. A player moving from a world-entry splash into that world's boss fight sees two unrelated title treatments in the same session.

This is polish, not a bug — the splash's glitch-text effect is a nice, deliberate flourish and shouldn't be flattened away. But giving it even a faint version of the living-panel bracket/segment motif (accent-colored, low-opacity, behind the jittered text) would tie it back into the same "this is a title card" family as the boss banner instead of reading as a one-off.

### 6. Two smaller, lower-priority notes

- **DevConsole has no chrome at all.** `DevConsole.cs:337-362` draws its panel as a raw `Primitives2D.FillRect` + a single divider line — no border, no shadow, none of `DrawPanel`'s treatment that every other overlay in the game uses. It's developer-only (backtick to open), so this is genuinely low priority, but since it's a one-line swap to `UiTheme.DrawPanel`, it costs little to bring in line with everything else the team looks at while testing.
- **Reforge is the only major system screen with zero non-mouse input path.** `LevelingHandler` has number-key shortcuts (1/2/3, R) even without a full focus grid; Title/Settings/Pause-Results all have complete D-pad/keyboard focus navigation via `UiFocusNavigator`. `ReforgeHandler.HandleInput` (`ReforgeHandler.cs:99-120`) only ever checks `mousePressed` and `Escape` — there's no keyboard or controller way to pick a slot or trigger Upgrade/Reroll. Worth a deliberate decision either way (forge is an optional, out-of-combat screen, so mouse-only may be an intentional scope cut) rather than an accidental gap, especially since every other system-interrupt screen in the game supports a controller.

## Suggested order of attack

1. **Scrim + LevelingHandler GuiScale fixes (#1, #3)** — smallest diffs, and #3 is the one that's actively working against a player who's already gone into Settings to make the game more comfortable to look at.
2. **SoulHub literal-size sweep (#2)** — mechanical grep-and-replace, no design decisions required.
3. **Corner-radius tokens + README correction (#4)** — a short-lived team conversation ("do we want rounded panels or not") followed by a small refactor.
4. **Title-card unification (#5)** — the one genuinely creative task in this list; worth doing once the mechanical fixes above are in, since it'll be easier to judge against a cleaner baseline.
5. **DevConsole chrome / Reforge input parity (#6)** — pick up opportunistically; neither blocks anything else here.

None of these require touching gameplay code — every fix lives in `UI/`, `Presentation/`, or the small drawing helpers inside `Systems/GameSession.cs`, and in every case the "correct" pattern to copy from already exists elsewhere in the same codebase.
