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
        Span<Vector3> rotatedVertices = stackalloc Vector3[8];
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
            rotatedVertices[index] = new Vector3(rollX, rollY, pitchZ);
            float perspective = 4.2f / Math.Max(1.7f, 4.2f - pitchZ);
            vertices[index] = new Vector3(center.X + rollX * extent * perspective,
                center.Y + rollY * extent * perspective, pitchZ);
        }

        Span<int> faceOrder = stackalloc int[6];
        static float FaceDepth(ReadOnlySpan<Vector3> cubeVertices, int faceIndex)
        {
            var face = CubeFaces[faceIndex];
            return (cubeVertices[face[0]].Z + cubeVertices[face[1]].Z +
                cubeVertices[face[2]].Z + cubeVertices[face[3]].Z) * .25f;
        }
        int visibleFaces = 0;
        var camera = new Vector3(0, 0, 4.2f);
        for (int faceIndex = 0; faceIndex < CubeFaces.Length; faceIndex++)
        {
            var face = CubeFaces[faceIndex];
            Vector3 first = rotatedVertices[face[0]];
            Vector3 second = rotatedVertices[face[1]];
            Vector3 third = rotatedVertices[face[2]];
            Vector3 faceCenter = (rotatedVertices[face[0]] + rotatedVertices[face[1]]
                + rotatedVertices[face[2]] + rotatedVertices[face[3]]) * .25f;
            Vector3 normal = Vector3.Cross(second - first, third - first);
            if (Vector3.Dot(normal, faceCenter) < 0)
                normal = -normal;
            if (Vector3.Dot(normal, camera - faceCenter) > 0)
                faceOrder[visibleFaces++] = faceIndex;
        }

        for (int index = 1; index < visibleFaces; index++)
        {
            int candidate = faceOrder[index];
            float candidateDepth = FaceDepth(vertices, candidate);
            int insertion = index - 1;
            while (insertion >= 0)
            {
                int existing = faceOrder[insertion];
                float existingDepth = FaceDepth(vertices, existing);
                bool after = existingDepth > candidateDepth + .0001f
                    || (MathF.Abs(existingDepth - candidateDepth) <= .0001f
                        && existing > candidate);
                if (!after)
                    break;
                faceOrder[insertion + 1] = faceOrder[insertion];
                insertion--;
            }
            faceOrder[insertion + 1] = candidate;
        }

        Span<Vector2> projected = stackalloc Vector2[24];
        for (int orderedIndex = 0; orderedIndex < visibleFaces; orderedIndex++)
        {
            var face = CubeFaces[faceOrder[orderedIndex]];
            int offset = orderedIndex * 4;
            for (int vertex = 0; vertex < 4; vertex++)
                projected[offset + vertex] = new Vector2(vertices[face[vertex]].X, vertices[face[vertex]].Y);
        }
        var shadowOffset = new Vector2(5, 7);
        Span<Vector2> shadow = stackalloc Vector2[4];
        for (int orderedIndex = 0; orderedIndex < visibleFaces; orderedIndex++)
        {
            var points = projected.Slice(orderedIndex * 4, 4);
            for (int vertex = 0; vertex < points.Length; vertex++)
                shadow[vertex] = points[vertex] + shadowOffset;
            Primitives2D.FillPolygonSpan(batch, shadow, UiTheme.Shadow);
        }
        for (int orderedIndex = 0; orderedIndex < visibleFaces; orderedIndex++)
        {
            var points = projected.Slice(orderedIndex * 4, 4);
            int physicalFace = faceOrder[orderedIndex];
            Color face = PhysicalCubeFaceColor(physicalFace, primary, secondary, accent);
            Primitives2D.FillPolygonSpan(batch, points, face);
            Primitives2D.PolygonOutlineSpan(batch, points, UiTheme.Ink, Math.Max(2, (int)(extent * .095f)));
            Primitives2D.Line(batch, points[0], points[1], UiTheme.Lighten(accent, 34), Math.Max(1, (int)(extent * .035f)));
        }
    }

    internal static Color PhysicalCubeFaceColor(int faceIndex, Color primary,
        Color secondary, Color accent) => faceIndex switch
    {
        0 => primary,
        1 => secondary,
        2 => UiTheme.Lighten(primary, 24),
        3 => Color.Lerp(primary, UiTheme.Ink, .24f),
        4 => Color.Lerp(secondary, accent, .18f),
        _ => Color.Lerp(primary, accent, .28f),
    };

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

    /// <summary>Block-built speaker chamber used by the Sound family.</summary>
    public static void Resonator(SpriteBatch batch, Vector2 center, float size,
        Color body, Color accent, float compression, int chambers = 2)
    {
        size = Math.Max(8f, size);
        compression = Math.Clamp(compression, 0f, 1f);
        float width = size * (1f + compression * .08f);
        float height = size * (1f - compression * .1f);
        Cuboid(batch, center, width, height, body, accent, compression * .35f);
        float cavity = size * (.18f + compression * .025f);
        Primitives2D.FillCircle(batch, center, cavity + 5, UiTheme.Ink);
        Primitives2D.FillCircle(batch, center, cavity, UiTheme.Void);
        Primitives2D.CircleOutline(batch, center, cavity * (.72f + compression * .12f), accent,
            Math.Max(2, (int)(size * .035f)), 24);
        for (int side = -1; side <= 1; side += 2)
        {
            for (int chamber = 0; chamber < chambers; chamber++)
            {
                float radius = size * (.28f + chamber * .12f + compression * .035f);
                var bounds = new Rectangle((int)(center.X - radius), (int)(center.Y - radius),
                    Math.Max(2, (int)(radius * 2)), Math.Max(2, (int)(radius * 2)));
                Primitives2D.Arc(batch, bounds,
                    side < 0 ? MathF.PI * .62f : -MathF.PI * .38f,
                    side < 0 ? MathF.PI * 1.38f : MathF.PI * .38f,
                    chamber == 0 ? accent : UiTheme.Cream * .65f,
                    Math.Max(2, (int)(size * .025f)), 16);
            }
        }
    }

    /// <summary>Heavy side plate with a hinge line, shared by Touch constructions.</summary>
    public static void HingedPlate(SpriteBatch batch, Vector2 center, float width,
        float height, Color body, Color accent, float angle)
    {
        Vector2 axis = new(MathF.Cos(angle), MathF.Sin(angle));
        Vector2 normal = new(-axis.Y, axis.X);
        float halfWidth = width * .5f;
        float halfHeight = height * .5f;
        Span<Vector2> points = stackalloc Vector2[4]
        {
            center - axis * halfWidth - normal * halfHeight,
            center + axis * halfWidth - normal * halfHeight,
            center + axis * halfWidth + normal * halfHeight,
            center - axis * halfWidth + normal * halfHeight,
        };
        Span<Vector2> shadow = stackalloc Vector2[4];
        for (int index = 0; index < 4; index++)
            shadow[index] = points[index] + new Vector2(4, 6);
        Primitives2D.FillPolygonSpan(batch, shadow, UiTheme.Shadow);
        Primitives2D.FillPolygonSpan(batch, points, body);
        Primitives2D.PolygonOutlineSpan(batch, points, UiTheme.Ink,
            Math.Max(2, (int)(Math.Min(width, height) * .08f)));
        Primitives2D.Line(batch, points[0], points[1], accent,
            Math.Max(2, (int)(height * .08f)));
        Vector2 hinge = center - axis * width * .32f;
        Primitives2D.FillCircle(batch, hinge, Math.Max(2, height * .12f), UiTheme.Cream);
    }

    /// <summary>Geometric iris whose opening is presentation-only.</summary>
    public static void Aperture(SpriteBatch batch, Vector2 center, float radius,
        Color body, Color accent, float opening, float rotation = 0f, int blades = 6)
    {
        radius = Math.Max(5f, radius);
        opening = Math.Clamp(opening, .08f, 1f);
        Span<Vector2> points = stackalloc Vector2[4];
        for (int blade = 0; blade < blades; blade++)
        {
            float angle = rotation + blade * MathF.Tau / blades;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 tangent = new(-direction.Y, direction.X);
            Vector2 outer = center + direction * radius;
            Vector2 inner = center + direction * radius * (.18f + opening * .28f);
            points[0] = inner - tangent * radius * .12f;
            points[1] = outer - tangent * radius * .28f;
            points[2] = outer + tangent * radius * .08f;
            points[3] = inner + tangent * radius * .08f;
            Primitives2D.FillPolygonSpan(batch, points, blade % 2 == 0 ? body : Color.Lerp(body, UiTheme.Ink, .18f));
            Primitives2D.PolygonOutlineSpan(batch, points, UiTheme.Ink, Math.Max(1, (int)(radius * .045f)));
        }
        Primitives2D.FillCircle(batch, center, radius * (.12f + opening * .13f), UiTheme.Void);
        Primitives2D.CircleOutline(batch, center, radius * (.15f + opening * .14f), accent,
            Math.Max(2, (int)(radius * .07f)), 24);
    }

    /// <summary>Translucency-free hard-edged prism/petal used by Phantasia.</summary>
    public static void PrismPetal(SpriteBatch batch, Vector2 center, float length,
        float width, Color body, Color accent, float angle)
    {
        Vector2 axis = new(MathF.Cos(angle), MathF.Sin(angle));
        Vector2 normal = new(-axis.Y, axis.X);
        Span<Vector2> points = stackalloc Vector2[4]
        {
            center + axis * length * .55f,
            center + normal * width * .5f,
            center - axis * length * .45f,
            center - normal * width * .5f,
        };
        Primitives2D.FillPolygonSpan(batch, points, body);
        Primitives2D.PolygonOutlineSpan(batch, points, UiTheme.Ink, Math.Max(2, (int)(width * .1f)));
        Primitives2D.Line(batch, points[0], points[1], accent, Math.Max(1, (int)(width * .07f)));
    }
}
