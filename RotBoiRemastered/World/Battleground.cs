using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>Ported from background.py's _paint_building style strings ("bastion", "archive", etc.).</summary>
public enum BuildingStyle { Plain, Bastion, Archive, Forge, Shrine, Vault }

/// <summary>
/// The tile grid for one arena/map, plus wall-collision queries and the five
/// procedural generators. Ported from background.py.
///
/// Cleanup vs. the Python original:
/// - Module-level globals (currRoomRects, spawnX/Y, BIOME_PALETTES, WALL_HEIGHT,
///   reassigned via `global` in configure_battleground) become instance state
///   on a proper class. Switching paths means creating a new Battleground via
///   CreateForPath rather than mutating one in place.
/// - Each tile was stored as `[type_int, pygame.Rect]` -- a mutable 2-element
///   list per cell, with the Rect redundant (fully derivable from x, y,
///   TileSize). Tiles here is a plain TileType[,]; TileRect computes the
///   rectangle on demand.
/// - The `_open_tile_cache`/`_raised_scenery_cache` dicts were keyed by
///   `id(currRoomRects)` and cleared on every miss -- which means they only
///   ever held one entry in practice. That's just a lazily-computed instance
///   field (_openTiles), no cache invalidation machinery needed.
/// - Raw tile-id ints (0-5) and building-style strings ("bastion", "archive",
///   ...) become the TileType and BuildingStyle enums (see TileType.cs).
/// - generate_battleground/generate_touch_battleground built their tile-id/
///   Rect pairs inline while the other three generators reused a shared
///   `_rect_grid` helper -- an inconsistency that no longer exists here since
///   no generator needs to build Rects at all.
/// </summary>
public sealed class Battleground
{
    public const int TileSize = 50;

    public TileType[,] Tiles { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<BiomePalette> Palettes { get; }
    public int WallHeight { get; }
    public Vector2 SpawnPosition { get; }

    private (int X, int Y)[]? _openTiles;

    public Battleground(TileType[,] tiles, IReadOnlyList<BiomePalette> palettes, int wallHeight, Vector2? spawnPosition = null)
    {
        Tiles = tiles;
        Height = tiles.GetLength(0);
        Width = tiles.GetLength(1);
        Palettes = palettes;
        WallHeight = wallHeight;
        SpawnPosition = spawnPosition ?? new Vector2(
            Width / 2 * TileSize - TileSize / 2f,
            Height / 2 * TileSize - TileSize / 2f);
    }

    public TileType TileAt(int x, int y) => Tiles[y, x];

    public Rectangle TileRect(int x, int y) => new(x * TileSize, y * TileSize, TileSize, TileSize);

    public bool IsRaisedAt(int x, int y) =>
        y >= 0 && y < Height && x >= 0 && x < Width && Tiles[y, x].IsRaised();

    /// <summary>Splits the circular map into three broad, intentionally soft-edged wards.</summary>
    public int BiomeForTile(int tileX, int tileY)
    {
        double dx = tileX - Width / 2.0;
        double dy = tileY - Height / 2.0;
        if (dy < -Math.Abs(dx) * .28)
            return 0; // the violet archive
        if (dx < 0)
            return 1; // the ember ward
        return 2; // the drowned circuit
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>True if any tile overlapped by the world rect is a wall (out of bounds counts as one).</summary>
    public bool RectHitsWall(Rectangle worldRect)
    {
        int left = FloorDiv(worldRect.Left, TileSize);
        int top = FloorDiv(worldRect.Top, TileSize);
        int right = FloorDiv(worldRect.Right - 1, TileSize);
        int bottom = FloorDiv(worldRect.Bottom - 1, TileSize);
        if (left < 0 || top < 0 || bottom >= Height || right >= Width)
            return true;
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                if (Tiles[y, x].IsSolid())
                    return true;
        return false;
    }

    /// <summary>
    /// Tests a convex world-space polygon against solid tiles. This is used by
    /// screen-aligned entities whose world footprint rotates with the camera.
    /// Merely touching a tile edge is allowed, matching Rectangle.Intersects.
    /// </summary>
    public bool ConvexPolygonHitsWall(IReadOnlyList<Vector2> polygon)
    {
        if (polygon.Count < 3)
            return false;
        float minX = polygon.Min(point => point.X), maxX = polygon.Max(point => point.X);
        float minY = polygon.Min(point => point.Y), maxY = polygon.Max(point => point.Y);
        if (minX < 0 || minY < 0 || maxX > Width * TileSize || maxY > Height * TileSize)
            return true;

        const float edgeEpsilon = 0.0001f;
        int left = Math.Clamp((int)Math.Floor(minX / TileSize), 0, Width - 1);
        int top = Math.Clamp((int)Math.Floor(minY / TileSize), 0, Height - 1);
        int right = Math.Clamp((int)Math.Floor((maxX - edgeEpsilon) / TileSize), 0, Width - 1);
        int bottom = Math.Clamp((int)Math.Floor((maxY - edgeEpsilon) / TileSize), 0, Height - 1);
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (Tiles[y, x].IsSolid() && ConvexPolygonIntersectsRectangle(polygon, TileRect(x, y)))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Separating-axis intersection for a convex polygon and an axis-aligned rectangle.</summary>
    public static bool ConvexPolygonIntersectsRectangle(IReadOnlyList<Vector2> polygon, Rectangle rectangle)
    {
        if (polygon.Count < 3)
            return false;
        var rectangleCorners = new[]
        {
            new Vector2(rectangle.Left, rectangle.Top), new Vector2(rectangle.Right, rectangle.Top),
            new Vector2(rectangle.Right, rectangle.Bottom), new Vector2(rectangle.Left, rectangle.Bottom),
        };
        var axes = new List<Vector2> { Vector2.UnitX, Vector2.UnitY };
        for (int index = 0; index < polygon.Count; index++)
        {
            Vector2 edge = polygon[(index + 1) % polygon.Count] - polygon[index];
            if (edge.LengthSquared() > 0.000001f)
                axes.Add(new Vector2(-edge.Y, edge.X));
        }

        foreach (var axis in axes)
        {
            float polygonMin = float.PositiveInfinity, polygonMax = float.NegativeInfinity;
            foreach (var point in polygon)
            {
                float projection = Vector2.Dot(point, axis);
                polygonMin = Math.Min(polygonMin, projection);
                polygonMax = Math.Max(polygonMax, projection);
            }
            float rectangleMin = float.PositiveInfinity, rectangleMax = float.NegativeInfinity;
            foreach (var point in rectangleCorners)
            {
                float projection = Vector2.Dot(point, axis);
                rectangleMin = Math.Min(rectangleMin, projection);
                rectangleMax = Math.Max(rectangleMax, projection);
            }
            if (polygonMax <= rectangleMin || rectangleMax <= polygonMin)
                return false;
        }
        return true;
    }

    private (int X, int Y)[] OpenTiles()
    {
        if (_openTiles is not null)
            return _openTiles;
        var tiles = new List<(int, int)>();
        for (int y = 1; y < Height - 1; y++)
            for (int x = 1; x < Width - 1; x++)
                if (!Tiles[y, x].IsSolid())
                    tiles.Add((x, y));
        _openTiles = tiles.ToArray();
        return _openTiles;
    }

    /// <summary>Returns a world-space rect that fits completely inside a random open floor tile.</summary>
    public Rectangle FindSpawnRect(int size, Vector2 playerWorldPosition, int minDistanceTiles = 4, Random? rng = null)
    {
        rng ??= Random.Shared;
        int playerTileX = (int)Math.Floor(playerWorldPosition.X / TileSize);
        int playerTileY = (int)Math.Floor(playerWorldPosition.Y / TileSize);
        var openTiles = OpenTiles();

        if (openTiles.Length == 0)
            return new Rectangle(TileSize, TileSize, size, size);

        // Random probing makes spawn work effectively constant-time even in large
        // rooms. The deterministic full scan is retained only as a rare fallback.
        foreach (bool requireDistance in new[] { true, false })
        {
            int attempts = Math.Min(32, Math.Max(1, openTiles.Length));
            for (int i = 0; i < attempts; i++)
            {
                var (tileX, tileY) = openTiles[rng.Next(openTiles.Length)];
                if (requireDistance && Math.Abs(tileX - playerTileX) < minDistanceTiles
                                     && Math.Abs(tileY - playerTileY) < minDistanceTiles)
                    continue;
                var candidate = new Rectangle(
                    (int)(tileX * TileSize + (TileSize - size) / 2.0),
                    (int)(tileY * TileSize + (TileSize - size) / 2.0),
                    size, size);
                if (!RectHitsWall(candidate))
                    return candidate;
            }
        }

        foreach (var (tileX, tileY) in openTiles)
        {
            var candidate = new Rectangle(
                (int)(tileX * TileSize + (TileSize - size) / 2.0),
                (int)(tileY * TileSize + (TileSize - size) / 2.0),
                size, size);
            if (!RectHitsWall(candidate))
                return candidate;
        }

        return new Rectangle(TileSize, TileSize, size, size);
    }

    /// <summary>
    /// Returns the nearest world-space rect that does not overlap wall tiles.
    /// Python's version took an unused `size` parameter (the search only ever
    /// used world_rect's own width/height) -- dropped here rather than ported
    /// faithfully, since porting dead parameters isn't worth preserving.
    /// </summary>
    public Rectangle FindNearestOpenRect(Rectangle worldRect)
    {
        if (!RectHitsWall(worldRect))
            return worldRect;

        int step = Math.Max(1, TileSize / 8);
        int maxDistance = Math.Max(step, TileSize);
        var bestCandidate = worldRect;
        int bestDistance = 1_000_000;

        for (int offsetX = -maxDistance; offsetX <= maxDistance; offsetX += step)
        {
            for (int offsetY = -maxDistance; offsetY <= maxDistance; offsetY += step)
            {
                if (offsetX == 0 && offsetY == 0)
                    continue;
                var candidate = new Rectangle(worldRect.X + offsetX, worldRect.Y + offsetY,
                    worldRect.Width, worldRect.Height);
                if (!RectHitsWall(candidate))
                {
                    int distance = Math.Abs(offsetX) + Math.Abs(offsetY);
                    if (distance < bestDistance)
                    {
                        bestCandidate = candidate;
                        bestDistance = distance;
                    }
                }
            }
        }
        return bestCandidate;
    }

    /// <summary>Tries a tiny local search to find a nearby open rect around a blocking wall.</summary>
    public Rectangle FindPathAroundWalls(Rectangle worldRect, float desiredDeltaX, float desiredDeltaY, int size)
    {
        var candidateOffsets = new List<(int X, int Y)>();
        if (desiredDeltaX != 0)
            candidateOffsets.Add(((int)(Math.Abs(desiredDeltaX) * 0.5) * (desiredDeltaX < 0 ? -1 : 1), 0));
        if (desiredDeltaY != 0)
            candidateOffsets.Add((0, (int)(Math.Abs(desiredDeltaY) * 0.5) * (desiredDeltaY < 0 ? -1 : 1)));
        candidateOffsets.AddRange(new (int, int)[]
        {
            (-size, 0), (size, 0), (0, -size), (0, size),
            (-size, -size), (-size, size), (size, -size), (size, size),
        });

        foreach (var (offsetX, offsetY) in candidateOffsets)
        {
            var candidate = new Rectangle(worldRect.X + offsetX, worldRect.Y + offsetY,
                worldRect.Width, worldRect.Height);
            if (!RectHitsWall(candidate))
                return candidate;
        }
        return FindNearestOpenRect(worldRect);
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private static void PaintRoad(TileType[,] grid, (int X, int Y) start, (int X, int Y) end, int width = 1)
    {
        int height = grid.GetLength(0), gridWidth = grid.GetLength(1);
        int steps = Math.Max(Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)), 1);
        for (int step = 0; step <= steps; step++)
        {
            int x = (int)Math.Round(start.X + (end.X - start.X) * (double)step / steps);
            int y = (int)Math.Round(start.Y + (end.Y - start.Y) * (double)step / steps);
            for (int oy = -width; oy <= width; oy++)
            {
                for (int ox = -width; ox <= width; ox++)
                {
                    int gy = y + oy, gx = x + ox;
                    if (gy >= 0 && gy < height && gx >= 0 && gx < gridWidth
                        && grid[gy, gx] != TileType.ArenaWall && grid[gy, gx] != TileType.OuterVoid)
                    {
                        grid[gy, gx] = TileType.Road;
                    }
                }
            }
        }
    }

    private static void PaintBuilding(TileType[,] grid, int centerX, int centerY, int width = 11, int height = 9,
        bool verticalDoors = false, BuildingStyle style = BuildingStyle.Plain)
    {
        int left = centerX - width / 2;
        int top = centerY - height / 2;
        int right = left + width - 1;
        int bottom = top + height - 1;
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                grid[y, x] = (x == left || x == right || y == top || y == bottom)
                    ? TileType.BuildingWall : TileType.BuildingFloor;

        // Every structure has two opposite, two-tile-wide passages.
        if (verticalDoors)
        {
            foreach (int x in new[] { centerX - 1, centerX })
            {
                grid[top, x] = TileType.Road;
                grid[bottom, x] = TileType.Road;
            }
        }
        else
        {
            foreach (int y in new[] { centerY - 1, centerY })
            {
                grid[y, left] = TileType.Road;
                grid[y, right] = TileType.Road;
            }
        }

        // Small silhouette changes make each ruin recognizable at a glance
        // while every room keeps its two safe, opposite exits.
        switch (style)
        {
            case BuildingStyle.Bastion:
                foreach (var (x, y) in new[]
                         {
                             (left + 2, top + 2), (right - 2, top + 2),
                             (left + 2, bottom - 2), (right - 2, bottom - 2),
                         })
                    grid[y, x] = TileType.BuildingWall;
                break;
            case BuildingStyle.Archive:
                for (int y = top + 2; y < bottom - 1; y += 3)
                {
                    grid[y, left + 2] = TileType.BuildingWall;
                    grid[y, right - 2] = TileType.BuildingWall;
                }
                break;
            case BuildingStyle.Forge:
                foreach (var (x, y) in new[] { (left + 2, top + 2), (right - 2, top + 2) })
                    grid[y, x] = TileType.BuildingWall;
                grid[bottom - 2, centerX] = TileType.BuildingWall;
                break;
            case BuildingStyle.Shrine:
                foreach (var (x, y) in new[]
                         {
                             (centerX, centerY - 1), (centerX - 1, centerY),
                             (centerX + 1, centerY), (centerX, centerY + 1),
                         })
                    grid[y, x] = TileType.BuildingWall;
                break;
            case BuildingStyle.Vault:
                for (int x = left + 2; x < right - 1; x++)
                    if (x != centerX - 1 && x != centerX)
                        grid[top + 2, x] = TileType.BuildingWall;
                break;
        }
    }

    private static void CircularShell(TileType[,] grid, int thickness = 2)
    {
        int size = grid.GetLength(0);
        int center = size / 2;
        int radius = center - 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double distance = Hypot(x - center, y - center);
                if (distance >= radius)
                    grid[y, x] = TileType.OuterVoid;
                else if (distance >= radius - thickness)
                    grid[y, x] = TileType.ArenaWall;
            }
        }
    }

    /// <summary>Creates a circular arena with roads, a central plaza, and six buildings (the original arena).</summary>
    public static Battleground GenerateSound(int size = 97)
    {
        size = Math.Max(61, size | 1);
        int center = size / 2;
        int radius = center - 2;
        var grid = new TileType[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double distance = Hypot(x - center, y - center);
                if (distance >= radius)
                    grid[y, x] = TileType.OuterVoid;
                else if (distance >= radius - 1)
                    grid[y, x] = TileType.ArenaWall;
            }
        }

        double layoutScale = size / 97.0;
        var buildingSpecs = new (int OffsetX, int OffsetY, bool Vertical, int Width, int Height, BuildingStyle Style)[]
        {
            (-23, -22, false, 13, 9, BuildingStyle.Bastion),
            (23, -22, false, 9, 13, BuildingStyle.Archive),
            (-28, 2, true, 11, 11, BuildingStyle.Forge),
            (28, 2, true, 15, 9, BuildingStyle.Plain),
            (-18, 26, false, 9, 11, BuildingStyle.Shrine),
            (18, 26, false, 13, 11, BuildingStyle.Vault),
        };
        var buildings = buildingSpecs.Select(spec => (
            X: center + (int)Math.Round(spec.OffsetX * layoutScale),
            Y: center + (int)Math.Round(spec.OffsetY * layoutScale),
            spec.Vertical, spec.Width, spec.Height, spec.Style)).ToArray();

        foreach (var building in buildings)
            PaintRoad(grid, (center, center), (building.X, building.Y), 1);

        for (int y = center - 7; y < center + 8; y++)
            for (int x = center - 7; x < center + 8; x++)
                if (Hypot(x - center, y - center) <= 7)
                    grid[y, x] = TileType.Road;

        foreach (var building in buildings)
            PaintBuilding(grid, building.X, building.Y, building.Width, building.Height, building.Vertical, building.Style);

        return new Battleground(grid, BiomePalettes.Sound, wallHeight: 14);
    }

    /// <summary>Creates the cramped sewer-prison used by the Path of Touch.</summary>
    public static Battleground GenerateTouch(int size = 87)
    {
        size = Math.Max(65, size | 1);
        int center = size / 2;
        var grid = new TileType[size, size];
        // A square containment shell makes this dungeon feel built to hold something.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int edge = new[] { x, y, size - 1 - x, size - 1 - y }.Min();
                if (edge < 2)
                    grid[y, x] = TileType.OuterVoid;
                else if (edge < 4)
                    grid[y, x] = TileType.ArenaWall;
            }
        }

        // Dense cell blocks leave narrow north/south and east/west drainage lanes.
        var blocks = new (int Ox, int Oy, int Width, int Height, bool Vertical, BuildingStyle Style)[]
        {
            (-27, -25, 13, 13, false, BuildingStyle.Vault), (-9, -25, 11, 13, false, BuildingStyle.Archive),
            (10, -25, 13, 13, false, BuildingStyle.Bastion), (28, -25, 11, 13, false, BuildingStyle.Vault),
            (-27, -7, 13, 11, true, BuildingStyle.Archive), (27, -7, 13, 11, true, BuildingStyle.Forge),
            (-27, 11, 13, 11, true, BuildingStyle.Bastion), (27, 11, 13, 11, true, BuildingStyle.Vault),
            (-27, 28, 13, 11, false, BuildingStyle.Forge), (-9, 28, 11, 11, false, BuildingStyle.Vault),
            (10, 28, 13, 11, false, BuildingStyle.Archive), (28, 28, 11, 11, false, BuildingStyle.Bastion),
        };
        foreach (var block in blocks)
            PaintBuilding(grid, center + block.Ox, center + block.Oy, block.Width, block.Height, block.Vertical, block.Style);

        // Main sewer channels and a small safe cistern at the spawn.
        PaintRoad(grid, (center, 4), (center, size - 5), 1);
        PaintRoad(grid, (4, center), (size - 5, center), 1);
        for (int y = center - 5; y < center + 6; y++)
            for (int x = center - 5; x < center + 6; x++)
                if (Hypot(x - center, y - center) <= 5)
                    grid[y, x] = TileType.Road;

        return new Battleground(grid, BiomePalettes.Touch, wallHeight: 22);
    }

    /// <summary>An exposed, building-free field with clear long sight lines.</summary>
    public static Battleground GenerateSight(int size = 91)
    {
        size = Math.Max(65, size | 1);
        int center = size / 2;
        var grid = new TileType[size, size];
        CircularShell(grid, 1);
        for (int angleIndex = 0; angleIndex < 8; angleIndex++)
        {
            double angle = angleIndex * 6.283185307 / 8;
            var end = (center + (int)Math.Round(Math.Cos(angle) * (center - 6)),
                       center + (int)Math.Round(Math.Sin(angle) * (center - 6)));
            PaintRoad(grid, (center, center), end, 1);
        }
        for (int y = center - 7; y < center + 8; y++)
            for (int x = center - 7; x < center + 8; x++)
                if (Hypot(x - center, y - center) <= 7)
                    grid[y, x] = TileType.Road;

        return new Battleground(grid, BiomePalettes.Sight, wallHeight: 14);
    }

    /// <summary>Open contaminated ground scattered with deterministic ruin fragments.</summary>
    public static Battleground GenerateChemesthesis(int size = 93)
    {
        size = Math.Max(67, size | 1);
        int center = size / 2;
        var grid = new TileType[size, size];
        CircularShell(grid, 1);
        // Broken corners and wall runs imply structures without creating real rooms.
        for (int y = 7; y < size - 7; y++)
        {
            for (int x = 7; x < size - 7; x++)
            {
                bool farFromSpawn = Hypot(x - center, y - center) > 9;
                int marker = (x * 47 + y * 83 + x * y * 7) % 317;
                if (farFromSpawn && (marker == 2 || marker == 3 || marker == 5 || marker == 71 || marker == 72))
                {
                    grid[y, x] = TileType.BuildingWall;
                    if ((marker == 2 || marker == 71) && x + 1 < size - 5)
                        grid[y, x + 1] = TileType.BuildingWall;
                    if ((marker == 3 || marker == 72) && y + 1 < size - 5)
                        grid[y + 1, x] = TileType.BuildingWall;
                }
            }
        }
        PaintRoad(grid, (5, center), (size - 6, center), 1);
        PaintRoad(grid, (center, 5), (center, size - 6), 1);

        return new Battleground(grid, BiomePalettes.Chemesthesis, wallHeight: 14);
    }

    /// <summary>Large dream courts with only three elaborate architectural anchors.</summary>
    public static Battleground GeneratePhantasia(int size = 101)
    {
        size = Math.Max(75, size | 1);
        int center = size / 2;
        var grid = new TileType[size, size];
        CircularShell(grid, 2);

        var buildings = new (int X, int Y, int Width, int Height, bool Vertical, BuildingStyle Style)[]
        {
            (center - 28, center - 21, 17, 15, false, BuildingStyle.Archive),
            (center + 29, center - 8, 15, 19, true, BuildingStyle.Shrine),
            (center - 6, center + 30, 21, 13, false, BuildingStyle.Bastion),
        };
        foreach (var building in buildings)
        {
            PaintRoad(grid, (center, center), (building.X, building.Y), 2);
            PaintBuilding(grid, building.X, building.Y, building.Width, building.Height, building.Vertical, building.Style);
            // Ornamental crowns, paired pillars, and approach gates add feature
            // density without filling the otherwise broad arena.
            foreach (var (ox, oy) in new[]
                     {
                         (-building.Width / 2 - 2, -building.Height / 2), (building.Width / 2 + 2, -building.Height / 2),
                         (-building.Width / 2 - 2, building.Height / 2), (building.Width / 2 + 2, building.Height / 2),
                     })
            {
                grid[building.Y + oy, building.X + ox] = TileType.BuildingWall;
            }
            foreach (int offset in new[] { -3, 0, 3 })
            {
                if (building.Vertical)
                {
                    grid[building.Y + offset, building.X - building.Width / 2 - 3] = TileType.BuildingWall;
                    grid[building.Y + offset, building.X + building.Width / 2 + 3] = TileType.BuildingWall;
                }
                else
                {
                    grid[building.Y - building.Height / 2 - 3, building.X + offset] = TileType.BuildingWall;
                    grid[building.Y + building.Height / 2 + 3, building.X + offset] = TileType.BuildingWall;
                }
            }
        }
        for (int y = center - 9; y < center + 10; y++)
            for (int x = center - 9; x < center + 10; x++)
                if (Hypot(x - center, y - center) <= 9)
                    grid[y, x] = TileType.Road;

        return new Battleground(grid, BiomePalettes.Phantasia, wallHeight: 20);
    }

    /// <summary>
    /// Authored sanctuary used only by The Soul. The player begins in the
    /// compact southern holdout, crosses a narrow northbound light tunnel,
    /// and emerges into a broad oval chamber whose five open bays are owned
    /// by the path portals. Unlike combat maps, its asymmetry is intentional:
    /// the spawn is low in the map so the architecture reads as a journey
    /// from safety toward possibility rather than another arena ring.
    /// </summary>
    public static Battleground GenerateSoul()
    {
        const int width = 79, height = 81;
        int centerX = width / 2;
        const int spawnY = 65;
        var grid = new TileType[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[y, x] = TileType.OuterVoid;

        // The portal chamber: a low, wide ellipse with a solid architectural
        // lip. Its interior is deliberately simple because SoulHub supplies
        // the animated paths and portal-specific decorative bleed.
        const int chamberY = 22, chamberRadiusX = 33, chamberRadiusY = 19;
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                double dx = (x - centerX) / (double)chamberRadiusX;
                double dy = (y - chamberY) / (double)chamberRadiusY;
                double distance = dx * dx + dy * dy;
                if (distance <= 1)
                    grid[y, x] = distance >= .84 ? TileType.ArenaWall : TileType.BuildingFloor;
            }
        }

        // A five-tile-wide processional tunnel joins the two spaces. Building
        // walls make the colored ribbons feel enclosed and dimensional.
        for (int y = 37; y <= 58; y++)
        {
            for (int x = centerX - 5; x <= centerX + 5; x++)
                grid[y, x] = Math.Abs(x - centerX) == 5 ? TileType.BuildingWall : TileType.Road;
        }

        // Compact holdout with one northern opening. Everything interactive
        // is positioned along its lower half by SoulHub.Enter.
        const int holdoutLeft = 25, holdoutRight = 53, holdoutTop = 55, holdoutBottom = 77;
        for (int y = holdoutTop; y <= holdoutBottom; y++)
        {
            for (int x = holdoutLeft; x <= holdoutRight; x++)
            {
                bool boundary = x == holdoutLeft || x == holdoutRight || y == holdoutTop || y == holdoutBottom;
                bool northDoor = y == holdoutTop && Math.Abs(x - centerX) <= 4;
                grid[y, x] = boundary && !northDoor ? TileType.BuildingWall : TileType.BuildingFloor;
            }
        }

        // Radial floor inlays give each portal bay a subtle permanent spine;
        // the animated path ribbons are drawn directly over these.
        var portalTiles = new[] { (15, 25), (27, 20), (39, 18), (51, 20), (63, 25) };
        foreach (var portal in portalTiles)
            PaintRoad(grid, (centerX, 37), portal, 1);

        var spawn = new Vector2(
            centerX * TileSize - TileSize / 2f,
            spawnY * TileSize - TileSize / 2f);
        return new Battleground(grid, BiomePalettes.Soul, wallHeight: 24, spawnPosition: spawn);
    }

    /// <summary>
    /// Ported from configure_battleground's path->generator dispatch. Unlike
    /// the Python version, "sound" isn't special-cased to reuse a
    /// module-level cached basicRoomRects -- none of the five generators use
    /// randomness (their "noise" is deterministic hash-like arithmetic on
    /// tile coordinates), so regenerating is just as deterministic and cheap
    /// enough (sub-millisecond for a ~100x100 grid) not to need caching here.
    /// </summary>
    public static Battleground CreateForPath(string pathKey) => pathKey switch
    {
        "sound" => GenerateSound(),
        "touch" => GenerateTouch(),
        "sight" => GenerateSight(),
        "chemesthesis" => GenerateChemesthesis(),
        "phantasia" => GeneratePhantasia(),
        _ => throw new KeyNotFoundException($"Unknown battleground path: {pathKey}"),
    };
}
