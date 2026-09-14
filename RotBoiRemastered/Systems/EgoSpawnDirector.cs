using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

/// <summary>
/// Proximity population for The Ego's overworld. Replaces the arena spawn
/// timer: regions populate as the player comes within
/// <see cref="EgoRun.SpawnRadius"/>, never inside the player's view, respawn
/// on their own clocks once cleared *and* the player has stepped back out of
/// spawn range, and shed any enemy that wanders past
/// <see cref="EgoRun.DespawnRadius"/>.
/// </summary>
public sealed class EgoSpawnDirector
{
    private readonly EgoRun _run;
    private readonly List<Enemy> _removeScratch = new();

    public EgoSpawnDirector(EgoRun run)
    {
        _run = run;
    }

    public EgoRun Run => _run;

    /// <summary>Pure rule: may this region populate right now?</summary>
    public static bool CanPopulate(EgoRegion region, float distanceToPlayer, float spawnRadius)
    {
        if (region.Populated || distanceToPlayer > spawnRadius)
            return false;
        if (region.AwaitingPlayerExit || region.RespawnTimer > 0)
            return false;
        return true;
    }

    /// <summary>Pure rule: advance a cleared region's respawn clock / exit gate.</summary>
    public static void TickRegion(EgoRegion region, float distanceToPlayer, float spawnRadius, double dtSeconds)
    {
        if (region.Populated)
            return;
        if (region.AwaitingPlayerExit && distanceToPlayer > spawnRadius)
            region.AwaitingPlayerExit = false;
        if (region.RespawnTimer > 0)
            region.RespawnTimer = Math.Max(0, region.RespawnTimer - dtSeconds);
    }

    public void Update(GameSession session, Random rng, double dtSeconds)
    {
        Vector2 player = session.PlayerWorldCenter;
        float spawnRadius = _run.SpawnRadius;
        float viewRadius = _run.ViewWidth;
        var state = session.State;
        var caps = Progression.EncounterCaps(state.CurrentLevel);
        state.EnemyCap = caps.EnemyCap;
        state.EnemyThreatCap = caps.ThreatCap;
        state.EnemyPopulationThreatCap = caps.PopulationThreatCap;

        Despawn(session, player);

        foreach (EgoRegion region in _run.Regions)
        {
            float distance = Vector2.Distance(region.Center, player);
            if (region.Populated)
            {
                region.Live.RemoveAll(enemy => enemy.IsDead() || !state.EnemyHolster.Contains(enemy));
                if (region.Live.Count == 0)
                {
                    region.Populated = false;
                    region.TimesCleared++;
                    region.RespawnTimer = region.IsHoldout
                        ? EgoWorldGenerator.HoldoutRespawnSeconds
                        : EgoWorldGenerator.SenselessRespawnSeconds;
                    region.AwaitingPlayerExit = true;
                }
                continue;
            }
            TickRegion(region, distance, spawnRadius, dtSeconds);
            if (distance > spawnRadius + region.RadiusWorld)
                continue;
            if (!CanPopulate(region, Math.Max(0, distance - region.RadiusWorld), spawnRadius))
                continue;
            if (state.EnemyHolster.Count >= state.EnemyCap)
                continue;
            Populate(session, region, player, viewRadius, rng);
        }
        state.CurrEnemyCount = state.EnemyHolster.Count;
    }

    private void Despawn(GameSession session, Vector2 player)
    {
        float despawn = _run.DespawnRadius;
        _removeScratch.Clear();
        foreach (Enemy enemy in session.State.EnemyHolster)
        {
            if (enemy is FracturedAphantasia or VeteranGuardianBoss || session.State.ActiveBoss == enemy)
                continue;
            if (Vector2.DistanceSquared(EgoRun.EnemyCenter(enemy), player) > despawn * despawn)
                _removeScratch.Add(enemy);
        }
        if (_removeScratch.Count == 0)
            return;
        foreach (Enemy enemy in _removeScratch)
            session.State.EnemyHolster.Remove(enemy);
        foreach (EgoRegion region in _run.Regions)
        {
            if (!region.Populated)
                continue;
            region.Live.RemoveAll(_removeScratch.Contains);
            if (region.Live.Count == 0)
            {
                // Walked away from a live camp: it simply re-forms on return.
                region.Populated = false;
                region.RespawnTimer = 0;
                region.AwaitingPlayerExit = false;
            }
        }
    }

    private void Populate(GameSession session, EgoRegion region, Vector2 player, float viewRadius, Random rng)
    {
        var state = session.State;
        string sense = region.IsHoldout
            ? region.SenseKey!
            : CampaignProgression.SenseKeys[rng.Next(CampaignProgression.SenseKeys.Length)];
        region.SenseKey = sense;
        int level = region.Kind switch
        {
            EgoRegionKind.Senseless => Math.Max(1, state.CurrentLevel - rng.Next(0, 3)),
            EgoRegionKind.VeteranSenseless => Math.Min(Progression.DungeonMaxLevel, state.CurrentLevel + 2),
            EgoRegionKind.Holdout => Math.Max(1, state.CurrentLevel),
            _ => Math.Min(Progression.DungeonMaxLevel, state.CurrentLevel + 3),
        };
        int groups = region.Kind switch
        {
            EgoRegionKind.Holdout => rng.Next(2, 4),
            EgoRegionKind.VeteranHoldout => rng.Next(3, 5),
            _ => 1,
        };

        string previousSense = GamePaths.Active().Key;
        GamePaths.SetActive(sense);
        try
        {
            region.Populated = true;
            region.Live.Clear();
            for (int groupIndex = 0; groupIndex < groups; groupIndex++)
            {
                Rectangle? anchor = session.Battleground.FindSpawnRectWithin(
                    (int)(Simulation.TileSize * 1.1f), region.Center, region.RadiusWorld, player, viewRadius, rng);
                if (anchor is null)
                    continue;
                double threatBudget = state.EnemyPopulationThreatCap - state.EnemyHolster.Sum(enemy => enemy.ThreatCost);
                if (threatBudget <= 0)
                    break;
                var patrol = EnemyCatalog.Shared.SpawnPatrol(level, threatBudget, session.Battleground, player,
                    session.AwarenessRange, session.ScreenHeight, state.EnemyHolster, rng,
                    contentPath: sense, anchorOverride: anchor);
                if (patrol is null)
                    continue;
                if (state.EnemyHolster.Count + patrol.Value.Group.Count > state.EnemyCap)
                    break;
                foreach (Enemy enemy in patrol.Value.Group)
                {
                    session.ApplyRunDifficulty(enemy);
                    enemy.ContentPath ??= sense;
                    if (region.IsHoldout)
                        enemy.EncounterKey = $"ego_{sense}_{(region.IsVeteran ? "veteran" : "holdout")}";
                }
                state.EnemyHolster.AddRange(patrol.Value.Group);
                region.Live.AddRange(patrol.Value.Group);
            }
            if (region.Kind == EgoRegionKind.VeteranHoldout)
            {
                Rectangle? leaderRect = session.Battleground.FindSpawnRectWithin(
                    (int)(Simulation.TileSize * 1.7f), region.Center, region.RadiusWorld * .35f, player, viewRadius, rng);
                if (leaderRect is Rectangle spot)
                {
                    var leader = new VeteranGuardianBoss(spot.X, spot.Y, sense, session.AwarenessRange, rng);
                    session.ApplyRunDifficulty(leader);
                    leader.EncounterKey = $"ego_{sense}_veteran";
                    state.EnemyHolster.Add(leader);
                    region.Live.Add(leader);
                }
            }
            if (region.Live.Count == 0)
            {
                // Nothing fit (view too close / cap hit): try again next tick.
                region.Populated = false;
            }
        }
        finally
        {
            GamePaths.SetActive(previousSense);
        }
    }

    /// <summary>Region the slain enemy belonged to, for bounty labels / stats.</summary>
    public EgoRegion? OwnerOf(Enemy enemy) =>
        _run.Regions.FirstOrDefault(region => region.Live.Contains(enemy));
}
