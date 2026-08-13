using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.UI;

/// <summary>A reusable Dissonance-inspired title band for entering a world or mode.</summary>
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
        int y = height / 2 - bandHeight / 2;
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, y, width, bandHeight), UiTheme.Void * (.88f * alpha));
        float reveal = MathHelper.SmoothStep(0, 1, (float)Math.Clamp(elapsed / .7, 0, 1));
        float lineWidth = width * .32f * reveal;
        Vector2 center = new(width / 2f, height / 2f - 15 * scale);
        Primitives2D.Line(spriteBatch, center + new Vector2(-lineWidth, 47 * scale),
            center + new Vector2(lineWidth, 47 * scale), Accent * alpha, Math.Max(2, (int)(3 * scale)));
        int jitter = ((int)(elapsed * 20) % 13 == 0) ? Math.Max(1, (int)(2 * scale)) : 0;
        UiTheme.DrawText(spriteBatch, Title.ToUpperInvariant(), 42 * scale, UiTheme.Ink * alpha,
            center + new Vector2(5 * scale, 6 * scale), "center");
        UiTheme.DrawText(spriteBatch, Title.ToUpperInvariant(), 42 * scale, Accent * alpha,
            center + new Vector2(jitter, 0), "center");
        UiTheme.DrawText(spriteBatch, Flavor, 12 * scale, UiTheme.Cream * alpha,
            center + new Vector2(0, 77 * scale), "center");
    }
}
