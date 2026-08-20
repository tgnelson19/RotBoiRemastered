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

internal readonly record struct DeathBannerLayout(
    Rectangle Banner,
    Rectangle EnterMind,
    Rectangle Retry,
    Rectangle Title);

/// <summary>Settings-first pause menu and immutable end-of-run debrief.</summary>
public sealed class Menus
{
    private readonly PresentationClock _presentationClock = new();
    private readonly SettingsMenu _settingsMenu = new();
    private readonly UiFocusNavigator _resultFocus = new();
    private readonly Dictionary<string, Rectangle> _buttons = new();

    public void AdvancePresentation(double seconds) =>
        _presentationClock.Advance(seconds);

    public void BeginResults() => _presentationClock.Reset();

    internal float ResultsPresentationSeconds => _presentationClock.Seconds;

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
        bool success = RunOutcomes.IsSuccess(report.Outcome);
        if (!success)
        {
            DrawDeathResults(spriteBatch, screenWidth, screenHeight, report,
                mousePosition, mouseDown);
            return;
        }
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
        ResultButton("results_soul", root.X + margin, "ENTER MIND",
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

    internal static DeathBannerLayout CalculateDeathBannerLayout(
        int screenWidth, int screenHeight)
    {
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        int margin = Math.Max(16, (int)(24 * scale));
        int width = Math.Min(screenWidth - margin * 2,
            Math.Max(340, (int)(760 * scale)));
        int height = Math.Min(screenHeight - margin * 2,
            Math.Max(230, (int)(300 * scale)));
        var banner = new Rectangle((screenWidth - width) / 2,
            (screenHeight - height) / 2, width, height);
        int pad = Math.Max(12, (int)(18 * scale));
        int gap = Math.Max(6, (int)(9 * scale));
        int buttonHeight = Math.Max(38, (int)(48 * scale));
        int buttonWidth = (banner.Width - pad * 2 - gap * 2) / 3;
        int y = banner.Bottom - pad - buttonHeight;
        var enterMind = new Rectangle(banner.X + pad, y,
            buttonWidth, buttonHeight);
        var retry = new Rectangle(enterMind.Right + gap, y,
            buttonWidth, buttonHeight);
        var title = new Rectangle(retry.Right + gap, y,
            banner.Right - pad - retry.Right - gap, buttonHeight);
        return new DeathBannerLayout(banner, enterMind, retry, title);
    }

    private void DrawDeathResults(SpriteBatch spriteBatch, int screenWidth,
        int screenHeight, RunResultReport report, Point mousePosition,
        bool mouseDown)
    {
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        float elapsed = _presentationClock.Seconds;
        float reveal = MathHelper.SmoothStep(0f, 1f,
            Math.Clamp(elapsed / .72f, 0f, 1f));
        float breathe = .5f + .5f * MathF.Sin(elapsed * 1.8f);
        var layout = CalculateDeathBannerLayout(screenWidth, screenHeight);
        Rectangle banner = layout.Banner;

        // A translucent veil quiets the still-living combat scene without
        // replacing it. The banner remains the only opaque UI mass.
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(0, 0, screenWidth, screenHeight),
            UiTheme.Void * (.12f + reveal * .34f));
        var shadow = banner;
        shadow.Offset(8, 10);
        Primitives2D.FillRect(spriteBatch, shadow,
            UiTheme.Shadow * (.72f * reveal));
        UiTheme.DrawFramedPanel(spriteBatch, banner,
            UiTheme.Panel * (.9f * reveal),
            Color.Lerp(UiTheme.Purple, UiTheme.Red, .32f + breathe * .12f)
                * reveal, 7);

        float lineHalf = banner.Width * .31f * reveal;
        float lineY = banner.Y + 104 * scale;
        Primitives2D.Line(spriteBatch,
            new Vector2(banner.Center.X - lineHalf, lineY),
            new Vector2(banner.Center.X + lineHalf, lineY),
            UiTheme.Purple * (.72f * reveal),
            Math.Max(2, (int)(3 * scale)));
        int jitter = (int)(elapsed * 18f) % 17 == 0
            ? Math.Max(1, (int)(2 * scale)) : 0;
        Vector2 title = new(banner.Center.X, banner.Y + 27 * scale);
        UiTheme.DrawText(spriteBatch, "RETURNING TO THE VOID",
            28 * scale, UiTheme.Ink * reveal,
            title + new Vector2(4 * scale, 5 * scale), "midtop");
        UiTheme.DrawText(spriteBatch, "RETURNING TO THE VOID",
            28 * scale,
            Color.Lerp(UiTheme.Purple, UiTheme.Cream, .35f + breathe * .16f)
                * reveal,
            title + new Vector2(jitter, 0), "midtop");
        UiTheme.DrawText(spriteBatch,
            "THE MIND REMEMBERS WHAT THE BODY COULD NOT",
            9 * scale, UiTheme.Cream * (.82f * reveal),
            new Vector2(banner.Center.X, banner.Y + 79 * scale), "midtop");

        string time = $"{(int)(report.Seconds / 60):D2}:"
            + $"{(int)(report.Seconds % 60):D2}";
        UiTheme.DrawText(spriteBatch,
            $"{report.PathTitle}  //  LEVEL {report.Level:D2}  //  "
                + $"{report.Kills} KILLS  //  {time}",
            8 * scale,
            GamePaths.PathsByKey.GetValueOrDefault(report.PathKey)?.Accent
                ?? UiTheme.Cream,
            new Vector2(banner.Center.X, banner.Y + 125 * scale), "midtop");
        UiTheme.DrawText(spriteBatch,
            report.LostLoadout.Count > 0
                ? "THE LOADOUT IS LOST. THE PATH REMAINS."
                : "THE PATH REMAINS.",
            8 * scale, UiTheme.Muted * reveal,
            new Vector2(banner.Center.X, banner.Y + 150 * scale), "midtop");

        RegisterDeathButton("results_soul", layout.EnterMind, "ENTER MIND",
            UiTheme.Purple, "F");
        RegisterDeathButton("retry", layout.Retry, "PLAY AGAIN",
            UiTheme.Green, "ENTER");
        RegisterDeathButton("results_title", layout.Title, "TITLE",
            UiTheme.Red, "ESC");

        void RegisterDeathButton(string id, Rectangle rect, string label,
            Color accent, string hint)
        {
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
            $"LEVEL {report.Level:D2}   KILLS {report.Kills}   UPGRADES {report.UpgradeCount}",
            8 * scale, accent,
            new Vector2(panel.X + pad, panel.Y + 34 * scale));
        string field = TimeLabel(report.FieldSeconds);
        string bosses = TimeLabel(report.BossSeconds);
        UiTheme.DrawText(spriteBatch,
            $"TIME {time}   FIELD {field}   BOSSES {bosses}   {RunPacing.TargetLabel}",
            7 * scale,
            report.PaceBand == RunPaceBand.OnTarget ? UiTheme.Green : UiTheme.Muted,
            new Vector2(panel.X + pad, panel.Y + 50 * scale));
        UiTheme.DrawText(spriteBatch, report.BuildIdentity, 16 * scale,
            UiTheme.Purple, new Vector2(panel.X + pad, panel.Y + 69 * scale));
        string families = report.DominantFamilies.Count == 0
            ? "NO DOMINANT UPGRADE FAMILY"
            : string.Join("  //  ", report.DominantFamilies);
        UiTheme.DrawText(spriteBatch, families, 7 * scale, UiTheme.Muted,
            new Vector2(panel.X + pad, panel.Y + 96 * scale));

        int rewardY = panel.Y + Math.Max((int)(112 * scale), panel.Height / 2);
        UiTheme.DrawText(spriteBatch, "REWARDS & PROGRESSION", 10 * scale,
            UiTheme.Cream, new Vector2(panel.X + pad, rewardY));
        UiTheme.DrawText(spriteBatch,
            $"MIND TOKENS  +{report.MindTokenReward}", 9 * scale,
            report.MindTokenReward > 0 ? UiTheme.Gold : UiTheme.Muted,
            new Vector2(panel.X + pad, rewardY + 25 * scale));
        UiTheme.DrawText(spriteBatch,
            $"PATH MASTERY  {report.PathMasteryBefore}  ->  {report.PathMasteryAfter}",
            8 * scale, UiTheme.Text,
            new Vector2(panel.X + pad, rewardY + 47 * scale));
        bool unlocked = report.NewGamePlusAfter > report.NewGamePlusBefore;
        string ng = unlocked
            ? $"NG+ UNLOCKED  {report.NewGamePlusBefore}  ->  {report.NewGamePlusAfter}"
            : $"NG+ UNLOCK TIER  {report.NewGamePlusAfter}";
        UiTheme.DrawText(spriteBatch, ng, 8 * scale,
            unlocked ? UiTheme.Green : UiTheme.Muted,
            new Vector2(panel.X + pad, rewardY + 69 * scale));
        if (report.CompletedQuests.Count > 0)
        {
            const int maxNamed = 2;
            string quests = "QUEST COMPLETE  //  " + string.Join("  //  ",
                report.CompletedQuests.Take(maxNamed)
                    .Select(quest => $"{quest.Name.ToUpperInvariant()} +{quest.Reward}"));
            if (report.CompletedQuests.Count > maxNamed)
                quests += $"  //  +{report.CompletedQuests.Count - maxNamed} MORE";
            UiTheme.DrawText(spriteBatch, quests, 8 * scale, UiTheme.Gold,
                new Vector2(panel.X + pad, rewardY + 91 * scale));
        }
    }

    private static string TimeLabel(double seconds) =>
        $"{(int)Math.Max(0, seconds) / 60:D2}:{(int)Math.Max(0, seconds) % 60:D2}";

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
