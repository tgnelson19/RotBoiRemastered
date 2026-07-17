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

    /// <summary>Connects consecutive points, matching pygame.draw.lines(..., closed, points, width).</summary>
    public static void Polyline(SpriteBatch spriteBatch, IReadOnlyList<Vector2> points, bool closed, Color color, int width)
    {
        for (int i = 0; i + 1 < points.Count; i++)
            Line(spriteBatch, points[i], points[i + 1], color, width);
        if (closed && points.Count > 2)
            Line(spriteBatch, points[^1], points[0], color, width);
    }

    /// <summary>Filled circle via horizontal scanline strips -- no mesh/vertex renderer available through SpriteBatch alone.</summary>
    public static void FillCircle(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
    {
        int r = (int)MathF.Round(radius);
        for (int y = -r; y <= r; y++)
        {
            int halfWidth = (int)MathF.Round(MathF.Sqrt(Math.Max(0, r * r - y * y)));
            if (halfWidth <= 0)
                continue;
            FillRect(spriteBatch, new Rectangle((int)center.X - halfWidth, (int)center.Y + y, halfWidth * 2, 1), color);
        }
    }

    public static void CircleOutline(SpriteBatch spriteBatch, Vector2 center, float radius, Color color, int width, int segments = 48)
    {
        var rect = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2), (int)(radius * 2));
        Arc(spriteBatch, rect, 0, MathF.Tau, color, width, segments);
    }

    /// <summary>Filled ellipse (pygame.draw.ellipse with width=0) bounded by rect, via horizontal scanline strips.</summary>
    public static void FillEllipse(SpriteBatch spriteBatch, Rectangle rect, Color color)
    {
        float rx = rect.Width / 2f, ry = rect.Height / 2f;
        Vector2 center = new(rect.X + rx, rect.Y + ry);
        int ryInt = (int)MathF.Round(ry);
        for (int y = -ryInt; y <= ryInt; y++)
        {
            if (ry < 0.0001f)
                continue;
            float ratio = 1f - (y * y) / (ry * ry);
            if (ratio < 0)
                continue;
            int halfWidth = (int)MathF.Round(rx * MathF.Sqrt(ratio));
            if (halfWidth <= 0)
                continue;
            FillRect(spriteBatch, new Rectangle((int)(center.X - halfWidth), (int)center.Y + y, halfWidth * 2, 1), color);
        }
    }

    public static void EllipseOutline(SpriteBatch spriteBatch, Rectangle rect, Color color, int width, int segments = 48)
        => Arc(spriteBatch, rect, 0, MathF.Tau, color, width, segments);

    /// <summary>
    /// Elliptical arc bounded by rect, sampled from startRadians to endRadians (matching
    /// pygame.draw.arc's direct cos/sin sampling in screen space -- no y-axis flip needed).
    /// </summary>
    public static void Arc(SpriteBatch spriteBatch, Rectangle rect, float startRadians, float endRadians, Color color, int width, int segments = 48)
    {
        float rx = rect.Width / 2f, ry = rect.Height / 2f;
        Vector2 center = new(rect.X + rx, rect.Y + ry);
        int steps = Math.Max(2, (int)MathF.Ceiling(Math.Abs(endRadians - startRadians) / MathF.Tau * segments));
        Vector2 Point(float t) => center + new Vector2(MathF.Cos(t) * rx, MathF.Sin(t) * ry);
        Vector2 previous = Point(startRadians);
        for (int i = 1; i <= steps; i++)
        {
            float t = startRadians + (endRadians - startRadians) * i / steps;
            Vector2 point = Point(t);
            Line(spriteBatch, previous, point, color, width);
            previous = point;
        }
    }

    /// <summary>
    /// Filled polygon (pygame.draw.polygon with width=0) via even-odd scanline fill.
    /// Handles the convex diamond/gem/shard shapes entity draw code uses; not a
    /// general-purpose renderer (no anti-aliasing, no self-intersection handling
    /// beyond the even-odd rule).
    /// </summary>
    public static void FillPolygon(SpriteBatch spriteBatch, IReadOnlyList<Vector2> points, Color color)
    {
        if (points.Count < 3)
            return;
        float minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        int yStart = (int)MathF.Floor(minY), yEnd = (int)MathF.Ceiling(maxY);
        var xs = new List<float>();
        for (int y = yStart; y <= yEnd; y++)
        {
            xs.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Count];
                if (a.Y == b.Y)
                    continue;
                if ((y >= a.Y && y < b.Y) || (y >= b.Y && y < a.Y))
                    xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                int xStart = (int)MathF.Round(xs[i]);
                int xEnd = (int)MathF.Round(xs[i + 1]);
                if (xEnd > xStart)
                    FillRect(spriteBatch, new Rectangle(xStart, y, xEnd - xStart, 1), color);
            }
        }
    }

    /// <summary>Polygon outline (pygame.draw.polygon with width>0) -- closed Polyline.</summary>
    public static void PolygonOutline(SpriteBatch spriteBatch, IReadOnlyList<Vector2> points, Color color, int width)
        => Polyline(spriteBatch, points, closed: true, color, width);

    /// <summary>
    /// Black out a star-shaped/polygonal arena exterior without a full-screen
    /// alpha mask. Ported from bossTypes.py's module-level `_draw_outside_arena`
    /// helper -- shared by Dissonance and PathChaseBoss (both draw their own
    /// arena boundary polygon, then black out everything past it), so it lives
    /// here instead of being duplicated per boss class.
    /// </summary>
    public static void DrawOutsideArena(SpriteBatch spriteBatch, Vector2 center, IReadOnlyList<Vector2> vertices)
    {
        if (vertices.Count < 3)
            return;
        int stride = Math.Max(1, (vertices.Count + 15) / 16);
        var sampled = vertices.Where((_, index) => index % stride == 0).ToList();
        float outerRadius = MathF.Sqrt(1920 * 1920 + 1080 * 1080) * 2.2f;
        var outer = new List<Vector2>();
        foreach (var point in sampled)
        {
            var delta = point - center;
            float distance = Math.Max(1.0f, delta.Length());
            outer.Add(center + delta / distance * outerRadius);
        }
        for (int index = 0; index < sampled.Count; index++)
        {
            int nextIndex = (index + 1) % sampled.Count;
            FillPolygon(spriteBatch, new[] { sampled[index], sampled[nextIndex], outer[nextIndex], outer[index] }, Color.Black);
        }
    }
}
