namespace RotBoiRemastered.Core;

/// <summary>
/// Frame-rate-independent timing, ported from the timing slice of
/// variableHolster.py (tileSizeGlobal, frameRate, REFERENCE_FPS,
/// get_frame_scale, get_timer_step, set_delta_time). The rest of
/// variableHolster.py -- pygame.init(), display-mode/fullscreen setup,
/// joystick init, mouse/keyboard globals -- is display/input plumbing that
/// belongs with Core/RotBoiGame.cs's own MonoGame window and input handling
/// once that's wired up, not here.
/// </summary>
public static class Simulation
{
    public const int TileSize = 50;
    public const int FrameRate = 120;
    private const double ReferenceFps = 240;

    private static double _deltaMilliseconds = 1000.0 / FrameRate;
    private static bool _hasFrameDelta;

    /// <summary>Call once per frame from Game.Update with GameTime.ElapsedGameTime.</summary>
    public static void SetDeltaTime(double milliseconds)
    {
        _deltaMilliseconds = milliseconds;
        _hasFrameDelta = true;
    }

    /// <summary>
    /// Elapsed time in units of the original 240 Hz simulation step. The
    /// upper clamp prevents a debugger pause or window drag from teleporting
    /// every entity on the next frame. Before the first frame, the
    /// configured cap remains a useful deterministic fallback for tests and
    /// object construction.
    /// </summary>
    public static double GetFrameScale()
    {
        if (!_hasFrameDelta || _deltaMilliseconds <= 0)
            return FrameRate > 0 ? ReferenceFps / FrameRate : 1.0;
        return Math.Min(_deltaMilliseconds * ReferenceFps / 1000, ReferenceFps * 0.05);
    }

    /// <summary>Elapsed time in units of one configured-FPS timer tick.</summary>
    public static double GetTimerStep()
    {
        if (!_hasFrameDelta)
            return 1.0;
        return Math.Min(_deltaMilliseconds * FrameRate / 1000, FrameRate * 0.05);
    }

    /// <summary>Resets to the pre-first-frame state. Test-only, mirrors a fresh Python module import.</summary>
    public static void ResetForTests()
    {
        _deltaMilliseconds = 1000.0 / FrameRate;
        _hasFrameDelta = false;
    }
}
