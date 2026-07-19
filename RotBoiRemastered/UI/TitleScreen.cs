using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

/// <summary>What HandleInput determined should happen, matching MenuAction's return-a-result shape.</summary>
public enum TitleAction { None, EnterSoul, Settings, Quit }

/// <summary>
/// The title screen: entry point, field manual, best-run tag. Ported from
/// character.py's runTheTitleScreen() (one big function in Python too -- no
/// separate helpers to extract). Follows Menus.cs's shape (Draw/HandleInput
/// pair, no module globals) rather than mutating state directly -- the
/// caller (Core/RotBoiGame.cs) is responsible for actually activating a path
/// and constructing/resetting a GameSession.
///
/// Path selection no longer happens here: The Soul is now the hub every run
/// launches from, with one equally-spaced portal per GamePaths entry (see
/// SoulHub.DrawPathPortals/NearbyPathPortal) standing in for the old
/// title-screen selector grid + per-path "ENTER {TITLE}" button. This screen
/// only gets you into the Soul.
///
/// Dropped vs. Python: the best-run tag reads GameProfile.Profile.BestLevel/
/// BestKills directly rather than `max(cS.highestLevel, profile["best_level"])`
/// -- GameProfile.RecordRun already updates BestLevel synchronously on every
/// defeat/completion, so the profile value is never stale by the time this
/// screen is shown again; there's no live "current session" value that could
/// ever exceed it here.
/// </summary>
public sealed class TitleScreen
{
    private Rectangle _soulButton;
    private Rectangle _settingsButton;

    private static void DrawGrid(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
    {
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), UiTheme.Void);
        int grid = Math.Max(28, Math.Min(screenWidth, screenHeight) / 28);
        var gridColor = new Color(23, 27, 35);
        for (int x = 0; x < screenWidth; x += grid)
            Primitives2D.Line(spriteBatch, new Vector2(x, 0), new Vector2(x, screenHeight), gridColor, 1);
        for (int y = 0; y < screenHeight; y += grid)
            Primitives2D.Line(spriteBatch, new Vector2(0, y), new Vector2(screenWidth, y), gridColor, 1);
    }

    public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Point mousePosition, bool mouseDown)
    {
        DrawGrid(spriteBatch, screenWidth, screenHeight);

        float scale = Math.Min(screenWidth, screenHeight);
        // Text sizes below (scale * .095, etc.) already pick up TextSize
        // through UiTheme.Font internally. Layout (button/panel widths,
        // heights, gaps) only ever scaled with GuiScale via uiScale --
        // DrawButton's own shrink-to-fit loop has a floor (raw size 9) that
        // still renders far too wide once a high TextSize multiplies it, so
        // without growing the boxes too, a long button label could overlap
        // its own border well before the text could shrink enough to fit.
        // uiScale now folds in the same growth-only factor
        // InformationSheet.TextGrowthFactor uses, so layout keeps pace with
        // whatever TextSize the text itself is already rendering at. The
        // extra headroom factor is capped well below MaxTextScale (2.0) --
        // DisplayScale already folds in GuiScale (up to 1.3x), and at max
        // GuiScale *and* max TextSize the two multiplied together (2.6x)
        // pushed the whole stack past the bottom of the screen. Capping
        // the headroom side of that product keeps both sliders maxed out
        // simultaneously from overflowing.
        float uiScale = UiTheme.DisplayScale(screenWidth, screenHeight) * (float)Math.Max(1.0, Math.Min(1.3, UiTheme.TextScaleMultiplier()));
        float contentWidth = Math.Min(screenWidth * .68f, 980 * uiScale);
        float left = (screenWidth - contentWidth) / 2f;
        // Free-floating title/subtitle/tagline text has no container to grow
        // with it, so a high TextSize can make one line's rendered height
        // alone exceed the fixed gap to the next fraction-of-screen-height
        // anchor below it. Math.Max(originalFraction, previousBottom + gap)
        // falls back to that measured bottom only once it would actually
        // overlap -- at default TextSize this is always the original
        // fraction (unchanged from before), so nothing shifts for anyone
        // who hasn't touched the slider. Capped on the high end too (at
        // roughly double its floor) so the cascade itself can't compound
        // into an overflow no matter how large uiScale gets.
        float gap = Math.Min(8 * uiScale, screenHeight * .018f);
        var titleRect = UiTheme.DrawText(spriteBatch, "ROTBOI", scale * .095, UiTheme.Text, new Vector2(screenWidth / 2f, screenHeight * .12f), "midtop");
        float subtitleY = Math.Max(screenHeight * .245f, titleRect.Bottom + gap);
        var subtitleRect = UiTheme.DrawText(spriteBatch, "R E M A S T E R E D", scale * .026, UiTheme.Cream, new Vector2(screenWidth / 2f, subtitleY), "midtop");
        float taglineY = Math.Max(screenHeight * .305f, subtitleRect.Bottom + gap);
        var taglineRect = UiTheme.DrawText(spriteBatch, "EVERY PATH WAITS IN THE SOUL.", scale * .019, UiTheme.Muted, new Vector2(screenWidth / 2f, taglineY), "midtop");

        // The old path-selector grid + per-path "ENTER {TITLE}" button lived
        // here; path choice now happens by walking up to one of the Soul's
        // equally-spaced path portals (see SoulHub.DrawPathPortals), so this
        // screen's only job is getting the player into the Soul.
        float soulButtonY = Math.Max(screenHeight * .5f, taglineRect.Bottom + gap * 4);
        float soulButtonHeight = Math.Min(Math.Max(58 * uiScale, scale * .068f), scale * .098f);
        _soulButton = new Rectangle((int)(left + contentWidth * .22f), (int)soulButtonY,
            (int)(contentWidth * .56f), (int)soulButtonHeight);
        UiTheme.DrawButton(spriteBatch, _soulButton, "ENTER THE SOUL", mousePosition, mouseDown, true,
            UiTheme.Purple, "SPACE / F", (int)(scale * .019f));

        float settingsButtonY = Math.Max(screenHeight * .615f, soulButtonY + soulButtonHeight + gap * 2);
        _settingsButton = new Rectangle((int)(left + contentWidth * .35f), (int)settingsButtonY,
            (int)(contentWidth * .30f), (int)Math.Min(Math.Max(42 * uiScale, scale * .052f), scale * .078f));
        UiTheme.DrawButton(spriteBatch, _settingsButton, "SETTINGS", mousePosition, mouseDown, true,
            UiTheme.Blue, null, (int)(scale * .014f));

        float controlsY = Math.Max(screenHeight * .665f, _settingsButton.Bottom + gap * 3);
        var controlsRect = new Rectangle((int)left, (int)controlsY, (int)contentWidth,
            (int)Math.Min(Math.Max(116 * uiScale, screenHeight * .15f), screenHeight * .21f));
        UiTheme.DrawPanel(spriteBatch, controlsRect, UiTheme.Panel, UiTheme.Border, shadow: 6);
        UiTheme.DrawText(spriteBatch, "FIELD MANUAL", scale * .018, UiTheme.Text,
            new Vector2(controlsRect.X + 18 * uiScale, controlsRect.Y + 14 * uiScale));
        var controls = new[] { ("WASD", "MOVE"), ("MOUSE", "AIM + FIRE"), ("SPACE", "DASH"), ("Q / E", "ROTATE"), ("I", "AUTOFIRE") };
        float cellWidth = (controlsRect.Width - 36 * uiScale) / controls.Length;
        for (int index = 0; index < controls.Length; index++)
        {
            var (key, action) = controls[index];
            float centerX = controlsRect.X + 18 * uiScale + cellWidth * (index + .5f);
            int keyWidth = (int)Math.Min(82 * uiScale, cellWidth - 12 * uiScale);
            var keyRect = new Rectangle((int)(centerX - keyWidth / 2f), (int)(controlsRect.Center.Y - 3 - 17 * uiScale), keyWidth, (int)(34 * uiScale));
            Primitives2D.FillRect(spriteBatch, keyRect, UiTheme.Ink);
            Primitives2D.RectOutline(spriteBatch, keyRect, UiTheme.Blue, 2);
            UiTheme.DrawText(spriteBatch, key, scale * .014, UiTheme.Blue, new Vector2(keyRect.Center.X, keyRect.Center.Y), "center");
            UiTheme.DrawText(spriteBatch, action, scale * .011, UiTheme.Muted, new Vector2(centerX, keyRect.Bottom + 11 * uiScale), "midtop");
        }

        int bestLevel = GameProfile.Profile.BestLevel;
        string recordLabel = bestLevel <= 0 ? "NO RUNS LOGGED" : $"BEST RUN  //  LEVEL {bestLevel:D2}  //  {GameProfile.Profile.BestKills} KILLS";
        UiTheme.DrawTag(spriteBatch, recordLabel, new Vector2(left, screenHeight * .87f), bestLevel > 0 ? UiTheme.Gold : UiTheme.Border, scale * .012);
        UiTheme.DrawText(spriteBatch, "SPACE / F  ENTER THE SOUL    ESC  QUIT", scale * .012, UiTheme.Muted,
            new Vector2(left + contentWidth, screenHeight * .875f), "topright");
    }

    public TitleAction HandleInput(IReadOnlySet<Keys> keysPressed, Point mousePosition, bool mousePressed)
    {
        if (keysPressed.Contains(Keys.Space) || keysPressed.Contains(Keys.F) || (_soulButton.Contains(mousePosition) && mousePressed))
            return TitleAction.EnterSoul;
        if (_settingsButton.Contains(mousePosition) && mousePressed)
            return TitleAction.Settings;
        if (keysPressed.Contains(Keys.Escape))
            return TitleAction.Quit;
        return TitleAction.None;
    }
}
