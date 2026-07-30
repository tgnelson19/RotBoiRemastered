using RotBoiRemastered.Core;

namespace RotBoiRemastered.Tests.Core;

public sealed class FramePacingTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(30, 30)]
    [InlineData(143, 145)]
    [InlineData(360, 360)]
    [InlineData(999, 360)]
    public void NormalizeFrameRate_ClampsAndUsesFiveFpsSteps(
        int requested,
        int expected)
    {
        Assert.Equal(expected, FramePacing.NormalizeFrameRate(requested));
    }

    [Fact]
    public void TargetElapsedTime_MatchesNormalizedFrameRate()
    {
        TimeSpan target = FramePacing.TargetElapsedTime(144);

        double error = Math.Abs((1.0 / 145.0) - target.TotalSeconds);
        Assert.InRange(error, 0, TimeSpan.FromTicks(1).TotalSeconds);
    }
}
