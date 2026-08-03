namespace RotBoiRemastered.Core;

/// <summary>
/// Shared, deterministic animation curves for presentation-only loops. The
/// helpers keep moving particles continuous at their wrap point without
/// introducing mutable particle state into the simulation.
/// </summary>
public static class VisualAnimation
{
    public static float LoopPhase(float seconds, float duration, float offset = 0f)
    {
        float phase = seconds / Math.Max(.001f, duration) + offset;
        return phase - MathF.Floor(phase);
    }

    public static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    /// <summary>
    /// Fades a traveling visual out before it wraps and back in after the
    /// wrap. Circular motion should not use this because its position already
    /// joins continuously.
    /// </summary>
    public static float SeamFade(float phase, float seam = .14f)
    {
        phase -= MathF.Floor(phase);
        seam = Math.Clamp(seam, .01f, .49f);
        float fadeIn = SmoothStep(phase / seam);
        float fadeOut = SmoothStep((1f - phase) / seam);
        return Math.Min(fadeIn, fadeOut);
    }

    public static float Sine(float seconds, float period, float offset = 0f) =>
        MathF.Sin(LoopPhase(seconds, period, offset) * MathF.Tau);

    public static float CosinePulse(float seconds, float period, float offset = 0f) =>
        .5f - .5f * MathF.Cos(LoopPhase(seconds, period, offset) * MathF.Tau);
}
