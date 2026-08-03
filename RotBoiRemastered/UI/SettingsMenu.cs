using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

/// <summary>
/// Settings-first pause surface shared by the title, Soul, and an active run.
/// The category and session rails stay fixed while the center page scrolls.
/// </summary>
public sealed class SettingsMenu
{
    private static readonly (string Id, string Label)[] Categories =
    {
        ("gameplay", "GAMEPLAY"),
        ("accessibility", "ACCESSIBILITY"),
        ("display", "DISPLAY"),
        ("interface", "INTERFACE"),
        ("controls", "CONTROLS"),
    };

    private readonly Dictionary<string, Rectangle> _controls = new();
    private readonly UiFocusNavigator _focus = new();
    private string _category = "gameplay";
    private string? _rebindingAction;
    private string? _confirmation;
    private double _scroll;
    private bool _lastInputWasMouse = true;

    // Kept non-constant so Menus can retain its old rendering code as a
    // compatibility fallback without creating unreachable-code warnings.
    public bool Enabled => true;

    private static float Scale(int width, int height) =>
        UiTheme.DisplayScale(width, height);

    private void Register(string id, Rectangle rect, bool enabled = true)
    {
        _controls[id] = rect;
        _focus.Register(id, rect, enabled);
    }

    private void DrawButton(SpriteBatch spriteBatch, string id, Rectangle rect,
        string label, Point mouse, bool mouseDown, Color accent,
        bool enabled = true, string? hint = null, double size = 12)
    {
        Register(id, rect, enabled);
        UiTheme.DrawButton(spriteBatch, rect, label, mouse, mouseDown, enabled,
            accent, hint, size);
        if (_focus.IsFocused(id) && enabled)
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream, 2);
    }

    public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight,
        Point mouse, bool mouseDown, bool canExtract, bool soulContext,
        bool settingsOnly, float animationTime)
    {
        _controls.Clear();
        _focus.BeginFrame();
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight),
            new Color(3, 5, 8, 205));

        float scale = Scale(screenWidth, screenHeight);
        int margin = Math.Max(8, (int)(14 * scale));
        int titleHeight = Math.Max(32, (int)(46 * scale));
        var root = new Rectangle(margin, margin, screenWidth - margin * 2,
            screenHeight - margin * 2);
        UiTheme.DrawCompositePanel(spriteBatch, root, animationTime,
            UiTheme.Panel * .98f, UiTheme.Border, 7);
        UiTheme.DrawText(spriteBatch, settingsOnly ? "SETTINGS" : "PAUSED",
            20 * scale, UiTheme.Text, new Vector2(root.X + margin, root.Y + 8 * scale));
        UiTheme.DrawText(spriteBatch,
            settingsOnly ? "THE SOUL REMEMBERS YOUR PREFERENCES" :
            soulContext ? "THE SANCTUARY IS WAITING" : "THE RUN IS FROZEN",
            8 * scale, UiTheme.Muted,
            new Vector2(root.Right - margin, root.Y + 14 * scale), "topright");

        int railWidth = Math.Clamp((int)(root.Width * .18f), 104, (int)(190 * scale));
        int actionWidth = Math.Clamp((int)(root.Width * .21f), 124, (int)(235 * scale));
        int gap = Math.Max(6, (int)(9 * scale));
        var categoryRail = new Rectangle(root.X + margin, root.Y + titleHeight,
            railWidth, root.Height - titleHeight - margin);
        var actionRail = new Rectangle(root.Right - margin - actionWidth,
            categoryRail.Y, actionWidth, categoryRail.Height);
        var page = new Rectangle(categoryRail.Right + gap, categoryRail.Y,
            actionRail.Left - gap - (categoryRail.Right + gap), categoryRail.Height);

        UiTheme.DrawCompositePanel(spriteBatch, categoryRail, animationTime,
            UiTheme.Ink, UiTheme.Border, 2);
        UiTheme.DrawCompositePanel(spriteBatch, page, animationTime,
            UiTheme.Panel, UiTheme.Border, 2);
        UiTheme.DrawCompositePanel(spriteBatch, actionRail, animationTime,
            UiTheme.Ink, UiTheme.Border, 2);

        int rowGap = Math.Max(4, (int)(6 * scale));
        int railRow = Math.Max(30, (categoryRail.Height - rowGap * 6) / 7);
        int y = categoryRail.Y + rowGap;
        foreach (var (id, label) in Categories)
        {
            var rect = new Rectangle(categoryRail.X + rowGap, y,
                categoryRail.Width - rowGap * 2, railRow);
            DrawButton(spriteBatch, $"category:{id}", rect, label, mouse, mouseDown,
                _category == id ? UiTheme.Purple : UiTheme.Border,
                size: 9 * scale);
            y += railRow + rowGap;
        }

        DrawPage(spriteBatch, page, mouse, mouseDown, scale);
        DrawActions(spriteBatch, actionRail, mouse, mouseDown, canExtract,
            soulContext, settingsOnly, scale);

        string prompt = _lastInputWasMouse
            ? "CLICK TO CHANGE  •  WHEEL TO SCROLL"
            : "D-PAD / WASD NAVIGATE  •  A / ENTER CONFIRM  •  B / ESC BACK";
        UiTheme.DrawText(spriteBatch, prompt, 7 * scale, UiTheme.Muted,
            new Vector2(page.Center.X, page.Bottom - 5 * scale), "midbottom");

        if (_confirmation is not null)
            DrawConfirmation(spriteBatch, root, mouse, mouseDown, scale, animationTime);
    }

    private void DrawPage(SpriteBatch spriteBatch, Rectangle page, Point mouse,
        bool mouseDown, float scale)
    {
        int pad = Math.Max(7, (int)(11 * scale));
        string title = Categories.First(category => category.Id == _category).Label;
        UiTheme.DrawText(spriteBatch, title, 13 * scale, UiTheme.Cream,
            new Vector2(page.X + pad, page.Y + pad));
        var viewport = new Rectangle(page.X + pad, page.Y + (int)(35 * scale),
            page.Width - pad * 2, page.Height - (int)(56 * scale));

        var rows = RowsForCategory();
        int rowHeight = Math.Max(34, (int)(46 * scale));
        int gap = Math.Max(4, (int)(6 * scale));
        int contentHeight = rows.Count * (rowHeight + gap);
        double maxScroll = Math.Max(0, contentHeight - viewport.Height);
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
        int y = viewport.Y - (int)_scroll;
        foreach (SettingRow row in rows)
        {
            var rect = new Rectangle(viewport.X, y, viewport.Width, rowHeight);
            if (rect.Top >= viewport.Top && rect.Bottom <= viewport.Bottom)
            {
                string label = row.Value.Length == 0 ? row.Label
                    : $"{row.Label}  //  {row.Value}";
                DrawButton(spriteBatch, row.Id, rect, label, mouse, mouseDown,
                    row.Accent, row.Enabled, size: 9 * scale);
                if (!string.IsNullOrWhiteSpace(row.Description) && rowHeight >= 38)
                    UiTheme.DrawText(spriteBatch, row.Description, 6.8 * scale,
                        row.Enabled ? UiTheme.Muted : UiTheme.Red,
                        new Vector2(rect.X + 8 * scale, rect.Bottom - 3 * scale),
                        "bottomleft");
            }
            y += rowHeight + gap;
        }

        if (maxScroll > 0)
        {
            var track = new Rectangle(viewport.Right - 3, viewport.Y, 3, viewport.Height);
            Primitives2D.FillRect(spriteBatch, track, UiTheme.Ink);
            int thumb = Math.Max(16, (int)(viewport.Height * viewport.Height /
                (double)contentHeight));
            int thumbY = viewport.Y + (int)((viewport.Height - thumb) * _scroll / maxScroll);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle(track.X, thumbY, track.Width, thumb), UiTheme.Purple);
        }
    }

    private sealed record SettingRow(string Id, string Label, string Value,
        string Description, Color Accent, bool Enabled = true);

    private List<SettingRow> RowsForCategory()
    {
        GameProfileData profile = GameProfile.Profile;
        static string OnOff(bool value) => value ? "ON" : "OFF";
        return _category switch
        {
            "gameplay" => new List<SettingRow>
            {
                new("setting:CasualMode", "CASUAL ASSIST", OnOff(profile.CasualMode), "20% less incoming damage", UiTheme.Green),
                new("setting:AutoFire", "DEFAULT AUTOFIRE", OnOff(profile.AutoFire), "New runs begin firing automatically", UiTheme.Green),
                new("setting:TutorialHints", "CONTEXT HINTS", OnOff(profile.TutorialHints), "Short situational reminders", UiTheme.Blue),
                new("setting:AimGuide", "AIM GUIDE", OnOff(profile.AimGuide), "Show a short aiming line", UiTheme.Blue),
            },
            "accessibility" => new List<SettingRow>
            {
                new("setting:DamageNumbers", "DAMAGE NUMBERS", OnOff(profile.DamageNumbers), "Show combat damage text", UiTheme.Red),
                new("setting:HighContrast", "HIGH CONTRAST", OnOff(profile.HighContrast), "Strengthen hostile warnings", UiTheme.Gold),
                new("setting:ScreenShake", "SCREEN SHAKE", $"{profile.ScreenShake * 100:0}%", "Impact-driven camera movement", UiTheme.Gold),
                new("setting:VisualEffects", "VISUAL EFFECTS", $"{profile.VisualEffectsIntensity * 100:0}%", "Ambient motion and particles", UiTheme.Purple),
                new("setting:TextSize", "TEXT SIZE", $"{profile.TextSize * 100:0}%", "Menu and interface type", UiTheme.Cream),
                new("setting:DamageTextSize", "DAMAGE TEXT SIZE", $"{profile.DamageTextSize * 100:0}%", "Floating combat type", UiTheme.Red),
            },
            "display" => new List<SettingRow>
            {
                new("setting:Fullscreen", "FULLSCREEN", OnOff(profile.Fullscreen), "Borderless native display", UiTheme.Blue),
                new("setting:GuiScale", "GUI SCALE", GuiScaleLabel(profile.GuiScale), "Size of interface containers", UiTheme.Blue),
                new("setting:CameraZoom", "CAMERA ZOOM", $"{profile.CameraZoom * 100:0}%", "Default combat camera distance", UiTheme.Purple),
                new("setting:MaxFrameRate", "FPS CAP", $"{profile.MaxFrameRate}", "Maximum update and presentation rate", UiTheme.Green),
                new("setting:VSync", "VERTICAL SYNC", OnOff(profile.VSync), "Match display refresh when supported", UiTheme.Green),
            },
            "interface" => new List<SettingRow>
            {
                FooterRow(0), FooterRow(1), FooterRow(2),
                new("setting:ResetUi", "RESTORE UI DEFAULTS", "", "Reset footer stats, scale, and text sizes", UiTheme.Gold),
            },
            _ => ControlsRows(),
        };
    }

    private static SettingRow FooterRow(int index)
    {
        string id = GameProfile.Profile.FooterStats[index];
        FooterStatDefinition definition = FooterStats.ById[id];
        return new SettingRow($"setting:Footer:{index}", $"FOOTER STAT {index + 1}",
            definition.Label.ToUpperInvariant(), "Choose a live combat readout", UiTheme.Purple);
    }

    private static List<SettingRow> ControlsRows()
    {
        var rows = new List<SettingRow>
        {
            new("manual", "FIELD MANUAL", "", "WASD move • mouse/right stick aim • Space/A dash • Tab/View dossier", UiTheme.Cream),
        };
        rows.AddRange(Keybinds.Actions.Select(action => new SettingRow(
            $"binding:{action.Id}", action.Label.ToUpperInvariant(),
            Keybinds.LabelForKey(Keybinds.KeyFor(action.Id)),
            "Select, then press a key; Escape clears the binding", UiTheme.Blue)));
        rows.Add(new SettingRow("binding:reset", "RESET BINDINGS", "",
            "Restore the default keyboard layout", UiTheme.Gold));
        return rows;
    }

    private static string GuiScaleLabel(double scale)
    {
        int index = 0;
        double best = double.MaxValue;
        for (int i = 0; i < UiTheme.GuiScaleLevels.Count; i++)
        {
            double distance = Math.Abs(UiTheme.GuiScaleLevels[i] - scale);
            if (distance < best) { best = distance; index = i; }
        }
        return UiTheme.GuiScaleLabels[index];
    }

    private void DrawActions(SpriteBatch spriteBatch, Rectangle rail, Point mouse,
        bool mouseDown, bool canExtract, bool soulContext, bool settingsOnly,
        float scale)
    {
        int pad = Math.Max(5, (int)(7 * scale));
        int rowHeight = Math.Max(29, (int)(39 * scale));
        int y = rail.Y + pad;
        void Action(string id, string label, Color color, bool enabled = true,
            string? hint = null)
        {
            var rect = new Rectangle(rail.X + pad, y, rail.Width - pad * 2, rowHeight);
            DrawButton(spriteBatch, id, rect, label, mouse, mouseDown, color,
                enabled, hint, 8.5 * scale);
            y += rowHeight + pad;
        }

        Action("action:resume", settingsOnly ? "BACK" : soulContext ? "RETURN TO SOUL" : "RESUME",
            UiTheme.Green, hint: "ESC");
        if (!soulContext && !settingsOnly)
        {
            Action("action:dossier", "DOSSIER", UiTheme.Purple, hint: "TAB");
            Action("action:restart", "RESTART RUN", UiTheme.Gold);
            Action("action:extract", canExtract ? "EXTRACT" : "EXTRACT LOCKED",
                UiTheme.Green, canExtract);
            if (!canExtract)
                UiTheme.DrawText(spriteBatch, "DEFEAT BEAUDIS TO UNLOCK", 5.8 * scale,
                    UiTheme.Red, new Vector2(rail.Center.X, y - pad / 2f), "center");
        }
        if (!settingsOnly)
            Action("action:title", "RETURN TO TITLE", UiTheme.Red);
        Action("action:quit", "QUIT GAME", UiTheme.Red);
    }

    private void DrawConfirmation(SpriteBatch spriteBatch, Rectangle root,
        Point mouse, bool mouseDown, float scale, float animationTime)
    {
        Primitives2D.FillRect(spriteBatch, root, new Color(0, 0, 0, 185));
        int width = Math.Min(root.Width - 24, Math.Max(250, (int)(430 * scale)));
        int height = Math.Min(root.Height - 24, Math.Max(140, (int)(180 * scale)));
        var modal = new Rectangle(root.Center.X - width / 2, root.Center.Y - height / 2,
            width, height);
        UiTheme.DrawCompositePanel(spriteBatch, modal, animationTime,
            UiTheme.PanelRaised, UiTheme.Red, 8);
        UiTheme.DrawText(spriteBatch, ConfirmationTitle(_confirmation!), 15 * scale,
            UiTheme.Text, new Vector2(modal.Center.X, modal.Y + 18 * scale), "midtop");
        UiTheme.DrawText(spriteBatch, ConfirmationDescription(_confirmation!),
            8 * scale, UiTheme.Muted,
            new Vector2(modal.Center.X, modal.Y + 48 * scale), "midtop");
        int pad = Math.Max(8, (int)(12 * scale));
        int h = Math.Max(34, (int)(44 * scale));
        var cancel = new Rectangle(modal.X + pad, modal.Bottom - pad - h,
            (modal.Width - pad * 3) / 2, h);
        var confirm = new Rectangle(cancel.Right + pad, cancel.Y, cancel.Width, h);
        DrawButton(spriteBatch, "confirm:cancel", cancel, "CANCEL", mouse, mouseDown,
            UiTheme.Border, size: 9 * scale);
        DrawButton(spriteBatch, "confirm:accept", confirm, "CONFIRM", mouse, mouseDown,
            UiTheme.Red, size: 9 * scale);
        if (_focus.FocusedId is not "confirm:cancel" and not "confirm:accept")
            _focus.Focus("confirm:cancel");
    }

    private static string ConfirmationTitle(string action) => action switch
    {
        "restart" => "RESTART THIS RUN?",
        "extract" => "EXTRACT NOW?",
        "title" => "RETURN TO TITLE?",
        _ => "QUIT THE GAME?",
    };

    private static string ConfirmationDescription(string action) => action switch
    {
        "restart" => "The current run ends and carried gear is retained.",
        "extract" => "Bank the current loadout and end this run.",
        "title" => "Unsaved run progress will be left behind.",
        _ => "Your profile will be saved before closing.",
    };

    public MenuAction Handle(IReadOnlySet<Keys> keysPressed, Point mouse,
        bool mouseDown, bool mousePressed, bool canExtract, bool soulContext,
        bool settingsOnly, int scrollWheelDelta)
    {
        bool controllerInput = InputState.UiUpPressed || InputState.UiDownPressed
            || InputState.UiLeftPressed || InputState.UiRightPressed
            || InputState.ControllerConfirmPressed || InputState.ControllerBackPressed;
        bool keyboardNavigation = keysPressed.Any(key => key is Keys.Up or Keys.Down
            or Keys.Left or Keys.Right or Keys.W or Keys.A or Keys.S or Keys.D
            or Keys.Enter or Keys.Space);
        if (mousePressed || scrollWheelDelta != 0) _lastInputWasMouse = true;
        else if (controllerInput || keyboardNavigation) _lastInputWasMouse = false;

        if (_rebindingAction is not null)
        {
            if (keysPressed.Count > 0)
            {
                Keys pressed = keysPressed.First();
                if (pressed == Keys.Escape) Keybinds.ClearBinding(_rebindingAction);
                else Keybinds.SetBinding(_rebindingAction, pressed);
                _rebindingAction = null;
            }
            return MenuAction.None;
        }

        if (scrollWheelDelta != 0)
            _scroll = Math.Max(0, _scroll - scrollWheelDelta * .28);

        string? hovered = _focus.At(mouse);
        if (mousePressed && hovered is not null)
            _focus.Focus(hovered);

        bool up = InputState.UiUpPressed || keysPressed.Contains(Keys.Up) || keysPressed.Contains(Keys.W);
        bool down = InputState.UiDownPressed || keysPressed.Contains(Keys.Down) || keysPressed.Contains(Keys.S);
        bool left = InputState.UiLeftPressed || keysPressed.Contains(Keys.Left) || keysPressed.Contains(Keys.A);
        bool right = InputState.UiRightPressed || keysPressed.Contains(Keys.Right) || keysPressed.Contains(Keys.D);
        if (up) _focus.Move(0, -1);
        if (down) _focus.Move(0, 1);
        if (left) _focus.Move(-1, 0);
        if (right) _focus.Move(1, 0);

        bool confirm = mousePressed || InputState.ControllerConfirmPressed
            || keysPressed.Contains(Keys.Enter) || keysPressed.Contains(Keys.Space);
        string? activated = mousePressed ? hovered : confirm ? _focus.FocusedId : null;

        if (_confirmation is not null)
        {
            if (InputState.ControllerBackPressed || keysPressed.Contains(Keys.Escape)
                || activated == "confirm:cancel")
            {
                _confirmation = null;
                return MenuAction.None;
            }
            if (activated == "confirm:accept")
            {
                string action = _confirmation;
                _confirmation = null;
                return action switch
                {
                    "restart" => MenuAction.Restart,
                    "extract" => MenuAction.Extract,
                    "title" => MenuAction.ReturnToTitle,
                    _ => MenuAction.Quit,
                };
            }
            return MenuAction.None;
        }

        if (InputState.ControllerBackPressed || keysPressed.Contains(Keys.Escape))
            return MenuAction.Resume;
        if (activated is null)
            return MenuAction.None;
        if (activated.StartsWith("category:"))
        {
            _category = activated[9..];
            _scroll = 0;
            return MenuAction.None;
        }
        if (activated.StartsWith("action:"))
        {
            string action = activated[7..];
            if (action == "resume") return MenuAction.Resume;
            if (action == "dossier") return MenuAction.Dossier;
            if (action is "restart" or "extract" or "title" or "quit")
                _confirmation = action;
            return MenuAction.None;
        }
        if (activated.StartsWith("binding:"))
        {
            string action = activated[8..];
            if (action == "reset") Keybinds.ResetDefaults();
            else _rebindingAction = action;
            return MenuAction.None;
        }
        if (activated.StartsWith("setting:"))
            ChangeSetting(activated[8..], right ? 1 : left ? -1 : 1);
        return MenuAction.None;
    }

    private static void ChangeSetting(string key, int direction)
    {
        GameProfileData profile = GameProfile.Profile;
        if (key is "CasualMode" or "AutoFire" or "TutorialHints" or "AimGuide"
            or "DamageNumbers" or "HighContrast" or "Fullscreen" or "VSync")
        {
            GameProfile.Toggle(key);
            return;
        }
        if (key.StartsWith("Footer:"))
        {
            int slot = int.Parse(key[7..]);
            IReadOnlyList<FooterStatDefinition> values = FooterStats.Definitions;
            int current = values.ToList().FindIndex(value => value.Id == profile.FooterStats[slot]);
            int next = (current + direction + values.Count) % values.Count;
            profile.FooterStats = FooterStats.Select(profile.FooterStats, slot, values[next].Id);
        }
        else if (key == "ResetUi")
        {
            profile.FooterStats = FooterStats.Defaults.ToList();
            profile.GuiScale = 1;
            profile.TextSize = 1;
            profile.DamageTextSize = .8;
        }
        else if (key == "ScreenShake")
            profile.ScreenShake = Cycle(profile.ScreenShake, new[] { 0d, .35, .65, 1d }, direction);
        else if (key == "VisualEffects")
            profile.VisualEffectsIntensity = Math.Clamp(profile.VisualEffectsIntensity + direction * .25, 0, 1);
        else if (key == "TextSize")
            profile.TextSize = Math.Clamp(profile.TextSize + direction * .1, UiTheme.MinTextScale, UiTheme.MaxTextScale);
        else if (key == "DamageTextSize")
            profile.DamageTextSize = Math.Clamp(profile.DamageTextSize + direction * .1, UiTheme.MinDamageTextScale, UiTheme.MaxDamageTextScale);
        else if (key == "GuiScale")
            profile.GuiScale = Cycle(profile.GuiScale, UiTheme.GuiScaleLevels, direction);
        else if (key == "CameraZoom")
            profile.CameraZoom = Math.Clamp(profile.CameraZoom + direction * .1, Camera.MinDefaultZoomScale, Camera.MaxDefaultZoomScale);
        else if (key == "MaxFrameRate")
            profile.MaxFrameRate = FramePacing.NormalizeFrameRate(profile.MaxFrameRate + direction * 15);
        GameProfile.SaveProfile();
    }

    private static double Cycle(double value, IReadOnlyList<double> levels, int direction)
    {
        int closest = 0;
        double distance = double.MaxValue;
        for (int index = 0; index < levels.Count; index++)
        {
            double candidate = Math.Abs(levels[index] - value);
            if (candidate < distance) { distance = candidate; closest = index; }
        }
        return levels[(closest + direction + levels.Count) % levels.Count];
    }
}
