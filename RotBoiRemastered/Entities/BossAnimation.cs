using RotBoiRemastered.Core;

namespace RotBoiRemastered.Entities;

/// <summary>Pure helpers for boss animation loops with invisible wrap seams.</summary>
internal static class BossAnimation
{
    public static float LoopPhase(float seconds, float duration, float offset = 0f)
        => VisualAnimation.LoopPhase(seconds, duration, offset);

    public static float SmoothStep(float value)
        => VisualAnimation.SmoothStep(value);

    public static float SeamFade(float phase, float seam = .14f)
        => VisualAnimation.SeamFade(phase, seam);

    public static float Sine(float seconds, float period, float offset = 0f) =>
        VisualAnimation.Sine(seconds, period, offset);

    public static float CosinePulse(float seconds, float period, float offset = 0f) =>
        VisualAnimation.CosinePulse(seconds, period, offset);

    public static float EaseInOutSine(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return .5f - .5f * MathF.Cos(value * MathF.PI);
    }

    public static float EaseOutBack(float value, float overshoot = 1.35f)
    {
        value = Math.Clamp(value, 0f, 1f) - 1f;
        return 1f + (overshoot + 1f) * value * value * value
            + overshoot * value * value;
    }

    public static float AttackPulse(float remainingFrames, float durationSeconds)
    {
        float duration = Math.Max(1f, Simulation.FrameRate * durationSeconds);
        float progress = 1f - Math.Clamp(remainingFrames / duration, 0f, 1f);
        return MathF.Sin(progress * MathF.PI);
    }
}
