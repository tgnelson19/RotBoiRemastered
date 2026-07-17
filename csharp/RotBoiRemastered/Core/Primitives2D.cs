using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RotBoiRemastered.Core;

/// <summary>
/// Low-level 2D shape drawing that MonoGame doesn't provide out of the box --
/// pygame.draw.rect/line have no direct SpriteBatch equivalent. Backed by a
/// single reusable 1x1 white pixel texture, tinted and stretched per call;
/// this is the standard MonoGame technique for primitive shapes. Needed by
/// UiTheme.cs now, and will be needed by every future rendering module
/// (background.py, bossTypes.py, etc. all lean on pygame.draw heavily).
/// </summary>
public static class Primitives2D
{
    private static Texture2D? _pixel;

    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    private static Texture2D Pixel => _pixel ?? throw new InvalidOperationException(
        "Primitives2D.Initialize(GraphicsDevice) must be called once (from LoadContent) before drawing.");

    public static void FillRect(SpriteBatch spriteBatch, Rectangle rect, Color color)
    {
        spriteBatch.Draw(Pixel, rect, color);
    }

    /// <summary>Border only, matching pygame.draw.rect(..., width>0) -- draws the four edges.</summary>
    public static void RectOutline(SpriteBatch spriteBatch, Rectangle rect, Color color, int width)
    {
        width = Math.Max(1, width);
        FillRect(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, width), color); // top
        FillRect(spriteBatch, new Rectangle(rect.X, rect.Bottom - width, rect.Width, width), color); // bottom
        FillRect(spriteBatch, new Rectangle(rect.X, rect.Y, width, rect.Height), color); // left
        FillRect(spriteBatch, new Rectangle(rect.Right - width, rect.Y, width, rect.Height), color); // right
    }

    public static void Line(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, int width)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length < 0.0001f)
            return;
        float angle = MathF.Atan2(delta.Y, delta.X);
        spriteBatch.Draw(Pixel, start, null, color, angle, Vector2.Zero,
            new Vector2(length, Math.Max(1, width)), SpriteEffects.None, 0f);
    }
}
