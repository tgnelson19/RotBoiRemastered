using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.UI;

/// <summary>Scalable, rotation-safe player projectile silhouettes built from primitive geometry.</summary>
public static class ProjectileVisuals
{
    public const float MinimumDrawSize = 12f;
    private static readonly Dictionary<string, string> SpritePathCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Visual-only floor shared by friendly and hostile projectiles. Collision
    /// geometry remains authored so readability does not alter balance.
    /// </summary>
    public static float NormalizeDrawSize(float size, float zoom = 1f) =>
        Math.Max(size, MinimumDrawSize / Math.Max(.01f, zoom));

    public static (Vector2 Tail, Vector2 Front) AxisEndpoints(Vector2 center, Vector2 forward, float size)
    {
        forward = SafeForward(forward);
        return (center - forward * size * .7f, center + forward * size * .7f);
    }

    public static void Draw(SpriteBatch spriteBatch, Vector2 center, Vector2 forward, float size,
        Color core, Color edge, string design, bool critical = false, float zoom = 1f)
    {
        size = NormalizeDrawSize(size, zoom);
        forward = SafeForward(forward);
        Vector2 side = new(-forward.Y, forward.X);
        Vector2 P(float x, float y) => center + forward * (x * size) + side * (y * size);
        void FillShape(Color color, ReadOnlySpan<Vector2> normalizedPoints)
        {
            Span<Vector2> points = stackalloc Vector2[normalizedPoints.Length];
            for (int index = 0; index < normalizedPoints.Length; index++)
            {
                Vector2 point = normalizedPoints[index];
                points[index] = P(point.X, point.Y);
            }
            Primitives2D.FillPolygonSpan(spriteBatch, points, color);
        }

        if (!SpritePathCache.TryGetValue(design, out string? spritePath))
        {
            spritePath = $"Bullets/{design}";
            SpritePathCache[design] = spritePath;
        }
        var sprite = Sprites.TryGet(spritePath);
        if (sprite is not null)
        {
            // Authored pointing +X (forward, pre-rotation) -- see
            // Content/Sprites/README.md. Not tinted by core/edge: authored
            // art keeps its own palette rather than being flattened to the
            // Wardrobe's color picker, unlike the procedural shapes below.
            float rotation = MathF.Atan2(forward.Y, forward.X);
            var origin = new Vector2(sprite.Width / 2f, sprite.Height / 2f);
            float scale = size * 1.6f / Math.Max(sprite.Width, sprite.Height);
            spriteBatch.Draw(sprite, center, null, Color.White, rotation, origin, scale, SpriteEffects.None, 0f);
        }
        else if (design == "shard")
        {
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.62f, -.27f), new(.12f, -.40f), new(.70f, 0),
                new(.12f, .40f), new(-.62f, .27f),
            });
            FillShape(core, stackalloc Vector2[]
            {
                new(-.43f, -.14f), new(.10f, -.24f), new(.48f, 0),
                new(.10f, .24f), new(-.43f, .14f),
            });
        }
        else if (design == "lance")
        {
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.72f, -.15f), new(.20f, -.25f), new(.72f, 0),
                new(.20f, .25f), new(-.72f, .15f),
            });
            FillShape(core, stackalloc Vector2[]
            {
                new(-.53f, -.07f), new(.18f, -.12f), new(.52f, 0),
                new(.18f, .12f), new(-.53f, .07f),
            });
        }
        else if (design == "comet")
        {
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.70f, 0), new(-.06f, -.31f), new(.28f, -.31f),
                new(.28f, .31f), new(-.06f, .31f),
            });
            Primitives2D.FillCircle(spriteBatch, P(.28f, 0), size * .40f, edge);
            FillShape(core, stackalloc Vector2[]
            {
                new(-.46f, 0), new(0, -.17f), new(.27f, -.17f),
                new(.27f, .17f), new(0, .17f),
            });
            Primitives2D.FillCircle(spriteBatch, P(.28f, 0), size * .24f, core);
        }
        else if (design == "fork")
        {
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.70f, -.38f), new(-.24f, -.18f),
                new(.12f, -.38f), new(.66f, 0), new(.12f, .38f),
                new(-.24f, .18f), new(-.70f, .38f), new(-.48f, 0),
            });
            FillShape(core, stackalloc Vector2[]
            {
                new(-.43f, -.21f), new(-.17f, -.10f),
                new(.10f, -.22f), new(.45f, 0), new(.10f, .22f),
                new(-.17f, .10f), new(-.43f, .21f), new(-.28f, 0),
            });
        }
        else
        {
            // Reference design: the narrow stem trails and the bulb is always
            // placed on +forward, so the broad end visibly leads the shot.
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.70f, -.18f), new(-.10f, -.18f),
                new(-.10f, -.40f), new(.28f, -.40f),
                new(.28f, .40f), new(-.10f, .40f),
                new(-.10f, .18f), new(-.70f, .18f),
            });
            Primitives2D.FillCircle(spriteBatch, P(.30f, 0), size * .40f, edge);
            FillShape(core, stackalloc Vector2[]
            {
                new(-.49f, -.09f), new(-.03f, -.09f),
                new(-.03f, -.23f), new(.27f, -.23f),
                new(.27f, .23f), new(-.03f, .23f),
                new(-.03f, .09f), new(-.49f, .09f),
            });
            Primitives2D.FillCircle(spriteBatch, P(.30f, 0), size * .23f, core);
        }

        if (critical)
        {
            Primitives2D.FillCircle(spriteBatch, P(.30f, 0), Math.Max(2, size * .09f), UiTheme.Cream);
            Primitives2D.CircleOutline(spriteBatch, P(.30f, 0), Math.Max(3, size * .31f), UiTheme.Purple, Math.Max(1, (int)(size * .06f)));
        }
    }

    private static Vector2 SafeForward(Vector2 forward) => forward.LengthSquared() < .0001f
        ? Vector2.UnitX
        : Vector2.Normalize(forward);
}
