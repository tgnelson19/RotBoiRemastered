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
