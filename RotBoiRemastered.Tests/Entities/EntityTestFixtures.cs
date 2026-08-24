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

    /// <summary>
    /// 60x60 grid: 1-tile wall border around a 58x58 open interior (3000x3000
    /// world units at TileSize=50) -- for geometry that needs to travel
    /// hundreds of pixels without clipping against <see cref="SmallOpenRoom"/>'s
    /// tight 250x250 bounds, such as a laser's wall raycast.
    /// </summary>
    public static Battleground LargeOpenRoom()
    {
        const int size = 60;
        var tiles = new TileType[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tiles[y, x] = (x == 0 || x == size - 1 || y == 0 || y == size - 1)
                    ? TileType.ArenaWall : TileType.Default;
        return new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);
    }
}
