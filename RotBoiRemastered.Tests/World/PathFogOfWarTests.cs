using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

public sealed class PathFogOfWarTests
{
    private static Battleground OpenGrid(int width = 10, int height = 10)
    {
        var tiles = new TileType[height, width];
        return new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);
    }

    private static Vector2 TileCenter(float x, float y) =>
        new(x * Battleground.TileSize, y * Battleground.TileSize);

    [Fact]
    public void Update_RevealsBlockingWallButNotTilesBehindIt()
    {
        var battleground = OpenGrid();
        battleground.Tiles[4, 4] = TileType.ArenaWall;
        var fog = new PathFogOfWar(battleground);

        fog.Update(TileCenter(2.5f, 4.5f));

        Assert.True(fog.IsVisible(4, 4));
        Assert.False(fog.IsVisible(5, 4));
        Assert.False(fog.IsExplored(5, 4));
    }

    [Fact]
    public void Update_KeepsOldSightExploredButNoLongerVisible()
    {
        var battleground = OpenGrid(20, 8);
        for (int y = 0; y < battleground.Height; y++)
            battleground.Tiles[y, 10] = TileType.BuildingWall;
        var fog = new PathFogOfWar(battleground);
        fog.Update(TileCenter(2.5f, 3.5f));
        Assert.True(fog.IsVisible(2, 3));

        fog.Update(TileCenter(15.5f, 3.5f));

        Assert.False(fog.IsVisible(2, 3));
        Assert.True(fog.IsExplored(2, 3));
        Assert.True(fog.IsVisible(15, 3));
    }

    [Fact]
    public void LineOfSight_ChangesAsObserverPeeksPastACorner()
    {
        var battleground = OpenGrid();
        battleground.Tiles[3, 4] = TileType.BuildingWall;
        var fog = new PathFogOfWar(battleground);

        Assert.False(fog.HasLineOfSight(3.5f, 3.5f, 5, 4));
        Assert.True(fog.HasLineOfSight(3.5f, 4.4f, 5, 4));
    }

    [Fact]
    public void IsWorldAreaVisible_ReturnsTrueWhenAnyCoveredTileIsLit()
    {
        var battleground = OpenGrid();
        for (int y = 0; y < battleground.Height; y++)
            battleground.Tiles[y, 6] = TileType.BuildingWall;
        var fog = new PathFogOfWar(battleground);
        fog.Update(TileCenter(2.5f, 2.5f));

        Assert.True(fog.IsWorldAreaVisible(new Rectangle(
            2 * Battleground.TileSize, 2 * Battleground.TileSize,
            Battleground.TileSize * 2, Battleground.TileSize)));
        Assert.False(fog.IsWorldAreaVisible(new Rectangle(
            8 * Battleground.TileSize, 8 * Battleground.TileSize,
            Battleground.TileSize, Battleground.TileSize)));
    }

    [Fact]
    public void Update_RevealsAnUnobstructedHallwayToTheFarEdgeOfTheFloor()
    {
        var battleground = OpenGrid(width: 141, height: 7);
        for (int x = 0; x < battleground.Width; x++)
        {
            battleground.Tiles[1, x] = TileType.BuildingWall;
            battleground.Tiles[5, x] = TileType.BuildingWall;
        }
        var fog = new PathFogOfWar(battleground);

        fog.Update(TileCenter(2.5f, 3.5f));

        Assert.True(fog.IsVisible(138, 3));
        Assert.True(fog.IsVisible(138, 1));
        Assert.True(fog.IsVisible(138, 5));
    }

    [Fact]
    public void Update_RevealsAnObscuredConvexCornerWhenItsNeighborWallIsVisible()
    {
        var battleground = OpenGrid();
        battleground.Tiles[5, 4] = TileType.BuildingWall;
        battleground.Tiles[5, 5] = TileType.BuildingWall;
        battleground.Tiles[4, 5] = TileType.BuildingWall;
        var fog = new PathFogOfWar(battleground);
        Vector2 observer = TileCenter(3.5f, 5.5f);

        Assert.False(fog.HasLineOfSight(3.5f, 5.5f, 5, 5));

        fog.Update(observer);

        Assert.True(fog.IsVisible(4, 5));
        Assert.True(fog.IsVisible(5, 5));
    }

    [Fact]
    public void Update_DoesNotCascadeCornerSupportAlongAnObscuredWall()
    {
        var battleground = OpenGrid();
        battleground.Tiles[5, 4] = TileType.BuildingWall;
        battleground.Tiles[5, 5] = TileType.BuildingWall;
        battleground.Tiles[4, 5] = TileType.BuildingWall;
        battleground.Tiles[4, 6] = TileType.BuildingWall;
        var fog = new PathFogOfWar(battleground);

        fog.Update(TileCenter(3.5f, 5.5f));

        Assert.True(fog.IsVisible(5, 5));
        Assert.False(fog.IsVisible(6, 4));
    }
}
