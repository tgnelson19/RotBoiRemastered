using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>The authored purpose of a procedurally placed room in Path mode.</summary>
public enum PathRoomType
{
    Start,
    Skirmish,
    Assault,
    Elite,
    Challenge,
    Treasure,
    Boss,
}

public enum PathSecretClueKind
{
    EchoRune,
    PressurePlate,
    LensAlignment,
    CleansingMark,
    TruthGlyph,
}

/// <summary>
/// Sense-authored collision silhouettes for the centered boss arena. Each
/// sense owns two layouts so repeated floors change cover without changing
/// the arena's central spawn, controller-friendly cardinal lanes, or open
/// perimeter.
/// </summary>
public enum PathBossArenaVariant
{
    ResonancePylons,
    EchoGates,
    PressureValves,
    DrainageBanks,
    RefractionFins,
    LensIslands,
    QuarantinePods,
    CinderVents,
    DreamPrisms,
    FalseStars,
}

/// <summary>
/// Collision-space validation for controller traversal and future local
/// cooperative spacing. The current game remains single-player; the
/// two-player result proves the room has multiple simultaneous safe pockets
/// rather than reserving every open footprint for one actor.
/// </summary>
public sealed record PathBossArenaSafetyReport(
    int OpenTiles,
    int ConnectedOpenTiles,
    int CardinalLanes,
    int SafePocketCount,
    bool CenterClear,
    bool SupportsControllerTraversal,
    bool SupportsTwoPlayerSpacing);

/// <summary>
/// One room in a generated Path floor. Runtime activation/clear flags live on
/// the room because a PathFloorLayout belongs to exactly one live floor.
/// </summary>
public sealed class PathRoom
{
    public int Id { get; }
    public PathRoomType Type { get; }
    public Rectangle TileBounds { get; }
    public PathRoomShape Shape { get; }
    public int Variant { get; }
    public bool IsMainPath { get; }
    public int Depth { get; }
    public string ThemeKey { get; }
    public List<Rectangle> DoorWorldRects { get; } = new();
    public bool IsActivated { get; set; }
    public bool IsCleared { get; set; }
    public bool IsSecret => Type == PathRoomType.Treasure;
    public bool IsRevealed { get; private set; }

    public string EncounterKey => $"path-room-{Id}";
    public bool IsCombatRoom => Type is PathRoomType.Skirmish
        or PathRoomType.Assault
        or PathRoomType.Elite
        or PathRoomType.Challenge
        or PathRoomType.Treasure;
    public string ShapeDisplayName => Shape switch
    {
        PathRoomShape.LongHall => "Long Hall",
        PathRoomShape.GrandArena => "Grand Arena",
        PathRoomShape.Crossroads => "Crossroads",
        _ => Shape.ToString(),
    };
    public string DungeonDisplayName => (ThemeKey, Shape) switch
    {
        ("touch", PathRoomShape.Sanctuary) => "Drylock Gate",
        ("touch", PathRoomShape.Chamber) => "Sump Chamber",
        ("touch", PathRoomShape.LongHall) => "Pressure Conduit",
        ("touch", PathRoomShape.GrandArena) => "Flood Cistern",
        ("touch", PathRoomShape.Maze) => "Pipeworks",
        ("touch", PathRoomShape.Crossroads) => "Valve Junction",
        ("touch", PathRoomShape.Diamond) => "Drain Seal",
        ("touch", PathRoomShape.Ring) => "Overflow Loop",
        ("touch", PathRoomShape.Ruin) => "Collapsed Sewer",

        ("sight", PathRoomShape.Sanctuary) => "Lens Gate",
        ("sight", PathRoomShape.Chamber) => "Drowned Gallery",
        ("sight", PathRoomShape.LongHall) => "Refraction Walk",
        ("sight", PathRoomShape.GrandArena) => "Tidal Rotunda",
        ("sight", PathRoomShape.Maze) => "Sunken Archive",
        ("sight", PathRoomShape.Crossroads) => "Caustic Crossing",
        ("sight", PathRoomShape.Diamond) => "Prism Vault",
        ("sight", PathRoomShape.Ring) => "Mirror Basin",
        ("sight", PathRoomShape.Ruin) => "Shattered Observatory",

        ("sound", PathRoomShape.Sanctuary) => "Quiet Gate",
        ("sound", PathRoomShape.Chamber) => "Echo Chamber",
        ("sound", PathRoomShape.LongHall) => "Resonance Hall",
        ("sound", PathRoomShape.GrandArena) => "Storm Amphitheater",
        ("sound", PathRoomShape.Maze) => "Whisper Maze",
        ("sound", PathRoomShape.Crossroads) => "Chime Junction",
        ("sound", PathRoomShape.Diamond) => "Thunder Seal",
        ("sound", PathRoomShape.Ring) => "Resonance Circuit",
        ("sound", PathRoomShape.Ruin) => "Broken Belfry",

        ("phantasia", PathRoomShape.Sanctuary) => "Dream Gate",
        ("phantasia", PathRoomShape.Chamber) => "Astral Cell",
        ("phantasia", PathRoomShape.LongHall) => "Starwalk",
        ("phantasia", PathRoomShape.GrandArena) => "Void Court",
        ("phantasia", PathRoomShape.Maze) => "Impossible Maze",
        ("phantasia", PathRoomShape.Crossroads) => "Crossed Reverie",
        ("phantasia", PathRoomShape.Diamond) => "Prism Reliquary",
        ("phantasia", PathRoomShape.Ring) => "Orbit Chapel",
        ("phantasia", PathRoomShape.Ruin) => "Fallen Constellation",

        ("chemesthesis", PathRoomShape.Sanctuary) => "Quarantine Gate",
        ("chemesthesis", PathRoomShape.Chamber) => "Cinder Vault",
        ("chemesthesis", PathRoomShape.LongHall) => "Rupture Hall",
        ("chemesthesis", PathRoomShape.GrandArena) => "Furnace Court",
        ("chemesthesis", PathRoomShape.Maze) => "Blight Maze",
        ("chemesthesis", PathRoomShape.Crossroads) => "Scorched Crossing",
        ("chemesthesis", PathRoomShape.Diamond) => "Cleansing Seal",
        ("chemesthesis", PathRoomShape.Ring) => "Ashen Circuit",
        ("chemesthesis", PathRoomShape.Ruin) => "Rotting Ruin",
        _ => ShapeDisplayName,
    };
    public string EntryBanner =>
        $"{Type.ToString().ToUpperInvariant()} {Depth:00} // {DungeonDisplayName.ToUpperInvariant()}";
    public Rectangle InteriorTileBounds => new(
        TileBounds.X + 1, TileBounds.Y + 1,
        Math.Max(1, TileBounds.Width - 2), Math.Max(1, TileBounds.Height - 2));
    public Rectangle WorldBounds => new(
        TileBounds.X * Battleground.TileSize,
        TileBounds.Y * Battleground.TileSize,
        TileBounds.Width * Battleground.TileSize,
        TileBounds.Height * Battleground.TileSize);
    public Vector2 WorldCenter => new(
        (TileBounds.Center.X + .5f) * Battleground.TileSize,
        (TileBounds.Center.Y + .5f) * Battleground.TileSize);

    public PathRoom(
        int id,
        PathRoomType type,
        Rectangle tileBounds,
        bool isMainPath,
        int depth,
        PathRoomShape shape = PathRoomShape.Chamber,
        int variant = 0,
        string themeKey = "sound")
    {
        Id = id;
        Type = type;
        TileBounds = tileBounds;
        Shape = shape;
        Variant = variant;
        IsMainPath = isMainPath;
        Depth = depth;
        ThemeKey = themeKey;
        IsActivated = type == PathRoomType.Start;
        IsCleared = type == PathRoomType.Start;
        IsRevealed = type != PathRoomType.Treasure;
    }

    public void Reveal() => IsRevealed = true;

    public bool ContainsWorld(Vector2 worldPosition)
    {
        int tileX = (int)Math.Floor(worldPosition.X / Battleground.TileSize);
        int tileY = (int)Math.Floor(worldPosition.Y / Battleground.TileSize);
        return ContainsInteriorTile(tileX, tileY);
    }

    /// <summary>
    /// Shape-space membership used consistently by carving, encounter
    /// activation, decoration placement, and movement locks.
    /// </summary>
    public bool ContainsInteriorTile(int tileX, int tileY)
    {
        if (!TileBounds.Contains(tileX, tileY))
            return false;
        int dx = Math.Abs(tileX - TileBounds.Center.X);
        int dy = Math.Abs(tileY - TileBounds.Center.Y);
        int radiusX = Math.Max(1, TileBounds.Width / 2 - 1);
        int radiusY = Math.Max(1, TileBounds.Height / 2 - 1);
        bool rectangularInterior = tileX > TileBounds.Left && tileX < TileBounds.Right - 1
            && tileY > TileBounds.Top && tileY < TileBounds.Bottom - 1;

        return Shape switch
        {
            PathRoomShape.GrandArena or PathRoomShape.Ring =>
                dx * dx / (double)(radiusX * radiusX)
                + dy * dy / (double)(radiusY * radiusY) <= 1.0,
            PathRoomShape.Diamond =>
                dx / (double)radiusX + dy / (double)radiusY <= 1.0,
            PathRoomShape.Crossroads =>
                rectangularInterior
                && (dx <= Math.Max(2, radiusX / 3)
                    || dy <= Math.Max(2, radiusY / 3)
                    || dx + dy <= Math.Min(radiusX, radiusY)),
            PathRoomShape.Sanctuary =>
                rectangularInterior && dx + dy <= radiusX + radiusY - 2,
            PathRoomShape.Ruin =>
                rectangularInterior
                && dx + dy <= radiusX + radiusY - 1
                && !((tileX * 13 + tileY * 7 + Variant * 5) % 31 == 0
                    && dx > radiusX / 2 && dy > radiusY / 2),
            _ => rectangularInterior,
        };
    }
}

public enum PathCorridorStyle
{
    SewerConduit,
    TidalCauseway,
    CloudBridge,
    Starwalk,
    Rupture,
}

public sealed record PathConnection(
    int FromRoomId,
    int ToRoomId,
    PathCorridorStyle Style = PathCorridorStyle.SewerConduit,
    int Width = 3,
    IReadOnlyList<Point>? Route = null,
    bool Hidden = false,
    PathSecretClueKind? ClueKind = null,
    Point? ClueTile = null,
    IReadOnlyList<Point>? SealTiles = null)
{
    public bool IsRevealed { get; private set; } = !Hidden;
    public void Reveal() => IsRevealed = true;
}

/// <summary>
/// A generated dungeon floor plus the semantic room graph used by Path mode
/// for encounter activation, treasure placement, navigation, and gates.
/// </summary>
public sealed class PathFloorLayout
{
    public Battleground Battleground { get; }
    public IReadOnlyList<PathRoom> Rooms { get; }
    public IReadOnlyList<PathConnection> Connections { get; }
    public PathLayoutStyle Style { get; }
    public PathBossArenaVariant BossArenaVariant { get; }
    public PathRoom StartRoom { get; }
    public PathRoom BossRoom { get; }
    public IReadOnlyList<PathRoom> TreasureRooms { get; }
    public IReadOnlyList<PathRoom> MainRouteRooms { get; }
    public IReadOnlyList<PathRoom> RequiredRoomsBeforeBoss { get; }
    public IReadOnlyList<PathDecoration> Decorations => Battleground.PathDecorations;

    public PathFloorLayout(Battleground battleground, IReadOnlyList<PathRoom> rooms,
        IReadOnlyList<PathConnection> connections,
        PathLayoutStyle style = PathLayoutStyle.Switchback,
        PathBossArenaVariant bossArenaVariant = PathBossArenaVariant.ResonancePylons)
    {
        Battleground = battleground;
        Rooms = rooms;
        Connections = connections;
        Style = style;
        BossArenaVariant = bossArenaVariant;
        PathRoom? startRoom = null;
        PathRoom? bossRoom = null;
        var treasureRooms = new List<PathRoom>();
        for (int index = 0; index < rooms.Count; index++)
        {
            PathRoom room = rooms[index];
            if (room.Type == PathRoomType.Start)
                startRoom = room;
            else if (room.Type == PathRoomType.Boss)
                bossRoom = room;
            else if (room.Type == PathRoomType.Treasure)
                treasureRooms.Add(room);
        }
        StartRoom = startRoom
            ?? throw new ArgumentException("Path layouts require a start room.", nameof(rooms));
        BossRoom = bossRoom
            ?? throw new ArgumentException("Path layouts require a boss room.", nameof(rooms));
        TreasureRooms = treasureRooms.ToArray();
        MainRouteRooms = rooms
            .Where(room => room.IsMainPath)
            .OrderBy(room => room.Depth)
            .ToArray();
        RequiredRoomsBeforeBoss = MainRouteRooms
            .Where(room => room.Type is not (PathRoomType.Start or PathRoomType.Boss))
            .ToArray();
        if (RequiredRoomsBeforeBoss.Count < PathFloorBlueprints.RequiredPreBossRoomCount)
        {
            throw new ArgumentException(
                $"Path layouts require at least {PathFloorBlueprints.RequiredPreBossRoomCount} "
                + "complete rooms before the boss.",
                nameof(rooms));
        }
    }

    public PathRoom? RoomAt(Vector2 worldPosition)
    {
        for (int index = 0; index < Rooms.Count; index++)
        {
            PathRoom room = Rooms[index];
            if (room.IsRevealed && room.ContainsWorld(worldPosition))
                return room;
        }
        return null;
    }

    public bool TryRevealTreasure(Vector2 playerWorldCenter, float radius)
    {
        float radiusSquared = radius * radius;
        for (int index = 0; index < Connections.Count; index++)
        {
            PathConnection connection = Connections[index];
            if (!connection.Hidden || connection.IsRevealed
                || connection.ClueTile is not Point clue)
            {
                continue;
            }
            Vector2 clueWorld = new(
                (clue.X + .5f) * Battleground.TileSize,
                (clue.Y + .5f) * Battleground.TileSize);
            if (Vector2.DistanceSquared(playerWorldCenter, clueWorld) > radiusSquared)
                continue;

            connection.Reveal();
            Rooms.First(room => room.Id == connection.ToRoomId).Reveal();
            if (connection.SealTiles is not null)
            {
                foreach (Point tile in connection.SealTiles)
                    Battleground.SetTile(tile.X, tile.Y, TileType.Road);
            }
            return true;
        }
        return false;
    }

    /// <summary>Finds a random open footprint inside a specific room, never elsewhere on the floor.</summary>
    public Rectangle FindSpawnRect(PathRoom room, int size, Random? rng = null)
    {
        rng ??= Random.Shared;
        var interior = room.InteriorTileBounds;
        for (int attempt = 0; attempt < 48; attempt++)
        {
            int tileX = rng.Next(interior.Left, Math.Max(interior.Left + 1, interior.Right));
            int tileY = rng.Next(interior.Top, Math.Max(interior.Top + 1, interior.Bottom));
            var candidate = new Rectangle(
                tileX * Battleground.TileSize + (Battleground.TileSize - size) / 2,
                tileY * Battleground.TileSize + (Battleground.TileSize - size) / 2,
                size, size);
            if (!Battleground.RectHitsWall(candidate))
                return candidate;
        }

        var centered = new Rectangle(
            (int)(room.WorldCenter.X - size / 2f),
            (int)(room.WorldCenter.Y - size / 2f),
            size, size);
        return Battleground.FindNearestOpenRect(centered);
    }

    /// <summary>
    /// Places an encounter as a composition rather than a bag of random
    /// points: halls form opposing banks, arenas form rings, and crossroads
    /// occupy separate arms. Wall checks keep maze/ring obstructions honest.
    /// </summary>
    public Rectangle FindEncounterSpawnRect(
        PathRoom room, int size, int index, int count, Random? rng = null)
    {
        rng ??= Random.Shared;
        Rectangle inner = room.InteriorTileBounds;
        float fraction = (index + .5f) / Math.Max(1, count);
        Point designed = inner.Center;
        switch (room.Shape)
        {
            case PathRoomShape.LongHall:
                if (inner.Width >= inner.Height)
                {
                    designed.X = inner.Left + 2 + (int)((inner.Width - 5) * fraction);
                    designed.Y += (index % 2 == 0 ? -1 : 1) * Math.Max(1, inner.Height / 4);
                }
                else
                {
                    designed.Y = inner.Top + 2 + (int)((inner.Height - 5) * fraction);
                    designed.X += (index % 2 == 0 ? -1 : 1) * Math.Max(1, inner.Width / 4);
                }
                break;

            case PathRoomShape.GrandArena:
            case PathRoomShape.Ring:
                float angle = MathF.Tau * fraction + room.Variant * .37f;
                designed.X += (int)(MathF.Cos(angle) * inner.Width * .31f);
                designed.Y += (int)(MathF.Sin(angle) * inner.Height * .31f);
                break;

            case PathRoomShape.Crossroads:
                Point[] arms =
                [
                    new(inner.Center.X + inner.Width / 3, inner.Center.Y),
                    new(inner.Center.X, inner.Center.Y + inner.Height / 3),
                    new(inner.Center.X - inner.Width / 3, inner.Center.Y),
                    new(inner.Center.X, inner.Center.Y - inner.Height / 3),
                ];
                designed = arms[index % arms.Length];
                break;

            case PathRoomShape.Diamond:
            case PathRoomShape.Ruin:
                float diamondAngle = MathF.Tau * fraction + MathF.PI / 4f;
                designed.X += (int)(MathF.Cos(diamondAngle) * inner.Width * .24f);
                designed.Y += (int)(MathF.Sin(diamondAngle) * inner.Height * .24f);
                break;
        }

        // Preserve the room-shape composition while varying the exact pocket
        // occupied on each run. Encounters stay distributed instead of
        // resolving into a visibly repeated formation at the threshold.
        designed.X += rng.Next(-Math.Max(1, inner.Width / 10),
            Math.Max(1, inner.Width / 10) + 1);
        designed.Y += rng.Next(-Math.Max(1, inner.Height / 10),
            Math.Max(1, inner.Height / 10) + 1);

        for (int attempt = 0; attempt < 24; attempt++)
        {
            int jitter = attempt == 0 ? 0 : 1 + attempt / 6;
            int tileX = designed.X + (jitter == 0 ? 0 : rng.Next(-jitter, jitter + 1));
            int tileY = designed.Y + (jitter == 0 ? 0 : rng.Next(-jitter, jitter + 1));
            if (!room.ContainsInteriorTile(tileX, tileY))
                continue;
            var candidate = new Rectangle(
                tileX * Battleground.TileSize + (Battleground.TileSize - size) / 2,
                tileY * Battleground.TileSize + (Battleground.TileSize - size) / 2,
                size, size);
            if (!Battleground.RectHitsWall(candidate))
                return candidate;
        }
        return FindSpawnRect(room, size, rng);
    }
}

/// <summary>
/// Builds a readable eastbound main route whose boss room is at the map's
/// true center (required by the existing sense bosses), with one or two
/// north/south treasure branches. All art is expressed in the same hard-edged
/// tile vocabulary as the existing 2.5D arenas.
/// </summary>
public static class PathFloorGenerator
{
    public const int Width = 141;
    public const int Height = 81;

    /// <summary>
    /// Chained treasure roll: each success creates a room and authorizes one
    /// more 50% roll, capped at three rooms.
    /// </summary>
    public static int RollTreasureRoomCount(Random rng)
    {
        int count = 0;
        while (count < 3 && rng.NextDouble() < .5)
            count++;
        return count;
    }

    public static PathFloorLayout Generate(
        string senseKey,
        int floorNumber,
        Random? rng = null,
        bool? containsTreasureArena = null)
    {
        if (!GamePaths.PathsByKey.ContainsKey(senseKey))
            throw new KeyNotFoundException($"Unknown Path sense: {senseKey}");
        if (floorNumber is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(floorNumber), "Path floors are numbered 1 through 10.");

        rng ??= Random.Shared;
        var tiles = new TileType[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                tiles[y, x] = TileType.OuterVoid;

        PathLayoutStyle style = ChooseLayoutStyle(senseKey, floorNumber, rng);
        var rooms = PathFloorBlueprints.Create(
                style,
                floorNumber,
                rng,
                containsTreasureArena.HasValue
                    ? containsTreasureArena.Value ? 1 : 0
                    : null)
            .Select(blueprint => new PathRoom(
                blueprint.Id,
                blueprint.Type,
                blueprint.Bounds,
                blueprint.IsMainPath,
                blueprint.Depth,
                blueprint.Shape,
                blueprint.Variant,
                senseKey))
            .ToList();

        foreach (var room in rooms)
            CarveRoom(tiles, room, senseKey, floorNumber);

        var connections = new List<PathConnection>();
        var mainRooms = rooms.Where(room => room.IsMainPath).OrderBy(room => room.Depth).ToList();
        for (int index = 0; index + 1 < mainRooms.Count; index++)
            Connect(tiles, mainRooms[index], mainRooms[index + 1], connections, senseKey, rng, rooms);
        foreach (var branch in rooms.Where(room => !room.IsMainPath))
        {
            var parent = mainRooms
                .Where(room => room.Type != PathRoomType.Boss)
                .MinBy(room => Vector2.DistanceSquared(room.WorldCenter, branch.WorldCenter))!;
            Connect(tiles, parent, branch, connections, senseKey, rng, rooms);
        }
        PathBossArenaVariant bossArenaVariant =
            ChooseBossArenaVariant(senseKey, floorNumber);
        AddBossArenaObstacles(
            tiles,
            rooms.Single(room => room.Type == PathRoomType.Boss),
            bossArenaVariant);
        SealOpenEdges(tiles);

        var palettes = senseKey switch
        {
            "sound" => BiomePalettes.Sound,
            "touch" => BiomePalettes.Touch,
            "sight" => BiomePalettes.Sight,
            "chemesthesis" => BiomePalettes.Chemesthesis,
            "phantasia" => BiomePalettes.Phantasia,
            _ => BiomePalettes.Sound,
        };
        int wallHeight = senseKey switch
        {
            "touch" => 22,
            "phantasia" => 20,
            _ => 14,
        };
        var start = rooms[0].WorldCenter - new Vector2(Battleground.TileSize * .375f);
        var decorations = PathThemeVisuals.GenerateDecorations(
            senseKey, floorNumber, rooms, rng, tiles, connections);
        var biomeMap = CreateRoomBiomeMap(tiles, rooms, floorNumber);
        var battleground = new Battleground(
            tiles, palettes, wallHeight, start,
            visualThemeKey: senseKey,
            pathFloorNumber: floorNumber,
            pathDecorations: decorations,
            biomeMap: biomeMap);
        return new PathFloorLayout(battleground, rooms, connections, style, bossArenaVariant);
    }

    private static PathBossArenaVariant ChooseBossArenaVariant(
        string senseKey, int floorNumber)
    {
        bool alternate = floorNumber % 2 == 0;
        return senseKey switch
        {
            "sound" => alternate
                ? PathBossArenaVariant.EchoGates
                : PathBossArenaVariant.ResonancePylons,
            "touch" => alternate
                ? PathBossArenaVariant.DrainageBanks
                : PathBossArenaVariant.PressureValves,
            "sight" => alternate
                ? PathBossArenaVariant.LensIslands
                : PathBossArenaVariant.RefractionFins,
            "chemesthesis" => alternate
                ? PathBossArenaVariant.CinderVents
                : PathBossArenaVariant.QuarantinePods,
            "phantasia" => alternate
                ? PathBossArenaVariant.FalseStars
                : PathBossArenaVariant.DreamPrisms,
            _ => PathBossArenaVariant.ResonancePylons,
        };
    }

    private static void AddBossArenaObstacles(
        TileType[,] tiles, PathRoom room, PathBossArenaVariant variant)
    {
        Rectangle inner = room.InteriorTileBounds;
        Point center = inner.Center;

        void Wall(int x, int y)
        {
            if (!room.ContainsInteriorTile(x, y))
                return;
            int dx = x - center.X, dy = y - center.Y;
            // A 9x9 center, three-tile cardinal lanes, and the outer two tiles
            // remain open for every authored boss and analog-stick movement.
            if (Math.Abs(dx) <= 4 && Math.Abs(dy) <= 4
                || Math.Abs(dx) <= 1
                || Math.Abs(dy) <= 1
                || Math.Abs(dx) >= 13
                || Math.Abs(dy) >= 13)
            {
                return;
            }
            if (!tiles[y, x].IsSolid())
                tiles[y, x] = TileType.BuildingWall;
        }

        void Block(int x, int y, int width, int height)
        {
            for (int oy = 0; oy < height; oy++)
                for (int ox = 0; ox < width; ox++)
                    Wall(x + ox, y + oy);
        }

        switch (variant)
        {
            case PathBossArenaVariant.ResonancePylons:
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sy in new[] { -1, 1 })
                    {
                        Block(center.X + sx * 8 - 1, center.Y + sy * 8 - 1, 2, 2);
                        Wall(center.X + sx * 11, center.Y + sy * 5);
                    }
                break;

            case PathBossArenaVariant.EchoGates:
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sy in new[] { -1, 1 })
                    {
                        Block(center.X + sx * 9 - 1, center.Y + sy * 6 - 1, 2, 3);
                        Wall(center.X + sx * 6, center.Y + sy * 10);
                    }
                break;

            case PathBossArenaVariant.PressureValves:
                foreach (int sx in new[] { -1, 1 })
                {
                    Block(center.X + sx * 8 - 1, center.Y - 9, 2, 4);
                    Block(center.X + sx * 8 - 1, center.Y + 6, 2, 4);
                }
                break;

            case PathBossArenaVariant.DrainageBanks:
                foreach (int sy in new[] { -1, 1 })
                {
                    Block(center.X - 10, center.Y + sy * 7 - 1, 4, 2);
                    Block(center.X + 7, center.Y + sy * 7 - 1, 4, 2);
                }
                break;

            case PathBossArenaVariant.RefractionFins:
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sy in new[] { -1, 1 })
                        for (int step = 0; step < 4; step++)
                            Wall(center.X + sx * (6 + step), center.Y + sy * (9 - step));
                break;

            case PathBossArenaVariant.LensIslands:
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sy in new[] { -1, 1 })
                    {
                        Wall(center.X + sx * 8, center.Y + sy * 6);
                        Wall(center.X + sx * 7, center.Y + sy * 7);
                        Wall(center.X + sx * 8, center.Y + sy * 8);
                        Wall(center.X + sx * 9, center.Y + sy * 7);
                    }
                break;

            case PathBossArenaVariant.QuarantinePods:
                for (int index = 0; index < 6; index++)
                {
                    float angle = MathF.PI / 6f + index * MathF.Tau / 6f;
                    int x = center.X + (int)MathF.Round(MathF.Cos(angle) * 9f);
                    int y = center.Y + (int)MathF.Round(MathF.Sin(angle) * 9f);
                    Block(x - 1, y - 1, 2, 2);
                }
                break;

            case PathBossArenaVariant.CinderVents:
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sy in new[] { -1, 1 })
                    {
                        Block(center.X + sx * 7 - 1, center.Y + sy * 10 - 1, 2, 2);
                        Block(center.X + sx * 10 - 1, center.Y + sy * 6 - 1, 2, 2);
                    }
                break;

            case PathBossArenaVariant.DreamPrisms:
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sy in new[] { -1, 1 })
                    {
                        Wall(center.X + sx * 8, center.Y + sy * 6);
                        Wall(center.X + sx * 7, center.Y + sy * 7);
                        Wall(center.X + sx * 8, center.Y + sy * 8);
                        Wall(center.X + sx * 9, center.Y + sy * 7);
                        Wall(center.X + sx * 8, center.Y + sy * 7);
                    }
                break;

            case PathBossArenaVariant.FalseStars:
                for (int index = 0; index < 8; index++)
                {
                    float angle = MathF.PI / 8f + index * MathF.Tau / 8f;
                    int x = center.X + (int)MathF.Round(MathF.Cos(angle) * 10f);
                    int y = center.Y + (int)MathF.Round(MathF.Sin(angle) * 10f);
                    Wall(x, y);
                    if (index % 2 == 0)
                        Wall(x + Math.Sign(center.X - x), y);
                }
                break;
        }
    }

    public static PathBossArenaSafetyReport EvaluateBossArenaSafety(PathFloorLayout layout)
    {
        PathRoom room = layout.BossRoom;
        Battleground battleground = layout.Battleground;
        Rectangle inner = room.InteriorTileBounds;
        Point center = inner.Center;
        var open = new HashSet<Point>();
        for (int y = inner.Top; y < inner.Bottom; y++)
            for (int x = inner.Left; x < inner.Right; x++)
                if (room.ContainsInteriorTile(x, y) && !battleground.TileAt(x, y).IsSolid())
                    open.Add(new Point(x, y));

        var connected = new HashSet<Point>();
        var queue = new Queue<Point>();
        if (open.Contains(center))
        {
            connected.Add(center);
            queue.Enqueue(center);
        }
        ReadOnlySpan<Point> steps = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
        while (queue.Count > 0)
        {
            Point point = queue.Dequeue();
            foreach (Point step in steps)
            {
                var next = new Point(point.X + step.X, point.Y + step.Y);
                if (open.Contains(next) && connected.Add(next))
                    queue.Enqueue(next);
            }
        }

        bool centerClear = true;
        for (int y = center.Y - 4; y <= center.Y + 4 && centerClear; y++)
            for (int x = center.X - 4; x <= center.X + 4; x++)
                if (!open.Contains(new Point(x, y)))
                {
                    centerClear = false;
                    break;
                }

        int cardinalLanes = 0;
        foreach (Point direction in steps)
        {
            bool clear = true;
            for (int distance = 0; distance <= 11 && clear; distance++)
            {
                for (int width = -1; width <= 1; width++)
                {
                    Point perpendicular = new(-direction.Y, direction.X);
                    var tile = new Point(
                        center.X + direction.X * distance + perpendicular.X * width,
                        center.Y + direction.Y * distance + perpendicular.Y * width);
                    if (!open.Contains(tile))
                    {
                        clear = false;
                        break;
                    }
                }
            }
            if (clear)
                cardinalLanes++;
        }

        var pockets = new List<Point>();
        for (int y = inner.Top + 1; y < inner.Bottom - 1; y += 2)
        {
            for (int x = inner.Left + 1; x < inner.Right - 1; x += 2)
            {
                bool footprintClear = true;
                for (int oy = -1; oy <= 1 && footprintClear; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        if (!open.Contains(new Point(x + ox, y + oy)))
                        {
                            footprintClear = false;
                            break;
                        }
                if (footprintClear
                    && pockets.All(existing =>
                        Math.Abs(existing.X - x) + Math.Abs(existing.Y - y) >= 8))
                {
                    pockets.Add(new Point(x, y));
                }
            }
        }

        bool controllerSafe = centerClear
            && cardinalLanes == 4
            // Circular tile silhouettes can leave at most four one-tile edge
            // notches outside the playable core; analog traversal only needs
            // the connected arena body and its authored lanes.
            && connected.Count >= open.Count - 4;
        return new PathBossArenaSafetyReport(
            open.Count,
            connected.Count,
            cardinalLanes,
            pockets.Count,
            centerClear,
            controllerSafe,
            controllerSafe && pockets.Count >= 4);
    }

    private static PathLayoutStyle ChooseLayoutStyle(string senseKey, int floorNumber, Random rng)
    {
        PathLayoutStyle[] choices = senseKey switch
        {
            "touch" =>
                [PathLayoutStyle.Procession, PathLayoutStyle.Switchback, PathLayoutStyle.Floodplain],
            "sight" =>
                [PathLayoutStyle.GrandCircuit, PathLayoutStyle.Floodplain, PathLayoutStyle.Switchback],
            "sound" =>
                [PathLayoutStyle.Switchback, PathLayoutStyle.GrandCircuit, PathLayoutStyle.Procession],
            "phantasia" =>
                [PathLayoutStyle.GrandCircuit, PathLayoutStyle.Procession, PathLayoutStyle.Switchback],
            "chemesthesis" =>
                [PathLayoutStyle.Floodplain, PathLayoutStyle.Switchback, PathLayoutStyle.GrandCircuit],
            _ => Enum.GetValues<PathLayoutStyle>(),
        };
        // Later floors lean toward the less linear option in each grammar
        // without making the sequence deterministic.
        int index = (rng.Next(choices.Length) + (floorNumber > 5 ? 1 : 0)) % choices.Length;
        return choices[index];
    }

    private static void CarveRoom(TileType[,] tiles, PathRoom room, string senseKey, int floorNumber)
    {
        Rectangle bounds = room.TileBounds;
        TileType baseFloor = senseKey switch
        {
            "touch" or "phantasia" => TileType.BuildingFloor,
            "sound" when room.Type is PathRoomType.Start or PathRoomType.Treasure => TileType.BuildingFloor,
            _ when room.Type is PathRoomType.Start or PathRoomType.Treasure => TileType.BuildingFloor,
            _ => TileType.Default,
        };
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                if (room.ContainsInteriorTile(x, y))
                    tiles[y, x] = baseFloor;
            }
        }
        SealOpenEdges(tiles, bounds);

        int centerX = bounds.Center.X, centerY = bounds.Center.Y;
        int radius = room.Type switch
        {
            PathRoomType.Start => 3,
            PathRoomType.Skirmish => 2,
            PathRoomType.Assault => 3,
            PathRoomType.Elite => 4,
            PathRoomType.Treasure => 2,
            PathRoomType.Boss => 8,
            _ => 2,
        };

        void PaintOpen(int x, int y, TileType type)
        {
            if (x > bounds.Left && x < bounds.Right - 1
                && y > bounds.Top && y < bounds.Bottom - 1
                && !tiles[y, x].IsSolid())
            {
                tiles[y, x] = type;
            }
        }

        // Every room preserves an unobstructed center cross while its material
        // pattern gives the sense a silhouette visible even at maximum zoom.
        for (int offset = -radius; offset <= radius; offset++)
        {
            PaintOpen(centerX + offset, centerY, TileType.Road);
            PaintOpen(centerX, centerY + offset, TileType.Road);
        }

        switch (senseKey)
        {
            case "touch":
                // Twin drainage channels wrap a dry brick service platform.
                for (int x = bounds.Left + 1; x < bounds.Right - 1; x++)
                {
                    PaintOpen(x, centerY - Math.Min(3, radius), TileType.Road);
                    PaintOpen(x, centerY + Math.Min(3, radius), TileType.Road);
                }
                break;

            case "sight":
                // Concentric shallow water lanes with a stable central island.
                int waterRadius = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 2 - 3);
                for (int y = bounds.Top + 1; y < bounds.Bottom - 1; y++)
                {
                    for (int x = bounds.Left + 1; x < bounds.Right - 1; x++)
                    {
                        int distance = Math.Max(Math.Abs(x - centerX), Math.Abs(y - centerY));
                        if (distance == waterRadius || (room.Type == PathRoomType.Boss && distance == waterRadius - 4))
                            PaintOpen(x, y, TileType.Road);
                    }
                }
                break;

            case "sound":
                // Air-current lanes taper toward the central resonance pad.
                for (int offset = -radius; offset <= radius; offset++)
                {
                    PaintOpen(centerX + offset, centerY + offset / 2, TileType.Road);
                    PaintOpen(centerX + offset, centerY - offset / 2, TileType.Road);
                }
                break;

            case "phantasia":
                // A skewed constellation breaks the otherwise black floor.
                for (int offset = -radius; offset <= radius; offset++)
                {
                    PaintOpen(centerX + offset, centerY + offset, TileType.Road);
                    if ((offset + room.Id) % 2 == 0)
                        PaintOpen(centerX + offset, centerY - offset, TileType.Road);
                }
                break;

            case "chemesthesis":
                // Deterministic fault lines get denser after the midpoint.
                int fractureStep = floorNumber > 5 ? 2 : 3;
                for (int x = bounds.Left + 2; x < bounds.Right - 2; x++)
                {
                    int jag = ((x * 7 + room.Id * 11) % 5) - 2;
                    if ((x - bounds.Left) % fractureStep == 0)
                        PaintOpen(x, centerY + jag, TileType.Road);
                }
                break;
        }

        if (room.Shape == PathRoomShape.GrandArena)
            PaintThemedGrandArenaFloor(tiles, room, senseKey);
        AddShapeObstacles(tiles, room);
    }

    /// <summary>
    /// Repaints only walkable material IDs, never collision, so Grand Arenas
    /// get a bold sense-specific floor plan without changing encounter space.
    /// The semantic decorations added by PathThemeVisuals sit on these larger
    /// rings, stages, and ritual fields instead of floating over a generic
    /// room treatment.
    /// </summary>
    private static void PaintThemedGrandArenaFloor(
        TileType[,] tiles,
        PathRoom room,
        string senseKey)
    {
        Rectangle inner = room.InteriorTileBounds;
        int centerX = inner.Center.X;
        int centerY = inner.Center.Y;
        int maxRadius = Math.Max(5, Math.Min(inner.Width, inner.Height) / 2 - 2);

        void Floor(int x, int y, TileType type)
        {
            if (room.ContainsInteriorTile(x, y) && !tiles[y, x].IsSolid())
                tiles[y, x] = type;
        }

        for (int y = inner.Top + 1; y < inner.Bottom - 1; y++)
        {
            for (int x = inner.Left + 1; x < inner.Right - 1; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                int ax = Math.Abs(dx);
                int ay = Math.Abs(dy);
                int squareRadius = Math.Max(ax, ay);
                int diamondRadius = ax + ay;
                double circleRadius = Math.Sqrt(dx * dx + dy * dy);

                switch (senseKey)
                {
                    case "touch":
                        // A dry brick service stage contained by two square
                        // drainage races and short cardinal overflow cuts.
                        if (squareRadius <= 3)
                            Floor(x, y, TileType.BuildingFloor);
                        if (squareRadius == 5
                            || squareRadius == Math.Min(10, maxRadius - 1)
                            || (squareRadius > 3 && (ax <= 1 || ay <= 1)))
                        {
                            Floor(x, y, TileType.Road);
                        }
                        break;

                    case "sight":
                        // Concentric optical basins and a diamond lens island.
                        if (diamondRadius <= 5)
                            Floor(x, y, TileType.BuildingFloor);
                        if (Math.Abs(circleRadius - 5.5) < .7
                            || Math.Abs(circleRadius - Math.Min(10.5, maxRadius - 1)) < .7)
                        {
                            Floor(x, y, TileType.Road);
                        }
                        break;

                    case "sound":
                        // Two diamond wave fronts cross a rectilinear stage;
                        // broken beats on alternating quadrants keep it from
                        // reading like Sight's continuous pool rings.
                        if (squareRadius <= 3)
                            Floor(x, y, TileType.BuildingFloor);
                        if (diamondRadius is >= 7 and <= 8
                            || diamondRadius is >= 14 and <= 15
                            || ((dx - dy) % 6 == 0 && squareRadius > 3))
                        {
                            Floor(x, y, TileType.Road);
                        }
                        break;

                    case "phantasia":
                        // A star court: diagonal rays, a dark central dream
                        // plate, and a deliberately incomplete orbital ring.
                        if (squareRadius <= 2)
                            Floor(x, y, TileType.BuildingFloor);
                        if (ax == ay
                            || (Math.Abs(circleRadius - Math.Min(9.5, maxRadius - 1)) < .65
                                && (x + y + room.Variant) % 5 != 0))
                        {
                            Floor(x, y, TileType.Road);
                        }
                        break;

                    case "chemesthesis":
                        // Layered cinder plates are split by asymmetric fault
                        // lines and an outer quarantine scar.
                        if (squareRadius <= 4 && (x + y) % 2 == 0)
                            Floor(x, y, TileType.BuildingFloor);
                        if ((dx * 3 + dy * 5 + room.Variant) % 11 == 0
                            || squareRadius == Math.Min(10, maxRadius - 1))
                        {
                            Floor(x, y, TileType.Road);
                        }
                        break;
                }
            }
        }

        // Reassert the universal open aiming/movement cross as the strongest
        // line in the material plan after every theme-specific repaint.
        int crossRadius = Math.Min(5, maxRadius - 1);
        for (int offset = -crossRadius; offset <= crossRadius; offset++)
        {
            Floor(centerX + offset, centerY, TileType.Road);
            Floor(centerX, centerY + offset, TileType.Road);
        }
    }

    private static void AddShapeObstacles(TileType[,] tiles, PathRoom room)
    {
        Rectangle inner = room.InteriorTileBounds;
        int centerX = inner.Center.X, centerY = inner.Center.Y;
        void AddPillar(int offsetX, int offsetY)
        {
            int x = centerX + offsetX;
            int y = centerY + offsetY;
            if (!room.ContainsInteriorTile(x, y)
                || Math.Abs(offsetX) <= 1
                || Math.Abs(offsetY) <= 1
                || tiles[y, x].IsSolid())
            {
                return;
            }
            tiles[y, x] = TileType.BuildingWall;
        }

        if (room.Shape == PathRoomShape.Maze)
        {
            bool vertical = inner.Width >= inner.Height;
            int spanStart = vertical ? inner.Left + 4 : inner.Top + 4;
            int spanEnd = vertical ? inner.Right - 3 : inner.Bottom - 3;
            for (int line = spanStart, index = 0; line < spanEnd; line += 5, index++)
            {
                if (vertical)
                {
                    int gapY = index % 2 == 0 ? inner.Top + 3 : inner.Bottom - 4;
                    for (int y = inner.Top + 1; y < inner.Bottom - 1; y++)
                    {
                        if (Math.Abs(y - gapY) <= 2 || Math.Abs(y - centerY) <= 1)
                            continue;
                        if (!tiles[y, line].IsSolid())
                            tiles[y, line] = TileType.BuildingWall;
                    }
                }
                else
                {
                    int gapX = index % 2 == 0 ? inner.Left + 3 : inner.Right - 4;
                    for (int x = inner.Left + 1; x < inner.Right - 1; x++)
                    {
                        if (Math.Abs(x - gapX) <= 2 || Math.Abs(x - centerX) <= 1)
                            continue;
                        if (!tiles[line, x].IsSolid())
                            tiles[line, x] = TileType.BuildingWall;
                    }
                }
            }
        }
        else if (room.Shape == PathRoomShape.Ring)
        {
            int radiusX = Math.Max(3, inner.Width / 5);
            int radiusY = Math.Max(3, inner.Height / 5);
            for (int y = centerY - radiusY; y <= centerY + radiusY; y++)
            {
                for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
                {
                    double distance = (x - centerX) * (x - centerX) / (double)(radiusX * radiusX)
                        + (y - centerY) * (y - centerY) / (double)(radiusY * radiusY);
                    if (distance is >= .62 and <= 1.15
                        && Math.Abs(x - centerX) > 1 && Math.Abs(y - centerY) > 1
                        && !tiles[y, x].IsSolid())
                    {
                        tiles[y, x] = TileType.BuildingWall;
                    }
                }
            }
        }
        else if (room.Shape is PathRoomShape.Chamber or PathRoomShape.Crossroads)
        {
            // Four compact corner supports make the otherwise rectilinear
            // modules read as actual vaulted dungeon rooms while leaving the
            // cardinal movement/aiming cross untouched.
            AddPillar(-3, -3);
            AddPillar(3, -3);
            AddPillar(-3, 3);
            AddPillar(3, 3);
        }
        else if (room.Shape == PathRoomShape.Diamond)
        {
            AddPillar(-3, -2);
            AddPillar(3, -2);
            AddPillar(-3, 2);
            AddPillar(3, 2);
        }
        else if (room.Shape == PathRoomShape.Ruin)
        {
            Point[] rubble = room.Variant % 2 == 0
                ? [new(-4, -3), new(3, -4), new(4, 2), new(-2, 4)]
                : [new(-3, -4), new(4, -2), new(2, 4), new(-4, 3)];
            foreach (Point point in rubble)
                AddPillar(point.X, point.Y);
        }
        else if (room.Shape == PathRoomShape.LongHall)
        {
            if (inner.Width >= inner.Height)
            {
                AddPillar(-4, -2);
                AddPillar(4, -2);
                AddPillar(-4, 2);
                AddPillar(4, 2);
            }
            else
            {
                AddPillar(-2, -4);
                AddPillar(2, -4);
                AddPillar(-2, 4);
                AddPillar(2, 4);
            }
        }
    }

    private static void SealOpenEdges(TileType[,] tiles, Rectangle? region = null)
    {
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        Rectangle area = region ?? new Rectangle(1, 1, width - 2, height - 2);
        int left = Math.Max(1, area.Left - 1), right = Math.Min(width - 1, area.Right + 1);
        int top = Math.Max(1, area.Top - 1), bottom = Math.Min(height - 1, area.Bottom + 1);
        var walls = new List<Point>();
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (tiles[y, x] != TileType.OuterVoid)
                    continue;
                bool nextToOpen = false;
                for (int oy = -1; oy <= 1 && !nextToOpen; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0)
                            continue;
                        TileType neighbor = tiles[y + oy, x + ox];
                        if (!neighbor.IsSolid())
                        {
                            nextToOpen = true;
                            break;
                        }
                    }
                }
                if (nextToOpen)
                    walls.Add(new Point(x, y));
            }
        }
        foreach (var wall in walls)
            tiles[wall.Y, wall.X] = TileType.BuildingWall;
    }

    private static void Connect(
        TileType[,] tiles,
        PathRoom from,
        PathRoom to,
        List<PathConnection> connections,
        string senseKey,
        Random rng,
        IReadOnlyList<PathRoom> allRooms)
    {
        Point start = from.TileBounds.Center;
        Point end = to.TileBounds.Center;
        PathCorridorStyle style = senseKey switch
        {
            "touch" => PathCorridorStyle.SewerConduit,
            "sight" => PathCorridorStyle.TidalCauseway,
            "sound" => PathCorridorStyle.CloudBridge,
            "phantasia" => PathCorridorStyle.Starwalk,
            _ => PathCorridorStyle.Rupture,
        };
        int width = style switch
        {
            PathCorridorStyle.TidalCauseway or PathCorridorStyle.Rupture => 5,
            _ => 3,
        };

        int midX = Math.Clamp((start.X + end.X) / 2, 2, Width - 3);
        int midY = Math.Clamp((start.Y + end.Y) / 2, 2, Height - 3);
        int leftDetour = Math.Clamp(Math.Min(start.X, end.X) - 12, 2, Width - 3);
        int rightDetour = Math.Clamp(Math.Max(start.X, end.X) + 12, 2, Width - 3);
        int topDetour = Math.Clamp(Math.Min(start.Y, end.Y) - 12, 2, Height - 3);
        int bottomDetour = Math.Clamp(Math.Max(start.Y, end.Y) + 12, 2, Height - 3);
        var unrelated = allRooms.Where(room => room.Id != from.Id && room.Id != to.Id).ToList();
        var occupiedCorridorTiles = new HashSet<Point>();
        for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
        {
            IReadOnlyList<Point>? existingRoute = connections[connectionIndex].Route;
            if (existingRoute is null)
                continue;
            for (int pointIndex = 0; pointIndex < existingRoute.Count; pointIndex++)
                occupiedCorridorTiles.Add(existingRoute[pointIndex]);
        }
        var candidates = new List<List<Point>>
        {
            BuildOrthogonalRoute(start, new Point(end.X, start.Y), end),
            BuildOrthogonalRoute(start, new Point(start.X, end.Y), end),
            BuildOrthogonalRoute(start, new Point(midX, start.Y), new Point(midX, end.Y), end),
            BuildOrthogonalRoute(start, new Point(start.X, midY), new Point(end.X, midY), end),
            BuildOrthogonalRoute(start, new Point(leftDetour, start.Y), new Point(leftDetour, end.Y), end),
            BuildOrthogonalRoute(start, new Point(rightDetour, start.Y), new Point(rightDetour, end.Y), end),
            BuildOrthogonalRoute(start, new Point(start.X, topDetour), new Point(end.X, topDetour), end),
            BuildOrthogonalRoute(start, new Point(start.X, bottomDetour), new Point(end.X, bottomDetour), end),
        };
        foreach (var room in unrelated)
        {
            int aroundLeft = Math.Clamp(room.TileBounds.Left - 3, 2, Width - 3);
            int aroundRight = Math.Clamp(room.TileBounds.Right + 2, 2, Width - 3);
            int aroundTop = Math.Clamp(room.TileBounds.Top - 3, 2, Height - 3);
            int aroundBottom = Math.Clamp(room.TileBounds.Bottom + 2, 2, Height - 3);
            candidates.Add(BuildOrthogonalRoute(
                start, new Point(aroundLeft, start.Y), new Point(aroundLeft, end.Y), end));
            candidates.Add(BuildOrthogonalRoute(
                start, new Point(aroundRight, start.Y), new Point(aroundRight, end.Y), end));
            candidates.Add(BuildOrthogonalRoute(
                start, new Point(start.X, aroundTop), new Point(end.X, aroundTop), end));
            candidates.Add(BuildOrthogonalRoute(
                start, new Point(start.X, aroundBottom), new Point(end.X, aroundBottom), end));
        }
        var route = candidates
            .OrderBy(candidate => CorridorRouteScore(
                    candidate,
                    unrelated,
                    occupiedCorridorTiles,
                    from,
                    to,
                    tiles)
                + rng.NextDouble() * .25)
            .First();
        if (RouteViolatesRoomGraph(
                route,
                unrelated,
                occupiedCorridorTiles,
                from,
                to))
        {
            route = BuildAvoidanceRoute(
                    start,
                    end,
                    unrelated,
                    occupiedCorridorTiles,
                    from,
                    to,
                    width,
                    rng)
                ?? route;
        }
        CarveCorridorRoute(tiles, route, width, style);
        bool hidden = to.Type == PathRoomType.Treasure;
        Point? clueTile = null;
        List<Point>? sealTiles = null;
        if (hidden)
        {
            int sealIndex = route.FindIndex(point => !from.TileBounds.Contains(point));
            sealIndex = Math.Clamp(sealIndex, 1, route.Count - 2);
            clueTile = route[Math.Max(0, sealIndex - 2)];
            Point before = route[sealIndex - 1];
            Point after = route[sealIndex + 1];
            Point direction = new(
                Math.Sign(after.X - before.X),
                Math.Sign(after.Y - before.Y));
            Point perpendicular = new(-direction.Y, direction.X);
            sealTiles = new List<Point>();
            for (int offset = -width / 2; offset <= width / 2; offset++)
            {
                Point tile = route[sealIndex] + perpendicular * offset;
                if (tile.X <= 0 || tile.X >= Width - 1
                    || tile.Y <= 0 || tile.Y >= Height - 1)
                {
                    continue;
                }
                tiles[tile.Y, tile.X] = TileType.BuildingWall;
                sealTiles.Add(tile);
            }
        }
        PathSecretClueKind? clueKind = hidden
            ? senseKey switch
            {
                "touch" => PathSecretClueKind.PressurePlate,
                "sight" => PathSecretClueKind.LensAlignment,
                "chemesthesis" => PathSecretClueKind.CleansingMark,
                "phantasia" => PathSecretClueKind.TruthGlyph,
                _ => PathSecretClueKind.EchoRune,
            }
            : null;
        connections.Add(new PathConnection(
            from.Id,
            to.Id,
            style,
            width,
            route,
            hidden,
            clueKind,
            clueTile,
            sealTiles));

        Point fromDirection = route.FirstOrDefault(point => point != start, end);
        Point toDirection = route.AsEnumerable().Reverse().FirstOrDefault(point => point != end, start);
        AddDoor(from, fromDirection - start);
        AddDoor(to, toDirection - end);
    }

    private static List<Point> BuildOrthogonalRoute(params Point[] waypoints)
    {
        var route = new List<Point>();
        for (int segment = 0; segment + 1 < waypoints.Length; segment++)
        {
            Point start = waypoints[segment], end = waypoints[segment + 1];
            int steps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
            for (int step = 0; step <= steps; step++)
            {
                int x = steps == 0
                    ? start.X
                    : (int)Math.Round(start.X + (end.X - start.X) * step / (double)steps);
                int y = steps == 0
                    ? start.Y
                    : (int)Math.Round(start.Y + (end.Y - start.Y) * step / (double)steps);
                var point = new Point(x, y);
                if (route.Count == 0 || route[^1] != point)
                    route.Add(point);
            }
        }
        return route;
    }

    private static double CorridorRouteScore(
        IReadOnlyList<Point> route,
        IReadOnlyList<PathRoom> unrelatedRooms,
        IReadOnlySet<Point> occupiedCorridorTiles,
        PathRoom from,
        PathRoom to,
        TileType[,] tiles)
    {
        double score = route.Count * .05;
        foreach (Point point in route)
        {
            foreach (var room in unrelatedRooms)
            {
                var avoidance = room.TileBounds;
                avoidance.Inflate(1, 1);
                if (avoidance.Contains(point))
                    score += 120;
            }
            if (occupiedCorridorTiles.Contains(point)
                && !from.TileBounds.Contains(point)
                && !to.TileBounds.Contains(point))
            {
                // Crossing two independently authored passages makes a
                // physical shortcut that no longer matches the serial room
                // graph. A large but finite cost still lets generation
                // recover if every candidate shares a one-tile landing.
                score += 90;
            }
            if (tiles[point.Y, point.X].IsRaised())
                score += 2;
        }
        return score;
    }

    private static bool RouteViolatesRoomGraph(
        IReadOnlyList<Point> route,
        IReadOnlyList<PathRoom> unrelatedRooms,
        IReadOnlySet<Point> occupiedCorridorTiles,
        PathRoom from,
        PathRoom to)
    {
        for (int index = 0; index < route.Count; index++)
        {
            Point point = route[index];
            for (int roomIndex = 0; roomIndex < unrelatedRooms.Count; roomIndex++)
            {
                if (unrelatedRooms[roomIndex].ContainsInteriorTile(point.X, point.Y))
                    return true;
            }
            if (occupiedCorridorTiles.Contains(point)
                && !from.TileBounds.Contains(point)
                && !to.TileBounds.Contains(point))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Fallback router for the few module/branch combinations that cannot be
    /// represented by one of the inexpensive authored dog-legs. It operates
    /// only during floor generation, keeps corridor width away from unrelated
    /// rooms, and treats existing route centerlines as occupied so the tile
    /// map cannot gain a shortcut absent from the semantic room graph.
    /// </summary>
    private static List<Point>? BuildAvoidanceRoute(
        Point start,
        Point end,
        IReadOnlyList<PathRoom> unrelatedRooms,
        IReadOnlySet<Point> occupiedCorridorTiles,
        PathRoom from,
        PathRoom to,
        int width,
        Random rng)
    {
        var visited = new bool[Height, Width];
        var parent = new Point[Height, Width];
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        visited[start.Y, start.X] = true;

        Point[] directions =
        [
            new(1, 0),
            new(0, 1),
            new(-1, 0),
            new(0, -1),
        ];
        int directionOffset = rng.Next(directions.Length);
        // One centerline tile of clearance is enough here because the route
        // is widened only after it clears room walls; a larger inflation can
        // make the compact optional-wing sockets form an artificial solid
        // barrier even though a readable corridor still fits between them.
        const int clearance = 1;

        bool Blocked(Point point)
        {
            if (point.X < 2 || point.X >= Width - 2
                || point.Y < 2 || point.Y >= Height - 2)
            {
                return true;
            }
            if (from.TileBounds.Contains(point) || to.TileBounds.Contains(point))
                return false;
            // Existing passages are soft obstacles here. The authored route
            // scorer strongly prefers not to cross them; permitting a single
            // shared landing keeps optional wings routable on the compact
            // map when unrelated room footprints form a complete barrier.
            for (int roomIndex = 0; roomIndex < unrelatedRooms.Count; roomIndex++)
            {
                Rectangle avoidance = unrelatedRooms[roomIndex].TileBounds;
                avoidance.Inflate(clearance, clearance);
                if (avoidance.Contains(point))
                    return true;
            }
            return false;
        }

        while (queue.Count > 0)
        {
            Point point = queue.Dequeue();
            if (point == end)
            {
                var result = new List<Point>();
                Point cursor = end;
                result.Add(cursor);
                while (cursor != start)
                {
                    cursor = parent[cursor.Y, cursor.X];
                    result.Add(cursor);
                }
                result.Reverse();
                return result;
            }

            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                Point direction = directions[(directionIndex + directionOffset) % directions.Length];
                Point next = point + direction;
                if (next.X < 0 || next.X >= Width || next.Y < 0 || next.Y >= Height
                    || visited[next.Y, next.X]
                    || Blocked(next))
                {
                    continue;
                }
                visited[next.Y, next.X] = true;
                parent[next.Y, next.X] = point;
                queue.Enqueue(next);
            }
        }
        return null;
    }

    private static void CarveCorridorRoute(
        TileType[,] tiles,
        IReadOnlyList<Point> route,
        int width,
        PathCorridorStyle style)
    {
        int halfWidth = width / 2;
        for (int index = 0; index < route.Count; index++)
        {
            Point point = route[index];
            Point previous = route[Math.Max(0, index - 1)];
            Point next = route[Math.Min(route.Count - 1, index + 1)];
            bool horizontal = Math.Abs(next.X - previous.X) >= Math.Abs(next.Y - previous.Y);
            int landingBonus = style is PathCorridorStyle.CloudBridge or PathCorridorStyle.Starwalk
                && index > 0 && index < route.Count - 1 && index % 9 == 0
                    ? 1
                    : 0;
            for (int offset = -halfWidth - landingBonus; offset <= halfWidth + landingBonus; offset++)
            {
                int tx = horizontal ? point.X : point.X + offset;
                int ty = horizontal ? point.Y + offset : point.Y;
                if (tx > 0 && tx < Width - 1 && ty > 0 && ty < Height - 1)
                    tiles[ty, tx] = TileType.Road;
            }
        }
    }

    private static void AddDoor(PathRoom room, Point outwardDirection)
    {
        Rectangle bounds = room.TileBounds;
        Rectangle tiles;
        if (Math.Abs(outwardDirection.X) >= Math.Abs(outwardDirection.Y))
        {
            int x = outwardDirection.X >= 0 ? bounds.Right - 1 : bounds.Left;
            tiles = new Rectangle(x, bounds.Center.Y - 1, 1, 3);
        }
        else
        {
            int y = outwardDirection.Y >= 0 ? bounds.Bottom - 1 : bounds.Top;
            tiles = new Rectangle(bounds.Center.X - 1, y, 3, 1);
        }
        room.DoorWorldRects.Add(new Rectangle(
            tiles.X * Battleground.TileSize,
            tiles.Y * Battleground.TileSize,
            tiles.Width * Battleground.TileSize,
            tiles.Height * Battleground.TileSize));
    }

    private static int[,] CreateRoomBiomeMap(
        TileType[,] tiles, IReadOnlyList<PathRoom> rooms, int floorNumber)
    {
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        var result = new int[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                PathRoom nearest = rooms.MinBy(room =>
                {
                    int dx = room.TileBounds.Center.X - x;
                    int dy = room.TileBounds.Center.Y - y;
                    return dx * dx + dy * dy;
                })!;
                result[y, x] = nearest.Type switch
                {
                    PathRoomType.Start => 0,
                    PathRoomType.Treasure => 1,
                    PathRoomType.Boss => 2,
                    _ => (nearest.Depth + (floorNumber > 5 ? 1 : 0)) % 3,
                };
            }
        }
        return result;
    }
}
