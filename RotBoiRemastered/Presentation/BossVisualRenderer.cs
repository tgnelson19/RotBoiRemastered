using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Presentation;

/// <summary>
/// Compact construction and lifecycle layer shared by the complete boss
/// roster. It is attached to the body and never recreates the removed
/// boss-following ambient diagrams.
/// </summary>
public static class BossVisualRenderer
{
    public static void DrawSoulConstruction(
        SpriteBatch spriteBatch,
        Enemy boss,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        VisualRenderContext context)
    {
        EnemyRenderPose pose = boss.RenderPose(
            camera, playerWorldPosition, screenShake);
        PathVisualProfile path = SoulVisualLanguage.Path(context.PathKey);
        BossPresentationState state = BossPresentationDirector.Derive(boss);
        int hierarchy = boss switch
        {
            PathGuardianBoss => 1,
            Aphantasia or Dissonance or Rot or Chronos or Ache or Malady => 3,
            _ => 2,
        };

        Vector2 axisX = Normalize(pose.WorldRight, Vector2.UnitX);
        Vector2 axisY = Normalize(pose.WorldDown, Vector2.UnitY);
        float radius = boss.Size * (hierarchy == 3 ? .16f : .13f);
        float pulse = 1f + MathF.Sin(context.Time * path.MotionCadence * 2.2f)
            * .045f;
        if (state is BossPresentationState.Trial
            or BossPresentationState.PhaseGate)
        {
            pulse += .12f;
        }

        // The same black soul well appears in every hierarchy. Its surround
        // changes material and complexity rather than changing meaning.
        Primitives2D.FillRect(spriteBatch,
            Centered(pose.Center + axisX * 3 + axisY * 4,
                radius * 1.7f, radius * 1.7f), UiTheme.Shadow * .8f);
        DrawCoreShape(spriteBatch, path.BodyKind, pose.Center,
            axisX, axisY, radius * pulse, path.Deep);
        DrawCoreShape(spriteBatch, path.BodyKind, pose.Center,
            axisX, axisY, radius * .58f * pulse,
            state == BossPresentationState.Stagger
                ? UiTheme.Cream
                : path.Secondary);

        int modules = 2 + hierarchy * 2;
        float span = boss.Size * (hierarchy == 3 ? .36f : .29f);
        for (int index = 0; index < modules; index++)
        {
            float angle = index * MathF.Tau / modules
                + Stepped(context.Time * path.MotionCadence * .28f, 24);
            float detach = state switch
            {
                BossPresentationState.Entrance => 1.25f,
                BossPresentationState.Trial => 1.18f,
                BossPresentationState.ZeroHealthSeal => .72f,
                BossPresentationState.DeathCollapse => .4f,
                _ => 1f,
            };
            Vector2 local = axisX * MathF.Cos(angle)
                + axisY * MathF.Sin(angle);
            Vector2 point = pose.Center + local * span * detach;
            float moduleSize = boss.Size * (.035f + hierarchy * .008f);
            Primitives2D.FillRect(spriteBatch,
                Centered(point, moduleSize * 1.5f, moduleSize * 1.5f),
                UiTheme.Ink * .86f);
            Primitives2D.FillRect(spriteBatch,
                Centered(point, moduleSize, moduleSize),
                index % 3 == 0 ? path.Secondary : path.Accent);
        }

        Color stateTrim = state switch
        {
            BossPresentationState.Entrance => UiTheme.Cream,
            BossPresentationState.Trial => UiTheme.Gold,
            BossPresentationState.ZeroHealthSeal => UiTheme.Red,
            BossPresentationState.DeathCollapse => UiTheme.Cream,
            _ => path.Accent,
        };
        DrawBodyBrackets(spriteBatch, pose.Center, axisX, axisY,
            boss.Size * .43f, stateTrim,
            Math.Max(2, (int)(boss.Size * .025f)));
    }

    private static void DrawCoreShape(
        SpriteBatch spriteBatch,
        SoulBodyKind kind,
        Vector2 center,
        Vector2 axisX,
        Vector2 axisY,
        float radius,
        Color color)
    {
        Vector2 P(float x, float y) =>
            center + axisX * x * radius + axisY * y * radius;
        if (kind == SoulBodyKind.Lens)
        {
            Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
            {
                P(-1, 0), P(0, -.62f), P(1, 0), P(0, .62f),
            }, color);
        }
        else if (kind == SoulBodyKind.DreamPrism)
        {
            Span<Vector2> star = stackalloc Vector2[8];
            for (int index = 0; index < star.Length; index++)
            {
                float angle = index * MathF.Tau / star.Length;
                float localRadius = index % 2 == 0 ? 1f : .55f;
                star[index] = P(
                    MathF.Cos(angle) * localRadius,
                    MathF.Sin(angle) * localRadius);
            }
            Primitives2D.FillPolygonSpan(spriteBatch, star, color);
        }
        else
        {
            Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
            {
                P(-.72f, -1), P(.72f, -1), P(1, -.58f), P(1, .58f),
                P(.72f, 1), P(-.72f, 1), P(-1, .58f), P(-1, -.58f),
            }, color);
        }
    }

    private static void DrawBodyBrackets(
        SpriteBatch spriteBatch,
        Vector2 center,
        Vector2 axisX,
        Vector2 axisY,
        float radius,
        Color color,
        int width)
    {
        for (int cornerX = -1; cornerX <= 1; cornerX += 2)
        for (int cornerY = -1; cornerY <= 1; cornerY += 2)
        {
            Vector2 corner = center
                + axisX * radius * cornerX
                + axisY * radius * cornerY;
            Primitives2D.Line(spriteBatch, corner,
                corner - axisX * radius * .18f * cornerX, color, width);
            Primitives2D.Line(spriteBatch, corner,
                corner - axisY * radius * .18f * cornerY, color, width);
        }
    }

    private static float Stepped(float value, int steps) =>
        MathF.Floor(value * steps) / steps * MathF.Tau;

    private static Vector2 Normalize(Vector2 value, Vector2 fallback) =>
        value.LengthSquared() > .0001f ? Vector2.Normalize(value) : fallback;

    private static Rectangle Centered(
        Vector2 center,
        float width,
        float height) =>
        new((int)(center.X - width / 2f),
            (int)(center.Y - height / 2f),
            Math.Max(1, (int)width),
            Math.Max(1, (int)height));
}
