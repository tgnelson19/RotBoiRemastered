using System.Diagnostics;
using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

public sealed class PathFogOfWarWindowTests
{
    private static Battleground OpenField(int size)
    {
        var tiles = new TileType[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tiles[y, x] = x == 0 || y == 0 || x == size - 1 || y == size - 1 ? TileType.OuterVoid : TileType.Default;
        return new Battleground(tiles, BiomePalettes.Soul, 10);
    }

    [Fact]
    public void WindowedFogSeesOnlyInsideTheWindowAndRemembersNothing()
    {
        var map = OpenField(120);
        var fog = new PathFogOfWar(map, windowRadiusTiles: 10);
        fog.Update(new Vector2(60.5f * Battleground.TileSize, 60.5f * Battleground.TileSize));
        Assert.True(fog.IsVisible(60, 60));
        Assert.True(fog.IsVisible(60 + 9, 60));
        Assert.False(fog.IsVisible(60 + 11, 60));
        Assert.False(fog.IsVisible(60 + 8, 60 + 8), "corner outside the circular window");
        Assert.False(fog.IsExplored(60, 60));

        // Move far away: the old window is cleared, nothing is remembered.
        fog.Update(new Vector2(20.5f * Battleground.TileSize, 20.5f * Battleground.TileSize));
        Assert.False(fog.IsVisible(60, 60));
        Assert.False(fog.IsExplored(60, 60));
        Assert.True(fog.IsVisible(20, 20));
    }

    [Fact]
    public void SubTileJitterDoesNotRetrace()
    {
        var map = OpenField(60);
        var fog = new PathFogOfWar(map, windowRadiusTiles: 6);
        var origin = new Vector2(30.5f * Battleground.TileSize, 30.5f * Battleground.TileSize);
        fog.Update(origin);
        // A 5px wobble keeps the same window; a full-tile move shifts it.
        fog.Update(origin + new Vector2(5, 0));
        Assert.True(fog.IsVisible(30 + 5, 30));
        fog.Update(origin + new Vector2(Battleground.TileSize * 3, 0));
        Assert.True(fog.IsVisible(30 + 8, 30));
        Assert.False(fog.IsVisible(30 - 4, 30));
    }

    [Fact]
    public void WindowedUpdatesOnTheEgoMapAreCheap()
    {
        var map = OpenField(400);
        var fog = new PathFogOfWar(map, windowRadiusTiles: 30);
        var rng = new Random(1);
        fog.Update(new Vector2(200 * Battleground.TileSize, 200 * Battleground.TileSize)); // warm-up
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            fog.Update(new Vector2(rng.Next(40, 360) * Battleground.TileSize + 25, rng.Next(40, 360) * Battleground.TileSize + 25));
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 400, $"100 updates took {stopwatch.ElapsedMilliseconds} ms");
    }
}
