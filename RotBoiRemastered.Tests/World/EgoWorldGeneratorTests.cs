using System.Diagnostics;
using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

public sealed class EgoWorldGeneratorTests
{
    private const float View = 1920f;

    [Fact]
    public void AllThreeTerrainsAppearAndBordersAreDithered()
    {
        var world = EgoWorldGenerator.Build(new Random(5), View);
        var field = world.Field;
        Assert.Contains(field.Seeds, seed => seed.Terrain == EgoTerrain.Plains);
        Assert.Contains(field.Seeds, seed => seed.Terrain == EgoTerrain.City);
        Assert.Contains(field.Seeds, seed => seed.Terrain == EgoTerrain.Caverns);
        Assert.Equal(EgoTerrain.Plains, field.TerrainAt(EgoWorldGenerator.Width / 2, EgoWorldGenerator.Height / 2));

        var effective = EgoWorldGenerator.EffectiveTerrain(field, new Random(1));
        int handedOver = 0, band = 0;
        for (int y = 0; y < EgoWorldGenerator.Height; y++)
            for (int x = 0; x < EgoWorldGenerator.Width; x++)
            {
                if (field.BorderBlend(x, y) <= 0f || field.TerrainAt(x, y) == field.SecondTerrainAt(x, y))
                    continue;
                band++;
                if (effective[y, x] != field.TerrainAt(x, y))
                    handedOver++;
            }
        Assert.True(band > 1000, "expected a wide border band");
        Assert.InRange(handedOver / (double)band, .05, .45);
    }

    [Fact]
    public void EveryHoldoutIsReachableFromSpawnAndSenseTinted()
    {
        var world = EgoWorldGenerator.Build(new Random(9), View);
        Battleground map = world.Battleground;
        var reachable = FloodFrom(map, new Point(map.Width / 2, map.Height / 2));
        foreach (EgoRegion holdout in world.Regions.Where(region => region.IsHoldout))
        {
            int tx = (int)(holdout.Center.X / Battleground.TileSize), ty = (int)(holdout.Center.Y / Battleground.TileSize);
            Assert.True(reachable[ty, tx], $"{holdout.SenseKey} holdout at {tx},{ty} is cut off");
            int expected = BiomePalettes.EgoSensePaletteOffset + Array.IndexOf(CampaignProgression.SenseKeys, holdout.SenseKey);
            Assert.Equal(expected, map.BiomeForTile(tx, ty));
        }
        // Outside every holdout the palette is one of the three terrains.
        Vector2 spawn = map.SpawnPosition;
        Assert.InRange(map.BiomeForTile((int)(spawn.X / Battleground.TileSize), (int)(spawn.Y / Battleground.TileSize)), 0, 2);
        Assert.NotEmpty(map.PathDecorations);
        Assert.Contains(map.PathDecorations, d => d.Layer == PathDecorationLayer.Ambient);
    }

    [Fact]
    public void BuildIsFastEnoughForRunStart()
    {
        var stopwatch = Stopwatch.StartNew();
        EgoWorldGenerator.Build(new Random(3), View);
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 4000, $"took {stopwatch.ElapsedMilliseconds} ms");
    }

    private static bool[,] FloodFrom(Battleground map, Point start)
    {
        var seen = new bool[map.Height, map.Width];
        var stack = new Stack<Point>();
        stack.Push(start);
        seen[start.Y, start.X] = true;
        while (stack.Count > 0)
        {
            Point p = stack.Pop();
            foreach (Point n in new[] { new Point(p.X - 1, p.Y), new Point(p.X + 1, p.Y), new Point(p.X, p.Y - 1), new Point(p.X, p.Y + 1) })
            {
                if (n.X < 0 || n.Y < 0 || n.X >= map.Width || n.Y >= map.Height || seen[n.Y, n.X] || map.TileAt(n.X, n.Y).IsSolid())
                    continue;
                seen[n.Y, n.X] = true;
                stack.Push(n);
            }
        }
        return seen;
    }
}

public sealed class EgoSpawnClearingTests
{
    [Fact]
    public void SpawnAndItsSurroundingsAreAlwaysOpenAcrossManySeeds()
    {
        for (int seed = 0; seed < 12; seed++)
        {
            var map = EgoWorldGenerator.Build(new Random(seed), 1920f).Battleground;
            int cx = map.Width / 2, cy = map.Height / 2;
            for (int y = cy - 2; y <= cy + 2; y++)
                for (int x = cx - 2; x <= cx + 2; x++)
                    Assert.False(map.TileAt(x, y).IsSolid(), $"seed {seed}: solid tile at {x},{y} beside spawn");
            Assert.False(map.RectHitsWall(new Microsoft.Xna.Framework.Rectangle(
                (int)map.SpawnPosition.X, (int)map.SpawnPosition.Y, 30, 30)));
        }
    }
}
