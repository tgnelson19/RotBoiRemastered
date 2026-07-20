using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Allocation-light procedural pieces shared by the path-boss silhouettes.
/// These intentionally use hard-edged polygons rather than sprite assets so
/// every ancient core keeps the same impossible, cubic visual ancestry.
/// </summary>
internal static class BossVisuals
{
    public static void Cube(SpriteBatch batch, Vector2 center, float size, Color front, Color accent, float turn = 0f)
    {
        size = Math.Max(3f, size);
        float half = size * .5f;
        float depth = size * (.18f + .035f * MathF.Sin(turn));
        var a = new Vector2(center.X - half, center.Y - half + depth);
        var b = new Vector2(center.X + half, center.Y - half + depth);
        var c = new Vector2(center.X + half, center.Y + half);
        var d = new Vector2(center.X - half, center.Y + half);
        var lift = new Vector2(depth * MathF.Cos(turn), -depth * 1.15f);
        var aa = a + lift;
        var bb = b + lift;

        Primitives2D.FillPolygon(batch, new[] { a + new Vector2(4, 6), b + new Vector2(4, 6), c + new Vector2(4, 6), d + new Vector2(4, 6) }, UiTheme.Shadow);
        Primitives2D.FillPolygon(batch, new[] { a, b, c, d }, front);
        Primitives2D.FillPolygon(batch, new[] { aa, bb, b, a }, UiTheme.Lighten(front, 38));
        Primitives2D.FillPolygon(batch, new[] { bb, b, c, c + lift }, Color.Lerp(front, UiTheme.Ink, .28f));
        Primitives2D.PolygonOutline(batch, new[] { a, b, c, d }, UiTheme.Ink, Math.Max(2, (int)(size * .055f)));
        Primitives2D.Line(batch, aa, bb, accent, Math.Max(1, (int)(size * .035f)));
        Primitives2D.Line(batch, aa, a, UiTheme.Ink, 2);
        Primitives2D.Line(batch, bb, b, UiTheme.Ink, 2);
    }

    public static void OrbitingCubes(SpriteBatch batch, Vector2 center, float age, int count, float radius,
        float cubeSize, Color first, Color second, float spread = 1f, float speed = 1f, bool? frontLayer = null)
    {
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.Tau / count + age * .006f * speed;
            bool inFront = MathF.Sin(angle) >= 0;
            if (frontLayer.HasValue && frontLayer.Value != inFront)
                continue;
            float localRadius = radius * spread * (1f + .08f * MathF.Sin(age * .017f + index * 1.7f));
            var point = center + new Vector2(MathF.Cos(angle) * localRadius, MathF.Sin(angle) * localRadius * .56f);
            float depthScale = .78f + .24f * (.5f + .5f * MathF.Sin(angle));
            Cube(batch, point, cubeSize * depthScale, index % 2 == 0 ? first : second, second, angle);
        }
    }

    /// <summary>Draw a genuinely rotating perspective cube for the cores that must read as three-dimensional.</summary>
    public static void RotatingCube3D(SpriteBatch batch, Vector2 center, float extent, Color primary,
        Color secondary, Color accent, float yaw, float pitch, float roll = 0f)
    {
        float[,] corners =
        {
            { -1, -1, -1 }, { 1, -1, -1 }, { 1, 1, -1 }, { -1, 1, -1 },
            { -1, -1, 1 }, { 1, -1, 1 }, { 1, 1, 1 }, { -1, 1, 1 },
        };
        var vertices = new Vector3[8];
        for (int index = 0; index < vertices.Length; index++)
        {
            float x = corners[index, 0], y = corners[index, 1], z = corners[index, 2];
            float yawX = x * MathF.Cos(yaw) + z * MathF.Sin(yaw);
            float yawZ = -x * MathF.Sin(yaw) + z * MathF.Cos(yaw);
            float pitchY = y * MathF.Cos(pitch) - yawZ * MathF.Sin(pitch);
            float pitchZ = y * MathF.Sin(pitch) + yawZ * MathF.Cos(pitch);
            float rollX = yawX * MathF.Cos(roll) - pitchY * MathF.Sin(roll);
            float rollY = yawX * MathF.Sin(roll) + pitchY * MathF.Cos(roll);
            float perspective = 4.2f / Math.Max(1.7f, 4.2f - pitchZ);
            vertices[index] = new Vector3(center.X + rollX * extent * perspective,
                center.Y + rollY * extent * perspective, pitchZ);
        }

        int[][] faces =
        {
            new[] { 0, 1, 2, 3 }, new[] { 4, 7, 6, 5 }, new[] { 0, 4, 5, 1 },
            new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 5, 6, 2 },
        };
        var ordered = faces.OrderBy(face => face.Average(vertex => vertices[vertex].Z)).ToArray();
        var projected = ordered
            .Select(face => face.Select(vertex => new Vector2(vertices[vertex].X, vertices[vertex].Y)).ToArray())
            .ToArray();
        foreach (var points in projected)
        {
            var shadow = points.Select(point => point + new Vector2(5, 7)).ToArray();
            Primitives2D.FillPolygon(batch, shadow, UiTheme.Shadow);
        }
        for (int faceIndex = 0; faceIndex < projected.Length; faceIndex++)
        {
            var points = projected[faceIndex];
            Color face = faceIndex % 2 == 0 ? primary : secondary;
            face = Color.Lerp(face, accent, .05f + faceIndex * .045f);
            Primitives2D.FillPolygon(batch, points, face);
            Primitives2D.PolygonOutline(batch, points, UiTheme.Ink, Math.Max(2, (int)(extent * .095f)));
            Primitives2D.Line(batch, points[0], points[1], UiTheme.Lighten(accent, 34), Math.Max(1, (int)(extent * .035f)));
        }
    }

    /// <summary>Draw a tall or wide cubic prism, used for pillars and half-buried slabs.</summary>
    public static void Cuboid(SpriteBatch batch, Vector2 center, float width, float height, Color front,
        Color accent, float turn = 0f)
    {
        width = Math.Max(4f, width);
        height = Math.Max(4f, height);
        float depth = Math.Max(4f, Math.Min(width, height) * (.16f + .035f * MathF.Sin(turn)));
        float skew = MathF.Cos(turn) * depth * .45f;
        var frontFace = new[]
        {
            new Vector2(center.X - width / 2f, center.Y - height / 2f),
            new Vector2(center.X + width / 2f, center.Y - height / 2f),
            new Vector2(center.X + width / 2f, center.Y + height / 2f),
            new Vector2(center.X - width / 2f, center.Y + height / 2f),
        };
        var lift = new Vector2(skew, -depth);
        var top = new[] { frontFace[0] + lift, frontFace[1] + lift, frontFace[1], frontFace[0] };
        var side = new[] { frontFace[1] + lift, frontFace[2] + lift, frontFace[2], frontFace[1] };
        Primitives2D.FillPolygon(batch, frontFace.Select(point => point + new Vector2(6, 8)).ToArray(), UiTheme.Shadow);
        Primitives2D.FillPolygon(batch, frontFace, front);
        Primitives2D.FillPolygon(batch, top, UiTheme.Lighten(front, 36));
        Primitives2D.FillPolygon(batch, side, Color.Lerp(front, UiTheme.Void, .34f));
        Primitives2D.PolygonOutline(batch, frontFace, UiTheme.Ink, Math.Max(2, (int)(Math.Min(width, height) * .055f)));
        Primitives2D.Line(batch, top[0], top[1], accent, Math.Max(1, (int)(Math.Min(width, height) * .03f)));
    }

    /// <summary>Draw a raised square floor slab with hard corners and visible front/right depth faces.</summary>
    public static void FloorSlab(SpriteBatch batch, Vector2 center, Vector2 axisX, Vector2 axisY,
        float sideLength, float thickness, Color topColor, Color edgeColor)
    {
        if (axisX.LengthSquared() < .0001f)
            axisX = Vector2.UnitX;
        if (axisY.LengthSquared() < .0001f)
            axisY = Vector2.UnitY;
        axisX.Normalize();
        axisY.Normalize();
        float half = Math.Max(4f, sideLength * .5f);
        var top = new[]
        {
            center - axisX * half - axisY * half,
            center + axisX * half - axisY * half,
            center + axisX * half + axisY * half,
            center - axisX * half + axisY * half,
        };
        var drop = new Vector2(0, Math.Max(3f, thickness));
        var front = new[] { top[3], top[2], top[2] + drop, top[3] + drop };
        var right = new[] { top[1], top[2], top[2] + drop, top[1] + drop };
        Primitives2D.FillPolygon(batch, top.Select(point => point + drop + new Vector2(6, 7)).ToArray(), UiTheme.Shadow);
        Primitives2D.FillPolygon(batch, front, Color.Lerp(topColor, UiTheme.Void, .42f));
        Primitives2D.FillPolygon(batch, right, Color.Lerp(topColor, UiTheme.Void, .58f));
        Primitives2D.FillPolygon(batch, top, topColor);
        Primitives2D.PolygonOutline(batch, top, UiTheme.Ink, Math.Max(3, (int)(thickness * .32f)));
        Primitives2D.Line(batch, top[0], top[1], edgeColor, Math.Max(2, (int)(thickness * .18f)));
        Primitives2D.Line(batch, top[0], top[3], Color.Lerp(edgeColor, topColor, .45f), 2);
    }

    /// <summary>Concentric breathing ellipses make power feel stored rather than emitted as noise.</summary>
    public static void OscillatingAura(SpriteBatch batch, Vector2 center, float age, float radius, Color color,
        int bands = 4, float speed = 1f)
    {
        for (int band = bands - 1; band >= 0; band--)
        {
            float phase = age * .012f * speed + band * .9f;
            float pulse = 1f + MathF.Sin(phase) * (.045f + band * .012f);
            float width = radius * (1.4f + band * .28f) * pulse;
            float height = radius * (.72f + band * .16f) * pulse;
            var ellipse = new Rectangle((int)(center.X - width), (int)(center.Y - height),
                Math.Max(2, (int)(width * 2)), Math.Max(2, (int)(height * 2)));
            Primitives2D.EllipseOutline(batch, ellipse, color * (.16f + band * .055f), 2 + band % 2);
        }
    }

    public static void Disassemble(SpriteBatch batch, Vector2 center, float age, float progress, float size,
        Color first, Color second, int pieces = 12)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        float eased = progress * progress * (3f - 2f * progress);
        for (int index = 0; index < pieces; index++)
        {
            float angle = index * 2.399963f + age * .002f * (index % 2 == 0 ? 1f : -1f);
            float distance = size * (.18f + eased * (1.2f + index % 4 * .22f));
            var point = center + new Vector2(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance * .72f);
            float pieceSize = size * (.16f + (index % 3) * .025f) * (1f + eased * .45f);
            Cube(batch, point, pieceSize, index % 2 == 0 ? first : second, UiTheme.Cream, angle);
        }
        float ringRadius = size * (.45f + eased * 1.45f);
        Primitives2D.CircleOutline(batch, center, ringRadius, second * (1f - progress * .65f), Math.Max(2, (int)(size * .035f)));
    }
}
