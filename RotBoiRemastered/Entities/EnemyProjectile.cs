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
    public const float MaximumLaserLifetime = 3f;
    public const float LaserSproutDuration = .16f;
    public const int LaserTentacleCount = 5;
    public const float LaserVisualWidthScale = 1.65f;
    public const float MinimumLaserVisualWidth = 14f;

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
    /// <summary>Positive speed gained per second -- used by slow-starting "flood" giants so they sweep out of the room over their lifetime instead of lingering.</summary>
    public float Acceleration { get; set; }
    /// <summary>
    /// Forces the (expensive, per-vertex-rotated) 3D twirling diamond render
    /// regardless of <see cref="Shape"/> -- reserved for a boss's large,
    /// sparse shots so ordinary bullet-hell volleys keep using the cheap
    /// flat 2D shapes.
    /// </summary>
    public bool LargeShot3D { get; set; }
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
    /// <summary>
    /// Opts a "laser" path projectile out of the normal
    /// <see cref="MaximumLaserLifetime"/> clamp (both the one-time clamp at
    /// construction and the per-frame reclamp in <see cref="Update"/>) --
    /// for scripted beams authored to persist and be driven (typically by
    /// rotating <see cref="Direction"/> directly) well past an ordinary
    /// laser's few-second burn, such as a boss's late-fight sweeping array.
    /// The projectile still respects an explicitly authored
    /// <see cref="Lifetime"/> if one is given.
    /// </summary>
    public bool LongLastingLaser { get; }
    /// <summary>
    /// Bends a "laser" path projectile's beam into a travelling sine wave
    /// instead of a straight line -- the existing <see cref="Amplitude"/>
    /// and <see cref="Frequency"/> fields (otherwise unused by the laser
    /// path) set the wave's size and how tightly it curls along the beam's
    /// length, and this speed (radians/sec) is how fast that shape appears
    /// to slide along the beam over time. Zero <see cref="Amplitude"/>
    /// (the default) keeps the beam straight, matching every laser that
    /// existed before this. The bend affects both the draw and the hit
    /// test (<see cref="Collides"/> samples the same curve), but the wall
    /// raycast that clips <see cref="RemainingRange"/> still follows the
    /// straight heading -- an approximation that's exact for the open
    /// radial arenas this is authored for.
    /// </summary>
    public float LaserWaveSpeed { get; set; }
    public Vector2? Target { get; set; }
    public float TelegraphDuration { get; set; } = 1.0f;
    /// <summary>
    /// A location-only warning used when a boss projectile originates away
    /// from its body. While active the projectile is stationary, harmless,
    /// and hidden behind a directionless warning marker at its spawn point.
    /// </summary>
    public float OriginTelegraphDuration { get; set; }
    /// <summary>
    /// True when a separate declaration marker has already warned this exact
    /// spawn point (for example, Ishe's delayed afterimage volleys).
    /// </summary>
    public bool OriginWasPretelegraphed { get; set; }
    public float FuseDuration { get; set; } = 3.0f;
    public float BlastRadius { get; set; }
    public int BurstCount { get; set; } = 8;
    public float BurstDamage { get; set; }
    public float BurstRangeTiles { get; set; } = 24f;
    public List<EnemyProjectile> SpawnedProjectiles { get; } = new();
    public int SplitCount { get; set; }
    public float? SplitAt { get; set; }
    public int SplitGeneration { get; set; }
    /// <summary>Velocity inherited by split children; portals deliberately emit slower children.</summary>
    public float SplitSpeedScale { get; set; } = 1.08f;
    /// <summary>Optional authored fan width. Null preserves the ordinary generation-based spread.</summary>
    public float? SplitSpread { get; set; }
    /// <summary>Distributes split children around a full circle instead of a forward fan.</summary>
    public bool SplitRadial { get; set; }
    /// <summary>Optional lifetime applied to split children; null preserves range-only expiry.</summary>
    public float? SplitChildLifetime { get; set; }
    /// <summary>
    /// Slots reserved against an encounter-owned projectile budget. Ordinary
    /// shots cost one; a delayed splitter can reserve its complete child wave.
    /// </summary>
    public int ThreatReservationCost { get; set; } = 1;
    /// <summary>
    /// Travel progress at which a splitting projectile begins visually
    /// declaring its activation. One disables the additional warning.
    /// </summary>
    public float SplitTelegraphStartRatio { get; set; } = 1f;
    /// <summary>Settable: Malady's purple pool (bossTypes.py's _spawn_pool) overrides the path=="laser" default so its hazard lingers instead of being consumed on the player's first hit.</summary>
    public bool PersistentHazard { get; set; }

    /// <summary>
    /// New primitive: when set (&gt; 0) on a "mine" path, the mine stays
    /// dim and harmless until the player enters this radius -- only then
    /// does its ordinary <see cref="TelegraphDuration"/> arming window
    /// begin. Zero (the default) preserves every existing mine's
    /// arm-on-a-fixed-timer behavior.
    /// </summary>
    public float ProximityRadius { get; set; }
    private float? _proximityTriggeredAtAge;

    /// <summary>
    /// New primitive: per-instance override of the sprout ease every laser
    /// used to share (the fixed <see cref="LaserSproutDuration"/> constant).
    /// A boss whose laser should visibly push into being over a longer
    /// beat -- rather than snapping to full length in well under a fifth
    /// of a second -- can lengthen this without affecting any other laser.
    /// </summary>
    public float SproutSeconds { get; set; } = LaserSproutDuration;

    /// <summary>
    /// New primitive: turns a moving (non-laser/bomb/orbit) shot's heading
    /// toward the player's live position at this many radians/sec, capped
    /// so it stays dodgeable, instead of holding the direction it spawned
    /// with. Zero (the default) is the ordinary straight/sine behavior.
    /// </summary>
    public float HomingTurnRate { get; set; }

    /// <summary>
    /// New primitive: a "bounce" path shot reflects off a circular
    /// boundary (typically the boss's own arena) this many times instead
    /// of flying through it or dying on a dungeon wall. Zero disables it.
    /// </summary>
    public Vector2 BounceCenter { get; set; }
    public float BounceRadius { get; set; }
    public int BouncesRemaining { get; set; }

    /// <summary>
    /// New primitive: a "pool" path's radius breathes in and out over its
    /// lifetime by this fraction of its base size (0 keeps the ordinary
    /// static radius) at <see cref="PoolPulseFrequency"/> hz.
    /// </summary>
    public float PoolPulseAmplitude { get; set; }
    public float PoolPulseFrequency { get; set; } = 1f;

    /// <summary>
    /// New primitive: a "tether" path projectile draws and hits as a live
    /// line between two other projectiles' current centers instead of
    /// moving under its own power -- e.g. two "orbit" shots connected by a
    /// damaging line, like a clock's hands. Assign both ends right after
    /// construction, before the tether is added to the sink; it expires
    /// automatically once either end does.
    /// </summary>
    public EnemyProjectile? TetherStart { get; set; }
    public EnemyProjectile? TetherEnd { get; set; }

    public bool Exploded { get; private set; }
    public float Age { get; private set; }
    public float Travelled { get; private set; }
    public bool RemFlag { get; set; }
    public List<Vector2> Trail { get; } = new(5);
    private bool _difficultyTimingApplied;
    private readonly float _authoredRange;
    /// <summary>
    /// Per-shot tumble speed (radians/sec) for the 3D diamond shape, rolled
    /// once at spawn so a volley twirls at varied rates rather than in
    /// lockstep -- some diamonds lazily drift, others snap around.
    /// </summary>
    private readonly float _spinRate;
    private readonly float _spinPhase;

    public EnemyProjectile(
        float worldX, float worldY, float direction, float speed, float damage, float size,
        float travelRange = 900f, Color? color = null, string shape = "square", string path = "linear",
        float amplitude = 0f, float frequency = .035f, float? lifetime = null, float speedDecay = 0f,
        Vector2? orbitCenter = null, float orbitRadius = 0f, float orbitAngle = 0f, float angularSpeed = 0f,
        string? owner = null, bool ignoreWalls = false, Vector2? target = null, float acceleration = 0f,
        bool longLastingLaser = false)
    {
        LongLastingLaser = longLastingLaser;
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
        Lifetime = path == "laser" && !longLastingLaser
            ? Math.Min(lifetime ?? MaximumLaserLifetime, MaximumLaserLifetime)
            : lifetime;
        SpeedDecay = speedDecay;
        Acceleration = acceleration;
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

        // Lasers are never allowed to opt out of dungeon collision. Their
        // effective range is clipped to the first wall during Update.
        IgnoreWalls = path == "laser" ? false : ignoreWalls;
        Target = target;
        BlastRadius = Simulation.TileSize * 1.5f;
        BurstDamage = Damage;
        PersistentHazard = path == "laser";
        _authoredRange = RemainingRange;
        _spinRate = Random.Shared.NextSingle() * 4f + 2f;
        _spinPhase = Random.Shared.NextSingle() * MathF.Tau;
    }

    public Vector2 OriginPoint => Path is "laser" or "origin_warning"
        ? new Vector2(OriginX, OriginY)
        : new Vector2(OriginX + Size / 2f, OriginY + Size / 2f);

    /// <summary>Current world-space center, used by "tether" partners to find each other each frame.</summary>
    public Vector2 Center() => new(WorldX + Size / 2f, WorldY + Size / 2f);

    /// <summary>
    /// The warning still consumes the full authored telegraph. Once it ends,
    /// the dangerous and visible beam grows together from its source over a
    /// very short ease rather than popping across the arena in one frame.
    /// </summary>
    internal float LaserSproutProgress
    {
        get
        {
            if (Path != "laser")
                return 1f;
            float linear = Math.Clamp(
                (Age - TelegraphDuration) / Math.Max(.001f, SproutSeconds), 0f, 1f);
            return linear * linear * (3f - 2f * linear);
        }
    }

    internal bool UsesRainbowLaserTentacles =>
        Path == "laser"
        && Owner?.StartsWith("aphantasia_", StringComparison.Ordinal) == true;

    internal Color LaserTentacleColor(int strand, float along = 0f)
    {
        if (!UsesRainbowLaserTentacles)
            return Color;
        float phase = Age * .34f + strand / (float)LaserTentacleCount
            + along * .42f;
        float pulse = .78f + .22f * MathF.Sin(
            Age * 8.4f + strand * 1.17f - along * 5.2f);
        return new Color(
            Math.Clamp((.5f + .5f * MathF.Sin(phase * MathF.Tau)) * pulse, 0f, 1f),
            Math.Clamp((.5f + .5f * MathF.Sin(phase * MathF.Tau + MathF.Tau / 3f)) * pulse, 0f, 1f),
            Math.Clamp((.5f + .5f * MathF.Sin(phase * MathF.Tau + MathF.Tau * 2f / 3f)) * pulse, 0f, 1f));
    }

    public void RequireOriginTelegraph(float duration) =>
        OriginTelegraphDuration = Math.Max(OriginTelegraphDuration, duration);

    public void RequireOriginTelegraphIfRemote(
        Vector2 ownerCenter,
        float ownerBodyRadius,
        float duration)
    {
        if (Vector2.DistanceSquared(OriginPoint, ownerCenter)
            > ownerBodyRadius * ownerBodyRadius)
        {
            RequireOriginTelegraph(duration);
        }
    }

    public Rectangle WorldRect()
    {
        if (Path == "laser" && Age >= TelegraphDuration)
        {
            float activeRange = RemainingRange * LaserSproutProgress;
            float endX = WorldX + MathF.Cos(Direction) * activeRange;
            float endY = WorldY + MathF.Sin(Direction) * activeRange;
            float x = Math.Min(WorldX, endX), y = Math.Min(WorldY, endY);
            float w = Math.Max(Size, Math.Abs(endX - WorldX)), h = Math.Max(Size, Math.Abs(endY - WorldY));
            return new Rectangle((int)x, (int)y, (int)w, (int)h);
        }
        if (Path == "tether" && TetherStart is not null && TetherEnd is not null)
        {
            Vector2 startPoint = TetherStart.Center(), endPoint = TetherEnd.Center();
            float x = Math.Min(startPoint.X, endPoint.X), y = Math.Min(startPoint.Y, endPoint.Y);
            float w = Math.Max(Size, Math.Abs(endPoint.X - startPoint.X)), h = Math.Max(Size, Math.Abs(endPoint.Y - startPoint.Y));
            return new Rectangle((int)x, (int)y, (int)w, (int)h);
        }
        return new Rectangle((int)WorldX, (int)WorldY, (int)Size, (int)Size);
    }

    /// <summary>
    /// The area to test for on-screen culling, as opposed to <see cref="WorldRect"/>
    /// which also doubles as the collision/wall-hit footprint. A laser's telegraph
    /// already paints its tentacle cluster and range markers across the full
    /// <see cref="RemainingRange"/> before it fires (see <see cref="DrawLaser"/>),
    /// but <see cref="WorldRect"/> stays a tiny box at the spawn point until the
    /// beam actually starts sprouting so telegraph-phase wall-hit checks don't
    /// fire prematurely. Left as-is, that tiny box is what culling used to test,
    /// so a laser spawned far from the player -- with its warning sweeping in
    /// toward them -- got the entire telegraph culled off screen, leaving
    /// players nothing to react to once it actually fired. This mirrors
    /// <see cref="WorldRect"/> for every other case.
    /// </summary>
    public Rectangle VisualCullRect()
    {
        if (Path == "laser" && Age < TelegraphDuration)
        {
            float endX = WorldX + MathF.Cos(Direction) * RemainingRange;
            float endY = WorldY + MathF.Sin(Direction) * RemainingRange;
            float x = Math.Min(WorldX, endX), y = Math.Min(WorldY, endY);
            float w = Math.Max(Size, Math.Abs(endX - WorldX)), h = Math.Max(Size, Math.Abs(endY - WorldY));
            return new Rectangle((int)x, (int)y, (int)w, (int)h);
        }
        return WorldRect();
    }

    /// <summary>
    /// A dormant proximity mine's own arming timer, measured from the
    /// moment the player first entered <see cref="ProximityRadius"/>
    /// rather than from spawn. Negative infinity (permanently "before its
    /// telegraph") until that happens, matching a mine's existing
    /// spawn-age gating for every mine that doesn't opt into this.
    /// </summary>
    private float EffectiveMineAge => ProximityRadius > 0f
        ? (_proximityTriggeredAtAge is float triggered ? Age - triggered : float.NegativeInfinity)
        : Age;

    /// <summary>Breathing pools grow/shrink by <see cref="PoolPulseAmplitude"/>; ordinary pools (amplitude 0) are unaffected.</summary>
    private float PoolPulseScale => PoolPulseAmplitude <= 0f
        ? 1f
        : Math.Max(.15f, 1f + PoolPulseAmplitude * MathF.Sin(Age * PoolPulseFrequency * MathF.Tau));

    public bool Collides(Rectangle rect)
    {
        if (Illusory)
            return false;
        if (Age < OriginTelegraphDuration)
            return false;
        if (Path == "bank" && Age < TelegraphDuration)
            return false;
        if (Path == "mine" && EffectiveMineAge < TelegraphDuration)
            return false;
        if (Path == "tether")
        {
            if (TetherStart is null || TetherEnd is null)
                return false;
            var inflated = rect;
            inflated.Inflate((int)Size, (int)Size);
            return SegmentIntersectsRect(TetherStart.Center(), TetherEnd.Center(), inflated);
        }
        if (Path == "pool")
        {
            if (Age < TelegraphDuration)
                return false;
            float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
            float nearestX = Math.Clamp(centerX, rect.Left, rect.Right);
            float nearestY = Math.Clamp(centerY, rect.Top, rect.Bottom);
            float radius = Size * .46f * PoolPulseScale;
            return (nearestX - centerX) * (nearestX - centerX) + (nearestY - centerY) * (nearestY - centerY) <= radius * radius;
        }
        if (Path == "laser")
        {
            float sprout = LaserSproutProgress;
            if (Age < TelegraphDuration || sprout <= .001f)
                return false;
            float activeRange = RemainingRange * sprout;
            var inflated = rect;
            inflated.Inflate((int)Size, (int)Size);
            if (Amplitude == 0)
                return SegmentIntersectsRect(
                    LaserPointAt(0f), LaserPointAt(activeRange), inflated);

            // A wavy beam (Amplitude != 0) is tested as a sampled polyline
            // along the same curve DrawLaser renders, so the hitbox always
            // matches what's on screen.
            const int waveSamples = 24;
            Vector2 previous = LaserPointAt(0f);
            for (int sample = 1; sample <= waveSamples; sample++)
            {
                Vector2 current = LaserPointAt(activeRange * sample / waveSamples);
                if (SegmentIntersectsRect(previous, current, inflated))
                    return true;
                previous = current;
            }
            return false;
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

    public void Update(Battleground battleground, bool casualMode, bool hardMode = false,
        Vector2? playerWorldPosition = null)
    {
        if (!_difficultyTimingApplied)
        {
            float warningScale = casualMode ? 1.25f : hardMode ? .86f : 1f;
            TelegraphDuration *= warningScale;
            OriginTelegraphDuration *= warningScale;
            if (Path == "bomb")
                FuseDuration *= warningScale;
            _difficultyTimingApplied = true;
        }
        float seconds = (float)Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        Age += seconds;

        // Proximity mines: arm the instant the player enters range rather
        // than on a fixed timer from spawn -- see EffectiveMineAge.
        if (Path == "mine" && ProximityRadius > 0f && _proximityTriggeredAtAge is null
            && playerWorldPosition.HasValue)
        {
            float mineCenterX = WorldX + Size / 2f, mineCenterY = WorldY + Size / 2f;
            float dx = playerWorldPosition.Value.X - mineCenterX, dy = playerWorldPosition.Value.Y - mineCenterY;
            if (dx * dx + dy * dy <= ProximityRadius * ProximityRadius)
                _proximityTriggeredAtAge = Age;
        }

        if (Age < OriginTelegraphDuration)
            return;

        switch (Path)
        {
            case "origin_warning":
                if (Age >= (Lifetime ?? TelegraphDuration))
                    RemFlag = true;
                return;

            case "pool":
                if (Age >= (Lifetime ?? 8.0f))
                    RemFlag = true;
                return;

            case "tether":
                if (TetherStart is null || TetherEnd is null || TetherStart.RemFlag || TetherEnd.RemFlag)
                    RemFlag = true;
                if (Lifetime is not null && Age >= Lifetime)
                    RemFlag = true;
                return;

            case "laser":
                if (Age >= TelegraphDuration && AngularSpeed != 0)
                    Direction += AngularSpeed * seconds;
                if (!LongLastingLaser)
                {
                    Lifetime = Math.Min(
                        Lifetime ?? MaximumLaserLifetime,
                        MaximumLaserLifetime);
                }
                RemainingRange = Math.Max(0f,
                    battleground.RaycastDistanceToWall(
                        new Vector2(WorldX, WorldY),
                        new Vector2(MathF.Cos(Direction), MathF.Sin(Direction)),
                        _authoredRange)
                    - Size * .5f);
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
                // Homing: steer toward the player's live position at a
                // capped turn rate before moving, rather than holding the
                // heading the shot spawned with (ordinary sine/linear).
                if (HomingTurnRate != 0f && playerWorldPosition.HasValue)
                {
                    float toPlayer = MathF.Atan2(
                        playerWorldPosition.Value.Y - (WorldY + Size / 2f),
                        playerWorldPosition.Value.X - (WorldX + Size / 2f));
                    float turnDelta = MathF.Atan2(
                        MathF.Sin(toPlayer - Direction), MathF.Cos(toPlayer - Direction));
                    float maxTurn = HomingTurnRate * seconds;
                    Direction += Math.Clamp(turnDelta, -maxTurn, maxTurn);
                }
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
                // Bounce: reflect off a circular boundary (typically the
                // boss's own arena) instead of flying through it or dying
                // on a dungeon wall.
                if (Path == "bounce" && BounceRadius > 0f && BouncesRemaining > 0)
                {
                    float bounceCenterX = WorldX + Size / 2f, bounceCenterY = WorldY + Size / 2f;
                    float offsetX = bounceCenterX - BounceCenter.X, offsetY = bounceCenterY - BounceCenter.Y;
                    float distanceFromCenter = MathF.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    if (distanceFromCenter > BounceRadius)
                    {
                        float normalX = offsetX / Math.Max(.0001f, distanceFromCenter);
                        float normalY = offsetY / Math.Max(.0001f, distanceFromCenter);
                        float headingX = MathF.Cos(Direction), headingY = MathF.Sin(Direction);
                        float dot = headingX * normalX + headingY * normalY;
                        Direction = MathF.Atan2(headingY - 2f * dot * normalY, headingX - 2f * dot * normalX);
                        // Clamp back onto the boundary so it doesn't visibly
                        // poke through before the reflected heading takes over.
                        WorldX = BounceCenter.X + normalX * BounceRadius - Size / 2f;
                        WorldY = BounceCenter.Y + normalY * BounceRadius - Size / 2f;
                        BouncesRemaining--;
                    }
                }
                if (SpeedDecay != 0)
                    Speed = Math.Max(0, Speed - SpeedDecay * seconds);
                if (Acceleration != 0)
                    Speed += Acceleration * seconds;
                if (SplitCount > 1 && SplitAt.HasValue && Travelled >= SplitAt.Value && !Exploded)
                {
                    Exploded = true;
                    float spread = SplitSpread ?? (.8f + .12f * SplitGeneration);
                    for (int index = 0; index < SplitCount; index++)
                    {
                        float fraction = SplitCount == 1 ? .5f : (float)index / (SplitCount - 1);
                        float childDirection = SplitRadial
                            ? Direction + index * MathF.Tau / SplitCount
                            : Direction - spread / 2f + spread * fraction;
                        var child = new EnemyProjectile(
                            WorldX, WorldY, childDirection,
                            Speed * SplitSpeedScale, Damage * .58f, Size * .72f,
                            travelRange: Math.Max(Simulation.TileSize * 5f, RemainingRange),
                            color: Color, shape: "diamond", lifetime: SplitChildLifetime,
                            owner: Owner, ignoreWalls: IgnoreWalls, acceleration: Acceleration);
                        if (SplitGeneration > 0)
                        {
                            child.SplitCount = SplitCount;
                            child.SplitAt = Math.Max(Simulation.TileSize * 2.5f, RemainingRange * .42f);
                            child.SplitGeneration = SplitGeneration - 1;
                            child.SplitSpeedScale = SplitSpeedScale;
                            child.SplitSpread = SplitSpread;
                            child.SplitRadial = SplitRadial;
                            child.SplitChildLifetime = SplitChildLifetime;
                            child.SplitTelegraphStartRatio = SplitTelegraphStartRatio;
                        }
                        child.LargeShot3D = LargeShot3D;
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
        if (Age < OriginTelegraphDuration)
        {
            DrawOriginTelegraph(
                spriteBatch,
                camera,
                playerWorldPosition,
                screenShake,
                highContrast,
                OriginTelegraphDuration);
            return;
        }
        if (Path == "origin_warning")
        {
            DrawOriginTelegraph(
                spriteBatch,
                camera,
                playerWorldPosition,
                screenShake,
                highContrast,
                Lifetime ?? TelegraphDuration);
            return;
        }
        if (Path == "pool")
        {
            DrawPool(spriteBatch, camera, playerWorldPosition, screenShake);
            return;
        }
        if (Path == "laser")
        {
            DrawLaser(spriteBatch, camera, playerWorldPosition, screenShake,
                highContrast);
            return;
        }
        if (Path == "bank")
        {
            DrawBank(spriteBatch, camera, playerWorldPosition, screenShake, highContrast);
            return;
        }
        if (Path == "tether")
        {
            DrawTether(spriteBatch, camera, playerWorldPosition, screenShake);
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
        if (vfxIntensity > 0)
        {
            // A faint ground-contact shadow, offset the same down-right
            // direction as every other shadow in the game (Player,
            // ProjectileVisuals' weapon bullets, laser telegraphs). Gated on
            // the same setting as the trail below so dense bullet-hell
            // encounters can be turned back down for performance.
            Primitives2D.FillCircle(spriteBatch,
                centerScreen + new Vector2(2f, 3f) * Math.Max(1f, visibleSize / 24f),
                visibleSize * .42f, UiTheme.Shadow * .35f);
        }
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
        if (LargeShot3D)
            DrawLargeTwirlDiamond(spriteBatch, rect, visibleSize);
        else if (Shape is "diamond" or "mine" or "bomb")
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

        DrawSplitTelegraph(spriteBatch, centerScreen, visibleSize, highContrast);

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

    private void DrawSplitTelegraph(SpriteBatch spriteBatch, Vector2 center,
        float visibleSize, bool highContrast)
    {
        if (Exploded || SplitCount <= 1 || !SplitAt.HasValue || SplitAt.Value <= 0)
            return;
        float start = Math.Clamp(SplitTelegraphStartRatio, 0f, 1f);
        if (start >= 1f)
            return;
        float travelRatio = Math.Clamp(Travelled / SplitAt.Value, 0f, 1f);
        if (travelRatio < start)
            return;
        float progress = (travelRatio - start) / Math.Max(.001f, 1f - start);
        Color warning = highContrast ? UiTheme.Cream : Color.Lerp(Color, UiTheme.Cream, progress);
        float ring = visibleSize * (.9f - progress * .18f);
        Primitives2D.CircleOutline(spriteBatch, center, ring, UiTheme.Ink, 6);
        Primitives2D.CircleOutline(spriteBatch, center, ring, warning, 3);
        for (int index = 0; index < Math.Min(8, SplitCount); index++)
        {
            float angle = Direction + index * MathF.Tau / Math.Min(8, SplitCount);
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Primitives2D.Line(spriteBatch,
                center + direction * visibleSize * .55f,
                center + direction * visibleSize * (.68f + progress * .18f),
                warning, 2);
        }
    }

    private void DrawDangerTrim(
        SpriteBatch spriteBatch,
        Rectangle rect,
        float visibleSize,
        Vector2 forward,
        string visualShape,
        Color dangerTrim)
    {
        // The twirling 3D diamond draws its own tumbling outline -- a flat,
        // non-rotating trim shape underneath it just read as a static
        // solid-colored shape peeking out from behind the twirl.
        if (LargeShot3D)
            return;
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

    /// <summary>Local-space vertices of a diamond/bipyramid: apex, apex, then a 4-point girdle ring.</summary>
    private static readonly Vector3[] DiamondVertices =
    [
        new(0, -1, 0), new(0, 1, 0),
        new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 0, -1),
    ];

    /// <summary>Eight triangular faces: four connecting the top apex to each girdle edge, four for the bottom apex.</summary>
    private static readonly int[][] DiamondFaces =
    [
        [0, 2, 3], [0, 3, 4], [0, 4, 5], [0, 5, 2],
        [1, 3, 2], [1, 4, 3], [1, 5, 4], [1, 2, 5],
    ];

    /// <summary>Fixed key light -- same upper-left-and-toward-camera direction used elsewhere in the game.</summary>
    private static readonly Vector3 DiamondLightDirection = Vector3.Normalize(new Vector3(-.35f, -.55f, .75f));

    private static Vector3 RotateYawPitch(Vector3 value, float yaw, float pitch)
    {
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        float rx = value.X * cy + value.Z * sy;
        float rz = -value.X * sy + value.Z * cy;
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        float ry = value.Y * cp - rz * sp;
        rz = value.Y * sp + rz * cp;
        return new Vector3(rx, ry, rz);
    }

    private static Vector2[] ProjectDiamond(Vector2 center, float extent, float yaw, float pitch)
    {
        var result = new Vector2[DiamondVertices.Length];
        for (int index = 0; index < DiamondVertices.Length; index++)
        {
            Vector3 rotated = RotateYawPitch(DiamondVertices[index], yaw, pitch);
            float perspective = 1f + rotated.Z * .12f;
            result[index] = center + new Vector2(rotated.X, rotated.Y) * extent * perspective;
        }
        return result;
    }

    /// <summary>Brightness for one diamond face against the fixed key light, kept in a [.5, 1] band so unlit faces stay readable.</summary>
    private static float DiamondFaceLight(int[] face, float yaw, float pitch)
    {
        Vector3 a = RotateYawPitch(DiamondVertices[face[0]], yaw, pitch);
        Vector3 b = RotateYawPitch(DiamondVertices[face[1]], yaw, pitch);
        Vector3 c = RotateYawPitch(DiamondVertices[face[2]], yaw, pitch);
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() > 0f)
            normal = Vector3.Normalize(normal);
        float lit = Vector3.Dot(normal, DiamondLightDirection);
        return .5f + .5f * Math.Clamp(lit, 0f, 1f);
    }

    private static void DrawFilledDiamond(SpriteBatch spriteBatch, Vector2[] points,
        Color fill, Color edge, float yaw, float pitch, int edgeWidth)
    {
        var face = new Vector2[3];
        foreach (int[] indices in DiamondFaces)
        {
            float light = DiamondFaceLight(indices, yaw, pitch);
            face[0] = points[indices[0]];
            face[1] = points[indices[1]];
            face[2] = points[indices[2]];
            Primitives2D.FillPolygon(spriteBatch, face, fill * (light * .85f));
        }
        // Girdle ring + the four edges into each apex -- enough silhouette to
        // read as a faceted gem without redrawing every triangle edge twice.
        // Bright white rather than dark ink -- these read poorly against the
        // game's dark arenas otherwise.
        for (int index = 2; index < points.Length; index++)
        {
            int next = index + 1 <= points.Length - 1 ? index + 1 : 2;
            Primitives2D.Line(spriteBatch, points[index], points[next], edge, edgeWidth);
            Primitives2D.Line(spriteBatch, points[0], points[index], edge, edgeWidth);
            Primitives2D.Line(spriteBatch, points[1], points[index], edge, edgeWidth);
        }
    }

    /// <summary>
    /// The original flat 2D rhombus -- restored as the default diamond
    /// render for ordinary volleys after the 3D twirl (below) turned out too
    /// expensive to run on every shot. Reserved now for the common case;
    /// <see cref="LargeShot3D"/> shots use <see cref="DrawLargeTwirlDiamond"/>
    /// instead.
    /// </summary>
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
        // A dormant proximity mine (armed but not yet triggered) is drawn
        // dim rather than at full brightness, so it reads as buried/inert.
        Color bodyColor = Shape == "mine" && ProximityRadius > 0f && EffectiveMineAge < 0f
            ? Color * .32f
            : Color;
        Primitives2D.FillQuad(spriteBatch, top, right, bottom, left, bodyColor);
        Primitives2D.QuadOutline(
            spriteBatch, top, right, bottom, left,
            UiTheme.Ink, Math.Max(2, (int)(visibleSize * .1f)));

        DrawDiamondOverlays(spriteBatch, rect, new Vector2(rect.Center.X, rect.Center.Y), visibleSize);
    }

    /// <summary>
    /// A tumbling 3D gem (a rotated bipyramid, lit by a fixed key light and
    /// projected with a cheap perspective fudge -- the same hand-rolled
    /// technique Aphantasia's own boss-body cube uses). Expensive enough
    /// (per-vertex rotation, per-face lighting, several draw calls) that
    /// it's reserved for <see cref="LargeShot3D"/> shots rather than every
    /// diamond in a volley.
    /// </summary>
    private void DrawLargeTwirlDiamond(
        SpriteBatch spriteBatch,
        Rectangle rect,
        float visibleSize)
    {
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        float yaw = (Age + _spinPhase) * _spinRate;
        float pitch = (Age + _spinPhase) * _spinRate * .6f;
        float extent = visibleSize * .5f;

        // Lighter, semi-translucent drop shadow -- the same twirling
        // silhouette offset down-right, at reduced alpha rather than a hard
        // full-opacity copy.
        Vector2[] shadowPoints = ProjectDiamond(center + new Vector2(3, 4), extent, yaw, pitch);
        foreach (int[] indices in DiamondFaces)
        {
            var shadowFace = new[] { shadowPoints[indices[0]], shadowPoints[indices[1]], shadowPoints[indices[2]] };
            Primitives2D.FillPolygon(spriteBatch, shadowFace, UiTheme.Shadow * .35f);
        }

        Vector2[] points = ProjectDiamond(center, extent, yaw, pitch);
        DrawFilledDiamond(spriteBatch, points, Color, Color.White,
            yaw, pitch, Math.Max(2, (int)(visibleSize * .07f)));

        DrawDiamondOverlays(spriteBatch, rect, center, visibleSize);
    }

    /// <summary>Mine-pulse/telegraph and bomb-fuse/blast overlays shared by both diamond renders.</summary>
    private void DrawDiamondOverlays(SpriteBatch spriteBatch, Rectangle rect, Vector2 center, float visibleSize)
    {
        if (Shape == "mine")
        {
            // A dormant proximity mine only pulses/rings once triggered --
            // see EffectiveMineAge -- so it reads as buried rather than as
            // an ordinary mine counting down from spawn.
            float mineAge = EffectiveMineAge;
            if (mineAge >= 0f)
            {
                int pulse = Math.Max(
                    3,
                    (int)(visibleSize
                        * (.12f + .05f * (1 + MathF.Sin(mineAge * 5f)))));
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)(center.X - pulse / 2f), (int)(center.Y - pulse / 2f), pulse, pulse), UiTheme.Text);
                if (mineAge < TelegraphDuration)
                {
                    float warningProgress = mineAge / Math.Max(.01f, TelegraphDuration);
                    float warningRadius = visibleSize
                        * (.72f + (1f - warningProgress) * .42f);
                    Primitives2D.CircleOutline(spriteBatch, center, warningRadius, UiTheme.Cream,
                        Math.Max(2, (int)(visibleSize * .07f)));
                }
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
        // Breathing pools (PoolPulseAmplitude != 0) grow/shrink on top of
        // the ordinary appear/fade envelope, matching the pulsing radius
        // Collides tests via PoolPulseScale.
        float scale = Math.Max(.08f, Math.Min(appearing, fading)) * PoolPulseScale;
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

    private void DrawOriginTelegraph(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        bool highContrast,
        float duration)
    {
        Vector2 center = camera.WorldToScreen(
            OriginPoint,
            playerWorldPosition,
            screenShake);
        float progress = Math.Clamp(
            Age / Math.Max(.01f, duration),
            0f,
            1f);
        float worldRadius = Math.Max(
            Simulation.TileSize * .34f,
            Size * .68f);
        float radius = Math.Max(
            8f,
            camera.WorldVectorToScreen(Vector2.UnitX * worldRadius).Length());
        radius *= 1.28f - progress * .28f;
        Color warning = highContrast ? UiTheme.Cream : Color;
        int stroke = Math.Max(2, (int)(radius * .13f));

        Primitives2D.FillCircle(
            spriteBatch,
            center + new Vector2(3, 4),
            radius * .38f,
            UiTheme.Shadow);
        Primitives2D.CircleOutline(
            spriteBatch,
            center,
            radius,
            UiTheme.Ink,
            stroke + 3,
            24);
        Primitives2D.CircleOutline(
            spriteBatch,
            center,
            radius,
            warning,
            stroke,
            24);
        Primitives2D.Arc(
            spriteBatch,
            new Rectangle(
                (int)(center.X - radius * .72f),
                (int)(center.Y - radius * .72f),
                Math.Max(2, (int)(radius * 1.44f)),
                Math.Max(2, (int)(radius * 1.44f))),
            -MathF.PI / 2f,
            -MathF.PI / 2f + MathF.Tau * progress,
            UiTheme.Cream,
            Math.Max(2, stroke - 1),
            24);
        Primitives2D.FillCircle(
            spriteBatch,
            center,
            Math.Max(3f, radius * (.15f + .04f * MathF.Sin(Age * 18f))),
            warning);

        // Four symmetric ticks emphasize the source location without
        // revealing or implying the projectile's future trajectory.
        for (int index = 0; index < 4; index++)
        {
            float angle = index * MathF.PI / 2f;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Primitives2D.Line(
                spriteBatch,
                center + direction * radius * .72f,
                center + direction * radius * 1.12f,
                warning,
                stroke);
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

    /// <summary>
    /// A "tether" has no position of its own -- it draws (and, via
    /// <see cref="Collides"/>, hits) as a live line between its two ends'
    /// current centers, so it visibly follows them wherever they orbit.
    /// </summary>
    private void DrawTether(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (TetherStart is null || TetherEnd is null)
            return;
        Vector2 start = camera.WorldToScreen(TetherStart.Center(), playerWorldPosition, screenShake);
        Vector2 end = camera.WorldToScreen(TetherEnd.Center(), playerWorldPosition, screenShake);
        int width = Math.Max(3, (int)(Size * .3f));
        float pulse = .7f + .3f * MathF.Sin(Age * 6f);
        Primitives2D.Line(spriteBatch, start + new Vector2(3, 4), end + new Vector2(3, 4), UiTheme.Shadow, width + 3);
        Primitives2D.Line(spriteBatch, start, end, Color * pulse, width);
        Primitives2D.Line(spriteBatch, start, end, UiTheme.Cream * .5f, Math.Max(1, width / 3));
    }

    private void DrawLaser(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake, bool highContrast)
    {
        Vector2 origin = new(WorldX, WorldY);
        Vector2 heading = new(MathF.Cos(Direction), MathF.Sin(Direction));
        Vector2 normal = new(-heading.Y, heading.X);
        Vector2 start = camera.WorldToScreen(origin, playerWorldPosition, screenShake);
        float visualWidth = Math.Max(MinimumLaserVisualWidth,
            camera.WorldVectorToScreen(normal * Size).Length()
                * LaserVisualWidthScale);

        if (Age < TelegraphDuration)
        {
            float progress = Age / Math.Max(.01f, TelegraphDuration);
            DrawLaserTentacleCluster(spriteBatch, camera, playerWorldPosition,
                screenShake, normal, RemainingRange,
                visualWidth * .95f, telegraph: true, highContrast);
            for (int step = 0; step < 5; step++)
            {
                Vector2 markerWorld = LaserPointAt(RemainingRange * (step / 4f));
                Vector2 marker = camera.WorldToScreen(markerWorld,
                    playerWorldPosition, screenShake);
                float markerRadius = 5f + (1f - progress) * 2f;
                Primitives2D.FillCircle(spriteBatch, marker + new Vector2(2, 3),
                    markerRadius + 2f, UiTheme.Shadow * .72f);
                Primitives2D.FillCircle(spriteBatch, marker,
                    markerRadius, UiTheme.Cream);
            }
            Primitives2D.FillCircle(spriteBatch, start,
                Math.Max(5f, visualWidth * .26f),
                highContrast ? UiTheme.Cream : Color.Lerp(Color, UiTheme.Cream, .28f));
        }
        else
        {
            float sprout = LaserSproutProgress;
            float activeRange = RemainingRange * sprout;
            DrawLaserTentacleCluster(spriteBatch, camera, playerWorldPosition,
                screenShake, normal, activeRange,
                visualWidth, telegraph: false, highContrast);
            Vector2 end = camera.WorldToScreen(LaserPointAt(activeRange),
                playerWorldPosition, screenShake);
            Color sourceColor = UsesRainbowLaserTentacles
                ? LaserTentacleColor(0) : Color;
            sourceColor = Color.Lerp(sourceColor, UiTheme.Cream, .12f);
            Primitives2D.FillCircle(spriteBatch, start,
                Math.Max(4f, visualWidth * .46f), sourceColor);
            Primitives2D.CircleOutline(spriteBatch, start,
                Math.Max(5f, visualWidth * .5f), UiTheme.Ink, 2, 18);
            Primitives2D.FillCircle(spriteBatch, end,
                Math.Max(3f, visualWidth * .2f),
                UsesRainbowLaserTentacles ? LaserTentacleColor(4, 1f) : UiTheme.Cream);
            if (TruthMarked)
                Primitives2D.FillCircle(spriteBatch, start,
                    Math.Max(3f, visualWidth * .26f), UiTheme.Cream);
        }
    }

    private void DrawLaserTentacleCluster(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake,
        Vector2 normal, float range, float width,
        bool telegraph, bool highContrast)
    {
        const int segments = 34;
        float vfx = .62f + .38f * Math.Clamp(
            (float)GameProfile.Profile.VisualEffectsIntensity, 0f, 1f);
        int seed = StableLaserSeed();
        for (int strand = 0; strand < LaserTentacleCount; strand++)
        {
            float lane = strand - (LaserTentacleCount - 1) * .5f;
            float strandPhase = seed * .017f + strand * 1.37f;
            Vector2? previous = null;
            for (int segment = 0; segment <= segments; segment++)
            {
                float amount = segment / (float)segments;
                float envelope = MathF.Sin(amount * MathF.PI);
                float flowing = MathF.Sin(amount * MathF.Tau * 3.15f
                    - Age * (telegraph ? 5.2f : 10.8f) + strandPhase);
                float laneOffset = lane * width * .18f * envelope;
                float waveOffset = flowing * width * (telegraph ? .14f : .22f)
                    * envelope;
                // The cluster's own per-strand flicker (laneOffset/waveOffset)
                // rides on top of the beam's centerline, which LaserPointAt
                // bends into the authored travelling sine wave when
                // Amplitude != 0 -- so the whole tentacle cluster curves
                // together instead of just flickering around a straight line.
                Vector2 world = LaserPointAt(range * amount)
                    + normal * (laneOffset + waveOffset);
                Vector2 screen = camera.WorldToScreen(world,
                    playerWorldPosition, screenShake);
                if (previous.HasValue)
                {
                    float pulse = .5f + .5f * MathF.Sin(
                        Age * (telegraph ? 6.2f : 13.5f)
                        - amount * 13f + strandPhase);
                    int strandWidth = telegraph
                        ? Math.Max(2, (int)MathF.Round(width * (.1f + pulse * .035f)))
                        : Math.Max(3, (int)MathF.Round(width
                            * (.17f + pulse * .08f)));
                    Color strandColor = LaserTentacleColor(strand, amount);
                    if (Illusory)
                        strandColor = Color.Lerp(strandColor, UiTheme.Muted, .68f);
                    else
                        strandColor = Color.Lerp(strandColor, UiTheme.Cream,
                            telegraph ? .3f : .08f + pulse * .1f);
                    float alpha = telegraph
                        ? (.48f + pulse * .38f) * vfx
                        : (.78f + pulse * .22f) * vfx;
                    Primitives2D.Line(spriteBatch,
                        previous.Value + new Vector2(0, telegraph ? 2 : 4),
                        screen + new Vector2(0, telegraph ? 2 : 4),
                        UiTheme.Shadow * (telegraph ? .3f : .72f),
                        strandWidth + (telegraph ? 4 : 7));
                    Primitives2D.Line(spriteBatch, previous.Value, screen,
                        strandColor * alpha, strandWidth);
                    if (!telegraph && (strand == 2 || highContrast))
                        Primitives2D.Line(spriteBatch,
                            previous.Value - new Vector2(0, 1),
                            screen - new Vector2(0, 1),
                            Color.Lerp(strandColor, UiTheme.Cream, .72f)
                                * (.3f + pulse * .35f),
                            Math.Max(1, strandWidth / 3));
                }
                previous = screen;
            }
        }
    }

    private int StableLaserSeed()
    {
        int value = (int)MathF.Abs(OriginX * .17f + OriginY * .11f);
        if (Owner is not null)
            foreach (char character in Owner)
                value = unchecked(value * 31 + character);
        return value & 0x7fffffff;
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

    /// <summary>
    /// World-space point <paramref name="alongDistance"/> pixels out along a
    /// "laser" path projectile's beam, bent into a travelling sine wave when
    /// <see cref="Amplitude"/> is nonzero (see <see cref="LaserWaveSpeed"/>).
    /// Shared by the hit test and the draw so they always trace the same
    /// curve.
    /// </summary>
    private Vector2 LaserPointAt(float alongDistance)
    {
        var origin = new Vector2(WorldX, WorldY);
        var heading = new Vector2(MathF.Cos(Direction), MathF.Sin(Direction));
        if (Amplitude == 0)
            return origin + heading * alongDistance;
        var normal = new Vector2(-heading.Y, heading.X);
        float offset = Amplitude
            * MathF.Sin(Frequency * alongDistance - LaserWaveSpeed * Age);
        return origin + heading * alongDistance + normal * offset;
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
