namespace RotBoiRemastered.World;

/// <summary>
/// Per-tile sight rules for The Ego's windowed fog. Open ground (plains,
/// city streets, holdouts) is simply seen; building interiors are only seen
/// from inside; caverns demand a true line of sight so nothing is visible
/// around a corner. See <see cref="PathFogOfWar"/> for how the zones combine.
/// </summary>
public static class EgoSightZones
{
    public const byte Open = 0;
    public const byte Interior = 1;
    public const byte Cavern = 2;

    public static byte[] Build(Battleground battleground, EgoTerrain[,] terrain)
    {
        int width = battleground.Width, height = battleground.Height;
        var zones = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                TileType tile = battleground.TileAt(x, y);
                byte zone = Open;
                if (tile == TileType.BuildingFloor || (tile.IsRaised() && IsInternalPartition(battleground, x, y)))
                    zone = Interior;
                else if (terrain[y, x] == EgoTerrain.Caverns)
                    zone = Cavern;
                zones[y * width + x] = zone;
            }
        return zones;
    }

    /// <summary>A raised tile whose every walkable neighbour is building floor: an inner wall, hidden with the rooms it divides.</summary>
    private static bool IsInternalPartition(Battleground battleground, int x, int y)
    {
        bool anyFloor = false;
        foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
        {
            if (nx < 0 || ny < 0 || nx >= battleground.Width || ny >= battleground.Height)
                continue;
            TileType neighbour = battleground.TileAt(nx, ny);
            if (neighbour.IsSolid())
                continue;
            if (neighbour != TileType.BuildingFloor)
                return false;
            anyFloor = true;
        }
        return anyFloor;
    }
}
