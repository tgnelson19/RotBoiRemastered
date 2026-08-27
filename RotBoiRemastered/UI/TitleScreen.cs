using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

public enum TitleAction
{
    None,
    EnterMind,
    [Obsolete("Use EnterMind; retained for save/test source compatibility.")]
    EnterSoul = EnterMind,
    Settings,
    Quit,
}

/// <summary>A quiet threshold into The Mind; detailed controls live in Settings.</summary>
public sealed class TitleScreen
{
    private readonly PresentationClock _presentationClock = new();
    private readonly UiFocusNavigator _focus = new();
    private Rectangle _soulButton;
    private Rectangle _settingsButton;
    private Rectangle _quitButton;
    private Rectangle _confirmCancel;
    private Rectangle _confirmQuit;
    private bool _quitConfirmation;
    /// <summary>
    /// Mirrors CSS :focus-visible: the navigator always has a FocusedId (so
    /// gamepad/keyboard input works from frame one), but the menu only draws
    /// that focus until the player actually asks for it by moving selection
    /// -- otherwise "Enter the Mind" would show its underline on the very
    /// first frame just for being first in tab order, which is exactly the
    /// permanent-highlight look this menu is meant to avoid.
    /// </summary>
    private bool _keyboardNavUsed;

    public void AdvancePresentation(double seconds) =>
        _presentationClock.Advance(seconds);

    /// <summary>
    /// "Deep Vigil": ROTBOI stays pinned to the true center of the frame no
    /// matter what's above or below it; the Soul Rose sits at the midpoint
    /// between the frame's top edge and the title, so it moves with either
    /// one changing; the menu is three quiet, underline-on-hover words
    /// anchored to the bottom edge instead of boxed buttons.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight,
        Point mouse, bool mouseDown)
    {
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        float animation = (float)(_presentationClock.Seconds
            * GameProfile.Profile.VisualEffectsIntensity);
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight),
            UiTheme.Void);

        int margin = Math.Max(10, (int)(20 * scale));
        var frame = new Rectangle(margin, margin, screenWidth - margin * 2,
            screenHeight - margin * 2);
        DrawQuietFrame(spriteBatch, frame, scale);

        float min = Math.Min(screenWidth, screenHeight);

        double titleTextSize = min * .076;
        double subtitleTextSize = min * .030;
        var titleFont = UiTheme.Font(titleTextSize);
        var subtitleFont = UiTheme.Font(subtitleTextSize);
        float subtitleGap = 6 * scale;
        float blockHeight = titleFont.MeasureString("ROTBOI").Y + subtitleGap
            + subtitleFont.MeasureString("R E M A S T E R E D").Y;
        float titleTop = frame.Center.Y - blockHeight / 2f;

        float availableGap = titleTop - frame.Top;
        float roseRadius = Math.Clamp(min * .22f, 60, 220);
        roseRadius = Math.Min(roseRadius, Math.Max(40f, availableGap * .48f));
        var roseCenter = new Vector2(screenWidth / 2f, frame.Top + availableGap / 2f);
        UiTheme.DrawSoulRose(spriteBatch, roseCenter, roseRadius, animation,
            .92f, GameProfile.Profile.PathMastery);

        Rectangle title = UiTheme.DrawText(spriteBatch, "ROTBOI", titleTextSize,
            UiTheme.Text, new Vector2(screenWidth / 2f, titleTop), "midtop");
        UiTheme.DrawText(spriteBatch, "R E M A S T E R E D", subtitleTextSize,
            UiTheme.Cream, new Vector2(screenWidth / 2f, title.Bottom + subtitleGap),
            "midtop");

        DrawMenuStrip(spriteBatch, frame, mouse, scale);

        if (_quitConfirmation)
            DrawQuitConfirmation(spriteBatch, frame, mouse, mouseDown, scale, animation);
    }

    /// <summary>Border plus two opposing corner brackets -- deliberately no fill, shadow, or accent rule; the frame should stay quiet.</summary>
    private static void DrawQuietFrame(SpriteBatch spriteBatch, Rectangle frame, float scale)
    {
        int borderWidth = Math.Max(1, (int)MathF.Round(scale));
        Primitives2D.RectOutline(spriteBatch, frame, UiTheme.Border, borderWidth);

        Color bracketColor = UiTheme.Lighten(UiTheme.Border, 16);
        int bracketSize = Math.Max(10, (int)(22 * scale));
        int inset = Math.Max(4, (int)(8 * scale));
        Primitives2D.Line(spriteBatch,
            new Vector2(frame.Left + inset, frame.Top + inset + bracketSize),
            new Vector2(frame.Left + inset, frame.Top + inset), bracketColor, borderWidth);
        Primitives2D.Line(spriteBatch,
            new Vector2(frame.Left + inset, frame.Top + inset),
            new Vector2(frame.Left + inset + bracketSize, frame.Top + inset), bracketColor, borderWidth);
        Primitives2D.Line(spriteBatch,
            new Vector2(frame.Right - inset - bracketSize, frame.Bottom - inset),
            new Vector2(frame.Right - inset, frame.Bottom - inset), bracketColor, borderWidth);
        Primitives2D.Line(spriteBatch,
            new Vector2(frame.Right - inset, frame.Bottom - inset),
            new Vector2(frame.Right - inset, frame.Bottom - inset - bracketSize), bracketColor, borderWidth);
    }

    /// <summary>Three ghost-style words anchored to the frame's bottom edge, above a thin divider.</summary>
    private void DrawMenuStrip(SpriteBatch spriteBatch, Rectangle frame, Point mouse, float scale)
    {
        double primarySize = 13 * scale;
        double secondarySize = primarySize;
        Vector2 primaryMeasured = UiTheme.Font(primarySize).MeasureString("ENTER THE MIND");
        Vector2 settingsMeasured = UiTheme.Font(secondarySize).MeasureString("SETTINGS");
        Vector2 quitMeasured = UiTheme.Font(secondarySize).MeasureString("QUIT");

        float gap = Math.Max(36, 64 * scale);
        float totalWidth = primaryMeasured.X + settingsMeasured.X + quitMeasured.X + gap * 2;
        float centerY = frame.Bottom - Math.Max(20, frame.Height * .07f);
        float x = frame.Center.X - totalWidth / 2f;

        float dividerY = centerY - Math.Max(primaryMeasured.Y, settingsMeasured.Y) / 2f
            - Math.Max(10, 14 * scale);
        Primitives2D.Line(spriteBatch, new Vector2(frame.Left, dividerY),
            new Vector2(frame.Right, dividerY), new Color(28, 33, 43), Math.Max(1, (int)scale));

        _focus.BeginFrame();
        _soulButton = DrawGhostButton(spriteBatch, "soul", x + primaryMeasured.X / 2f, centerY,
            "ENTER THE MIND", primarySize, mouse, primary: true, scale);
        x += primaryMeasured.X + gap;
        _settingsButton = DrawGhostButton(spriteBatch, "settings", x + settingsMeasured.X / 2f, centerY,
            "SETTINGS", secondarySize, mouse, primary: false, scale);
        x += settingsMeasured.X + gap;
        _quitButton = DrawGhostButton(spriteBatch, "quit", x + quitMeasured.X / 2f, centerY,
            "QUIT", secondarySize, mouse, primary: false, scale);
    }

    /// <summary>
    /// Muted at rest -- including while it merely holds keyboard/controller
    /// focus, so "Enter the Mind" doesn't stand out from Settings/Quit just
    /// by being first in tab order. Only an actual mouse hover reveals color
    /// and an underline, and only then does the primary button get its
    /// distinct purple underline instead of the shared cream one.
    /// </summary>
    private Rectangle DrawGhostButton(SpriteBatch spriteBatch, string id, float centerX, float centerY,
        string label, double textSize, Point mouse, bool primary, float scale)
    {
        var font = UiTheme.Font(textSize);
        Vector2 measured = font.MeasureString(label);
        var hitRect = new Rectangle(
            (int)(centerX - measured.X / 2f - 14 * scale),
            (int)(centerY - measured.Y / 2f - 8 * scale),
            (int)MathF.Ceiling(measured.X + 28 * scale),
            (int)MathF.Ceiling(measured.Y + 16 * scale));

        bool hovered = hitRect.Contains(mouse);
        bool focused = _keyboardNavUsed && _focus.IsFocused(id);

        Color textColor = hovered
            ? (primary ? UiTheme.Cream : UiTheme.Text)
            : focused ? UiTheme.Text : UiTheme.Muted;
        UiTheme.DrawText(spriteBatch, label, textSize, textColor,
            new Vector2(centerX, centerY), "center");

        if (hovered || focused)
        {
            Color underline = hovered && primary ? UiTheme.Purple : UiTheme.Cream;
            float underlineY = centerY + measured.Y / 2f + 3 * scale;
            Primitives2D.Line(spriteBatch,
                new Vector2(centerX - measured.X / 2f, underlineY),
                new Vector2(centerX + measured.X / 2f, underlineY), underline,
                Math.Max(1, (int)scale));
        }

        _focus.Register(id, hitRect);
        return hitRect;
    }

    private void DrawQuitConfirmation(SpriteBatch spriteBatch, Rectangle frame,
        Point mouse, bool mouseDown, float scale, float animation)
    {
        UiTheme.DrawScrim(spriteBatch, frame);
        int width = Math.Min(frame.Width - 20, Math.Max(240, (int)(410 * scale)));
        int height = Math.Min(frame.Height - 20, Math.Max(130, (int)(165 * scale)));
        var modal = new Rectangle(frame.Center.X - width / 2,
            frame.Center.Y - height / 2, width, height);
        UiTheme.DrawFramedPanel(spriteBatch, modal,
            UiTheme.PanelRaised, UiTheme.Red, 8);
        UiTheme.DrawText(spriteBatch, "QUIT THE GAME?", 16 * scale, UiTheme.Text,
            new Vector2(modal.Center.X, modal.Y + 20 * scale), "midtop");
        UiTheme.DrawText(spriteBatch, "Your profile will be saved before closing.",
            8 * scale, UiTheme.Muted,
            new Vector2(modal.Center.X, modal.Y + 52 * scale), "midtop");
        int pad = Math.Max(8, (int)(12 * scale));
        int h = Math.Max(34, (int)(44 * scale));
        _confirmCancel = new Rectangle(modal.X + pad, modal.Bottom - pad - h,
            (modal.Width - pad * 3) / 2, h);
        _confirmQuit = new Rectangle(_confirmCancel.Right + pad, _confirmCancel.Y,
            _confirmCancel.Width, h);
        UiTheme.DrawButton(spriteBatch, _confirmCancel, "CANCEL", mouse, mouseDown,
            true, UiTheme.Border, null, 9 * scale);
        UiTheme.DrawButton(spriteBatch, _confirmQuit, "QUIT", mouse, mouseDown,
            true, UiTheme.Red, null, 9 * scale);
    }

    public TitleAction HandleInput(IReadOnlySet<Keys> keysPressed, Point mouse,
        bool mousePressed)
    {
        if (_quitConfirmation)
        {
            if (keysPressed.Contains(Keys.Escape) || InputState.ControllerBackPressed
                || mousePressed && _confirmCancel.Contains(mouse))
            {
                _quitConfirmation = false;
                return TitleAction.None;
            }
            if (keysPressed.Contains(Keys.Enter) || InputState.ControllerConfirmPressed
                || mousePressed && _confirmQuit.Contains(mouse))
            {
                _quitConfirmation = false;
                return TitleAction.Quit;
            }
            return TitleAction.None;
        }

        string? hovered = _focus.At(mouse);
        if (mousePressed && hovered is not null) _focus.Focus(hovered);
        bool up = InputState.UiUpPressed || keysPressed.Contains(Keys.Up)
            || keysPressed.Contains(Keys.W);
        bool down = InputState.UiDownPressed || keysPressed.Contains(Keys.Down)
            || keysPressed.Contains(Keys.S);
        bool left = InputState.UiLeftPressed || keysPressed.Contains(Keys.Left)
            || keysPressed.Contains(Keys.A);
        bool right = InputState.UiRightPressed || keysPressed.Contains(Keys.Right)
            || keysPressed.Contains(Keys.D);
        if (up || down || left || right) _keyboardNavUsed = true;
        if (up) _focus.Move(0, -1);
        if (down) _focus.Move(0, 1);
        if (left) _focus.Move(-1, 0);
        if (right) _focus.Move(1, 0);

        if (keysPressed.Contains(Keys.Escape) || InputState.ControllerBackPressed)
        {
            _quitConfirmation = true;
            return TitleAction.None;
        }
        if (keysPressed.Contains(Keys.F)) return TitleAction.EnterMind;

        bool confirm = keysPressed.Contains(Keys.Enter) || keysPressed.Contains(Keys.Space)
            || InputState.ControllerConfirmPressed || mousePressed;
        if (!confirm) return TitleAction.None;
        string? activated = mousePressed ? hovered : _focus.FocusedId ?? "soul";
        return activated switch
        {
            "soul" => TitleAction.EnterMind,
            "settings" => TitleAction.Settings,
            "quit" => OpenQuitConfirmation(),
            _ => TitleAction.None,
        };
    }

    private TitleAction OpenQuitConfirmation()
    {
        _quitConfirmation = true;
        return TitleAction.None;
    }
}
