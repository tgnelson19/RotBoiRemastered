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
    public const double FirstHunterDelaySeconds = 360;
    public const double HunterMinGapSeconds = 300;
    public const double HunterMaxGapSeconds = 480;
    /// <summary>Holdouts closer than this many views feud when both are awake and the player is near both.</summary>
    public const float SkirmishRangeViews = 1.5f;
    private Vector2 _lastPlayer;
    private Vector2 _playerHeading = Vector2.UnitX;

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
        TrackHeading(player);
        UpdateHunter(session, rng);
        UpdateRemains(session, player, rng);

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
        UpdateSkirmishes(session, player);
        state.CurrEnemyCount = state.EnemyHolster.Count;
    }

    private void TrackHeading(Vector2 player)
    {
        Vector2 delta = player - _lastPlayer;
        if (delta.LengthSquared() > 1f)
        {
            delta.Normalize();
            _playerHeading = Vector2.Lerp(_playerHeading, delta, .08f);
            if (_playerHeading.LengthSquared() < .01f) _playerHeading = delta;
        }
        _lastPlayer = player;
    }

    /// <summary>
    /// One hunter at a time, released behind the player's line of travel a
    /// few views out, the first after six minutes and then every 5-8 minutes
    /// once the last one is gone.
    /// </summary>
    private void UpdateHunter(GameSession session, Random rng)
    {
        var state = session.State;
        if (state.RunTimeSeconds < _run.NextHunterAt)
            return;
        if (state.EnemyHolster.Any(enemy => enemy is EgoHunter && !enemy.IsDead()))
            return;
        Vector2 behind = -_playerHeading;
        if (behind.LengthSquared() < .01f) behind = Vector2.UnitX;
        behind.Normalize();
        float distance = _run.ViewWidth * (2f + (float)rng.NextDouble());
        Vector2 target = session.PlayerWorldCenter + behind * distance;
        Rectangle? spot = session.Battleground.FindSpawnRectWithin((int)(Simulation.TileSize * 1.35f), target,
            _run.ViewWidth * .6f, session.PlayerWorldCenter, _run.ViewWidth, rng)
            ?? session.Battleground.FindSpawnRectWithin((int)(Simulation.TileSize * 1.35f), session.PlayerWorldCenter,
                _run.ViewWidth * 3f, session.PlayerWorldCenter, _run.ViewWidth * 1.5f, rng);
        if (spot is not Rectangle rect)
            return;
        string sense = CampaignProgression.SenseKeys[rng.Next(CampaignProgression.SenseKeys.Length)];
        var hunter = new EgoHunter(rect.X, rect.Y, sense, _run.ViewWidth * 3f, state.CurrentLevel, rng);
        session.ApplyRunDifficulty(hunter);
        state.EnemyHolster.Add(hunter);
        _run.HuntersReleased++;
        _run.NextHunterAt = state.RunTimeSeconds + HunterMinGapSeconds
            + rng.NextDouble() * (HunterMaxGapSeconds - HunterMinGapSeconds);
        session.ShowEntrySplash("Something Is Following", "It came from the way you came.", GamePaths.PathsByKey[sense].Accent);
        BossAudio.Emit(BossAudioCueKind.Declaration, sense, .6f);
    }

    /// <summary>Re-arm the hunter clock after one dies so the next waits its full gap.</summary>
    public void NotifyHunterGone(GameSession session, Random rng)
    {
        _run.NextHunterAt = session.State.RunTimeSeconds + HunterMinGapSeconds
            + rng.NextDouble() * (HunterMaxGapSeconds - HunterMinGapSeconds);
    }

    /// <summary>Remains traces become a one-item crate as the player comes within spawn range.</summary>
    private void UpdateRemains(GameSession session, Vector2 player, Random rng)
    {
        float spawnRadius = _run.SpawnRadius;
        foreach (EgoTrace trace in _run.Traces)
        {
            if (trace.Kind != EgoTraceKind.Remains || trace.Taken)
                continue;
            if (trace.Crate is LootCrate crate)
            {
                if (crate.Items.Count == 0 || !session.State.LootCrateList.Contains(crate))
                {
                    trace.Taken = true;
                    trace.Crate = null;
                }
                continue;
            }
            if (Vector2.DistanceSquared(trace.World, player) > spawnRadius * spawnRadius)
                continue;
            var drops = Items.GenerateDrops(1, rng, session.State.AnyHardModeActive, null,
                session.State.NewGamePlusLevel, session.State.IsTrueHardMode);
            if (drops.Count == 0)
            {
                trace.Taken = true;
                continue;
            }
            var spawned = new LootCrate(trace.World.X - Simulation.TileSize * .3f, trace.World.Y - Simulation.TileSize * .3f, drops);
            session.State.LootCrateList.Add(spawned);
            trace.Crate = spawned;
        }
    }

    /// <summary>Crates vanish with the overworld holster when a door is entered; forget them so they can re-form.</summary>
    public void ForgetRemainsCrates()
    {
        foreach (EgoTrace trace in _run.Traces)
            if (trace.Crate is not null && trace.Crate.Items.Count > 0)
                trace.Crate = null;
    }

    /// <summary>
    /// Two awake holdouts of different senses within SkirmishRangeViews of
    /// each other feud while the player is within spawn range of both: every
    /// member takes the nearest rival as its target when the player is out of
    /// its awareness.
    /// </summary>
    private void UpdateSkirmishes(GameSession session, Vector2 player)
    {
        float range = _run.ViewWidth * SkirmishRangeViews;
        float spawnRadius = _run.SpawnRadius;
        var feuding = new HashSet<EgoRegion>();
        var holdouts = _run.Regions.Where(region => region.IsHoldout && region.Populated && region.Live.Count > 0).ToList();
        for (int a = 0; a < holdouts.Count; a++)
            for (int b = a + 1; b < holdouts.Count; b++)
            {
                EgoRegion first = holdouts[a], second = holdouts[b];
                if (first.SenseKey == second.SenseKey)
                    continue;
                if (Vector2.Distance(first.Center, second.Center) > range + first.RadiusWorld + second.RadiusWorld)
                    continue;
                if (Vector2.Distance(first.Center, player) > spawnRadius || Vector2.Distance(second.Center, player) > spawnRadius)
                    continue;
                feuding.Add(first);
                feuding.Add(second);
                Assign(first, second);
                Assign(second, first);
            }
        foreach (EgoRegion region in _run.Regions)
        {
            if (feuding.Contains(region) || !region.IsHoldout)
                continue;
            foreach (Enemy enemy in region.Live)
                enemy.FeudTarget = null;
        }

        static void Assign(EgoRegion side, EgoRegion rivals)
        {
            foreach (Enemy enemy in side.Live)
            {
                if (enemy.IsDead() || enemy is VeteranGuardianBoss)
                    continue;
                if (enemy.FeudTarget is { } current && !current.IsDead() && rivals.Live.Contains(current))
                    continue;
                Vector2 center = EgoRun.EnemyCenter(enemy);
                enemy.FeudTarget = rivals.Live
                    .Where(rival => !rival.IsDead())
                    .OrderBy(rival => Vector2.DistanceSquared(EgoRun.EnemyCenter(rival), center))
                    .FirstOrDefault();
            }
        }
    }

    private void Despawn(GameSession session, Vector2 player)
    {
        float despawn = _run.DespawnRadius;
        _removeScratch.Clear();
        foreach (Enemy enemy in session.State.EnemyHolster)
        {
            if (enemy is FracturedAphantasia or VeteranGuardianBoss or EgoHunter || session.State.ActiveBoss == enemy)
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
