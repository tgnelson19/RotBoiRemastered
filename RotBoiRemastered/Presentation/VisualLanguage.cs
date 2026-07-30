using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Presentation;

/// <summary>
/// Stable semantic channels. Path color is allowed inside a visual, but these
/// trims and cadences always answer the same player-facing question.
/// </summary>
public enum VisualSemanticCue
{
    Ambient,
    Friendly,
    Hostile,
    HostileIgnition,
    Interactable,
    Reward,
    Unavailable,
}

public enum RoomPresentationState
{
    Dormant,
    Awakening,
    Combat,
    Release,
    Residual,
}

public enum BossPresentationState
{
    Entrance,
    Engagement,
    Declaration,
    PhaseGate,
    Trial,
    Stagger,
    Recovery,
    ZeroHealthSeal,
    DeathCollapse,
}

public enum SoulBodyKind
{
    Resonator,
    PressureBlock,
    Lens,
    CinderCore,
    DreamPrism,
}

public enum VfxPrimitive
{
    Square,
    Chip,
    Streak,
    Shard,
    ArcSegment,
    Afterimage,
}

/// <summary>
/// Every draw-time input which is allowed to change presentation. Keeping it
/// immutable prevents render code from becoming another simulation owner.
/// </summary>
public readonly record struct VisualRenderContext(
    float Time,
    float UserIntensity,
    float EffectiveIntensity,
    float CameraAngleDegrees,
    float Zoom,
    string PathKey,
    int Act,
    RoomPresentationState RoomState,
    bool HardMode,
    int Mastery,
    int NewGamePlus)
{
    public float OptionalIntensity =>
        Math.Clamp(UserIntensity, 0f, 1f)
        * Math.Clamp(EffectiveIntensity, 0f, 1f);

    public bool IsSecondAct => Act >= 2;
}

public sealed record PathVisualProfile(
    string Key,
    SoulBodyKind BodyKind,
    Color Accent,
    Color Secondary,
    Color Deep,
    float MotionCadence,
    string PureGift,
    string Distortion);

public sealed record RoomRoleVisualProfile(
    PathRoomType Type,
    string GlyphKey,
    int SegmentCount,
    bool Crowned,
    bool Split,
    bool Keyed);

public readonly record struct EnemyRoleAnchors(
    Vector2 Primary,
    Vector2 Secondary,
    Vector2 Tertiary,
    string RoleKey);

public sealed record EnemyVisualProfile(
    string PathKey,
    string Family,
    string Tier,
    SoulBodyKind BodyKind,
    int ConstructionModules,
    EnemyRoleAnchors Anchors);

public sealed record VfxRecipe(
    string Key,
    VfxPrimitive Primitive,
    BitVfxLayer Layer,
    int Count,
    float Speed,
    float Lifetime,
    float Gravity,
    bool Essential);

public readonly record struct VisualDensity(
    float UserIntensity,
    float EffectiveIntensity,
    float Ambience,
    float Trails,
    float Debris)
{
    public float Optional => UserIntensity * EffectiveIntensity;
}

public sealed class PresentationClock
{
    public float Seconds { get; private set; }

    public void Advance(double elapsedSeconds) =>
        Seconds += (float)Math.Clamp(elapsedSeconds, 0, .05);

    public void Reset(float seconds = 0f) =>
        Seconds = Math.Max(0f, seconds);
}

/// <summary>
/// One source of truth for the Living Soul construction grammar.
/// </summary>
public static class SoulVisualLanguage
{
    public static readonly IReadOnlyDictionary<string, PathVisualProfile> Paths =
        new Dictionary<string, PathVisualProfile>(StringComparer.Ordinal)
        {
            ["sound"] = new(
                "sound", SoulBodyKind.Resonator,
                new Color(207, 191, 151), new Color(132, 119, 170),
                new Color(47, 43, 59), 1.15f,
                "purposeful resonance", "meaningless repetition"),
            ["touch"] = new(
                "touch", SoulBodyKind.PressureBlock,
                new Color(91, 132, 74), new Color(151, 119, 64),
                new Color(38, 51, 37), .72f,
                "shelter and contact", "burial and occupation"),
            ["sight"] = new(
                "sight", SoulBodyKind.Lens,
                new Color(104, 190, 222), new Color(228, 142, 63),
                new Color(31, 54, 70), 1.65f,
                "clarity and discovery", "surveillance and fixation"),
            ["chemesthesis"] = new(
                "chemesthesis", SoulBodyKind.CinderCore,
                new Color(207, 83, 45), new Color(111, 142, 62),
                new Color(62, 34, 31), .94f,
                "warning and self-preservation", "pain without cause"),
            ["phantasia"] = new(
                "phantasia", SoulBodyKind.DreamPrism,
                new Color(190, 83, 175), new Color(111, 91, 203),
                new Color(52, 28, 66), 1.02f,
                "invention and possibility", "creation without restraint"),
        };

    public static readonly IReadOnlyDictionary<PathRoomType, RoomRoleVisualProfile> RoomRoles =
        new Dictionary<PathRoomType, RoomRoleVisualProfile>
        {
            [PathRoomType.Start] = new(PathRoomType.Start, "hearth", 1, false, false, false),
            [PathRoomType.Skirmish] = new(PathRoomType.Skirmish, "threshold", 1, false, false, false),
            [PathRoomType.Assault] = new(PathRoomType.Assault, "advance", 3, false, false, false),
            [PathRoomType.Elite] = new(PathRoomType.Elite, "crown", 4, true, false, false),
            [PathRoomType.Challenge] = new(PathRoomType.Challenge, "branch", 2, false, true, false),
            [PathRoomType.Treasure] = new(PathRoomType.Treasure, "key", 4, false, false, true),
            [PathRoomType.Boss] = new(PathRoomType.Boss, "seal", 5, false, false, false),
        };

    public static readonly IReadOnlyList<string> EnemyFamilies = new[]
    {
        "basic", "runner", "drifter", "skirmisher", "bulwark",
        "ranged_wanderer", "shotgunner", "snake", "parent", "child",
        "pillar", "banner", "rammer", "warder", "splitter", "collector",
        "volley", "laser", "bomb", "miniboss",
        "sound_echoer", "sound_resonator",
        "touch_clasper", "touch_mirekeeper",
        "sight_blinker", "sight_lens",
        "chem_cinderpod", "chem_sporecaster",
        "phantasia_mirage", "phantasia_dreamweaver",
    };

    public static readonly IReadOnlyList<string> EnemyTiers =
        new[] { "easy", "medium", "hard" };

    public static readonly IReadOnlyDictionary<string, VfxRecipe> VfxRecipes =
        new Dictionary<string, VfxRecipe>(StringComparer.Ordinal)
        {
            ["impact"] = new("impact", VfxPrimitive.Chip, BitVfxLayer.World, 8, 1.8f, .34f, .025f, true),
            ["critical"] = new("critical", VfxPrimitive.Shard, BitVfxLayer.World, 18, 2.8f, .55f, .025f, true),
            ["shield"] = new("shield", VfxPrimitive.Streak, BitVfxLayer.World, 12, 2.1f, .4f, .018f, true),
            ["death"] = new("death", VfxPrimitive.Shard, BitVfxLayer.World, 22, 2.6f, .72f, .035f, false),
            ["boss_death"] = new("boss_death", VfxPrimitive.ArcSegment, BitVfxLayer.Overlay, 44, 3.8f, 1.2f, .02f, true),
            ["pickup"] = new("pickup", VfxPrimitive.Streak, BitVfxLayer.World, 7, 1.5f, .38f, -.01f, false),
            ["room_release"] = new("room_release", VfxPrimitive.Chip, BitVfxLayer.Ground, 28, 2.1f, .8f, .018f, false),
            ["dash"] = new("dash", VfxPrimitive.Afterimage, BitVfxLayer.Ground, 10, 1.4f, .28f, 0f, false),
        };

    public static PathVisualProfile Path(string? pathKey) =>
        Paths.TryGetValue(pathKey ?? "", out PathVisualProfile? profile)
            ? profile
            : Paths["sound"];

    public static Color CueColor(
        VisualSemanticCue cue,
        PathVisualProfile path,
        bool highContrast = false)
    {
        if (highContrast && cue is VisualSemanticCue.Hostile or VisualSemanticCue.HostileIgnition)
            return cue == VisualSemanticCue.HostileIgnition ? Color.White : UiTheme.Red;
        return cue switch
        {
            VisualSemanticCue.Ambient => path.Accent * .42f,
            VisualSemanticCue.Friendly => path.Secondary,
            VisualSemanticCue.Hostile => UiTheme.Red,
            VisualSemanticCue.HostileIgnition => UiTheme.Cream,
            VisualSemanticCue.Interactable => UiTheme.Cream,
            VisualSemanticCue.Reward => UiTheme.Gold,
            VisualSemanticCue.Unavailable => UiTheme.Border,
            _ => path.Accent,
        };
    }

    public static RoomPresentationState DeriveRoomState(
        bool activated,
        bool cleared,
        float secondsSinceEntry,
        float secondsSinceClear)
    {
        if (!activated)
            return RoomPresentationState.Dormant;
        if (!cleared && secondsSinceEntry < 1.1f)
            return RoomPresentationState.Awakening;
        if (!cleared)
            return RoomPresentationState.Combat;
        if (secondsSinceClear < 1.25f)
            return RoomPresentationState.Release;
        return RoomPresentationState.Residual;
    }

    public static EnemyVisualProfile Enemy(
        string? pathKey,
        string family,
        string tier)
    {
        PathVisualProfile path = Path(pathKey);
        int modules = tier switch
        {
            "hard" => 3,
            "medium" => 2,
            _ => 1,
        };
        return new EnemyVisualProfile(
            path.Key,
            family,
            EnemyTiers.Contains(tier) ? tier : "easy",
            path.BodyKind,
            modules,
            AnchorsFor(family));
    }

    public static EnemyRoleAnchors AnchorsFor(string family)
    {
        string role = family switch
        {
            "ranged_wanderer" => "aperture",
            "shotgunner" => "vents",
            "volley" => "chambers",
            "laser" or "sight_lens" => "iris",
            "bomb" or "chem_cinderpod" => "fuse",
            "warder" or "bulwark" => "shield",
            "rammer" or "touch_clasper" => "compression",
            "banner" => "command",
            "collector" => "cage",
            "parent" or "child" => "tether",
            "pillar" => "foundation",
            "snake" => "segments",
            _ when family.Contains("echo", StringComparison.Ordinal) => "resonance",
            _ when family.Contains("spore", StringComparison.Ordinal) => "pods",
            _ when family.Contains("mirage", StringComparison.Ordinal)
                || family.Contains("dream", StringComparison.Ordinal) => "prism",
            _ => "core",
        };
        return new EnemyRoleAnchors(
            new Vector2(.72f, 0f),
            new Vector2(-.48f, -.42f),
            new Vector2(-.48f, .42f),
            role);
    }

    public static int ProgressionScarTier(int mastery, int newGamePlus, bool hardMode) =>
        Math.Clamp((mastery > 0 ? 1 : 0)
            + Math.Min(2, Math.Max(0, mastery - 1))
            + Math.Min(2, Math.Max(0, newGamePlus))
            + (hardMode ? 1 : 0), 0, 6);

    public static void DrawRoomGlyph(
        SpriteBatch spriteBatch,
        Vector2 center,
        float size,
        PathRoomType roomType,
        PathVisualProfile path,
        float time,
        float energy = 1f,
        float rotationRadians = 0f)
    {
        RoomRoleVisualProfile role = RoomRoles[roomType];
        float pulse = .94f + .06f * MathF.Sin(time * path.MotionCadence * 2f);
        float radius = size * pulse;
        Color deep = path.Deep * (.7f * energy);
        Color accent = path.Accent * (.12f + .5f * energy);
        Vector2 Axis(float x, float y)
        {
            float cosine = MathF.Cos(rotationRadians);
            float sine = MathF.Sin(rotationRadians);
            return center + new Vector2(
                x * cosine - y * sine,
                x * sine + y * cosine);
        }

        if (roomType == PathRoomType.Start)
        {
            Primitives2D.CircleOutline(spriteBatch, center, radius, accent, Math.Max(1, (int)(size * .08f)));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)(center.X - size * .17f), (int)(center.Y - size * .17f),
                    Math.Max(2, (int)(size * .34f)), Math.Max(2, (int)(size * .34f))),
                path.Secondary * energy);
            return;
        }

        if (roomType == PathRoomType.Skirmish || roomType == PathRoomType.Assault)
        {
            int bars = role.SegmentCount;
            for (int index = 0; index < bars; index++)
            {
                float offset = (index - (bars - 1) / 2f) * size * .3f;
                Primitives2D.Line(spriteBatch, Axis(offset, -size * .55f),
                    Axis(offset, size * .55f), accent, Math.Max(2, (int)(size * .1f)));
            }
            return;
        }

        Span<Vector2> diamond = stackalloc Vector2[4]
        {
            Axis(0, -radius), Axis(radius, 0), Axis(0, radius), Axis(-radius, 0),
        };
        Primitives2D.FillPolygonSpan(spriteBatch, diamond, deep);
        for (int index = 0; index < role.SegmentCount; index++)
        {
            float angle = time * .22f + index * MathF.Tau / role.SegmentCount;
            Vector2 a = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * .55f;
            Vector2 b = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            Primitives2D.Line(spriteBatch, a, b, accent, Math.Max(1, (int)(size * .07f)));
        }
        if (role.Crowned)
        {
            Primitives2D.Line(spriteBatch, Axis(-size * .45f, -size * .75f),
                Axis(-size * .18f, -size), path.Secondary * energy, 2);
            Primitives2D.Line(spriteBatch, Axis(-size * .18f, -size),
                Axis(0, -size * .72f), path.Secondary * energy, 2);
            Primitives2D.Line(spriteBatch, Axis(0, -size * .72f),
                Axis(size * .2f, -size), path.Secondary * energy, 2);
            Primitives2D.Line(spriteBatch, Axis(size * .2f, -size),
                Axis(size * .45f, -size * .75f), path.Secondary * energy, 2);
        }
        if (role.Split)
            Primitives2D.Line(spriteBatch, Axis(0, -radius), Axis(0, radius), UiTheme.Cream * energy, 2);
        if (role.Keyed)
        {
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)(center.X - size * .15f), (int)(center.Y - size * .05f),
                    Math.Max(2, (int)(size * .3f)), Math.Max(2, (int)(size * .48f))),
                UiTheme.Gold * energy);
            Primitives2D.CircleOutline(spriteBatch, center - new Vector2(0, size * .13f),
                size * .19f, UiTheme.Gold * energy, 2);
        }
    }
}

/// <summary>
/// Reduces optional spectacle as gameplay information occupies more of the
/// frame. Essential visuals never consume this value.
/// </summary>
public static class VisualDensityDirector
{
    public static VisualDensity Calculate(
        double userIntensity,
        int visibleEnemies,
        int hostileProjectiles,
        float telegraphCoverage,
        bool bossActive,
        bool authoredPeak = false)
    {
        float user = (float)Math.Clamp(userIntensity, 0, 1);
        float enemyPressure = Math.Clamp((visibleEnemies - 12) / 48f, 0f, 1f);
        float projectilePressure = Math.Clamp((hostileProjectiles - 36) / 124f, 0f, 1f);
        float warningPressure = Math.Clamp(telegraphCoverage, 0f, 1f);
        float pressure = Math.Max(enemyPressure, Math.Max(projectilePressure, warningPressure));
        if (bossActive)
            pressure = Math.Max(pressure, .16f);
        float floor = authoredPeak ? .58f : .22f;
        float effective = MathHelper.Lerp(1f, floor, pressure);
        return new VisualDensity(
            user,
            effective,
            Math.Clamp(effective * .78f, .12f, 1f),
            Math.Clamp(effective * .9f, .18f, 1f),
            Math.Clamp(effective, .22f, 1f));
    }
}

public static class BossPresentationDirector
{
    public static BossPresentationState Derive(Enemy boss)
    {
        if (boss is Beaudis { Dying: true }
            or Dissonance { Dying: true }
            or PathGuardianBoss { Dying: true }
            or PathChaseBoss { Dying: true })
        {
            return BossPresentationState.DeathCollapse;
        }
        if (boss.Hp <= 0)
            return BossPresentationState.ZeroHealthSeal;
        if (boss is Dissonance { StaggerRecoveryRemaining: > 0 })
            return BossPresentationState.Recovery;
        if (boss is Beaudis { IsStaggered: true }
            or Dissonance { IsStaggered: true })
        {
            return BossPresentationState.Stagger;
        }
        if (boss is PathGuardianBoss { TrialActive: true }
            || boss is PathChaseBoss { PresentationSurvivalActive: true })
        {
            return BossPresentationState.Trial;
        }
        bool phaseGate = boss switch
        {
            PathGuardianBoss guardian =>
                guardian.PhaseGatePending || guardian.TransitionRemaining > 0,
            PathChaseBoss chase => chase.VisualTransitionRemaining > 0,
            Dissonance dissonance => dissonance.TransitionRemaining > 0,
            _ => false,
        };
        if (phaseGate)
        {
            return BossPresentationState.PhaseGate;
        }
        double entrance = boss switch
        {
            Beaudis value => value.EntranceRemaining,
            Dissonance value => value.EntranceRemaining,
            PathGuardianBoss value => value.EntranceRemaining,
            PathChaseBoss value => value.EntranceRemaining,
            _ => 0,
        };
        if (entrance > 0)
            return BossPresentationState.Entrance;
        if (boss.VisualAttackTimer > 0)
            return BossPresentationState.Declaration;
        return BossPresentationState.Engagement;
    }
}
