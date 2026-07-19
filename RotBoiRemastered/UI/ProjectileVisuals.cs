using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.UI;

/// <summary>Scalable, rotation-safe player projectile silhouettes built from primitive geometry.</summary>
public static class ProjectileVisuals
{
    public static (Vector2 Tail, Vector2 Front) AxisEndpoints(Vector2 center, Vector2 forward, float size)
    {
        forward = SafeForward(forward);
        return (center - forward * size * .7f, center + forward * size * .7f);
    }

    public static void Draw(SpriteBatch spriteBatch, Vector2 center, Vector2 forward, float size,
        Color core, Color edge, string design, bool critical = false)
    {
        forward = SafeForward(forward);
        Vector2 side = new(-forward.Y, forward.X);
        Vector2 P(float x, float y) => center + forward * (x * size) + side * (y * size);
        Vector2[] Shape(params (float X, float Y)[] points) => points.Select(point => P(point.X, point.Y)).ToArray();

        var sprite = Sprites.TryGet($"Bullets/{design}");
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
            FillLayer(spriteBatch, Shape((-.62f, -.27f), (.12f, -.40f), (.70f, 0), (.12f, .40f), (-.62f, .27f)), edge);
            FillLayer(spriteBatch, Shape((-.43f, -.14f), (.10f, -.24f), (.48f, 0), (.10f, .24f), (-.43f, .14f)), core);
        }
        else if (design == "lance")
        {
            FillLayer(spriteBatch, Shape((-.72f, -.15f), (.20f, -.25f), (.72f, 0), (.20f, .25f), (-.72f, .15f)), edge);
            FillLayer(spriteBatch, Shape((-.53f, -.07f), (.18f, -.12f), (.52f, 0), (.18f, .12f), (-.53f, .07f)), core);
        }
        else if (design == "comet")
        {
            FillLayer(spriteBatch, Shape((-.70f, 0), (-.06f, -.31f), (.28f, -.31f), (.28f, .31f), (-.06f, .31f)), edge);
            Primitives2D.FillCircle(spriteBatch, P(.28f, 0), size * .40f, edge);
            FillLayer(spriteBatch, Shape((-.46f, 0), (.00f, -.17f), (.27f, -.17f), (.27f, .17f), (.00f, .17f)), core);
            Primitives2D.FillCircle(spriteBatch, P(.28f, 0), size * .24f, core);
        }
        else if (design == "fork")
        {
            FillLayer(spriteBatch, Shape((-.70f, -.38f), (-.24f, -.18f), (.12f, -.38f), (.66f, 0), (.12f, .38f), (-.24f, .18f), (-.70f, .38f), (-.48f, 0)), edge);
            FillLayer(spriteBatch, Shape((-.43f, -.21f), (-.17f, -.10f), (.10f, -.22f), (.45f, 0), (.10f, .22f), (-.17f, .10f), (-.43f, .21f), (-.28f, 0)), core);
        }
        else
        {
            // Reference design: the narrow stem trails and the bulb is always
            // placed on +forward, so the broad end visibly leads the shot.
            FillLayer(spriteBatch, Shape((-.70f, -.18f), (-.10f, -.18f), (-.10f, -.40f), (.28f, -.40f),
                (.28f, .40f), (-.10f, .40f), (-.10f, .18f), (-.70f, .18f)), edge);
            Primitives2D.FillCircle(spriteBatch, P(.30f, 0), size * .40f, edge);
            FillLayer(spriteBatch, Shape((-.49f, -.09f), (-.03f, -.09f), (-.03f, -.23f), (.27f, -.23f),
                (.27f, .23f), (-.03f, .23f), (-.03f, .09f), (-.49f, .09f)), core);
            Primitives2D.FillCircle(spriteBatch, P(.30f, 0), size * .23f, core);
        }

        if (critical)
        {
            Primitives2D.FillCircle(spriteBatch, P(.30f, 0), Math.Max(2, size * .09f), UiTheme.Cream);
            Primitives2D.CircleOutline(spriteBatch, P(.30f, 0), Math.Max(3, size * .31f), UiTheme.Purple, Math.Max(1, (int)(size * .06f)));
        }
    }

    private static void FillLayer(SpriteBatch spriteBatch, IReadOnlyList<Vector2> points, Color color) =>
        Primitives2D.FillPolygon(spriteBatch, points, color);

    private static Vector2 SafeForward(Vector2 forward) => forward.LengthSquared() < .0001f
        ? Vector2.UnitX
        : Vector2.Normalize(forward);
}
