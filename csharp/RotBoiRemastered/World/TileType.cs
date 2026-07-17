namespace RotBoiRemastered.World;

/// <summary>
/// Ported from background.py's raw tile-id integers (0-5). The Python
/// original also had a `tileTypes` dict mapping ids to display names/colors,
/// but nothing in the codebase ever reads it (grep-confirmed dead code) --
/// dropped rather than carried over.
/// </summary>
public enum TileType
{
    Default = 0,
    ArenaWall = 1,
    Road = 2,
    BuildingFloor = 3,
    BuildingWall = 4,
    OuterVoid = 5,
}

/// <summary>
/// Ported from background.py's RAISED_TILES/SOLID_TILES sets. Extension
/// methods instead of raw `tile in {1, 4}` membership checks scattered
/// through call sites, now that tiles are a real enum instead of bare ints.
/// </summary>
public static class TileTypeExtensions
{
    private static readonly HashSet<TileType> RaisedTiles = new() { TileType.ArenaWall, TileType.BuildingWall };
    private static readonly HashSet<TileType> SolidTilesSet = new()
    {
        TileType.ArenaWall, TileType.BuildingWall, TileType.OuterVoid,
    };

    public static bool IsRaised(this TileType tile) => RaisedTiles.Contains(tile);
    public static bool IsSolid(this TileType tile) => SolidTilesSet.Contains(tile);
}
