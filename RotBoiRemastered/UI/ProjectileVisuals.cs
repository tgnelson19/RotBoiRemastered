using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.UI;

/// <summary>Scalable, rotation-safe player projectile silhouettes built from primitive geometry.</summary>
public static class ProjectileVisuals
{
    public const float MinimumDrawSize = 12f;
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
        Color core, Color edge, string design, bool critical = false, float zoom = 1f,
        float animationTime = 0f, bool drawShadow = false, float intensity = 1f)
    {
        size = NormalizeDrawSize(size, zoom);
        forward = SafeForward(forward);
        float motion = .25f + .75f * Math.Clamp(intensity, 0f, 1f);
        if (design == "bulb")
            size *= 1f + .045f * MathF.Sin(animationTime * 8f) * motion;
        else if (design == "shard")
            forward = Rotate(forward, MathF.Floor(animationTime * 8f) * MathF.PI / 2f);
        else if (design == "lance")
            center -= forward * MathF.Round((.5f + .5f * MathF.Sin(animationTime * 18f)) * 2f * motion);
        else if (design is "prism" or "cog" or "sigil")
            forward = Rotate(forward, animationTime * (design == "cog" ? 4.2f : 2.2f) * motion);

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

        if (drawShadow)
            DrawShadow(spriteBatch, center + new Vector2(3, 4), forward, size, design);

        if (design == "shard")
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
            float flex = 1f + MathF.Sin(animationTime * 11f) * .14f * motion;
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.70f, -.38f * flex), new(-.24f, -.18f),
                new(.12f, -.38f * flex), new(.66f, 0), new(.12f, .38f * flex),
                new(-.24f, .18f), new(-.70f, .38f * flex), new(-.48f, 0),
            });
            FillShape(core, stackalloc Vector2[]
            {
                new(-.43f, -.21f), new(-.17f, -.10f),
                new(.10f, -.22f), new(.45f, 0), new(.10f, .22f),
                new(-.17f, .10f), new(-.43f, .21f), new(-.28f, 0),
            });
        }
        else if (design == "prism")
        {
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.62f, 0), new(0, -.48f), new(.62f, 0), new(0, .48f),
            });
            FillShape(core, stackalloc Vector2[]
            {
                new(-.34f, 0), new(0, -.28f), new(.34f, 0), new(0, .28f),
            });
            Primitives2D.Line(spriteBatch, P(0, -.28f), P(.34f, 0), UiTheme.Cream, Math.Max(1, (int)(size * .06f)));
        }
        else if (design == "cog")
        {
            Span<Vector2> outer = stackalloc Vector2[16];
            for (int index = 0; index < outer.Length; index++)
            {
                float angle = index * MathF.Tau / outer.Length;
                float radius = index % 2 == 0 ? .62f : .43f;
                outer[index] = P(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            }
            Primitives2D.FillPolygonSpan(spriteBatch, outer, edge);
            Primitives2D.FillCircle(spriteBatch, center, size * .34f, core);
            Primitives2D.FillCircle(spriteBatch, center, size * .12f, UiTheme.Ink);
        }
        else if (design == "satellite")
        {
            Primitives2D.FillCircle(spriteBatch, center + new Vector2(3, 3), size * .43f, UiTheme.Shadow);
            Primitives2D.FillCircle(spriteBatch, center, size * .43f, edge);
            Primitives2D.FillCircle(spriteBatch, center, size * .25f, core);
            for (int index = 0; index < 2; index++)
            {
                float angle = animationTime * (3.2f + index) * motion + index * MathF.PI;
                Vector2 mote = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .55f) * size * .68f;
                int moteSize = Math.Max(2, (int)(size * .15f));
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)mote.X - moteSize / 2, (int)mote.Y - moteSize / 2, moteSize, moteSize),
                    index == 0 ? core : edge);
            }
        }
        else if (design == "wave")
        {
            float flex = MathF.Sin(animationTime * 12f) * .12f * motion;
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.70f, -.18f - flex), new(-.34f, -.42f), new(.02f, -.12f + flex),
                new(.34f, -.36f), new(.72f, 0), new(.34f, .36f),
                new(.02f, .12f - flex), new(-.34f, .42f), new(-.70f, .18f + flex),
            });
            FillShape(core, stackalloc Vector2[]
            {
                new(-.45f, -.08f), new(-.18f, -.21f), new(.06f, -.04f),
                new(.42f, 0), new(.06f, .04f), new(-.18f, .21f), new(-.45f, .08f),
            });
        }
        else if (design == "sigil")
        {
            FillShape(edge, stackalloc Vector2[]
            {
                new(-.18f, -.62f), new(.18f, -.62f), new(.18f, -.18f),
                new(.62f, -.18f), new(.62f, .18f), new(.18f, .18f),
                new(.18f, .62f), new(-.18f, .62f), new(-.18f, .18f),
                new(-.62f, .18f), new(-.62f, -.18f), new(-.18f, -.18f),
            });
            Primitives2D.FillCircle(spriteBatch, center, size * .25f, core);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)(center.X - size * .08f), (int)(center.Y - size * .08f),
                    Math.Max(2, (int)(size * .16f)), Math.Max(2, (int)(size * .16f))),
                UiTheme.Cream);
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

        if (design == "comet" && intensity > 0)
        {
            for (int index = 0; index < Math.Max(1, (int)MathF.Ceiling(3 * intensity)); index++)
            {
                float flicker = MathF.Sin(animationTime * (10f + index) + index * 2.1f) * .08f;
                float x = -.82f - index * .18f;
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)P(x, flicker).X - 1, (int)P(x, flicker).Y - 1,
                        Math.Max(2, (int)(size * (.12f - index * .018f))),
                        Math.Max(2, (int)(size * (.12f - index * .018f)))),
                    index == 0 ? edge * .7f : core * .45f);
            }
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

    private static Vector2 Rotate(Vector2 value, float angle)
    {
        float cosine = MathF.Cos(angle);
        float sine = MathF.Sin(angle);
        return new Vector2(
            value.X * cosine - value.Y * sine,
            value.X * sine + value.Y * cosine);
    }

    private static void DrawShadow(
        SpriteBatch spriteBatch,
        Vector2 center,
        Vector2 forward,
        float size,
        string design)
    {
        Vector2 side = new(-forward.Y, forward.X);
        Vector2 P(float x, float y) => center + forward * (x * size) + side * (y * size);
        if (design is "cog" or "satellite")
        {
            Primitives2D.FillCircle(spriteBatch, center, size * .52f, UiTheme.Shadow);
            return;
        }
        if (design is "prism" or "sigil")
        {
            Primitives2D.FillQuad(spriteBatch,
                P(0, -.62f), P(.62f, 0), P(0, .62f), P(-.62f, 0), UiTheme.Shadow);
            return;
        }
        if (design == "lance")
        {
            Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
            {
                P(-.72f, -.18f), P(.22f, -.28f), P(.72f, 0),
                P(.22f, .28f), P(-.72f, .18f),
            }, UiTheme.Shadow);
            return;
        }
        Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
        {
            P(-.70f, -.38f), P(.18f, -.43f), P(.72f, 0),
            P(.18f, .43f), P(-.70f, .38f),
        }, UiTheme.Shadow);
    }
}
