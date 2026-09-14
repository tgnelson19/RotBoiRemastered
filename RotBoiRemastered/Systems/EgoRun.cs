using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

public enum EgoRegionKind { Senseless, VeteranSenseless, Holdout, VeteranHoldout }

/// <summary>
/// One populatable patch of The Ego's overworld. Holdouts are placed discs
/// with a fixed sense; senseless regions are the coarse grid cells covering
/// everything else and roll a random sense every time they repopulate.
/// </summary>
public sealed class EgoRegion
{
    public EgoRegionKind Kind { get; init; }
    public string? SenseKey { get; set; }
    public Vector2 Center { get; init; }
    public float RadiusWorld { get; init; }
    public bool IsHoldout => Kind is EgoRegionKind.Holdout or EgoRegionKind.VeteranHoldout;
    public bool IsVeteran => Kind is EgoRegionKind.VeteranSenseless or EgoRegionKind.VeteranHoldout;

    /// <summary>Enemies this region currently owns. Cleared entries are pruned by the spawn director.</summary>
    public List<Enemy> Live { get; } = new();
    public bool Populated { get; set; }
    /// <summary>Seconds left before the region may repopulate (only counts down once cleared).</summary>
    public double RespawnTimer { get; set; }
    /// <summary>Once cleared, the player must leave the spawn radius before a respawn can arm.</summary>
    public bool AwaitingPlayerExit { get; set; }
    public int TimesCleared { get; set; }

    public bool Contains(Vector2 world) =>
        Vector2.DistanceSquared(world, Center) <= RadiusWorld * RadiusWorld;
}

public sealed record EgoDungeonPortal(string SenseKey, bool Veteran, Vector2 World);

/// <summary>
/// Runtime-only state for The Ego. Like <see cref="ExpeditionRun"/> it is never
/// serialized: dungeon and boss detours suspend it, while extraction, death,
/// or process exit abandon the map without touching permanent progress.
/// </summary>
public sealed class EgoRun
{
    public const double MidpointDungeonDropChance = 1.0 / 25.0;

    public int Seed { get; }
    /// <summary>Player's horizontal view width in world units at run start; every distance rule keys off it.</summary>
    public float ViewWidth { get; }
    public Battleground Battleground { get; }
    public Vector2 Spawn => Battleground.SpawnPosition;
    public IReadOnlyList<EgoRegion> Regions { get; }
    public IEnumerable<EgoRegion> Holdouts => Regions.Where(region => region.IsHoldout);

    public int MidpointBossesDefeated { get; private set; }
    public HashSet<string> VeteranBossesDefeated { get; } = new();
    public bool EventBossSpawned { get; set; }
    public bool EventBossDefeated { get; private set; }
    public Vector2? AphantasiaPortalWorld { get; private set; }
    public bool AphantasiaEntered { get; set; }
    public List<EgoDungeonPortal> DungeonPortals { get; } = new();
    public Vector2? SuspendedReturnPosition { get; private set; }
    public EgoDungeonPortal? ActiveDungeon { get; private set; }

    public float SpawnRadius => ViewWidth * EgoWorldGenerator.SpawnRadiusViews;
    public float DespawnRadius => ViewWidth * EgoWorldGenerator.DespawnRadiusViews;
    public float VeteranMinDistance => ViewWidth * EgoWorldGenerator.VeteranMinDistanceViews;

    public EgoRun(int? seed = null, float viewWidth = 1920f)
    {
        Seed = seed ?? Random.Shared.Next();
        ViewWidth = Math.Max(400f, viewWidth);
        var rng = new Random(Seed);
        Battleground = EgoWorldGenerator.Generate(rng);
        Regions = EgoWorldGenerator.PlaceRegions(Battleground, rng, ViewWidth);
    }

    public bool IsVeteranSpace(Vector2 world) =>
        Vector2.Distance(world, Spawn) >= VeteranMinDistance;

    /// <summary>Roll a dungeon drop for a slain overworld enemy. Veteran leaders always drop.</summary>
    public EgoDungeonPortal? RollDungeonDrop(Enemy enemy, string? senseKey, Random rng)
    {
        if (senseKey is null || !CampaignProgression.SenseKeys.Contains(senseKey))
            return null;
        if (enemy is VeteranGuardianBoss)
            return AddPortal(new EgoDungeonPortal(senseKey, Veteran: true, EnemyCenter(enemy)));
        if (enemy is PathGuardianBoss or PathChaseBoss or FracturedAphantasia)
            return null;
        if (rng.NextDouble() >= MidpointDungeonDropChance)
            return null;
        return AddPortal(new EgoDungeonPortal(senseKey, Veteran: false, EnemyCenter(enemy)));
    }

    public static Vector2 EnemyCenter(Enemy enemy) =>
        new(enemy.WorldX + enemy.Size / 2f, enemy.WorldY + enemy.Size / 2f);

    public EgoDungeonPortal AddPortal(EgoDungeonPortal portal)
    {
        DungeonPortals.Add(portal);
        return portal;
    }

    public bool EnterDungeon(EgoDungeonPortal portal, Vector2 returnPosition)
    {
        if (!DungeonPortals.Contains(portal))
            return false;
        ActiveDungeon = portal;
        SuspendedReturnPosition = returnPosition;
        return true;
    }

    public bool CompleteDungeon()
    {
        if (ActiveDungeon is null)
            return false;
        DungeonPortals.Remove(ActiveDungeon);
        if (ActiveDungeon.Veteran)
            VeteranBossesDefeated.Add(ActiveDungeon.SenseKey);
        else
            MidpointBossesDefeated++;
        ActiveDungeon = null;
        return true;
    }

    public void AbandonDungeon()
    {
        ActiveDungeon = null;
    }

    public void RecordEventBossDefeated(Vector2 world)
    {
        EventBossDefeated = true;
        AphantasiaPortalWorld = world;
    }

    public void SuspendForAphantasia(Vector2 returnPosition)
    {
        SuspendedReturnPosition = returnPosition;
        AphantasiaEntered = true;
    }

    public EgoRegion? RegionAt(Vector2 world)
    {
        EgoRegion? holdout = Regions.FirstOrDefault(region => region.IsHoldout && region.Contains(world));
        if (holdout is not null)
            return holdout;
        return Regions
            .Where(region => !region.IsHoldout)
            .OrderBy(region => Vector2.DistanceSquared(region.Center, world))
            .FirstOrDefault();
    }
}
