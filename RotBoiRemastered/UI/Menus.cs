using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

/// <summary>
/// What HandlePause/HandleResults determined should happen, in place of
/// Python's direct `vH.state = ...` assignment. Ported from menus.py's
/// handle_pause/handle_results -- the caller (not yet built; needs the
/// deferred Player.cs/main-loop state machine) is responsible for actually
/// transitioning GameState and calling resetAllStats() for Restart.
/// </summary>
public enum MenuAction { None, Resume, Restart, Extract, ReturnToTitle }

/// <summary>
/// Everything the results screen needs from the run's final state. Ported
/// from the `cS` reads inside menus.py's draw_results -- characterStats.py
/// isn't ported yet (see Entities/README.md's Player.cs note), so this is
/// the explicit seam, same pattern as LevelUpStatSnapshot.
/// </summary>
public sealed class RunResultsSnapshot
{
    public required string RunOutcome { get; init; }
    public int CurrentLevel { get; init; }
    public int NumOfEnemiesKilled { get; init; }
    public double RunTimeSeconds { get; init; }
    public required IReadOnlyDictionary<string, int> UpgradeTypeCounts { get; init; }
}

/// <summary>
/// Pause, settings, and end-of-run screens. Ported from menus.py.
///
/// Cleanup vs. the Python original: `_buttons`/`_settings_tab`/
/// `_rebinding_action` were module-level globals -- now instance state on
/// this class, same cleanup as every other stateful module ported so far
/// (RuntimeEncounter, Battleground, Camera). `handle_pause`/`handle_results`
/// directly assigned `vH.state`/called `game.resetAllStats()`; here they
/// return a `MenuAction` instead and leave the actual state transition to
/// the caller, matching `LevelingHandler.PlayerClicked`'s existing
/// return-a-result contract. The caller synchronizes the persisted autofire
/// preference into the current RunState after this handler returns.
/// </summary>
public sealed class Menus
{
    private static readonly (string Key, string Label, string Description)[] GameplayOptions =
    {
        ("CasualMode", "CASUAL ASSIST", "20% less incoming damage"),
        ("AutoFire", "DEFAULT AUTOFIRE", "New runs begin firing automatically"),
        ("TutorialHints", "CONTEXT HINTS", "Show short first-run reminders"),
        ("AimGuide", "AIM GUIDE", "Draw a short aiming line"),
        ("DamageNumbers", "DAMAGE NUMBERS", "Show combat damage text"),
        ("HighContrast", "HIGH CONTRAST", "Brighten hostile warnings"),
    };

    private static readonly (string Key, string Label)[] Tabs =
    {
        ("gameplay", "GAMEPLAY"), ("options", "OPTIONS"), ("keybinds", "KEYBINDS"),
    };

    private readonly Dictionary<string, Rectangle> _buttons = new();
    private readonly Dictionary<string, (Rectangle Hit, Rectangle Track, double Min, double Max)> _sliders = new();
    private string _settingsTab = "gameplay";
    private string? _rebindingAction;
    private string? _activeSlider;
    private bool _sliderDirty;

    private static bool GetGameplayToggle(string key) => key switch
    {
        "CasualMode" => GameProfile.Profile.CasualMode,
        "AutoFire" => GameProfile.Profile.AutoFire,
        "TutorialHints" => GameProfile.Profile.TutorialHints,
        "AimGuide" => GameProfile.Profile.AimGuide,
        "DamageNumbers" => GameProfile.Profile.DamageNumbers,
        "HighContrast" => GameProfile.Profile.HighContrast,
        _ => false,
    };

    private static int ClosestIndex(IReadOnlyList<double> levels, double current)
    {
        int best = 0;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < levels.Count; i++)
        {
            double diff = Math.Abs(levels[i] - current);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    private float Backdrop(SpriteBatch spriteBatch, int screenWidth, int screenHeight, string title, string subtitle)
    {
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), UiTheme.Void);
        int grid = Math.Max(28, Math.Min(screenWidth, screenHeight) / 28);
        var gridColor = new Color(23, 27, 35);
        for (int x = 0; x < screenWidth; x += grid)
            Primitives2D.Line(spriteBatch, new Vector2(x, 0), new Vector2(x, screenHeight), gridColor, 1);
        for (int y = 0; y < screenHeight; y += grid)
            Primitives2D.Line(spriteBatch, new Vector2(0, y), new Vector2(screenWidth, y), gridColor, 1);
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        UiTheme.DrawText(spriteBatch, title, 34 * scale, UiTheme.Text, new Vector2(screenWidth / 2f, screenHeight * .09f), "midtop");
        UiTheme.DrawText(spriteBatch, subtitle, 12 * scale, UiTheme.Cream, new Vector2(screenWidth / 2f, screenHeight * .17f), "midtop");
        return scale;
    }

    private bool Button(SpriteBatch spriteBatch, string name, Rectangle rect, string label, Point mousePosition, bool mouseDown,
        Color? accent = null, string? keyHint = null, bool enabled = true, double? textSize = null)
    {
        _buttons[name] = rect;
        double size = textSize ?? (int)(15 * UiTheme.DisplayScale(spriteBatch));
        return UiTheme.DrawButton(spriteBatch, rect, label, mousePosition, mouseDown, enabled, accent, keyHint, size);
    }

    private bool Activated(string name, Point mousePosition, bool mousePressed) =>
        mousePressed && _buttons.TryGetValue(name, out var rect) && rect.Contains(mousePosition);

    private void Slider(SpriteBatch spriteBatch, string name, Rectangle rect, string label, string valueLabel,
        double value, double min, double max, Point mousePosition, Color accent)
    {
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, rect.Contains(mousePosition) ? accent : UiTheme.Border, shadow: 3);
        UiTheme.DrawText(spriteBatch, label, 10 * UiTheme.DisplayScale(spriteBatch), UiTheme.Text,
            new Vector2(rect.X + 10, rect.Y + 7));
        UiTheme.DrawText(spriteBatch, valueLabel, 9 * UiTheme.DisplayScale(spriteBatch), accent,
            new Vector2(rect.Right - 10, rect.Y + 8), "topright");
        var track = new Rectangle(rect.X + 12, rect.Bottom - 15, rect.Width - 24, 7);
        Primitives2D.FillRect(spriteBatch, track, UiTheme.Ink);
        double ratio = Math.Clamp((value - min) / Math.Max(.0001, max - min), 0, 1);
        var fill = new Rectangle(track.X, track.Y, (int)Math.Round(track.Width * ratio), track.Height);
        Primitives2D.FillRect(spriteBatch, fill, accent);
        int knobX = track.X + (int)Math.Round(track.Width * ratio);
        Primitives2D.FillRect(spriteBatch, new Rectangle(knobX - 3, track.Y - 4, 7, track.Height + 8), UiTheme.Cream);
        _sliders[name] = (rect, track, min, max);
    }

    public static double SliderValue(Rectangle track, int mouseX, double min, double max)
    {
        double ratio = Math.Clamp((mouseX - track.Left) / (double)Math.Max(1, track.Width), 0, 1);
        return min + (max - min) * ratio;
    }

    private void FinishSlider()
    {
        if (_sliderDirty) GameProfile.SaveProfile();
        _activeSlider = null;
        _sliderDirty = false;
    }

    public void DrawPause(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Point mousePosition, bool mouseDown,
        bool canExtract = false, bool soulContext = false, bool settingsOnly = false)
    {
        _buttons.Clear();
        _sliders.Clear();
        string title = settingsOnly ? "SETTINGS" : soulContext ? "SOUL PAUSED" : "RUN PAUSED";
        string subtitle = settingsOnly ? "Tune the game before entering the rot." : soulContext
            ? "The sanctuary will wait for you." : "Take a breath. Combat is fully stopped.";
        float scale = Backdrop(spriteBatch, screenWidth, screenHeight, title, subtitle);
        float width = Math.Min(screenWidth * .68f, 900 * scale);
        float left = (screenWidth - width) / 2f;
        float buttonW = width * .34f, buttonH = 58 * scale;
        Button(spriteBatch, "resume", new Rectangle((int)left, (int)(screenHeight * .25f), (int)buttonW, (int)buttonH),
            settingsOnly ? "BACK" : soulContext ? "RETURN TO SOUL" : "RESUME", mousePosition, mouseDown, UiTheme.Green, "ESC");
        if (!soulContext && !settingsOnly)
        {
            Button(spriteBatch, "restart", new Rectangle((int)left, (int)(screenHeight * .25f + 72 * scale), (int)buttonW, (int)buttonH),
                "RESTART RUN", mousePosition, mouseDown, UiTheme.Gold, "R");
            Button(spriteBatch, "extract", new Rectangle((int)left, (int)(screenHeight * .25f + 216 * scale), (int)buttonW, (int)buttonH),
                canExtract ? "EXTRACT EQUIPMENT" : "EXTRACTION LOCKED", mousePosition, mouseDown, UiTheme.Green, "X", canExtract);
        }
        if (!settingsOnly)
            Button(spriteBatch, "title", new Rectangle((int)left, (int)(screenHeight * .25f + 144 * scale), (int)buttonW, (int)buttonH),
                "RETURN TO TITLE", mousePosition, mouseDown, UiTheme.Red, "Q");

        var settings = new Rectangle((int)(left + width * .40f), (int)(screenHeight * .25f), (int)(width * .60f), (int)(screenHeight * .48f));
        UiTheme.DrawPanel(spriteBatch, settings, UiTheme.Panel, UiTheme.Blue, shadow: 6);

        float tabH = 38 * scale;
        float tabW = settings.Width / (float)Tabs.Length;
        for (int index = 0; index < Tabs.Length; index++)
        {
            var (key, label) = Tabs[index];
            var rect = new Rectangle((int)(settings.X + index * tabW), settings.Y, (int)tabW, (int)tabH);
            Button(spriteBatch, $"tab_{key}", rect, label, mousePosition, mouseDown, _settingsTab == key ? UiTheme.Blue : UiTheme.Border);
        }

        float bodyTop = settings.Y + tabH + 12 * scale;
        if (_settingsTab == "gameplay")
        {
            for (int index = 0; index < GameplayOptions.Length; index++)
            {
                var (key, label, description) = GameplayOptions[index];
                float y = bodyTop + index * 51 * scale;
                var rect = new Rectangle((int)(settings.X + 14 * scale), (int)y, (int)(settings.Width - 28 * scale), (int)(42 * scale));
                bool active = GetGameplayToggle(key);
                Button(spriteBatch, key, rect, $"{label}  //  {(active ? "ON" : "OFF")}", mousePosition, mouseDown,
                    active ? UiTheme.Green : UiTheme.Border);
                UiTheme.DrawText(spriteBatch, description, 8 * scale, UiTheme.Muted,
                    new Vector2(rect.X + 10 * scale, rect.Bottom - 4 * scale), "bottomleft");
            }
        }
        else if (_settingsTab == "options")
        {
            var shakeRect = new Rectangle((int)(settings.X + 14 * scale), (int)bodyTop, (int)(settings.Width - 28 * scale), (int)(42 * scale));
            Button(spriteBatch, "screen_shake", shakeRect,
                $"SCREEN SHAKE  //  {(int)(GameProfile.Profile.ScreenShake * 100)}%", mousePosition, mouseDown, UiTheme.Gold);
            UiTheme.DrawText(spriteBatch, "How strongly hits rattle the camera", 8 * scale, UiTheme.Muted,
                new Vector2(shakeRect.X + 10 * scale, shakeRect.Bottom - 4 * scale), "bottomleft");

            int rowHeight = (int)(48 * scale), rowGap = (int)(7 * scale);
            int sliderX = (int)(settings.X + 14 * scale), sliderWidth = (int)(settings.Width - 28 * scale);
            var textSizeRect = new Rectangle(sliderX, (int)(bodyTop + 51 * scale), sliderWidth, rowHeight);
            Slider(spriteBatch, "text_size", textSizeRect, "TEXT SIZE", $"{GameProfile.Profile.TextSize * 100:0}%",
                GameProfile.Profile.TextSize, UiTheme.MinTextScale, UiTheme.MaxTextScale, mousePosition, UiTheme.Gold);
            var guiRect = new Rectangle(sliderX, textSizeRect.Bottom + rowGap, sliderWidth, rowHeight);
            Slider(spriteBatch, "gui_scale", guiRect, "GUI SCALE", $"{GameProfile.Profile.GuiScale * 100:0}%",
                GameProfile.Profile.GuiScale, UiTheme.MinGuiScale, UiTheme.MaxGuiScale, mousePosition, UiTheme.Blue);
            var damageRect = new Rectangle(sliderX, guiRect.Bottom + rowGap, sliderWidth, rowHeight);
            Slider(spriteBatch, "damage_text_size", damageRect, "DAMAGE TEXT SIZE", $"{GameProfile.Profile.DamageTextSize * 100:0}%",
                GameProfile.Profile.DamageTextSize, UiTheme.MinDamageTextScale, UiTheme.MaxDamageTextScale, mousePosition, UiTheme.Red);

            var fullscreenRect = new Rectangle(sliderX, damageRect.Bottom + rowGap, sliderWidth, (int)(42 * scale));
            bool fullscreen = GameProfile.Profile.Fullscreen;
            Button(spriteBatch, "fullscreen", fullscreenRect, $"FULLSCREEN  //  {(fullscreen ? "ON" : "OFF")}",
                mousePosition, mouseDown, fullscreen ? UiTheme.Green : UiTheme.Border);
            UiTheme.DrawText(spriteBatch, "Native-resolution fullscreen (F11 also toggles this)", 8 * scale, UiTheme.Muted,
                new Vector2(fullscreenRect.X + 10 * scale, fullscreenRect.Bottom - 4 * scale), "bottomleft");
        }
        else
        {
            float rowH = 33 * scale;
            for (int index = 0; index < Keybinds.Actions.Count; index++)
            {
                var (actionId, label, _) = Keybinds.Actions[index];
                float y = bodyTop + index * rowH;
                var rect = new Rectangle((int)(settings.X + 14 * scale), (int)y, (int)(settings.Width - 28 * scale), (int)(28 * scale));
                if (_rebindingAction == actionId)
                {
                    Button(spriteBatch, $"keybind_{actionId}", rect, $"{label}  //  PRESS A KEY (ESC CLEARS)",
                        mousePosition, mouseDown, UiTheme.Gold, textSize: 13 * scale);
                }
                else
                {
                    var key = Keybinds.KeyFor(actionId);
                    string keyLabel = Keybinds.LabelForKey(key);
                    Button(spriteBatch, $"keybind_{actionId}", rect, $"{label}  //  {keyLabel}", mousePosition, mouseDown,
                        key is not null ? UiTheme.Blue : UiTheme.Border, textSize: 13 * scale);
                }
            }
            float mouseY = bodyTop + Keybinds.Actions.Count * rowH + 10 * scale;
            UiTheme.DrawText(spriteBatch, "MOUSE  //  AIM / FIRE  (not rebindable)", 8 * scale, UiTheme.Muted,
                new Vector2(settings.X + 14 * scale, mouseY));
        }

        UiTheme.DrawText(spriteBatch, "TAB toggles HUD details during play", 9 * scale, UiTheme.Muted,
            new Vector2(screenWidth / 2f, screenHeight * .82f), "center");
    }

    public MenuAction HandlePause(IReadOnlySet<Keys> keysPressed, Point mousePosition, bool mouseDown, bool mousePressed,
        bool canExtract = false, bool soulContext = false, bool settingsOnly = false)
    {
        if (!mouseDown && _activeSlider is not null)
            FinishSlider();
        if (_rebindingAction is not null)
        {
            if (keysPressed.Count > 0)
            {
                var pressedKey = keysPressed.First();
                if (pressedKey == Keys.Escape)
                    Keybinds.ClearBinding(_rebindingAction);
                else
                    Keybinds.SetBinding(_rebindingAction, pressedKey);
                _rebindingAction = null;
            }
            return MenuAction.None;
        }

        if (keysPressed.Contains(Keys.Escape) || Activated("resume", mousePosition, mousePressed))
        {
            FinishSlider();
            return MenuAction.Resume;
        }
        // Keybinds.Pressed reads InputState.KeysPressed directly rather than
        // taking a parameter (see Keybinds.cs); checking the bound key against
        // the keysPressed passed in here instead keeps this method's input
        // fully explicit rather than depending on that global staying in sync.
        var restartKey = Keybinds.KeyFor("restart");
        if (!soulContext && !settingsOnly && ((restartKey.HasValue && keysPressed.Contains(restartKey.Value)) || Activated("restart", mousePosition, mousePressed)))
            return MenuAction.Restart;
        if (!settingsOnly && (keysPressed.Contains(Keys.Q) || Activated("title", mousePosition, mousePressed)))
            return MenuAction.ReturnToTitle;
        if (!soulContext && !settingsOnly && canExtract && (keysPressed.Contains(Keys.X) || Activated("extract", mousePosition, mousePressed)))
            return MenuAction.Extract;

        foreach (var (key, _) in Tabs)
        {
            if (Activated($"tab_{key}", mousePosition, mousePressed))
            {
                _settingsTab = key;
                break;
            }
        }

        if (_settingsTab == "gameplay")
        {
            foreach (var (key, _, _) in GameplayOptions)
            {
                if (Activated(key, mousePosition, mousePressed))
                    GameProfile.Toggle(key);
            }
        }
        else if (_settingsTab == "options")
        {
            if (Activated("screen_shake", mousePosition, mousePressed))
            {
                double[] levels = { 0.0, .35, .65, 1.0 };
                int idx = ClosestIndex(levels, GameProfile.Profile.ScreenShake);
                GameProfile.Profile.ScreenShake = levels[(idx + 1) % levels.Length];
                GameProfile.SaveProfile();
            }
            if (Activated("fullscreen", mousePosition, mousePressed))
                GameProfile.Toggle("Fullscreen");
            if (mouseDown)
            {
                if (_activeSlider is null)
                    _activeSlider = _sliders.FirstOrDefault(slider => slider.Value.Hit.Contains(mousePosition)).Key;
                if (_activeSlider is not null && _sliders.TryGetValue(_activeSlider, out var slider))
                {
                    double value = SliderValue(slider.Track, mousePosition.X, slider.Min, slider.Max);
                    if (_activeSlider == "text_size") GameProfile.Profile.TextSize = Math.Round(value, 2);
                    if (_activeSlider == "gui_scale") GameProfile.Profile.GuiScale = Math.Round(value, 2);
                    if (_activeSlider == "damage_text_size") GameProfile.Profile.DamageTextSize = Math.Round(value, 2);
                    _sliderDirty = true;
                }
            }
        }
        else
        {
            foreach (var (actionId, _, _) in Keybinds.Actions)
            {
                if (Activated($"keybind_{actionId}", mousePosition, mousePressed))
                {
                    _rebindingAction = actionId;
                    break;
                }
            }
        }
        return MenuAction.None;
    }

    public void DrawResults(SpriteBatch spriteBatch, int screenWidth, int screenHeight, RunResultsSnapshot results,
        Point mousePosition, bool mouseDown)
    {
        bool completed = results.RunOutcome == "RUN COMPLETE";
        Color accent = completed ? UiTheme.Cream : UiTheme.Red;
        float scale = Backdrop(spriteBatch, screenWidth, screenHeight, results.RunOutcome, "The build is saved to your run record.");
        float width = Math.Min(screenWidth * .62f, 820 * scale);
        var panel = new Rectangle((int)((screenWidth - width) / 2f), (int)(screenHeight * .25f), (int)width, (int)(screenHeight * .38f));
        UiTheme.DrawPanel(spriteBatch, panel, UiTheme.PanelRaised, accent, shadow: 8);

        int totalUpgrades = results.UpgradeTypeCounts.Values.Sum();
        var stats = new (string Label, string Value)[]
        {
            ("LEVEL", results.CurrentLevel.ToString("D2")),
            ("KILLS", results.NumOfEnemiesKilled.ToString()),
            ("TIME", $"{(int)(results.RunTimeSeconds / 60):D2}:{(int)(results.RunTimeSeconds % 60):D2}"),
            ("UPGRADES", totalUpgrades.ToString()),
        };
        float cell = panel.Width / (float)stats.Length;
        for (int index = 0; index < stats.Length; index++)
        {
            float x = panel.X + cell * (index + .5f);
            UiTheme.DrawText(spriteBatch, stats[index].Value, 29 * scale, accent, new Vector2(x, panel.Y + 55 * scale), "center");
            UiTheme.DrawText(spriteBatch, stats[index].Label, 9 * scale, UiTheme.Muted, new Vector2(x, panel.Y + 89 * scale), "center");
        }

        var families = new Dictionary<string, int>();
        foreach (var (name, count) in results.UpgradeTypeCounts)
        {
            if (Upgrades.DefinitionsByName.TryGetValue(name, out var definition))
                families[definition.Category] = families.GetValueOrDefault(definition.Category) + count;
        }
        string shape = families.Count > 0
            ? families.OrderByDescending(kv => kv.Value).First().Key.ToUpperInvariant()
            : "UNSHAPED";
        UiTheme.DrawText(spriteBatch, $"RUN SHAPE  //  {shape}", 13 * scale, UiTheme.Purple,
            new Vector2(panel.Center.X, panel.Bottom - 50 * scale), "center");

        Button(spriteBatch, "retry", new Rectangle(panel.X, (int)(panel.Bottom + 24 * scale), (int)(panel.Width * .48f), (int)(58 * scale)),
            "PLAY AGAIN", mousePosition, mouseDown, UiTheme.Green, "ENTER");
        Button(spriteBatch, "results_title",
            new Rectangle((int)(panel.Right - panel.Width * .48f), (int)(panel.Bottom + 24 * scale), (int)(panel.Width * .48f), (int)(58 * scale)),
            "TITLE SCREEN", mousePosition, mouseDown, UiTheme.Red, "ESC");
    }

    public MenuAction HandleResults(IReadOnlySet<Keys> keysPressed, Point mousePosition, bool mousePressed)
    {
        if (keysPressed.Contains(Keys.Enter) || Activated("retry", mousePosition, mousePressed))
            return MenuAction.Restart;
        if (keysPressed.Contains(Keys.Escape) || Activated("results_title", mousePosition, mousePressed))
            return MenuAction.ReturnToTitle;
        return MenuAction.None;
    }
}
