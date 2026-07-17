using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Configurable three-phase placeholder for rapidly prototyping path bosses.
/// Ported from bossTypes.py's `PathChaseBoss` class-attribute config (every
/// field below was an overridable Python class attribute -- `bossName`,
/// `phaseLabels`, `bodyColor`, `movementSpeed`, ... -- inherited/overridden
/// per concrete subclass). Rather than mirror that with C# virtual
/// properties (which would require calling virtual members from the base
/// constructor to compute `size`/`speed`/etc. before `base(...)` runs -- a
/// well-known C# hazard), each subclass builds one of these immutable
/// records instead and passes it up explicitly. Subclasses of a subclass
/// (e.g. `Chronos` overriding `Ishe`) use a `with` expression against the
/// parent's config, mirroring Python's partial class-attribute override
/// exactly.
/// </summary>
public sealed record PathChaseBossConfig(
    string BossName,
    string Subtitle,
    IReadOnlyList<string> PhaseLabels,
    bool FinalBoss,
    string Pattern,
    string OwnerPrefix,
    Color BodyColor,
    Color FinalBodyColor,
    Color AccentColor,
    Color FinalAccentColor,
    double MovementSpeed,
    double BodyScale,
    double FinalBodyScale,
    double CooldownSeconds,
    double FinalCooldownSeconds,
    double ShotSpeed,
    double FinalShotSpeed,
    double ShotDamage,
    double FinalShotDamage,
    double ShotScale,
    double FinalShotScale,
    double ShotRangeTiles,
    string ArenaShape,
    double ArenaScale,
    IReadOnlyList<string> MovementModes)
{
    public static readonly PathChaseBossConfig Default = new(
        BossName: "PATH BOSS", Subtitle: "CONTENT PLACEHOLDER",
        PhaseLabels: new[] { "HUNT", "PRESS", "OVERWHELM" }, FinalBoss: false,
        Pattern: "fan", OwnerPrefix: "path",
        BodyColor: new Color(91, 103, 53), FinalBodyColor: new Color(48, 82, 48),
        AccentColor: new Color(132, 119, 63), FinalAccentColor: new Color(74, 125, 67),
        MovementSpeed: .21, BodyScale: 1.9, FinalBodyScale: 2.35,
        CooldownSeconds: 2.85, FinalCooldownSeconds: 2.35,
        ShotSpeed: .68, FinalShotSpeed: .82, ShotDamage: 275, FinalShotDamage: 360,
        ShotScale: .30, FinalShotScale: .34, ShotRangeTiles: 18,
        ArenaShape: "circle", ArenaScale: 10.4,
        MovementModes: new[] { "chase", "static", "path" });
}

/// <summary>
/// Shared base for alternate mid/final bosses on non-"sound" content paths
/// (see `gamePaths.py`'s `boss_key()` -- not wired to path selection in this
/// port yet, so these bosses aren't reachable through natural gameplay
/// triggers, same as in Python until that per-path selection exists).
///
/// Cleanup vs. the Python original:
/// - `stagger`/`maxStagger`/`isStaggered`/`perfectStagger`/
///   `staggerRecoveryRemaining`/`runeSilenceRemaining`/`survivalActive`/
///   `survivalRemaining` are all set in Python's `__init__` but never read
///   by `PathChaseBoss` itself, `Ishe`/`Chronos`, or the Touch family
///   (`PlagueTouchBoss`/`Bair`/`Sting`) -- confirmed by reading every
///   method on all of them. They're only meaningful on
///   `SinChemesthesisBoss` (which has a real stagger system), not yet
///   ported (see Entities/README.md). Dropped here; revisit if that family
///   is ever ported and needs to share this base's stagger fields.
/// - `ArenaCenter`/`ArenaRadius` are computed once from an explicit
///   `Battleground` constructor parameter (same cleanup as `Dissonance`'s
///   `_arena_center()` -> cached field) instead of reading a
///   `background.py` global from both update- and draw-side methods.
/// </summary>
public class PathChaseBoss : Enemy
{
    protected readonly Random Rng;
    protected PathChaseBossConfig Config { get; }
    public Vector2 ArenaCenter { get; }
    public float ArenaRadius { get; }

    public int Phase { get; protected set; } = 1;
    public string PhaseLabel { get; protected set; }
    public string PhaseFlavor { get; protected set; }
    public Color PhaseAccent { get; protected set; }
    public double EntranceRemaining { get; set; } = .9;
    public bool DebugPhaseLocked { get; set; }
    public double PhaseElapsed { get; set; }
    public double PhaseTimeLimit { get; }
    protected readonly float[] ArenaSeed;

    public PathChaseBoss(float worldX, float worldY, Battleground battleground, PathChaseBossConfig config, Random? rng = null)
        : base(worldX, worldY,
            (float)(config.MovementSpeed * (config.FinalBoss ? 1.16 : 1.0)),
            Simulation.TileSize * (float)(config.FinalBoss ? config.FinalBodyScale : config.BodyScale),
            config.FinalBoss ? config.FinalBodyColor : config.BodyColor,
            config.FinalBoss ? 360 : 270, config.FinalBoss ? 48000 : 29000,
            config.FinalBoss ? 520 : 280, config.FinalBoss ? 4.0 : 3.3,
            float.PositiveInfinity, $"{config.OwnerPrefix}_boss", "hard")
    {
        Config = config;
        Rng = rng ?? Random.Shared;
        ArenaCenter = new Vector2(battleground.Width * Simulation.TileSize / 2f, battleground.Height * Simulation.TileSize / 2f);
        PhaseLabel = config.PhaseLabels[0];
        PhaseFlavor = ToTitleCase(config.Subtitle);
        PhaseAccent = config.FinalBoss ? config.FinalAccentColor : config.AccentColor;
        AttackCooldown = Simulation.FrameRate * 1.1f;
        AttackCooldownMax = Simulation.FrameRate * (float)(config.FinalBoss ? config.FinalCooldownSeconds : config.CooldownSeconds);
        ArenaRadius = Simulation.TileSize * (float)config.ArenaScale;
        PhaseTimeLimit = config.FinalBoss ? 28.0 : 24.0;
        ArenaSeed = Enumerable.Range(0, 28).Select(_ => (float)(Rng.NextDouble() * .3 - .15)).ToArray();
    }

    private static string ToTitleCase(string text) => string.Join(" ", text.Split(' ').Select(
        word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    protected static double Seconds() => Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);

    protected Vector2 Center() => new(WorldX + Size / 2f, WorldY + Size / 2f);

    protected List<Vector2> ArenaVertices()
    {
        float radius = ArenaRadius;
        if (Config.ArenaShape == "square")
        {
            return new List<Vector2>
            {
                new(ArenaCenter.X - radius, ArenaCenter.Y - radius), new(ArenaCenter.X + radius, ArenaCenter.Y - radius),
                new(ArenaCenter.X + radius, ArenaCenter.Y + radius), new(ArenaCenter.X - radius, ArenaCenter.Y + radius),
            };
        }
        if (Config.ArenaShape == "triangle")
        {
            var triangle = new List<Vector2>();
            for (int index = 0; index < 3; index++)
            {
                float angle = -MathF.PI / 2f + index * 2f * MathF.PI / 3f;
                triangle.Add(new Vector2(ArenaCenter.X + MathF.Cos(angle) * radius, ArenaCenter.Y + MathF.Sin(angle) * radius));
            }
            return triangle;
        }
        int count = Config.ArenaShape == "jagged" ? 28 : 64;
        var points = new List<Vector2>();
        for (int index = 0; index < count; index++)
        {
            float angle = index * 2f * MathF.PI / count;
            float localRadius;
            if (Config.ArenaShape == "jagged")
                localRadius = radius * (1 + ArenaSeed[index] + MathF.Sin(Age * .013f + index * 1.71f) * .13f);
            else if (Config.ArenaShape == "atomic")
                localRadius = radius * (.88f + .1f * MathF.Sin(angle * 3 + Age * .008f) + .045f * MathF.Sin(angle * 7 - Age * .011f));
            else
                localRadius = radius;
            points.Add(new Vector2(ArenaCenter.X + MathF.Cos(angle) * localRadius, ArenaCenter.Y + MathF.Sin(angle) * localRadius));
        }
        return points;
    }

    protected static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> vertices)
    {
        bool inside = false;
        var previous = vertices[^1];
        foreach (var current in vertices)
        {
            if ((current.Y > point.Y) != (previous.Y > point.Y))
            {
                // The crossing test must preserve the edge's sign. Replacing a
                // negative denominator with epsilon classifies half of a clockwise
                // polygon as exterior and repeatedly drags the player to its center.
                float crossingX = (previous.X - current.X) * (point.Y - current.Y) / (previous.Y - current.Y) + current.X;
                if (point.X < crossingX)
                    inside = !inside;
            }
            previous = current;
        }
        return inside;
    }

    /// <summary>Return the nearest point, segment, and squared distance on a polygon.</summary>
    protected static (Vector2 Point, int Segment, float DistanceSq) ClosestBoundaryPoint(Vector2 point, IReadOnlyList<Vector2> vertices)
    {
        Vector2 bestPoint = vertices[0];
        int bestSegment = 0;
        float bestDistance = float.PositiveInfinity;
        for (int index = 0; index < vertices.Count; index++)
        {
            var start = vertices[index];
            var end = vertices[(index + 1) % vertices.Count];
            float dx = end.X - start.X, dy = end.Y - start.Y;
            float lengthSq = dx * dx + dy * dy;
            float amount = lengthSq <= 1e-9f ? 0f : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSq, 0f, 1f);
            var candidate = new Vector2(start.X + dx * amount, start.Y + dy * amount);
            float distance = (point.X - candidate.X) * (point.X - candidate.X) + (point.Y - candidate.Y) * (point.Y - candidate.Y);
            if (distance < bestDistance)
            {
                bestPoint = candidate;
                bestSegment = index;
                bestDistance = distance;
            }
        }
        return (bestPoint, bestSegment, bestDistance);
    }

    /// <summary>Ported from constrain_player_position(). Called by GameSession's movement-constraint branch for any boss exposing this arena shape.</summary>
    public (float X, float Y) ConstrainPlayerPosition(float playerX, float playerY, float playerSize)
    {
        var playerCenter = new Vector2(playerX + playerSize / 2f, playerY + playerSize / 2f);
        var vertices = ArenaVertices();
        var (nearest, segmentIndex, distanceSq) = ClosestBoundaryPoint(playerCenter, vertices);
        // A center-only test permits half the player body to leak through diagonal
        // edges. Keep a circular body margin inside every segment instead.
        float margin = playerSize * .72f;
        bool inside = PointInPolygon(playerCenter, vertices);
        if (inside && distanceSq >= margin * margin)
            return (playerX, playerY);

        var start = vertices[segmentIndex];
        var end = vertices[(segmentIndex + 1) % vertices.Count];
        float dx = end.X - start.X, dy = end.Y - start.Y;
        float length = Math.Max(1e-9f, MathF.Sqrt(dx * dx + dy * dy));
        float signedArea = 0f;
        for (int index = 0; index < vertices.Count; index++)
        {
            var a = vertices[index];
            var b = vertices[(index + 1) % vertices.Count];
            signedArea += a.X * b.Y - b.X * a.Y;
        }
        // These world polygons currently wind with positive signed area. The left
        // segment normal is therefore inward; retain support for reversed winding.
        var normal = signedArea >= 0 ? new Vector2(-dy / length, dx / length) : new Vector2(dy / length, -dx / length);
        var corrected = nearest + normal * margin;

        // Mildly concave animated boundaries can place a local normal outside an
        // adjacent spike. Fall back to a short centerward inset only in that case.
        if (!PointInPolygon(corrected, vertices))
        {
            float towardX = ArenaCenter.X - nearest.X, towardY = ArenaCenter.Y - nearest.Y;
            float towardLength = Math.Max(1e-9f, MathF.Sqrt(towardX * towardX + towardY * towardY));
            corrected = new Vector2(nearest.X + towardX / towardLength * margin, nearest.Y + towardY / towardLength * margin);
        }
        return (corrected.X - playerSize / 2f, corrected.Y - playerSize / 2f);
    }

    protected virtual void UpdatePhase()
    {
        if (DebugPhaseLocked)
            return;
        double ratio = Math.Max(0.0, (double)Hp / MaxHp);
        int newPhase = ratio <= .34 ? 3 : ratio <= .67 ? 2 : 1;
        if (newPhase != Phase)
        {
            Phase = newPhase;
            PhaseLabel = Config.PhaseLabels[newPhase - 1];
            AttackCooldown = Math.Min(AttackCooldown!.Value, Simulation.FrameRate * .7f);
        }
    }

    /// <summary>Dev/testing hotkey support. Ported from debug_set_phase().</summary>
    public virtual void DebugSetPhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, 3);
        PhaseLabel = Config.PhaseLabels[Phase - 1];
        DebugPhaseLocked = true;
        AttackCooldown = 0f;
    }

    protected virtual void FirePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        var center = Center();
        float direction = MathF.Atan2(playerY - center.Y, playerX - center.X);
        int count = Config.Pattern switch
        {
            "minefield" => Config.FinalBoss ? new[] { 2, 3, 5 }[Phase - 1] : new[] { 1, 2, 3 }[Phase - 1],
            "mirage" => Config.FinalBoss ? new[] { 3, 5, 7 }[Phase - 1] : new[] { 2, 3, 5 }[Phase - 1],
            _ => Config.FinalBoss ? new[] { 1, 2, 3 }[Phase - 1] : new[] { 1, 1, 2 }[Phase - 1],
        };
        float spread = Config.Pattern switch { "rush" => .22f, "minefield" => 2.5f, "mirage" => 1.15f, _ => .34f };
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            float shotSize = Size * (float)(Config.FinalBoss ? Config.FinalShotScale : Config.ShotScale);
            string shape = Config.Pattern == "minefield" ? "mine" : Config.Pattern is "rush" or "mirage" ? "diamond" : "square";
            sink.Add(new EnemyProjectile(
                center.X - shotSize / 2f, center.Y - shotSize / 2f, direction + offset,
                (float)(Config.FinalBoss ? Config.FinalShotSpeed : Config.ShotSpeed),
                (float)(Config.FinalBoss ? Config.FinalShotDamage : Config.ShotDamage),
                shotSize, travelRange: Simulation.TileSize * (float)Config.ShotRangeTiles, color: PhaseAccent,
                shape: shape, path: Config.Pattern == "mirage" ? "sine" : "linear",
                amplitude: Config.Pattern == "mirage" ? Simulation.TileSize * .65f : 0f,
                lifetime: Config.Pattern == "minefield" ? 20.0f : null,
                speedDecay: Config.Pattern == "minefield" ? .08f : 0f,
                owner: $"{Config.OwnerPrefix}_{(Config.FinalBoss ? "final" : "mid")}", ignoreWalls: Config.Pattern == "minefield"));
        }
        // Touch's final boss retains the initial slow radial cage placeholder.
        if (Config.Pattern == "boulder" && Config.FinalBoss && Phase == 3)
        {
            for (int index = 0; index < 8; index++)
            {
                sink.Add(new EnemyProjectile(center.X, center.Y, index * MathF.PI / 4f, .48f, 300f, Size * .23f,
                    travelRange: Simulation.TileSize * 11f, color: PhaseAccent, shape: "diamond", owner: $"{Config.OwnerPrefix}_ring"));
            }
        }
        MarkAttack(.42f);
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        PhaseElapsed += dt;
        UpdatePhase();
        string mode = Config.MovementModes[(Phase - 1) % Config.MovementModes.Count];
        float originalSpeed = Speed;
        float effectivePlayerX = context.PlayerWorldX, effectivePlayerY = context.PlayerWorldY;
        if (mode == "static")
        {
            Speed = 0;
        }
        else if (mode == "path")
        {
            effectivePlayerX = ArenaCenter.X + MathF.Cos((float)PhaseElapsed * .8f) * ArenaRadius * .55f;
            effectivePlayerY = ArenaCenter.Y + MathF.Sin((float)PhaseElapsed * .8f) * ArenaRadius * .55f;
        }
        var effectiveContext = mode == "chase"
            ? context
            : new EnemyUpdateContext
            {
                PlayerWorldX = effectivePlayerX, PlayerWorldY = effectivePlayerY, Battleground = context.Battleground,
                ProjectileSink = context.ProjectileSink, AllEnemies = context.AllEnemies, ExperienceBubbles = context.ExperienceBubbles,
            };
        base.Update(effectiveContext);
        Speed = originalSpeed;
        AttackCooldown -= (float)Simulation.GetTimerStep();
        if (EntranceRemaining <= 0 && AttackCooldown <= 0)
        {
            FirePattern(effectivePlayerX, effectivePlayerY, context.ProjectileSink);
            double rate = 1.0 - .11 * (Phase - 1);
            AttackCooldownMax ??= Simulation.FrameRate * (float)(Config.FinalBoss ? Config.FinalCooldownSeconds : Config.CooldownSeconds);
            AttackCooldown = AttackCooldownMax.Value * (float)(rate * (.9 + Rng.NextDouble() * .22));
        }
    }

    private void DrawPathArena(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var worldVertices = ArenaVertices();
        var vertices = worldVertices.Select(v => camera.WorldToScreen(v, playerWorldPosition, screenShake)).ToArray();
        if (vertices.Length < 3)
            return;
        var arenaCenterScreen = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        Primitives2D.DrawOutsideArena(spriteBatch, arenaCenterScreen, vertices);
        Primitives2D.PolygonOutline(spriteBatch, vertices, UiTheme.Shadow, 14);
        Primitives2D.PolygonOutline(spriteBatch, vertices, UiTheme.Ink, 8);
        Primitives2D.PolygonOutline(spriteBatch, vertices, PhaseAccent, 3);
        double progress = 1 - (PhaseElapsed % PhaseTimeLimit) / PhaseTimeLimit;
        int lit = Math.Max(2, (int)(vertices.Length * progress));
        Primitives2D.Polyline(spriteBatch, vertices.Take(lit).ToArray(), false, UiTheme.Cream, 2);
        if (Config.ArenaShape == "atomic")
        {
            for (int index = 0; index < 3; index++)
            {
                // Rotating a full arena-sized alpha surface here would allocate a
                // huge buffer every frame -- draw the projected atomic orbit as a
                // compact polyline instead (matches the Python comment/fix as-is).
                float rotation = index * MathF.PI / 3f + Age * .012f * MathF.PI / 180f;
                float c = MathF.Cos(rotation), s = MathF.Sin(rotation);
                var points = new Vector2[65];
                for (int step = 0; step < 65; step++)
                {
                    float angle = step * 2f * MathF.PI / 64f;
                    float localX = MathF.Cos(angle) * ArenaRadius * .9f;
                    float localY = MathF.Sin(angle) * ArenaRadius * .31f;
                    points[step] = new Vector2(arenaCenterScreen.X + localX * c - localY * s, arenaCenterScreen.Y + localX * s + localY * c);
                }
                var orbitColor = Color.Lerp(PhaseAccent, UiTheme.Void, .72f);
                Primitives2D.PolygonOutline(spriteBatch, points, UiTheme.Ink, 5);
                Primitives2D.PolygonOutline(spriteBatch, points, orbitColor, 3);
            }
        }
        int markerIndex = Math.Min(vertices.Length - 1, (int)((1 - progress) * (vertices.Length - 1)));
        Primitives2D.FillCircle(spriteBatch, vertices[markerIndex], 5, UiTheme.Cream);
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawPathArena(spriteBatch, camera, playerWorldPosition, screenShake);
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var inset = rect;
        inset.Inflate(-(int)(Size * .34f), -(int)(Size * .34f));
        Primitives2D.FillEllipse(spriteBatch, inset, UiTheme.Ink);
        Primitives2D.EllipseOutline(spriteBatch, inset, PhaseAccent, Math.Max(3, (int)(Size * .06f)));
        foreach (float offset in new[] { -.22f, .22f })
        {
            float x = rect.Center.X + rect.Width * offset;
            Primitives2D.Line(spriteBatch, new Vector2(x, rect.Y + rect.Height * .22f), new Vector2(x, rect.Bottom - rect.Height * .18f),
                UiTheme.Lighten(PhaseAccent, 42), 3);
        }
    }
}
