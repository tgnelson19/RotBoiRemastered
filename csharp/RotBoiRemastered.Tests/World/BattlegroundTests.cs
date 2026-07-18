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
}
