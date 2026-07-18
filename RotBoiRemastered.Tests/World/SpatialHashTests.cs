using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

/// <summary>Ported from tests/test_spatial_hash.py.</summary>
public class SpatialHashTests
{
    private sealed class Marker
    {
    }

    [Fact]
    public void Query_OnlyReturnsNearbyObjects_WithoutDuplicates()
    {
        var grid = new SpatialHash<Marker>(cellSize: 10);
        var near = new Marker();
        var far = new Marker();
        grid.Insert(near, new Rectangle(5, 5, 11, 11));
        grid.Insert(far, new Rectangle(40, 40, 5, 5));

        var result = grid.Query(new Rectangle(8, 8, 10, 10)).ToList();

        Assert.Equal(new[] { near }, result);
    }

    [Fact]
    public void Query_DoesNotDuplicate_WhenItemSpansMultipleCells()
    {
        var grid = new SpatialHash<Marker>(cellSize: 10);
        var spanning = new Marker();
        // Spans four cells at cellSize=10.
        grid.Insert(spanning, new Rectangle(8, 8, 6, 6));

        var result = grid.Query(new Rectangle(0, 0, 20, 20)).ToList();

        Assert.Single(result);
        Assert.Same(spanning, result[0]);
    }
}
