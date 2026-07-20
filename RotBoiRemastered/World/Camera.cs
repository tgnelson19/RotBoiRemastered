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
    public const float MinZoom = .5f;
    public const float MaxZoom = 3.5f;
    public const float ZoomStep = .15f;
    public const double MinDefaultZoomScale = .75;
    public const double MaxDefaultZoomScale = 1.5;
    private const int ReferenceWidth = 1920;
    private const int ReferenceHeight = 1080;
    public float AngleDegrees { get; private set; }
    public float Zoom { get; private set; } = 1f;
    public float DefaultZoom { get; private set; } = 1f;

    /// <summary>Screen-space center of the player and camera rotation pivot.</summary>
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

    public void SetZoom(float zoom) => Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
    public void AdjustZoom(float amount) => SetZoom(Zoom + amount);

    /// <summary>
    /// Makes world objects occupy roughly the same proportion of the viewport
    /// at every resolution. The preference is a persisted accessibility
    /// multiplier; manual O/P or wheel adjustments remain relative when the
    /// window is resized or fullscreen is toggled.
    /// </summary>
    public void ConfigureViewport(int width, int height, double preference, bool resetZoom = false)
    {
        float previousDefault = DefaultZoom;
        DefaultZoom = DefaultZoomForViewport(width, height, preference);
        if (resetZoom)
            SetZoom(DefaultZoom);
        else if (Math.Abs(DefaultZoom - previousDefault) > .0001f)
            SetZoom(Zoom * DefaultZoom / previousDefault);
    }

    public void ResetView()
    {
        SetAngle(0);
        SetZoom(DefaultZoom);
    }

    public static float DefaultZoomForViewport(int width, int height, double preference = 1.0)
    {
        float resolutionScale = Math.Min((float)Math.Max(1, width) / ReferenceWidth,
            (float)Math.Max(1, height) / ReferenceHeight);
        resolutionScale = Math.Clamp(resolutionScale, .65f, 2.4f);
        float preferred = (float)Math.Clamp(preference, MinDefaultZoomScale, MaxDefaultZoomScale);
        return Math.Clamp(resolutionScale * preferred, MinZoom, MaxZoom);
    }

    /// <summary>Uniform world-only zoom around the player/camera lock.</summary>
    public Matrix WorldTransform =>
        Matrix.CreateTranslation(-Lock.X, -Lock.Y, 0)
        * Matrix.CreateScale(Zoom, Zoom, 1)
        * Matrix.CreateTranslation(Lock.X, Lock.Y, 0);

    public Vector2 ApplyZoom(Vector2 logicalScreenPosition) =>
        Lock + (logicalScreenPosition - Lock) * Zoom;

    public Vector2 RemoveZoom(Vector2 displayScreenPosition) =>
        Lock + (displayScreenPosition - Lock) / Zoom;

    public Rectangle LogicalViewport(Rectangle displayViewport)
    {
        Vector2 topLeft = RemoveZoom(new Vector2(displayViewport.Left, displayViewport.Top));
        Vector2 bottomRight = RemoveZoom(new Vector2(displayViewport.Right, displayViewport.Bottom));
        return new Rectangle((int)MathF.Floor(topLeft.X), (int)MathF.Floor(topLeft.Y),
            (int)MathF.Ceiling(bottomRight.X - topLeft.X), (int)MathF.Ceiling(bottomRight.Y - topLeft.Y));
    }

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
