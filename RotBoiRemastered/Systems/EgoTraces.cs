using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

public enum EgoTraceKind { Remains, Scorch, Plaque, Cache, Monument }

/// <summary>
/// Evidence that someone came this way before. Remains are a broken crate
/// holding a single item; scorches are burnt floor; plaques are inscriptions
/// on ruined city walls, read with F; caches are buried crates betrayed only
/// by a faint mark underfoot, dug with F; monuments are the inscriptions at
/// each landmark's heart. Bounties never point at these -- they are found,
/// not hunted.
/// </summary>
public sealed class EgoTrace
{
    public EgoTraceKind Kind { get; init; }
    public Vector2 World { get; init; }
    public int TextIndex { get; init; }
    public bool Taken { get; set; }
    public bool Read { get; set; }
    /// <summary>A cache the player has dug up; its crate then forms like remains.</summary>
    public bool Dug { get; set; }
    public EgoLandmarkKind? LandmarkKind { get; init; }
    public LootCrate? Crate { get; set; }

    public bool IsReadable => Kind is EgoTraceKind.Plaque or EgoTraceKind.Monument;
    public string Inscription => Kind == EgoTraceKind.Monument && LandmarkKind is EgoLandmarkKind landmark
        ? EgoLandmarks.InscriptionFor(landmark)
        : EgoTraces.Inscriptions[TextIndex % EgoTraces.Inscriptions.Count];
}

public static class EgoTraces
{
    public const int RemainsTarget = 14;
    public const int ScorchTarget = 18;
    public const int PlaqueTarget = 12;
    public const int CacheTarget = 8;
    public const float InteractRadiusTiles = 1.6f;
    /// <summary>A cache's mark only fades in once the player is within this many tiles.</summary>
    public const float CacheRevealTiles = 6f;

    /// <summary>One-line inscriptions left on the ruins. Shown at most once per run each.</summary>
    public static readonly IReadOnlyList<string> Inscriptions = new[]
    {
        "WE BUILT THIS CITY TO HEAR OURSELVES THINK. IT LISTENED BACK.",
        "THE FIVE WERE ONE ONCE. ASK THE DARK WHICH OF THEM LEFT FIRST.",
        "IF YOU ARE READING THIS YOU CAME WITH NOTHING. GOOD. NOTHING IS WHAT IT WANTS.",
        "SOUND WAS THE FIRST TO FORGET ITS NAME.",
        "THE TOUCH-BORN SEALED THE CISTERNS AND DROWNED WITH THEM.",
        "SIGHT SAW THE END AND BUILT MIRRORS SO IT WOULD NOT HAVE TO LOOK ALONE.",
        "BURN THE ROT. BURN THE ROAD. CHEMESTHESIS BURNED THE MAP.",
        "PHANTASIA DREAMED A DOOR. WE ARE STILL INSIDE THE DREAM.",
        "THE VETERANS DO NOT GUARD. THEY WAIT.",
        "SOMETHING FRACTURED WALKS THE FAR WILDS. IT HAS APHANTASIA'S FACE.",
        "I LEFT MY CRATE BY THE WATER. TAKE THE RING. I WON'T NEED IT.",
        "THE FOG IS NOT WEATHER. IT IS THE EDGE OF WHAT YOU CAN IMAGINE.",
        "COUNT THE HOLDOUTS. WHEN THEY STOP ANSWERING, YOU ARE DEEP ENOUGH.",
        "EVERY DOOR THEY DROP LEADS SOMEWHERE THEY ALREADY LOST.",
        "THE HUNTER SMELLS WHAT YOU CARRY. CARRY LESS.",
        "WE THOUGHT THE MIND WAS THE CENTER. THE EGO IS THE CENTER. THE MIND IS THE WALL.",
        "TWENTY STEPS UP AND THE SHARD COMES LOOKING. WE NEVER MADE THE TWENTIETH.",
        "THERE WAS A CITY HERE BEFORE THE CITY. THERE IS A CITY UNDER IT STILL.",
        "IF THE PLAINS GO QUIET, RUN FOR THE CAVES. IF THE CAVES GO QUIET, PRAY.",
        "APHANTASIA IS NOT BLIND. APHANTASIA HAS SIMPLY STOPPED PICTURING YOU.",
        "THE CORE OF THE VOID IS A DOOR, NOT A FLOOR.",
        "I WROTE MY NAME HERE. THE WALL FORGOT IT BEFORE I DID.",
        "DO NOT TRUST A HOLDOUT THAT WELCOMES YOU.",
        "LEVEL IS JUST HOW MUCH OF YOURSELF YOU HAVE REMEMBERED.",
        "THE LAST OF US WENT NORTH. THERE IS NO NORTH HERE.",
        "WHERE THE GRASS WON'T GROW, WE HID WHAT WE COULDN'T CARRY. LOOK DOWN.",
        "FIVE PLACES STILL GLOW OUT THERE. ONE FOR EACH OF THEM. GO AND SEE WHAT THEY LEFT.",
    };

    public static List<EgoTrace> Generate(Battleground map, EgoTerrain[,] terrain, IReadOnlyList<EgoRegion> regions, Random rng,
        IReadOnlyList<EgoLandmark>? landmarks = null)
    {
        var traces = new List<EgoTrace>();
        var open = map.OpenTiles();
        var spawn = map.SpawnPosition;
        var textOrder = Enumerable.Range(0, Inscriptions.Count).OrderBy(_ => rng.Next()).ToList();
        landmarks ??= Array.Empty<EgoLandmark>();
        // Monuments first: one at each landmark's heart.
        foreach (EgoLandmark landmark in landmarks)
            traces.Add(new EgoTrace { Kind = EgoTraceKind.Monument, World = landmark.Center, LandmarkKind = landmark.Kind });
        bool NearHoldout(Vector2 world) => regions.Any(region => region.IsHoldout
            && Vector2.DistanceSquared(region.Center, world) < MathF.Pow(region.RadiusWorld * .8f, 2))
            || landmarks.Any(landmark => landmark.Contains(world));

        void Place(EgoTraceKind kind, int target, Func<(int X, int Y), bool> allowed)
        {
            int placed = 0;
            for (int attempt = 0; attempt < 4000 && placed < target; attempt++)
            {
                var tile = open[rng.Next(open.Length)];
                Vector2 world = new((tile.X + .5f) * Battleground.TileSize, (tile.Y + .5f) * Battleground.TileSize);
                if (Vector2.Distance(world, spawn) < Battleground.TileSize * 20 || NearHoldout(world) || !allowed(tile))
                    continue;
                if (traces.Any(other => Vector2.DistanceSquared(other.World, world) < MathF.Pow(Battleground.TileSize * 10, 2)))
                    continue;
                traces.Add(new EgoTrace
                {
                    Kind = kind,
                    World = world,
                    TextIndex = kind == EgoTraceKind.Plaque ? textOrder[placed % textOrder.Count] : 0,
                });
                placed++;
            }
        }

        bool BesideWall((int X, int Y) tile) =>
            map.TileAt(tile.X + 1, tile.Y).IsRaised() || map.TileAt(tile.X - 1, tile.Y).IsRaised()
            || map.TileAt(tile.X, tile.Y - 1).IsRaised();

        Place(EgoTraceKind.Remains, RemainsTarget, tile => terrain[tile.Y, tile.X] != EgoTerrain.Plains);
        Place(EgoTraceKind.Scorch, ScorchTarget, _ => true);
        Place(EgoTraceKind.Plaque, PlaqueTarget, tile => terrain[tile.Y, tile.X] == EgoTerrain.City && BesideWall(tile));
        Place(EgoTraceKind.Cache, CacheTarget, tile => map.TileAt(tile.X, tile.Y) == TileType.Default);
        return traces;
    }

    /// <summary>Floor scars for scorch traces, baked with the rest of the map's decorations.</summary>
    public static IEnumerable<PathDecoration> ScorchDecorations(IEnumerable<EgoTrace> traces, Random rng) =>
        traces.Where(trace => trace.Kind == EgoTraceKind.Scorch)
            .Select((trace, index) => new PathDecoration(
                rng.Next(2) == 0 ? PathDecorationKind.ScorchedCrater : PathDecorationKind.StormCrack,
                PathDecorationLayer.Floor, trace.World, 1.3f + (float)rng.NextDouble() * .9f, rng.Next(4), 2000 + index));
}
