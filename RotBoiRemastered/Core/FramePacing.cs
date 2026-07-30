namespace RotBoiRemastered.Core;

/// <summary>
/// User-facing frame pacing limits. MonoGame supports both a fixed maximum
/// update/draw rate and presentation synchronized to the display refresh.
/// </summary>
public static class FramePacing
{
    public const int MinimumFrameRate = 30;
    public const int MaximumFrameRate = 360;
    public const int FrameRateStep = 5;
    public const int DefaultFrameRate = 120;

    public static int NormalizeFrameRate(int frameRate)
    {
        int clamped = Math.Clamp(frameRate, MinimumFrameRate, MaximumFrameRate);
        int stepped = (int)Math.Round(clamped / (double)FrameRateStep) * FrameRateStep;
        return Math.Clamp(stepped, MinimumFrameRate, MaximumFrameRate);
    }

    public static TimeSpan TargetElapsedTime(int frameRate) =>
        TimeSpan.FromSeconds(1.0 / NormalizeFrameRate(frameRate));
}
