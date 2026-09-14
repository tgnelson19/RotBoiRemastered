using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.World;

public enum EgoLandmarkKind { DrownedCistern, MirrorField, ListeningTower, DreamGate, Pyre }

/// <summary>
/// One of The Ego's five one-per-seed places: where a sense fell. Each is a
/// set piece the player can stumble on, read, and shelter in -- no enemies
/// populate inside it.
/// </summary>
public sealed class EgoLandmark
{
    public EgoLandmarkKind Kind { get; init; }
    public Vector2 Center { get; init; }
    public float RadiusWorld { get; init; }
    /// <summary>Set the first time the player steps inside, for the one-time title splash.</summary>
    public bool Seen { get; set; }

    public string SenseKey => EgoLandmarks.SenseFor(Kind);
    public string Title => EgoLandmarks.TitleFor(Kind);
    public bool Contains(Vector2 world) => Vector2.DistanceSquared(world, Center) <= RadiusWorld * RadiusWorld;
}

public static class EgoLandmarks
{
    public const int RadiusTiles = 6;
    public const float MinDistanceFromSpawnViews = 4f;
    public const float SeparationViews = 1.5f;
    public const int DecorationRoomIdBase = 4000;

    public static readonly IReadOnlyList<EgoLandmarkKind> Kinds = Enum.GetValues<EgoLandmarkKind>();

    public static string SenseFor(EgoLandmarkKind kind) => kind switch
    {
        EgoLandmarkKind.DrownedCistern => "touch",
        EgoLandmarkKind.MirrorField => "sight",
        EgoLandmarkKind.ListeningTower => "sound",
        EgoLandmarkKind.DreamGate => "phantasia",
        _ => "chemesthesis",
    };

    public static string TitleFor(EgoLandmarkKind kind) => kind switch
    {
        EgoLandmarkKind.DrownedCistern => "THE DROWNED CISTERN",
        EgoLandmarkKind.MirrorField => "THE MIRROR FIELD",
        EgoLandmarkKind.ListeningTower => "THE LISTENING TOWER",
        EgoLandmarkKind.DreamGate => "THE DREAM GATE",
        _ => "THE PYRE",
    };

    /// <summary>The monument inscription at each landmark's heart. Distinct from the wandering plaques.</summary>
    public static string InscriptionFor(EgoLandmarkKind kind) => kind switch
    {
        EgoLandmarkKind.DrownedCistern => "THE TOUCH-BORN CAME DOWN HERE TO FEEL SOMETHING SOLID. THE WATER ROSE TO MEET THEM.",
        EgoLandmarkKind.MirrorField => "SIGHT PLANTED A THOUSAND MIRRORS SO THE SKY WOULD HAVE TO LOOK BACK. IT NEVER DID.",
        EgoLandmarkKind.ListeningTower => "SOUND BUILT THIS TOWER TO HEAR THE OTHERS CALL. THE LAST THING IT HEARD WAS ITSELF.",
        EgoLandmarkKind.DreamGate => "PHANTASIA OPENED A DOOR BETWEEN TWO LANDS AND FORGOT WHICH SIDE IT WAS ON.",
        _ => "CHEMESTHESIS LIT THE PYRE TO BURN THE ROT OUT OF THE WORLD. THE WORLD WAS THE ROT.",
    };

    /// <summary>
    /// Picks one site per kind on that kind's terrain, far from spawn and
    /// from each other, before holdouts are placed so they can respect it.
    /// Kinds that find no site are simply absent for that seed.
    /// </summary>
    public static List<EgoLandmark> Place(TileType[,] tiles, EgoTerrain[,] terrain, EgoRegionField field,
        Random rng, float viewWidth)
    {
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        var placed = new List<EgoLandmark>();
        Vector2 spawn = new((width / 2 + .5f) * Battleground.TileSize, (height / 2 + .5f) * Battleground.TileSize);
        float minSpawn = viewWidth * MinDistanceFromSpawnViews;
        float separation = viewWidth * SeparationViews;
        float radius = RadiusTiles * Battleground.TileSize;

        bool Suits(EgoLandmarkKind kind, int x, int y) => kind switch
        {
            EgoLandmarkKind.DrownedCistern => terrain[y, x] == EgoTerrain.Caverns,
            EgoLandmarkKind.MirrorField => terrain[y, x] == EgoTerrain.Plains && field.BorderBlend(x, y) < .2f,
            EgoLandmarkKind.ListeningTower => terrain[y, x] == EgoTerrain.City && field.BorderBlend(x, y) < .3f,
            EgoLandmarkKind.DreamGate => field.BorderBlend(x, y) > .6f && terrain[y, x] != EgoTerrain.Caverns,
            _ => terrain[y, x] != EgoTerrain.Caverns,
        };

        foreach (EgoLandmarkKind kind in Kinds)
        {
            for (int attempt = 0; attempt < 3000; attempt++)
            {
                int x = rng.Next(RadiusTiles + 3, width - RadiusTiles - 3);
                int y = rng.Next(RadiusTiles + 3, height - RadiusTiles - 3);
                if (!Suits(kind, x, y))
                    continue;
                Vector2 center = new((x + .5f) * Battleground.TileSize, (y + .5f) * Battleground.TileSize);
                if (Vector2.Distance(center, spawn) < minSpawn)
                    continue;
                if (placed.Any(other => Vector2.Distance(other.Center, center) < separation + radius + other.RadiusWorld))
                    continue;
                placed.Add(new EgoLandmark { Kind = kind, Center = center, RadiusWorld = radius });
                break;
            }
        }
        return placed;
    }

    /// <summary>Carves or builds each landmark's footprint into the tile grid.</summary>
    public static void Stamp(TileType[,] tiles, IReadOnlyList<EgoLandmark> landmarks, Random rng)
    {
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        foreach (EgoLandmark landmark in landmarks)
        {
            int cx = (int)(landmark.Center.X / Battleground.TileSize), cy = (int)(landmark.Center.Y / Battleground.TileSize);
            switch (landmark.Kind)
            {
                case EgoLandmarkKind.ListeningTower:
                    ClearDisc(tiles, cx, cy, RadiusTiles - 1);
                    Battleground.PaintBuilding(tiles, cx, cy, 9, 9, verticalDoors: rng.Next(2) == 0, BuildingStyle.Shrine);
                    // The shrine's plus would seal the heart; open one arm as the altar niche.
                    tiles[cy + 1, cx] = TileType.BuildingFloor;
                    break;
                case EgoLandmarkKind.Pyre:
                    ClearDisc(tiles, cx, cy, RadiusTiles - 1);
                    Battleground.PaintBuilding(tiles, cx, cy, 9, 9, verticalDoors: rng.Next(2) == 0, BuildingStyle.Forge);
                    break;
                case EgoLandmarkKind.DrownedCistern:
                    ExpeditionWorldGenerator.CarveRoom(tiles, new Point(cx, cy), RadiusTiles - 1, RadiusTiles - 2);
                    break;
                default:
                    ClearDisc(tiles, cx, cy, RadiusTiles - 2);
                    break;
            }
            // Every landmark keeps its heart walkable for the monument.
            if (cx > 0 && cy > 0 && cx < width && cy < height)
                tiles[cy, cx] = tiles[cy, cx] == TileType.BuildingFloor ? TileType.BuildingFloor : TileType.Default;
        }
    }

    private static void ClearDisc(TileType[,] tiles, int cx, int cy, int radius)
    {
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        for (int y = Math.Max(2, cy - radius); y <= Math.Min(height - 3, cy + radius); y++)
            for (int x = Math.Max(2, cx - radius); x <= Math.Min(width - 3, cx + radius); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                    tiles[y, x] = TileType.Default;
    }

    /// <summary>
    /// The set dressing: the sense's crest writ large, a ring of its luminous
    /// landmark prop (so the place glows from the murk edge), floor motifs and
    /// two ambient emitters. RoomId identifies the landmark.
    /// </summary>
    public static IReadOnlyList<PathDecoration> Decorations(TileType[,] tiles, IReadOnlyList<EgoLandmark> landmarks, Random rng)
    {
        var output = new List<PathDecoration>();
        int height = tiles.GetLength(0), width = tiles.GetLength(1);
        foreach (EgoLandmark landmark in landmarks)
        {
            string sense = landmark.SenseKey;
            var profile = PathThemeVisuals.For(sense);
            int roomId = DecorationRoomIdBase + (int)landmark.Kind;
            int cx = (int)(landmark.Center.X / Battleground.TileSize), cy = (int)(landmark.Center.Y / Battleground.TileSize);
            output.Add(new PathDecoration(PathThemeVisuals.EntranceCrestFor(sense), PathDecorationLayer.Floor,
                landmark.Center, 7f, 0, roomId));

            PathDecorationKind prop = landmark.Kind switch
            {
                EgoLandmarkKind.DrownedCistern => PathDecorationKind.PressureTank,
                EgoLandmarkKind.MirrorField => PathDecorationKind.MirrorArch,
                EgoLandmarkKind.ListeningTower => PathDecorationKind.OrganStack,
                EgoLandmarkKind.DreamGate => PathDecorationKind.LanternSpire,
                _ => PathDecorationKind.FurnaceIdol,
            };
            int ringCount = landmark.Kind == EgoLandmarkKind.MirrorField ? 10 : 6;
            float ringRadius = landmark.Kind is EgoLandmarkKind.ListeningTower or EgoLandmarkKind.Pyre ? 3.2f : 3.6f;
            for (int index = 0; index < ringCount; index++)
            {
                float angle = index * MathF.Tau / ringCount + (float)rng.NextDouble() * .2f;
                int tx = cx + (int)MathF.Round(MathF.Cos(angle) * ringRadius);
                int ty = cy + (int)MathF.Round(MathF.Sin(angle) * ringRadius);
                if (tx < 2 || ty < 2 || tx >= width - 2 || ty >= height - 2 || tiles[ty, tx].IsSolid())
                    continue;
                output.Add(new PathDecoration(prop, PathDecorationLayer.Raised, TileCenter(tx, ty),
                    1.1f + (float)rng.NextDouble() * .25f, rng.Next(4), roomId));
            }
            int motifs = landmark.Kind == EgoLandmarkKind.DrownedCistern ? 6 : 4;
            for (int motif = 0; motif < motifs; motif++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2);
                float distance = 1.5f + (float)rng.NextDouble() * 2.5f;
                int tx = cx + (int)MathF.Round(MathF.Cos(angle) * distance);
                int ty = cy + (int)MathF.Round(MathF.Sin(angle) * distance);
                if (tx < 2 || ty < 2 || tx >= width - 2 || ty >= height - 2 || tiles[ty, tx].IsSolid() || (tx == cx && ty == cy))
                    continue;
                var kind = landmark.Kind == EgoLandmarkKind.DrownedCistern
                    ? PathDecorationKind.WaterPool
                    : profile.FloorMotifs[rng.Next(profile.FloorMotifs.Count)];
                output.Add(new PathDecoration(kind, PathThemeVisuals.LayerFor(kind), TileCenter(tx, ty),
                    1.3f + (float)rng.NextDouble() * .8f, rng.Next(4), roomId));
            }
            output.Add(new PathDecoration(profile.AmbientEmitter, PathDecorationLayer.Ambient,
                landmark.Center + new Vector2(-Battleground.TileSize * 2, 0), 1.4f, rng.Next(4), roomId));
            output.Add(new PathDecoration(profile.AmbientEmitter, PathDecorationLayer.Ambient,
                landmark.Center + new Vector2(Battleground.TileSize * 2, 0), 1.4f, rng.Next(4), roomId));
        }
        return output;
    }

    private static Vector2 TileCenter(int x, int y) =>
        new((x + .5f) * Battleground.TileSize, (y + .5f) * Battleground.TileSize);
}
