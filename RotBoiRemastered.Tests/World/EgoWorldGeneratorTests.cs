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
    public void TerrainPropsAvoidHoldoutsSpawnAndTraces()
    {
        var world = EgoWorldGenerator.Build(new Random(13), View);
        Battleground map = world.Battleground;
        var terrainProps = map.PathDecorations.Where(d => d.RoomId is >= 3000 and < 4000).ToList();
        Assert.True(terrainProps.Count > 200, $"expected the land to be dressed, got {terrainProps.Count}");
        Assert.Contains(terrainProps, d => d.Layer == PathDecorationLayer.Raised);
        Vector2 spawn = map.SpawnPosition;
        float clear = EgoThemeVisuals.TerrainPropSpawnClearTiles * Battleground.TileSize;
        foreach (PathDecoration prop in terrainProps)
        {
            Assert.DoesNotContain(world.Regions, r => r.IsHoldout && r.Contains(prop.WorldPosition));
            Assert.True(Vector2.Distance(prop.WorldPosition, spawn) > clear, "prop inside spawn clearing");
            int tx = (int)(prop.WorldPosition.X / Battleground.TileSize), ty = (int)(prop.WorldPosition.Y / Battleground.TileSize);
            Assert.False(map.TileAt(tx, ty).IsSolid(), "prop on a solid tile");
            foreach (EgoTrace trace in world.Traces.Where(t => t.Kind != EgoTraceKind.Monument))
                Assert.True(Math.Abs(trace.World.X - prop.WorldPosition.X) > Battleground.TileSize * EgoThemeVisuals.TerrainPropTraceClearTiles
                    || Math.Abs(trace.World.Y - prop.WorldPosition.Y) > Battleground.TileSize * EgoThemeVisuals.TerrainPropTraceClearTiles,
                    "prop on top of a trace");
        }
        // Deterministic for a seed.
        var again = EgoWorldGenerator.Build(new Random(13), View).Battleground.PathDecorations.Where(d => d.RoomId is >= 3000 and < 4000).ToList();
        Assert.Equal(terrainProps.Select(d => (d.Kind, d.WorldPosition)), again.Select(d => (d.Kind, d.WorldPosition)));
    }

    [Fact]
    public void EgoWallsCrumbleOnlyOnTerrainAndStayInRange()
    {
        var world = EgoWorldGenerator.Build(new Random(21), View);
        Battleground map = world.Battleground;
        int crumbled = 0, walls = 0;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.TileAt(x, y).IsRaised())
                    continue;
                int height = ArenaRenderer.WallHeightFor(map, x, y);
                Assert.Equal(height, ArenaRenderer.WallHeightFor(map, x, y));
                Assert.InRange(height, 4, map.WallHeight + 2);
                if (map.TileAt(x, y) != TileType.BuildingWall)
                    continue;
                string? theme = map.ThemeKeyForTile(x, y);
                if (theme is not ("ego_city" or "ego_plains"))
                {
                    Assert.Equal(map.WallHeight, height);
                    continue;
                }
                walls++;
                if (height < map.WallHeight)
                    crumbled++;
            }
        Assert.True(walls > 100);
        Assert.InRange(crumbled / (double)walls, .4, .7);
    }

    [Fact]
    public void MurkAlphaRampsOnlyInsideTheBand()
    {
        const int radius = 20;
        Assert.Equal(0, GameSession.EgoMurkAlpha(0, radius));
        Assert.Equal(0, GameSession.EgoMurkAlpha(radius - GameSession.EgoMurkBandTiles, radius));
        int mid = GameSession.EgoMurkAlpha(radius - GameSession.EgoMurkBandTiles / 2f, radius);
        int edge = GameSession.EgoMurkAlpha(radius, radius);
        Assert.True(mid > 0 && mid < edge);
        Assert.Equal(200, edge);
        Assert.Equal(200, GameSession.EgoMurkAlpha(radius + 5, radius));
    }

    [Fact]
    public void LandmarksAreUniqueReachableQuietAndOffHoldouts()
    {
        var world = EgoWorldGenerator.Build(new Random(31), View);
        Battleground map = world.Battleground;
        Assert.True(world.Landmarks.Count >= 3, $"expected most landmarks to place, got {world.Landmarks.Count}");
        Assert.Equal(world.Landmarks.Count, world.Landmarks.Select(l => l.Kind).Distinct().Count());
        var reachable = FloodFrom(map, new Point(map.Width / 2, map.Height / 2));
        foreach (EgoLandmark landmark in world.Landmarks)
        {
            int tx = (int)(landmark.Center.X / Battleground.TileSize), ty = (int)(landmark.Center.Y / Battleground.TileSize);
            Assert.True(reachable[ty, tx], $"{landmark.Kind} is cut off");
            Assert.True(Vector2.Distance(landmark.Center, map.SpawnPosition) >= View * EgoLandmarks.MinDistanceFromSpawnViews);
            Assert.DoesNotContain(world.Regions, r => r.IsHoldout && Vector2.Distance(r.Center, landmark.Center) < r.RadiusWorld + landmark.RadiusWorld);
            Assert.Contains(world.Traces, t => t.Kind == EgoTraceKind.Monument && t.LandmarkKind == landmark.Kind && t.World == landmark.Center);
            Assert.Contains(map.PathDecorations, d => d.RoomId == EgoLandmarks.DecorationRoomIdBase + (int)landmark.Kind && d.Layer == PathDecorationLayer.Raised);
        }
        Assert.All(world.Traces.Where(t => t.Kind == EgoTraceKind.Monument), t => Assert.False(string.IsNullOrEmpty(t.Inscription)));
    }

    [Fact]
    public void CachesGrottosAndFrontierGenerate()
    {
        var world = EgoWorldGenerator.Build(new Random(37), View);
        Battleground map = world.Battleground;
        var caches = world.Traces.Where(t => t.Kind == EgoTraceKind.Cache).ToList();
        Assert.True(caches.Count >= 4, $"caches: {caches.Count}");
        Assert.All(caches, c => Assert.Equal(TileType.Default,
            map.TileAt((int)(c.World.X / Battleground.TileSize), (int)(c.World.Y / Battleground.TileSize))));
        Assert.All(caches, c => Assert.DoesNotContain(world.Landmarks, l => l.Contains(c.World)));

        Assert.Contains(map.PathDecorations, d => d.Kind == PathDecorationKind.GlowFungus);
        Assert.All(map.PathDecorations.Where(d => d.Kind == PathDecorationKind.GlowFungus), d =>
            Assert.Equal(EgoTerrain.Caverns, world.Terrain[(int)(d.WorldPosition.Y / Battleground.TileSize), (int)(d.WorldPosition.X / Battleground.TileSize)]));

        var stakes = map.PathDecorations.Where(d => d.Kind == PathDecorationKind.WarningStake).ToList();
        Assert.True(stakes.Count > 20, $"stakes: {stakes.Count}"); // at 1920px views the ring mostly leaves the 400-tile map; only the corners are veteran space
        float radius = View * EgoWorldGenerator.VeteranMinDistanceViews;
        Assert.All(stakes, s => Assert.InRange(Vector2.Distance(s.WorldPosition, map.SpawnPosition), radius - Battleground.TileSize * 2, radius + Battleground.TileSize * 2));
        Assert.All(stakes, s => Assert.InRange(s.Variant, 0, CampaignProgression.SenseKeys.Length - 1));
    }

    [Fact]
    public void SightZonesHideInteriorsFromOutsideAndDemandRaysInCaverns()
    {
        var world = EgoWorldGenerator.Build(new Random(41), View);
        Battleground map = world.Battleground;
        byte[] zones = EgoSightZones.Build(map, world.Terrain);
        int interior = 0, cavern = 0;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                byte zone = zones[y * map.Width + x];
                if (map.TileAt(x, y) == TileType.BuildingFloor) Assert.Equal(EgoSightZones.Interior, zone);
                if (zone == EgoSightZones.Interior) interior++;
                if (zone == EgoSightZones.Cavern) cavern++;
            }
        Assert.True(interior > 0 && cavern > 0);

        // Stand on open ground beside a building: its floor stays dark, its wall shows.
        (int X, int Y)? door = null;
        for (int y = 2; y < map.Height - 2 && door is null; y++)
            for (int x = 2; x < map.Width - 2; x++)
                if (map.TileAt(x, y) == TileType.Default && zones[y * map.Width + x] == EgoSightZones.Open
                    && map.TileAt(x + 1, y).IsRaised() && map.TileAt(x + 2, y) == TileType.BuildingFloor)
                { door = (x, y); break; }
        Assert.NotNull(door);
        var (dx, dy) = door.Value;
        var fog = new PathFogOfWar(map, 12, zones);
        fog.Update(new Vector2((dx + .5f) * Battleground.TileSize, (dy + .5f) * Battleground.TileSize));
        Assert.True(fog.IsVisible(dx, dy));
        Assert.True(fog.IsVisible(dx + 1, dy), "outer wall visible from outside");
        Assert.False(fog.IsVisible(dx + 2, dy), "interior hidden from outside");
        // From inside, the same floor is seen.
        fog.Update(new Vector2((dx + 2.5f) * Battleground.TileSize, (dy + .5f) * Battleground.TileSize));
        Assert.True(fog.IsVisible(dx + 2, dy));
    }

    [Fact]
    public void OpenGroundIgnoresLineOfSightButCavernsDoNot()
    {
        const int size = 40;
        var tiles = new TileType[size, size];
        var terrain = new EgoTerrain[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                tiles[y, x] = edge ? TileType.OuterVoid : TileType.Default;
                terrain[y, x] = x >= size / 2 ? EgoTerrain.Caverns : EgoTerrain.Plains;
            }
        // A boulder on the plains and a rock in the caverns, both directly east of an observer.
        tiles[10, 6] = TileType.BuildingWall;
        tiles[10, 26] = TileType.BuildingWall;
        var map = new Battleground(tiles, BiomePalettes.Ego, 18, new Vector2(5.5f * Battleground.TileSize, 10.5f * Battleground.TileSize), "ego");
        var fog = new PathFogOfWar(map, 10, EgoSightZones.Build(map, terrain));
        fog.Update(new Vector2(5.5f * Battleground.TileSize, 10.5f * Battleground.TileSize));
        Assert.True(fog.IsVisible(8, 10), "plains tile behind a boulder is still seen");
        fog.Update(new Vector2(25.5f * Battleground.TileSize, 10.5f * Battleground.TileSize));
        Assert.True(fog.IsVisible(26, 10));
        Assert.False(fog.IsVisible(28, 10), "cavern tile behind a rock is hidden");
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
