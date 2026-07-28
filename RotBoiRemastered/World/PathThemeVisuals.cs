using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>Render layer for semantic, non-gameplay Path scenery.</summary>
public enum PathDecorationLayer
{
    Floor,
    Low,
    Raised,
    Ambient,
}

/// <summary>
/// Visual vocabulary shared by the generated layout and renderer. These are
/// semantic props rather than extra collision tiles, so themed density can
/// grow without compromising movement or camera-relative aiming.
/// </summary>
public enum PathDecorationKind
{
    SewerChannel,
    SewerGrate,
    SludgePool,
    PipeStack,
    Valve,
    Pump,
    BrickRunes,
    PressureTank,
    DripEmitter,

    WaterPool,
    CausticCurrent,
    LensBuoy,
    SteppingStone,
    BrokenColumn,
    MosaicLens,
    MirrorArch,
    RippleEmitter,

    CloudBank,
    WindLane,
    StormCrack,
    EchoPylon,
    Chime,
    LightningRod,
    ResonanceTiles,
    OrganStack,
    WindEmitter,

    StarField,
    Nebula,
    Constellation,
    VoidRift,
    Asteroid,
    PrismObelisk,
    OrbitShrine,
    DreamGlyph,
    LanternSpire,
    StarEmitter,

    CrackedEarth,
    RotPatch,
    ScorchedCrater,
    RouteChevron,
    ThresholdRune,
    RustBarricade,
    DeadTree,
    RuinSlab,
    CinderPlate,
    FurnaceIdol,
    TreasureSeal,
    AshEmitter,
}

public sealed record PathDecoration(
    PathDecorationKind Kind,
    PathDecorationLayer Layer,
    Vector2 WorldPosition,
    float Scale,
    int Variant,
    int RoomId);

/// <summary>
/// One sense's material, prop, and ambient vocabulary. Profiles deliberately
/// contain no rendering code; they let generation and rendering vary
/// independently while keeping the theme contract data-driven.
/// </summary>
public sealed record PathThemeVisualProfile(
    string Key,
    IReadOnlyList<PathDecorationKind> FloorMotifs,
    IReadOnlyList<PathDecorationKind> RaisedProps,
    PathDecorationKind AmbientEmitter,
    int BaseFloorBudget,
    int BaseRaisedBudget);

public static class PathThemeVisuals
{
    public static readonly IReadOnlyDictionary<string, PathThemeVisualProfile> Profiles =
        new Dictionary<string, PathThemeVisualProfile>
        {
            ["touch"] = new(
                "touch",
                new[] { PathDecorationKind.SewerChannel, PathDecorationKind.SewerGrate, PathDecorationKind.SludgePool, PathDecorationKind.BrickRunes },
                new[] { PathDecorationKind.PipeStack, PathDecorationKind.Valve, PathDecorationKind.Pump, PathDecorationKind.PressureTank },
                PathDecorationKind.DripEmitter, 6, 4),
            ["sight"] = new(
                "sight",
                new[] { PathDecorationKind.WaterPool, PathDecorationKind.CausticCurrent, PathDecorationKind.MosaicLens },
                new[] { PathDecorationKind.LensBuoy, PathDecorationKind.SteppingStone, PathDecorationKind.BrokenColumn, PathDecorationKind.MirrorArch },
                PathDecorationKind.RippleEmitter, 7, 4),
            ["sound"] = new(
                "sound",
                new[] { PathDecorationKind.CloudBank, PathDecorationKind.WindLane, PathDecorationKind.ResonanceTiles },
                new[] { PathDecorationKind.EchoPylon, PathDecorationKind.Chime, PathDecorationKind.LightningRod, PathDecorationKind.OrganStack },
                PathDecorationKind.WindEmitter, 7, 4),
            ["phantasia"] = new(
                "phantasia",
                new[] { PathDecorationKind.StarField, PathDecorationKind.Nebula, PathDecorationKind.Constellation, PathDecorationKind.DreamGlyph },
                new[] { PathDecorationKind.Asteroid, PathDecorationKind.PrismObelisk, PathDecorationKind.OrbitShrine, PathDecorationKind.LanternSpire },
                PathDecorationKind.StarEmitter, 8, 4),
            ["chemesthesis"] = new(
                "chemesthesis",
                new[] { PathDecorationKind.CrackedEarth, PathDecorationKind.RotPatch, PathDecorationKind.ScorchedCrater, PathDecorationKind.CinderPlate },
                new[] { PathDecorationKind.RustBarricade, PathDecorationKind.DeadTree, PathDecorationKind.RuinSlab, PathDecorationKind.FurnaceIdol },
                PathDecorationKind.AshEmitter, 7, 5),
        };

    public static PathThemeVisualProfile For(string key) =>
        Profiles.TryGetValue(key, out var profile)
            ? profile
            : throw new KeyNotFoundException($"Unknown Path visual theme: {key}");

    /// <summary>
    /// Large, readable floor seal used at the protected entrance. These are
    /// deliberately the material-emblem motifs rather than the loose
    /// environmental pools used deeper in a floor.
    /// </summary>
    public static PathDecorationKind EntranceCrestFor(string senseKey) => senseKey switch
    {
        "touch" => PathDecorationKind.BrickRunes,
        "sight" => PathDecorationKind.MosaicLens,
        "sound" => PathDecorationKind.ResonanceTiles,
        "phantasia" => PathDecorationKind.DreamGlyph,
        "chemesthesis" => PathDecorationKind.CinderPlate,
        _ => throw new KeyNotFoundException($"Unknown Path visual theme: {senseKey}"),
    };

    /// <summary>
    /// Central medallion for Grand Arena rooms. Sharing each entrance's crest
    /// makes the dungeon feel architecturally authored by one culture while
    /// the larger scale and surrounding ring turn it into arena spectacle.
    /// </summary>
    public static PathDecorationKind GrandArenaCenterpieceFor(string senseKey) =>
        EntranceCrestFor(senseKey);

    public static IReadOnlyList<PathDecoration> GenerateDecorations(
        string senseKey,
        int floorNumber,
        IReadOnlyList<PathRoom> rooms,
        Random? rng = null,
        TileType[,]? tiles = null,
        IReadOnlyList<PathConnection>? connections = null)
    {
        rng ??= Random.Shared;
        var profile = For(senseKey);
        bool deteriorated = floorNumber > PathRunFloorBoundary;
        var decorations = new List<PathDecoration>();

        foreach (var room in rooms)
        {
            AddSignatureDecoration(decorations, senseKey, room, deteriorated, rng);

            int roomBonus = room.Type switch
            {
                PathRoomType.Start => 1,
                PathRoomType.Assault => 1,
                PathRoomType.Elite => 2,
                PathRoomType.Challenge => 4,
                PathRoomType.Treasure => 3,
                PathRoomType.Boss => 5,
                _ => 0,
            };
            int floorBudget = profile.BaseFloorBudget + roomBonus + (deteriorated ? 2 : 0);
            int raisedBudget = profile.BaseRaisedBudget + roomBonus / 2 + (deteriorated ? 1 : 0);

            var floorAnchors = CandidateAnchors(room, perimeterOnly: false, tiles)
                .OrderBy(_ => rng.NextDouble()).ToList();
            var raisedAnchors = CandidateAnchors(room, perimeterOnly: true, tiles)
                .Where(point => FarFromDoors(room, point))
                .OrderBy(_ => rng.NextDouble()).ToList();

            for (int index = 0; index < Math.Min(floorBudget, floorAnchors.Count); index++)
            {
                var kind = profile.FloorMotifs[(index + room.Id + floorNumber) % profile.FloorMotifs.Count];
                decorations.Add(new PathDecoration(
                    kind, LayerFor(kind), TileCenter(floorAnchors[index]),
                    .78f + (float)rng.NextDouble() * .5f,
                    rng.Next(4), room.Id));
            }
            for (int index = 0; index < Math.Min(raisedBudget, raisedAnchors.Count); index++)
            {
                var kind = profile.RaisedProps[(index + room.Depth + floorNumber) % profile.RaisedProps.Count];
                decorations.Add(new PathDecoration(
                    kind, PathDecorationLayer.Raised, TileCenter(raisedAnchors[index]),
                    .82f + (float)rng.NextDouble() * .38f,
                    rng.Next(4), room.Id));
            }
            AddArchitectureAssembly(decorations, senseKey, room, tiles, deteriorated);
            AddEntranceComposition(decorations, senseKey, room, tiles, profile, deteriorated);
            AddGrandArenaComposition(decorations, senseKey, room, tiles, profile, deteriorated);

            int ambientCount = 1
                + (room.Type is PathRoomType.Treasure or PathRoomType.Challenge or PathRoomType.Boss ? 1 : 0)
                + (deteriorated ? 1 : 0);
            for (int index = 0; index < ambientCount; index++)
            {
                float angle = (index + .35f) * MathF.Tau / ambientCount;
                float radius = Battleground.TileSize * (room.Type == PathRoomType.Boss ? 7f : 2.2f);
                decorations.Add(new PathDecoration(
                    profile.AmbientEmitter, PathDecorationLayer.Ambient,
                    room.WorldCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius,
                    deteriorated ? 1.25f : 1f, rng.Next(4), room.Id));
            }
        }

        if (connections is not null)
            AddCorridorMotifs(decorations, senseKey, connections, floorNumber, rng);

        return decorations;
    }

    // Kept local rather than depending on Systems.PathRun so World generation
    // remains independent of the live run director.
    private const int PathRunFloorBoundary = 5;

    private static void AddSignatureDecoration(List<PathDecoration> output, string senseKey,
        PathRoom room, bool deteriorated, Random rng)
    {
        if (room.Type == PathRoomType.Treasure)
        {
            output.Add(new PathDecoration(
                PathDecorationKind.TreasureSeal,
                PathDecorationLayer.Floor,
                room.WorldCenter,
                4.8f,
                room.Variant % 4,
                room.Id));
        }

        PathDecorationKind? kind = (senseKey, room.Type) switch
        {
            ("touch", PathRoomType.Start) => PathDecorationKind.BrickRunes,
            ("touch", PathRoomType.Treasure) => PathDecorationKind.SludgePool,
            ("touch", PathRoomType.Boss) => PathDecorationKind.SewerChannel,
            ("touch", PathRoomType.Challenge) => PathDecorationKind.SludgePool,

            ("sight", PathRoomType.Start) => PathDecorationKind.MosaicLens,
            ("sight", PathRoomType.Treasure) => PathDecorationKind.WaterPool,
            ("sight", PathRoomType.Boss) => PathDecorationKind.WaterPool,
            ("sight", PathRoomType.Challenge) => PathDecorationKind.CausticCurrent,

            ("sound", PathRoomType.Start) => PathDecorationKind.ResonanceTiles,
            ("sound", PathRoomType.Treasure) => PathDecorationKind.CloudBank,
            ("sound", PathRoomType.Boss) => PathDecorationKind.StormCrack,
            ("sound", PathRoomType.Challenge) => PathDecorationKind.StormCrack,

            ("phantasia", PathRoomType.Start) => PathDecorationKind.DreamGlyph,
            ("phantasia", PathRoomType.Treasure) => PathDecorationKind.Nebula,
            ("phantasia", PathRoomType.Boss) => PathDecorationKind.VoidRift,
            ("phantasia", PathRoomType.Challenge) => PathDecorationKind.Nebula,

            ("chemesthesis", PathRoomType.Start) => PathDecorationKind.CinderPlate,
            ("chemesthesis", PathRoomType.Treasure) => PathDecorationKind.RotPatch,
            ("chemesthesis", PathRoomType.Boss) => PathDecorationKind.ScorchedCrater,
            ("chemesthesis", PathRoomType.Challenge) => PathDecorationKind.RotPatch,
            _ => null,
        };
        if (kind is null)
            return;

        float scale = room.Type switch
        {
            PathRoomType.Start => 4.25f,
            PathRoomType.Treasure => 2.6f,
            PathRoomType.Challenge => 3.1f,
            PathRoomType.Boss => 7.5f,
            _ => 1.4f,
        };
        if (deteriorated)
            scale *= 1.15f;
        output.Add(new PathDecoration(kind.Value, LayerFor(kind.Value), room.WorldCenter, scale, rng.Next(4), room.Id));

        if (!deteriorated)
            return;
        PathDecorationKind deterioration = senseKey switch
        {
            "touch" => PathDecorationKind.SludgePool,
            "sight" => PathDecorationKind.WaterPool,
            "sound" => PathDecorationKind.StormCrack,
            "phantasia" => PathDecorationKind.VoidRift,
            "chemesthesis" => PathDecorationKind.RotPatch,
            _ => PathDecorationKind.CrackedEarth,
        };
        output.Add(new PathDecoration(
            deterioration, LayerFor(deterioration),
            room.WorldCenter + new Vector2(Battleground.TileSize * 2.1f, -Battleground.TileSize * 1.7f),
            Math.Max(1.6f, scale * .42f), rng.Next(4), room.Id));
    }

    private static void AddArchitectureAssembly(
        List<PathDecoration> output,
        string senseKey,
        PathRoom room,
        TileType[,]? tiles,
        bool deteriorated)
    {
        PathDecorationKind landmark = senseKey switch
        {
            "touch" => PathDecorationKind.PressureTank,
            "sight" => PathDecorationKind.MirrorArch,
            "sound" => PathDecorationKind.OrganStack,
            "phantasia" => PathDecorationKind.LanternSpire,
            _ => PathDecorationKind.FurnaceIdol,
        };
        Point[] offsets = room.Type switch
        {
            PathRoomType.Treasure =>
                [new(-3, -3), new(3, -3), new(-3, 3), new(3, 3)],
            PathRoomType.Boss =>
                [new(-6, -5), new(6, -5), new(-6, 5), new(6, 5)],
            PathRoomType.Elite or PathRoomType.Challenge =>
                [new(-4, 0), new(4, 0)],
            _ when room.Shape is PathRoomShape.LongHall =>
                [new(-4, -2), new(4, 2)],
            _ => Array.Empty<Point>(),
        };

        int variant = room.Variant;
        foreach (Point offset in offsets)
        {
            Point tile = new(room.TileBounds.Center.X + offset.X, room.TileBounds.Center.Y + offset.Y);
            if (!room.ContainsInteriorTile(tile.X, tile.Y)
                || (tiles is not null && tiles[tile.Y, tile.X].IsSolid())
                || !FarFromDoors(room, tile))
            {
                continue;
            }
            output.Add(new PathDecoration(
                landmark,
                PathDecorationLayer.Raised,
                TileCenter(tile),
                deteriorated ? 1.18f : 1.02f,
                variant++ % 4,
                room.Id));
        }
    }

    /// <summary>
    /// Turns the protected spawn from an empty safety box into a ceremonial
    /// threshold: a readable crest beneath the player, a processional floor
    /// axis toward the first door, paired landmarks, and two ambient sources.
    /// Everything remains semantic scenery, so the composition adds no
    /// collision or combat behavior.
    /// </summary>
    private static void AddEntranceComposition(
        List<PathDecoration> output,
        string senseKey,
        PathRoom room,
        TileType[,]? tiles,
        PathThemeVisualProfile profile,
        bool deteriorated)
    {
        if (room.Type != PathRoomType.Start)
            return;

        Point center = room.InteriorTileBounds.Center;
        Point forward = EntranceDirection(room);
        Point side = new(-forward.Y, forward.X);
        PathDecorationKind floorAccent = senseKey switch
        {
            "touch" => PathDecorationKind.SewerChannel,
            "sight" => PathDecorationKind.CausticCurrent,
            "sound" => PathDecorationKind.WindLane,
            "phantasia" => PathDecorationKind.Constellation,
            _ => PathDecorationKind.CrackedEarth,
        };
        (PathDecorationKind Landmark, PathDecorationKind Sentinel) raised = senseKey switch
        {
            "touch" => (PathDecorationKind.PressureTank, PathDecorationKind.Valve),
            "sight" => (PathDecorationKind.MirrorArch, PathDecorationKind.LensBuoy),
            "sound" => (PathDecorationKind.OrganStack, PathDecorationKind.Chime),
            "phantasia" => (PathDecorationKind.LanternSpire, PathDecorationKind.PrismObelisk),
            _ => (PathDecorationKind.FurnaceIdol, PathDecorationKind.RuinSlab),
        };

        Point Offset(int forwardAmount, int sideAmount) => new(
            center.X + forward.X * forwardAmount + side.X * sideAmount,
            center.Y + forward.Y * forwardAmount + side.Y * sideAmount);

        // A paired aisle visually aims the player toward the first corridor,
        // while side panels frame the center without drawing over the spawn.
        var floorOffsets = new[]
        {
            (Forward: -2, Side: -3), (Forward: -2, Side: 3),
            (Forward: 0, Side: -3), (Forward: 0, Side: 3),
            (Forward: 2, Side: -2), (Forward: 2, Side: 2),
        };
        for (int index = 0; index < floorOffsets.Length; index++)
        {
            Point tile = Offset(floorOffsets[index].Forward, floorOffsets[index].Side);
            if (!OpenDecorationTile(room, tile, tiles))
                continue;
            output.Add(new PathDecoration(
                floorAccent,
                LayerFor(floorAccent),
                TileCenter(tile),
                (deteriorated ? 1.42f : 1.2f) + index % 2 * .12f,
                (room.Variant + index) % 4,
                room.Id));
        }

        Point threshold = Offset(4, 0);
        if (OpenDecorationTile(room, threshold, tiles))
        {
            output.Add(new PathDecoration(
                PathDecorationKind.RouteChevron,
                PathDecorationLayer.Floor,
                TileCenter(threshold),
                1.55f,
                DirectionVariant(forward),
                room.Id));
        }

        var raisedOffsets = new[]
        {
            (Forward: 2, Side: -4, Kind: raised.Landmark),
            (Forward: 2, Side: 4, Kind: raised.Landmark),
            (Forward: -3, Side: -3, Kind: raised.Sentinel),
            (Forward: -3, Side: 3, Kind: raised.Sentinel),
        };
        for (int index = 0; index < raisedOffsets.Length; index++)
        {
            var placement = raisedOffsets[index];
            Point tile = Offset(placement.Forward, placement.Side);
            if (!OpenDecorationTile(room, tile, tiles) || !FarFromDoors(room, tile))
                continue;
            output.Add(new PathDecoration(
                placement.Kind,
                PathDecorationLayer.Raised,
                TileCenter(tile),
                deteriorated ? 1.2f : 1.08f,
                (room.Variant + index) % 4,
                room.Id));
        }

        for (int direction = -1; direction <= 1; direction += 2)
        {
            Point tile = Offset(1, direction * 3);
            if (!OpenDecorationTile(room, tile, tiles))
                continue;
            output.Add(new PathDecoration(
                profile.AmbientEmitter,
                PathDecorationLayer.Ambient,
                TileCenter(tile),
                deteriorated ? 1.3f : 1.08f,
                (room.Variant + direction + 4) % 4,
                room.Id));
        }
    }

    /// <summary>
    /// Grand Arenas receive a complete theme-specific composition instead of
    /// inheriting the same random prop scatter as smaller rooms: one oversized
    /// medallion, an eight-part floor ring, paired perimeter monuments, and a
    /// four-source atmospheric halo.
    /// </summary>
    private static void AddGrandArenaComposition(
        List<PathDecoration> output,
        string senseKey,
        PathRoom room,
        TileType[,]? tiles,
        PathThemeVisualProfile profile,
        bool deteriorated)
    {
        if (room.Shape != PathRoomShape.GrandArena)
            return;

        PathDecorationKind ringKind = senseKey switch
        {
            "touch" => PathDecorationKind.SewerChannel,
            "sight" => PathDecorationKind.CausticCurrent,
            "sound" => PathDecorationKind.WindLane,
            "phantasia" => PathDecorationKind.Constellation,
            _ => PathDecorationKind.CrackedEarth,
        };
        (PathDecorationKind Primary, PathDecorationKind Secondary) monuments = senseKey switch
        {
            "touch" => (PathDecorationKind.PressureTank, PathDecorationKind.Pump),
            "sight" => (PathDecorationKind.MirrorArch, PathDecorationKind.LensBuoy),
            "sound" => (PathDecorationKind.OrganStack, PathDecorationKind.EchoPylon),
            "phantasia" => (PathDecorationKind.LanternSpire, PathDecorationKind.PrismObelisk),
            _ => (PathDecorationKind.FurnaceIdol, PathDecorationKind.RuinSlab),
        };

        output.Add(new PathDecoration(
            GrandArenaCenterpieceFor(senseKey),
            PathDecorationLayer.Floor,
            room.WorldCenter,
            (room.Type == PathRoomType.Boss ? 7.8f : 6.4f) * (deteriorated ? 1.08f : 1f),
            room.Variant % 4,
            room.Id));

        Rectangle inner = room.InteriorTileBounds;
        float ringRadiusX = Math.Max(4, inner.Width * .28f) * Battleground.TileSize;
        float ringRadiusY = Math.Max(4, inner.Height * .28f) * Battleground.TileSize;
        for (int index = 0; index < 8; index++)
        {
            float angle = -MathF.PI / 2f + index * MathF.Tau / 8f;
            Vector2 world = room.WorldCenter + new Vector2(
                MathF.Cos(angle) * ringRadiusX,
                MathF.Sin(angle) * ringRadiusY);
            Point tile = new(
                (int)(world.X / Battleground.TileSize),
                (int)(world.Y / Battleground.TileSize));
            if (!OpenDecorationTile(room, tile, tiles))
                continue;
            output.Add(new PathDecoration(
                ringKind,
                LayerFor(ringKind),
                TileCenter(tile),
                (deteriorated ? 1.76f : 1.48f) + index % 2 * .14f,
                (room.Variant + index) % 4,
                room.Id));
        }

        int radiusX = Math.Max(4, inner.Width / 2 - 4);
        int radiusY = Math.Max(4, inner.Height / 2 - 4);
        Point center = inner.Center;
        var monumentOffsets = new[]
        {
            new Point(-radiusX, -radiusY), new Point(radiusX, -radiusY),
            new Point(-radiusX, radiusY), new Point(radiusX, radiusY),
            new Point(-radiusX, 0), new Point(radiusX, 0),
        };
        for (int index = 0; index < monumentOffsets.Length; index++)
        {
            Point offset = monumentOffsets[index];
            Point tile = new(center.X + offset.X, center.Y + offset.Y);
            if (!OpenDecorationTile(room, tile, tiles) || !FarFromDoors(room, tile))
                continue;
            output.Add(new PathDecoration(
                index % 3 == 2 ? monuments.Secondary : monuments.Primary,
                PathDecorationLayer.Raised,
                TileCenter(tile),
                deteriorated ? 1.3f : 1.14f,
                (room.Variant + index) % 4,
                room.Id));
        }

        for (int index = 0; index < 4; index++)
        {
            float angle = MathF.PI / 4f + index * MathF.Tau / 4f;
            output.Add(new PathDecoration(
                profile.AmbientEmitter,
                PathDecorationLayer.Ambient,
                room.WorldCenter + new Vector2(
                    MathF.Cos(angle) * ringRadiusX * .72f,
                    MathF.Sin(angle) * ringRadiusY * .72f),
                deteriorated ? 1.42f : 1.18f,
                (room.Variant + index) % 4,
                room.Id));
        }
    }

    private static Point EntranceDirection(PathRoom room)
    {
        if (room.DoorWorldRects.Count == 0)
            return new Point(1, 0);
        Vector2 doorCenter = room.DoorWorldRects
            .MinBy(door => Vector2.DistanceSquared(door.Center.ToVector2(), room.WorldCenter))
            .Center.ToVector2();
        Vector2 delta = doorCenter - room.WorldCenter;
        if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
            return new Point(delta.X >= 0 ? 1 : -1, 0);
        return new Point(0, delta.Y >= 0 ? 1 : -1);
    }

    private static int DirectionVariant(Point direction) =>
        direction.X > 0 ? 0
        : direction.Y > 0 ? 1
        : direction.X < 0 ? 2
        : 3;

    private static bool OpenDecorationTile(PathRoom room, Point tile, TileType[,]? tiles) =>
        room.ContainsInteriorTile(tile.X, tile.Y)
        && (tiles is null || !tiles[tile.Y, tile.X].IsSolid());

    private static void AddCorridorMotifs(
        List<PathDecoration> output,
        string senseKey,
        IReadOnlyList<PathConnection> connections,
        int floorNumber,
        Random rng)
    {
        PathDecorationKind kind = senseKey switch
        {
            "touch" => PathDecorationKind.SewerGrate,
            "sight" => PathDecorationKind.CausticCurrent,
            "sound" => PathDecorationKind.WindLane,
            "phantasia" => PathDecorationKind.Constellation,
            _ => PathDecorationKind.CrackedEarth,
        };
        int stride = floorNumber > 5 ? 7 : 10;
        foreach (var connection in connections)
        {
            var route = connection.Route;
            if (route is null || route.Count < stride + 2)
                continue;
            for (int index = stride / 2; index < route.Count - stride / 2; index += stride)
            {
                Point tile = route[index];
                Point previous = route[Math.Max(0, index - 1)];
                Point next = route[Math.Min(route.Count - 1, index + 1)];
                int directionVariant = Math.Abs(next.X - previous.X) >= Math.Abs(next.Y - previous.Y)
                    ? next.X >= previous.X ? 0 : 2
                    : next.Y >= previous.Y ? 1 : 3;
                output.Add(new PathDecoration(
                    kind,
                    LayerFor(kind),
                    TileCenter(tile),
                    .7f + (float)rng.NextDouble() * .3f,
                    (index + connection.FromRoomId + connection.ToRoomId) % 4,
                    -1));
                if ((index / stride) % 2 == 0)
                {
                    output.Add(new PathDecoration(
                        PathDecorationKind.RouteChevron,
                        PathDecorationLayer.Floor,
                        TileCenter(tile),
                        .82f,
                        directionVariant,
                        -1));
                }
            }

            int thresholdIndex = Math.Max(1, route.Count - Math.Max(3, connection.Width));
            Point threshold = route[thresholdIndex];
            Point before = route[Math.Max(0, thresholdIndex - 1)];
            int thresholdVariant = Math.Abs(threshold.X - before.X) >= Math.Abs(threshold.Y - before.Y)
                ? threshold.X >= before.X ? 0 : 2
                : threshold.Y >= before.Y ? 1 : 3;
            output.Add(new PathDecoration(
                PathDecorationKind.ThresholdRune,
                PathDecorationLayer.Floor,
                TileCenter(threshold),
                1.05f,
                thresholdVariant,
                connection.ToRoomId));
        }
    }

    private static IEnumerable<Point> CandidateAnchors(
        PathRoom room, bool perimeterOnly, TileType[,]? tiles)
    {
        Rectangle inner = room.InteriorTileBounds;
        int stride = room.Type == PathRoomType.Boss ? 3 : 2;
        for (int y = inner.Top + 1; y < inner.Bottom - 1; y += stride)
        {
            for (int x = inner.Left + 1; x < inner.Right - 1; x += stride)
            {
                if (!room.ContainsInteriorTile(x, y)
                    || (tiles is not null && tiles[y, x].IsSolid()))
                {
                    continue;
                }
                int dx = Math.Abs(x - inner.Center.X), dy = Math.Abs(y - inner.Center.Y);
                if (dx <= 2 && dy <= 2)
                    continue;
                if (perimeterOnly && dx < inner.Width / 2 - 3 && dy < inner.Height / 2 - 3)
                    continue;
                yield return new Point(x, y);
            }
        }
    }

    private static bool FarFromDoors(PathRoom room, Point tile)
    {
        var world = TileCenter(tile);
        return room.DoorWorldRects.All(door =>
        {
            var expanded = door;
            expanded.Inflate(Battleground.TileSize, Battleground.TileSize);
            return !expanded.Contains(world.ToPoint());
        });
    }

    private static Vector2 TileCenter(Point tile) =>
        new((tile.X + .5f) * Battleground.TileSize, (tile.Y + .5f) * Battleground.TileSize);

    public static PathDecorationLayer LayerFor(PathDecorationKind kind) => kind switch
    {
        PathDecorationKind.SludgePool
            or PathDecorationKind.WaterPool
            or PathDecorationKind.CloudBank
            or PathDecorationKind.Nebula
            or PathDecorationKind.RotPatch => PathDecorationLayer.Low,
        PathDecorationKind.PipeStack
            or PathDecorationKind.Valve
            or PathDecorationKind.Pump
            or PathDecorationKind.PressureTank
            or PathDecorationKind.LensBuoy
            or PathDecorationKind.SteppingStone
            or PathDecorationKind.BrokenColumn
            or PathDecorationKind.MirrorArch
            or PathDecorationKind.EchoPylon
            or PathDecorationKind.Chime
            or PathDecorationKind.LightningRod
            or PathDecorationKind.OrganStack
            or PathDecorationKind.Asteroid
            or PathDecorationKind.PrismObelisk
            or PathDecorationKind.OrbitShrine
            or PathDecorationKind.LanternSpire
            or PathDecorationKind.RustBarricade
            or PathDecorationKind.DeadTree
            or PathDecorationKind.RuinSlab
            or PathDecorationKind.FurnaceIdol => PathDecorationLayer.Raised,
        PathDecorationKind.DripEmitter
            or PathDecorationKind.RippleEmitter
            or PathDecorationKind.WindEmitter
            or PathDecorationKind.StarEmitter
            or PathDecorationKind.AshEmitter => PathDecorationLayer.Ambient,
        _ => PathDecorationLayer.Floor,
    };
}
