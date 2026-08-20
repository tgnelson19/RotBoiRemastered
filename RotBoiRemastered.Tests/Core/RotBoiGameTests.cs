using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Core;

public sealed class RotBoiGameTests
{
    [Fact]
    public void CompletedRunCanOpenResultsWithKeyboardOrController()
    {
        Assert.True(RotBoiGame.ResultsRequested(
            new HashSet<Keys> { Keys.Enter }, controllerConfirm: false));
        Assert.True(RotBoiGame.ResultsRequested(
            new HashSet<Keys>(), controllerConfirm: true));
        Assert.False(RotBoiGame.ResultsRequested(
            new HashSet<Keys>(), controllerConfirm: false));
    }

    [Fact]
    public void OnlyDefeatResultsKeepTheOldWorldAnimatingBehindTheBanner()
    {
        Assert.True(RotBoiGame.ResultsWorldContinues(RunOutcomes.Defeated));
        Assert.False(RotBoiGame.ResultsWorldContinues(RunOutcomes.Extracted));
        Assert.False(RotBoiGame.ResultsWorldContinues(RunOutcomes.RunComplete));
        Assert.False(RotBoiGame.ResultsWorldContinues(null));
    }
}
