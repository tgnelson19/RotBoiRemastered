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
    private static readonly int[][] CubeFaces =
    [
        [0, 1, 2, 3],
        [4, 7, 6, 5],
        [0, 4, 5, 1],
        [3, 2, 6, 7],
        [0, 3, 7, 4],
        [1, 5, 6, 2],
    ];

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

        var shadowOffset = new Vector2(4, 6);
        Primitives2D.FillQuad(batch, a + shadowOffset, b + shadowOffset, c + shadowOffset, d + shadowOffset, UiTheme.Shadow);
        Primitives2D.FillQuad(batch, a, b, c, d, front);
        Primitives2D.FillQuad(batch, aa, bb, b, a, UiTheme.Lighten(front, 38));
        Primitives2D.FillQuad(batch, bb, b, c, c + lift, Color.Lerp(front, UiTheme.Ink, .28f));
        Span<Vector2> frontOutline = stackalloc Vector2[4] { a, b, c, d };
        Primitives2D.PolygonOutlineSpan(batch, frontOutline, UiTheme.Ink, Math.Max(2, (int)(size * .055f)));
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
        Span<Vector3> vertices = stackalloc Vector3[8];
        float cosYaw = MathF.Cos(yaw);
        float sinYaw = MathF.Sin(yaw);
        float cosPitch = MathF.Cos(pitch);
        float sinPitch = MathF.Sin(pitch);
        float cosRoll = MathF.Cos(roll);
        float sinRoll = MathF.Sin(roll);
        for (int index = 0; index < vertices.Length; index++)
        {
            float x = index is 1 or 2 or 5 or 6 ? 1f : -1f;
            float y = index is 2 or 3 or 6 or 7 ? 1f : -1f;
            float z = index >= 4 ? 1f : -1f;
            float yawX = x * cosYaw + z * sinYaw;
            float yawZ = -x * sinYaw + z * cosYaw;
            float pitchY = y * cosPitch - yawZ * sinPitch;
            float pitchZ = y * sinPitch + yawZ * cosPitch;
            float rollX = yawX * cosRoll - pitchY * sinRoll;
            float rollY = yawX * sinRoll + pitchY * cosRoll;
            float perspective = 4.2f / Math.Max(1.7f, 4.2f - pitchZ);
            vertices[index] = new Vector3(center.X + rollX * extent * perspective,
                center.Y + rollY * extent * perspective, pitchZ);
        }

        Span<int> faceOrder = stackalloc int[6] { 0, 1, 2, 3, 4, 5 };
        static float FaceDepth(ReadOnlySpan<Vector3> cubeVertices, int faceIndex)
        {
            var face = CubeFaces[faceIndex];
            return (cubeVertices[face[0]].Z + cubeVertices[face[1]].Z +
                cubeVertices[face[2]].Z + cubeVertices[face[3]].Z) * .25f;
        }
        for (int index = 1; index < faceOrder.Length; index++)
        {
            int candidate = faceOrder[index];
            float candidateDepth = FaceDepth(vertices, candidate);
            int insertion = index - 1;
            while (insertion >= 0 &&
                FaceDepth(vertices, faceOrder[insertion]) > candidateDepth)
            {
                faceOrder[insertion + 1] = faceOrder[insertion];
                insertion--;
            }
            faceOrder[insertion + 1] = candidate;
        }

        Span<Vector2> projected = stackalloc Vector2[24];
        for (int orderedIndex = 0; orderedIndex < faceOrder.Length; orderedIndex++)
        {
            var face = CubeFaces[faceOrder[orderedIndex]];
            int offset = orderedIndex * 4;
            for (int vertex = 0; vertex < 4; vertex++)
                projected[offset + vertex] = new Vector2(vertices[face[vertex]].X, vertices[face[vertex]].Y);
        }
        var shadowOffset = new Vector2(5, 7);
        Span<Vector2> shadow = stackalloc Vector2[4];
        for (int orderedIndex = 0; orderedIndex < faceOrder.Length; orderedIndex++)
        {
            var points = projected.Slice(orderedIndex * 4, 4);
            for (int vertex = 0; vertex < points.Length; vertex++)
                shadow[vertex] = points[vertex] + shadowOffset;
            Primitives2D.FillPolygonSpan(batch, shadow, UiTheme.Shadow);
        }
        for (int orderedIndex = 0; orderedIndex < faceOrder.Length; orderedIndex++)
        {
            var points = projected.Slice(orderedIndex * 4, 4);
            Color face = orderedIndex % 2 == 0 ? primary : secondary;
            face = Color.Lerp(face, accent, .05f + orderedIndex * .045f);
            Primitives2D.FillPolygonSpan(batch, points, face);
            Primitives2D.PolygonOutlineSpan(batch, points, UiTheme.Ink, Math.Max(2, (int)(extent * .095f)));
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
        var first = new Vector2(center.X - width / 2f, center.Y - height / 2f);
        var second = new Vector2(center.X + width / 2f, center.Y - height / 2f);
        var third = new Vector2(center.X + width / 2f, center.Y + height / 2f);
        var fourth = new Vector2(center.X - width / 2f, center.Y + height / 2f);
        var lift = new Vector2(skew, -depth);
        var shadowOffset = new Vector2(6, 8);
        Primitives2D.FillQuad(batch, first + shadowOffset, second + shadowOffset, third + shadowOffset, fourth + shadowOffset, UiTheme.Shadow);
        Primitives2D.FillQuad(batch, first, second, third, fourth, front);
        Primitives2D.FillQuad(batch, first + lift, second + lift, second, first, UiTheme.Lighten(front, 36));
        Primitives2D.FillQuad(batch, second + lift, third + lift, third, second, Color.Lerp(front, UiTheme.Void, .34f));
        Span<Vector2> frontOutline = stackalloc Vector2[4] { first, second, third, fourth };
        Primitives2D.PolygonOutlineSpan(batch, frontOutline, UiTheme.Ink, Math.Max(2, (int)(Math.Min(width, height) * .055f)));
        Primitives2D.Line(batch, first + lift, second + lift, accent, Math.Max(1, (int)(Math.Min(width, height) * .03f)));
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
        var first = center - axisX * half - axisY * half;
        var second = center + axisX * half - axisY * half;
        var third = center + axisX * half + axisY * half;
        var fourth = center - axisX * half + axisY * half;
        var drop = new Vector2(0, Math.Max(3f, thickness));
        var shadowOffset = drop + new Vector2(6, 7);
        Primitives2D.FillQuad(batch, first + shadowOffset, second + shadowOffset, third + shadowOffset, fourth + shadowOffset, UiTheme.Shadow);
        Primitives2D.FillQuad(batch, fourth, third, third + drop, fourth + drop, Color.Lerp(topColor, UiTheme.Void, .42f));
        Primitives2D.FillQuad(batch, second, third, third + drop, second + drop, Color.Lerp(topColor, UiTheme.Void, .58f));
        Primitives2D.FillQuad(batch, first, second, third, fourth, topColor);
        Span<Vector2> topOutline = stackalloc Vector2[4] { first, second, third, fourth };
        Primitives2D.PolygonOutlineSpan(batch, topOutline, UiTheme.Ink, Math.Max(3, (int)(thickness * .32f)));
        Primitives2D.Line(batch, first, second, edgeColor, Math.Max(2, (int)(thickness * .18f)));
        Primitives2D.Line(batch, first, fourth, Color.Lerp(edgeColor, topColor, .45f), 2);
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
