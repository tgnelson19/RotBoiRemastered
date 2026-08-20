using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>
/// Single authored source of truth for The Mind's collision geometry and
/// interaction anchors. Tile generation and MindHub interaction placement
/// both consume these values so a visual shrine can never drift away from
/// the floor or portal tile that supports it.
/// </summary>
internal static class SoulLayout
{
    public const int Width = 119;
    public const int Height = 130;
    public const int MinimumBoundaryBufferTiles = 12;
    public const int SelectionAreaScale = 2;
    public static readonly Point SpawnTile = new(59, 78);
    public static readonly Point NexusTile = new(59, 38);
    public static readonly Point TunnelSouthTile = new(59, 68);
    public static readonly Point BodyDoorTile = new(59, 92);
    public static readonly Point DummyTile = new(72, 72);
    /// <summary>
    /// Center of the safe chapel crossing. The Aphantasia victory trophy is
    /// decorative rather than colliding, so the wide nave remains navigable.
    /// </summary>
    public static readonly Point AphantasiaStatueTile = new(59, 80);
    // Legacy name retained for source compatibility: this is the campaign's
    // combined Body / Soul entrance, below The Mind's home chapel.
    public static readonly Point CorePortalTile = new(59, 112);
    public static readonly Point AphantasiaPortalTile = new(37, 112);

    public static readonly IReadOnlyDictionary<string, Point> StationTiles =
        new Dictionary<string, Point>
        {
            ["storage"] = new(48, 84),
            ["quests"] = new(48, 76),
            ["skills"] = new(59, 89),
            ["wardrobe"] = new(70, 76),
            ["hard_mode"] = new(70, 84),
            ["no_extract"] = new(68, 87),
            ["developer_armory"] = new(54, 89),
        };

    /// <summary>
    /// Original crown vectors retained as authored data. PortalTiles applies
    /// SelectionAreaScale to these vectors so geometry, visuals, and
    /// interaction anchors all stretch from the same source.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Point> PortalOffsets =
        new Dictionary<string, Point>
        {
            ["touch"] = new(-21, 4),
            ["sight"] = new(-12, -7),
            ["sound"] = new(0, -10),
            ["chemesthesis"] = new(12, -7),
            ["phantasia"] = new(19, 5),
        };

    public static readonly IReadOnlyDictionary<string, Point> PortalTiles =
        PortalOffsets.ToDictionary(
            entry => entry.Key,
            entry => new Point(
                NexusTile.X + entry.Value.X * SelectionAreaScale,
                NexusTile.Y + entry.Value.Y * SelectionAreaScale));

    public static readonly IReadOnlySet<string> AllGateKeys =
        PortalOffsets.Keys.Concat(["core", "aphantasia"]).ToHashSet();

    /// <summary>Maps legacy non-colliding chapel art into the wider layout.</summary>
    public static Point AuthoredTile(int x, int y) => new(x + 20, y + 13);

    public static Vector2 TileWorldCenter(Point tile) => new(
        (tile.X + .5f) * Battleground.TileSize,
        (tile.Y + .5f) * Battleground.TileSize);

    public static Vector2 SpawnTopLeft =>
        TileWorldCenter(SpawnTile) - new Vector2(Battleground.TileSize * .375f);

    public static TileType[,] BuildTiles(IReadOnlySet<string>? unlockedPaths = null)
    {
        var grid = new TileType[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                grid[y, x] = TileType.OuterVoid;

        // Main home chapel and its balanced utility alcoves.
        PaintRect(grid, 46, 69, 72, 87, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(59, 68), 9, 6, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(47, 76), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(47, 84), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(71, 76), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(71, 84), 4, 4, TileType.BuildingFloor);
        PaintEllipse(grid, new Point(59, 89), 6, 4, TileType.BuildingFloor);
        PaintEllipse(grid, DummyTile, 5, 4, TileType.BuildingFloor);

        // A quiet processional aisle ties every utility shrine to the apse.
        PaintRect(grid, 58, 66, 60, 90, TileType.Road);
        PaintCapsule(grid, new Point(49, 80), new Point(69, 80), 1.45f, TileType.Road);

        // The short transition remains physically legible at 0% VFX.
        PaintCapsule(grid, TunnelSouthTile, NexusTile, 3.25f, TileType.Road);

        // Composite dais and five compact, genuinely separated Path branches.
        PaintEllipse(grid, NexusTile, 5, 5, TileType.BuildingFloor);
        PaintRing(grid, NexusTile, 3.1f, 4.25f, TileType.Road);
        foreach (Point portal in PortalTiles.Values)
        {
            PaintCapsule(grid, NexusTile, portal, 2.35f, TileType.Road);
            PaintEllipse(grid, portal, 4, 4, TileType.BuildingFloor);
            PaintRing(grid, portal, 2.2f, 3.55f, TileType.Road);
        }

        // Campaign progression leaves the home base downward. The five arena
        // clears open the Body / Soul door; five Soul finales then open the
        // short, leftward void walk to Aphantasia.
        PaintCapsule(grid, BodyDoorTile, CorePortalTile, 2.35f, TileType.Road);
        PaintEllipse(grid, CorePortalTile, 4, 4, TileType.BuildingFloor);
        PaintRing(grid, CorePortalTile, 2.2f, 3.55f, TileType.Road);
        PaintCapsule(grid, CorePortalTile, AphantasiaPortalTile, 2.35f, TileType.Road);
        PaintEllipse(grid, AphantasiaPortalTile, 4, 4, TileType.BuildingFloor);
        PaintRing(grid, AphantasiaPortalTile, 2.2f, 3.55f, TileType.Road);

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

        if (unlockedPaths is not null)
        {
            foreach ((string sense, Point portal) in PortalTiles)
                if (!unlockedPaths.Contains(sense))
                    SealBranch(grid, portal);
            if (!unlockedPaths.Contains("core"))
                SealCorridor(grid, BodyDoorTile, CorePortalTile, .38f);
            if (!unlockedPaths.Contains("aphantasia"))
                SealCorridor(grid, CorePortalTile, AphantasiaPortalTile, .55f);
        }

        return grid;
    }

    private static void SealBranch(TileType[,] grid, Point portal)
    {
        SealCorridor(grid, NexusTile, portal, .70f);
    }

    private static void SealCorridor(TileType[,] grid, Point start, Point end, float amount)
    {
        Vector2 direction = end.ToVector2() - start.ToVector2();
        direction.Normalize();
        Vector2 perpendicular = new(-direction.Y, direction.X);
        // Keep the seal inside its own outer branch. Nearer the nexus, the
        // five wide corridors overlap enough that one locked wing can clip a
        // neighboring unlocked route.
        Vector2 gate = Vector2.Lerp(start.ToVector2(), end.ToVector2(), amount);
        const int extent = 5;
        for (int y = Math.Max(1, (int)gate.Y - extent);
             y <= Math.Min(Height - 2, (int)gate.Y + extent); y++)
            for (int x = Math.Max(1, (int)gate.X - extent);
                 x <= Math.Min(Width - 2, (int)gate.X + extent); x++)
            {
                Vector2 delta = new Vector2(x, y) - gate;
                float depth = MathF.Abs(Vector2.Dot(delta, direction));
                float span = MathF.Abs(Vector2.Dot(delta, perpendicular));
                if (depth <= 1.65f && span <= 3.75f
                    && grid[y, x] is TileType.BuildingFloor or TileType.Road)
                    grid[y, x] = TileType.BuildingWall;
            }
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

    private static void PaintCapsule(TileType[,] grid, Point start, Point end,
        float radius, TileType tile)
    {
        Vector2 a = start.ToVector2();
        Vector2 segment = end.ToVector2() - a;
        float lengthSquared = Math.Max(.001f, segment.LengthSquared());
        int left = Math.Max(0, (int)MathF.Floor(Math.Min(start.X, end.X) - radius));
        int right = Math.Min(Width - 1, (int)MathF.Ceiling(Math.Max(start.X, end.X) + radius));
        int top = Math.Max(0, (int)MathF.Floor(Math.Min(start.Y, end.Y) - radius));
        int bottom = Math.Min(Height - 1, (int)MathF.Ceiling(Math.Max(start.Y, end.Y) + radius));
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                Vector2 point = new(x, y);
                float amount = Math.Clamp(Vector2.Dot(point - a, segment) / lengthSquared, 0f, 1f);
                if (Vector2.DistanceSquared(point, a + segment * amount) <= radius * radius)
                    grid[y, x] = tile;
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
