using RotBoiRemastered.Core;

namespace RotBoiRemastered.Tests.Core;

public sealed class VisualAnimationTests
{
    [Fact]
    public void LoopPhaseWrapsPositiveAndNegativeInputs()
    {
        Assert.Equal(.25f, VisualAnimation.LoopPhase(.5f, 2f), 5);
        Assert.Equal(.75f, VisualAnimation.LoopPhase(-.5f, 2f), 5);
    }

    [Fact]
    public void SeamFadeHidesTravelingVisualAtBothEnds()
    {
        Assert.Equal(0f, VisualAnimation.SeamFade(0f), 5);
        Assert.Equal(0f, VisualAnimation.SeamFade(1f), 5);
        Assert.Equal(1f, VisualAnimation.SeamFade(.5f), 5);
        Assert.InRange(VisualAnimation.SeamFade(.05f), 0f, 1f);
    }
}
