using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.Entities;

public enum BossMotionTheme
{
    Touch,
    Phantasia,
    Sound,
    Chemesthesis,
    Sight,
}

public enum BossMovementMode
{
    Stationary,
    Chase,
    FixedPath,
    Burrow,
}

public enum BossPathShape
{
    None,
    Circle,
    Square,
    Triangle,
    FigureEight,
    Ellipse,
    Jagged,
}

/// <summary>Typed, inspectable locomotion authored for one boss phase.</summary>
public readonly record struct BossMovementPhaseProfile(
    BossMovementMode Mode,
    BossPathShape Path = BossPathShape.None,
    float LoopSeconds = 10f,
    float RadiusX = .55f,
    float RadiusY = .55f,
    int Direction = 1,
    float SpeedScale = 1f)
{
    public static BossMovementPhaseProfile Stationary() =>
        new(BossMovementMode.Stationary);

    public static BossMovementPhaseProfile Chase(float speedScale = 1f) =>
        new(BossMovementMode.Chase, SpeedScale: speedScale);

    public static BossMovementPhaseProfile Fixed(
        BossPathShape path,
        float loopSeconds,
        float radiusX = .55f,
        float radiusY = .55f,
        int direction = 1,
        float speedScale = 1f) =>
        new(BossMovementMode.FixedPath, path, loopSeconds, radiusX, radiusY,
            direction < 0 ? -1 : 1, speedScale);

    public static BossMovementPhaseProfile Burrow() =>
        new(BossMovementMode.Burrow);
}

/// <summary>
/// Shared "boss turns to face where it's heading" smoothing, generalized
/// from Aphantasia's own body-turning system (its private `_facingYaw`,
/// updated each Update() via an atan2-toward-target heading blended in with
/// an exponential turn rate rather than snapped, gated to only run while
/// actively chasing/pathing). Callers keep their own yaw field and pass it
/// back in every frame; this only computes the next value.
/// </summary>
public static class BossFacing
{
    /// <summary>
    /// Blend <paramref name="currentYaw"/> toward the angle from
    /// <paramref name="from"/> to <paramref name="toward"/>, at a rate that
    /// converges smoothly rather than snapping -- matching
    /// Aphantasia.cs's own `1f - MathF.Exp(-3.2f*dt)` turn blend. Callers are
    /// expected to gate this to their own "actively advancing" movement
    /// state (e.g. BossMovementMode.Chase/FixedPath) and to hold the boss's
    /// existing idle/ambient spin otherwise -- this helper only ever
    /// computes the next yaw, it doesn't know about idle fallback.
    /// </summary>
    public static float SmoothFacingYaw(float currentYaw, Vector2 from, Vector2 toward,
        double dt, float turnRate = 3.2f)
    {
        Vector2 delta = toward - from;
        if (delta.LengthSquared() < .0001f)
            return currentYaw;
        float desired = MathF.Atan2(delta.Y, delta.X);
        float diff = MathF.IEEERemainder(desired - currentYaw, MathF.Tau);
        float blend = 1f - MathF.Exp(-turnRate * (float)dt);
        return currentYaw + diff * blend;
    }
}

internal readonly record struct BossLocomotionFrame(
    BossMovementPhaseProfile Profile,
    Vector2 Target,
    float SpeedPerReferenceTick)
{
    public bool Stationary => Profile.Mode is BossMovementMode.Stationary or BossMovementMode.Burrow;
}

/// <summary>
/// Shared target authoring for bosses. It keeps movement targets separate
/// from combat targets and builds an arc-length table for every fixed path so
/// ellipses and figure-eights do not surge at their parameter seams.
/// </summary>
internal sealed class BossLocomotionController
{
    private const int PathSamples = 128;
    private readonly BossMotionTheme _theme;
    private readonly float[] _seed;
    private readonly Vector2[] _samples = new Vector2[PathSamples + 1];
    private readonly float[] _cumulative = new float[PathSamples + 1];
    private int _phase = -1;
    private BossMovementPhaseProfile _profile;
    private double _elapsed;
    private float _pathOffset;
    private float _pathLength;
    private Vector2 _steeredTarget;
    private Vector2 _lastPlayer;
    private Vector2 _playerVelocity;
    private double _soundDecisionRemaining;

    public BossLocomotionController(BossMotionTheme theme, IReadOnlyList<float> seed)
    {
        _theme = theme;
        _seed = seed.Count == 0 ? new[] { 0f } : seed.ToArray();
    }

    public BossLocomotionFrame Update(
        int phase,
        BossMovementPhaseProfile profile,
        Vector2 bossCenter,
        Vector2 player,
        Vector2 arenaCenter,
        float arenaRadius,
        float baseSpeed,
        double seconds)
    {
        seconds = Math.Clamp(seconds, 0, .05);
        if (_phase != phase || _profile != profile)
            EnterPhase(phase, profile, bossCenter, arenaCenter, arenaRadius);

        _elapsed += seconds;
        Vector2 playerDelta = player - _lastPlayer;
        if (seconds > 0 && _lastPlayer != Vector2.Zero)
            _playerVelocity = Vector2.Lerp(_playerVelocity,
                playerDelta / (float)seconds, .22f);
        _lastPlayer = player;

        if (profile.Mode is BossMovementMode.Stationary or BossMovementMode.Burrow)
            return new BossLocomotionFrame(profile, bossCenter, 0);

        if (profile.Mode == BossMovementMode.FixedPath)
        {
            float progress = ((float)(_elapsed / Math.Max(.1f, profile.LoopSeconds))
                * profile.Direction + _pathOffset) % 1f;
            if (progress < 0)
                progress += 1f;
            Vector2 target = SampleByDistance(progress);
            float pixelsPerSecond = _pathLength / Math.Max(.1f, profile.LoopSeconds);
            float referenceSpeed = pixelsPerSecond / (float)Simulation.ReferenceFps;
            return new BossLocomotionFrame(profile, target,
                Math.Max(baseSpeed, referenceSpeed * 1.16f) * profile.SpeedScale);
        }

        Vector2 chaseTarget = ThemedChaseTarget(
            bossCenter, player, arenaCenter, arenaRadius, seconds);
        float chaseScale = _theme switch
        {
            BossMotionTheme.Touch => .78f,
            BossMotionTheme.Phantasia => 1.05f,
            BossMotionTheme.Sound => 1f,
            BossMotionTheme.Chemesthesis => 1.15f + .24f * MathF.Sin((float)_elapsed * 4.7f),
            BossMotionTheme.Sight => 1.45f,
            _ => 1f,
        };
        return new BossLocomotionFrame(profile, chaseTarget,
            baseSpeed * chaseScale * profile.SpeedScale);
    }

    private void EnterPhase(int phase, BossMovementPhaseProfile profile,
        Vector2 bossCenter, Vector2 arenaCenter, float arenaRadius)
    {
        _phase = phase;
        _profile = profile;
        _elapsed = 0;
        _steeredTarget = bossCenter;
        _lastPlayer = Vector2.Zero;
        _playerVelocity = Vector2.Zero;
        _soundDecisionRemaining = 0;
        if (profile.Mode != BossMovementMode.FixedPath)
            return;

        BuildPath(profile, arenaCenter, arenaRadius, phase);
        int closest = 0;
        float closestDistance = float.PositiveInfinity;
        for (int index = 0; index < PathSamples; index++)
        {
            float distance = Vector2.DistanceSquared(bossCenter, _samples[index]);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = index;
            }
        }
        _pathOffset = _pathLength <= 0 ? 0 : _cumulative[closest] / _pathLength;
    }

    private void BuildPath(BossMovementPhaseProfile profile,
        Vector2 center, float radius, int phase)
    {
        _pathLength = 0;
        for (int index = 0; index <= PathSamples; index++)
        {
            float t = index / (float)PathSamples;
            _samples[index] = center + PathPoint(profile.Path, t, phase)
                * new Vector2(radius * profile.RadiusX, radius * profile.RadiusY);
            if (index > 0)
                _pathLength += Vector2.Distance(_samples[index - 1], _samples[index]);
            _cumulative[index] = _pathLength;
        }
    }

    private Vector2 PathPoint(BossPathShape shape, float t, int phase)
    {
        float angle = t * MathF.Tau;
        return shape switch
        {
            BossPathShape.Circle => new Vector2(MathF.Cos(angle), MathF.Sin(angle)),
            BossPathShape.Ellipse => new Vector2(MathF.Cos(angle), MathF.Sin(angle)),
            BossPathShape.FigureEight => new Vector2(
                MathF.Sin(angle), MathF.Sin(angle * 2f)),
            BossPathShape.Square => PolygonPoint(t, 4, MathF.PI / 4f),
            BossPathShape.Triangle => PolygonPoint(t, 3, -MathF.PI / 2f),
            BossPathShape.Jagged => JaggedPoint(t, phase),
            _ => Vector2.Zero,
        };
    }

    private static Vector2 PolygonPoint(float t, int sides, float rotation)
    {
        float scaled = t * sides;
        int side = Math.Min(sides - 1, (int)MathF.Floor(scaled));
        float local = scaled - side;
        float firstAngle = rotation + side * MathF.Tau / sides;
        float secondAngle = rotation + (side + 1) * MathF.Tau / sides;
        var first = new Vector2(MathF.Cos(firstAngle), MathF.Sin(firstAngle));
        var second = new Vector2(MathF.Cos(secondAngle), MathF.Sin(secondAngle));
        return Vector2.Lerp(first, second, local);
    }

    private Vector2 JaggedPoint(float t, int phase)
    {
        const int points = 9;
        float scaled = t * points;
        int point = (int)MathF.Floor(scaled) % points;
        float local = scaled - MathF.Floor(scaled);
        Vector2 P(int index)
        {
            index = (index % points + points) % points;
            float angle = index * MathF.Tau / points;
            float variance = _seed[(index + phase * 3) % _seed.Length];
            float radial = .68f + (index % 3) * .13f + variance;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radial;
        }
        Vector2 p0 = P(point - 1), p1 = P(point), p2 = P(point + 1), p3 = P(point + 2);
        float u2 = local * local, u3 = u2 * local;
        return .5f * ((2f * p1) + (-p0 + p2) * local
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
    }

    private Vector2 SampleByDistance(float progress)
    {
        if (_pathLength <= .001f)
            return _samples[0];
        float desired = progress * _pathLength;
        int upper = Array.BinarySearch(_cumulative, desired);
        if (upper < 0)
            upper = ~upper;
        upper = Math.Clamp(upper, 1, PathSamples);
        int lower = upper - 1;
        float span = Math.Max(.001f, _cumulative[upper] - _cumulative[lower]);
        return Vector2.Lerp(_samples[lower], _samples[upper],
            (desired - _cumulative[lower]) / span);
    }

    private Vector2 ThemedChaseTarget(Vector2 boss, Vector2 player,
        Vector2 arenaCenter, float arenaRadius, double seconds)
    {
        Vector2 desired;
        switch (_theme)
        {
            case BossMotionTheme.Touch:
                desired = player;
                _steeredTarget = Vector2.Lerp(_steeredTarget, desired,
                    Math.Min(1f, (float)seconds * .85f));
                return _steeredTarget;
            case BossMotionTheme.Phantasia:
            {
                Vector2 radial = player - arenaCenter;
                Vector2 tangent = radial.LengthSquared() > 1
                    ? Vector2.Normalize(new Vector2(-radial.Y, radial.X))
                    : Vector2.UnitX;
                desired = player + tangent * arenaRadius * .16f
                    * MathF.Sin((float)_elapsed * .72f);
                _steeredTarget = Vector2.Lerp(_steeredTarget, desired,
                    Math.Min(1f, (float)seconds * 2.25f));
                return _steeredTarget;
            }
            case BossMotionTheme.Sound:
                _soundDecisionRemaining -= seconds;
                if (_soundDecisionRemaining <= 0)
                {
                    _soundDecisionRemaining += .5;
                    Vector2 direction = player - boss;
                    float angle = MathF.Atan2(direction.Y, direction.X);
                    angle = MathF.Round(angle / (MathF.PI / 4f)) * MathF.PI / 4f;
                    _steeredTarget = player + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                        * arenaRadius * .08f;
                }
                return _steeredTarget;
            case BossMotionTheme.Chemesthesis:
            {
                float first = _seed[(_phase * 5 + 3) % _seed.Length];
                float second = _seed[(_phase * 7 + 1) % _seed.Length];
                desired = player + new Vector2(
                    MathF.Sin((float)_elapsed * (2.7f + first)) * arenaRadius * .24f,
                    MathF.Sin((float)_elapsed * (4.1f + second) + 1.7f) * arenaRadius * .20f);
                _steeredTarget = Vector2.Lerp(_steeredTarget, desired,
                    Math.Min(1f, (float)seconds * 5.4f));
                return _steeredTarget;
            }
            case BossMotionTheme.Sight:
                desired = player + Vector2.Clamp(_playerVelocity * .20f,
                    new Vector2(-arenaRadius * .3f), new Vector2(arenaRadius * .3f));
                return desired;
            default:
                return player;
        }
    }
}
