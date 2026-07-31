using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

/// <summary>The boss tier assigned to the current generated Path floor.</summary>
public enum PathFloorBossTier
{
    Guardian,
    Midpoint,
    Finale,
}

/// <summary>
/// Owns the ten-floor structure of the composite Path game mode. Each
/// five-floor act is a shuffled permutation of all senses, and the second
/// permutation is adjusted so floor six never immediately repeats floor five.
/// </summary>
public sealed class PathRun
{
    public const int TotalFloors = 10;
    public const int FloorsPerAct = 5;
    public const double TitleBannerSeconds = 3.6;
    public const double RoomBannerSeconds = 2.25;

    private readonly List<string> _senseOrder;
    private readonly int[] _floorSeeds;
    private readonly HashSet<int> _treasureFloors;
    private readonly List<PathRoom> _activeCombatRooms = new();
    private Task<PreparedPathFloor>? _nextFloorPreparation;
    private PathFogOfWar? _installedPreparedFog;

    private sealed record PreparedPathFloor(
        int FloorNumber,
        PathFloorLayout Layout,
        PathFogOfWar Fog);

    public int FloorNumber { get; private set; } = 1;
    public string CurrentSenseKey => _senseOrder[FloorNumber - 1];
    public GamePath CurrentSense => GamePaths.PathsByKey[CurrentSenseKey];
    public bool IsSecondAct => FloorNumber > FloorsPerAct;
    public PathFloorBossTier BossTier => FloorNumber switch
    {
        FloorsPerAct => PathFloorBossTier.Midpoint,
        TotalFloors => PathFloorBossTier.Finale,
        _ => PathFloorBossTier.Guardian,
    };
    public PathFloorLayout Layout { get; private set; }
    public IReadOnlyList<PathRoom> ActiveCombatRooms => _activeCombatRooms;
    public bool ExitPortalOpen { get; private set; }
    public bool IsComplete { get; private set; }
    public double FloorStartedAtRunSeconds { get; private set; }
    public PathRoom? LastEnteredRoom { get; private set; }
    public double RoomEnteredAtRunSeconds { get; private set; }
    public IReadOnlyList<string> SenseOrder => _senseOrder;
    public IReadOnlySet<int> TreasureFloors => _treasureFloors;

    public string SenseDisplayName => CurrentSenseKey switch
    {
        "chemesthesis" => "Chemesthesis",
        _ => char.ToUpperInvariant(CurrentSenseKey[0]) + CurrentSenseKey[1..],
    };
    public string TitleBanner => $"Traversing the Path of {SenseDisplayName}";
    public Vector2 ExitPortalWorld => Layout.BossRoom.WorldCenter;

    public DungeonFloorDifficultyProfile DifficultyProfile =>
        DungeonFloorDifficultyProfile.ForFloor(FloorNumber);
    public double HealthMultiplier => DifficultyProfile.Health;
    public double DamageMultiplier => DifficultyProfile.Damage;
    public double TimingMultiplier => DifficultyProfile.Timing;
    public int ComplexityTier => DifficultyProfile.Complexity;
    // Movement speed is authored per enemy/boss. The old blanket second-act
    // multiplier made collision-heavy enemies and bosses accelerate without
    // changing their warning language.
    public double SpeedMultiplier => 1.0;

    public PathRun(Random? rng = null)
    {
        rng ??= Random.Shared;
        _senseOrder = BuildSenseOrder(rng);
        _floorSeeds = new int[TotalFloors];
        for (int index = 0; index < _floorSeeds.Length; index++)
            _floorSeeds[index] = rng.Next();
        _treasureFloors = SelectTreasureFloors(rng);
        Layout = GenerateFloor(FloorNumber);
    }

    private PathFloorLayout GenerateFloor(int floorNumber) =>
        PathFloorGenerator.Generate(
            _senseOrder[floorNumber - 1],
            floorNumber,
            new Random(_floorSeeds[floorNumber - 1]),
            _treasureFloors.Contains(floorNumber));

    private static HashSet<int> SelectTreasureFloors(Random rng)
    {
        var selected = new HashSet<int>();
        SelectFrom([1, 2, 3, 4]);
        SelectFrom([6, 7, 8, 9]);
        return selected;

        void SelectFrom(int[] candidates)
        {
            for (int index = candidates.Length - 1; index > 0; index--)
            {
                int swap = rng.Next(index + 1);
                (candidates[index], candidates[swap]) =
                    (candidates[swap], candidates[index]);
            }
            selected.Add(candidates[0]);
            selected.Add(candidates[1]);
        }
    }

    /// <summary>
    /// Builds the next immutable floor and its initial visibility solution on
    /// a worker while the player is still exploring the current floor.
    /// AdvanceFloor normally consumes an already-complete result instead of
    /// blocking the render thread at the portal.
    /// </summary>
    internal Task PrepareNextFloorAsync(float playerSize)
    {
        if (FloorNumber >= TotalFloors)
            return Task.CompletedTask;
        if (_nextFloorPreparation is not null)
            return _nextFloorPreparation;

        int nextFloor = FloorNumber + 1;
        string senseKey = _senseOrder[nextFloor - 1];
        int seed = _floorSeeds[nextFloor - 1];
        _nextFloorPreparation = Task.Run(() =>
        {
            PathFloorLayout layout = PathFloorGenerator.Generate(
                senseKey,
                nextFloor,
                new Random(seed),
                _treasureFloors.Contains(nextFloor));
            var fog = new PathFogOfWar(layout.Battleground);
            Vector2 observer = layout.Battleground.SpawnPosition
                + new Vector2(playerSize / 2f);
            fog.Update(observer);
            return new PreparedPathFloor(nextFloor, layout, fog);
        });
        return _nextFloorPreparation;
    }

    internal bool NextFloorPreparationCompleted =>
        _nextFloorPreparation?.IsCompletedSuccessfully == true;

    internal PathFogOfWar? TakeInstalledPreparedFog()
    {
        PathFogOfWar? fog = _installedPreparedFog;
        _installedPreparedFog = null;
        return fog;
    }

    private static List<string> BuildSenseOrder(Random rng)
    {
        List<string> Shuffle()
        {
            var keys = GamePaths.Paths.Select(path => path.Key).ToList();
            for (int index = keys.Count - 1; index > 0; index--)
            {
                int swap = rng.Next(index + 1);
                (keys[index], keys[swap]) = (keys[swap], keys[index]);
            }
            return keys;
        }

        var first = Shuffle();
        var second = Shuffle();
        if (second[0] == first[^1])
        {
            int swap = second.FindIndex(1, key => key != first[^1]);
            (second[0], second[swap]) = (second[swap], second[0]);
        }
        first.AddRange(second);
        return first;
    }

    public bool TitleBannerVisible(double runTimeSeconds) =>
        runTimeSeconds - FloorStartedAtRunSeconds < TitleBannerSeconds;

    public bool RoomBannerVisible(double runTimeSeconds) =>
        LastEnteredRoom is not null
        && runTimeSeconds - RoomEnteredAtRunSeconds < RoomBannerSeconds;

    /// <summary>
    /// Clears every activated non-boss encounter whose tagged enemies are
    /// gone. More than one room may be active at once: rushing forward never
    /// closes a threshold behind the player.
    /// </summary>
    public IReadOnlyList<PathRoom> CompleteReadyCombatRooms(
        IReadOnlyList<Enemy> enemies,
        IReadOnlySet<string>? pendingEncounterKeys = null)
    {
        List<PathRoom>? completedRooms = null;
        for (int roomIndex = _activeCombatRooms.Count - 1; roomIndex >= 0; roomIndex--)
        {
            PathRoom room = _activeCombatRooms[roomIndex];
            if (pendingEncounterKeys?.Contains(room.EncounterKey) == true)
                continue;
            bool hasLivingEnemy = false;
            for (int enemyIndex = 0; enemyIndex < enemies.Count; enemyIndex++)
            {
                if (enemies[enemyIndex].EncounterKey == room.EncounterKey)
                {
                    hasLivingEnemy = true;
                    break;
                }
            }
            if (hasLivingEnemy)
                continue;
            room.IsCleared = true;
            _activeCombatRooms.RemoveAt(roomIndex);
            (completedRooms ??= new List<PathRoom>()).Add(room);
        }
        // Reverse traversal makes removal allocation-free; one in-place
        // reversal restores the activation order exposed by the old query.
        completedRooms?.Reverse();
        return completedRooms is null
            ? Array.Empty<PathRoom>()
            : completedRooms;
    }

    /// <summary>
    /// Activates a room on first entry. Room activation never prevents another
    /// room from activating, so players can rush ahead and accumulate pursuing
    /// encounters. Treasure rooms are combat rooms and only clear after their
    /// guardian-strength encounter is defeated.
    /// </summary>
    public PathRoom? TryActivateRoom(Vector2 playerWorldCenter, double runTimeSeconds = 0)
    {
        if (ExitPortalOpen || IsComplete)
            return null;
        var room = Layout.RoomAt(playerWorldCenter);
        if (room is null || room.IsActivated)
            return null;

        room.IsActivated = true;
        LastEnteredRoom = room;
        RoomEnteredAtRunSeconds = runTimeSeconds;
        if (room.IsCombatRoom)
            _activeCombatRooms.Add(room);
        else if (room.Type != PathRoomType.Boss)
            room.IsCleared = true;
        return room;
    }

    public void NotifyBossDefeated()
    {
        var bossRoom = Layout.BossRoom;
        bossRoom.IsActivated = true;
        bossRoom.IsCleared = true;
        if (FloorNumber >= TotalFloors)
            IsComplete = true;
        else
            ExitPortalOpen = true;
    }

    public bool PlayerAtExitPortal(Rectangle playerWorldRect, int radius)
    {
        if (!ExitPortalOpen)
            return false;
        var rect = new Rectangle(
            (int)ExitPortalWorld.X - radius,
            (int)ExitPortalWorld.Y - radius,
            radius * 2,
            radius * 2);
        return playerWorldRect.Intersects(rect);
    }

    /// <summary>Generates and installs the next floor while retaining run-level progression.</summary>
    public bool AdvanceFloor(double runTimeSeconds)
    {
        if (!ExitPortalOpen || FloorNumber >= TotalFloors)
            return false;
        int nextFloor = FloorNumber + 1;
        PreparedPathFloor? prepared =
            _nextFloorPreparation?.GetAwaiter().GetResult();
        FloorNumber = nextFloor;
        if (prepared?.FloorNumber == nextFloor)
        {
            Layout = prepared.Layout;
            _installedPreparedFog = prepared.Fog;
        }
        else
        {
            Layout = GenerateFloor(FloorNumber);
            _installedPreparedFog = null;
        }
        _nextFloorPreparation = null;
        _activeCombatRooms.Clear();
        ExitPortalOpen = false;
        FloorStartedAtRunSeconds = runTimeSeconds;
        LastEnteredRoom = null;
        return true;
    }
}
