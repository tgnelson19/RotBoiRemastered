using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Projectiles fired by enemies, including reusable boss path primitives
/// (linear, sine, pool, laser, bomb, orbit, splitting). Ported from
/// enemyProjectile.py.
///
/// Cleanup vs. the Python original: Update (physics/state/spawning) and Draw
/// (rendering) are split -- Python's updateAndDraw(screen) did both at once,
/// which meant the pool/laser/bomb/split branching logic couldn't be unit
/// tested without a real Surface. `Trail` now stores world-space points
/// instead of screen-space pixels -- Python recomputed posX/posY once per
/// frame and appended straight to the trail, which is fine as long as
/// Update and Draw always run back-to-back in the same frame, but breaks
/// that implicit coupling. World-space points converted through the camera
/// at Draw time have no such coupling and are strictly more correct if the
/// camera ever rotates between a point being recorded and drawn.
/// </summary>
public sealed class EnemyProjectile
{
    private const float HostileSpeedScale = .52f;
    private const float DissonanceDamageScale = 1.3f;

    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float OriginX { get; }
    public float OriginY { get; }
    public float Direction { get; set; }
    public float Speed { get; set; }
    public float Damage { get; set; }
    public float Size { get; set; }
    public float RemainingRange { get; set; }
    public Color Color { get; set; }
    public string? ContentPath { get; set; }
    public string Shape { get; }
    public string Path { get; }
    public float Amplitude { get; }
    public float Frequency { get; }
    public float? Lifetime { get; set; }
    public float SpeedDecay { get; }
    public Vector2? OrbitCenter { get; set; }
    public float OrbitRadius { get; set; }
    public float OrbitAngle { get; set; }
    public float AngularSpeed { get; set; }
    public string? Owner { get; }
    public bool Illusory { get; set; }
    public bool TruthMarked { get; set; }
    public double BeliefGain { get; set; }
    public double ClarityGain { get; set; }
    /// <summary>Ported from bossTypes.py's SinChemesthesisBoss._shot duck-typed `affliction`/`afflictionDuration`/`afflictionStrength`/`exposure`/`afflictionSource` attributes -- applied to RunState.BossAfflictions on player hit (see GameSession.HurtPlayer). Null means "no affliction," matching Python's `getattr(projectile, "affliction", None)`.</summary>
    public string? Affliction { get; set; }
    public double AfflictionDuration { get; set; }
    public double AfflictionStrength { get; set; }
    public double Exposure { get; set; }
    public Vector2? AfflictionSource { get; set; }
    public bool IgnoreWalls { get; }
    public Vector2? Target { get; set; }
    public float TelegraphDuration { get; set; } = 1.0f;
    public float FuseDuration { get; set; } = 3.0f;
    public float BlastRadius { get; set; }
    public int BurstCount { get; set; } = 8;
    public float BurstDamage { get; set; }
    public float BurstRangeTiles { get; set; } = 24f;
    public List<EnemyProjectile> SpawnedProjectiles { get; } = new();
    public int SplitCount { get; set; }
    public float? SplitAt { get; set; }
    public int SplitGeneration { get; set; }
    /// <summary>Settable: Malady's purple pool (bossTypes.py's _spawn_pool) overrides the path=="laser" default so its hazard lingers instead of being consumed on the player's first hit.</summary>
    public bool PersistentHazard { get; set; }
    public bool Exploded { get; private set; }
    public float Age { get; private set; }
    public float Travelled { get; private set; }
    public bool RemFlag { get; set; }
    public List<Vector2> Trail { get; } = new(5);
    private bool _difficultyTimingApplied;

    public EnemyProjectile(
        float worldX, float worldY, float direction, float speed, float damage, float size,
        float travelRange = 900f, Color? color = null, string shape = "square", string path = "linear",
        float amplitude = 0f, float frequency = .035f, float? lifetime = null, float speedDecay = 0f,
        Vector2? orbitCenter = null, float orbitRadius = 0f, float orbitAngle = 0f, float angularSpeed = 0f,
        string? owner = null, bool ignoreWalls = false, Vector2? target = null)
    {
        WorldX = worldX;
        WorldY = worldY;
        OriginX = worldX;
        OriginY = worldY;
        Direction = direction;
        Speed = speed;
        string ownerText = owner ?? "";
        float bossScale = ownerText.StartsWith("beaudis") || ownerText.StartsWith("dissonance") ? 100f : 1f;
        float dissonanceScale = ownerText.StartsWith("dissonance") ? DissonanceDamageScale : 1f;
        Damage = MathF.Round(damage * bossScale * dissonanceScale);
        Size = size;
        RemainingRange = travelRange;
        Color = color ?? UiTheme.Red;
        Shape = shape;
        Path = path;
        Amplitude = amplitude;
        Frequency = frequency;
        Lifetime = lifetime;
        SpeedDecay = speedDecay;
        OrbitCenter = orbitCenter;
        OrbitRadius = orbitRadius;
        OrbitAngle = orbitAngle;
        AngularSpeed = angularSpeed;
        Owner = owner;

        // Dissonance bullets should paint complete lanes across the final arena.
        // Mines retain their deliberately local range and orbit fields retain lifetime rules.
        if (ownerText.StartsWith("dissonance") && path != "mine" && path != "orbit" && lifetime is null)
            RemainingRange = Math.Max(RemainingRange, Simulation.TileSize * 72f);
        if (ownerText.Contains("survival") || ownerText.Contains("boundary_inward"))
            RemainingRange = float.PositiveInfinity;

        IgnoreWalls = ignoreWalls;
        Target = target;
        BlastRadius = Simulation.TileSize * 1.5f;
        BurstDamage = Damage;
        PersistentHazard = path == "laser";
    }

    public Rectangle WorldRect()
    {
        if (Path == "laser" && Age >= TelegraphDuration)
        {
            float endX = WorldX + MathF.Cos(Direction) * RemainingRange;
            float endY = WorldY + MathF.Sin(Direction) * RemainingRange;
            float x = Math.Min(WorldX, endX), y = Math.Min(WorldY, endY);
            float w = Math.Max(Size, Math.Abs(endX - WorldX)), h = Math.Max(Size, Math.Abs(endY - WorldY));
            return new Rectangle((int)x, (int)y, (int)w, (int)h);
        }
        return new Rectangle((int)WorldX, (int)WorldY, (int)Size, (int)Size);
    }

    public bool Collides(Rectangle rect)
    {
        if (Illusory)
            return false;
        if (Path is "mine" or "bank" && Age < TelegraphDuration)
            return false;
        if (Path == "pool")
        {
            if (Age < TelegraphDuration)
                return false;
            float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
            float nearestX = Math.Clamp(centerX, rect.Left, rect.Right);
            float nearestY = Math.Clamp(centerY, rect.Top, rect.Bottom);
            float radius = Size * .46f;
            return (nearestX - centerX) * (nearestX - centerX) + (nearestY - centerY) * (nearestY - centerY) <= radius * radius;
        }
        if (Path == "laser")
        {
            if (Age < TelegraphDuration)
                return false;
            var start = new Vector2(WorldX, WorldY);
            var end = new Vector2(WorldX + MathF.Cos(Direction) * RemainingRange, WorldY + MathF.Sin(Direction) * RemainingRange);
            var inflated = rect;
            inflated.Inflate((int)Size, (int)Size);
            return SegmentIntersectsRect(start, end, inflated);
        }
        if (Path == "bomb")
        {
            if (!Exploded)
                return false;
            float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
            float nearestX = Math.Clamp(centerX, rect.Left, rect.Right);
            float nearestY = Math.Clamp(centerY, rect.Top, rect.Bottom);
            return (nearestX - centerX) * (nearestX - centerX) + (nearestY - centerY) * (nearestY - centerY) <= BlastRadius * BlastRadius;
        }
        return rect.Intersects(WorldRect());
    }

    public void Update(Battleground battleground, bool casualMode, bool hardMode = false)
    {
        if (!_difficultyTimingApplied)
        {
            float warningScale = casualMode ? 1.25f : hardMode ? .86f : 1f;
            TelegraphDuration *= warningScale;
            if (Path == "bomb")
                FuseDuration *= warningScale;
            _difficultyTimingApplied = true;
        }
        float seconds = (float)Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        Age += seconds;

        switch (Path)
        {
            case "pool":
                if (Age >= (Lifetime ?? 8.0f))
                    RemFlag = true;
                return;

            case "laser":
                if (Age >= TelegraphDuration && AngularSpeed != 0)
                    Direction += AngularSpeed * seconds;
                if (Lifetime is not null && Age >= Lifetime)
                    RemFlag = true;
                return;

            case "bomb":
                if (Age < 1.0f && Target.HasValue)
                {
                    float progress = Math.Min(1.0f, Age);
                    WorldX = OriginX + (Target.Value.X - OriginX) * progress;
                    WorldY = OriginY + (Target.Value.Y - OriginY) * progress
                        - MathF.Sin(progress * MathF.PI) * Simulation.TileSize * 2.5f;
                }
                else if (Age >= FuseDuration && !Exploded)
                {
                    Exploded = true;
                    for (int index = 0; index < BurstCount; index++)
                    {
                        SpawnedProjectiles.Add(new EnemyProjectile(
                            WorldX, WorldY, index * 2f * MathF.PI / Math.Max(1, BurstCount), .9f,
                            BurstDamage * .28f, Simulation.TileSize * .38f,
                            travelRange: Simulation.TileSize * BurstRangeTiles, color: Color, shape: "diamond",
                            owner: $"{Owner}_burst", ignoreWalls: true));
                    }
                }
                else if (Exploded && Age >= FuseDuration + .18f)
                {
                    RemFlag = true;
                }
                break;

            case "orbit" when OrbitCenter.HasValue:
                OrbitAngle += AngularSpeed * seconds;
                WorldX = OrbitCenter.Value.X + MathF.Cos(OrbitAngle) * OrbitRadius - Size / 2f;
                WorldY = OrbitCenter.Value.Y + MathF.Sin(OrbitAngle) * OrbitRadius - Size / 2f;
                break;

            default:
                float comfortScale = casualMode ? .88f : 1.0f;
                float distance = Speed * HostileSpeedScale * comfortScale * (float)Simulation.GetFrameScale();
                Travelled += distance;
                RemainingRange -= distance;
                if (Path == "sine")
                {
                    float lateral = MathF.Sin(Travelled * Frequency) * Amplitude;
                    WorldX = OriginX + MathF.Cos(Direction) * Travelled - MathF.Sin(Direction) * lateral;
                    WorldY = OriginY + MathF.Sin(Direction) * Travelled + MathF.Cos(Direction) * lateral;
                }
                else
                {
                    WorldX += MathF.Cos(Direction) * distance;
                    WorldY += MathF.Sin(Direction) * distance;
                }
                if (SpeedDecay != 0)
                    Speed = Math.Max(0, Speed - SpeedDecay * seconds);
                if (SplitCount > 1 && SplitAt.HasValue && Travelled >= SplitAt.Value && !Exploded)
                {
                    Exploded = true;
                    float spread = .8f + .12f * SplitGeneration;
                    for (int index = 0; index < SplitCount; index++)
                    {
                        float fraction = SplitCount == 1 ? .5f : (float)index / (SplitCount - 1);
                        var child = new EnemyProjectile(
                            WorldX, WorldY, Direction - spread / 2f + spread * fraction,
                            Speed * 1.08f, Damage * .58f, Size * .72f,
                            travelRange: Math.Max(Simulation.TileSize * 5f, RemainingRange),
                            color: Color, shape: "diamond", owner: Owner, ignoreWalls: IgnoreWalls);
                        if (SplitGeneration > 0)
                        {
                            child.SplitCount = SplitCount;
                            child.SplitAt = Math.Max(Simulation.TileSize * 2.5f, RemainingRange * .42f);
                            child.SplitGeneration = SplitGeneration - 1;
                        }
                        SpawnedProjectiles.Add(child);
                    }
                    RemFlag = true;
                }
                break;
        }

        // Common tail for bomb/orbit/default -- pool/laser returned above.
        Trail.Add(new Vector2(WorldX + Size / 2f, WorldY + Size / 2f));
        if (Trail.Count > 5)
            Trail.RemoveAt(0);

        bool expired = Lifetime is not null && Age >= Lifetime;
        bool rangeSpent = Path != "orbit" && RemainingRange <= 0;
        bool wallHit = !IgnoreWalls && battleground.RectHitsWall(WorldRect());
        if (expired || rangeSpent || wallHit)
            RemFlag = true;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake, bool highContrast)
    {
        if (Path == "pool")
        {
            DrawPool(spriteBatch, camera, playerWorldPosition, screenShake);
            return;
        }
        if (Path == "laser")
        {
            DrawLaser(spriteBatch, camera, playerWorldPosition, screenShake);
            return;
        }
        if (Path == "bank")
        {
            DrawBank(spriteBatch, camera, playerWorldPosition, screenShake, highContrast);
            return;
        }

        float visibleSize = ProjectileVisuals.NormalizeDrawSize(
            Size,
            camera.Zoom);
        Vector2 centerWorld = new(
            WorldX + Size / 2f,
            WorldY + Size / 2f);
        Vector2 centerScreen = camera.WorldToScreen(
            centerWorld,
            playerWorldPosition,
            screenShake);
        var rect = new Rectangle(
            (int)(centerScreen.X - visibleSize / 2f),
            (int)(centerScreen.Y - visibleSize / 2f),
            (int)MathF.Ceiling(visibleSize),
            (int)MathF.Ceiling(visibleSize));

        float vfxIntensity = (float)GameProfile.Profile.VisualEffectsIntensity;
        if (vfxIntensity > 0 && Trail.Count > 1)
        {
            int visibleTrail = Math.Max(1,
                (int)MathF.Ceiling((Trail.Count - 1) * vfxIntensity));
            int firstTrail = Math.Max(0, Trail.Count - 1 - visibleTrail);
            for (int index = firstTrail; index < Trail.Count - 1; index++)
            {
                Vector2 trailScreen = camera.WorldToScreen(Trail[index], playerWorldPosition, screenShake);
                int trailSize = Math.Max(
                    2,
                    (int)(visibleSize * (index + 1)
                        / Trail.Count * .22f));
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)(trailScreen.X - trailSize / 2f), (int)(trailScreen.Y - trailSize / 2f), trailSize, trailSize),
                    UiTheme.Ink);
                if (index >= Trail.Count - 3)
                {
                    int coreSize = Math.Max(1, trailSize / 2);
                    Primitives2D.FillRect(spriteBatch,
                        new Rectangle((int)(trailScreen.X - coreSize / 2f), (int)(trailScreen.Y - coreSize / 2f), coreSize, coreSize),
                        Color);
                }
            }
        }

        string visualShape = ResolveVisualShape();
        Color dangerTrim = SoulVisualLanguage.CueColor(
            VisualSemanticCue.Hostile,
            SoulVisualLanguage.Path(ContentPath ?? GamePaths.Active().Key),
            highContrast);
        DrawDangerTrim(
            spriteBatch,
            rect,
            visibleSize,
            camera.WorldVectorToScreen(
                new Vector2(MathF.Cos(Direction), MathF.Sin(Direction))),
            visualShape,
            dangerTrim);
        if (Shape is "diamond" or "mine" or "bomb")
            DrawDiamondShape(spriteBatch, rect, visibleSize);
        else if (visualShape != "square")
            DrawCustomShape(
                spriteBatch,
                new Vector2(rect.Center.X, rect.Center.Y),
                visibleSize,
                camera.WorldVectorToScreen(
                    new Vector2(MathF.Cos(Direction), MathF.Sin(Direction))),
                visualShape);
        else
            DrawSquareShape(spriteBatch, rect, visibleSize);

        if (highContrast)
            Primitives2D.RectOutline(
                spriteBatch,
                InflateF(rect, 4, 4),
                UiTheme.Cream,
                Math.Max(2, (int)(visibleSize * .08f)));

        var center = new Vector2(rect.Center.X, rect.Center.Y);
        if (Age < .1f)
        {
            int ignition = Math.Max(2, (int)(visibleSize * .12f));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle(rect.Center.X - ignition / 2,
                    rect.Center.Y - ignition / 2, ignition, ignition),
                SoulVisualLanguage.CueColor(
                    VisualSemanticCue.HostileIgnition,
                    SoulVisualLanguage.Path(
                        ContentPath ?? GamePaths.Active().Key),
                    highContrast));
        }
        if (TruthMarked)
            Primitives2D.FillCircle(
                spriteBatch,
                center,
                Math.Max(2, (int)(visibleSize * .1f)),
                UiTheme.Cream);
        else if (Illusory)
            Primitives2D.CircleOutline(
                spriteBatch,
                center,
                Math.Max(3, (int)(visibleSize * .22f)),
                UiTheme.Muted,
                2);
    }

    private void DrawDangerTrim(
        SpriteBatch spriteBatch,
        Rectangle rect,
        float visibleSize,
        Vector2 forward,
        string visualShape,
        Color dangerTrim)
    {
        float trimSize = visibleSize * 1.18f;
        Vector2 center = rect.Center.ToVector2();
        if (Shape is "diamond" or "mine" or "bomb")
        {
            Primitives2D.FillQuad(spriteBatch,
                center + new Vector2(0, -trimSize * .5f),
                center + new Vector2(trimSize * .5f, 0),
                center + new Vector2(0, trimSize * .5f),
                center - new Vector2(trimSize * .5f, 0),
                dangerTrim);
            return;
        }
        if (visualShape == "square")
        {
            int trim = Math.Max(2, (int)(visibleSize * .09f));
            Primitives2D.FillRect(spriteBatch,
                InflateF(rect, trim, trim), dangerTrim);
            return;
        }
        forward = forward.LengthSquared() > .0001f
            ? Vector2.Normalize(forward)
            : Vector2.UnitX;
        if (visualShape is not ("wave" or "tuning_fork" or "chevron" or "needle"))
        {
            float spin = Age
                * (visualShape is "star" or "cracked_core" ? 2.8f : 1.4f)
                + StableVisualVariant() * .73f;
            forward = Rotate(forward, spin);
        }
        DrawCustomShapeLayer(
            spriteBatch,
            center,
            trimSize,
            forward,
            new Vector2(-forward.Y, forward.X),
            visualShape,
            dangerTrim,
            shadow: true);
    }

    public string ResolveVisualShape()
    {
        if (Shape != "square")
            return Shape;
        string path = ContentPath ?? GamePaths.Active().Key;
        int variant = StableVisualVariant();
        return path switch
        {
            "sound" => variant switch { 0 => "wave", 1 => "tuning_fork", _ => "chevron" },
            "touch" => variant switch { 0 => "rivet", 1 => "chain_link", _ => "slab" },
            "sight" => variant switch { 0 => "eye", 1 => "needle", _ => "lens" },
            "chemesthesis" => variant switch { 0 => "ember", 1 => "spore", _ => "cracked_core" },
            "phantasia" => variant switch { 0 => "star", 1 => "crescent", _ => "orbit_core" },
            _ => "square",
        };
    }

    private int StableVisualVariant()
    {
        int value = (int)MathF.Abs(OriginX * .17f + OriginY * .11f);
        if (Owner is not null)
        {
            for (int index = 0; index < Owner.Length; index++)
                value = unchecked(value * 31 + Owner[index]);
        }
        return Math.Abs(value % 3);
    }

    private void DrawCustomShape(
        SpriteBatch spriteBatch,
        Vector2 center,
        float size,
        Vector2 forward,
        string visualShape)
    {
        forward = forward.LengthSquared() < .0001f
            ? Vector2.UnitX
            : Vector2.Normalize(forward);
        float spin = Age * (visualShape is "star" or "cracked_core" ? 2.8f : 1.4f)
            + StableVisualVariant() * .73f;
        if (visualShape is "wave" or "tuning_fork" or "chevron" or "needle")
            spin = 0;
        else
            forward = Rotate(forward, spin);
        Vector2 side = new(-forward.Y, forward.X);

        DrawCustomShapeLayer(
            spriteBatch, center + new Vector2(3, 4), size, forward, side,
            visualShape, UiTheme.Shadow, shadow: true);
        DrawCustomShapeLayer(
            spriteBatch, center, size, forward, side,
            visualShape, Color, shadow: false);
    }

    private static void DrawCustomShapeLayer(
        SpriteBatch spriteBatch,
        Vector2 center,
        float size,
        Vector2 forward,
        Vector2 side,
        string visualShape,
        Color color,
        bool shadow)
    {
        Vector2 P(float x, float y) => center + forward * (x * size) + side * (y * size);
        int stroke = Math.Max(2, (int)(size * .09f));
        Color edge = shadow ? color : UiTheme.Ink;
        Color light = shadow ? color : UiTheme.Lighten(color, 52);

        switch (visualShape)
        {
            case "wave":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-.62f, -.13f), P(-.28f, -.43f), P(.05f, -.12f),
                    P(.34f, -.36f), P(.68f, 0), P(.34f, .36f),
                    P(.05f, .12f), P(-.28f, .43f), P(-.62f, .13f),
                }, color);
                if (!shadow)
                    Primitives2D.Line(spriteBatch, P(-.35f, 0), P(.42f, 0), light, stroke);
                break;
            case "tuning_fork":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-.66f, -.35f), P(.02f, -.35f), P(.02f, -.14f),
                    P(.65f, -.14f), P(.65f, .14f), P(.02f, .14f),
                    P(.02f, .35f), P(-.66f, .35f), P(-.38f, 0),
                }, color);
                if (!shadow)
                    Primitives2D.FillCircle(spriteBatch, P(.38f, 0), size * .12f, light);
                break;
            case "chevron":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-.65f, -.42f), P(.68f, 0), P(-.65f, .42f),
                    P(-.35f, 0),
                }, color);
                if (!shadow)
                    Primitives2D.PolylineSpan(spriteBatch, stackalloc Vector2[]
                    {
                        P(-.42f, -.26f), P(.36f, 0), P(-.42f, .26f),
                    }, false, light, stroke);
                break;
            case "rivet":
                Primitives2D.FillCircle(spriteBatch, center, size * .52f, color);
                if (!shadow)
                {
                    Primitives2D.CircleOutline(spriteBatch, center, size * .5f, edge, stroke, 16);
                    Primitives2D.FillRect(spriteBatch,
                        new Rectangle((int)(center.X - size * .17f), (int)(center.Y - stroke / 2f),
                            Math.Max(3, (int)(size * .34f)), stroke), light);
                }
                break;
            case "chain_link":
                Primitives2D.FillEllipse(spriteBatch,
                    new Rectangle((int)(center.X - size * .62f), (int)(center.Y - size * .34f),
                        (int)(size * 1.24f), (int)(size * .68f)), color);
                if (!shadow)
                    Primitives2D.EllipseOutline(spriteBatch,
                        new Rectangle((int)(center.X - size * .38f), (int)(center.Y - size * .17f),
                            (int)(size * .76f), (int)(size * .34f)), edge, stroke, 18);
                break;
            case "slab":
                {
                    var slab = new Rectangle((int)(center.X - size * .62f), (int)(center.Y - size * .35f),
                        (int)(size * 1.24f), (int)(size * .7f));
                    Primitives2D.FillRect(spriteBatch, slab, color);
                    if (!shadow)
                    {
                        Primitives2D.RectOutline(spriteBatch, slab, edge, stroke);
                        Primitives2D.Line(spriteBatch, P(-.18f, -.3f), P(.12f, .28f), light, 2);
                    }
                    break;
                }
            case "eye":
                Primitives2D.FillEllipse(spriteBatch,
                    new Rectangle((int)(center.X - size * .62f), (int)(center.Y - size * .34f),
                        (int)(size * 1.24f), (int)(size * .68f)), color);
                if (!shadow)
                {
                    Primitives2D.EllipseOutline(spriteBatch,
                        new Rectangle((int)(center.X - size * .62f), (int)(center.Y - size * .34f),
                            (int)(size * 1.24f), (int)(size * .68f)), edge, stroke, 20);
                    Primitives2D.FillCircle(spriteBatch, center, size * .18f, light);
                }
                break;
            case "needle":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-.7f, -.16f), P(.2f, -.23f), P(.72f, 0),
                    P(.2f, .23f), P(-.7f, .16f),
                }, color);
                if (!shadow)
                    Primitives2D.Line(spriteBatch, P(-.42f, 0), P(.42f, 0), light, 2);
                break;
            case "lens":
                Primitives2D.FillQuad(spriteBatch,
                    P(0, -.58f), P(.66f, 0), P(0, .58f), P(-.66f, 0), color);
                if (!shadow)
                {
                    Primitives2D.QuadOutline(spriteBatch,
                        P(0, -.58f), P(.66f, 0), P(0, .58f), P(-.66f, 0), edge, stroke);
                    Primitives2D.FillCircle(spriteBatch, center, size * .18f, light);
                }
                break;
            case "ember":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -.66f), P(.42f, -.22f), P(.58f, .3f),
                    P(.06f, .55f), P(-.48f, .34f), P(-.38f, -.28f),
                }, color);
                if (!shadow)
                    Primitives2D.FillCircle(spriteBatch, center, size * .18f, light);
                break;
            case "spore":
                Primitives2D.FillCircle(spriteBatch, center, size * .46f, color);
                Primitives2D.FillCircle(spriteBatch, P(.38f, -.28f), size * .22f, color);
                Primitives2D.FillCircle(spriteBatch, P(-.36f, .3f), size * .2f, color);
                if (!shadow)
                    Primitives2D.FillCircle(spriteBatch, P(.1f, -.08f), size * .11f, light);
                break;
            case "cracked_core":
                Primitives2D.FillQuad(spriteBatch,
                    P(0, -.62f), P(.62f, 0), P(0, .62f), P(-.62f, 0), color);
                if (!shadow)
                {
                    Primitives2D.Line(spriteBatch, P(-.1f, -.48f), P(.08f, -.05f), edge, 2);
                    Primitives2D.Line(spriteBatch, P(.08f, -.05f), P(-.18f, .44f), light, 2);
                }
                break;
            case "star":
                {
                    Span<Vector2> star = stackalloc Vector2[10];
                    for (int index = 0; index < star.Length; index++)
                    {
                        float angle = -MathF.PI / 2f + index * MathF.PI / 5f;
                        float radius = index % 2 == 0 ? .66f : .28f;
                        star[index] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * size * radius;
                    }
                    Primitives2D.FillPolygonSpan(spriteBatch, star, color);
                    if (!shadow)
                        Primitives2D.FillCircle(spriteBatch, center, size * .14f, light);
                    break;
                }
            case "crescent":
                Primitives2D.FillCircle(spriteBatch, center, size * .55f, color);
                if (!shadow)
                {
                    Primitives2D.FillCircle(spriteBatch, center + forward * size * .22f,
                        size * .43f, UiTheme.Ink);
                    Primitives2D.FillCircle(spriteBatch, center - forward * size * .2f,
                        size * .09f, light);
                }
                break;
            case "orbit_core":
                Primitives2D.FillCircle(spriteBatch, center, size * .36f, color);
                Primitives2D.FillCircle(spriteBatch, P(.62f, 0), size * .16f, color);
                Primitives2D.FillCircle(spriteBatch, P(-.62f, 0), size * .16f, color);
                if (!shadow)
                    Primitives2D.CircleOutline(spriteBatch, center, size * .58f, light, 2, 18);
                break;
        }
    }

    private static Vector2 Rotate(Vector2 value, float angle)
    {
        float cosine = MathF.Cos(angle);
        float sine = MathF.Sin(angle);
        return new Vector2(
            value.X * cosine - value.Y * sine,
            value.X * sine + value.Y * cosine);
    }

    private void DrawSquareShape(
        SpriteBatch spriteBatch,
        Rectangle rect,
        float visibleSize)
    {
        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, Color);
        Primitives2D.RectOutline(
            spriteBatch,
            rect,
            UiTheme.Ink,
            Math.Max(2, (int)(visibleSize * .1f)));
        Primitives2D.FillRect(
            spriteBatch,
            InflateF(
                rect,
                -(int)(visibleSize * .5f),
                -(int)(visibleSize * .5f)),
            UiTheme.Lighten(Color, 45));
    }

    private void DrawDiamondShape(
        SpriteBatch spriteBatch,
        Rectangle rect,
        float visibleSize)
    {
        Vector2 top = new(rect.X + rect.Width / 2f, rect.Y);
        Vector2 right = new(rect.Right, rect.Y + rect.Height / 2f);
        Vector2 bottom = new(rect.X + rect.Width / 2f, rect.Bottom);
        Vector2 left = new(rect.X, rect.Y + rect.Height / 2f);
        Vector2 shadow = new(3, 3);
        Primitives2D.FillQuad(
            spriteBatch, top + shadow, right + shadow, bottom + shadow, left + shadow,
            UiTheme.Shadow);
        Primitives2D.FillQuad(spriteBatch, top, right, bottom, left, Color);
        Primitives2D.QuadOutline(
            spriteBatch, top, right, bottom, left,
            UiTheme.Ink, Math.Max(2, (int)(visibleSize * .1f)));

        var center = new Vector2(rect.Center.X, rect.Center.Y);
        if (Shape == "mine")
        {
            int pulse = Math.Max(
                3,
                (int)(visibleSize
                    * (.12f + .05f * (1 + MathF.Sin(Age * 5f)))));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)(center.X - pulse / 2f), (int)(center.Y - pulse / 2f), pulse, pulse), UiTheme.Text);
            if (Age < TelegraphDuration)
            {
                float warningProgress = Age / Math.Max(.01f, TelegraphDuration);
                float warningRadius = visibleSize
                    * (.72f + (1f - warningProgress) * .42f);
                Primitives2D.CircleOutline(spriteBatch, center, warningRadius, UiTheme.Cream,
                    Math.Max(2, (int)(visibleSize * .07f)));
            }
        }
        else if (Shape == "bomb")
        {
            float fuse = Math.Max(0, FuseDuration - Age);
            Primitives2D.FillCircle(
                spriteBatch,
                center,
                Math.Max(
                    3,
                    (int)(visibleSize
                        * (.1f + .04f * MathF.Sin(Age * 14f)))),
                UiTheme.Cream);
            if (Age >= 1.0f)
            {
                var warning = new Rectangle(0, 0, (int)(BlastRadius * 2), (int)(BlastRadius * 2));
                CenterOn(ref warning, rect.Center);
                float urgency = 1 - fuse / Math.Max(.01f, FuseDuration - 1.0f);
                Primitives2D.EllipseOutline(spriteBatch, warning, UiTheme.Red, Math.Max(2, (int)(2 + urgency * 3)));
                Primitives2D.Arc(spriteBatch, InflateF(rect, 8, 8), -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * Math.Max(0, urgency), UiTheme.Cream, 3);
            }
            if (Exploded)
            {
                var blast = new Rectangle(0, 0, (int)(BlastRadius * 2), (int)(BlastRadius * 2));
                CenterOn(ref blast, rect.Center);
                Primitives2D.EllipseOutline(
                    spriteBatch,
                    blast,
                    UiTheme.Gold,
                    Math.Max(5, (int)(visibleSize * .2f)));
            }
        }
    }

    private void DrawPool(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPos = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPos.X, (int)(screenPos.Y + Size / 2f - Size * .29f), (int)Size, (int)(Size * .58f));
        float lifetime = Lifetime ?? 8.0f;
        float appearing = Math.Min(1.0f, Age / Math.Max(.01f, TelegraphDuration));
        float fading = Math.Min(1.0f, Math.Max(0.0f, lifetime - Age) / .7f);
        float scale = Math.Max(.08f, Math.Min(appearing, fading));
        var visible = InflateF(rect, -rect.Width * (1 - scale), -rect.Height * (1 - scale));

        Primitives2D.FillEllipse(spriteBatch, InflateF(visible, 10, 7), UiTheme.Shadow);
        Primitives2D.FillEllipse(spriteBatch, InflateF(visible, 5, 3), UiTheme.Ink);
        Primitives2D.FillEllipse(spriteBatch, visible, Color);
        var inner = InflateF(visible, -visible.Width * .18f, -visible.Height * .24f);
        Primitives2D.EllipseOutline(spriteBatch, inner, UiTheme.Lighten(Color, 34), 3);

        for (int index = 0; index < 5; index++)
        {
            float angle = Age * (1.8f + index * .13f) + index * 2f * MathF.PI / 5f;
            float radiusX = visible.Width * .34f, radiusY = visible.Height * .27f;
            var point = new Vector2(visible.Center.X + MathF.Cos(angle) * radiusX, visible.Center.Y + MathF.Sin(angle) * radiusY);
            int mote = Math.Max(2, (int)(Size * (.025f + .008f * MathF.Sin(Age * 5f + index))));
            Primitives2D.FillCircle(spriteBatch, point, mote + 2, UiTheme.Ink);
            Primitives2D.FillCircle(spriteBatch, point, mote, UiTheme.Cream);
        }

        if (Age < TelegraphDuration)
        {
            float progress = Age / Math.Max(.01f, TelegraphDuration);
            var warning = InflateF(visible, 12, 8);
            Primitives2D.Arc(spriteBatch, warning, -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * progress, UiTheme.Cream, 3);
        }
    }

    private void DrawBank(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition,
        Vector2 screenShake, bool highContrast)
    {
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var slab = new Rectangle((int)screen.X, (int)(screen.Y + Size * .18f), (int)Size, (int)(Size * .64f));
        float warning = Math.Clamp(Age / Math.Max(.01f, TelegraphDuration), 0f, 1f);

        if (Age < TelegraphDuration)
        {
            Color ghost = Color * (.2f + warning * .32f);
            Primitives2D.FillRect(spriteBatch, slab, ghost);
            Primitives2D.RectOutline(spriteBatch, slab, UiTheme.Cream, Math.Max(2, (int)(Size * .055f)));
            for (int seam = 1; seam < 4; seam++)
            {
                int x = slab.X + slab.Width * seam / 4;
                Primitives2D.Line(spriteBatch, new Vector2(x, slab.Top), new Vector2(x, slab.Bottom),
                    UiTheme.Cream * .55f, 2);
            }
            return;
        }

        var shadow = slab;
        shadow.Offset(5, 7);
        Primitives2D.FillRect(spriteBatch, shadow, UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, slab, Color);
        Primitives2D.RectOutline(spriteBatch, slab, UiTheme.Ink, Math.Max(3, (int)(Size * .075f)));

        // Broad sediment courses and sparse cracks read as one advancing wall
        // of compacted matter, rather than another boss's jeweled projectile.
        for (int course = 1; course < 3; course++)
        {
            int y = slab.Y + slab.Height * course / 3;
            Primitives2D.Line(spriteBatch, new Vector2(slab.Left + 3, y),
                new Vector2(slab.Right - 3, y), UiTheme.Lighten(Color, 24), 2);
        }
        for (int crack = 0; crack < 3; crack++)
        {
            float x = slab.Left + slab.Width * (.22f + crack * .28f);
            Primitives2D.Polyline(spriteBatch, new[]
            {
                new Vector2(x, slab.Top + 4),
                new Vector2(x + (crack % 2 == 0 ? 7 : -6), slab.Center.Y),
                new Vector2(x - 3, slab.Bottom - 4),
            }, false, UiTheme.Ink, 2);
        }
        if (highContrast)
            Primitives2D.RectOutline(spriteBatch, InflateF(slab, 4, 4), UiTheme.Cream, 3);
    }

    private void DrawLaser(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 start = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var endWorld = new Vector2(WorldX + MathF.Cos(Direction) * RemainingRange, WorldY + MathF.Sin(Direction) * RemainingRange);
        Vector2 end = camera.WorldToScreen(endWorld, playerWorldPosition, screenShake);

        if (Age < TelegraphDuration)
        {
            float progress = Age / Math.Max(.01f, TelegraphDuration);
            int pulse = 2 + (int)((1 - progress) * 3);
            Primitives2D.Line(spriteBatch, start, end, Color, pulse);
            for (int step = 0; step < 5; step++)
            {
                var marker = new Vector2((start.X * (4 - step) + end.X * step) / 4f, (start.Y * (4 - step) + end.Y * step) / 4f);
                Primitives2D.FillCircle(spriteBatch, marker, 3, UiTheme.Cream);
            }
        }
        else
        {
            int width = Math.Max(8, (int)(Size * (1.15f + .18f * MathF.Sin(Age * 18f))));
            Primitives2D.Line(spriteBatch, start, end, UiTheme.Ink, width + 8);
            Primitives2D.Line(spriteBatch, start, end, Color, width);
            Color coreColor = Illusory ? UiTheme.Muted : UiTheme.Cream;
            Primitives2D.Line(spriteBatch, start, end, coreColor, Math.Max(2, width / 3));
            Primitives2D.FillCircle(spriteBatch, start, Math.Max(4, width / 2), Color);
            Primitives2D.CircleOutline(spriteBatch, start, Math.Max(5, width / 2), UiTheme.Ink, 2, 18);
            Primitives2D.FillCircle(spriteBatch, end, Math.Max(3, width / 3), coreColor);
            float intensity = (float)GameProfile.Profile.VisualEffectsIntensity;
            int packets = (int)MathF.Ceiling(5 * intensity);
            for (int index = 0; index < packets; index++)
            {
                float phase = (Age * (1.7f + index * .08f) + index / (float)Math.Max(1, packets)) % 1f;
                Vector2 packet = Vector2.Lerp(start, end, phase);
                int packetSize = Math.Max(2, width / 5);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)packet.X - packetSize / 2,
                        (int)packet.Y - packetSize / 2, packetSize, packetSize),
                    coreColor * (.75f * VisualAnimation.SeamFade(phase)));
            }
            if (TruthMarked)
                Primitives2D.FillCircle(spriteBatch, start, Math.Max(3, width / 3), UiTheme.Cream);
        }
    }

    private static Rectangle InflateF(Rectangle rect, float dx, float dy)
    {
        var result = rect;
        result.Inflate((int)MathF.Round(dx), (int)MathF.Round(dy));
        return result;
    }

    private static void CenterOn(ref Rectangle rect, Point center)
    {
        rect.X = center.X - rect.Width / 2;
        rect.Y = center.Y - rect.Height / 2;
    }

    private static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Rectangle rect)
    {
        if (rect.Contains((int)start.X, (int)start.Y) || rect.Contains((int)end.X, (int)end.Y))
            return true;
        var topLeft = new Vector2(rect.Left, rect.Top);
        var topRight = new Vector2(rect.Right, rect.Top);
        var bottomRight = new Vector2(rect.Right, rect.Bottom);
        var bottomLeft = new Vector2(rect.Left, rect.Bottom);
        return SegmentsIntersect(start, end, topLeft, topRight)
            || SegmentsIntersect(start, end, topRight, bottomRight)
            || SegmentsIntersect(start, end, bottomRight, bottomLeft)
            || SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d1 = Cross(p4 - p3, p1 - p3);
        float d2 = Cross(p4 - p3, p2 - p3);
        float d3 = Cross(p2 - p1, p3 - p1);
        float d4 = Cross(p2 - p1, p4 - p1);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
