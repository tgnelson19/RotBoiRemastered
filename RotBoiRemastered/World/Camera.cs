using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>
/// World-to-screen projection and camera yaw. Ported from background.py's
/// camera-related module state and functions.
///
/// Cleanup vs. the Python original: cameraAngleDegrees/lockX/lockY were
/// module-level globals reassigned via `global` statements; here they're
/// instance state on a proper class. WorldToScreen/ScreenToWorld also no
/// longer reach into hidden module state for player position and screen
/// shake (background.py oddly owned playerPosX/playerPosY itself, despite
/// being "the world/background module") -- they take both as explicit
/// parameters instead, so Camera has no implicit dependency on Player or
/// shake-effect internals. Player position ownership moves to whatever
/// represents the player once Entities/ is ported.
/// </summary>
public sealed class Camera
{
    public float AngleDegrees { get; private set; }

    /// <summary>Screen-space anchor point the player is drawn at (was lockX/lockY).</summary>
    public Vector2 Lock { get; set; }

    /// <summary>Set continuous camera yaw in degrees, normalized to one revolution.</summary>
    public void SetAngle(float degrees)
    {
        // C#'s % preserves the dividend's sign (-10 % 360 == -10), unlike
        // Python's, which always returns a result in [0, 360) for a positive
        // divisor. A single correction is enough since |remainder| < 360 always.
        float normalized = degrees % 360f;
        if (normalized < 0)
            normalized += 360f;
        AngleDegrees = normalized;
    }

    /// <summary>Compatibility helper for callers that want an exact cardinal view.</summary>
    public void SetQuarterTurns(int turns) => SetAngle(turns * 90f);

    /// <summary>Rotate the world counter-clockwise for positive degree values.</summary>
    public void Rotate(float degrees) => SetAngle(AngleDegrees + degrees);

    private (float Cos, float Sin) CameraComponents()
    {
        float angle = MathHelper.ToRadians(AngleDegrees);
        return (MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>Rotate a world-space vector into the current camera orientation.</summary>
    public Vector2 WorldVectorToScreen(Vector2 delta)
    {
        var (cosine, sine) = CameraComponents();
        return new Vector2(
            delta.X * cosine + delta.Y * sine,
            -delta.X * sine + delta.Y * cosine);
    }

    /// <summary>Rotate a screen-space vector back onto the world's ground plane.</summary>
    public Vector2 ScreenVectorToWorld(Vector2 delta)
    {
        var (cosine, sine) = CameraComponents();
        return new Vector2(
            delta.X * cosine - delta.Y * sine,
            delta.X * sine + delta.Y * cosine);
    }

    public Vector2 WorldToScreen(Vector2 worldPosition, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenDelta = WorldVectorToScreen(worldPosition - playerWorldPosition);
        return screenDelta + Lock + screenShake;
    }

    public Vector2 ScreenToWorld(Vector2 screenPosition, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenDelta = screenPosition - Lock - screenShake;
        Vector2 worldDelta = ScreenVectorToWorld(screenDelta);
        return playerWorldPosition + worldDelta;
    }
}
