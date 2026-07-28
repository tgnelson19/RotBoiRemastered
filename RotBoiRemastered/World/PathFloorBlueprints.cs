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
/// Authored macro-layout grammar for Path floors. Dimensions deliberately
/// vary by several screen lengths; procedural work inside each blueprint
/// supplies the micro-layout, theme treatment, props, and encounters.
/// </summary>
internal static class PathFloorBlueprints
{
    public static IReadOnlyList<PathRoomBlueprint> Create(
        PathLayoutStyle style, int floorNumber, Random rng)
    {
        const int mainY = PathFloorGenerator.Height / 2;
        const int bossX = PathFloorGenerator.Width / 2;

        Rectangle Around(int centerX, int centerY, int width, int height) =>
            new(centerX - width / 2, centerY - height / 2, width, height);

        int V() => rng.Next(8);
        var rooms = style switch
        {
            PathLayoutStyle.Switchback => new List<PathRoomBlueprint>
            {
                new(0, PathRoomType.Start, Around(8, mainY, 13, 13), PathRoomShape.Sanctuary, true, 0, V()),
                new(1, PathRoomType.Skirmish, Around(27, 15, 29, 9), PathRoomShape.LongHall, true, 1, V()),
                new(2, PathRoomType.Assault, Around(35, mainY, 25, 23), PathRoomShape.GrandArena, true, 2, V()),
                new(3, PathRoomType.Elite, Around(31, 68, 33, 17), PathRoomShape.Maze, true, 3, V()),
                new(4, PathRoomType.Boss, Around(bossX, mainY, 33, 33), PathRoomShape.GrandArena, true, 4, V()),
            },
            PathLayoutStyle.GrandCircuit => new List<PathRoomBlueprint>
            {
                new(0, PathRoomType.Start, Around(8, mainY, 13, 13), PathRoomShape.Sanctuary, true, 0, V()),
                new(1, PathRoomType.Skirmish, Around(25, mainY, 19, 19), PathRoomShape.Crossroads, true, 1, V()),
                new(2, PathRoomType.Assault, Around(41, 14, 25, 19), PathRoomShape.Ring, true, 2, V()),
                new(3, PathRoomType.Elite, Around(40, 68, 27, 17), PathRoomShape.Maze, true, 3, V()),
                new(4, PathRoomType.Boss, Around(bossX, mainY, 33, 33), PathRoomShape.GrandArena, true, 4, V()),
            },
            PathLayoutStyle.Procession => new List<PathRoomBlueprint>
            {
                new(0, PathRoomType.Start, Around(8, mainY, 13, 13), PathRoomShape.Sanctuary, true, 0, V()),
                new(1, PathRoomType.Skirmish, Around(28, mainY, 31, 9), PathRoomShape.LongHall, true, 1, V()),
                new(2, PathRoomType.Assault, Around(43, 14, 19, 21), PathRoomShape.Maze, true, 2, V()),
                new(3, PathRoomType.Elite, Around(40, 68, 27, 23), PathRoomShape.GrandArena, true, 3, V()),
                new(4, PathRoomType.Boss, Around(bossX, mainY, 33, 33), PathRoomShape.GrandArena, true, 4, V()),
            },
            _ => new List<PathRoomBlueprint>
            {
                new(0, PathRoomType.Start, Around(8, mainY, 13, 13), PathRoomShape.Sanctuary, true, 0, V()),
                new(1, PathRoomType.Skirmish, Around(25, 17, 21, 19), PathRoomShape.Crossroads, true, 1, V()),
                new(2, PathRoomType.Assault, Around(30, 65, 9, 29), PathRoomShape.LongHall, true, 2, V()),
                new(3, PathRoomType.Elite, Around(43, mainY, 21, 21), PathRoomShape.Ring, true, 3, V()),
                new(4, PathRoomType.Boss, Around(bossX, mainY, 33, 33), PathRoomShape.GrandArena, true, 4, V()),
            },
        };

        // Treasure branches use a chained 50% roll: zero or one is common,
        // while two and three become increasingly rare. The larger footprints
        // leave enough room for their guardian-strength encounter rather than
        // presenting the chest inside the old 11x11 closet.
        int treasureCount = PathFloorGenerator.RollTreasureRoomCount(rng);
        IReadOnlyList<PathRoomBlueprint> treasureCandidates = style switch
        {
            PathLayoutStyle.Switchback =>
            [
                new(5, PathRoomType.Treasure, Around(49, 8, 15, 13), PathRoomShape.Diamond, false, 2, V()),
                new(6, PathRoomType.Treasure, Around(8, 68, 13, 15), PathRoomShape.Ruin, false, 3, V()),
                new(7, PathRoomType.Treasure, Around(8, 27, 13, 13), PathRoomShape.Ring, false, 1, V()),
            ],
            PathLayoutStyle.GrandCircuit =>
            [
                new(5, PathRoomType.Treasure, Around(16, 68, 15, 15), PathRoomShape.Ruin, false, 3, V()),
                new(6, PathRoomType.Treasure, Around(15, 13, 15, 15), PathRoomShape.Diamond, false, 2, V()),
                new(7, PathRoomType.Treasure, Around(10, 26, 13, 11), PathRoomShape.Ring, false, 1, V()),
            ],
            PathLayoutStyle.Procession =>
            [
                new(5, PathRoomType.Treasure, Around(18, 68, 15, 15), PathRoomShape.Diamond, false, 3, V()),
                new(6, PathRoomType.Treasure, Around(18, 13, 15, 15), PathRoomShape.Ruin, false, 2, V()),
                new(7, PathRoomType.Treasure, Around(8, 26, 13, 11), PathRoomShape.Ring, false, 1, V()),
            ],
            _ =>
            [
                new(5, PathRoomType.Treasure, Around(48, 10, 15, 13), PathRoomShape.Ruin, false, 2, V()),
                new(6, PathRoomType.Treasure, Around(14, 68, 15, 15), PathRoomShape.Diamond, false, 3, V()),
                new(7, PathRoomType.Treasure, Around(8, 53, 13, 11), PathRoomShape.Ring, false, 2, V()),
            ],
        };
        rooms.AddRange(treasureCandidates.Take(treasureCount));

        if (floorNumber >= 2 && rng.NextDouble() < .68)
        {
            PathRoomBlueprint challenge = style switch
            {
                PathLayoutStyle.Switchback => new(8, PathRoomType.Challenge,
                    Around(53, 69, 11, 13), PathRoomShape.Ring, false, 3, V()),
                PathLayoutStyle.GrandCircuit => new(8, PathRoomType.Challenge,
                    Around(9, 55, 13, 11), PathRoomShape.LongHall, false, 2, V()),
                PathLayoutStyle.Procession => new(8, PathRoomType.Challenge,
                    Around(8, 55, 13, 11), PathRoomShape.Ruin, false, 2, V()),
                _ => new(8, PathRoomType.Challenge,
                    Around(49, 70, 13, 13), PathRoomShape.Maze, false, 3, V()),
            };
            rooms.Add(challenge);
        }

        return rooms;
    }
}
