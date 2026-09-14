using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.World;

/// <summary>
/// Set dressing for The Ego. Every sense holdout is taken over by that
/// sense's <see cref="PathThemeVisuals"/> vocabulary -- a large signature
/// crest at its heart, raised props, floor motifs and one ambient emitter --
/// so it reads as that sense's outpost from a distance and up close, whatever
/// terrain it sits in. Terrain itself gets only light dressing here; its
/// identity comes from the palette and per-tile floor detail.
/// </summary>
public static class EgoThemeVisuals
{
    public const float SignatureScale = 5f;

    public static IReadOnlyList<PathDecoration> GenerateDecorations(TileType[,] tiles,
        IReadOnlyList<EgoRegion> regions, EgoTerrain[,] terrain, Random rng)
    {
        var output = new List<PathDecoration>();
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        for (int index = 0; index < regions.Count; index++)
        {
            EgoRegion region = regions[index];
            if (!region.IsHoldout || region.SenseKey is null)
                continue;
            var profile = PathThemeVisuals.For(region.SenseKey);
            int roomId = 1000 + index;
            Point centerTile = new((int)(region.Center.X / Battleground.TileSize), (int)(region.Center.Y / Battleground.TileSize));

            // The heart: the sense's entrance crest, large, on the holdout center.
            output.Add(new PathDecoration(PathThemeVisuals.EntranceCrestFor(region.SenseKey),
                PathDecorationLayer.Floor, TileCenter(centerTile), SignatureScale, 0, roomId));

            int radiusTiles = (int)(region.RadiusWorld / Battleground.TileSize);
            int raised = region.IsVeteran ? rng.Next(7, 11) : rng.Next(4, 8);
            int floor = region.IsVeteran ? 4 : 2;
            var used = new HashSet<Point> { centerTile };

            Point? Spot(int minRadius, int maxRadius, bool needFloor)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2);
                    int distance = rng.Next(minRadius, maxRadius + 1);
                    Point tile = new(centerTile.X + (int)MathF.Round(MathF.Cos(angle) * distance),
                        centerTile.Y + (int)MathF.Round(MathF.Sin(angle) * distance));
                    if (tile.X < 2 || tile.Y < 2 || tile.X >= width - 2 || tile.Y >= height - 2)
                        continue;
                    if (used.Contains(tile) || tiles[tile.Y, tile.X].IsSolid())
                        continue;
                    if (needFloor && tiles[tile.Y, tile.X] == TileType.BuildingFloor)
                        continue;
                    used.Add(tile);
                    return tile;
                }
                return null;
            }

            for (int prop = 0; prop < raised; prop++)
            {
                if (Spot(3, Math.Max(4, radiusTiles - 1), needFloor: false) is Point tile)
                    output.Add(new PathDecoration(profile.RaisedProps[rng.Next(profile.RaisedProps.Count)],
                        PathDecorationLayer.Raised, TileCenter(tile), .95f + (float)rng.NextDouble() * .3f, rng.Next(4), roomId));
            }
            for (int motif = 0; motif < floor; motif++)
            {
                if (Spot(2, Math.Max(3, radiusTiles - 2), needFloor: false) is Point tile)
                {
                    var kind = profile.FloorMotifs[rng.Next(profile.FloorMotifs.Count)];
                    output.Add(new PathDecoration(kind, PathThemeVisuals.LayerFor(kind), TileCenter(tile),
                        1.2f + (float)rng.NextDouble() * .8f, rng.Next(4), roomId));
                }
            }
            // Veteran holdouts fly the sense's landmark on each flank of the crest.
            if (region.IsVeteran)
            {
                PathDecorationKind landmark = region.SenseKey switch
                {
                    "touch" => PathDecorationKind.PressureTank,
                    "sight" => PathDecorationKind.MirrorArch,
                    "sound" => PathDecorationKind.OrganStack,
                    "phantasia" => PathDecorationKind.LanternSpire,
                    _ => PathDecorationKind.FurnaceIdol,
                };
                foreach (Point offset in new[] { new Point(-4, -3), new Point(4, -3), new Point(-4, 3), new Point(4, 3) })
                {
                    Point tile = new(centerTile.X + offset.X, centerTile.Y + offset.Y);
                    if (tile.X < 2 || tile.Y < 2 || tile.X >= width - 2 || tile.Y >= height - 2 || tiles[tile.Y, tile.X].IsSolid())
                        continue;
                    output.Add(new PathDecoration(landmark, PathDecorationLayer.Raised, TileCenter(tile), 1.18f, rng.Next(4), roomId));
                }
            }
            output.Add(new PathDecoration(profile.AmbientEmitter, PathDecorationLayer.Ambient,
                TileCenter(centerTile), region.IsVeteran ? 1.6f : 1.1f, rng.Next(4), roomId));
        }
        return output;
    }

    /// <summary>Tiles around spawn kept clear of terrain props so the opening view stays readable.</summary>
    public const int TerrainPropSpawnClearTiles = EgoWorldGenerator.SpawnClearingRadius + 2;
    public const int TerrainPropTraceClearTiles = 2;

    /// <summary>
    /// The land between holdouts is abandoned, not empty: hash-scattered
    /// dead trees and slabs on the plains, barricades and broken columns in
    /// the city, the odd slab in the caverns, plus cracked earth underfoot.
    /// Placement is deterministic per tile from the region field so it is
    /// stable for a seed, never lands in a holdout, the spawn clearing, or
    /// on top of a trace.
    /// </summary>
    public static IReadOnlyList<PathDecoration> GenerateTerrainDecorations(TileType[,] tiles,
        EgoTerrain[,] terrain, IReadOnlyList<EgoRegion> regions, EgoRegionField field,
        IReadOnlyList<EgoTrace> traces, IReadOnlyList<EgoLandmark>? landmarks = null)
    {
        var output = new List<PathDecoration>();
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        var holdouts = regions.Where(region => region.IsHoldout).ToList();
        landmarks ??= Array.Empty<EgoLandmark>();
        var traceTiles = new HashSet<Point>(traces.Select(trace =>
            new Point((int)(trace.World.X / Battleground.TileSize), (int)(trace.World.Y / Battleground.TileSize))));
        int spawnX = width / 2, spawnY = height / 2;
        int clear2 = TerrainPropSpawnClearTiles * TerrainPropSpawnClearTiles;

        bool NearTrace(int x, int y)
        {
            for (int dy = -TerrainPropTraceClearTiles; dy <= TerrainPropTraceClearTiles; dy++)
                for (int dx = -TerrainPropTraceClearTiles; dx <= TerrainPropTraceClearTiles; dx++)
                    if (traceTiles.Contains(new Point(x + dx, y + dy)))
                        return true;
            return false;
        }
        bool Is(int x, int y, TileType type) => x >= 0 && y >= 0 && x < width && y < height && tiles[y, x] == type;
        bool Beside(int x, int y, Func<TileType, bool> test) =>
            (x > 0 && test(tiles[y, x - 1])) || (x + 1 < width && test(tiles[y, x + 1]))
            || (y > 0 && test(tiles[y - 1, x])) || (y + 1 < height && test(tiles[y + 1, x]));

        for (int y = 2; y < height - 2; y++)
            for (int x = 2; x < width - 2; x++)
            {
                TileType tile = tiles[y, x];
                if (tile.IsSolid() || tile == TileType.BuildingFloor)
                    continue;
                int dx = x - spawnX, dy = y - spawnY;
                if (dx * dx + dy * dy <= clear2)
                    continue;
                Vector2 world = TileCenter(new Point(x, y));
                if (holdouts.Any(holdout => holdout.Contains(world)) || landmarks.Any(landmark => landmark.Contains(world)))
                    continue;
                float roll = field.Hash(x, y, 77);
                PathDecorationKind? kind = terrain[y, x] switch
                {
                    EgoTerrain.Plains when tile == TileType.Default => roll switch
                    {
                        < .004f => PathDecorationKind.DeadTree,
                        < .0065f => PathDecorationKind.RuinSlab,
                        < .012f => PathDecorationKind.CrackedEarth,
                        _ => null,
                    },
                    EgoTerrain.City => roll switch
                    {
                        < .006f when tile == TileType.Road || Beside(x, y, t => t == TileType.Road) => PathDecorationKind.RustBarricade,
                        < .01f when tile != TileType.Road && Beside(x, y, t => t.IsRaised()) => PathDecorationKind.BrokenColumn,
                        < .017f when tile != TileType.Road => PathDecorationKind.RuinSlab,
                        < .027f when tile != TileType.Road => PathDecorationKind.CrackedEarth,
                        _ => null,
                    },
                    EgoTerrain.Caverns => roll < .003f ? PathDecorationKind.RuinSlab : null,
                    _ => null,
                };
                if (kind is null || NearTrace(x, y))
                    continue;
                float scale = .85f + field.Hash(x, y, 78) * .5f;
                int variant = (int)(field.Hash(x, y, 79) * 4);
                output.Add(new PathDecoration(kind.Value, PathThemeVisuals.LayerFor(kind.Value), world, scale, variant, 3000 + (y * width + x) % 1000));
            }
        return output;
    }

    public const int GrottoRoomIdBase = 5000;
    public const int FrontierRoomIdBase = 6000;
    /// <summary>Fraction of carved cavern rooms that become lit grottos.</summary>
    public const float GrottoChance = .3f;

    /// <summary>
    /// Rare lit pockets in the black: roughly a third of carved cavern rooms
    /// get a clump of glow fungus (a light source) and a still pool or two.
    /// </summary>
    public static IReadOnlyList<PathDecoration> GenerateGrottos(TileType[,] tiles, EgoTerrain[,] terrain, IReadOnlyList<Point> caveRooms,
        EgoRegionField field, IReadOnlyList<EgoRegion> regions, IReadOnlyList<EgoLandmark> landmarks)
    {
        var output = new List<PathDecoration>();
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        var holdouts = regions.Where(region => region.IsHoldout).ToList();
        for (int index = 0; index < caveRooms.Count; index++)
        {
            Point room = caveRooms[index];
            if (field.Hash(room.X, room.Y, 31) >= GrottoChance)
                continue;
            Vector2 center = TileCenter(room);
            if (holdouts.Any(h => h.Contains(center)) || landmarks.Any(l => l.Contains(center)))
                continue;
            int roomId = GrottoRoomIdBase + index;
            int fungi = 3 + (int)(field.Hash(room.X, room.Y, 32) * 3), pools = 1 + (int)(field.Hash(room.X, room.Y, 33) * 2);
            var used = new HashSet<Point>();
            for (int item = 0; item < fungi + pools; item++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 12 && !placed; attempt++)
                {
                    float a = field.Hash(room.X + item, room.Y + attempt, 34) * MathF.Tau;
                    float d = 1f + field.Hash(room.X + attempt, room.Y + item, 35) * 3f;
                    Point tile = new(room.X + (int)MathF.Round(MathF.Cos(a) * d), room.Y + (int)MathF.Round(MathF.Sin(a) * d));
                    if (tile.X < 2 || tile.Y < 2 || tile.X >= width - 2 || tile.Y >= height - 2
                        || tiles[tile.Y, tile.X].IsSolid() || terrain[tile.Y, tile.X] != EgoTerrain.Caverns || !used.Add(tile))
                        continue;
                    bool pool = item >= fungi;
                    var kind = pool ? PathDecorationKind.WaterPool : PathDecorationKind.GlowFungus;
                    output.Add(new PathDecoration(kind, PathThemeVisuals.LayerFor(kind), TileCenter(tile),
                        pool ? 1.2f + field.Hash(tile.X, tile.Y, 36) * .8f : .9f + field.Hash(tile.X, tile.Y, 36) * .4f,
                        (int)(field.Hash(tile.X, tile.Y, 37) * 4), roomId));
                    placed = true;
                }
            }
        }
        return output;
    }

    /// <summary>
    /// The veteran frontier: a ring of warning stakes at the distance beyond
    /// which veteran space begins, each flying the colour of the nearest
    /// veteran holdout. Walked in two-tile arc steps; gaps where the ground
    /// is solid or the hash says so, so it reads as a line someone once kept.
    /// </summary>
    public static IReadOnlyList<PathDecoration> GenerateFrontier(TileType[,] tiles, IReadOnlyList<EgoRegion> regions,
        IReadOnlyList<EgoLandmark> landmarks, EgoRegionField field, float viewWidth)
    {
        var output = new List<PathDecoration>();
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        Vector2 spawn = new((width / 2 + .5f) * Battleground.TileSize, (height / 2 + .5f) * Battleground.TileSize);
        float radius = viewWidth * EgoWorldGenerator.VeteranMinDistanceViews;
        var veterans = regions.Where(region => region.Kind == EgoRegionKind.VeteranHoldout).ToList();
        var holdouts = regions.Where(region => region.IsHoldout).ToList();
        int steps = Math.Max(16, (int)(MathF.Tau * radius / (Battleground.TileSize * 2f)));
        var used = new HashSet<Point>();
        for (int step = 0; step < steps; step++)
        {
            float angle = step * MathF.Tau / steps;
            Vector2 world = spawn + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            Point tile = new((int)(world.X / Battleground.TileSize), (int)(world.Y / Battleground.TileSize));
            if (tile.X < 3 || tile.Y < 3 || tile.X >= width - 3 || tile.Y >= height - 3)
                continue;
            if (tiles[tile.Y, tile.X] != TileType.Default || !used.Add(tile))
                continue;
            if (field.Hash(tile.X, tile.Y, 41) >= .7f)
                continue;
            Vector2 center = TileCenter(tile);
            if (holdouts.Any(h => h.Contains(center)) || landmarks.Any(l => l.Contains(center)))
                continue;
            EgoRegion? nearest = veterans.OrderBy(v => Vector2.DistanceSquared(v.Center, center)).FirstOrDefault();
            int sense = nearest?.SenseKey is string key ? Math.Max(0, Array.IndexOf(CampaignProgression.SenseKeys, key)) : 0;
            output.Add(new PathDecoration(PathDecorationKind.WarningStake, PathDecorationLayer.Raised, center,
                .95f + field.Hash(tile.X, tile.Y, 42) * .2f, sense, FrontierRoomIdBase + step));
        }
        return output;
    }

    /// <summary>Sense key an ambient emitter belongs to, so The Ego can tint it without a PathRun.</summary>
    public static string SenseForEmitter(PathDecorationKind kind) => kind switch
    {
        PathDecorationKind.DripEmitter => "touch",
        PathDecorationKind.RippleEmitter => "sight",
        PathDecorationKind.WindEmitter => "sound",
        PathDecorationKind.StarEmitter => "phantasia",
        _ => "chemesthesis",
    };

    private static Vector2 TileCenter(Point tile) =>
        new((tile.X + .5f) * Battleground.TileSize, (tile.Y + .5f) * Battleground.TileSize);
}
