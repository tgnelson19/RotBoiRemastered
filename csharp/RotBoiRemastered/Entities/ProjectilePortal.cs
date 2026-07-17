using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>Ported from projectilePortal.py's fire_pattern_burst wave tuples: (pellets, spread, speed, sizeScale).</summary>
public readonly record struct BurstWave(int Pellets, float Spread, float Speed, float SizeScale);

/// <summary>
/// Boss-owned orbiting projectile emitter with a configurable inward
/// shotgun. Ported from projectilePortal.py.
///
/// Cleanup vs. the Python original: the queued follow-up wave list
/// (`burstQueue`) was a list of loosely-typed `[timer, target, pellets,
/// spread, speed, sizeScale, damage, color, ownerSuffix, directionOffset]`
/// entries -- now the private `PendingBurst` record. update/draw stay
/// combined via a caller-provided `List&lt;EnemyProjectile&gt;` sink (matching
/// Python's `projectile_sink` parameter), since portal firing genuinely is
/// state mutation, not rendering -- Draw itself takes no projectile sink.
/// </summary>
public sealed class ProjectilePortal
{
    private readonly record struct PendingBurst(
        float Timer, Vector2 Target, int PelletCount, float Spread, float Speed, float SizeScale,
        float Damage, Color? Color, string OwnerSuffix, float DirectionOffset);

    public Vector2 OrbitCenter { get; set; }
    public float Radius { get; set; }
    public float Angle { get; set; }
    public float AngularSpeed { get; }
    public float FireInterval { get; }
    public float FireCooldown { get; set; }
    public int PelletCount { get; }
    public float Spread { get; }
    public string Owner { get; }
    public Color Color { get; }
    public int Polarity { get; }
    public string MovementPath { get; }
    public float Size { get; } = Simulation.TileSize * .9f;
    public float MaxHp { get; } = 600f;
    public float Hp { get; private set; }
    public int HitsTaken { get; private set; }
    public int HitsToDisable { get; } = 3;
    public bool PhaseDisabled { get; private set; }
    public IReadOnlyList<Vector2[]> RuneStrokes { get; private set; } = Array.Empty<Vector2[]>();
    public float DisabledRemaining { get; private set; }
    public float RegenerationTime { get; } = 5.0f;
    public List<Vector2> Trail { get; } = new();
    public float TelegraphTimer { get; private set; }
    public string TelegraphKind { get; private set; } = "inward";
    public Vector2 TelegraphTarget { get; private set; }
    public bool ShowTether { get; set; } = true;
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    public bool RemFlag { get; set; }

    private readonly List<PendingBurst> _burstQueue = new();

    public ProjectilePortal(Vector2 center, float radius, float angle, float angularSpeed = .35f,
        float fireInterval = 1.7f, int pelletCount = 5, float spread = .72f, string owner = "dissonance_portal",
        Color? color = null, int polarity = 1, string movementPath = "orbit")
    {
        OrbitCenter = center;
        Radius = radius;
        Angle = angle;
        AngularSpeed = angularSpeed;
        FireInterval = fireInterval;
        FireCooldown = fireInterval * (Wrap2Pi(angle) / (2f * MathF.PI));
        PelletCount = pelletCount;
        Spread = spread;
        Owner = owner;
        Color = color ?? UiTheme.Purple;
        Polarity = polarity >= 0 ? 1 : -1;
        MovementPath = movementPath;
        Hp = MaxHp;
        Place();
    }

    private static float Wrap2Pi(float angle)
    {
        float twoPi = 2f * MathF.PI;
        float result = angle % twoPi;
        return result < 0 ? result + twoPi : result;
    }

    public bool Active => !RemFlag && DisabledRemaining <= 0;
    public bool BlocksShots => Active && !PhaseDisabled;

    /// <summary>Public (Python's `_place` was underscore-private in name only -- Beaudis's survival phase calls it directly to reposition a portal it's driving manually).</summary>
    public void Place()
    {
        float centerX = OrbitCenter.X, centerY = OrbitCenter.Y;
        float offsetX, offsetY;
        switch (MovementPath)
        {
            case "figure8":
                offsetX = MathF.Cos(Angle) * Radius;
                offsetY = MathF.Sin(Angle * 2) * Radius * .48f;
                break;
            case "square":
                float phase = Wrap2Pi(Angle) / (2f * MathF.PI) % 1f * 4f;
                int side = (int)phase;
                float progress = phase % 1f;
                var corners = new (float X, float Y)[] { (-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1) };
                var start = corners[side];
                var end = corners[side + 1];
                offsetX = (start.X + (end.X - start.X) * progress) * Radius;
                offsetY = (start.Y + (end.Y - start.Y) * progress) * Radius;
                break;
            case "tornado":
                float breathingRadius = Radius * (.72f + .28f * MathF.Sin(Angle * 1.7f));
                offsetX = MathF.Cos(Angle) * breathingRadius;
                offsetY = MathF.Sin(Angle) * breathingRadius * .62f;
                break;
            case "wave":
                offsetX = MathF.Cos(Angle) * Radius;
                offsetY = MathF.Sin(Angle * 3) * Radius * .38f;
                break;
            default:
                offsetX = MathF.Cos(Angle) * Radius;
                offsetY = MathF.Sin(Angle) * Radius;
                break;
        }
        WorldX = centerX + offsetX - Size / 2f;
        WorldY = centerY + offsetY - Size / 2f;
        var point = new Vector2(WorldX + Size / 2f, WorldY + Size / 2f);
        if (Trail.Count == 0 || Math.Abs(point.X - Trail[^1].X) + Math.Abs(point.Y - Trail[^1].Y) > 3)
        {
            Trail.Add(point);
            if (Trail.Count > 7)
                Trail.RemoveAt(0);
        }
    }

    /// <summary>Restore interception and full firepower for a newly started phase.</summary>
    public void ResetForPhase(IReadOnlyList<Vector2[]>? runeStrokes = null)
    {
        HitsTaken = 0;
        PhaseDisabled = false;
        DisabledRemaining = 0f;
        Hp = MaxHp;
        RuneStrokes = runeStrokes ?? Array.Empty<Vector2[]>();
        _burstQueue.Clear();
    }

    public bool TakeDamage(float amount)
    {
        if (!BlocksShots)
            return false;
        HitsTaken += 1;
        Hp = MathF.Round(MaxHp * Math.Max(0, HitsToDisable - HitsTaken) / HitsToDisable);
        if (HitsTaken >= HitsToDisable)
        {
            PhaseDisabled = true;
            return true;
        }
        return false;
    }

    public void UpdateStatus(float dt)
    {
        TelegraphTimer = Math.Max(0f, TelegraphTimer - dt);
        if (DisabledRemaining > 0)
        {
            DisabledRemaining = Math.Max(0f, DisabledRemaining - dt);
            if (DisabledRemaining <= 0)
            {
                Hp = MaxHp;
                FireCooldown = Math.Max(FireCooldown, .5f);
            }
        }
    }

    public void Update(List<EnemyProjectile> projectileSink, float dt)
    {
        if (RemFlag || !Active)
            return;
        Angle += AngularSpeed * dt;
        Place();
        UpdateBursts(projectileSink, dt);
        FireCooldown -= dt;
        if (FireCooldown > 0)
        {
            if (FireCooldown <= .32f)
            {
                TelegraphTimer = Math.Max(TelegraphTimer, FireCooldown);
                TelegraphKind = "shotgun";
                TelegraphTarget = OrbitCenter;
            }
            return;
        }

        // The standard portal shotgun is a three-beat phrase. Each wave changes
        // density, speed, and silhouette, making the volley readable as one attack.
        FirePatternBurst(projectileSink, OrbitCenter, new[]
        {
            new BurstWave(PelletCount, Spread, .92f, .4f),
            new BurstWave(Math.Max(3, PelletCount - 2), Spread * .72f, 1.18f, .42f),
            new BurstWave(PelletCount + 2, Spread * 1.12f, 1.42f, .28f),
        }, waveInterval: .14f, ownerSuffix: "shot");
        FireCooldown += FireInterval;
    }

    /// <summary>Advance queued follow-up waves without coupling them to portal movement. Public in Python too -- Beaudis's survival phase calls it directly instead of the portal's own Update.</summary>
    public void UpdateBursts(List<EnemyProjectile> projectileSink, float dt)
    {
        if (!Active)
            return;
        var remaining = new List<PendingBurst>();
        foreach (var burst in _burstQueue)
        {
            float timer = burst.Timer - dt;
            if (timer <= 0)
            {
                FireWave(projectileSink, burst.Target, burst.PelletCount, burst.Spread, burst.Speed,
                    burst.SizeScale, burst.Damage, burst.Color, burst.OwnerSuffix, burst.DirectionOffset);
            }
            else
            {
                remaining.Add(burst with { Timer = timer });
            }
        }
        _burstQueue.Clear();
        _burstQueue.AddRange(remaining);
    }

    private void FireWave(List<EnemyProjectile> sink, Vector2 target, int pelletCount, float spread, float speed,
        float sizeScale, float damage, Color? color, string ownerSuffix, float directionOffset = 0f)
    {
        if (!Active)
            return;
        float portalX = WorldX + Size / 2f, portalY = WorldY + Size / 2f;
        float direction = MathF.Atan2(target.Y - portalY, target.X - portalX) + directionOffset;
        float distance = Vector2.Distance(target, new Vector2(portalX, portalY));
        int count = PhaseDisabled ? Math.Max(1, (pelletCount + 1) / 2) : pelletCount;
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * spread / Math.Max(1, count - 1);
            float shotSize = Simulation.TileSize * sizeScale;
            sink.Add(new EnemyProjectile(
                portalX - shotSize / 2f, portalY - shotSize / 2f, direction + offset, speed, damage, shotSize,
                travelRange: Math.Max(Simulation.TileSize * 72f, distance), color: color ?? Color,
                shape: "diamond", path: "linear", owner: $"{Owner}_{ownerSuffix}", ignoreWalls: true));
        }
    }

    /// <summary>
    /// Fire the first wave now and queue coordinated variable follow-ups.
    /// Wave entries are (pellets, spread, speed, sizeScale) -- keeping this
    /// vocabulary on the emitter lets every phase reuse it without
    /// duplicating timing machinery.
    /// </summary>
    public void FirePatternBurst(List<EnemyProjectile> sink, Vector2 target, IReadOnlyList<BurstWave> waves,
        float waveInterval = .13f, float damage = .85f, Color? color = null,
        string ownerSuffix = "pattern_burst", float directionOffset = 0f)
    {
        if (!Active || waves.Count == 0)
            return;
        TelegraphTimer = .32f;
        TelegraphKind = "fan";
        TelegraphTarget = target;
        var first = waves[0];
        FireWave(sink, target, first.Pellets, first.Spread, first.Speed, first.SizeScale, damage, color, ownerSuffix, directionOffset);
        for (int index = 1; index < waves.Count; index++)
        {
            var wave = waves[index];
            _burstQueue.Add(new PendingBurst(waveInterval * index, target, wave.Pellets, wave.Spread, wave.Speed,
                wave.SizeScale, damage, color, ownerSuffix, directionOffset));
        }
    }

    /// <summary>Fire a volley at an arbitrary world point without changing the orbit.</summary>
    public void FireToward(List<EnemyProjectile> sink, Vector2 target, int? pelletCount = null, float? spread = null,
        float speed = 1.15f, float damage = .85f, Color? color = null, string ownerSuffix = "shot")
    {
        if (!Active)
            return;
        int count = pelletCount ?? PelletCount;
        if (PhaseDisabled)
            count = Math.Max(1, (count + 1) / 2);
        float actualSpread = spread ?? Spread;
        float portalX = WorldX + Size / 2f, portalY = WorldY + Size / 2f;
        float direction = MathF.Atan2(target.Y - portalY, target.X - portalX);
        TelegraphTimer = .28f;
        TelegraphKind = count <= 2 ? "line" : "fan";
        TelegraphTarget = target;
        float distance = Vector2.Distance(target, new Vector2(portalX, portalY));
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * actualSpread / Math.Max(1, count - 1);
            float shotSize = Simulation.TileSize * .4f;
            sink.Add(new EnemyProjectile(
                portalX - shotSize / 2f, portalY - shotSize / 2f, direction + offset, speed, damage, shotSize,
                travelRange: Math.Max(Simulation.TileSize * 72f, distance), color: color ?? Color,
                shape: "diamond", path: "linear", owner: $"{Owner}_{ownerSuffix}", ignoreWalls: true));
        }
    }

    /// <summary>Launch a readable comet train: fast leader, progressively slower tail.</summary>
    public void FireSpeedBurst(List<EnemyProjectile> sink, Vector2 target, int count = 4, Color? color = null,
        string ownerSuffix = "speed_burst")
    {
        float portalX = WorldX + Size / 2f, portalY = WorldY + Size / 2f;
        float direction = MathF.Atan2(target.Y - portalY, target.X - portalX);
        float[] speeds = { 1.35f, 1.05f, .78f, .52f, .36f };
        int actualCount = PhaseDisabled ? Math.Max(1, (count + 1) / 2) : count;
        for (int index = 0; index < Math.Max(1, Math.Min(actualCount, speeds.Length)); index++)
        {
            float shotSize = Simulation.TileSize * (.34f + index * .035f);
            sink.Add(new EnemyProjectile(
                portalX - shotSize / 2f, portalY - shotSize / 2f, direction, speeds[index], .85f, shotSize,
                travelRange: float.PositiveInfinity, color: color ?? Color, shape: "diamond",
                path: "linear", owner: $"{Owner}_{ownerSuffix}", ignoreWalls: true));
        }
    }

    public Vector2 OrbitCenterScreen(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
        => camera.WorldToScreen(OrbitCenter, playerWorldPosition, screenShake);

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (RemFlag)
            return;
        Vector2 screenPos = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPos.X, (int)screenPos.Y, (int)Size, (int)Size);

        if (Trail.Count > 1)
        {
            var trailScreen = Trail.Select(p => camera.WorldToScreen(p, playerWorldPosition, screenShake)).ToArray();
            Primitives2D.Polyline(spriteBatch, trailScreen, closed: false, UiTheme.Ink, 5);
            Primitives2D.Polyline(spriteBatch, trailScreen, closed: false, Color, 2);
        }

        if (!Active)
        {
            Primitives2D.FillEllipse(spriteBatch, Offset(rect, 3, 4), UiTheme.Shadow);
            Primitives2D.EllipseOutline(spriteBatch, rect, UiTheme.Muted, Math.Max(2, (int)(Size * .1f)));
            float repair = DisabledRemaining / RegenerationTime;
            Primitives2D.Arc(spriteBatch, InflateF(rect, 8, 8), -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * (1 - repair), Color, 3);
            Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Bottom), UiTheme.Ink, 3);
            Primitives2D.Line(spriteBatch, new Vector2(rect.Right, rect.Top), new Vector2(rect.Left, rect.Bottom), UiTheme.Ink, 3);
            return;
        }

        float pulse = MathF.Sin(Angle * 3) * Size * .06f;
        var outer = InflateF(rect, pulse, pulse);
        Primitives2D.FillEllipse(spriteBatch, Offset(outer, 4, 5), UiTheme.Shadow);
        Primitives2D.FillEllipse(spriteBatch, outer, Color);
        Primitives2D.EllipseOutline(spriteBatch, outer, UiTheme.Ink, Math.Max(3, (int)(Size * .11f)));

        for (int ringIndex = 0; ringIndex < 2; ringIndex++)
        {
            var ring = InflateF(outer, -Size * (.16f + ringIndex * .15f), -Size * (.16f + ringIndex * .15f));
            float ringStart = Angle * (ringIndex == 0 ? 2.2f : -1.7f);
            Primitives2D.Arc(spriteBatch, ring, ringStart, ringStart + MathF.PI * 1.25f, UiTheme.Lighten(Color, 35), 2);
        }

        var inner = InflateF(outer, -Size * .36f, -Size * .36f);
        Primitives2D.FillEllipse(spriteBatch, inner, UiTheme.Void);

        if (RuneStrokes.Count > 0)
        {
            float runeScale = inner.Width * .34f;
            var innerCenter = new Vector2(inner.Center.X, inner.Center.Y);
            foreach (var stroke in RuneStrokes)
            {
                if (stroke.Length <= 1)
                    continue;
                var points = stroke.Select(p => innerCenter + p * runeScale).ToArray();
                Primitives2D.Polyline(spriteBatch, points, closed: false, UiTheme.Ink, 4);
                Primitives2D.Polyline(spriteBatch, points, closed: false, UiTheme.Cream, 2);
            }
        }

        if (TelegraphTimer > 0)
        {
            // A compact charge halo communicates imminent fire without painting
            // additional aiming lanes over an already dense bullet field.
            float charge = Math.Min(1.0f, TelegraphTimer / .32f);
            var chargeRing = InflateF(outer, Size * (.18f + charge * .18f), Size * (.18f + charge * .18f));
            Primitives2D.Arc(spriteBatch, chargeRing, -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * (1 - charge), UiTheme.Ink, 6);
            Primitives2D.Arc(spriteBatch, chargeRing, -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * (1 - charge), UiTheme.Cream, 2);
        }

        // Three rune motes counter-rotate around the mouth, making even distant
        // emitters readable and giving every path a little clockwork personality.
        for (int moteIndex = 0; moteIndex < 3; moteIndex++)
        {
            float moteAngle = -Angle * 2.1f + moteIndex * 2f * MathF.PI / 3f;
            float moteRadius = outer.Width * .56f;
            var mote = new Vector2(outer.Center.X + MathF.Cos(moteAngle) * moteRadius, outer.Center.Y + MathF.Sin(moteAngle) * moteRadius);
            Primitives2D.FillCircle(spriteBatch, mote, Math.Max(3, (int)(Size * .075f)) + 2, UiTheme.Ink);
            Primitives2D.FillCircle(spriteBatch, mote, Math.Max(3, (int)(Size * .075f)), UiTheme.Cream);
        }

        Color polarityColor = Polarity > 0 ? UiTheme.Blue : UiTheme.Red;
        var innerCenter2 = new Vector2(inner.Center.X, inner.Center.Y);
        Primitives2D.Line(spriteBatch, innerCenter2 - new Vector2(inner.Width * .22f, 0), innerCenter2 + new Vector2(inner.Width * .22f, 0), polarityColor, 2);
        if (Polarity > 0)
            Primitives2D.Line(spriteBatch, innerCenter2 - new Vector2(0, inner.Height * .22f), innerCenter2 + new Vector2(0, inner.Height * .22f), polarityColor, 2);

        if (ShowTether)
        {
            Vector2 tetherEnd = OrbitCenterScreen(camera, playerWorldPosition, screenShake);
            Primitives2D.Line(spriteBatch, innerCenter2, tetherEnd, UiTheme.Muted, 2);
            for (int index = 0; index < 3; index++)
            {
                float progress = (Angle * .18f + index / 3f) % 1f;
                var packetCenter = new Vector2(
                    innerCenter2.X + (tetherEnd.X - innerCenter2.X) * progress,
                    innerCenter2.Y + (tetherEnd.Y - innerCenter2.Y) * progress);
                var packet = new Rectangle((int)packetCenter.X - 2, (int)packetCenter.Y - 2, 4, 4);
                Primitives2D.FillRect(spriteBatch, InflateF(packet, 2, 2), UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch, packet, Color);
            }
        }

        float hpRatio = Math.Max(0.0f, Hp / MaxHp);
        Primitives2D.Arc(spriteBatch, InflateF(outer, 5, 5), -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * hpRatio, UiTheme.Cream, 2);
        if (PhaseDisabled)
            Primitives2D.EllipseOutline(spriteBatch, InflateF(outer, 9, 9), UiTheme.Muted, 3);
        // Aiming telegraphs intentionally stay hidden; portal-to-portal rune-cannon
        // connections are drawn by boss-specific code and remain that formation's visual link.
    }

    private static Rectangle Offset(Rectangle rect, int dx, int dy) => new(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);

    private static Rectangle InflateF(Rectangle rect, float dx, float dy)
    {
        var result = rect;
        result.Inflate((int)MathF.Round(dx), (int)MathF.Round(dy));
        return result;
    }
}
