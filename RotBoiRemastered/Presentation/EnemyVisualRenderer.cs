using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Presentation;

/// <summary>
/// Procedural Path-native chassis. Family subclasses still draw their
/// mechanical attachments; this renderer makes the shared ancestry and tier
/// evolution visible beneath them.
/// </summary>
public static class EnemyVisualRenderer
{
    public static void DrawBody(
        SpriteBatch spriteBatch,
        EnemyRenderPose pose,
        EnemyVisualProfile profile,
        Color bodyColor,
        float authoredSize,
        string archetype,
        string? modifier,
        Color? modifierColor,
        int newGamePlusLevel)
    {
        float halfX = pose.Rect.Width * .48f;
        float halfY = pose.Rect.Height * .48f;
        Vector2 axisX = Safe(pose.WorldRight, Vector2.UnitX);
        Vector2 axisY = Safe(pose.WorldDown, Vector2.UnitY);
        var geometry = new BodyGeometry(
            pose.Center, axisX, axisY, halfX, halfY);

        Vector2 shadow = axisX * Math.Max(2f, authoredSize * .055f)
            + axisY * Math.Max(3f, authoredSize * .075f);
        DrawChassis(spriteBatch, profile.BodyKind, geometry, pose.Center + shadow,
            axisX, axisY, halfX, halfY, UiTheme.Shadow);
        DrawChassis(spriteBatch, profile.BodyKind, geometry, pose.Center,
            axisX, axisY, halfX, halfY, bodyColor);
        DrawOutline(spriteBatch, profile.BodyKind, geometry,
            UiTheme.Ink, Math.Max(2, (int)(authoredSize * .065f)));

        DrawSoulCore(spriteBatch, profile, pose, geometry, bodyColor, authoredSize);
        DrawConstructionTier(spriteBatch, profile, pose, geometry, authoredSize);
        DrawRoleTopology(spriteBatch, profile, pose, geometry, authoredSize);
        DrawArchetypeMark(spriteBatch, archetype, pose, geometry, bodyColor, authoredSize);
        if (modifier is not null)
            DrawModifierInlay(spriteBatch, modifier, modifierColor, pose, geometry, authoredSize);
        DrawProgressionScars(
            spriteBatch, newGamePlusLevel, pose, geometry, authoredSize);
    }

    private static void DrawChassis(
        SpriteBatch spriteBatch,
        SoulBodyKind kind,
        in BodyGeometry geometry,
        Vector2 center,
        Vector2 axisX,
        Vector2 axisY,
        float halfX,
        float halfY,
        Color color)
    {
        switch (kind)
        {
            case SoulBodyKind.Resonator:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(-.76f, -1f), geometry.Point(.65f, -1f), geometry.Point(1f, -.45f),
                    geometry.Point(1f, .45f), geometry.Point(.65f, 1f), geometry.Point(-.76f, 1f),
                    geometry.Point(-1f, .48f), geometry.Point(-1f, -.48f),
                }, color);
                break;
            case SoulBodyKind.PressureBlock:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(-.82f, -1f), geometry.Point(.82f, -1f), geometry.Point(1f, -.76f),
                    geometry.Point(1f, .76f), geometry.Point(.82f, 1f), geometry.Point(-.82f, 1f),
                    geometry.Point(-1f, .76f), geometry.Point(-1f, -.76f),
                }, color);
                break;
            case SoulBodyKind.Lens:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(-1f, 0), geometry.Point(-.48f, -.78f), geometry.Point(0, -1f),
                    geometry.Point(.48f, -.78f), geometry.Point(1f, 0), geometry.Point(.48f, .78f),
                    geometry.Point(0, 1f), geometry.Point(-.48f, .78f),
                }, color);
                break;
            case SoulBodyKind.CinderCore:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(-.78f, -.92f), geometry.Point(.28f, -1f), geometry.Point(.92f, -.62f),
                    geometry.Point(1f, .25f), geometry.Point(.58f, .94f), geometry.Point(-.34f, 1f),
                    geometry.Point(-1f, .48f), geometry.Point(-.94f, -.4f),
                }, color);
                break;
            default:
                Span<Vector2> star = stackalloc Vector2[12];
                for (int index = 0; index < star.Length; index++)
                {
                    float angle = -MathF.PI / 2f + index * MathF.Tau / star.Length;
                    float radius = index % 2 == 0 ? 1f : .63f;
                    star[index] = geometry.Point(MathF.Cos(angle) * radius,
                        MathF.Sin(angle) * radius);
                }
                Primitives2D.FillPolygonSpan(spriteBatch, star, color);
                break;
        }
    }

    private static void DrawOutline(
        SpriteBatch spriteBatch,
        SoulBodyKind kind,
        in BodyGeometry geometry,
        Color color,
        int width)
    {
        Span<Vector2> outline = stackalloc Vector2[12];
        int count;
        if (kind == SoulBodyKind.Lens)
        {
            count = 8;
            outline[0] = geometry.Point(-1f, 0); outline[1] = geometry.Point(-.48f, -.78f);
            outline[2] = geometry.Point(0, -1f); outline[3] = geometry.Point(.48f, -.78f);
            outline[4] = geometry.Point(1f, 0); outline[5] = geometry.Point(.48f, .78f);
            outline[6] = geometry.Point(0, 1f); outline[7] = geometry.Point(-.48f, .78f);
        }
        else if (kind == SoulBodyKind.DreamPrism)
        {
            count = 12;
            for (int index = 0; index < count; index++)
            {
                float angle = -MathF.PI / 2f + index * MathF.Tau / count;
                float radius = index % 2 == 0 ? 1f : .63f;
                outline[index] = geometry.Point(MathF.Cos(angle) * radius,
                    MathF.Sin(angle) * radius);
            }
        }
        else
        {
            count = 8;
            outline[0] = geometry.Point(-.78f, -1f); outline[1] = geometry.Point(.72f, -1f);
            outline[2] = geometry.Point(1f, -.58f); outline[3] = geometry.Point(1f, .58f);
            outline[4] = geometry.Point(.72f, 1f); outline[5] = geometry.Point(-.78f, 1f);
            outline[6] = geometry.Point(-1f, .58f); outline[7] = geometry.Point(-1f, -.58f);
        }
        Primitives2D.PolygonOutlineSpan(spriteBatch, outline[..count], color, width);
    }

    private static void DrawSoulCore(
        SpriteBatch spriteBatch,
        EnemyVisualProfile profile,
        EnemyRenderPose pose,
        in BodyGeometry geometry,
        Color bodyColor,
        float size)
    {
        PathVisualProfile path = SoulVisualLanguage.Path(profile.PathKey);
        float pulse = 1f + pose.AttackPulse * .18f;
        Color core = pose.HitFlash ? UiTheme.Cream : path.Secondary;
        switch (profile.BodyKind)
        {
            case SoulBodyKind.Resonator:
                for (int index = -1; index <= 1; index++)
                    Primitives2D.Line(spriteBatch, geometry.Point(-.5f, index * .3f),
                        geometry.Point(.5f * pulse, index * .3f), core, Math.Max(2, (int)(size * .045f)));
                break;
            case SoulBodyKind.PressureBlock:
                Primitives2D.FillRect(spriteBatch,
                    CenteredRect(geometry.Point(0, 0), size * .32f * pulse, size * .32f / pulse),
                    path.Deep);
                Primitives2D.RectOutline(spriteBatch,
                    CenteredRect(geometry.Point(0, 0), size * .38f * pulse, size * .38f / pulse),
                    core, Math.Max(2, (int)(size * .045f)));
                break;
            case SoulBodyKind.Lens:
                Primitives2D.FillEllipse(spriteBatch,
                    CenteredRect(geometry.Point(0, 0), size * .62f * pulse, size * .3f / pulse),
                    path.Deep);
                Primitives2D.FillCircle(spriteBatch, geometry.Point(.18f * pose.AttackPulse, 0),
                    Math.Max(2f, size * .11f), core);
                break;
            case SoulBodyKind.CinderCore:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(0, -.48f), geometry.Point(.38f, -.08f), geometry.Point(.2f, .48f),
                    geometry.Point(-.28f, .3f), geometry.Point(-.43f, -.18f),
                }, path.Deep);
                Primitives2D.Line(spriteBatch, geometry.Point(-.28f, -.2f), geometry.Point(.25f, .28f),
                    core, Math.Max(2, (int)(size * .05f)));
                break;
            default:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(0, -.48f * pulse), geometry.Point(.4f, 0),
                    geometry.Point(0, .48f * pulse), geometry.Point(-.4f, 0),
                }, path.Deep);
                Primitives2D.FillRect(spriteBatch,
                    CenteredRect(geometry.Point(0, 0), size * .12f, size * .12f), core);
                break;
        }
    }

    private static void DrawConstructionTier(
        SpriteBatch spriteBatch,
        EnemyVisualProfile profile,
        EnemyRenderPose pose,
        in BodyGeometry geometry,
        float size)
    {
        PathVisualProfile path = SoulVisualLanguage.Path(profile.PathKey);
        for (int module = 1; module < profile.ConstructionModules; module++)
        {
            float y = module == 1 ? -.72f : .72f;
            float flicker = .65f + .25f * MathF.Sin(
                pose.WalkPhase * 2f + module * 2.4f);
            Primitives2D.Line(spriteBatch, geometry.Point(-.72f, y), geometry.Point(.72f, y),
                path.Secondary * flicker, Math.Max(1, (int)(size * .035f)));
            if (profile.Tier == "hard")
            {
                Primitives2D.Line(spriteBatch, geometry.Point(-.12f, y),
                    geometry.Point(.14f, y - MathF.Sign(y) * .22f),
                    UiTheme.Cream * .65f, Math.Max(1, (int)(size * .025f)));
            }
        }
    }

    private static void DrawRoleTopology(
        SpriteBatch spriteBatch,
        EnemyVisualProfile profile,
        EnemyRenderPose pose,
        in BodyGeometry geometry,
        float size)
    {
        PathVisualProfile path = SoulVisualLanguage.Path(profile.PathKey);
        Vector2 primary = geometry.Point(profile.Anchors.Primary.X, profile.Anchors.Primary.Y);
        Color roleColor = Color.Lerp(path.Accent, UiTheme.Cream,
            .22f + pose.AttackPulse * .48f);
        int width = Math.Max(2, (int)(size * .05f));
        switch (profile.Anchors.RoleKey)
        {
            case "aperture":
                Primitives2D.FillCircle(spriteBatch, primary,
                    size * (.11f + pose.AttackPulse * .07f), path.Deep);
                Primitives2D.CircleOutline(spriteBatch, primary,
                    size * (.17f + pose.AttackPulse * .08f), roleColor, width);
                break;
            case "vents":
                Primitives2D.Line(spriteBatch, geometry.Point(.18f, -.52f),
                    geometry.Point(.8f + pose.AttackPulse * .2f, -.72f), roleColor, width);
                Primitives2D.Line(spriteBatch, geometry.Point(.18f, .52f),
                    geometry.Point(.8f + pose.AttackPulse * .2f, .72f), roleColor, width);
                break;
            case "chambers":
                for (int chamber = -1; chamber <= 1; chamber++)
                    Primitives2D.FillRect(spriteBatch,
                        CenteredRect(geometry.Point(.34f, chamber * .35f), size * .12f, size * .12f),
                        chamber <= MathF.Round(pose.AttackPulse * 2f - 1f)
                            ? roleColor : path.Deep);
                break;
            case "iris":
                for (int fin = 0; fin < 4; fin++)
                {
                    float angle = pose.AttackPulse * .5f + fin * MathF.PI / 2f;
                    Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                    Primitives2D.Line(spriteBatch, primary + direction * size * .08f,
                        primary + direction * size * .25f, roleColor, width);
                }
                break;
            case "fuse":
                Primitives2D.Line(spriteBatch, geometry.Point(-.05f, -.82f), geometry.Point(.34f, -1.18f),
                    roleColor, width);
                Primitives2D.FillRect(spriteBatch,
                    CenteredRect(geometry.Point(.4f, -1.22f), size * .09f, size * .09f),
                    UiTheme.Cream);
                break;
            case "shield":
                Primitives2D.Line(spriteBatch, geometry.Point(.92f, -.78f), geometry.Point(.92f, .78f),
                    roleColor, Math.Max(3, width * 2));
                break;
            case "compression":
                float squeeze = pose.AttackPulse * .18f;
                Primitives2D.Line(spriteBatch, geometry.Point(-.72f + squeeze, -.62f),
                    geometry.Point(-.72f + squeeze, .62f), roleColor, width);
                Primitives2D.Line(spriteBatch, geometry.Point(.72f - squeeze, -.62f),
                    geometry.Point(.72f - squeeze, .62f), roleColor, width);
                break;
            case "command":
                Primitives2D.Line(spriteBatch, geometry.Point(-.7f, -.68f), geometry.Point(.62f, -1.1f),
                    roleColor, width);
                Primitives2D.Line(spriteBatch, geometry.Point(.62f, -1.1f), geometry.Point(.35f, -.58f),
                    roleColor, width);
                break;
            case "cage":
                Primitives2D.RectOutline(spriteBatch,
                    Bounds(geometry.Point(-.4f, -.4f), geometry.Point(.4f, .4f)), roleColor, width);
                break;
            case "tether":
                Primitives2D.Line(spriteBatch, geometry.Point(-.88f, 0), geometry.Point(-1.18f, 0),
                    roleColor * (.6f + .3f * MathF.Sin(pose.WalkPhase * 4f)), width);
                break;
            case "foundation":
                Primitives2D.Line(spriteBatch, geometry.Point(-.8f, .86f), geometry.Point(.8f, .86f),
                    roleColor, Math.Max(3, width * 2));
                break;
            case "segments":
                for (int index = -1; index <= 1; index++)
                    Primitives2D.FillCircle(spriteBatch, geometry.Point(index * .34f, .08f),
                        size * .08f, roleColor);
                break;
        }
    }

    private static void DrawArchetypeMark(
        SpriteBatch spriteBatch,
        string archetype,
        EnemyRenderPose pose,
        in BodyGeometry geometry,
        Color bodyColor,
        float size)
    {
        Color mark = UiTheme.Lighten(bodyColor, 50);
        int width = Math.Max(1, (int)(size * .035f));
        switch (archetype)
        {
            case "runner":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(-.28f, 0), geometry.Point(0, -.28f), geometry.Point(.36f, 0), geometry.Point(0, .28f),
                }, mark);
                break;
            case "skirmisher":
                Primitives2D.Line(spriteBatch, geometry.Point(-.28f, 0), geometry.Point(.28f, 0), mark, width);
                Primitives2D.Line(spriteBatch, geometry.Point(0, -.28f), geometry.Point(0, .28f), mark, width);
                break;
            case "bulwark":
                Primitives2D.PolygonOutlineSpan(spriteBatch, stackalloc Vector2[]
                {
                    geometry.Point(0, -.32f), geometry.Point(.3f, 0), geometry.Point(0, .32f), geometry.Point(-.3f, 0),
                }, mark, width);
                break;
        }
    }

    private static void DrawModifierInlay(
        SpriteBatch spriteBatch,
        string modifier,
        Color? modifierColor,
        EnemyRenderPose pose,
        in BodyGeometry geometry,
        float size)
    {
        Color color = modifierColor ?? UiTheme.Cream;
        int width = Math.Max(2, (int)(size * .04f));
        switch (modifier)
        {
            case "hasty":
                Primitives2D.Line(spriteBatch, geometry.Point(-.82f, -.28f), geometry.Point(-.58f, 0), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(-.82f, .28f), geometry.Point(-.58f, 0), color, width);
                break;
            case "armored":
                Primitives2D.Line(spriteBatch, geometry.Point(-.58f, -.75f), geometry.Point(.58f, .75f), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(.58f, -.75f), geometry.Point(-.58f, .75f), color, width);
                break;
            case "volatile":
                Primitives2D.Line(spriteBatch, geometry.Point(-.22f, -.72f), geometry.Point(.12f, -.15f), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(.12f, -.15f), geometry.Point(-.08f, .22f), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(-.08f, .22f), geometry.Point(.3f, .72f), color, width);
                break;
            case "regenerating":
                Primitives2D.Line(spriteBatch, geometry.Point(-.42f, 0), geometry.Point(.42f, 0), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(0, -.42f), geometry.Point(0, .42f), color, width);
                break;
            case "champion":
                Primitives2D.Line(spriteBatch, geometry.Point(-.45f, -.72f), geometry.Point(-.2f, -.96f), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(-.2f, -.96f), geometry.Point(0, -.72f), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(0, -.72f), geometry.Point(.22f, -.96f), color, width);
                Primitives2D.Line(spriteBatch, geometry.Point(.22f, -.96f), geometry.Point(.45f, -.72f), color, width);
                break;
            default:
                Primitives2D.FillRect(spriteBatch,
                    CenteredRect(geometry.Point(.68f, -.68f), size * .12f, size * .12f), color);
                break;
        }
    }

    private static void DrawProgressionScars(
        SpriteBatch spriteBatch,
        int newGamePlusLevel,
        EnemyRenderPose pose,
        in BodyGeometry geometry,
        float size)
    {
        int seams = Math.Min(3, Math.Max(0, newGamePlusLevel));
        for (int index = 0; index < seams; index++)
        {
            float x = -.48f + index * .42f;
            Primitives2D.Line(spriteBatch,
                geometry.Point(x, -.82f),
                geometry.Point(x + .18f, -.18f),
                UiTheme.Red * (.62f + .12f * MathF.Sin(
                    pose.WalkPhase * 3f + index)),
                Math.Max(1, (int)(size * .03f)));
            Primitives2D.FillRect(spriteBatch,
                CenteredRect(geometry.Point(x + .18f, -.1f),
                    Math.Max(2, size * .055f),
                    Math.Max(2, size * .055f)),
                UiTheme.Cream * .7f);
        }
    }

    private readonly record struct BodyGeometry(
        Vector2 Center,
        Vector2 AxisX,
        Vector2 AxisY,
        float HalfX,
        float HalfY)
    {
        public Vector2 Point(float x, float y) =>
            Center + AxisX * (x * HalfX) + AxisY * (y * HalfY);
    }

    private static Vector2 Safe(Vector2 value, Vector2 fallback) =>
        value.LengthSquared() > .0001f ? Vector2.Normalize(value) : fallback;

    private static Rectangle CenteredRect(Vector2 center, float width, float height) =>
        new((int)(center.X - width / 2f), (int)(center.Y - height / 2f),
            Math.Max(1, (int)width), Math.Max(1, (int)height));

    private static Rectangle Bounds(Vector2 a, Vector2 b) =>
        new((int)MathF.Min(a.X, b.X), (int)MathF.Min(a.Y, b.Y),
            Math.Max(1, (int)MathF.Abs(b.X - a.X)),
            Math.Max(1, (int)MathF.Abs(b.Y - a.Y)));
}
