using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;

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
}
