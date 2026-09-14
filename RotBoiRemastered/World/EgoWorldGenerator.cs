using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.World;

public sealed record EgoWorld(Battleground Battleground, IReadOnlyList<EgoRegion> Regions, EgoRegionField Field,
    IReadOnlyList<EgoTrace> Traces);

/// <summary>
/// The Ego's overworld: three terrains (open plains, ruined city grids, close
/// caverns) assigned by <see cref="EgoRegionField"/> and bleeding into each
/// other, with sense holdouts stamped on top regardless of terrain. Distances
/// are expressed in multiples of the player's horizontal view width ("views")
/// so the layout scales with resolution/zoom.
/// </summary>
public static class EgoWorldGenerator
{
    public const int Width = 400;
    public const int Height = 400;

    /// <summary>Enemies populate a region once the player is within this many views of it.</summary>
    public const float SpawnRadiusViews = 2f;
    /// <summary>Overworld enemies farther than this many views from the player are despawned.</summary>
    public const float DespawnRadiusViews = 4f;
    /// <summary>Veteran holdouts / veteran senseless space begin this many views from spawn.</summary>
    public const float VeteranMinDistanceViews = 6f;
    /// <summary>Holdouts may never come within this many views of one another or of spawn.</summary>
    public const float HoldoutSeparationViews = 2f;
    public const float HoldoutRadiusViews = .5f;
    public const float VeteranHoldoutRadiusViews = .75f;
    public const int TargetHoldouts = 25;
    public const int TargetVeteranHoldouts = 10;
    public const double SenselessRespawnSeconds = 60;
    public const double HoldoutRespawnSeconds = 180;
    /// <summary>Tiles from a holdout's edge over which its sense palette dithers into the terrain's.</summary>
    public const int HoldoutRimTiles = 3;

    /// <summary>Visual theme per Ego palette index: three terrains, then the five senses.</summary>
    public static readonly IReadOnlyList<string?> PaletteThemeKeys =
        new[] { "ego_plains", "ego_city", "ego_caverns" }.Concat(CampaignProgression.SenseKeys).ToArray();

    public static EgoWorld Build(Random rng, float viewWidth)
    {
        int viewTiles = Math.Max(8, (int)MathF.Round(viewWidth / Battleground.TileSize));
        var field = new EgoRegionField(rng, Width, Height, viewTiles);
        var terrain = EffectiveTerrain(field, rng);
        var tiles = GenerateTiles(field, terrain, rng, viewTiles);
        ClearSpawnClearing(tiles);
        EnsureConnected(tiles, new Point(Width / 2, Height / 2), rng);

        Vector2 spawn = new((Width / 2 + .125f) * Battleground.TileSize, (Height / 2 + .125f) * Battleground.TileSize);
        var probe = new Battleground(tiles, BiomePalettes.Ego, 18, spawn, "ego");
        var regions = PlaceRegions(probe, rng, viewWidth, terrain);
        StampHoldoutStructures(tiles, regions, rng);
        ClearSpawnClearing(tiles);
        EnsureConnected(tiles, new Point(Width / 2, Height / 2), rng);
        ExpeditionWorldGenerator.AddWallShell(tiles);
        int[,] biomeMap = BuildBiomeMap(terrain, regions, field);
        var finalProbe = new Battleground(tiles, BiomePalettes.Ego, 18, spawn, "ego");
        var traces = EgoTraces.Generate(finalProbe, terrain, regions, rng);
        var decorations = EgoThemeVisuals.GenerateDecorations(tiles, regions, terrain, rng)
            .Concat(EgoTraces.ScorchDecorations(traces, rng)).ToList();
        var battleground = new Battleground(tiles, BiomePalettes.Ego, 18, spawn, "ego",
            pathDecorations: decorations, biomeMap: biomeMap, paletteThemeKeys: PaletteThemeKeys);
        foreach (EgoRegion region in regions)
            region.Terrain = terrain[(int)(region.Center.Y / Battleground.TileSize), (int)(region.Center.X / Battleground.TileSize)];
        return new EgoWorld(battleground, regions, field, traces);
    }

    /// <summary>Kept for callers that only need a map; regions are placed separately.</summary>
    public static Battleground Generate(Random rng) => Build(rng, 1920f).Battleground;

    /// <summary>
    /// Terrain per tile after bleeding: inside the border band a tile may be
    /// handed to the runner-up seed by a dithered coin flip weighted by how
    /// close to the border it is, so edges fray instead of cutting straight.
    /// </summary>
    internal static EgoTerrain[,] EffectiveTerrain(EgoRegionField field, Random rng)
    {
        var terrain = new EgoTerrain[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                EgoTerrain first = field.TerrainAt(x, y), second = field.SecondTerrainAt(x, y);
                float blend = field.BorderBlend(x, y);
                terrain[y, x] = first != second && blend > 0f && field.Hash(x, y, 99) < blend * .5f ? second : first;
            }
        return terrain;
    }

    private static TileType[,] GenerateTiles(EgoRegionField field, EgoTerrain[,] terrain, Random rng, int viewTiles)
    {
        var tiles = new TileType[Height, Width];
        // Plains base: open ground everywhere, boulders scattered by hash.
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (x < 2 || y < 2 || x >= Width - 2 || y >= Height - 2)
                {
                    tiles[y, x] = TileType.OuterVoid;
                    continue;
                }
                switch (terrain[y, x])
                {
                    case EgoTerrain.Caverns:
                        tiles[y, x] = TileType.OuterVoid;
                        break;
                    default:
                        tiles[y, x] = TileType.Default;
                        break;
                }
            }

        foreach (EgoRegionField.Seed seed in field.Seeds)
        {
            switch (seed.Terrain)
            {
                case EgoTerrain.Plains:
                    ScatterBoulders(tiles, terrain, seed, field, viewTiles);
                    break;
                case EgoTerrain.City:
                    LayCity(tiles, terrain, seed, rng, viewTiles);
                    break;
                case EgoTerrain.Caverns:
                    CarveCaverns(tiles, terrain, seed, rng, viewTiles);
                    break;
            }
        }
        // Dirt trails between neighbouring plains seeds.
        var plains = field.Seeds.Where(seed => seed.Terrain == EgoTerrain.Plains).ToList();
        foreach (EgoRegionField.Seed seed in plains)
        {
            EgoRegionField.Seed? nearest = plains
                .Where(other => other != seed)
                .OrderBy(other => Vector2.DistanceSquared(other.Tile.ToVector2(), seed.Tile.ToVector2()))
                .FirstOrDefault();
            if (nearest is not null)
                Battleground.PaintRoad(tiles, (seed.Tile.X, seed.Tile.Y), (nearest.Tile.X, nearest.Tile.Y), 0);
        }
        // Ruin city walls that touch open plains, then wrap void in masonry.
        for (int y = 1; y < Height - 1; y++)
            for (int x = 1; x < Width - 1; x++)
                if (tiles[y, x].IsRaised() && terrain[y, x] == EgoTerrain.City
                    && Neighbours(x, y).Any(n => terrain[n.Y, n.X] == EgoTerrain.Plains && !tiles[n.Y, n.X].IsSolid())
                    && field.Hash(x, y, 5) < .55f)
                    tiles[y, x] = TileType.Default;
        return tiles;
    }

    private static IEnumerable<Point> Neighbours(int x, int y)
    {
        yield return new Point(x - 1, y);
        yield return new Point(x + 1, y);
        yield return new Point(x, y - 1);
        yield return new Point(x, y + 1);
    }

    private static void ScatterBoulders(TileType[,] tiles, EgoTerrain[,] terrain, EgoRegionField.Seed seed,
        EgoRegionField field, int viewTiles)
    {
        int reach = viewTiles * 3;
        for (int y = Math.Max(2, seed.Tile.Y - reach); y < Math.Min(Height - 2, seed.Tile.Y + reach); y++)
            for (int x = Math.Max(2, seed.Tile.X - reach); x < Math.Min(Width - 2, seed.Tile.X + reach); x++)
            {
                if (terrain[y, x] != EgoTerrain.Plains || tiles[y, x] != TileType.Default)
                    continue;
                float roll = field.Hash(x, y, 21);
                if (roll < .012f)
                {
                    tiles[y, x] = TileType.BuildingWall;
                    if (roll < .005f && x + 1 < Width - 2 && terrain[y, x + 1] == EgoTerrain.Plains)
                        tiles[y, x + 1] = TileType.BuildingWall;
                    if (roll < .002f && y + 1 < Height - 2 && terrain[y + 1, x] == EgoTerrain.Plains)
                        tiles[y + 1, x] = TileType.BuildingWall;
                }
            }
    }

    private static void LayCity(TileType[,] tiles, EgoTerrain[,] terrain, EgoRegionField.Seed seed, Random rng, int viewTiles)
    {
        int reach = viewTiles * 3;
        int left = Math.Max(3, seed.Tile.X - reach), right = Math.Min(Width - 4, seed.Tile.X + reach);
        int top = Math.Max(3, seed.Tile.Y - reach), bottom = Math.Min(Height - 4, seed.Tile.Y + reach);
        bool City(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && terrain[y, x] == EgoTerrain.City;

        // Road grid: irregular cell sizes so blocks differ; roads only where
        // the terrain is city so the grid frays into the neighbouring biome.
        var columns = new List<int>();
        for (int x = left + rng.Next(3, 8); x < right; x += rng.Next(9, 15)) columns.Add(x);
        var rows = new List<int>();
        for (int y = top + rng.Next(3, 8); y < bottom; y += rng.Next(9, 15)) rows.Add(y);
        int mainWidth = rng.Next(1, 3);
        foreach (int x in columns)
            for (int y = top; y <= bottom; y++)
                for (int ox = 0; ox < mainWidth; ox++)
                    if (City(x + ox, y)) tiles[y, x + ox] = TileType.Road;
        foreach (int y in rows)
            for (int x = left; x <= right; x++)
                for (int oy = 0; oy < mainWidth; oy++)
                    if (City(x, y + oy)) tiles[y + oy, x] = TileType.Road;

        // Blocks between roads get a building if the footprint is all city.
        var xStarts = new[] { left }.Concat(columns.Select(x => x + mainWidth)).ToList();
        var xEnds = columns.Select(x => x - 1).Concat(new[] { right }).ToList();
        var yStarts = new[] { top }.Concat(rows.Select(y => y + mainWidth)).ToList();
        var yEnds = rows.Select(y => y - 1).Concat(new[] { bottom }).ToList();
        for (int cy = 0; cy < yStarts.Count; cy++)
            for (int cx = 0; cx < xStarts.Count; cx++)
            {
                int bx0 = xStarts[cx] + 1, bx1 = xEnds[cx] - 1;
                int by0 = yStarts[cy] + 1, by1 = yEnds[cy] - 1;
                int blockW = bx1 - bx0 + 1, blockH = by1 - by0 + 1;
                if (blockW < 6 || blockH < 6)
                    continue;
                int centerX = (bx0 + bx1) / 2, centerY = (by0 + by1) / 2;
                if (!City(centerX, centerY) || rng.NextDouble() < .18)
                    continue;
                int w = Math.Min(blockW - 1, rng.Next(6, 12)) | 1, h = Math.Min(blockH - 1, rng.Next(6, 10)) | 1;
                if (w < 5 || h < 5)
                    continue;
                bool footprintOk = true;
                for (int y = centerY - h / 2; y <= centerY + h / 2 && footprintOk; y++)
                    for (int x = centerX - w / 2; x <= centerX + w / 2; x++)
                        if (!City(x, y) || x < 2 || y < 2 || x >= Width - 2 || y >= Height - 2) { footprintOk = false; break; }
                if (!footprintOk)
                    continue;
                var style = (BuildingStyle)rng.Next(Enum.GetValues<BuildingStyle>().Length);
                Battleground.PaintBuilding(tiles, centerX, centerY, w, h, verticalDoors: rng.Next(2) == 0, style);
                // Alleys: a one-wide cut through the block beside larger buildings.
                if (blockW >= 12 && rng.Next(2) == 0)
                    for (int y = by0; y <= by1; y++)
                        if (City(bx0, y) && tiles[y, bx0] == TileType.Default) tiles[y, bx0] = TileType.Road;
                // Ruin: a quarter of buildings lose stretches of wall.
                if (rng.NextDouble() < .25)
                {
                    int gaps = rng.Next(2, 5);
                    for (int gap = 0; gap < gaps; gap++)
                    {
                        int gx = rng.Next(centerX - w / 2, centerX + w / 2 + 1);
                        int gy = rng.Next(centerY - h / 2, centerY + h / 2 + 1);
                        if (tiles[gy, gx] == TileType.BuildingWall) tiles[gy, gx] = TileType.Default;
                    }
                }
            }
    }

    private static void CarveCaverns(TileType[,] tiles, EgoTerrain[,] terrain, EgoRegionField.Seed seed, Random rng, int viewTiles)
    {
        int reach = viewTiles * 3;
        var candidates = new List<Point>();
        for (int y = Math.Max(6, seed.Tile.Y - reach); y < Math.Min(Height - 6, seed.Tile.Y + reach); y += 3)
            for (int x = Math.Max(6, seed.Tile.X - reach); x < Math.Min(Width - 6, seed.Tile.X + reach); x += 3)
                if (terrain[y, x] == EgoTerrain.Caverns)
                    candidates.Add(new Point(x, y));
        if (candidates.Count == 0)
            return;
        int roomCount = Math.Max(4, candidates.Count / 40);
        var centers = new List<Point> { terrain[seed.Tile.Y, seed.Tile.X] == EgoTerrain.Caverns ? seed.Tile : candidates[rng.Next(candidates.Count)] };
        ExpeditionWorldGenerator.CarveRoom(tiles, centers[0], rng.Next(5, 10), rng.Next(4, 8));
        for (int index = 1; index < roomCount; index++)
        {
            Point center = candidates[rng.Next(candidates.Count)];
            Point nearest = centers.OrderBy(other => Vector2.DistanceSquared(other.ToVector2(), center.ToVector2())).First();
            ExpeditionWorldGenerator.CarveTunnel(tiles, nearest, center, rng.Next(1, 3), rng.Next(2) == 0);
            ExpeditionWorldGenerator.CarveRoom(tiles, center, rng.Next(4, 10), rng.Next(3, 8));
            centers.Add(center);
            if (index > 2 && rng.NextDouble() < .4)
                ExpeditionWorldGenerator.CarveTunnel(tiles, center, centers[rng.Next(index)], 1, rng.Next(2) == 0);
        }
    }

    /// <summary>
    /// The player always lands on open ground: nothing (boulder, dithered
    /// city wall, cavern void) may generate on or right beside the spawn tile.
    /// </summary>
    public const int SpawnClearingRadius = 4;
    internal static void ClearSpawnClearing(TileType[,] tiles)
    {
        int cx = Width / 2, cy = Height / 2;
        for (int y = cy - SpawnClearingRadius; y <= cy + SpawnClearingRadius; y++)
            for (int x = cx - SpawnClearingRadius; x <= cx + SpawnClearingRadius; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= SpawnClearingRadius * SpawnClearingRadius
                    && tiles[y, x].IsSolid())
                    tiles[y, x] = TileType.Default;
    }

    /// <summary>
    /// Flood-fills walkable tiles from spawn and tunnels every other pocket
    /// back to the main body (tiny pockets are simply sealed) so no holdout
    /// or trace can generate unreachable.
    /// </summary>
    internal static void EnsureConnected(TileType[,] tiles, Point spawn, Random rng)
    {
        for (int pass = 0; pass < 200; pass++)
        {
            var component = new int[Height, Width];
            int count = 0;
            var sizes = new List<int>();
            var samples = new List<Point>();
            for (int y = 1; y < Height - 1; y++)
                for (int x = 1; x < Width - 1; x++)
                {
                    if (tiles[y, x].IsSolid() || component[y, x] != 0)
                        continue;
                    count++;
                    int size = 0;
                    var stack = new Stack<Point>();
                    stack.Push(new Point(x, y));
                    component[y, x] = count;
                    while (stack.Count > 0)
                    {
                        Point p = stack.Pop();
                        size++;
                        foreach (Point n in Neighbours(p.X, p.Y))
                        {
                            if (n.X < 1 || n.Y < 1 || n.X >= Width - 1 || n.Y >= Height - 1) continue;
                            if (tiles[n.Y, n.X].IsSolid() || component[n.Y, n.X] != 0) continue;
                            component[n.Y, n.X] = count;
                            stack.Push(n);
                        }
                    }
                    sizes.Add(size);
                    samples.Add(new Point(x, y));
                }
            int main = component[spawn.Y, spawn.X];
            if (main == 0)
            {
                ExpeditionWorldGenerator.CarveRoom(tiles, spawn, 4, 4);
                continue;
            }
            if (count <= 1)
                return;
            bool changed = false;
            for (int id = 1; id <= count; id++)
            {
                if (id == main)
                    continue;
                if (sizes[id - 1] < 12)
                {
                    for (int y = 1; y < Height - 1; y++)
                        for (int x = 1; x < Width - 1; x++)
                            if (component[y, x] == id) tiles[y, x] = TileType.BuildingWall;
                    changed = true;
                    continue;
                }
                Point from = samples[id - 1];
                Point? best = null;
                int bestDistance = int.MaxValue;
                for (int y = 1; y < Height - 1; y += 2)
                    for (int x = 1; x < Width - 1; x += 2)
                        if (component[y, x] == main)
                        {
                            int d = Math.Abs(x - from.X) + Math.Abs(y - from.Y);
                            if (d < bestDistance) { bestDistance = d; best = new Point(x, y); }
                        }
                if (best is Point target)
                {
                    ExpeditionWorldGenerator.CarveTunnel(tiles, from, target, 1, rng.Next(2) == 0);
                    changed = true;
                    break; // recompute components after each tunnel
                }
            }
            if (!changed)
                return;
        }
    }

    public static IReadOnlyList<EgoRegion> PlaceRegions(Battleground battleground, Random rng, float viewWidth) =>
        PlaceRegions(battleground, rng, viewWidth, null);

    /// <summary>
    /// Places holdouts (never overlapping, never within HoldoutSeparationViews of
    /// each other or spawn), veteran holdouts (only beyond VeteranMinDistanceViews,
    /// preferring city/cavern ground), then tiles the rest with senseless cells.
    /// </summary>
    public static IReadOnlyList<EgoRegion> PlaceRegions(Battleground battleground, Random rng, float viewWidth, EgoTerrain[,]? terrain)
    {
        var regions = new List<EgoRegion>();
        Vector2 spawn = battleground.SpawnPosition;
        float separation = viewWidth * HoldoutSeparationViews;
        float veteranMin = viewWidth * VeteranMinDistanceViews;
        var open = battleground.OpenTiles();

        bool Separated(Vector2 candidate, float radius) =>
            Vector2.Distance(candidate, spawn) >= separation + radius
            && regions.All(other =>
                Vector2.Distance(other.Center, candidate) >= separation + radius + other.RadiusWorld);

        void TryPlace(EgoRegionKind kind, int target, float radius, Func<Vector2, (int X, int Y), bool> allowed, int senseOffset)
        {
            int placed = 0;
            for (int attempt = 0; attempt < 6000 && placed < target && open.Length > 0; attempt++)
            {
                var (tx, ty) = open[rng.Next(open.Length)];
                Vector2 candidate = new((tx + .5f) * Battleground.TileSize, (ty + .5f) * Battleground.TileSize);
                if (tx < 12 || ty < 12 || tx >= battleground.Width - 12 || ty >= battleground.Height - 12)
                    continue;
                if (!allowed(candidate, (tx, ty)) || !Separated(candidate, radius))
                    continue;
                string sense = CampaignProgression.SenseKeys[(placed + senseOffset) % CampaignProgression.SenseKeys.Length];
                regions.Add(new EgoRegion { Kind = kind, SenseKey = sense, Center = candidate, RadiusWorld = radius });
                placed++;
            }
        }

        TryPlace(EgoRegionKind.VeteranHoldout, TargetVeteranHoldouts, viewWidth * VeteranHoldoutRadiusViews,
            (candidate, tile) => Vector2.Distance(candidate, spawn) >= veteranMin
                && (terrain is null || terrain[tile.Y, tile.X] != EgoTerrain.Plains || rng.NextDouble() < .25),
            rng.Next(5));
        TryPlace(EgoRegionKind.Holdout, TargetHoldouts, viewWidth * HoldoutRadiusViews,
            (_, _) => true, rng.Next(5));

        int cellTiles = Math.Max(4, (int)MathF.Round(viewWidth / Battleground.TileSize));
        for (int cy = 0; cy < battleground.Height; cy += cellTiles)
        {
            for (int cx = 0; cx < battleground.Width; cx += cellTiles)
            {
                bool walkable = false;
                for (int y = cy; y < Math.Min(battleground.Height, cy + cellTiles) && !walkable; y++)
                    for (int x = cx; x < Math.Min(battleground.Width, cx + cellTiles); x++)
                        if (!battleground.TileAt(x, y).IsSolid()) { walkable = true; break; }
                if (!walkable)
                    continue;
                Vector2 center = new((cx + cellTiles / 2f) * Battleground.TileSize, (cy + cellTiles / 2f) * Battleground.TileSize);
                if (regions.Any(region => region.IsHoldout && region.Contains(center)))
                    continue;
                bool veteran = Vector2.Distance(center, spawn) >= veteranMin;
                regions.Add(new EgoRegion
                {
                    Kind = veteran ? EgoRegionKind.VeteranSenseless : EgoRegionKind.Senseless,
                    Center = center,
                    RadiusWorld = cellTiles * Battleground.TileSize * .5f,
                });
            }
        }
        return regions;
    }

    /// <summary>Plains holdouts sometimes get a ruined structure; every holdout keeps its center walkable.</summary>
    private static void StampHoldoutStructures(TileType[,] tiles, IReadOnlyList<EgoRegion> regions, Random rng)
    {
        foreach (EgoRegion region in regions.Where(region => region.IsHoldout))
        {
            int cx = (int)(region.Center.X / Battleground.TileSize), cy = (int)(region.Center.Y / Battleground.TileSize);
            bool plainsLike = tiles[cy, cx] == TileType.Default;
            if (!plainsLike || rng.NextDouble() < .5)
                continue;
            int w = rng.Next(11, 16) | 1, h = rng.Next(9, 14) | 1;
            if (cx - w / 2 < 2 || cy - h / 2 < 2 || cx + w / 2 >= Width - 2 || cy + h / 2 >= Height - 2)
                continue;
            var styles = new[] { BuildingStyle.Plain, BuildingStyle.Bastion, BuildingStyle.Archive, BuildingStyle.Vault };
            Battleground.PaintBuilding(tiles, cx, cy, w, h, verticalDoors: rng.Next(2) == 0, styles[rng.Next(styles.Length)]);
            region.HasStructure = true;
        }
    }

    /// <summary>Palette index per tile: terrain outside holdouts, the holdout's sense inside, dithered over the rim.</summary>
    internal static int[,] BuildBiomeMap(EgoTerrain[,] terrain, IReadOnlyList<EgoRegion> regions, EgoRegionField field)
    {
        var map = new int[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                map[y, x] = (int)terrain[y, x];
        float rim = HoldoutRimTiles * Battleground.TileSize;
        foreach (EgoRegion region in regions.Where(region => region.IsHoldout))
        {
            int senseIndex = Array.IndexOf(CampaignProgression.SenseKeys, region.SenseKey);
            if (senseIndex < 0) continue;
            int paletteIndex = BiomePalettes.EgoSensePaletteOffset + senseIndex;
            float outer = region.RadiusWorld + rim;
            int minX = Math.Max(0, (int)((region.Center.X - outer) / Battleground.TileSize));
            int maxX = Math.Min(Width - 1, (int)((region.Center.X + outer) / Battleground.TileSize));
            int minY = Math.Max(0, (int)((region.Center.Y - outer) / Battleground.TileSize));
            int maxY = Math.Min(Height - 1, (int)((region.Center.Y + outer) / Battleground.TileSize));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float d = Vector2.Distance(new Vector2((x + .5f) * Battleground.TileSize, (y + .5f) * Battleground.TileSize), region.Center);
                    if (d <= region.RadiusWorld - rim)
                        map[y, x] = paletteIndex;
                    else if (d <= region.RadiusWorld + rim)
                    {
                        float t = (region.RadiusWorld + rim - d) / (rim * 2f);
                        if (field.Hash(x, y, 42) < t)
                            map[y, x] = paletteIndex;
                    }
                }
        }
        return map;
    }
}
