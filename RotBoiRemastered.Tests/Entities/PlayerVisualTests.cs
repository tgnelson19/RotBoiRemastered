using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

public sealed class PlayerVisualTests
{
    [Theory]
    [InlineData(60, 100, 100, 60)]
    [InlineData(60, 50, 100, 30)]
    [InlineData(60, 0, 100, 0)]
    [InlineData(60, -10, 100, 0)]
    [InlineData(60, 140, 100, 60)]
    [InlineData(60, 10, 0, 60)]
    public void HealthBarFillIsClampedToTheAuthoredWidth(
        int width,
        int health,
        int maximum,
        int expected)
    {
        Assert.Equal(
            expected,
            Player.HealthBarFillWidth(width, health, maximum));
    }
}
