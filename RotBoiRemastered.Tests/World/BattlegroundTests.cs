using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

/// <summary>
/// Ported from tests/test_battleground_generation.py, the spawn/collision
/// coverage in tests/test_background_spawn.py (adapted to a slightly larger
/// fixture since TileSize is a real 50px constant here rather than a
/// monkey-patchable global the Python test could shrink to 10px), and the
/// map-silhouette checks from tests/test_game_paths.py.
/// </summary>
public class BattlegroundTests
{
    private static Battleground SmallOpenRoom()
    {
        // 5x5 grid: 1-tile wall border around a 3x3 open interior.
        var tiles = new TileType[5, 5];
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                tiles[y, x] = (x == 0 || x == 4 || y == 0 || y == 4) ? TileType.ArenaWall : TileType.Default;
        return new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);
    }

    [Fact]
    public void Sound_HasCircularSolidBoundary_AndOpenCenter()
    {
        var battleground = Battleground.GenerateSound(61);
        int center = battleground.Height / 2;
        Assert.True(battleground.TileAt(0, 0).IsSolid());
        Assert.True(battleground.TileAt(battleground.Width - 1, 0).IsSolid());
        Assert.Equal(TileType.Road, battleground.TileAt(center, center));
    }

    [Fact]
    public void Sound_ContainsBuildings_WithTwoSidedPassages()
    {
        var battleground = Battleground.GenerateSound(97);
        var counts = new Dictionary<TileType, int>();
        for (int y = 0; y < battleground.Height; y++)
        {
            for (int x = 0; x < battleground.Width; x++)
            {
                var tile = battleground.TileAt(x, y);
                counts[tile] = counts.GetValueOrDefault(tile) + 1;
            }
        }

        Assert.True(counts.GetValueOrDefault(TileType.BuildingWall) > 100);
        Assert.True(counts.GetValueOrDefault(TileType.BuildingFloor) > 200);
        Assert.True(counts.GetValueOrDefault(TileType.Road) > 100);
    }

    [Fact]
    public void FindSpawnRect_AvoidsWalls()
    {
        var battleground = SmallOpenRoom();
        var spawnRect = battleground.FindSpawnRect(size: 40, playerWorldPosition: Vector2.Zero, rng: new Random(1));
        Assert.False(battleground.RectHitsWall(spawnRect));
    }

    [Fact]
    public void FindNearestOpenRect_EscapesWallOverlap()
    {
        var battleground = SmallOpenRoom();
        // Fully inside the wall tile at grid (0, 2) -- left edge, middle row.
        var overlapping = new Rectangle(10, 110, 30, 30);
        var safe = battleground.FindNearestOpenRect(overlapping);
        Assert.False(battleground.RectHitsWall(safe));
    }

    [Fact]
    public void FindNearestOpenRect_PrefersSmallestOffset()
    {
        var battleground = SmallOpenRoom();
        var overlapping = new Rectangle(10, 110, 30, 30);
        var safe = battleground.FindNearestOpenRect(overlapping);
        Assert.True(Math.Abs(safe.X - overlapping.X) <= Battleground.TileSize);
        Assert.True(Math.Abs(safe.Y - overlapping.Y) <= Battleground.TileSize);
    }

    [Fact]
    public void ConvexPolygonHitsWall_DetectsRotatedCornerOverlap()
    {
        var battleground = SmallOpenRoom();
        var polygon = new[]
        {
            new Vector2(48, 75), new Vector2(75, 48),
            new Vector2(102, 75), new Vector2(75, 102),
        };

        Assert.True(battleground.ConvexPolygonHitsWall(polygon));
    }

    [Fact]
    public void ScreenAlignedRectangleHitsWall_MatchesExplicitRotatedFootprint()
    {
        var battleground = SmallOpenRoom();
        var camera = new Camera();
        camera.SetAngle(37);

        foreach (Vector2 anchor in new[]
                 {
                     new Vector2(48, 75),
                     new Vector2(75, 75),
                     new Vector2(125, 125),
                 })
        {
            const float size = 30;
            var polygon = new[]
            {
                anchor,
                anchor + camera.ScreenVectorToWorld(new Vector2(size, 0)),
                anchor + camera.ScreenVectorToWorld(new Vector2(size, size)),
                anchor + camera.ScreenVectorToWorld(new Vector2(0, size)),
            };

            Assert.Equal(
                battleground.ConvexPolygonHitsWall(polygon),
                battleground.ScreenAlignedRectangleHitsWall(
                    anchor, size, size, camera));
        }
    }

    [Fact]
    public void FindPathAroundWalls_ReturnsSafeStep()
    {
        var battleground = SmallOpenRoom();
        var worldRect = new Rectangle(60, 60, 30, 30); // fully within the open interior
        var safe = battleground.FindPathAroundWalls(worldRect, 0, 50, 30);
        Assert.False(battleground.RectHitsWall(safe));
    }

    [Fact]
    public void MapProfiles_HaveDistinctStructuralSilhouettes()
    {
        var sight = Battleground.GenerateSight();
        var chemesthesis = Battleground.GenerateChemesthesis();
        var phantasia = Battleground.GeneratePhantasia();

        static int Count(Battleground battleground, TileType tile)
        {
            int total = 0;
            for (int y = 0; y < battleground.Height; y++)
                for (int x = 0; x < battleground.Width; x++)
                    if (battleground.TileAt(x, y) == tile)
                        total++;
            return total;
        }

        Assert.Equal(0, Count(sight, TileType.BuildingFloor) + Count(sight, TileType.BuildingWall));
        Assert.Equal(0, Count(chemesthesis, TileType.BuildingFloor));
        Assert.True(Count(chemesthesis, TileType.BuildingWall) > 20);
        Assert.True(Count(phantasia, TileType.BuildingFloor) > 100);
        Assert.True(Count(phantasia, TileType.BuildingWall) > 80);
        Assert.True(phantasia.Height > sight.Height);
    }

    [Fact]
    public void Touch_IsDenserThanSound_AndKeepsASafeOpenCistern()
    {
        var sound = Battleground.GenerateSound(87);
        var touch = Battleground.GenerateTouch(87);

        static int WallCount(Battleground battleground)
        {
            int total = 0;
            for (int y = 0; y < battleground.Height; y++)
                for (int x = 0; x < battleground.Width; x++)
                    if (battleground.TileAt(x, y).IsRaised())
                        total++;
            return total;
        }

        int center = touch.Height / 2;
        Assert.True(WallCount(touch) > WallCount(sound));
        Assert.Equal(TileType.Road, touch.TileAt(center, center));
    }

    [Fact]
    public void Soul_IsAChapelTransitionAndFiveBranchCrown()
    {
        var soul = Battleground.GenerateSoul();
        int spawnTileX = (int)((soul.SpawnPosition.X + Battleground.TileSize / 2f) / Battleground.TileSize);
        int spawnTileY = (int)((soul.SpawnPosition.Y + Battleground.TileSize / 2f) / Battleground.TileSize);

        Assert.Equal(SoulLayout.SpawnTile.Y, spawnTileY);
        Assert.False(soul.TileAt(spawnTileX, spawnTileY).IsSolid());
        Assert.Equal(TileType.Road, soul.TileAt(SoulLayout.TunnelSouthTile.X, SoulLayout.TunnelSouthTile.Y));
        Assert.False(soul.TileAt(SoulLayout.NexusTile.X, SoulLayout.NexusTile.Y).IsSolid());
        Assert.All(SoulLayout.StationTiles.Values,
            tile => Assert.False(soul.TileAt(tile.X, tile.Y).IsSolid()));
        Assert.All(SoulLayout.PortalTiles.Values,
            tile => Assert.False(soul.TileAt(tile.X, tile.Y).IsSolid()));
        Assert.False(soul.TileAt(SoulLayout.DummyTile.X, SoulLayout.DummyTile.Y).IsSolid());
        Assert.True(soul.TileAt(20, 30).IsSolid());
    }

    [Fact]
    public void Soul_AllDestinationsAreReachableWithoutAStartupHike()
    {
        var soul = Battleground.GenerateSoul();

        Assert.InRange(ShortestPath(soul, SoulLayout.SpawnTile, SoulLayout.NexusTile), 1, 23);
        foreach (Point portal in SoulLayout.PortalTiles.Values)
            Assert.InRange(ShortestPath(soul, SoulLayout.SpawnTile, portal), 1, 52);
        foreach (Point station in SoulLayout.StationTiles.Values)
            Assert.InRange(ShortestPath(soul, SoulLayout.SpawnTile, station), 0, 18);
        Assert.InRange(ShortestPath(soul, SoulLayout.SpawnTile, SoulLayout.DummyTile), 1, 18);
    }

    [Fact]
    public void Soul_PathAlcovesRemainDistinctBeyondTheSharedNexus()
    {
        Point[] portals = SoulLayout.PortalTiles.Values.ToArray();
        for (int left = 0; left < portals.Length; left++)
            for (int right = left + 1; right < portals.Length; right++)
                Assert.True(Vector2.Distance(portals[left].ToVector2(), portals[right].ToVector2()) >= 6f);
    }

    [Fact]
    public void Soul_SelectionCrownUsesDoubledAuthoredPortalVectors()
    {
        Assert.Equal(2, SoulLayout.SelectionAreaScale);
        foreach (var (key, offset) in SoulLayout.PortalOffsets)
        {
            Point portal = SoulLayout.PortalTiles[key];
            Assert.Equal(
                new Point(
                    SoulLayout.NexusTile.X
                        + offset.X * SoulLayout.SelectionAreaScale,
                    SoulLayout.NexusTile.Y
                        + offset.Y * SoulLayout.SelectionAreaScale),
                portal);
        }
    }

    [Fact]
    public void Mind_AuthoredGeometryKeepsEverySurfaceAwayFromTheMapBoundary()
    {
        TileType[,] tiles = SoulLayout.BuildTiles(new HashSet<string>());

        for (int y = 0; y < tiles.GetLength(0); y++)
        {
            for (int x = 0; x < tiles.GetLength(1); x++)
            {
                if (tiles[y, x] == TileType.OuterVoid)
                    continue;
                Assert.True(x >= SoulLayout.MinimumBoundaryBufferTiles);
                Assert.True(y >= SoulLayout.MinimumBoundaryBufferTiles);
                Assert.True(x < tiles.GetLength(1) - SoulLayout.MinimumBoundaryBufferTiles);
                Assert.True(y < tiles.GetLength(0) - SoulLayout.MinimumBoundaryBufferTiles);
            }
        }
    }

    [Fact]
    public void Mind_LockedGateWallsOnlyReplaceAuthoredWalkableCorridorTiles()
    {
        TileType[,] open = SoulLayout.BuildTiles(SoulLayout.AllGateKeys);
        TileType[,] locked = SoulLayout.BuildTiles(new HashSet<string>());

        for (int y = 0; y < locked.GetLength(0); y++)
        {
            for (int x = 0; x < locked.GetLength(1); x++)
            {
                if (locked[y, x] == open[y, x])
                    continue;
                Assert.False(open[y, x].IsSolid());
                Assert.Equal(TileType.BuildingWall, locked[y, x]);
            }
        }
    }

    [Fact]
    public void Mind_EachGateIndependentlySealsOnlyItsOwnArenaWing()
    {
        foreach (string unlockedSense in SoulLayout.PortalTiles.Keys)
        {
            TileType[,] tiles = SoulLayout.BuildTiles(new HashSet<string> { unlockedSense });
            HashSet<Point> reachable = ReachableTiles(tiles, SoulLayout.SpawnTile);

            Assert.Contains(SoulLayout.NexusTile, reachable);
            foreach ((string sense, Point portal) in SoulLayout.PortalTiles)
                Assert.Equal(sense == unlockedSense, reachable.Contains(portal));
        }
    }

    [Fact]
    public void Mind_FogRevealsTheGateButConcealsEveryLockedArenaWing()
    {
        foreach (string unlockedSense in SoulLayout.PortalTiles.Keys)
        {
            TileType[,] tiles = SoulLayout.BuildTiles(new HashSet<string> { unlockedSense });
            var battleground = new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);
            var fog = new PathFogOfWar(battleground);
            HashSet<Point> reachable = ReachableTiles(tiles, SoulLayout.SpawnTile);

            // Model a thorough visit to every place the player can physically
            // reach. A sealed wing must remain completely undiscovered even
            // after the rest of The Mind has been explored.
            foreach (Point tile in reachable)
                fog.Update(SoulLayout.TileWorldCenter(tile));

            foreach ((string sense, Point portal) in SoulLayout.PortalTiles)
                Assert.Equal(sense == unlockedSense, fog.IsExplored(portal.X, portal.Y));
        }
    }

    [Fact]
    public void Mind_EndgameGatesFormASequentialSightCoreAphantasiaSpine()
    {
        Assert.Equal(SoulLayout.PortalTiles["sight"].X, SoulLayout.CorePortalTile.X);
        Assert.Equal(SoulLayout.CorePortalTile.X, SoulLayout.AphantasiaPortalTile.X);
        Assert.True(SoulLayout.PortalTiles["sight"].Y > SoulLayout.CorePortalTile.Y);
        Assert.True(SoulLayout.CorePortalTile.Y > SoulLayout.AphantasiaPortalTile.Y);

        var sensesOnly = SoulLayout.PortalTiles.Keys.ToHashSet();
        HashSet<Point> beforeCore = ReachableTiles(
            SoulLayout.BuildTiles(sensesOnly), SoulLayout.SpawnTile);
        Assert.Contains(SoulLayout.PortalTiles["sight"], beforeCore);
        Assert.DoesNotContain(SoulLayout.CorePortalTile, beforeCore);
        Assert.DoesNotContain(SoulLayout.AphantasiaPortalTile, beforeCore);

        sensesOnly.Add("core");
        HashSet<Point> beforeAphantasia = ReachableTiles(
            SoulLayout.BuildTiles(sensesOnly), SoulLayout.SpawnTile);
        Assert.Contains(SoulLayout.CorePortalTile, beforeAphantasia);
        Assert.DoesNotContain(SoulLayout.AphantasiaPortalTile, beforeAphantasia);

        sensesOnly.Add("aphantasia");
        HashSet<Point> fullyOpen = ReachableTiles(
            SoulLayout.BuildTiles(sensesOnly), SoulLayout.SpawnTile);
        Assert.Contains(SoulLayout.AphantasiaPortalTile, fullyOpen);
    }

    private static HashSet<Point> ReachableTiles(TileType[,] tiles, Point start)
    {
        int height = tiles.GetLength(0);
        int width = tiles.GetLength(1);
        var visited = new HashSet<Point> { start };
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        Point[] directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        };
        while (queue.Count > 0)
        {
            Point tile = queue.Dequeue();
            foreach (Point direction in directions)
            {
                Point next = tile + direction;
                if (next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= height
                    || tiles[next.Y, next.X].IsSolid() || !visited.Add(next))
                    continue;
                queue.Enqueue(next);
            }
        }
        return visited;
    }

    private static int ShortestPath(Battleground battleground, Point start, Point destination)
    {
        var queue = new Queue<(Point Tile, int Distance)>();
        var visited = new HashSet<Point> { start };
        queue.Enqueue((start, 0));
        Point[] directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        };
        while (queue.Count > 0)
        {
            var (tile, distance) = queue.Dequeue();
            if (tile == destination)
                return distance;
            foreach (Point direction in directions)
            {
                Point next = tile + direction;
                if (next.X < 0 || next.Y < 0
                    || next.X >= battleground.Width || next.Y >= battleground.Height
                    || battleground.TileAt(next.X, next.Y).IsSolid()
                    || !visited.Add(next))
                    continue;
                queue.Enqueue((next, distance + 1));
            }
        }
        return -1;
    }
}
