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

    public void AdvancePresentation(double seconds) =>
        _presentationClock.Advance(seconds);

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
        UiTheme.DrawFramedPanel(spriteBatch, frame,
            new Color(9, 11, 16), UiTheme.Border, 8);

        float min = Math.Min(screenWidth, screenHeight);
        float roseRadius = Math.Clamp(min * .11f, 30, 110);
        var roseCenter = new Vector2(screenWidth / 2f,
            Math.Max(frame.Y + roseRadius + 12 * scale, screenHeight * .22f));
        UiTheme.DrawSoulRose(spriteBatch, roseCenter, roseRadius, animation,
            .68f, GameProfile.Profile.PathMastery);

        float titleY = roseCenter.Y + roseRadius * .78f;
        Rectangle title = UiTheme.DrawText(spriteBatch, "ROTBOI", min * .076,
            UiTheme.Text, new Vector2(screenWidth / 2f, titleY), "midtop");
        UiTheme.DrawText(spriteBatch, "R E M A S T E R E D", min * .019,
            UiTheme.Cream, new Vector2(screenWidth / 2f, title.Bottom + 3 * scale),
            "midtop");

        float width = Math.Min(screenWidth * .52f, 540 * scale);
        width = Math.Max(210, width);
        int buttonHeight = Math.Max(34, (int)(52 * scale));
        int gap = Math.Max(6, (int)(9 * scale));
        int firstY = Math.Max((int)(screenHeight * .53f), title.Bottom + (int)(34 * scale));
        int left = (screenWidth - (int)width) / 2;
        _soulButton = new Rectangle(left, firstY, (int)width, buttonHeight);
        int smallWidth = ((int)width - gap) / 2;
        _settingsButton = new Rectangle(left, _soulButton.Bottom + gap,
            smallWidth, buttonHeight);
        _quitButton = new Rectangle(_settingsButton.Right + gap,
            _settingsButton.Y, smallWidth, buttonHeight);

        _focus.BeginFrame();
        _focus.Register("soul", _soulButton);
        _focus.Register("settings", _settingsButton);
        _focus.Register("quit", _quitButton);
        DrawButton(spriteBatch, "soul", _soulButton, "ENTER THE MIND", mouse,
            mouseDown, UiTheme.Purple, "ENTER", 13 * scale);
        DrawButton(spriteBatch, "settings", _settingsButton, "SETTINGS", mouse,
            mouseDown, UiTheme.Blue, null, 10 * scale);
        DrawButton(spriteBatch, "quit", _quitButton, "QUIT", mouse,
            mouseDown, UiTheme.Red, null, 10 * scale);

        int bestLevel = GameProfile.Profile.BestLevel;
        string best = bestLevel <= 0 ? "NO RUNS LOGGED" :
            $"BEST RUN  //  LEVEL {bestLevel:D2}  //  {GameProfile.Profile.BestKills} KILLS";
        UiTheme.DrawText(spriteBatch, best, 8 * scale,
            bestLevel > 0 ? UiTheme.Gold : UiTheme.Muted,
            new Vector2(screenWidth / 2f,
                Math.Min(frame.Bottom - 12 * scale, _settingsButton.Bottom + 24 * scale)),
            "midtop");

        if (_quitConfirmation)
            DrawQuitConfirmation(spriteBatch, frame, mouse, mouseDown, scale, animation);
    }

    private void DrawButton(SpriteBatch spriteBatch, string id, Rectangle rect,
        string label, Point mouse, bool mouseDown, Color accent, string? hint,
        double size)
    {
        UiTheme.DrawButton(spriteBatch, rect, label, mouse, mouseDown, true,
            accent, hint, size);
        if (_focus.IsFocused(id))
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream, 2);
    }

    private void DrawQuitConfirmation(SpriteBatch spriteBatch, Rectangle frame,
        Point mouse, bool mouseDown, float scale, float animation)
    {
        Primitives2D.FillRect(spriteBatch, frame, new Color(0, 0, 0, 190));
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
