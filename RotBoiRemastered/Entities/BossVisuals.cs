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
    internal static readonly int[][] CubeFaces =
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

    /// <summary>Local-space vertices of a unit cube, index-encoded the same way the original hand-built cube rig used.</summary>
    internal static readonly Vector3[] CubeVertices =
    [
        new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
        new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
    ];

    /// <summary>Draw a genuinely rotating perspective cube for the cores that must read as three-dimensional.</summary>
    public static void RotatingCube3D(SpriteBatch batch, Vector2 center, float extent, Color primary,
        Color secondary, Color accent, float yaw, float pitch, float roll = 0f,
        float escalation = 0f) =>
        RotatingSolid3D(batch, center, extent, CubeVertices, CubeFaces,
            faceIndex => PhysicalCubeFaceColor(faceIndex, primary, secondary, accent),
            yaw, pitch, roll, edgeAccent: accent, escalation: escalation);

    /// <summary>
    /// Generalization of the rotate -> project -> backface-cull -> depth-sort
    /// -> per-face-light -> draw pipeline <see cref="RotatingCube3D"/> was
    /// originally hand-built just for a cube: identical math, but the caller
    /// supplies its own local-space vertex set and face list (each face a
    /// list of 3+ vertex indices), so any convex solid -- a cube, a
    /// diamond/bipyramid, a future prism -- can share one implementation
    /// instead of re-deriving its own copy of this pipeline.
    /// </summary>
    public static void RotatingSolid3D(
        SpriteBatch batch,
        Vector2 center,
        float extent,
        ReadOnlySpan<Vector3> localVertices,
        int[][] faces,
        Func<int, Color> faceColor,
        float yaw,
        float pitch,
        float roll = 0f,
        Color? edgeAccent = null,
        float cameraZ = 4.2f,
        float escalation = 0f)
    {
        escalation = Math.Clamp(escalation, 0f, 1f);
        int vertexCount = localVertices.Length;
        Span<Vector3> rotatedVertices = vertexCount <= 32 ? stackalloc Vector3[vertexCount] : new Vector3[vertexCount];
        Span<Vector3> projected = vertexCount <= 32 ? stackalloc Vector3[vertexCount] : new Vector3[vertexCount];
        float cosYaw = MathF.Cos(yaw);
        float sinYaw = MathF.Sin(yaw);
        float cosPitch = MathF.Cos(pitch);
        float sinPitch = MathF.Sin(pitch);
        float cosRoll = MathF.Cos(roll);
        float sinRoll = MathF.Sin(roll);
        for (int index = 0; index < vertexCount; index++)
        {
            Vector3 local = localVertices[index];
            float yawX = local.X * cosYaw + local.Z * sinYaw;
            float yawZ = -local.X * sinYaw + local.Z * cosYaw;
            float pitchY = local.Y * cosPitch - yawZ * sinPitch;
            float pitchZ = local.Y * sinPitch + yawZ * cosPitch;
            float rollX = yawX * cosRoll - pitchY * sinRoll;
            float rollY = yawX * sinRoll + pitchY * cosRoll;
            rotatedVertices[index] = new Vector3(rollX, rollY, pitchZ);
            float perspective = cameraZ / Math.Max(cameraZ * .4f, cameraZ - pitchZ);
            projected[index] = new Vector3(center.X + rollX * extent * perspective,
                center.Y + rollY * extent * perspective, pitchZ);
        }

        Span<int> faceOrder = faces.Length <= 32 ? stackalloc int[faces.Length] : new int[faces.Length];
        static float FaceDepth(ReadOnlySpan<Vector3> projectedVertices, int[][] solidFaces, int faceIndex)
        {
            var face = solidFaces[faceIndex];
            float sum = 0f;
            foreach (int vertexIndex in face)
                sum += projectedVertices[vertexIndex].Z;
            return sum / face.Length;
        }
        int visibleFaces = 0;
        var camera = new Vector3(0, 0, cameraZ);
        for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            var face = faces[faceIndex];
            Vector3 first = rotatedVertices[face[0]];
            Vector3 second = rotatedVertices[face[1]];
            Vector3 third = rotatedVertices[face[2]];
            Vector3 faceCenter = Vector3.Zero;
            foreach (int vertexIndex in face)
                faceCenter += rotatedVertices[vertexIndex];
            faceCenter /= face.Length;
            Vector3 normal = Vector3.Cross(second - first, third - first);
            if (Vector3.Dot(normal, faceCenter) < 0)
                normal = -normal;
            if (Vector3.Dot(normal, camera - faceCenter) > 0)
                faceOrder[visibleFaces++] = faceIndex;
        }

        for (int index = 1; index < visibleFaces; index++)
        {
            int candidate = faceOrder[index];
            float candidateDepth = FaceDepth(projected, faces, candidate);
            int insertion = index - 1;
            while (insertion >= 0)
            {
                int existing = faceOrder[insertion];
                float existingDepth = FaceDepth(projected, faces, existing);
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

        var shadowOffset = new Vector2(5, 7);
        Span<Vector2> faceBuffer = stackalloc Vector2[8];
        Span<Vector2> inset = stackalloc Vector2[8];
        for (int orderedIndex = 0; orderedIndex < visibleFaces; orderedIndex++)
        {
            var face = faces[faceOrder[orderedIndex]];
            for (int vertex = 0; vertex < face.Length; vertex++)
            {
                var p = projected[face[vertex]];
                faceBuffer[vertex] = new Vector2(p.X, p.Y) + shadowOffset;
            }
            Primitives2D.FillPolygonSpan(batch, faceBuffer[..face.Length], UiTheme.Shadow);
        }
        for (int orderedIndex = 0; orderedIndex < visibleFaces; orderedIndex++)
        {
            int physicalFace = faceOrder[orderedIndex];
            var face = faces[physicalFace];
            for (int vertex = 0; vertex < face.Length; vertex++)
            {
                var p = projected[face[vertex]];
                faceBuffer[vertex] = new Vector2(p.X, p.Y);
            }
            var points = faceBuffer[..face.Length];
            Color fill = UiTheme.Saturate(faceColor(physicalFace), escalation * .55f);
            Primitives2D.FillPolygonSpan(batch, points, fill);
            Primitives2D.PolygonOutlineSpan(batch, points, UiTheme.Ink, Math.Max(2, (int)(extent * .095f)));

            // Second form: an inset facet inside each visible face plus a
            // bevelled silhouette. Both are layering rather than new geometry
            // -- the solid keeps its authored shape and simply gains depth.
            if (escalation > .01f)
            {
                Vector2 faceMiddle = Vector2.Zero;
                for (int vertex = 0; vertex < face.Length; vertex++)
                    faceMiddle += points[vertex];
                faceMiddle /= face.Length;
                float pull = .18f + .22f * escalation;
                for (int vertex = 0; vertex < face.Length; vertex++)
                    inset[vertex] = Vector2.Lerp(points[vertex], faceMiddle, pull);
                var innerPoints = inset[..face.Length];
                Primitives2D.FillPolygonSpan(batch, innerPoints,
                    UiTheme.Saturate(Color.Lerp(fill, UiTheme.Ink, .3f), escalation * .8f));
                Primitives2D.DrawPolygonBevel(batch, innerPoints,
                    UiTheme.Saturate(fill, escalation), Math.Max(1, (int)(extent * .03f)));
            }

            if (edgeAccent.HasValue)
            {
                Color edge = UiTheme.Saturate(
                    UiTheme.Lighten(edgeAccent.Value, 34), escalation);
                Primitives2D.Line(batch, points[0], points[1], edge,
                    Math.Max(1, (int)(extent * (.035f + .022f * escalation))));
            }
        }
    }

    /// <summary>
    /// A translucent wireframe shell around a rotating solid -- both halves
    /// (near and far, split by which side of the camera each face/edge sits
    /// on) drawn back-then-front, so a caller can sandwich an opaque solid
    /// between the two calls in <see cref="DrawWireShell"/> (the far half
    /// behind it, the near half in front). Generalized from Aphantasia's own
    /// hand-built `DrawWireCube`/`DrawWireCubeLayer` (previously only usable
    /// on her own fixed 8-vertex cube) the same way <see cref="RotatingSolid3D"/>
    /// generalized <see cref="RotatingCube3D"/> -- any vertex/face set works,
    /// edges are derived from the face list rather than needing a separate
    /// edge table.
    /// </summary>
    public static void DrawWireShellLayer(
        SpriteBatch batch,
        Vector2 center,
        float extent,
        ReadOnlySpan<Vector3> localVertices,
        int[][] faces,
        Color fill,
        Color edgeColor,
        float yaw,
        float pitch,
        float roll,
        bool front,
        float cameraZ = 4.2f)
    {
        int vertexCount = localVertices.Length;
        Span<Vector3> rotatedVertices = vertexCount <= 32 ? stackalloc Vector3[vertexCount] : new Vector3[vertexCount];
        Span<Vector2> projected = vertexCount <= 32 ? stackalloc Vector2[vertexCount] : new Vector2[vertexCount];
        float cosYaw = MathF.Cos(yaw), sinYaw = MathF.Sin(yaw);
        float cosPitch = MathF.Cos(pitch), sinPitch = MathF.Sin(pitch);
        float cosRoll = MathF.Cos(roll), sinRoll = MathF.Sin(roll);
        for (int index = 0; index < vertexCount; index++)
        {
            Vector3 local = localVertices[index];
            float yawX = local.X * cosYaw + local.Z * sinYaw;
            float yawZ = -local.X * sinYaw + local.Z * cosYaw;
            float pitchY = local.Y * cosPitch - yawZ * sinPitch;
            float pitchZ = local.Y * sinPitch + yawZ * cosPitch;
            float rollX = yawX * cosRoll - pitchY * sinRoll;
            float rollY = yawX * sinRoll + pitchY * cosRoll;
            rotatedVertices[index] = new Vector3(rollX, rollY, pitchZ);
            float perspective = cameraZ / Math.Max(cameraZ * .4f, cameraZ - pitchZ);
            projected[index] = center + new Vector2(rollX, rollY) * extent * perspective;
        }

        var camera = new Vector3(0, 0, cameraZ);
        Span<Vector2> faceBuffer = stackalloc Vector2[8];
        foreach (var face in faces)
        {
            Vector3 faceCenter = Vector3.Zero;
            foreach (int vertexIndex in face)
                faceCenter += rotatedVertices[vertexIndex];
            faceCenter /= face.Length;
            Vector3 normal = Vector3.Cross(
                rotatedVertices[face[1]] - rotatedVertices[face[0]],
                rotatedVertices[face[2]] - rotatedVertices[face[0]]);
            if (Vector3.Dot(normal, faceCenter) < 0)
                normal = -normal;
            bool faceFront = Vector3.Dot(normal, camera - faceCenter) > 0;
            if (faceFront != front)
                continue;
            for (int vertex = 0; vertex < face.Length; vertex++)
                faceBuffer[vertex] = projected[face[vertex]];
            Primitives2D.FillPolygonSpan(batch, faceBuffer[..face.Length], fill * .3f);
        }

        // Edges are every adjacent vertex pair inside each face, deduped so a
        // shared edge between two faces (the common case) draws once.
        Span<(int A, int B)> drawnEdges = stackalloc (int, int)[faces.Length * 4];
        int drawnCount = 0;
        foreach (var face in faces)
        {
            for (int vertex = 0; vertex < face.Length; vertex++)
            {
                int a = face[vertex], b = face[(vertex + 1) % face.Length];
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                bool seen = false;
                for (int index = 0; index < drawnCount; index++)
                {
                    if (drawnEdges[index].A != lo || drawnEdges[index].B != hi)
                        continue;
                    seen = true;
                    break;
                }
                if (seen)
                    continue;
                drawnEdges[drawnCount++] = (lo, hi);
                float depth = (rotatedVertices[lo].Z + rotatedVertices[hi].Z) * .5f;
                if ((depth > 0f) != front)
                    continue;
                Primitives2D.Line(batch, projected[lo], projected[hi], UiTheme.Ink, 8);
                Primitives2D.Line(batch, projected[lo], projected[hi], edgeColor, 3);
            }
        }
    }

    /// <summary>Both halves of a wire shell, back then front -- see <see cref="DrawWireShellLayer"/>.</summary>
    public static void DrawWireShell(
        SpriteBatch batch, Vector2 center, float extent,
        ReadOnlySpan<Vector3> localVertices, int[][] faces,
        Color fill, Color edgeColor, float yaw, float pitch, float roll = 0f, float cameraZ = 4.2f)
    {
        DrawWireShellLayer(batch, center, extent, localVertices, faces, fill, edgeColor, yaw, pitch, roll, front: false, cameraZ);
        DrawWireShellLayer(batch, center, extent, localVertices, faces, fill, edgeColor, yaw, pitch, roll, front: true, cameraZ);
    }

    /// <summary>
    /// A radiating burst of tentacle-like spikes from a boss's core, meant to
    /// sell a phase transition as an actual event rather than the generic
    /// freeze-pose every <c>VisualTransitionRemaining</c> window already
    /// applies to boss motion. Pass <paramref name="progress"/> as 1 at the
    /// instant the transition begins, fading to 0 as it completes (i.e.
    /// <c>VisualTransitionRemaining / duration-at-the-moment-it-was-set</c>)
    /// -- spikes shrink and fade out together as the boss settles into its
    /// new phase.
    /// </summary>
    public static void DrawTransitionBurst(SpriteBatch batch, Vector2 center, float age,
        float radius, Color color, float progress, int spikes = 10, int seed = 0)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        if (progress <= .001f)
            return;
        float eased = progress * progress * (3f - 2f * progress);
        for (int index = 0; index < spikes; index++)
        {
            float hashSeed = index * 12.9898f + seed * 3.77f;
            float hash = hashSeed - MathF.Floor(hashSeed);
            float angle = hash * MathF.Tau + age * .0015f;
            Primitives2D.DrawTentacleSpike(batch, center, angle,
                radius * (.6f + eased * 1.1f), radius * .1f * eased,
                index * 1.7f, index / (float)spikes, age * .001f,
                segments: 10, darken: 1f - eased, alpha: eased, themeColor: color);
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

    /// <summary>
    /// A small pulsing dot projected onto the front face of a rotating solid
    /// (yaw/pitch/roll matching whatever was passed to <see cref="RotatingCube3D"/>/
    /// <see cref="RotatingSolid3D"/> for the same body) -- reads as an eye or a
    /// "this is the front" cue, making a turn-to-face mechanic much more
    /// legible than the body rotation alone. Generalized from Aphantasia's
    /// own private marker (originally hand-rolled only for her own cube rig)
    /// so any rotating-solid boss core can share it.
    /// </summary>
    public static void DrawFacingMarker(SpriteBatch batch, Vector2 center, float extent,
        float yaw, float pitch, float roll, double pulseSeconds, Color dotColor)
    {
        float cosYaw = MathF.Cos(yaw), sinYaw = MathF.Sin(yaw);
        float cosPitch = MathF.Cos(pitch), sinPitch = MathF.Sin(pitch);
        float cosRoll = MathF.Cos(roll), sinRoll = MathF.Sin(roll);
        // Local +Z (the "front") rotated by the same yaw/pitch/roll math
        // RotatingSolid3D uses internally, then perspective-projected the
        // same cheap way every rotating solid in this game already is.
        float yawX = sinYaw, yawZ = cosYaw;
        float pitchY = -yawZ * sinPitch, pitchZ = yawZ * cosPitch;
        float rollX = yawX * cosRoll - pitchY * sinRoll;
        float rollY = yawX * sinRoll + pitchY * cosRoll;
        float perspective = 1f + pitchZ * .12f;
        Vector2 tip = center + new Vector2(rollX, rollY) * extent * perspective * 1.22f;
        float pulse = .7f + .3f * MathF.Sin((float)pulseSeconds * 8f);
        float dotRadius = Math.Max(3f, extent * .09f) * pulse;
        Primitives2D.FillCircle(batch, tip + new Vector2(2, 3), dotRadius, UiTheme.Shadow);
        Primitives2D.FillCircle(batch, tip, dotRadius, UiTheme.Cream);
        Primitives2D.CircleOutline(batch, tip, Math.Max(4f, extent * .13f), dotColor, 2);
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
