using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

internal static class EntityTestFixtures
{
    /// <summary>5x5 grid: 1-tile wall border around a 3x3 open interior (250x250 world units at TileSize=50).</summary>
    public static Battleground SmallOpenRoom()
    {
        var tiles = new TileType[5, 5];
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                tiles[y, x] = (x == 0 || x == 4 || y == 0 || y == 4) ? TileType.ArenaWall : TileType.Default;
        return new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);
    }
}
