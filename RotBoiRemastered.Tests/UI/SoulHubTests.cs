using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

public sealed class SoulHubTests
{
    [Fact]
    public void StationRadius_ClosesOnlyAfterPlayerWalksBeyondDismissalDistance()
    {
        var station = new Vector2(400, 300);
        var justInside = station + new Vector2(Simulation.TileSize * 1.84f, 0);
        var justOutside = station + new Vector2(Simulation.TileSize * 1.86f, 0);

        Assert.True(SoulHub.WithinStationRadius(justInside, station, 1.85f));
        Assert.False(SoulHub.WithinStationRadius(justOutside, station, 1.85f));
    }
}
