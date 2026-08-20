using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.UI;

/// <summary>
/// A reusable title card for entering a world or mode (a Sense/Path, the
/// Dungeon, Aphantasia, the Body/Soul campaign). Framed with the same
/// bracket-corner + cycling per-Sense segment chrome as GameSession's boss
/// name banner, floor title banner, and run-complete banner (all built on
/// <see cref="UiTheme.DrawLivingPanel"/>/<see cref="UiTheme.DrawCompositePanel"/>)
/// instead of a one-off flat band, so every "title card" moment in the game
/// reads as one family. The headline is a fixed, stable engrave (an Ink
/// drop-shadow behind a solid accent-colored copy) -- no per-frame jitter.
/// </summary>
public sealed class ModeEntrySplash
{
    public const double Duration = 3.6;
    public string Title { get; private set; } = "";
    public string Flavor { get; private set; } = "";
    public Color Accent { get; private set; } = UiTheme.Purple;
    public double Remaining { get; private set; }
    public bool Active => Remaining > 0;

    public void Show(string title, string flavor, Color accent)
    {
        Title = title;
        Flavor = flavor;
        Accent = accent;
        Remaining = Duration;
    }

    public void Update(double seconds) => Remaining = Math.Max(0, Remaining - Math.Min(.05, seconds));

    public void Draw(SpriteBatch spriteBatch, int width, int height)
    {
        if (!Active) return;
        double elapsed = Duration - Remaining;
        float alpha = (float)Math.Clamp(Math.Min(elapsed / .45, Remaining / .75), 0, 1);
        float scale = UiTheme.DisplayScale(width, height);
        int bandHeight = Math.Max(150, (int)(height * .31f));
        var band = new Rectangle(0, height / 2 - bandHeight / 2, width, bandHeight);

        UiTheme.DrawCompositePanel(spriteBatch, band, (float)elapsed,
            fill: UiTheme.Void * (.88f * alpha), border: Accent * alpha, shadow: 0);

        Vector2 center = new(width / 2f, height / 2f - 15 * scale);
        UiTheme.DrawText(spriteBatch, Title.ToUpperInvariant(), 42 * scale, UiTheme.Ink * alpha,
            center + new Vector2(5 * scale, 6 * scale), "center");
        UiTheme.DrawText(spriteBatch, Title.ToUpperInvariant(), 42 * scale, Accent * alpha,
            center, "center");
        UiTheme.DrawText(spriteBatch, Flavor, 12 * scale, UiTheme.Cream * alpha,
            center + new Vector2(0, 77 * scale), "center");
    }
}
