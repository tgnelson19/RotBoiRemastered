using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.World;

/// <summary>
/// The Ego's overworld: one very large connected cave net (same carve
/// primitives as <see cref="ExpeditionWorldGenerator"/>) with sense holdouts
/// scattered across it. Distances are expressed in multiples of the player's
/// horizontal view width ("views") so the layout scales with resolution/zoom.
/// </summary>
public static class EgoWorldGenerator
{
    public const int Width = 400;
    public const int Height = 400;
    private const int RoomCount = 190;

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

    public static Battleground Generate(Random rng)
    {
        var tiles = new TileType[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                tiles[y, x] = TileType.OuterVoid;

        var centers = new List<Point> { new(Width / 2, Height / 2) };
        ExpeditionWorldGenerator.CarveRoom(tiles, centers[0], 10, 8);
        for (int index = 1; index < RoomCount; index++)
        {
            Point center = new(rng.Next(10, Width - 10), rng.Next(10, Height - 10));
            Point nearest = centers.OrderBy(other => DistanceSquared(center, other)).First();
            ExpeditionWorldGenerator.CarveTunnel(tiles, nearest, center, rng.Next(1, 3), rng.Next(2) == 0);
            ExpeditionWorldGenerator.CarveRoom(tiles, center, rng.Next(5, 13), rng.Next(4, 10));
            centers.Add(center);
            if (index > 3 && rng.NextDouble() < .42)
            {
                Point loop = centers[rng.Next(index)];
                ExpeditionWorldGenerator.CarveTunnel(tiles, center, loop, 1, rng.Next(2) == 0);
            }
        }
        ExpeditionWorldGenerator.AddWallShell(tiles);
        Point spawn = centers[0];
        return new Battleground(tiles, BiomePalettes.Soul, 20,
            new Vector2((spawn.X + .125f) * Battleground.TileSize,
                (spawn.Y + .125f) * Battleground.TileSize),
            "ego");
    }

    /// <summary>
    /// Places holdouts (never overlapping, never within HoldoutSeparationViews of
    /// each other or spawn), veteran holdouts (only beyond VeteranMinDistanceViews),
    /// and then tiles the rest of the map with senseless grid cells one view wide.
    /// </summary>
    public static IReadOnlyList<EgoRegion> PlaceRegions(Battleground battleground, Random rng, float viewWidth)
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

        void TryPlace(EgoRegionKind kind, int target, float radius, Func<Vector2, bool> allowed, int senseOffset)
        {
            int placed = 0;
            for (int attempt = 0; attempt < 6000 && placed < target && open.Length > 0; attempt++)
            {
                var (tx, ty) = open[rng.Next(open.Length)];
                Vector2 candidate = new((tx + .5f) * Battleground.TileSize, (ty + .5f) * Battleground.TileSize);
                if (!allowed(candidate) || !Separated(candidate, radius))
                    continue;
                string sense = CampaignProgression.SenseKeys[(placed + senseOffset) % CampaignProgression.SenseKeys.Length];
                regions.Add(new EgoRegion { Kind = kind, SenseKey = sense, Center = candidate, RadiusWorld = radius });
                placed++;
            }
        }

        // Veterans first: they have the stricter placement rule and would
        // otherwise be crowded out by the ring of ordinary holdouts.
        TryPlace(EgoRegionKind.VeteranHoldout, TargetVeteranHoldouts, viewWidth * VeteranHoldoutRadiusViews,
            candidate => Vector2.Distance(candidate, spawn) >= veteranMin, rng.Next(5));
        TryPlace(EgoRegionKind.Holdout, TargetHoldouts, viewWidth * HoldoutRadiusViews,
            _ => true, rng.Next(5));

        // Senseless cells: a coarse grid over every walkable tile not owned by
        // a holdout. Each cell repopulates independently.
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

    private static int DistanceSquared(Point a, Point b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
