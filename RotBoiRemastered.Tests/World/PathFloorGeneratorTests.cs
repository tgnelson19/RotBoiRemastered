using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

public sealed class PathFloorGeneratorTests
{
    private static void RevealAllTreasures(PathFloorLayout layout)
    {
        foreach (PathConnection connection in layout.Connections.Where(value => value.Hidden))
        {
            Point clue = Assert.IsType<Point>(connection.ClueTile);
            var world = new Vector2(
                (clue.X + .5f) * Battleground.TileSize,
                (clue.Y + .5f) * Battleground.TileSize);
            Assert.True(layout.TryRevealTreasure(world, 1f));
        }
    }

    private sealed class ChainedRollRandom(params double[] rolls) : Random
    {
        private int _index;
        public override double NextDouble() => rolls[_index++];
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void Generate_AlwaysIncludesProtectedStartAndCenteredBoss(string senseKey)
    {
        for (int floor = 1; floor <= 10; floor++)
        {
            var layout = PathFloorGenerator.Generate(senseKey, floor, new Random(100 + floor));

            Assert.True(layout.StartRoom.IsActivated);
            Assert.True(layout.StartRoom.IsCleared);
            Assert.False(layout.StartRoom.IsCombatRoom);
            Assert.InRange(layout.TreasureRooms.Count, 0, 3);
            Assert.Equal(PathRoomType.Boss, layout.BossRoom.Type);
            Assert.Equal(layout.Battleground.Width / 2, layout.BossRoom.TileBounds.Center.X);
            Assert.Equal(layout.Battleground.Height / 2, layout.BossRoom.TileBounds.Center.Y);
            Assert.Equal(senseKey, layout.Battleground.VisualThemeKey);
            Assert.Equal(floor, layout.Battleground.PathFloorNumber);
            Assert.NotEmpty(layout.Decorations);
            Assert.False(layout.Battleground.RectHitsWall(new Rectangle(
                (int)layout.Battleground.SpawnPosition.X,
                (int)layout.Battleground.SpawnPosition.Y,
                30, 30)));
        }
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void Generate_RequiresSevenSerialCombatModulesBeforeBoss(string senseKey)
    {
        for (int seed = 0; seed < 12; seed++)
        {
            var layout = PathFloorGenerator.Generate(
                senseKey,
                1 + seed % 10,
                new Random(seed * 997 + 41));

            Assert.Equal(9, layout.MainRouteRooms.Count);
            Assert.Equal(7, layout.RequiredRoomsBeforeBoss.Count);
            Assert.All(layout.RequiredRoomsBeforeBoss, room =>
            {
                Assert.True(room.IsMainPath);
                Assert.True(room.IsCombatRoom);
                Assert.InRange(room.Depth, 1, 7);
                Assert.Equal(senseKey, room.ThemeKey);
                Assert.NotEqual(room.ShapeDisplayName, room.DungeonDisplayName);
            });
            Assert.Equal(7,
                layout.RequiredRoomsBeforeBoss.Select(room => room.Shape).Distinct().Count());
            Assert.Equal(8, layout.BossRoom.Depth);

            for (int index = 0; index + 1 < layout.MainRouteRooms.Count; index++)
            {
                PathRoom from = layout.MainRouteRooms[index];
                PathRoom to = layout.MainRouteRooms[index + 1];
                Assert.Contains(layout.Connections, connection =>
                    connection.FromRoomId == from.Id
                    && connection.ToRoomId == to.Id
                    && !connection.Hidden);
            }

            for (int left = 0; left < layout.MainRouteRooms.Count; left++)
            {
                for (int right = left + 1; right < layout.MainRouteRooms.Count; right++)
                {
                    PathRoom leftRoom = layout.MainRouteRooms[left];
                    PathRoom rightRoom = layout.MainRouteRooms[right];
                    Assert.False(leftRoom.TileBounds.Intersects(rightRoom.TileBounds),
                        $"{senseKey} seed {seed}, {layout.Style}: room {leftRoom.Id} "
                        + $"{leftRoom.TileBounds} overlaps room {rightRoom.Id} "
                        + $"{rightRoom.TileBounds}");
                }
            }
        }
    }

    [Fact]
    public void Generate_ShufflesTheCompleteRoomModuleLibraryAcrossFloors()
    {
        var sequences = new HashSet<string>();
        var shapes = new HashSet<PathRoomShape>();
        for (int seed = 0; seed < 40; seed++)
        {
            var layout = PathFloorGenerator.Generate(
                "phantasia",
                1 + seed % 10,
                new Random(seed));
            PathRoomShape[] sequence = layout.RequiredRoomsBeforeBoss
                .Select(room => room.Shape)
                .ToArray();
            sequences.Add(string.Join(',', sequence));
            shapes.UnionWith(sequence);
        }

        Assert.True(sequences.Count >= 24);
        Assert.Equal(
            Enum.GetValues<PathRoomShape>()
                .Where(shape => shape != PathRoomShape.Sanctuary)
                .Order(),
            shapes.Order());
    }

    [Fact]
    public void RollTreasureRoomCount_UsesChainedHalfChanceAndCapsAtThree()
    {
        Assert.Equal(0, PathFloorGenerator.RollTreasureRoomCount(
            new ChainedRollRandom(.5)));
        Assert.Equal(1, PathFloorGenerator.RollTreasureRoomCount(
            new ChainedRollRandom(.49, .5)));
        Assert.Equal(2, PathFloorGenerator.RollTreasureRoomCount(
            new ChainedRollRandom(.1, .1, .5)));
        Assert.Equal(3, PathFloorGenerator.RollTreasureRoomCount(
            new ChainedRollRandom(.1, .1, .1)));
    }

    [Fact]
    public void Generate_ProducesZeroThroughThreeTreasureBranchesAcrossSeeds()
    {
        var counts = Enumerable.Range(0, 500)
            .Select(seed => PathFloorGenerator.Generate(
                "touch", 4, new Random(seed)).TreasureRooms.Count)
            .ToHashSet();

        Assert.Equal(new[] { 0, 1, 2, 3 }, counts.Order());
    }

    [Fact]
    public void Generate_AllSpecialRoomsAreReachableFromStart()
    {
        var layout = PathFloorGenerator.Generate("phantasia", 8, new Random(77));
        RevealAllTreasures(layout);
        var battleground = layout.Battleground;
        var start = layout.StartRoom.TileBounds.Center;
        var visited = new HashSet<Point> { start };
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        var steps = new[] { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) };

        while (queue.Count > 0)
        {
            Point point = queue.Dequeue();
            foreach (Point step in steps)
            {
                var next = new Point(point.X + step.X, point.Y + step.Y);
                if (next.X < 0 || next.X >= battleground.Width || next.Y < 0 || next.Y >= battleground.Height
                    || battleground.TileAt(next.X, next.Y).IsSolid() || !visited.Add(next))
                    continue;
                queue.Enqueue(next);
            }
        }

        Assert.Contains(layout.BossRoom.TileBounds.Center, visited);
        Assert.All(layout.TreasureRooms, room => Assert.Contains(room.TileBounds.Center, visited));
    }

    [Fact]
    public void Generate_IsReproducibleForASeed()
    {
        var left = PathFloorGenerator.Generate("chemesthesis", 6, new Random(42));
        var right = PathFloorGenerator.Generate("chemesthesis", 6, new Random(42));

        Assert.Equal(left.Rooms.Select(room => (room.Type, room.TileBounds)),
            right.Rooms.Select(room => (room.Type, room.TileBounds)));
        Assert.Equal(left.Decorations, right.Decorations);
        for (int y = 0; y < left.Battleground.Height; y++)
            for (int x = 0; x < left.Battleground.Width; x++)
                Assert.Equal(left.Battleground.TileAt(x, y), right.Battleground.TileAt(x, y));
    }

    [Fact]
    public void Generate_AssignsSemanticRoomBiomesInsteadOfGlobalWedges()
    {
        PathFloorLayout? layout = null;
        for (int seed = 0; seed < 50 && layout is null; seed++)
        {
            var candidate = PathFloorGenerator.Generate("touch", 2, new Random(seed));
            if (candidate.TreasureRooms.Count > 0)
                layout = candidate;
        }
        Assert.NotNull(layout);

        Assert.Equal(0, layout!.Battleground.BiomeForTile(
            layout.StartRoom.TileBounds.Center.X, layout.StartRoom.TileBounds.Center.Y));
        Assert.Equal(1, layout.Battleground.BiomeForTile(
            layout.TreasureRooms[0].TileBounds.Center.X, layout.TreasureRooms[0].TileBounds.Center.Y));
        Assert.Equal(2, layout.Battleground.BiomeForTile(
            layout.BossRoom.TileBounds.Center.X, layout.BossRoom.TileBounds.Center.Y));
    }

    [Fact]
    public void FindSpawnRect_StaysInsideRequestedRoom()
    {
        var layout = PathFloorGenerator.Generate("sight", 4, new Random(5));
        var room = layout.Rooms.First(value => value.Type == PathRoomType.Assault);

        for (int index = 0; index < 50; index++)
        {
            var spawn = layout.FindSpawnRect(room, 32, new Random(index));
            Assert.True(room.WorldBounds.Contains(spawn.Center));
            Assert.False(layout.Battleground.RectHitsWall(spawn));
        }
    }

    [Fact]
    public void Generate_UsesMultipleMacroLayoutsAndLargeNonRectangularRooms()
    {
        var styles = new HashSet<PathLayoutStyle>();
        var shapes = new HashSet<PathRoomShape>();
        int widest = 0, tallest = 0;
        for (int seed = 0; seed < 80; seed++)
        {
            foreach (string senseKey in GamePaths.Paths.Select(path => path.Key))
            {
                var layout = PathFloorGenerator.Generate(senseKey, 4, new Random(seed));
                styles.Add(layout.Style);
                foreach (var room in layout.Rooms.Where(room => room.IsCombatRoom))
                {
                    shapes.Add(room.Shape);
                    widest = Math.Max(widest, room.TileBounds.Width);
                    tallest = Math.Max(tallest, room.TileBounds.Height);
                }
            }
        }

        Assert.Equal(Enum.GetValues<PathLayoutStyle>().Order(), styles.Order());
        Assert.Contains(PathRoomShape.LongHall, shapes);
        Assert.Contains(PathRoomShape.GrandArena, shapes);
        Assert.Contains(PathRoomShape.Maze, shapes);
        Assert.Contains(PathRoomShape.Crossroads, shapes);
        Assert.True(widest >= 17);
        Assert.True(tallest >= 17);
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void Generate_AllRoomsRemainReachableAcrossLayoutSeeds(string senseKey)
    {
        for (int seed = 0; seed < 35; seed++)
        {
            var layout = PathFloorGenerator.Generate(senseKey, 7, new Random(seed));
            RevealAllTreasures(layout);
            var battleground = layout.Battleground;
            var start = layout.StartRoom.TileBounds.Center;
            var visited = new HashSet<Point> { start };
            var queue = new Queue<Point>();
            queue.Enqueue(start);
            ReadOnlySpan<Point> steps =
                [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

            while (queue.Count > 0)
            {
                Point point = queue.Dequeue();
                foreach (Point step in steps)
                {
                    var next = new Point(point.X + step.X, point.Y + step.Y);
                    if (next.X < 0 || next.X >= battleground.Width
                        || next.Y < 0 || next.Y >= battleground.Height
                        || battleground.TileAt(next.X, next.Y).IsSolid()
                        || !visited.Add(next))
                    {
                        continue;
                    }
                    queue.Enqueue(next);
                }
            }

            Assert.All(layout.Rooms, room =>
                Assert.Contains(room.TileBounds.Center, visited));
        }
    }

    [Theory]
    [InlineData("touch", PathCorridorStyle.SewerConduit, 3)]
    [InlineData("sight", PathCorridorStyle.TidalCauseway, 5)]
    [InlineData("sound", PathCorridorStyle.CloudBridge, 3)]
    [InlineData("phantasia", PathCorridorStyle.Starwalk, 3)]
    [InlineData("chemesthesis", PathCorridorStyle.Rupture, 5)]
    public void Generate_CorridorsUseSenseSpecificTraversalGrammar(
        string senseKey, PathCorridorStyle expectedStyle, int expectedWidth)
    {
        var layout = PathFloorGenerator.Generate(senseKey, 4, new Random(47));

        Assert.NotEmpty(layout.Connections);
        Assert.All(layout.Connections, connection =>
        {
            Assert.Equal(expectedStyle, connection.Style);
            Assert.Equal(expectedWidth, connection.Width);
            Assert.NotNull(connection.Route);
            Assert.True(connection.Route!.Count >= 2);
        });
    }

    [Fact]
    public void Generate_AddsOptionalRewardChallengeBranches()
    {
        PathRoom? challenge = null;
        for (int seed = 0; seed < 30 && challenge is null; seed++)
        {
            challenge = PathFloorGenerator.Generate("chemesthesis", 6, new Random(seed))
                .Rooms.FirstOrDefault(room => room.Type == PathRoomType.Challenge);
        }

        Assert.NotNull(challenge);
        Assert.False(challenge!.IsMainPath);
        Assert.True(challenge.IsCombatRoom);
    }

    [Fact]
    public void FindEncounterSpawnRect_UsesHallLengthAndArenaPerimeter()
    {
        PathFloorLayout? hallLayout = null;
        PathRoom? hall = null;
        for (int seed = 0; seed < 20 && hall is null; seed++)
        {
            hallLayout = PathFloorGenerator.Generate("touch", 3, new Random(seed));
            hall = hallLayout.Rooms.FirstOrDefault(room =>
                room.IsCombatRoom && room.Shape == PathRoomShape.LongHall);
        }
        Assert.NotNull(hallLayout);
        Assert.NotNull(hall);

        var hallSpawns = Enumerable.Range(0, 8)
            .Select(index => hallLayout!.FindEncounterSpawnRect(
                hall!, 30, index, 8, new Random(index)))
            .ToList();
        int xSpan = hallSpawns.Max(rect => rect.Center.X) - hallSpawns.Min(rect => rect.Center.X);
        int ySpan = hallSpawns.Max(rect => rect.Center.Y) - hallSpawns.Min(rect => rect.Center.Y);
        Assert.True(Math.Max(xSpan, ySpan) >= Battleground.TileSize * 8);

        var arena = hallLayout.Rooms.First(room => room.Shape == PathRoomShape.GrandArena);
        var arenaSpawns = Enumerable.Range(0, 8)
            .Select(index => hallLayout.FindEncounterSpawnRect(
                arena, 30, index, 8, new Random(100 + index)))
            .ToList();
        Assert.True(arenaSpawns.Select(rect => rect.Center).Distinct().Count() >= 6);
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void Generate_RoutesDoNotCutThroughUnrelatedRooms(string senseKey)
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var layout = PathFloorGenerator.Generate(senseKey, 6, new Random(seed));
            foreach (var connection in layout.Connections)
            {
                Assert.NotNull(connection.Route);
                foreach (var room in layout.Rooms.Where(room =>
                    room.Id != connection.FromRoomId && room.Id != connection.ToRoomId))
                {
                    var intersections = connection.Route!
                        .Where(point => room.ContainsInteriorTile(point.X, point.Y))
                        .ToList();
                    Assert.True(intersections.Count == 0,
                        $"{senseKey} seed {seed}, {layout.Style}, connection "
                        + $"{connection.FromRoomId}->{connection.ToRoomId} crossed room {room.Id} "
                        + $"at {string.Join(", ", intersections.Take(4))}");
                }
            }
        }
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void Generate_BossArenaVariantsPreserveControllerAndTwoPlayerSpace(
        string senseKey)
    {
        var variants = new HashSet<PathBossArenaVariant>();
        for (int seed = 0; seed < 30; seed++)
        {
            var layout = PathFloorGenerator.Generate(
                senseKey,
                1 + seed % 10,
                new Random(seed));
            variants.Add(layout.BossArenaVariant);
            PathBossArenaSafetyReport safety =
                PathFloorGenerator.EvaluateBossArenaSafety(layout);

            Assert.True(safety.CenterClear);
            Assert.Equal(4, safety.CardinalLanes);
            Assert.InRange(
                safety.OpenTiles - safety.ConnectedOpenTiles,
                0,
                4);
            Assert.True(safety.SupportsControllerTraversal);
            Assert.True(safety.SupportsTwoPlayerSpacing);
            Assert.True(safety.SafePocketCount >= 4);

            int interiorObstacles = 0;
            Rectangle inner = layout.BossRoom.InteriorTileBounds;
            for (int y = inner.Top; y < inner.Bottom; y++)
                for (int x = inner.Left; x < inner.Right; x++)
                    if (layout.BossRoom.ContainsInteriorTile(x, y)
                        && layout.Battleground.TileAt(x, y).IsSolid())
                    {
                        interiorObstacles++;
                    }
            Assert.True(interiorObstacles >= 4);
        }

        Assert.Equal(2, variants.Count);
    }
}
