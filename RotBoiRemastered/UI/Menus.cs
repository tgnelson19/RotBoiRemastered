using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public enum MenuAction
{
    None, Resume, Dossier, Restart, Extract, ReturnToTitle, EnterSoul, Quit,
}

/// <summary>Settings-first pause menu and immutable end-of-run debrief.</summary>
public sealed class Menus
{
    private readonly PresentationClock _presentationClock = new();
    private readonly SettingsMenu _settingsMenu = new();
    private readonly UiFocusNavigator _resultFocus = new();
    private readonly Dictionary<string, Rectangle> _buttons = new();

    public void AdvancePresentation(double seconds) =>
        _presentationClock.Advance(seconds);

    public void DrawPause(SpriteBatch spriteBatch, int screenWidth,
        int screenHeight, Point mousePosition, bool mouseDown,
        bool canExtract = false, bool soulContext = false,
        bool settingsOnly = false) =>
        _settingsMenu.Draw(spriteBatch, screenWidth, screenHeight, mousePosition,
            mouseDown, canExtract, soulContext, settingsOnly,
            (float)_presentationClock.Seconds);

    public MenuAction HandlePause(IReadOnlySet<Keys> keysPressed,
        Point mousePosition, bool mouseDown, bool mousePressed,
        bool canExtract = false, bool soulContext = false,
        bool settingsOnly = false, int scrollWheelDelta = 0) =>
        _settingsMenu.Handle(keysPressed, mousePosition, mouseDown,
            mousePressed, canExtract, soulContext, settingsOnly,
            scrollWheelDelta);

    public static double SliderValue(Rectangle track, int mouseX,
        double min, double max)
    {
        double ratio = Math.Clamp((mouseX - track.Left)
            / (double)Math.Max(1, track.Width), 0, 1);
        return min + (max - min) * ratio;
    }

    private void Button(SpriteBatch spriteBatch, string id, Rectangle rect,
        string label, Point mouse, bool mouseDown, Color accent, string hint,
        double size)
    {
        _buttons[id] = rect;
        UiTheme.DrawButton(spriteBatch, rect, label, mouse, mouseDown, true,
            accent, hint, size);
        if (_resultFocus.IsFocused(id))
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream, 2);
    }

    private bool Activated(string id, Point mouse, bool mousePressed) =>
        mousePressed && _buttons.TryGetValue(id, out Rectangle rect)
            && rect.Contains(mouse);

    public void DrawResults(SpriteBatch spriteBatch, int screenWidth,
        int screenHeight, RunResultReport report, Point mousePosition,
        bool mouseDown)
    {
        _buttons.Clear();
        _resultFocus.BeginFrame();
        bool success = report.Outcome is "RUN COMPLETE" or "EXTRACTED"
            or "APHANTASIA DEFEATED";
        Color outcomeAccent = success ? UiTheme.Cream : UiTheme.Red;
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        float animation = (float)(_presentationClock.Seconds
            * GameProfile.Profile.VisualEffectsIntensity);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(0, 0, screenWidth, screenHeight), UiTheme.Void);
        int margin = Math.Max(8, (int)(14 * scale));
        var root = new Rectangle(margin, margin, screenWidth - margin * 2,
            screenHeight - margin * 2);
        UiTheme.DrawFramedPanel(spriteBatch, root,
            UiTheme.Panel, outcomeAccent, 8);

        UiTheme.DrawText(spriteBatch, report.Outcome, 24 * scale,
            outcomeAccent, new Vector2(root.Center.X, root.Y + 10 * scale),
            "midtop");
        string pathLine = $"{report.PathTitle}  //  "
            + (report.HardMode ? "HARD MODE  //  " : "")
            + (report.NoExtract ? "NO EXTRACT  //  " : "")
            + (report.NewGamePlusLevel > 0
                ? $"NG+{report.NewGamePlusLevel}  //  " : "")
            + report.BuildIdentity;
        UiTheme.DrawText(spriteBatch, pathLine, 8 * scale,
            GamePaths.PathsByKey.GetValueOrDefault(report.PathKey)?.Accent
                ?? UiTheme.Cream,
            new Vector2(root.Center.X, root.Y + 42 * scale), "midtop");

        int contentTop = root.Y + Math.Max(62, (int)(67 * scale));
        int actionHeight = Math.Max(38, (int)(48 * scale));
        int actionGap = Math.Max(5, (int)(8 * scale));
        int contentBottom = root.Bottom - actionHeight - actionGap * 2;
        int contentHeight = Math.Max(100, contentBottom - contentTop);
        int gap = Math.Max(6, (int)(9 * scale));
        int leftWidth = (root.Width - margin * 2 - gap) / 2;
        var buildPanel = new Rectangle(root.X + margin, contentTop, leftWidth,
            contentHeight);
        var gearPanel = new Rectangle(buildPanel.Right + gap, contentTop,
            root.Right - margin - buildPanel.Right - gap, contentHeight);
        UiTheme.DrawFramedPanel(spriteBatch, buildPanel,
            UiTheme.PanelRaised, UiTheme.Border, 3);
        UiTheme.DrawFramedPanel(spriteBatch, gearPanel,
            UiTheme.PanelRaised, UiTheme.Border, 3);

        DrawDebriefBuild(spriteBatch, buildPanel, report, outcomeAccent, scale);
        DrawDebriefGear(spriteBatch, gearPanel, report, scale);

        int actionY = root.Bottom - actionGap - actionHeight;
        int actionWidth = (root.Width - margin * 2 - actionGap * 2) / 3;
        ResultButton("results_soul", root.X + margin, "ENTER SOUL",
            UiTheme.Purple, "F");
        ResultButton("retry", root.X + margin + actionWidth + actionGap,
            "PLAY AGAIN", UiTheme.Green, "ENTER");
        ResultButton("results_title",
            root.X + margin + (actionWidth + actionGap) * 2,
            "TITLE", UiTheme.Red, "ESC");

        void ResultButton(string id, int x, string label, Color accent,
            string hint)
        {
            var rect = new Rectangle(x, actionY, actionWidth, actionHeight);
            _resultFocus.Register(id, rect);
            Button(spriteBatch, id, rect, label, mousePosition, mouseDown,
                accent, hint, 9 * scale);
        }
    }

    private static void DrawDebriefBuild(SpriteBatch spriteBatch,
        Rectangle panel, RunResultReport report, Color accent, float scale)
    {
        int pad = Math.Max(7, (int)(11 * scale));
        UiTheme.DrawText(spriteBatch, "RUN DEBRIEF", 11 * scale, UiTheme.Cream,
            new Vector2(panel.X + pad, panel.Y + pad));
        string time = $"{(int)(report.Seconds / 60):D2}:"
            + $"{(int)(report.Seconds % 60):D2}";
        UiTheme.DrawText(spriteBatch,
            $"LEVEL {report.Level:D2}   KILLS {report.Kills}   TIME {time}   UPGRADES {report.UpgradeCount}",
            8 * scale, accent,
            new Vector2(panel.X + pad, panel.Y + 34 * scale));
        UiTheme.DrawText(spriteBatch, report.BuildIdentity, 16 * scale,
            UiTheme.Purple, new Vector2(panel.X + pad, panel.Y + 59 * scale));
        string families = report.DominantFamilies.Count == 0
            ? "NO DOMINANT UPGRADE FAMILY"
            : string.Join("  •  ", report.DominantFamilies);
        UiTheme.DrawText(spriteBatch, families, 7 * scale, UiTheme.Muted,
            new Vector2(panel.X + pad, panel.Y + 86 * scale));

        int rewardY = panel.Y + Math.Max((int)(112 * scale), panel.Height / 2);
        UiTheme.DrawText(spriteBatch, "REWARDS & PROGRESSION", 10 * scale,
            UiTheme.Cream, new Vector2(panel.X + pad, rewardY));
        UiTheme.DrawText(spriteBatch,
            $"MIND TOKENS  +{report.SoulTokenReward}", 9 * scale,
            report.SoulTokenReward > 0 ? UiTheme.Gold : UiTheme.Muted,
            new Vector2(panel.X + pad, rewardY + 25 * scale));
        UiTheme.DrawText(spriteBatch,
            $"PATH MASTERY  {report.PathMasteryBefore}  →  {report.PathMasteryAfter}",
            8 * scale, UiTheme.Text,
            new Vector2(panel.X + pad, rewardY + 47 * scale));
        bool unlocked = report.NewGamePlusAfter > report.NewGamePlusBefore;
        string ng = unlocked
            ? $"NG+ UNLOCKED  {report.NewGamePlusBefore}  →  {report.NewGamePlusAfter}"
            : $"NG+ UNLOCK TIER  {report.NewGamePlusAfter}";
        UiTheme.DrawText(spriteBatch, ng, 8 * scale,
            unlocked ? UiTheme.Green : UiTheme.Muted,
            new Vector2(panel.X + pad, rewardY + 69 * scale));
    }

    private static void DrawDebriefGear(SpriteBatch spriteBatch,
        Rectangle panel, RunResultReport report, float scale)
    {
        int pad = Math.Max(7, (int)(11 * scale));
        bool retained = report.LostLoadout.Count == 0;
        IReadOnlyList<RunItemSummary> items = retained
            ? report.RetainedLoadout : report.LostLoadout;
        UiTheme.DrawText(spriteBatch,
            retained ? "LOADOUT RETAINED" : "LOADOUT LOST", 11 * scale,
            retained ? UiTheme.Green : UiTheme.Red,
            new Vector2(panel.X + pad, panel.Y + pad));
        if (items.Count == 0)
        {
            UiTheme.DrawText(spriteBatch, "NO EQUIPMENT OR STASH ITEMS",
                8 * scale, UiTheme.Muted,
                new Vector2(panel.X + pad, panel.Y + 40 * scale));
            return;
        }
        int rowHeight = Math.Max(22, (int)(27 * scale));
        int maxRows = Math.Max(2,
            (panel.Height - (int)(48 * scale)) / rowHeight);
        int y = panel.Y + (int)(40 * scale);
        foreach (RunItemSummary item in items.Take(maxRows))
        {
            UiTheme.DrawText(spriteBatch, item.Slot, 7 * scale, UiTheme.Muted,
                new Vector2(panel.X + pad, y));
            UiTheme.DrawText(spriteBatch, item.Name.ToUpperInvariant(),
                8 * scale, UiTheme.Text,
                new Vector2(panel.X + panel.Width * .34f, y));
            UiTheme.DrawText(spriteBatch, item.Rarity.ToUpperInvariant(),
                7 * scale, UiTheme.Gold,
                new Vector2(panel.Right - pad, y), "topright");
            y += rowHeight;
        }
        if (items.Count > maxRows)
            UiTheme.DrawText(spriteBatch, $"+ {items.Count - maxRows} MORE ITEMS",
                7 * scale, UiTheme.Muted,
                new Vector2(panel.X + pad, panel.Bottom - pad), "bottomleft");
    }

    public MenuAction HandleResults(IReadOnlySet<Keys> keysPressed,
        Point mousePosition, bool mousePressed)
    {
        string? hovered = _resultFocus.At(mousePosition);
        if (mousePressed && hovered is not null)
            _resultFocus.Focus(hovered);
        bool left = InputState.UiLeftPressed || keysPressed.Contains(Keys.Left)
            || keysPressed.Contains(Keys.A);
        bool right = InputState.UiRightPressed || keysPressed.Contains(Keys.Right)
            || keysPressed.Contains(Keys.D);
        if (left) _resultFocus.Move(-1, 0);
        if (right) _resultFocus.Move(1, 0);
        if (keysPressed.Contains(Keys.F)
            || Activated("results_soul", mousePosition, mousePressed))
            return MenuAction.EnterSoul;
        if (keysPressed.Contains(Keys.Enter)
            || Activated("retry", mousePosition, mousePressed))
            return MenuAction.Restart;
        if (keysPressed.Contains(Keys.Escape)
            || Activated("results_title", mousePosition, mousePressed)
            || InputState.ControllerBackPressed)
            return MenuAction.ReturnToTitle;
        if (!InputState.ControllerConfirmPressed)
            return MenuAction.None;
        return _resultFocus.FocusedId switch
        {
            "results_soul" => MenuAction.EnterSoul,
            "retry" => MenuAction.Restart,
            "results_title" => MenuAction.ReturnToTitle,
            _ => MenuAction.None,
        };
    }
}
