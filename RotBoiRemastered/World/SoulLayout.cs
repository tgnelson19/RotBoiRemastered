using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>
/// Single authored source of truth for The Soul's collision geometry and
/// interaction anchors. Tile generation and SoulHub interaction placement
/// both consume these values so a visual shrine can never drift away from
/// the floor or portal tile that supports it.
/// </summary>
internal static class SoulLayout
{
    public const int Width = 79;
    public const int Height = 81;
    public const int SelectionAreaScale = 2;
    public static readonly Point SpawnTile = new(39, 65);
    public static readonly Point NexusTile = new(39, 43);
    public static readonly Point TunnelSouthTile = new(39, 56);
    public static readonly Point DummyTile = new(50, 58);

    public static readonly IReadOnlyDictionary<string, Point> StationTiles =
        new Dictionary<string, Point>
        {
            ["storage"] = new(30, 70),
            ["quests"] = new(30, 64),
            ["skills"] = new(39, 75),
            ["wardrobe"] = new(48, 64),
            ["hard_mode"] = new(48, 70),
        };

    /// <summary>
    /// Original crown vectors retained as authored data. PortalTiles applies
    /// SelectionAreaScale to these vectors so geometry, visuals, and
    /// interaction anchors all stretch from the same source.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Point> PortalOffsets =
        new Dictionary<string, Point>
        {
            ["sound"] = new(-10, -4),
            ["touch"] = new(-5, -9),
            ["sight"] = new(0, -13),
            ["chemesthesis"] = new(5, -9),
            ["phantasia"] = new(10, -4),
        };

    public static readonly IReadOnlyDictionary<string, Point> PortalTiles =
        PortalOffsets.ToDictionary(
            entry => entry.Key,
            entry => new Point(
                NexusTile.X + entry.Value.X * SelectionAreaScale,
                NexusTile.Y + entry.Value.Y * SelectionAreaScale));

    public static Vector2 TileWorldCenter(Point tile) => new(
        (tile.X + .5f) * Battleground.TileSize,
        (tile.Y + .5f) * Battleground.TileSize);

    public static Vector2 SpawnTopLeft =>
        TileWorldCenter(SpawnTile) - new Vector2(Battleground.TileSize * .375f);

    public static TileType[,] BuildTiles()
    {
        var grid = new TileType[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                grid[y, x] = TileType.OuterVoid;

        // Main chapel nave and its softer apse/alcove silhouette.
        PaintRect(grid, 26, 58, 52, 75, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(39, 57), 9, 6, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(27, 64), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(27, 70), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(51, 64), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(51, 70), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(39, 75), 6, 4, TileType.BuildingFloor);
        PaintEllipse(grid, DummyTile, 5, 4, TileType.BuildingFloor);

        // A quiet processional aisle ties every utility shrine to the apse.
        PaintRect(grid, 38, 55, 40, 76, TileType.Road);
        PaintLine(grid, new Point(29, 67), new Point(49, 67), 1, TileType.Road);

        // The short transition remains physically legible at 0% VFX.
        PaintLine(grid, TunnelSouthTile, NexusTile, 3, TileType.Road);

        // Composite dais and five compact, genuinely separated Path branches.
        PaintEllipse(grid, NexusTile, 5, 5, TileType.BuildingFloor);
        PaintRing(grid, NexusTile, 3.1f, 4.25f, TileType.Road);
        foreach (Point portal in PortalTiles.Values)
        {
            PaintLine(grid, NexusTile, portal, 2, TileType.Road);
            PaintEllipse(grid, portal, 3, 3, TileType.BuildingFloor);
            PaintRing(grid, portal, 1.55f, 2.55f, TileType.Road);
        }

        // Grow a one-tile masonry shell around the authored walkable mask.
        // Computing the mask first prevents the shell from recursively
        // spreading across the OuterVoid during this pass.
        var shell = new bool[Height, Width];
        for (int y = 1; y < Height - 1; y++)
        {
            for (int x = 1; x < Width - 1; x++)
            {
                if (grid[y, x] != TileType.OuterVoid)
                    continue;
                for (int oy = -1; oy <= 1 && !shell[y, x]; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        if (grid[y + oy, x + ox] is TileType.BuildingFloor or TileType.Road)
                        {
                            shell[y, x] = true;
                            break;
                        }
            }
        }
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (shell[y, x])
                    grid[y, x] = TileType.BuildingWall;

        return grid;
    }

    private static void PaintRect(TileType[,] grid, int left, int top, int right, int bottom, TileType tile)
    {
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                grid[y, x] = tile;
    }

    private static void PaintEllipse(TileType[,] grid, Point center, int radiusX, int radiusY, TileType tile)
    {
        for (int y = center.Y - radiusY; y <= center.Y + radiusY; y++)
        {
            for (int x = center.X - radiusX; x <= center.X + radiusX; x++)
            {
                float dx = (x - center.X) / (float)radiusX;
                float dy = (y - center.Y) / (float)radiusY;
                if (dx * dx + dy * dy <= 1f)
                    grid[y, x] = tile;
            }
        }
    }

    private static void PaintLine(TileType[,] grid, Point start, Point end, int halfWidth, TileType tile)
    {
        int steps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        for (int step = 0; step <= steps; step++)
        {
            int x = (int)MathF.Round(MathHelper.Lerp(start.X, end.X, step / (float)Math.Max(1, steps)));
            int y = (int)MathF.Round(MathHelper.Lerp(start.Y, end.Y, step / (float)Math.Max(1, steps)));
            for (int oy = -halfWidth; oy <= halfWidth; oy++)
                for (int ox = -halfWidth; ox <= halfWidth; ox++)
                    grid[y + oy, x + ox] = tile;
        }
    }

    private static void PaintRing(TileType[,] grid, Point center, float innerRadius, float outerRadius, TileType tile)
    {
        int extent = (int)MathF.Ceiling(outerRadius);
        for (int y = center.Y - extent; y <= center.Y + extent; y++)
        {
            for (int x = center.X - extent; x <= center.X + extent; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center.X, center.Y));
                if (distance >= innerRadius && distance <= outerRadius)
                    grid[y, x] = tile;
            }
        }
    }
}
