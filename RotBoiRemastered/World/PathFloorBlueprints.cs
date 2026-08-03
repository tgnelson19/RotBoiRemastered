using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>The large-scale route silhouette selected for one generated floor.</summary>
public enum PathLayoutStyle
{
    Switchback,
    GrandCircuit,
    Procession,
    Floodplain,
}

/// <summary>
/// Collision-space room silhouette. This is independent from
/// <see cref="PathRoomType"/>: an assault can happen in a reservoir, a long
/// conduit, or a maze rather than always meaning "slightly larger box."
/// </summary>
public enum PathRoomShape
{
    Sanctuary,
    Chamber,
    LongHall,
    GrandArena,
    Maze,
    Crossroads,
    Diamond,
    Ring,
    Ruin,
}

internal sealed record PathRoomBlueprint(
    int Id,
    PathRoomType Type,
    Rectangle Bounds,
    PathRoomShape Shape,
    bool IsMainPath,
    int Depth,
    int Variant);

/// <summary>
/// Authored macro-layout grammar for Path floors. Seven similarly scaled room
/// modules are shuffled into a serial route before the centered boss chamber.
/// The stable placement sockets protect traversal and camera readability;
/// the shuffled module deck supplies the room-to-room variety.
/// </summary>
internal static class PathFloorBlueprints
{
    public const int RequiredPreBossRoomCount = 7;

    private static readonly PathRoomShape[] ModuleDeck =
    [
        PathRoomShape.Chamber,
        PathRoomShape.LongHall,
        PathRoomShape.GrandArena,
        PathRoomShape.Maze,
        PathRoomShape.Crossroads,
        PathRoomShape.Diamond,
        PathRoomShape.Ring,
        PathRoomShape.Ruin,
    ];

    private static readonly PathRoomType[] EncounterCadence =
    [
        PathRoomType.Skirmish,
        PathRoomType.Skirmish,
        PathRoomType.Assault,
        PathRoomType.Skirmish,
        PathRoomType.Assault,
        PathRoomType.Elite,
        PathRoomType.Elite,
    ];

    public static IReadOnlyList<PathRoomBlueprint> Create(
        PathLayoutStyle style,
        int floorNumber,
        Random rng,
        int? treasureRoomCount = null)
    {
        const int mainY = PathFloorGenerator.Height / 2;
        const int bossX = PathFloorGenerator.Width / 2;

        Rectangle Around(int centerX, int centerY, int width, int height) =>
            new(centerX - width / 2, centerY - height / 2, width, height);

        int V() => rng.Next(8);

        var rooms = new List<PathRoomBlueprint>(12)
        {
            new(
                0,
                PathRoomType.Start,
                Around(7, mainY, 11, 11),
                PathRoomShape.Sanctuary,
                true,
                0,
                V()),
        };

        Point[] sockets = MainRouteSockets(style);
        PathRoomShape[] modules = ShuffledModules(rng);
        for (int index = 0; index < RequiredPreBossRoomCount; index++)
        {
            int variant = V();
            PathRoomShape shape = modules[index];
            (int width, int height) = ModuleDimensions(shape, variant);
            Point socket = sockets[index];
            rooms.Add(new PathRoomBlueprint(
                index + 1,
                EncounterCadence[index],
                Around(socket.X, socket.Y, width, height),
                shape,
                true,
                index + 1,
                variant));
        }

        rooms.Add(new PathRoomBlueprint(
            RequiredPreBossRoomCount + 1,
            PathRoomType.Boss,
            Around(bossX, mainY, 33, 33),
            PathRoomShape.GrandArena,
            true,
            RequiredPreBossRoomCount + 1,
            V()));

        // Optional reward wings occupy the otherwise unused far side of the
        // centered boss chamber. Their stable sockets never consume or
        // shortcut one of the seven mandatory room modules.
        int treasureCount = treasureRoomCount.HasValue
            ? Math.Clamp(treasureRoomCount.Value, 0, 3)
            : PathFloorGenerator.RollTreasureRoomCount(rng);
        PathRoomBlueprint[] treasureCandidates =
        [
            new(9, PathRoomType.Treasure,
                Around(101, 13, 23, 23), PathRoomShape.Diamond, false, 4, V()),
            new(10, PathRoomType.Treasure,
                Around(128, 38, 21, 19), PathRoomShape.Ruin, false, 5, V()),
            new(11, PathRoomType.Treasure,
                Around(101, 70, 21, 19), PathRoomShape.Ring, false, 6, V()),
        ];
        rooms.AddRange(treasureCandidates.Take(treasureCount));

        if (floorNumber >= 2 && rng.NextDouble() < .68)
        {
            Point challengeSocket = style is PathLayoutStyle.Switchback
                or PathLayoutStyle.Procession
                ? new Point(70, 72)
                : new Point(70, 8);
            rooms.Add(new PathRoomBlueprint(
                12,
                PathRoomType.Challenge,
                Around(challengeSocket.X, challengeSocket.Y, 13, 13),
                style is PathLayoutStyle.Procession or PathLayoutStyle.Floodplain
                    ? PathRoomShape.Maze
                    : PathRoomShape.Crossroads,
                false,
                6,
                V()));
        }

        return rooms;
    }

    private static PathRoomShape[] ShuffledModules(Random rng)
    {
        var result = (PathRoomShape[])ModuleDeck.Clone();
        for (int index = result.Length - 1; index > 0; index--)
        {
            int swap = rng.Next(index + 1);
            (result[index], result[swap]) = (result[swap], result[index]);
        }
        return result;
    }

    private static (int Width, int Height) ModuleDimensions(
        PathRoomShape shape,
        int variant) => shape switch
    {
        PathRoomShape.LongHall when variant % 2 == 0 => (17, 9),
        PathRoomShape.LongHall => (9, 17),
        _ => (15, 15),
    };

    private static Point[] MainRouteSockets(PathLayoutStyle style)
    {
        // The upper and lower circuits are mirror-safe. Small style-specific
        // offsets change the route silhouette without allowing modules to
        // overlap each other or the centered 33x33 boss arena.
        return style switch
        {
            PathLayoutStyle.Switchback =>
            [
                new(15, 13), new(33, 10), new(51, 15), new(44, 35),
                new(22, 30), new(17, 65), new(42, 68),
            ],
            PathLayoutStyle.Procession =>
            [
                new(15, 12), new(34, 10), new(51, 15), new(43, 35),
                new(22, 29), new(17, 64), new(42, 68),
            ],
            PathLayoutStyle.GrandCircuit =>
            [
                new(15, 67), new(33, 70), new(51, 65), new(44, 45),
                new(22, 50), new(17, 16), new(42, 13),
            ],
            _ =>
            [
                new(15, 68), new(34, 70), new(51, 65), new(43, 45),
                new(22, 51), new(17, 17), new(42, 13),
            ],
        };
    }
}
