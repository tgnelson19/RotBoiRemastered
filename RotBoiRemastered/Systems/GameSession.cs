using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

/// <summary>What HandleLevelingProcess determined should happen, matching MenuAction's return-a-result shape.</summary>
public enum LevelUpOutcome { StillChoosing, ContinueLeveling, ReturnToGame }

/// <summary>
/// Ported from character.py's selectBountyTarget() return dict. `Target` is
/// either an <see cref="Enemy"/> or a <see cref="RuntimeEncounter"/> (Python's
/// heterogeneous dict value) -- InformationSheet.BountyDetails is the only
/// place that needs to tell them apart.
/// </summary>
public sealed record BountyInfo(Vector2 World, double Score, string Label, object Target);

/// <summary>
/// One run in progress: owns the player, run state, battleground, camera,
/// and leveling screen, and orchestrates them each frame. Ported from
/// character.py's "handling*"/"update*"/"draw*" free functions plus
/// resetAllStats()/combarinoPlayerStats()/handleLevelingProcess() --
/// module-level functions reaching into characterStats.py's globals become
/// instance methods on one session object, same cleanup as every other
/// stateful module in this port.
///
/// The complete boss roster, path-specific boss selection/enemy identity,
/// arena constraints, projectile containment, portal routing, bounty and
/// combat HUD overlays are all orchestrated here.
/// </summary>
public sealed class GameSession
{
    public ModeEntrySplash EntrySplash { get; } = new();
    public void ShowEntrySplash(string title, string flavor, Color accent)
    {
        EntrySplash.Show(title, flavor, accent);
        // The title band briefly obscures the middle of the arena. Matching
        // damage grace to its lifetime prevents a new mode from attacking a
        // player before its objective and identity have finished appearing.
        State.GracePeriod = Math.Max(State.GracePeriod,
            Simulation.FrameRate * ModeEntrySplash.Duration);
    }
    public void UpdateEntrySplash(double seconds) => EntrySplash.Update(seconds);
    public void DrawEntrySplash(SpriteBatch spriteBatch) => EntrySplash.Draw(spriteBatch, ScreenWidth, ScreenHeight);
    public const double BossHealthMultiplier = 2.0;
    /// <summary>
    /// Emergency safety ceiling, not a pattern-density tool. Boss patterns
    /// are authored to reach the arena boundary and expire naturally before
    /// this limit; exceeding it means old bullets are being truncated.
    /// </summary>
    public const int MaxBossProjectiles = 360;
    /// <summary>
    /// Speed gained per second by a projectile swept via
    /// <see cref="Enemy.TransitionSweepRequested"/> -- large enough that
    /// even a slow shot crosses a boss arena and exits its radial boundary
    /// (where the existing arena-bound check removes it) within roughly two
    /// seconds of the sweep firing. Halved from its original 9f: the sweep
    /// now runs under a phase interlude several seconds long, so the shots
    /// have time to leave, and the old value made them snap off-screen fast
    /// enough to read as a glitch rather than as the arena being cleared.
    /// </summary>
    private const float TransitionSweepAcceleration = 4.5f;
    private const int CrateInteractRadius = 24;
    private const int MaxLootCrates = 40;
    private const int BossPortalInteractRadius = 40;
    private const double HostileMinDamage = 25;
    private const double HostileDamageFloorRatio = .1;
    public const double FragmentDropChance = 1.0 / 3.0;

    public RunState State { get; } = new();
    public Player Player { get; private set; }
    public Battleground Battleground { get; private set; }
    public Camera Camera { get; } = new();
    public LevelingHandler LevelingHandler { get; private set; }
    public ReforgeHandler ReforgeHandler { get; private set; }
    public InformationSheet InformationSheet { get; private set; }
    public FooterHud FooterHud { get; } = new();
    public RunRewardSummary? LastRunRewardSummary { get; private set; }
    public PathRun? PathRun { get; private set; }
    public ExpeditionRun? Expedition { get; private set; }
    public CampaignActivity? CampaignActivity { get; private set; }
    public string? CampaignActivitySense { get; private set; }
    public bool AphantasiaPrecombatDraftsPending =>
        CampaignActivity == Systems.CampaignActivity.Aphantasia
        && State.PendingLevelUps > 0;
    public PathFogOfWar? PathFog { get; private set; }
    public bool IsPathMode => PathRun is not null;
    public bool IsPathFogActive => _pathFogActive && PathFog is not null;
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }
    public Vector2 ScreenShake { get; set; } = Vector2.Zero;
    public Rectangle CombatViewport => new(0, 0, ScreenWidth, ScreenHeight);
    public Rectangle HudSafeArea => FooterHud.SafeArea(ScreenWidth, ScreenHeight);

    // Survives ResetAll -- it re-bakes lazily against the new Battleground
    // reference on the next DrawBackground call, no explicit reset needed.
    private readonly ArenaRenderer _arenaRenderer = new();
    private readonly WorldLighting _worldLighting = new();
    private readonly List<ArenaLightPost> _arenaLightPosts = new();
    private readonly List<WorldLightSource> _worldLightSources = new();
    private readonly BitVfxSystem _visualEffects = new();
    private readonly List<WorldDepthDrawItem> _worldDepthItemScratch = new();
    private readonly HashSet<int> _drawnEncounterIdScratch = new();
    private readonly Action<SpriteBatch, WorldDepthDrawItem> _drawWorldDepthItem;
    private readonly List<RuntimeEncounter> _encounterScratch = new();
    private readonly HashSet<int> _encounterIdScratch = new();
    private readonly List<Enemy> _ungroupedEnemyScratch = new();
    private readonly List<SpawnedEnemyGroup> _spawnedGroupScratch = new();
    private readonly List<Enemy> _spawnedEnemyScratch = new();
    private readonly HashSet<Enemy> _rejectedOwnerScratch = new(ReferenceEqualityComparer.Instance);
    private readonly List<EnemyProjectile> _spawnedProjectileScratch = new();
    private readonly HashSet<EnemyProjectile> _campaignTunedProjectiles =
        new(ReferenceEqualityComparer.Instance);
    private int _campaignProjectileSequence;
    private readonly HashSet<int> _worldEncounterIdScratch = new();
    private readonly SpatialHash<Enemy> _enemyCollisionGrid =
        new(Math.Max(64, (int)(Simulation.TileSize * 2)));
    private readonly Dictionary<Enemy, IReadOnlyList<(string Part, Rectangle Rect)>>
        _enemyHitboxScratch = new(ReferenceEqualityComparer.Instance);
    private readonly List<Enemy> _collisionCandidateScratch = new();
    private readonly List<Enemy> _orderedCollisionCandidateScratch = new();
    private readonly HashSet<Enemy> _collisionCandidateSet =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Enemy> _deadEnemyScratch =
        new(ReferenceEqualityComparer.Instance);
    private readonly EnemyUpdateContext _enemyUpdateContext;
    private readonly Comparison<RuntimeEncounter> _encounterDistanceComparison;
    private readonly Comparison<Enemy> _enemyDistanceComparison;
    private readonly HashSet<int> _bountyEncounterIdScratch = new();
    private readonly Dictionary<int, double> _roomClearedAt = new();
    private readonly Dictionary<int, float> _roomVisualEnergy = new();
    private readonly List<PendingPathWave> _pendingPathWaves = new();
    private readonly HashSet<string> _pendingPathEncounterKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _preloadedPathEncounterKeys =
        new(StringComparer.Ordinal);
    private const int PathWaveSpawnBudgetPerFrame = 3;
    private int _pathWaveSpawnBudgetRemaining;
    private VisualDensity _visualDensity = new(1, 1, 1, 1, 1);
    private PlayerBuildSnapshot? _playerBuildSnapshot;
    private bool _pathFogActive;
    private Vector2 _enemySortCenter;
    private string? _activeBossKey;
    private BossEncounterTelemetryTracker? _bossTelemetry;
    private bool _bossTelemetryDeathCueEmitted;
    private double _controllerPromptRemaining;
    private bool _debugVisualGallery;
    private string _debugVisualGalleryPath = "sound";
    private string _debugVisualGalleryTier = "easy";
    private DungeonBossInstanceState? _dungeonBossInstance;

    private readonly record struct SpawnedEnemyGroup(
        Enemy Owner,
        int Start,
        int Count,
        bool Atomic);

    private sealed class PendingPathWave
    {
        public required PathRoom Room { get; init; }
        public required Random Rng { get; init; }
        public required IReadOnlyList<EnemyDefinition> Definitions { get; init; }
        public required int EncounterLevel { get; init; }
        public required int Count { get; init; }
        public required bool GuardianStrength { get; init; }
        public Dictionary<string, int> FamilyCounts { get; } =
            new(StringComparer.Ordinal);
        public List<Enemy> Spawned { get; } = new();
        public int NextIndex { get; set; }
    }

    internal bool HasPendingPathWaves => _pendingPathWaves.Count > 0;

    private static readonly string[] LongHallRoles =
        ["artillery", "control", "artillery", "pressure"];
    private static readonly string[] GrandArenaRoles =
        ["pressure", "artillery", "tank", "support"];
    private static readonly string[] MazeRoles =
        ["pressure", "tank", "control", "pressure"];
    private static readonly string[] CrossroadsRoles =
        ["control", "pressure", "artillery", "pressure"];
    private static readonly string[] RingRoles =
        ["artillery", "control", "pressure", "support"];
    private static readonly string[] RuinRoles =
        ["pressure", "control", "tank"];
    private static readonly string[] DefaultRoomRoles =
        ["pressure", "artillery", "control", "tank"];

    public bool BossTelemetryActive => _bossTelemetry is not null;
    public bool DungeonBossInstanceActive => _dungeonBossInstance is not null;
    private bool SoulCampaignFinaleActive =>
        PathRun is { IsSecretDungeon: true, BossTier: PathFloorBossTier.Finale }
        && Expedition?.World == CampaignWorld.Soul;
    public bool PreferControllerPrompts => _controllerPromptRemaining > 0;
    public VisualDensity CurrentVisualDensity => _visualDensity;
    public bool CanExtract => !State.NoExtract
        && !State.GameCompleted
        && CampaignActivity switch
        {
            Systems.CampaignActivity.Arena => State.BeaudisDefeated,
            Systems.CampaignActivity.Core => PathRun is not null
                && (PathRun.FloorNumber > global::RotBoiRemastered.Systems.PathRun.FloorsPerAct
                    || PathRun.FloorNumber == global::RotBoiRemastered.Systems.PathRun.FloorsPerAct
                    && PathRun.ExitPortalOpen),
            Systems.CampaignActivity.Body or Systems.CampaignActivity.Soul =>
                Expedition?.DefeatedGuardians > 0,
            Systems.CampaignActivity.Aphantasia => false,
            _ => State.BeaudisDefeated,
        };
    internal IReadOnlyList<ArenaLightPost> ArenaLightPosts =>
        _arenaLightPosts;

    public Vector2 PlayerWorldCenter => new(
        Player.WorldX + (float)State.PlayerSize / 2f,
        Player.WorldY + (float)State.PlayerSize / 2f);

    /// <summary>
    /// Screen-height-derived default awareness range, matching the value
    /// Python's Enemy.__init__ used to compute internally from `vH.sH`
    /// before that became an explicit constructor parameter (see
    /// Entities/Enemy.cs's cleanup notes).
    /// </summary>
    public float AwarenessRange => ScreenHeight * .5f;

    /// <summary>Combat text has an independent accessibility scale and intentionally compact base size.</summary>
    public double DamageTextFontSize => Math.Max(8, Math.Round(18
        * Math.Clamp(GameProfile.Profile.DamageTextSize, UiTheme.MinDamageTextScale, UiTheme.MaxDamageTextScale)));

    public GameSession(Battleground battleground, int screenWidth, int screenHeight, Random? rng = null)
    {
        _drawWorldDepthItem = DrawWorldDepthItem;
        _encounterDistanceComparison = CompareEncountersForPressure;
        _enemyDistanceComparison = CompareEnemiesForPressure;
        _enemyUpdateContext = new EnemyUpdateContext
        {
            PlayerWorldX = 0,
            PlayerWorldY = 0,
            Battleground = battleground,
        };
        Battleground = battleground;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Player = new Player(battleground.SpawnPosition.X, battleground.SpawnPosition.Y);
        LevelingHandler = new LevelingHandler(screenWidth, screenHeight, rng);
        ReforgeHandler = new ReforgeHandler(screenWidth, screenHeight);
        InformationSheet = new InformationSheet(screenWidth, screenHeight);
        Camera.Lock = new Vector2(screenWidth / 2f, screenHeight / 2f);
        Camera.ConfigureViewport(screenWidth, screenHeight, GameProfile.Profile.CameraZoom, resetZoom: true);
        RefreshLightingFixtures();
        LoadCarriedItems();
    }

    /// <summary>Ported from character.py's resetAllStats() (the parts not already covered by RunState.Reset()).</summary>
    public void ResetAll(Battleground battleground, Random? rng = null)
    {
        _enemyCollisionGrid.Reset();
        _visualEffects.Clear();
        _campaignTunedProjectiles.Clear();
        _campaignProjectileSequence = 0;
        _playerBuildSnapshot = null;
        PathRun = null;
        Expedition = null;
        CampaignActivity = null;
        CampaignActivitySense = null;
        PathFog = null;
        _pathFogActive = false;
        _roomClearedAt.Clear();
        _roomVisualEnergy.Clear();
        LastRunRewardSummary = null;
        State.Reset();
        Battleground = battleground;
        Player = new Player(battleground.SpawnPosition.X, battleground.SpawnPosition.Y);
        Camera.SetAngle(0);
        ScreenShake = Vector2.Zero;
        LevelingHandler = new LevelingHandler(ScreenWidth, ScreenHeight, rng);
        ReforgeHandler = new ReforgeHandler(ScreenWidth, ScreenHeight);
        InformationSheet = new InformationSheet(ScreenWidth, ScreenHeight);
        Camera.Lock = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
        Camera.ConfigureViewport(ScreenWidth, ScreenHeight, GameProfile.Profile.CameraZoom, resetZoom: true);
        _activeBossKey = null;
        _bossTelemetry = null;
        _bossTelemetryDeathCueEmitted = false;
        _controllerPromptRemaining = 0;
        _debugVisualGallery = false;
        _dungeonBossInstance = null;
        RefreshLightingFixtures();
        LoadCarriedItems();
    }

    /// <summary>
    /// Refreshes The Mind's dev-controlled gate geometry without treating the
    /// change as a new run. Live position, equipment, inventory, and stats stay
    /// attached to the existing player/session.
    /// </summary>
    internal void RefreshMindBattleground(Battleground battleground)
    {
        Battleground = battleground;
        RefreshLightingFixtures();
    }

    /// <summary>Starts a fresh ten-floor composite Path run.</summary>
    public void StartPathRun(Random? rng = null)
    {
        _enemyCollisionGrid.Reset();
        _visualEffects.Clear();
        _playerBuildSnapshot = null;
        rng ??= Random.Shared;
        var pathRun = new PathRun(rng);
        InstallPathRun(pathRun, rng);
        CampaignActivity = Systems.CampaignActivity.Core;
        CampaignActivitySense = null;
        ShowEntrySplash("The Dungeon", "Ten floors. Five senses. No memory required.", UiTheme.Gold);
    }

    public void StartArena(string sense, Random? rng = null)
    {
        if (!CampaignProgression.PortalUnlocked(sense))
            throw new InvalidOperationException($"The {sense} arena is still sealed.");
        GamePaths.Select(sense);
        ResetAll(GamePaths.ActivateSelected(), rng);
        CampaignActivity = Systems.CampaignActivity.Arena;
        CampaignActivitySense = sense;
        GamePath path = GamePaths.PathsByKey[sense];
        ShowEntrySplash(path.Title, path.Subtitle, path.Accent);
    }

    public void StartAphantasia(Random? rng = null)
    {
        rng ??= Random.Shared;
        // The Mind's braziers and equipped loadout are live session state.
        // Capture them before ResetAll, which otherwise reloads the last
        // profile-saved equipment and can discard changes made in The Mind.
        bool noHealing = State.NoHealing;
        bool noExtract = State.NoExtract;
        Dictionary<string, ItemDrop?> equipment = State.Equipment
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        ResetAll(BossArenaFactory.Create("aphantasia", Progression.FinalBossLevel), rng);
        State.SetEquipment(equipment);
        State.SetHardMode(noHealing);
        State.SetNoExtract(noExtract);
        State.SetNewGamePlusLevel(NewGamePlus.SelectedLevel("phantasia"));
        CampaignActivity = Systems.CampaignActivity.Aphantasia;
        CampaignActivitySense = "phantasia";
        State.CurrentLevel = Progression.MaxLevel;
        State.PendingLevelUps = Progression.MaxLevel;
        State.ExpCount = 0;
        State.FillHealthForMilestone();
        RefreshLightingFixtures();

        SpawnBoss(
            (x, y, spawnRng) =>
            {
                var boss = new Aphantasia(
                    x, y, Battleground, spawnRng, noHealing, noExtract)
                {
                    ContentPath = "phantasia",
                };
                return boss;
            },
            rng,
            bossKey: "aphantasia");
        ShowEntrySplash("Aphantasia", "At the northern edge of thought, imagination remembers you.", UiTheme.Purple);
    }

    public void StartExpedition(CampaignWorld world, string? finaleSense = null,
        Random? rng = null)
    {
        rng ??= Random.Shared;
        var expedition = new ExpeditionRun(world, rng.Next(), finaleSense);
        ResetAll(expedition.Battleground, rng);
        Expedition = expedition;
        CampaignActivity = world == CampaignWorld.Body
            ? Systems.CampaignActivity.Body : Systems.CampaignActivity.Soul;
        CampaignActivitySense = finaleSense;
        State.EnemySpawningEnabled = true;
        ShowEntrySplash(world == CampaignWorld.Body ? "The Body" : "The Soul",
            world == CampaignWorld.Body
                ? "Descend through flesh; what follows will remember the wound."
                : "The journey continues. One sense waits at its end.",
            world == CampaignWorld.Body ? UiTheme.Red : UiTheme.Gold);
    }

    /// <summary>
    /// Continues a completed Body expedition directly into its hostile Soul
    /// layer. Unlike StartExpedition, this deliberately preserves the entire
    /// live build: level, upgrades, health, equipment, inventory, challenges,
    /// and elapsed run time all cross the boundary intact.
    /// </summary>
    internal void ContinueCompletedBodyIntoSoul(Random? rng = null)
    {
        if (Expedition is not { World: CampaignWorld.Body, Complete: true } body)
            throw new InvalidOperationException("The Body expedition is not complete.");

        CampaignProgression.CompleteBody();
        rng ??= Random.Shared;
        string[] lockedSenses = CampaignProgression.SenseKeys
            .Where(sense => !CampaignProgression.Data.ArenaUnlocks.Contains(sense))
            .ToArray();
        string finaleSense = lockedSenses.Contains(body.FinaleSense)
            ? body.FinaleSense
            : lockedSenses.Length > 0
                ? lockedSenses[rng.Next(lockedSenses.Length)]
                : body.FinaleSense;
        var soul = new ExpeditionRun(CampaignWorld.Soul, rng.Next(), finaleSense);

        _enemyCollisionGrid.Reset();
        _visualEffects.Clear();
        _campaignTunedProjectiles.Clear();
        _campaignProjectileSequence = 0;
        PathRun = null;
        PathFog = null;
        _pathFogActive = false;
        _roomClearedAt.Clear();
        _roomVisualEnergy.Clear();
        _pendingPathWaves.Clear();
        _pendingPathEncounterKeys.Clear();
        _preloadedPathEncounterKeys.Clear();
        LastRunRewardSummary = null;
        Expedition = soul;
        CampaignActivity = Systems.CampaignActivity.Soul;
        CampaignActivitySense = finaleSense;
        Battleground = soul.Battleground;
        Player = new Player(Battleground.SpawnPosition.X, Battleground.SpawnPosition.Y);

        State.ActiveBoss = null;
        State.EnemyHolster.Clear();
        State.EnemyProjectileHolster.Clear();
        State.BulletHolster.Clear();
        State.DamageTextList.Clear();
        State.ExperienceList.Clear();
        State.FragmentList.Clear();
        State.LootCrateList.Clear();
        State.NearbyCrate = null;
        State.CurrEnemyCount = 0;
        State.GameCompleted = false;
        State.EnemySpawningEnabled = true;
        State.CurrentStage = 1;

        Camera.SetAngle(0);
        ScreenShake = Vector2.Zero;
        LevelingHandler = new LevelingHandler(ScreenWidth, ScreenHeight, rng);
        ReforgeHandler = new ReforgeHandler(ScreenWidth, ScreenHeight);
        InformationSheet = new InformationSheet(ScreenWidth, ScreenHeight);
        Camera.Lock = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
        Camera.ConfigureViewport(ScreenWidth, ScreenHeight,
            GameProfile.Profile.CameraZoom, resetZoom: true);
        _activeBossKey = null;
        _bossTelemetry = null;
        _bossTelemetryDeathCueEmitted = false;
        _controllerPromptRemaining = 0;
        _debugVisualGallery = false;
        _dungeonBossInstance = null;
        RefreshLightingFixtures();
        ShowEntrySplash("The Soul", "The Body falls away. The same journey continues beneath it.", UiTheme.Gold);
    }

    public bool TryEnterExpeditionSecretDungeon(Random? rng = null)
    {
        if (Expedition is null)
            return false;
        float radius = Simulation.TileSize * 1.6f;
        ExpeditionSecret? secret = Expedition.Secrets
            .Where(item => item.IsAvailable(Expedition.DefeatedGuardians)
                && Vector2.DistanceSquared(item.WorldPosition, PlayerWorldCenter) <= radius * radius)
            .OrderBy(item => Vector2.DistanceSquared(item.WorldPosition, PlayerWorldCenter))
            .FirstOrDefault();
        if (secret is null)
            return false;
        if (secret.State < SecretState.DungeonOpen)
        {
            Expedition.SolveSecret(secret.SenseKey);
            return true;
        }
        if (!Expedition.EnterDungeon(secret.SenseKey, PlayerWorldCenter))
            return false;
        rng ??= Random.Shared;
        InstallPathRun(PathRun.CreateSecretDungeon(Expedition, secret, rng), rng);
        return true;
    }

    private void InstallPathRun(PathRun pathRun, Random rng)
    {
        GamePaths.SetActive(pathRun.CurrentSenseKey);
        PathRun = pathRun;
        Expedition = pathRun.Expedition;
        _roomClearedAt.Clear();
        _roomVisualEnergy.Clear();
        LastRunRewardSummary = null;
        _pendingPathWaves.Clear();
        _pendingPathEncounterKeys.Clear();
        _preloadedPathEncounterKeys.Clear();
        if (!pathRun.IsSecretDungeon)
        {
            State.Reset();
            State.SetNewGamePlusLevel(NewGamePlus.SelectedLevel(NewGamePlus.DungeonKey));
        }
        else
        {
            State.ActiveBoss = null;
            State.EnemyHolster.Clear();
            State.EnemyProjectileHolster.Clear();
            State.BulletHolster.Clear();
            State.DamageTextList.Clear();
            State.ExperienceList.Clear();
            State.FragmentList.Clear();
            State.LootCrateList.Clear();
            State.NearbyCrate = null;
            State.CurrEnemyCount = 0;
            State.EnemySpawningEnabled = true;
            State.GameCompleted = false;
        }
        Battleground = pathRun.Layout.Battleground;
        Player = new Player(Battleground.SpawnPosition.X, Battleground.SpawnPosition.Y);
        PathFog = new PathFogOfWar(Battleground);
        RefreshPathFog();
        Camera.SetAngle(0);
        ScreenShake = Vector2.Zero;
        LevelingHandler = new LevelingHandler(ScreenWidth, ScreenHeight, rng);
        ReforgeHandler = new ReforgeHandler(ScreenWidth, ScreenHeight);
        InformationSheet = new InformationSheet(ScreenWidth, ScreenHeight);
        Camera.Lock = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
        Camera.ConfigureViewport(ScreenWidth, ScreenHeight, GameProfile.Profile.CameraZoom, resetZoom: true);
        _activeBossKey = null;
        _bossTelemetry = null;
        _bossTelemetryDeathCueEmitted = false;
        _controllerPromptRemaining = 0;
        _debugVisualGallery = false;
        _dungeonBossInstance = null;
        RefreshLightingFixtures();
        State.CurrentStage = 1;
        if (!pathRun.IsSecretDungeon)
            LoadCarriedItems();
    }

    /// <summary>Restarts whichever run mode is currently represented by this session.</summary>
    public void RestartCurrentRun(Random? rng = null)
    {
        if (CampaignActivity == Systems.CampaignActivity.Aphantasia)
        {
            StartAphantasia(rng);
            return;
        }
        if (Expedition is not null)
        {
            StartExpedition(Expedition.World,
                Expedition.World == CampaignWorld.Soul ? Expedition.FinaleSense : null, rng);
            return;
        }
        if (CampaignActivity == Systems.CampaignActivity.Arena
            && CampaignActivitySense is { } sense)
        {
            StartArena(sense, rng);
            return;
        }
        if (PathRun is not null)
            StartPathRun(rng);
        else
            ResetAll(GamePaths.ActivateSelected(), rng);
    }

    /// <summary>
    /// Single persistence boundary for extraction and completion. The exact
    /// reward deltas are retained for the immutable results report.
    /// </summary>
    public RunRewardSummary FinalizeSuccessfulRun(string outcome, bool completed)
    {
        if (LastRunRewardSummary is not null)
            return LastRunRewardSummary;
        if (!completed)
            CompleteBossTelemetry(victory: false);
        State.RunOutcome = outcome;
        string path = PathRun is not null
            ? NewGamePlus.DungeonKey
            : CampaignActivitySense ?? GamePaths.Selected().Key;
        bool grantsMetaCompletion = completed && PathRun is null;
        LastRunRewardSummary = MetaProgression.RecordExtraction(
            State, path, completed, grantCompletionRewards: grantsMetaCompletion);
        MetaProgression.SyncCarriedItems(State);
        GameProfile.RecordRun(State.CurrentLevel, State.NumOfEnemiesKilled, completed);
        return LastRunRewardSummary;
    }

    private void InstallNextPathFloor()
    {
        if (PathRun is null)
            return;
        _enemyCollisionGrid.Reset();
        _playerBuildSnapshot = null;
        _pendingPathWaves.Clear();
        _pendingPathEncounterKeys.Clear();
        _preloadedPathEncounterKeys.Clear();
        GamePaths.SetActive(PathRun.CurrentSenseKey);
        _dungeonBossInstance = null;
        Battleground = PathRun.Layout.Battleground;
        RefreshLightingFixtures();
        Player = new Player(Battleground.SpawnPosition.X, Battleground.SpawnPosition.Y);
        PathFog = PathRun.TakeInstalledPreparedFog()
            ?? new PathFogOfWar(Battleground);
        RefreshPathFog();
        ScreenShake = Vector2.Zero;
        _activeBossKey = null;
        _bossTelemetry = null;
        _bossTelemetryDeathCueEmitted = false;
        State.ActiveBoss = null;
        State.EnemyHolster.Clear();
        State.EnemyProjectileHolster.Clear();
        State.BulletHolster.Clear();
        State.DamageTextList.Clear();
        State.ExperienceList.Clear();
        State.FragmentList.Clear();
        State.LootCrateList.Clear();
        State.NearbyCrate = null;
        State.CurrEnemyCount = 0;
        State.EnemySpawningEnabled = true;
        State.CurrentStage = PathRun.IsSecondAct ? 2 : 1;
        State.GracePeriod = Simulation.FrameRate * 2.0;
        State.BossAfflictions.Reset();
        State.DreamState.Reset();
        InformationSheet.CancelDrag();
    }

    public void Resize(int screenWidth, int screenHeight)
    {
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        LevelingHandler.UpdateLayout(screenWidth, screenHeight);
        ReforgeHandler.UpdateLayout(screenWidth, screenHeight);
        InformationSheet.SyncLayout(screenWidth, screenHeight);
        Camera.Lock = new Vector2(screenWidth / 2f, screenHeight / 2f);
        Camera.ConfigureViewport(screenWidth, screenHeight, GameProfile.Profile.CameraZoom);
    }

    /// <summary>
    /// Loads whatever's currently carried (GameProfile.Profile.CarriedEquipment/
    /// CarriedInventory) into this session -- called by the constructor and by
    /// ResetAll, so every run/Soul-visit start picks up your persistent loadout
    /// with no separate call needed at each call site. See
    /// MetaProgression.SyncCarriedItems/ClearCarriedItems for the write side.
    /// </summary>
    public void LoadCarriedItems()
    {
        var equipment = new Dictionary<string, ItemDrop?>();
        foreach (var (slot, stored) in GameProfile.Profile.CarriedEquipment)
            equipment[slot] = Items.Deserialize(stored);
        State.SetEquipment(equipment);
        for (int index = 0; index < State.Inventory.Count; index++)
            State.Inventory[index] = index < GameProfile.Profile.CarriedInventory.Count
                ? Items.Deserialize(GameProfile.Profile.CarriedInventory[index])
                : null;
        State.FillHealthForMilestone();
    }

    /// <summary>
    /// Ported from character.py's drawBackground(). Bakes/draws the arena's
    /// floor plane and camera-facing walls/decorations via
    /// <see cref="ArenaRenderer"/> -- see that class's doc comment for why
    /// baking still happens despite this port dropping Python's rotate/cache
    /// pipeline. Manages its own SpriteBatch.Begin/End pair (both for the
    /// lazy render-target bake and the scissor-clipped per-frame draw), so
    /// callers must invoke this *before* starting the frame's own
    /// SpriteBatch.Begin() -- MonoGame doesn't allow nested Begin calls.
    /// </summary>
    public void DrawBackground(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
    {
        _arenaRenderer.EnsureBaked(graphicsDevice, spriteBatch, Battleground);
        _arenaRenderer.Draw(spriteBatch, graphicsDevice, Camera, PlayerWorldCenter, ScreenShake,
            CombatViewport,
            drawRaisedScenery: false);
    }

    public void DrawBackgroundFull(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
    {
        Camera.Lock = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
        _arenaRenderer.EnsureBaked(graphicsDevice, spriteBatch, Battleground);
        _arenaRenderer.Draw(spriteBatch, graphicsDevice, Camera, PlayerWorldCenter, ScreenShake,
            new Rectangle(0, 0, ScreenWidth, ScreenHeight));
    }

    public void DrawRaisedScenery(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
    {
        _arenaRenderer.DrawRaisedSceneryOnly(spriteBatch, Camera, PlayerWorldCenter,
            ScreenShake, new Rectangle(0, 0, ScreenWidth, ScreenHeight),
            (float)State.RunTimeSeconds, _visualDensity.Optional, _roomVisualEnergy);
    }

    private string ActiveLightingPathKey =>
        CampaignActivity == Systems.CampaignActivity.Aphantasia
            ? "aphantasia"
            : PathRun?.CurrentSenseKey ?? CampaignActivitySense ?? GamePaths.Active().Key;

    private void RefreshLightingFixtures()
    {
        _arenaLightPosts.Clear();
        _worldLightSources.Clear();
        string pathKey = ActiveLightingPathKey;
        if (CampaignActivity == Systems.CampaignActivity.Aphantasia)
            return;
        bool standaloneOrInstancedArena =
            PathRun is null || _dungeonBossInstance is not null;
        if (standaloneOrInstancedArena)
        {
            _arenaLightPosts.AddRange(
                WorldLighting.BuildArenaLightPosts(Battleground));
            LightingTheme theme = WorldLighting.ThemeFor(pathKey);
            foreach (ArenaLightPost post in _arenaLightPosts)
            {
                _worldLightSources.Add(
                    WorldLighting.SourceFor(post, theme, pathKey));
            }
            return;
        }

        _worldLightSources.AddRange(
            WorldLighting.BuildPathLightSources(Battleground, pathKey));
    }

    /// <summary>
    /// Darkens only the combat viewport and restores path-colored local light.
    /// Called after world drawing and before fog so hidden tiles remain hidden.
    /// </summary>
    public void DrawAtmosphericLighting(
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice)
    {
        float darknessScale = State.ActiveBoss is Aphantasia aphantasia
            ? aphantasia.ArenaDarknessScale : 1f;
        float playerLightScale = State.ActiveBoss is Aphantasia lightBoss
            ? lightBoss.ArenaPlayerLightScale : 1f;
        _worldLighting.DrawAtmosphere(
            spriteBatch,
            graphicsDevice,
            CombatViewport,
            Camera,
            PlayerWorldCenter,
            ScreenShake,
            ActiveLightingPathKey,
            (float)State.RunTimeSeconds,
            _worldLightSources,
            ActiveVisibilityFog,
            _visualDensity.Optional,
            GameProfile.Profile.HighContrast,
            darknessScale,
            playerLightScale);
    }

    // ----- Player movement/combat -----

    /// <summary>Ported from character.py's movePlayer().</summary>
    /// <summary>
    /// Ported from character.py's movePlayer(), including boss obstacles,
    /// polygonal path-boss arenas, Dissonance's circular arena, and analog input.
    /// </summary>
    public void MovePlayer(bool moveLeft, bool moveRight, bool moveUp, bool moveDown, bool dashPressed, Vector2 controllerMove = default)
    {
        var before = new Vector2(Player.WorldX, Player.WorldY);
        bool wasDashing = State.Dashing;
        var obstacles = State.ActiveBoss is SinChemesthesisBoss chemicalBoss
            ? chemicalBoss.MovementObstacles()
            : null;
        Player.Move(State, Battleground, Camera, moveLeft, moveRight, moveUp, moveDown,
            dashPressed, obstacles, controllerMove,
            useArenaBoundaryConstraint: State.ActiveBoss is Aphantasia);
        if (State.ActiveBoss is IBossArenaController arenaController)
        {
            Vector2 constrained = arenaController.ConstrainPlayer(
                new Vector2(Player.WorldX, Player.WorldY),
                (float)State.PlayerSize);
            Player.SetAnimatedPosition(constrained.X, constrained.Y);
        }
        else if (State.ActiveBoss is PathChaseBoss pathBoss)
        {
            var constrained = pathBoss.ConstrainPlayerPosition(Player.WorldX, Player.WorldY, (float)State.PlayerSize);
            Player.SetAnimatedPosition(constrained.X, constrained.Y);
        }
        else if (State.ActiveBoss is Dissonance dissonance)
        {
            float playerX = Player.WorldX + (float)State.PlayerSize / 2f, playerY = Player.WorldY + (float)State.PlayerSize / 2f;
            float deltaX = playerX - dissonance.ArenaCenter.X, deltaY = playerY - dissonance.ArenaCenter.Y;
            float distance = Math.Max(1f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
            float limit = dissonance.ArenaRadius - (float)State.PlayerSize * .7f;
            if (distance > limit)
            {
                Player.SetAnimatedPosition(
                    dissonance.ArenaCenter.X + deltaX / distance * limit - (float)State.PlayerSize / 2f,
                    dissonance.ArenaCenter.Y + deltaY / distance * limit - (float)State.PlayerSize / 2f);
            }
        }
        Player.AdvanceVisuals(
            Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate));
        double traveled = Vector2.Distance(before, new Vector2(Player.WorldX, Player.WorldY));
        if (!wasDashing && State.Dashing)
        {
            _visualEffects.Emit(
                "dash",
                PlayerWorldCenter,
                State.PlayerColor,
                State.PlayerEdgeColor,
                (int)(State.RunTimeSeconds * 1000) ^ 0x51DA,
                _visualDensity.Optional,
                new Vector2((float)State.DX, (float)State.DY) * .08f);
        }
        if (traveled >= 1)
            GameProfile.IncrementQuest("distance_traveled", (long)Math.Round(traveled), State);
        RefreshPathFog();
    }

    /// <summary>
    /// Fog remains active throughout ordinary dungeon floors, including
    /// grand arenas and guardian boss rooms. Only the larger midpoint/finale
    /// encounters on floors 5 and 10 expose their complete arena. Suppression
    /// applies to every fog consumer, not only the final mask, so enemies,
    /// projectiles, bounties, and boss UI share the same visibility policy.
    /// </summary>
    private void RefreshPathFog()
    {
        if (_dungeonBossInstance is not null)
        {
            _pathFogActive = false;
            return;
        }
        if (PathFog is null || PathRun is null)
        {
            _pathFogActive = false;
            return;
        }

        PathRoom? room = PathRun.Layout.RoomAt(PlayerWorldCenter);
        bool majorBossFloor = PathRun.FloorNumber is 5 or 10;
        bool majorBossArena = majorBossFloor
            && (room?.Type == PathRoomType.Boss
                || State.ActiveBoss is not null);
        _pathFogActive = !majorBossArena;
        if (_pathFogActive)
            PathFog.Update(PlayerWorldCenter);
    }

    private PathFogOfWar? ActiveVisibilityFog =>
        IsPathFogActive ? PathFog : null;

    public void DrawPlayer(SpriteBatch spriteBatch, float sizeScale = 1f) => Player.Draw(spriteBatch, State, Camera, sizeScale);

    public void AdvancePlayerVisuals(double seconds) =>
        Player.AdvanceVisuals(seconds);

    public void UpdateVisualEffects(double seconds)
    {
        int telegraphs = 0;
        for (int index = 0; index < State.EnemyProjectileHolster.Count; index++)
        {
            EnemyProjectile projectile = State.EnemyProjectileHolster[index];
            if (projectile.Age < projectile.TelegraphDuration
                && projectile.Path is "laser" or "mine" or "bank" or "pool")
            {
                telegraphs++;
            }
        }
        float telegraphCoverage = Math.Clamp(
            telegraphs / 18f, 0f, 1f);
        _visualDensity = VisualDensityDirector.Calculate(
            GameProfile.Profile.VisualEffectsIntensity,
            State.EnemyHolster.Count,
            State.EnemyProjectileHolster.Count,
            telegraphCoverage,
            State.ActiveBoss is not null,
            authoredPeak: State.ActiveBoss is Aphantasia or Dissonance or PathChaseBoss);
        _visualEffects.Update(seconds);
    }

    public VisualRenderContext CurrentVisualContext()
    {
        string pathKey = PathRun?.CurrentSenseKey
            ?? CampaignActivitySense
            ?? GamePaths.Active().Key;
        PathRoom? room = PathRun?.Layout.RoomAt(PlayerWorldCenter);
        RoomPresentationState roomState = RoomPresentationState.Residual;
        if (room is not null)
        {
            float enteredFor = ReferenceEquals(room, PathRun?.LastEnteredRoom)
                ? (float)Math.Max(0, State.RunTimeSeconds - PathRun!.RoomEnteredAtRunSeconds)
                : float.MaxValue;
            float clearedFor = _roomClearedAt.TryGetValue(room.Id, out double clearedAt)
                ? (float)Math.Max(0, State.RunTimeSeconds - clearedAt)
                : float.MaxValue;
            roomState = SoulVisualLanguage.DeriveRoomState(
                room.IsActivated, room.IsCleared, enteredFor, clearedFor);
        }
        return new VisualRenderContext(
            (float)State.RunTimeSeconds,
            _visualDensity.UserIntensity,
            _visualDensity.EffectiveIntensity,
            Camera.AngleDegrees,
            Camera.Zoom,
            pathKey,
            PathRun?.IsSecondAct == true ? 2 : 1,
            roomState,
            State.HardMode,
            GameProfile.Profile.PathMastery.GetValueOrDefault(pathKey),
            State.NewGamePlusLevel);
    }

    public void DrawVisualEffects(SpriteBatch spriteBatch, BitVfxLayer layer) =>
        _visualEffects.Draw(
            spriteBatch,
            layer,
            Camera,
            PlayerWorldCenter,
            ScreenShake,
            CombatViewport);

    /// <summary>Ported from character.py's handlingBulletCreation() for mouse and controller aiming.</summary>
    public void HandleBulletCreation(Vector2 mouseScreenPosition, bool mouseDown, bool dragInProgress, Random? rng = null, bool controllerFiring = false)
    {
        rng ??= Random.Shared;
        if (!dragInProgress)
            Player.SetAimDirection(mouseScreenPosition - Camera.Lock);
        if (State.AttackCooldownTimer <= 0 && !dragInProgress && (State.AutoFire || mouseDown || controllerFiring))
        {
            State.AttackCooldownTimer = State.AttackCooldownStat;
            bool currCrit = false;
            int currCritChance = (int)Math.Floor(State.CritChance);
            int chance = rng.Next(1, 101);
            if (chance <= 100 * (State.CritChance - Math.Truncate(State.CritChance)))
            {
                currCrit = true;
                currCritChance = (int)Math.Floor(State.CritChance) + 1;
            }
            double currDamage = State.BulletDamage * Math.Pow(State.CritDamage, currCritChance);

            int currProjectileCount = (int)Math.Floor(State.ProjectileCount);
            chance = rng.Next(1, 101);
            if (chance <= 100 * (State.ProjectileCount - Math.Truncate(State.ProjectileCount)))
                currProjectileCount = (int)Math.Floor(State.ProjectileCount) + 1;

            int currPierce = (int)Math.Floor(State.BulletPierce);
            chance = rng.Next(1, 101);
            if (chance <= 100 * (State.BulletPierce - Math.Truncate(State.BulletPierce)))
                currPierce = (int)Math.Floor(State.BulletPierce) + 1;

            float screenOriginX = Camera.Lock.X, screenOriginY = Camera.Lock.Y;
            float originX = Player.WorldX + (float)State.PlayerSize / 2f, originY = Player.WorldY + (float)State.PlayerSize / 2f;

            for (int bNum = 0; bNum < currProjectileCount; bNum++)
            {
                var targetDelta = Camera.ScreenVectorToWorld(new Vector2(mouseScreenPosition.X - screenOriginX, mouseScreenPosition.Y - screenOriginY));
                float targetX = originX + targetDelta.X, targetY = originY + targetDelta.Y;
                float direction = MathF.Atan2(originY - targetY, targetX - originX);

                if (currProjectileCount != 1)
                {
                    float dirDelta = -((float)State.AzimuthalProjectileAngle / 2f);
                    direction += dirDelta + bNum * ((float)State.AzimuthalProjectileAngle / (currProjectileCount - 1));
                }

                State.BulletHolster.Add(new Bullet(
                    Player.WorldX + (float)State.PlayerSize / 2f - (float)State.BulletSize / 2f,
                    Player.WorldY + (float)State.PlayerSize / 2f - (float)State.BulletSize / 2f,
                    (float)State.BulletSpeed, direction, (float)State.BulletRange, (float)State.BulletSize,
                    State.BulletColor, currPierce, (float)currDamage, currCrit, State.BulletEdgeColor, State.BulletDesign));
            }
            Player.MarkFired();
            GameProfile.IncrementQuest("shots_fired", currProjectileCount, State);
        }
        else if (State.AttackCooldownTimer > 0)
        {
            State.AttackCooldownTimer = Math.Max(0, State.AttackCooldownTimer - Simulation.GetTimerStep());
        }
    }

    public void UpdateBullets()
    {
        foreach (var bullet in State.BulletHolster)
            bullet.Update(Battleground);
        State.BulletHolster.RemoveAll(b => b.RemFlag);
    }

    public void DrawBullets(SpriteBatch spriteBatch)
    {
        foreach (var bullet in State.BulletHolster)
            bullet.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
    }

    // ----- Enemies -----

    /// <summary>Level threshold reached, boss not yet fought or fighting -- see <see cref="BossPortalOpen"/>'s doc comment.</summary>
    private bool NaturalMidBossRequested => PathRun is null
        && State.CurrentLevel >= Progression.MidBossLevel && !State.BeaudisEncounterStarted && State.ActiveBoss is null;

    /// <summary>Level threshold reached, Beaudis already down, Dissonance not yet fought or fighting -- see <see cref="BossPortalOpen"/>'s doc comment.</summary>
    private bool NaturalFinalBossRequested => PathRun is null
        && State.CurrentLevel >= Progression.FinalBossLevel && State.BeaudisDefeated && !State.DissonanceEncounterStarted && State.ActiveBoss is null;

    /// <summary>
    /// True whenever a boss fight is "available" but not yet entered -- the
    /// swirling portal (<see cref="DrawBossPortal"/>) is visible at
    /// <see cref="ArenaCenterWorld"/> under exactly this condition, and
    /// <see cref="HandleEnemyCreation"/> only actually starts the fight once
    /// the player has walked into it (<see cref="PlayerAtBossPortal"/>).
    /// Purely derived from existing RunState -- no separate "portal open"
    /// flag to keep in sync.
    /// </summary>
    private bool BossPortalOpen => NaturalMidBossRequested || NaturalFinalBossRequested;

    private bool PathMajorBossGatewayOpen => PathRun is { FloorNumber: 5 or 10 } run
        && run.Layout.BossRoom.IsActivated
        && !run.Layout.BossRoom.IsCleared
        && State.ActiveBoss is null
        && _dungeonBossInstance is null;

    private Vector2 CurrentPathPortalWorld =>
        _dungeonBossInstance?.ArenaCenter
        ?? PathRun?.ExitPortalWorld
        ?? ArenaCenterWorld;

    /// <summary>
    /// Where a natural boss fight always happens -- Dissonance's forced
    /// spawn rect and SpawnBoss's own default (non-forced) search both
    /// already centered on this same point, so the portal, the touch
    /// check, and the eventual spawn position all share one formula.
    /// </summary>
    private Vector2 ArenaCenterWorld => new(Battleground.Width * Simulation.TileSize / 2f, Battleground.Height * Simulation.TileSize / 2f);

    private bool PlayerAtBossPortal()
    {
        int radius = BossPortalInteractRadius;
        var portalRect = new Rectangle((int)(ArenaCenterWorld.X - radius), (int)(ArenaCenterWorld.Y - radius), radius * 2, radius * 2);
        return Player.WorldRect(State).Intersects(portalRect);
    }

    private bool PlayerAtPathPortal(Vector2 world)
    {
        int radius = BossPortalInteractRadius;
        var portalRect = new Rectangle(
            (int)world.X - radius,
            (int)world.Y - radius,
            radius * 2,
            radius * 2);
        return Player.WorldRect(State).Intersects(portalRect);
    }

    /// <summary>
    /// Moves the player straight down from a boss arena center by `distance`,
    /// snapped to the nearest open tile. Standalone level-10/20 encounters
    /// and Path floor-5/10 milestones use this arena staging; ordinary Path
    /// guardians leave the player exactly where they entered the room.
    /// </summary>
    private void StepPlayerBackFrom(Vector2 center, float distance)
    {
        var playerSpawn = Battleground.FindNearestOpenRect(new Rectangle(
            (int)(center.X - State.PlayerSize / 2f), (int)(center.Y + distance - State.PlayerSize / 2f),
            (int)State.PlayerSize, (int)State.PlayerSize));
        Player.SetPosition(playerSpawn.X, playerSpawn.Y);
    }

    /// <summary>
    /// Ported from character.py's handlingEnemyCreation(). The natural
    /// Beaudis/Dissonance triggers now really spawn -- including the hidden
    /// debug-summon hotkey, which resolves through GamePaths.BossKey the
    /// same as a natural trigger would, so it summons whichever path is
    /// currently active/selected, not always Dissonance.
    ///
    /// Unlike the original automatic trigger, a natural encounter no longer
    /// starts the instant the level threshold is reached -- the portal that
    /// opens at <see cref="ArenaCenterWorld"/> (see
    /// <see cref="BossPortalOpen"/>/<see cref="DrawBossPortal"/>) only
    /// actually starts the fight once the player is standing on it
    /// (<see cref="PlayerAtBossPortal"/>) *and* presses the "interact"
    /// keybind -- walking up to it alone no longer commits you. Ordinary
    /// enemy spawning below pauses for as long as the portal is up (entered
    /// or not) -- existing enemies aren't cleared, just no new ones join
    /// while a boss fight is pending. The debug hotkey still bypasses the
    /// portal (both the position and the button-press requirement) entirely.
    ///
    /// Every boss except Dissonance (GamePaths' "sound" path) spawns via the
    /// generic non-forced search in SpawnBoss and gets a small, generic
    /// step-back afterward so the player doesn't land on top of it -- this
    /// applies uniformly across every path's mid/final boss, not just one.
    /// Dissonance keeps its own bespoke forced-arena-center spawn and
    /// larger repositioning (mirroring its much bigger ArenaRadius), same
    /// as before this portal existed.
    /// </summary>
    public void HandleEnemyCreation(Random? rng = null, bool interactPressed = false)
    {
        rng ??= Random.Shared;
        if (CampaignActivity == Systems.CampaignActivity.Aphantasia)
            return;
        if (Expedition is not null && PathRun is null && interactPressed)
            TryEnterExpeditionSecretDungeon(rng);
        if (PathRun is not null)
        {
            HandlePathEnemyCreation(rng, interactPressed);
            return;
        }
        bool naturalMidBossRequested = NaturalMidBossRequested;
        bool naturalFinalBossRequested = NaturalFinalBossRequested;
        if (State.BossDebugRequested || ((naturalMidBossRequested || naturalFinalBossRequested) && PlayerAtBossPortal() && interactPressed))
        {
            bool naturalEncounter = !State.BossDebugRequested;
            bool midpoint = naturalMidBossRequested && naturalEncounter;
            string bossKey = midpoint ? GamePaths.BossKey(midpoint: true) : GamePaths.BossKey(midpoint: false);
            if (midpoint)
                State.BeaudisEncounterStarted = true;
            else
            {
                if (naturalFinalBossRequested && naturalEncounter)
                    State.DissonanceEncounterStarted = true;
            }

            if (!BossCatalog.Shared.TryGet(bossKey, out var definition) || definition is null)
                throw new InvalidOperationException($"Boss '{bossKey}' is not registered.");

            if (midpoint)
            {
                SpawnBoss((x, y, r) => definition.Factory(x, y, Battleground, AwarenessRange, r), rng, bossKey: bossKey);
                StepPlayerBackFrom(ArenaCenterWorld, Simulation.TileSize * 2.5f);
            }
            else
            {
                // Final boss: teleport to a dedicated, obstacle-free arena
                // instead of fighting inside the shared room's fixed
                // building layout -- the same BossArenaFactory swap Path
                // mode's floor-10 finale already uses
                // (EnterPathMajorBossInstance), just one-way since winning a
                // Classic-mode finale ends the run immediately, so there's
                // nothing to restore.
                Battleground arena = BossArenaFactory.Create(bossKey, scale: 1.5f);
                State.EnemyHolster.Clear();
                State.EnemyProjectileHolster.Clear();
                State.BulletHolster.Clear();
                State.DamageTextList.Clear();
                State.ExperienceList.Clear();
                State.FragmentList.Clear();
                State.LootCrateList.Clear();
                State.NearbyCrate = null;
                State.CurrEnemyCount = 0;
                Battleground = arena;
                RefreshLightingFixtures();
                _enemyCollisionGrid.Reset();
                Player.SetPosition(arena.SpawnPosition.X, arena.SpawnPosition.Y);

                if (bossKey == "dissonance")
                {
                    float arenaX = ArenaCenterWorld.X, arenaY = ArenaCenterWorld.Y;
                    float size = Simulation.TileSize * 1.9f;
                    var forcedRect = new Rectangle((int)(arenaX - size / 2f), (int)(arenaY - size / 2f), (int)size, (int)size);
                    SpawnBoss((x, y, r) => definition.Factory(x, y, Battleground, AwarenessRange, r), rng, forcedRect, bossKey,
                        clearFloorLoot: false, clearCombatants: false);
                }
                else
                {
                    SpawnBoss((x, y, r) => definition.Factory(x, y, Battleground, AwarenessRange, r), rng, bossKey: bossKey,
                        clearFloorLoot: false, clearCombatants: false);
                }
            }
            State.BossDebugRequested = false;
            return;
        }

        if (naturalMidBossRequested || naturalFinalBossRequested)
            return; // portal is open but not yet entered -- pause ordinary spawning while it's up.

        if (!State.EnemySpawningEnabled)
            return;

        var caps = Progression.EncounterCaps(State.CurrentLevel);
        State.EnemyCap = caps.EnemyCap;
        State.EnemyThreatCap = caps.ThreatCap;
        State.EnemyPopulationThreatCap = caps.PopulationThreatCap;

        // Mini-bosses enter the ordinary world once per run.
        int outsideAwarenessTiles = (int)Math.Ceiling(ScreenHeight * .625 / Simulation.TileSize) + 2;
        for (int gateIndex = 0;
             gateIndex < Progression.MinibossGates.Count;
             gateIndex++)
        {
            var (unlockLevel, key) = Progression.MinibossGates[gateIndex];
            if (State.CurrentLevel >= unlockLevel && !State.GuaranteedMiniBossesSpawned.Contains(key) && State.EnemyHolster.Count < State.EnemyCap)
            {
                var miniboss = EnemyCatalog.Shared.Spawn(State.CurrentLevel, Battleground, PlayerWorldCenter, AwarenessRange,
                    rng, key: key, minDistanceTiles: outsideAwarenessTiles);
                if (miniboss is not null)
                {
                    ApplyRunDifficulty(miniboss);
                    State.EnemyHolster.Add(miniboss);
                }
                State.GuaranteedMiniBossesSpawned.Add(key);
            }
        }
        State.CurrEnemyCount = State.EnemyHolster.Count;

        State.EnemySpawnTimer -= Simulation.GetTimerStep();
        State.EncounterSpawnCooldown = Math.Max(0, State.EncounterSpawnCooldown - Simulation.GetTimerStep());
        if (State.EnemySpawnTimer > 0)
            return;

        double currentThreat = 0;
        _worldEncounterIdScratch.Clear();
        foreach (Enemy enemy in State.EnemyHolster)
        {
            currentThreat += enemy.ThreatCost;
            if (enemy.Encounter is not null)
                _worldEncounterIdScratch.Add(enemy.Encounter.Id);
        }
        var pacing = Progression.EncounterPacing(State.CurrentLevel);
        if (_worldEncounterIdScratch.Count < pacing.MaxWorldEncounters && State.CurrEnemyCount < State.EnemyCap
            && currentThreat < State.EnemyPopulationThreatCap && State.EnemySpawnTimer <= 0)
        {
            State.EnemySpawnTimer = Simulation.FrameRate * pacing.SpawnIntervalSeconds * rng.Next(85, 116) / 100.0;
            double remainingThreat = State.EnemyPopulationThreatCap - currentThreat;

            (string Key, List<Enemy> Group)? encounterResult = null;
            if (State.CurrentLevel >= 5 && State.EncounterSpawnCooldown <= 0 && rng.Next(1, 101) <= pacing.CuratedChance * 100)
            {
                var curated = EnemyCatalog.Shared.SpawnEncounter(State.CurrentLevel, remainingThreat, Battleground,
                    PlayerWorldCenter, AwarenessRange, ScreenHeight, State.EnemyHolster, rng);
                if (curated.HasValue)
                    encounterResult = (curated.Value.Package.Key, curated.Value.Group);
            }
            if (encounterResult is null)
            {
                var patrol = EnemyCatalog.Shared.SpawnPatrol(State.CurrentLevel, remainingThreat, Battleground,
                    PlayerWorldCenter, AwarenessRange, ScreenHeight, State.EnemyHolster, rng,
                    contentPath: GamePaths.Active().Key);
                if (patrol.HasValue)
                    encounterResult = (patrol.Value.Encounter.Key, patrol.Value.Group);
            }
            if (encounterResult.HasValue)
            {
                var (key, group) = encounterResult.Value;
                foreach (var enemy in group)
                    ApplyRunDifficulty(enemy);
                double groupThreat = 0;
                for (int index = 0; index < group.Count; index++)
                    groupThreat += group[index].ThreatCost;
                if (State.EnemyHolster.Count + group.Count <= State.EnemyCap && currentThreat + groupThreat <= State.EnemyPopulationThreatCap)
                {
                    State.EnemyHolster.AddRange(group);
                    if (!key.StartsWith("patrol_"))
                        State.EncounterSpawnCooldown = Simulation.FrameRate * 18;
                    State.CurrEnemyCount = State.EnemyHolster.Count;
                    return;
                }
            }
            State.CurrEnemyCount = State.EnemyHolster.Count;
        }
    }

    /// <summary>
    /// Ported from character.py's shared boss-spawn prep (the arena-clearing
    /// block in handlingEnemyCreation) + BossCatalog.spawn's arena-center
    /// placement search. `factory` receives the found spawn position instead
    /// of constructing at a placeholder position and being repositioned
    /// after -- Enemy.WorldX/Y only have a protected setter, and every boss
    /// constructor already accepts worldX/Y directly, so there's no need for
    /// a reposition hook.
    /// </summary>
    /// <summary>
    /// `forcedSpawnRect` bypasses the open-space search for bosses that own
    /// their entire arena and must land exactly at its center regardless of
    /// nearby obstacles -- Dissonance's constructor call passes one (its
    /// spawn position mirrors character.py's `if boss_key == "dissonance":`
    /// special-case, which repositions the boss to the exact arena center
    /// instead of BossCatalog.spawn's generic nearest-open-rect search).
    /// </summary>
    private void SpawnBoss(Func<float, float, Random, Enemy> factory, Random rng, Rectangle? forcedSpawnRect = null,
        string? bossKey = null, bool clearFloorLoot = true, bool clearCombatants = true)
    {
        var skippedBranchRooms = PathRun?.Layout.Rooms
            .Where(room => !room.IsMainPath
                && room.IsCombatRoom
                && room.IsActivated
                && !room.IsCleared)
            .ToList() ?? new List<PathRoom>();
        var skippedKeys = skippedBranchRooms
            .Select(room => room.EncounterKey)
            .ToHashSet(StringComparer.Ordinal);
        double skippedBranchThreat = State.EnemyHolster
            .Where(enemy => enemy.EncounterKey is not null
                && skippedKeys.Contains(enemy.EncounterKey))
            .Sum(enemy => enemy.ThreatCost);
        double carriedEnemyThreat = clearCombatants
            ? 0
            : State.EnemyHolster.Sum(enemy => enemy.ThreatCost);

        if (clearCombatants)
        {
            State.EnemyHolster.Clear();
            State.EnemyProjectileHolster.Clear();
            State.DamageTextList.Clear();
            State.ExperienceList.Clear();
            State.FragmentList.Clear();
        }
        if (clearFloorLoot)
        {
            State.LootCrateList.Clear();
            State.NearbyCrate = null;
        }

        Rectangle spawnRect;
        if (forcedSpawnRect.HasValue)
        {
            spawnRect = forcedSpawnRect.Value;
        }
        else
        {
            float footprint = Simulation.TileSize * 1.9f;
            var center = ArenaCenterWorld;
            var requested = new Rectangle((int)(center.X - footprint / 2f), (int)(center.Y - footprint / 2f), (int)footprint, (int)footprint);
            spawnRect = Battleground.FindNearestOpenRect(requested);
        }

        var boss = factory(spawnRect.X, spawnRect.Y, rng);
        ApplyRunDifficulty(boss);
        State.EnemyHolster.Add(boss);
        State.ActiveBoss = boss;
        _activeBossKey = bossKey;
        State.BossDebugInvincible = false;
        State.CurrEnemyCount = State.EnemyHolster.Count;
        State.EnemySpawningEnabled = false;
        State.GracePeriod = Simulation.FrameRate * 2;
        _bossTelemetry = new BossEncounterTelemetryTracker(
            bossKey ?? BossKeyFor(boss) ?? boss.Family,
            PathRun?.CurrentSenseKey ?? boss.ContentPath ?? GamePaths.Active().Key,
            PathRun?.FloorNumber ?? State.CurrentLevel,
            State.RunTimeSeconds,
            skippedBranchRooms.Count,
            skippedBranchThreat,
            carriedEnemyThreat);
        _bossTelemetryDeathCueEmitted = false;
    }

    public void RecordControllerActivity(bool active)
    {
        double seconds = Simulation.GetTimerStep()
            / Math.Max(1, Simulation.FrameRate);
        _controllerPromptRemaining = active
            ? 4.0
            : Math.Max(0, _controllerPromptRemaining - seconds);
        _bossTelemetry?.RecordControllerUse(active);
    }

    /// <summary>
    /// True for bosses whose constructor already owns the encounter's health,
    /// damage, movement, and projectile language. Applying the ordinary-enemy
    /// sense profile to these a second time made nominally equivalent bosses
    /// vary from Sight's .56 health multiplier to Chemesthesis's 2.15 before
    /// floor difficulty was even considered.
    /// </summary>
    public static bool UsesAuthoredBossBalance(Enemy enemy) =>
        enemy is Aphantasia or PathGuardianBoss or PathChaseBoss or Beaudis or Dissonance;

    /// <summary>
    /// Applies ordinary sense identity only to catalog enemies and adds. Boss
    /// classes keep their authored baseline, then receive the shared NG+/Path
    /// run curve exactly once.
    /// </summary>
    internal void ApplyRunDifficulty(Enemy enemy)
    {
        if (UsesAuthoredBossBalance(enemy))
            enemy.ContentPath ??= PathRun?.CurrentSenseKey ?? GamePaths.Active().Key;
        else
            GamePaths.ApplyEnemyIdentity(enemy);
        NewGamePlus.ApplyEnemyHealth(enemy, State.NewGamePlusLevel);
        if (PathRun is not null)
        {
            enemy.MaxHp = Math.Max(1, (int)Math.Round(enemy.MaxHp * PathRun.HealthMultiplier));
            enemy.Hp = enemy.MaxHp;
            enemy.Damage = Math.Max(1, (int)Math.Round(enemy.Damage * PathRun.DamageMultiplier));
            enemy.Speed *= (float)PathRun.SpeedMultiplier;
            enemy.ExpValue *= PathRun.IsSecondAct ? 1.45 : 1.0;
            if (UsesAuthoredBossBalance(enemy))
            {
                if (enemy.AttackCooldown.HasValue)
                    enemy.AttackCooldown *= (float)PathRun.TimingMultiplier;
                if (enemy.AttackCooldownMax.HasValue)
                    enemy.AttackCooldownMax *= (float)PathRun.TimingMultiplier;
            }
        }
        if (UsesAuthoredBossBalance(enemy))
        {
            enemy.MaxHp = Math.Max(1,
                (int)Math.Round(enemy.MaxHp * BossHealthMultiplier));
            enemy.Hp = enemy.MaxHp;
        }
        if (SoulCampaignFinaleActive && UsesAuthoredBossBalance(enemy))
        {
            enemy.Damage = Math.Max(1, (int)Math.Round(enemy.Damage * .75));
            if (enemy.AttackCooldown.HasValue)
                enemy.AttackCooldown *= 1.25f;
            if (enemy.AttackCooldownMax.HasValue)
                enemy.AttackCooldownMax *= 1.25f;
        }
        if (UsesAuthoredBossBalance(enemy) && State.HardMode)
        {
            enemy.MaxHp = Math.Max(1, (int)Math.Round(enemy.MaxHp * 1.12));
            enemy.Hp = enemy.MaxHp;
            enemy.Damage = Math.Max(1, (int)Math.Round(enemy.Damage * 1.15));
            if (enemy.AttackCooldown.HasValue)
                enemy.AttackCooldown *= .90f;
            if (enemy.AttackCooldownMax.HasValue)
                enemy.AttackCooldownMax *= .90f;
        }
        else if (UsesAuthoredBossBalance(enemy) && GameProfile.Profile.CasualMode)
        {
            enemy.MaxHp = Math.Max(1, (int)Math.Round(enemy.MaxHp * .82));
            enemy.Hp = enemy.MaxHp;
            enemy.Damage = Math.Max(1, (int)Math.Round(enemy.Damage * .80));
            if (enemy.AttackCooldown.HasValue)
                enemy.AttackCooldown *= 1.15f;
            if (enemy.AttackCooldownMax.HasValue)
                enemy.AttackCooldownMax *= 1.15f;
        }
    }

    /// <summary>
    /// Ported from character.py's handlingEnemyUpdatesAndDrawing(). Split
    /// into Update/Draw (Python interleaved per-enemy update-then-draw
    /// purely to share one loop; drawing order among enemies was never
    /// semantically significant), so the pressure-budget/spawn-absorption
    /// logic is unit testable without a GraphicsDevice.
    /// </summary>
    public void UpdateEnemies()
    {
        if (AphantasiaPrecombatDraftsPending)
            return;
        var playerCenter = new Vector2(Player.WorldX + (float)State.PlayerSize / 2f, Player.WorldY + (float)State.PlayerSize / 2f);
        double pressureUsed = 0.0;
        _encounterScratch.Clear();
        _encounterIdScratch.Clear();
        _ungroupedEnemyScratch.Clear();
        foreach (var enemy in State.EnemyHolster)
        {
            var encounter = enemy.Encounter;
            if (encounter is null)
                _ungroupedEnemyScratch.Add(enemy);
            else if (_encounterIdScratch.Add(encounter.Id))
                _encounterScratch.Add(encounter);
        }

        _enemySortCenter = playerCenter;
        _encounterScratch.Sort(_encounterDistanceComparison);
        foreach (var encounter in _encounterScratch)
        {
            bool wantsPressure = encounter.State == "engaged" || encounter.DistanceTo(playerCenter.X, playerCenter.Y) <= encounter.ActivationRange;
            bool allowed = !wantsPressure || pressureUsed + encounter.ThreatCost <= State.EnemyThreatCap;
            encounter.Update(playerCenter.X, playerCenter.Y, Battleground, allowed);
            if (encounter.EngagementAllowed)
                pressureUsed += encounter.ThreatCost;
        }

        _ungroupedEnemyScratch.Sort(_enemyDistanceComparison);
        foreach (var enemy in _ungroupedEnemyScratch)
        {
            if (IsDormantPathEnemy(enemy))
            {
                enemy.EngagementAllowed = false;
                continue;
            }
            double cost = enemy.ThreatCost;
            bool isBoss = ReferenceEquals(enemy, State.ActiveBoss);
            enemy.EngagementAllowed = isBoss || pressureUsed + cost <= State.EnemyThreatCap;
            if (enemy.EngagementAllowed)
                pressureUsed += cost;
        }

        _spawnedGroupScratch.Clear();
        _spawnedEnemyScratch.Clear();
        _enemyUpdateContext.PlayerWorldX = playerCenter.X;
        _enemyUpdateContext.PlayerWorldY = playerCenter.Y;
        _enemyUpdateContext.Battleground = Battleground;
        _enemyUpdateContext.ProjectileSink = State.EnemyProjectileHolster;
        _enemyUpdateContext.AllEnemies = State.EnemyHolster;
        _enemyUpdateContext.ExperienceBubbles = State.ExperienceList;
        _enemyUpdateContext.Camera = Camera;
        _enemyUpdateContext.BossAfflictions = State.BossAfflictions;
        _enemyUpdateContext.PlayerBuildSnapshot = CurrentPlayerBuildSnapshot();
        _enemyUpdateContext.PlayerBullets = State.BulletHolster;
        _enemyUpdateContext.DreamState = State.DreamState;
        _enemyUpdateContext.PlayerMovementSpeed = (float)(State.PlayerSpeed
            * State.BossAfflictions.MovementMultiplier());
        var context = _enemyUpdateContext;
        foreach (var enemy in State.EnemyHolster)
        {
            if (IsDormantPathEnemy(enemy))
                continue;
            enemy.SetCollisionCamera(Camera);
            enemy.EnsureCollisionSafePosition(Battleground);
            int projectileStart = State.EnemyProjectileHolster.Count;
            double seconds = Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
            var control = StatusEffects.Update(enemy, seconds);
            float originalSpeed = enemy.Speed;
            enemy.Speed *= (float)control.MovementMultiplier;
            if (enemy.AttackCooldown is not null)
                enemy.AttackCooldown += (float)(control.AttackDelay * seconds * Simulation.FrameRate);
            if (!control.Stunned && enemy.Hp > 0)
                enemy.Update(context);
            enemy.Speed = originalSpeed;
            // A few authored attacks teleport directly rather than using
            // TryAxisMove; validate those destinations as well.
            enemy.EnsureCollisionSafePosition(Battleground);
            if (UsesAuthoredBossBalance(enemy))
            {
                string pathKey = enemy.ContentPath ?? GamePaths.Active().Key;
                for (int index = projectileStart; index < State.EnemyProjectileHolster.Count; index++)
                {
                    EnemyProjectile projectile = State.EnemyProjectileHolster[index];
                    projectile.ContentPath ??= pathKey;
                    if (!projectile.OriginWasPretelegraphed
                        && projectile.Path != "origin_warning"
                        && projectile.OriginTelegraphDuration <= 0f)
                    {
                        projectile.RequireOriginTelegraphIfRemote(
                            new Vector2(
                                enemy.WorldX + enemy.Size / 2f,
                                enemy.WorldY + enemy.Size / 2f),
                            enemy.Size * .7f,
                            Math.Clamp(
                                projectile.TelegraphDuration,
                                .45f,
                                1.25f));
                    }
                    if (PathRun is not null)
                        projectile.Damage = MathF.Round(
                            projectile.Damage * (float)PathRun.DamageMultiplier);
                    if (State.HardMode)
                        projectile.Damage = MathF.Round(projectile.Damage * 1.15f);
                }
            }
            else
            {
                GamePaths.TuneNewProjectiles(State.EnemyProjectileHolster, projectileStart);
            }
            HandleEnemyTransitionRequests(enemy);
            if (enemy.SpawnedEnemies.Count > 0)
            {
                int start = _spawnedEnemyScratch.Count;
                _spawnedEnemyScratch.AddRange(enemy.SpawnedEnemies);
                _spawnedGroupScratch.Add(new SpawnedEnemyGroup(
                    enemy, start, enemy.SpawnedEnemies.Count, enemy.AtomicSpawnGroup));
            }
            enemy.SpawnedEnemies.Clear();
        }

        _rejectedOwnerScratch.Clear();
        double currentPopulationThreat = 0;
        for (int index = 0; index < State.EnemyHolster.Count; index++)
            currentPopulationThreat += State.EnemyHolster[index].ThreatCost;
        foreach (SpawnedEnemyGroup group in _spawnedGroupScratch)
        {
            double groupThreat = 0;
            int end = group.Start + group.Count;
            for (int index = group.Start; index < end; index++)
                groupThreat += _spawnedEnemyScratch[index].ThreatCost;
            if (group.Atomic && (State.EnemyHolster.Count + group.Count > State.EnemyCap
                || currentPopulationThreat + groupThreat > State.EnemyPopulationThreatCap))
            {
                _rejectedOwnerScratch.Add(group.Owner);
                continue;
            }
            for (int index = group.Start; index < end; index++)
            {
                Enemy enemy = _spawnedEnemyScratch[index];
                ApplyRunDifficulty(enemy);
                if (State.EnemyHolster.Count >= State.EnemyCap)
                    break;
                if (currentPopulationThreat + enemy.ThreatCost > State.EnemyPopulationThreatCap)
                    break;
                if (group.Owner.Encounter is not null && enemy.Encounter is null)
                {
                    enemy.Encounter = group.Owner.Encounter;
                    enemy.EncounterSlot = group.Owner.Encounter.Members.Count;
                    enemy.CombatSide = enemy.EncounterSlot % 2 != 0 ? -1 : 1;
                    group.Owner.Encounter.Members.Add(enemy);
                }
                State.EnemyHolster.Add(enemy);
                currentPopulationThreat += enemy.ThreatCost;
            }
        }
        if (_rejectedOwnerScratch.Count > 0)
            State.EnemyHolster.RemoveAll(e => _rejectedOwnerScratch.Contains(e));
        State.CurrEnemyCount = State.EnemyHolster.Count;
        UpdateBossTelemetry();

        // Ported from Dissonance._update_visuals's `vH.screenShakeX/Y` global write --
        // computed here instead and assigned to this session's own ScreenShake, matching
        // this port's "explicit parameter over hidden global" convention (see Dissonance.cs).
        if (State.ActiveBoss is Dissonance dissonance)
            ScreenShake = dissonance.ComputeScreenShake(GameProfile.Profile.ScreenShake);
    }

    private PlayerBuildSnapshot CurrentPlayerBuildSnapshot()
    {
        if (_playerBuildSnapshot is { } snapshot
            && snapshot.Types.Count == State.UpgradeTypeCounts.Count
            && SnapshotTypesMatch(snapshot.Types)
            && snapshot.Stats["projectile_count"] == State.ProjectileCount
            && snapshot.Stats["pierce"] == State.BulletPierce
            && snapshot.Stats["crit_chance"] == State.CritChance
            && snapshot.Stats["crit_damage"] == State.CritDamage
            && snapshot.Stats["bullet_speed"] == State.BulletSpeed
            && snapshot.Stats["bullet_size"] == State.BulletSize)
        {
            return snapshot;
        }

        return _playerBuildSnapshot = State.BuildSnapshot();
    }

    private bool SnapshotTypesMatch(IReadOnlyDictionary<string, int> snapshotTypes)
    {
        foreach (var (name, count) in State.UpgradeTypeCounts)
        {
            if (!snapshotTypes.TryGetValue(name, out int snapshotCount) || snapshotCount != count)
                return false;
        }
        return true;
    }

    private int CompareEncountersForPressure(
        RuntimeEncounter left, RuntimeEncounter right)
    {
        int engagedCompare =
            (left.State != "engaged").CompareTo(right.State != "engaged");
        return engagedCompare != 0
            ? engagedCompare
            : left.DistanceTo(_enemySortCenter.X, _enemySortCenter.Y)
                .CompareTo(right.DistanceTo(_enemySortCenter.X, _enemySortCenter.Y));
    }

    private int CompareEnemiesForPressure(Enemy left, Enemy right)
    {
        float leftX = left.WorldX + left.Size / 2f - _enemySortCenter.X;
        float leftY = left.WorldY + left.Size / 2f - _enemySortCenter.Y;
        float rightX = right.WorldX + right.Size / 2f - _enemySortCenter.X;
        float rightY = right.WorldY + right.Size / 2f - _enemySortCenter.Y;
        return (leftX * leftX + leftY * leftY)
            .CompareTo(rightX * rightX + rightY * rightY);
    }

    private void HandleEnemyTransitionRequests(Enemy enemy)
    {
        bool phaseCheckpoint = enemy.MilestoneHealRequested
            || enemy.TransitionCleanupRequested;
        if (phaseCheckpoint
            && (State.HardMode || State.GoldenFlameMode || State.VoidMode)
            && ReferenceEquals(enemy, State.ActiveBoss))
        {
            State.FillHealthForMilestone();
        }
        enemy.MilestoneHealRequested = false;

        if (enemy.TransitionCleanupRequested)
        {
            if (enemy.TransitionCleanupOwner is not null)
                State.EnemyProjectileHolster.RemoveAll(projectile =>
                    projectile.Owner == enemy.TransitionCleanupOwner);
            else
                State.EnemyProjectileHolster.Clear();
            enemy.TransitionCleanupRequested = false;
        }

        if (enemy.TransitionSweepRequested)
        {
            IEnumerable<EnemyProjectile> swept = enemy.TransitionSweepOwner is not null
                ? State.EnemyProjectileHolster.Where(
                    projectile => projectile.Owner == enemy.TransitionSweepOwner)
                : State.EnemyProjectileHolster;
            foreach (EnemyProjectile projectile in swept)
            {
                // Persistent hazards are terrain, not a volley: Rot's sludge
                // and Ache's crystal fields are meant to accumulate across a
                // fight, so sweeping them would erase the identity of both
                // encounters every time a phase turned over.
                if (projectile.PersistentHazard)
                    continue;
                projectile.Acceleration = Math.Max(
                    projectile.Acceleration, TransitionSweepAcceleration);
            }
            enemy.TransitionSweepRequested = false;
        }

        if (enemy.PhaseInterludeInvulnerabilitySeconds > 0)
        {
            if (ReferenceEquals(enemy, State.ActiveBoss))
            {
                State.GracePeriod = Math.Max(
                    State.GracePeriod,
                    Simulation.FrameRate * enemy.PhaseInterludeInvulnerabilitySeconds);
            }
            enemy.PhaseInterludeInvulnerabilitySeconds = 0;
        }
    }

    public void DrawEnemies(SpriteBatch spriteBatch)
    {
        PathFogOfWar? fog = ActiveVisibilityFog;
        _drawnEncounterIdScratch.Clear();
        foreach (var enemy in State.EnemyHolster)
        {
            if (fog is not null
                && !fog.IsWorldAreaVisible(enemy.WorldRect()))
            {
                continue;
            }
            var encounter = enemy.Encounter;
            if (encounter is not null && _drawnEncounterIdScratch.Add(encounter.Id))
                encounter.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
            enemy.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
        }
    }

    /// <summary>Ported from character.py's handlingEnemyProjectileUpdating(), including boss-arena containment and overflow trimming.</summary>
    public void UpdateEnemyProjectiles()
    {
        if (AphantasiaPrecombatDraftsPending)
            return;
        if (SoulCampaignFinaleActive)
        {
            foreach (EnemyProjectile projectile in State.EnemyProjectileHolster.ToArray())
            {
                if (!_campaignTunedProjectiles.Add(projectile))
                    continue;
                _campaignProjectileSequence++;
                if (_campaignProjectileSequence % 4 == 0)
                    projectile.RemFlag = true;
                else
                {
                    projectile.Speed *= .8f;
                    projectile.Damage *= .75f;
                }
            }
        }
        _spawnedProjectileScratch.Clear();
        bool casualMode = GameProfile.Profile.CasualMode;
        (Vector2 Center, float Radius)? radialArena = State.ActiveBoss switch
        {
            Aphantasia aphantasia => (aphantasia.ArenaCenter, aphantasia.ArenaRadius),
            Dissonance dissonance => (dissonance.ArenaCenter, dissonance.ArenaRadius),
            PathGuardianBoss guardian => (guardian.ArenaCenter, guardian.ArenaRadius),
            _ => null,
        };
        var pathArena = State.ActiveBoss as PathChaseBoss;
        bool bossDying = DeathSpectacleActive(State.ActiveBoss)
            && State.ActiveBoss is not Aphantasia;
        foreach (var projectile in State.EnemyProjectileHolster)
        {
            var center = new Vector2(projectile.WorldX + projectile.Size / 2f, projectile.WorldY + projectile.Size / 2f);
            if (pathArena is not null)
            {
                if (!pathArena.ProjectileWithinArenaBounds(center))
                    projectile.RemFlag = true;
            }
            else if (radialArena.HasValue)
            {
                if (Vector2.Distance(center, radialArena.Value.Center) > radialArena.Value.Radius * 1.04f)
                    projectile.RemFlag = true;
            }
            if (bossDying)
                projectile.RemFlag = true;
            projectile.Update(Battleground, casualMode, State.HardMode, PlayerWorldCenter);
            _spawnedProjectileScratch.AddRange(projectile.SpawnedProjectiles);
            projectile.SpawnedProjectiles.Clear();
        }
        State.EnemyProjectileHolster.RemoveAll(p => p.RemFlag);
        GamePaths.TuneNewProjectiles(_spawnedProjectileScratch);
        State.EnemyProjectileHolster.AddRange(_spawnedProjectileScratch);
        int projectileLimit = State.ActiveBoss is Aphantasia
            ? MaxBossProjectiles * Aphantasia.ProjectileCapacityMultiplier
            : MaxBossProjectiles;
        while (State.ActiveBoss is not null
            && State.EnemyProjectileHolster.Count > projectileLimit)
        {
            EnemyProjectile longestLasting = State.EnemyProjectileHolster
                .MaxBy(projectile => projectile.Age)!;
            State.EnemyProjectileHolster.Remove(longestLasting);
        }
    }

    /// <summary>Persistent pools are ground hazards and render below every combat actor.</summary>
    public void DrawGroundEnemyProjectiles(SpriteBatch spriteBatch)
    {
        bool highContrast = GameProfile.Profile.HighContrast;
        PathFogOfWar? fog = ActiveVisibilityFog;
        Rectangle viewport = CombatLogicalViewport();
        foreach (var projectile in State.EnemyProjectileHolster)
        {
            if (projectile.Path != "pool"
                || (fog is not null
                    && !fog.IsWorldAreaVisible(projectile.WorldRect()))
                || !IsWorldAreaNearViewport(
                    Camera, PlayerWorldCenter, ScreenShake,
                    viewport, projectile.VisualCullRect()))
            {
                continue;
            }
            projectile.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake, highContrast);
        }
    }

    /// <summary>Airborne hostile shots and telegraphs render above combat actors.</summary>
    public void DrawEnemyProjectiles(SpriteBatch spriteBatch)
    {
        bool highContrast = GameProfile.Profile.HighContrast;
        PathFogOfWar? fog = ActiveVisibilityFog;
        Rectangle viewport = CombatLogicalViewport();
        foreach (var projectile in State.EnemyProjectileHolster)
        {
            if (projectile.Path == "pool"
                || (fog is not null
                    && !fog.IsWorldAreaVisible(projectile.WorldRect()))
                || !IsWorldAreaNearViewport(
                    Camera, PlayerWorldCenter, ScreenShake,
                    viewport, projectile.VisualCullRect()))
            {
                continue;
            }
            projectile.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake, highContrast);
        }
    }

    public void DrawExpeditionSecrets(SpriteBatch spriteBatch)
    {
        if (Expedition is null || PathRun is not null)
            return;
        float time = (float)State.RunTimeSeconds;
        foreach (ExpeditionSecret secret in Expedition.Secrets)
        {
            bool available = secret.IsAvailable(Expedition.DefeatedGuardians);
            Color accent = available
                ? GamePaths.PathsByKey[secret.SenseKey].Accent
                : UiTheme.Muted * .38f;
            float radius = Simulation.TileSize
                * (secret.State >= SecretState.DungeonOpen ? .78f : .34f);
            Primitives2D.FillCircle(spriteBatch, secret.WorldPosition,
                radius * .78f, UiTheme.Ink);
            Primitives2D.CircleOutline(spriteBatch, secret.WorldPosition,
                radius, accent * (.82f + .18f * MathF.Sin(time * 2.1f)),
                secret.State >= SecretState.DungeonOpen ? 4 : 2);
            for (int rune = 0; rune < 5; rune++)
            {
                float angle = rune * MathF.Tau / 5f
                    + time * (secret.State >= SecretState.DungeonOpen ? .7f : .08f);
                Vector2 at = secret.WorldPosition
                    + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * .72f;
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)at.X - 3, (int)at.Y - 3, 6, 6), accent * .7f);
            }
        }
    }

    public void DrawExpeditionHint(SpriteBatch spriteBatch)
    {
        if (CampaignActivity == Systems.CampaignActivity.Aphantasia)
            return;
        if (Expedition is null || PathRun is not null)
            return;
        float radius = Simulation.TileSize * 1.8f;
        ExpeditionSecret? nearby = Expedition.Secrets
            .Where(item => Vector2.DistanceSquared(item.WorldPosition, PlayerWorldCenter) <= radius * radius)
            .OrderBy(item => Vector2.DistanceSquared(item.WorldPosition, PlayerWorldCenter))
            .FirstOrDefault();
        if (nearby is null)
            return;
        float scale = UiTheme.DisplayScale(ScreenWidth, ScreenHeight);
        string prompt = !nearby.IsAvailable(Expedition.DefeatedGuardians)
            ? "THE MARK DOES NOT ANSWER"
            : nearby.State >= SecretState.DungeonOpen
                ? $"{Keybinds.LabelForKey(Keybinds.KeyFor("interact"))}  //  ENTER"
                : $"{nearby.JournalClue}  //  {Keybinds.LabelForKey(Keybinds.KeyFor("interact"))} SEARCH";
        UiTheme.DrawText(spriteBatch, prompt, 10 * scale,
            nearby.IsAvailable(Expedition.DefeatedGuardians)
                ? GamePaths.PathsByKey[nearby.SenseKey].Accent : UiTheme.Muted,
            new Vector2(ScreenWidth / 2f, ScreenHeight - 82 * scale), "center");
    }

    /// <summary>
    /// Draws every elevated combat object together with the arena's raised
    /// scenery in camera-relative painter order. Category priorities preserve
    /// the old readability order only when two ground anchors have identical
    /// depth; otherwise physical north/south depth wins so wall tops and faces
    /// correctly cover players, enemies, and shots behind them.
    ///
    /// Persistent pools stay in DrawGroundEnemyProjectiles because they are
    /// painted onto the floor rather than standing at an elevated anchor.
    /// </summary>
    public void DrawDepthSortedCombatWorld(SpriteBatch spriteBatch)
    {
        PathFogOfWar? fog = ActiveVisibilityFog;
        bool bossProjectileOverlay = State.ActiveBoss is not null;
        Rectangle viewport = CombatLogicalViewport();
        _worldDepthItemScratch.Clear();
        _worldDepthItemScratch.EnsureCapacity(
            State.BulletHolster.Count + State.EnemyHolster.Count
            + (bossProjectileOverlay ? 0 : State.EnemyProjectileHolster.Count)
            + State.LootCrateList.Count + _arenaLightPosts.Count + 8);
        int stableOrder = 0;

        foreach (ArenaLightPost post in _arenaLightPosts)
        {
            var bounds = new Rectangle(
                (int)post.WorldPosition.X - Simulation.TileSize / 2,
                (int)post.WorldPosition.Y - Simulation.TileSize,
                Simulation.TileSize,
                Simulation.TileSize * 2);
            if (!IsWorldAreaNearViewport(
                    Camera, PlayerWorldCenter, ScreenShake,
                    viewport, bounds))
            {
                continue;
            }
            _worldDepthItemScratch.Add(new WorldDepthDrawItem(
                post.WorldPosition, 22, stableOrder++,
                WorldDepthDrawKind.LightPost, post));
        }

        foreach (var bullet in State.BulletHolster)
        {
            if (!IsWorldAreaNearViewport(
                    Camera, PlayerWorldCenter, ScreenShake,
                    viewport, bullet.WorldRect()))
            {
                continue;
            }
            Vector2 anchor = new(bullet.WorldX + bullet.Size / 2f, bullet.WorldY + bullet.Size / 2f);
            _worldDepthItemScratch.Add(new WorldDepthDrawItem(
                anchor, 10, stableOrder++, WorldDepthDrawKind.Bullet, bullet));
        }

        _drawnEncounterIdScratch.Clear();
        foreach (var enemy in State.EnemyHolster)
        {
            if (fog is not null
                && !fog.IsWorldAreaVisible(enemy.WorldRect()))
            {
                continue;
            }
            if (!IsWorldAreaNearViewport(
                    Camera, PlayerWorldCenter, ScreenShake,
                    viewport, enemy.WorldRect()))
            {
                continue;
            }
            var encounter = enemy.Encounter;
            if (encounter is not null && _drawnEncounterIdScratch.Add(encounter.Id))
            {
                _worldDepthItemScratch.Add(new WorldDepthDrawItem(
                    encounter.Center(), 15, stableOrder++,
                    WorldDepthDrawKind.Encounter, encounter));
            }

            Vector2 anchor = new(enemy.WorldX + enemy.Size / 2f, enemy.WorldY + enemy.Size / 2f);
            _worldDepthItemScratch.Add(new WorldDepthDrawItem(
                anchor, 20, stableOrder++, WorldDepthDrawKind.Enemy, enemy));
        }

        foreach (LootCrate crate in State.LootCrateList)
        {
            if ((fog is not null && !fog.IsWorldAreaVisible(crate.WorldRect()))
                || !IsWorldAreaNearViewport(
                    Camera, PlayerWorldCenter, ScreenShake,
                    viewport, crate.WorldRect()))
                continue;
            Vector2 anchor = new(
                crate.WorldX + crate.Size / 2f,
                crate.WorldY + crate.Size);
            _worldDepthItemScratch.Add(new WorldDepthDrawItem(
                anchor, 24, stableOrder++, WorldDepthDrawKind.LootCrate, crate));
        }

        bool pathExit = PathRun?.ExitPortalOpen == true;
        bool pathGateway = PathMajorBossGatewayOpen;
        if (BossPortalOpen || pathExit || pathGateway)
        {
            Vector2 portalWorld = pathGateway
                ? PathRun!.Layout.BossRoom.WorldCenter
                : pathExit
                ? CurrentPathPortalWorld
                : ArenaCenterWorld;
            _worldDepthItemScratch.Add(new WorldDepthDrawItem(
                portalWorld, 26, stableOrder++,
                WorldDepthDrawKind.Portal, portalWorld));
        }

        _worldDepthItemScratch.Add(new WorldDepthDrawItem(
            PlayerWorldCenter, 30, stableOrder++, WorldDepthDrawKind.Player, Player));

        foreach (var projectile in State.EnemyProjectileHolster)
        {
            if (projectile.Path == "pool"
                || (fog is not null
                    && !fog.IsWorldAreaVisible(projectile.WorldRect()))
                || !IsWorldAreaNearViewport(
                    Camera, PlayerWorldCenter, ScreenShake,
                    viewport, projectile.VisualCullRect()))
            {
                continue;
            }
            if (bossProjectileOverlay)
                continue;
            Vector2 anchor = new(projectile.WorldX + projectile.Size / 2f, projectile.WorldY + projectile.Size / 2f);
            _worldDepthItemScratch.Add(new WorldDepthDrawItem(
                anchor, 40, stableOrder++,
                WorldDepthDrawKind.EnemyProjectile, projectile));
        }

        _arenaRenderer.DrawDepthSortedWorld(
            spriteBatch,
            Camera,
            PlayerWorldCenter,
            ScreenShake,
            CombatViewport,
            _worldDepthItemScratch,
            _drawWorldDepthItem,
            (float)State.RunTimeSeconds,
            _visualDensity.Optional,
            _roomVisualEnergy);

        // Boss rooms are deliberately clear of fog and traversal occlusion.
        // Drawing their potentially 150 airborne shots as one overlay avoids
        // inserting and sorting every projectile with the static depth scene,
        // while keeping telegraphs above actors and arena architecture.
        if (bossProjectileOverlay)
        {
            bool highContrast = GameProfile.Profile.HighContrast;
            for (int index = 0;
                 index < State.EnemyProjectileHolster.Count;
                 index++)
            {
                EnemyProjectile projectile =
                    State.EnemyProjectileHolster[index];
                if (projectile.Path != "pool"
                    && IsWorldAreaNearViewport(
                        Camera, PlayerWorldCenter, ScreenShake,
                        viewport, projectile.VisualCullRect()))
                {
                    projectile.Draw(
                        spriteBatch,
                        Camera,
                        PlayerWorldCenter,
                        ScreenShake,
                        highContrast);
                }
            }
        }
    }

    private Rectangle CombatLogicalViewport() =>
        Camera.LogicalViewport(
            CombatViewport);

    internal static bool IsWorldAreaNearViewport(
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        Rectangle logicalViewport,
        Rectangle worldArea,
        float padding = Simulation.TileSize * 3f)
    {
        Vector2 worldCenter = new(
            worldArea.X + worldArea.Width * .5f,
            worldArea.Y + worldArea.Height * .5f);
        Vector2 screenCenter = camera.WorldToScreen(
            worldCenter, playerWorldPosition, screenShake);
        float halfWidth = worldArea.Width * .5f;
        float halfHeight = worldArea.Height * .5f;
        float radius = MathF.Sqrt(
            halfWidth * halfWidth + halfHeight * halfHeight) + padding;
        return screenCenter.X + radius >= logicalViewport.Left
            && screenCenter.X - radius <= logicalViewport.Right
            && screenCenter.Y + radius >= logicalViewport.Top
            && screenCenter.Y - radius <= logicalViewport.Bottom;
    }

    private void DrawWorldDepthItem(SpriteBatch spriteBatch, WorldDepthDrawItem item)
    {
        switch (item.Kind)
        {
            case WorldDepthDrawKind.Bullet:
                ((Bullet)item.Drawable).Draw(
                    spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
                break;
            case WorldDepthDrawKind.Encounter:
                ((RuntimeEncounter)item.Drawable).Draw(
                    spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
                break;
            case WorldDepthDrawKind.Enemy:
                Enemy enemy = (Enemy)item.Drawable;
                enemy.Draw(
                    spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
                if (ReferenceEquals(enemy, State.ActiveBoss)
                    || enemy is PathGuardianBoss)
                {
                    BossVisualRenderer.DrawSoulConstruction(
                        spriteBatch,
                        enemy,
                        Camera,
                        PlayerWorldCenter,
                        ScreenShake,
                        CurrentVisualContext());
                }
                break;
            case WorldDepthDrawKind.LootCrate:
                ((LootCrate)item.Drawable).DrawInWorldPass(
                    spriteBatch,
                    Camera,
                    PlayerWorldCenter,
                    ScreenShake,
                    (float)State.RunTimeSeconds);
                break;
            case WorldDepthDrawKind.LightPost:
                WorldLighting.DrawLightPost(
                    spriteBatch,
                    Camera,
                    PlayerWorldCenter,
                    ScreenShake,
                    (ArenaLightPost)item.Drawable,
                    ActiveLightingPathKey,
                    (float)State.RunTimeSeconds,
                    _visualDensity.Optional);
                break;
            case WorldDepthDrawKind.Portal:
                DrawBossPortalInWorldPass(spriteBatch);
                break;
            case WorldDepthDrawKind.Player:
                Player.Draw(spriteBatch, State, Camera);
                break;
            case WorldDepthDrawKind.EnemyProjectile:
                ((EnemyProjectile)item.Drawable).Draw(
                    spriteBatch, Camera, PlayerWorldCenter, ScreenShake,
                    GameProfile.Profile.HighContrast);
                break;
        }
    }

    /// <summary>Ported from character.py's handlingDamagingEnemies(). Portal-hit routing is deferred (no current enemy type implements it).</summary>
    public void HandleDamagingEnemies(Random? rng = null)
    {
        rng ??= Random.Shared;
        _enemyCollisionGrid.Clear();
        _enemyHitboxScratch.Clear();
        foreach (var enemy in State.EnemyHolster)
        {
            if (ReferenceEquals(enemy, State.ActiveBoss) && DeathSpectacleActive(enemy))
                continue;
            var hitboxes = enemy.GetScreenHitboxes(Camera, PlayerWorldCenter, ScreenShake);
            _enemyHitboxScratch[enemy] = hitboxes;
            for (int index = 0; index < hitboxes.Count; index++)
                _enemyCollisionGrid.Insert(enemy, hitboxes[index].Rect);
        }

        _deadEnemyScratch.Clear();
        foreach (var bullet in State.BulletHolster)
        {
            var bulletScreenPos = Camera.WorldToScreen(new Vector2(bullet.WorldX, bullet.WorldY), PlayerWorldCenter, ScreenShake);
            var bulletRect = new Rectangle((int)bulletScreenPos.X, (int)bulletScreenPos.Y, (int)bullet.Size, (int)bullet.Size);
            _enemyCollisionGrid.Query(
                bulletRect, _collisionCandidateScratch, _collisionCandidateSet);
            _orderedCollisionCandidateScratch.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int index = 0; index < _collisionCandidateScratch.Count; index++)
                {
                    Enemy candidate = _collisionCandidateScratch[index];
                    bool shieldHit = HasShieldIntersection(
                        _enemyHitboxScratch[candidate], bulletRect);
                    if (shieldHit == (pass == 0))
                        _orderedCollisionCandidateScratch.Add(candidate);
                }
            }

            foreach (var enemy in _orderedCollisionCandidateScratch)
            {
                if (_deadEnemyScratch.Contains(enemy))
                    continue;
                var hitboxes = _enemyHitboxScratch[enemy];
                int collidedIndex = -1;
                for (int index = 0; index < hitboxes.Count; index++)
                {
                    if (bulletRect.Intersects(hitboxes[index].Rect))
                    {
                        collidedIndex = index;
                        break;
                    }
                }
                if (collidedIndex < 0)
                    continue;
                var collided = hitboxes[collidedIndex];
                if (enemy.CantTouchMeList.Contains(bullet))
                    continue;
                if (collided.Part.StartsWith("portal:") && enemy is Dissonance dissonance
                    && dissonance.RoutePlayerBullet(bullet, int.Parse(collided.Part["portal:".Length..])))
                {
                    continue;
                }

                enemy.CantTouchMeList.Add(bullet);
                bullet.Pierce -= 1;
                if (bullet.Pierce <= 0)
                    bullet.RemFlag = true;
                double hitDamage = bullet.Damage * StatusEffects.DamageMultiplier(enemy, bullet);
                var result = enemy.TakeDamage(hitDamage, collided.Part, DamageSource.Direct);
                if (result.Applied && !result.Killed)
                {
                    StatusEffects.RollPlayerHit(enemy, bullet, State.Equipment.Values, State.ProjectileCount, rng);
                    if (State.Equipment.GetValueOrDefault("weapon") is { } weapon && Items.ActiveEffectIds(weapon).Count > 0)
                        UniqueEffects.OnPlayerHit(enemy, bullet, weapon, State, rng);
                }
                if (result.Applied)
                {
                    enemy.MarkVisualHit(result.Blocked ? .16f : .1f);
                    Vector2 impactWorld = new(
                        enemy.WorldX + enemy.Size / 2f,
                        enemy.WorldY + enemy.Size / 2f);
                    Vector2 bulletBias = new(
                        MathF.Cos(bullet.Direction),
                        -MathF.Sin(bullet.Direction));
                    string recipe = result.Blocked
                        ? "shield"
                        : bullet.IsCritical ? "critical" : "impact";
                    _visualEffects.Emit(
                        recipe,
                        impactWorld,
                        result.Blocked ? UiTheme.Blue : bullet.Color,
                        bullet.IsCritical ? UiTheme.Purple : UiTheme.Cream,
                        (int)(bullet.WorldX * 31 + bullet.WorldY * 17 + enemy.Hp),
                        _visualDensity.Optional,
                        bulletBias * 1.2f);
                    GameProfile.IncrementQuest("damage_dealt", Math.Max(0, (long)Math.Round(result.Amount)), State);
                    if (bullet.IsCritical)
                        GameProfile.IncrementQuest("critical_hits", state: State);
                }
                HandleEnemyTransitionRequests(enemy);
                Color currColor = bullet.IsCritical ? UiTheme.Purple : UiTheme.Gold;
                object displayValue = result.Applied ? Math.Round(result.Amount) : "BLOCK";
                var textWorld = Camera.ScreenToWorld(new Vector2(collided.Rect.X, collided.Rect.Y), PlayerWorldCenter, ScreenShake);
                State.DamageTextList.Add(new DamageText(textWorld.X, textWorld.Y, currColor, displayValue, collided.Rect.Width, Simulation.FrameRate));
                if (result.Killed)
                    _deadEnemyScratch.Add(enemy);
            }
        }

        foreach (var enemy in State.EnemyHolster)
            if (enemy.IsDead())
                _deadEnemyScratch.Add(enemy);

        foreach (var enemy in _deadEnemyScratch)
        {
            bool bossDeath = ReferenceEquals(enemy, State.ActiveBoss);
            _visualEffects.Emit(
                bossDeath ? "boss_death" : "death",
                new Vector2(enemy.WorldX + enemy.Size / 2f, enemy.WorldY + enemy.Size / 2f),
                enemy.Color,
                UiTheme.Cream,
                (int)(enemy.WorldX * 13 + enemy.WorldY * 29 + State.NumOfEnemiesKilled),
                _visualDensity.Optional);
            State.NumOfEnemiesKilled += 1;
            GameProfile.IncrementQuest("enemies_defeated", state: State);
            GameProfile.IncrementQuest($"kills_sense_{CampaignActivitySense ?? GamePaths.Active().Key}", state: State);
            var xpTier = enemy is Beaudis or PathGuardianBoss || enemy.Family == "miniboss"
                ? ExperienceBubble.ExperienceTier.Guardian
                : enemy is Dissonance or Aphantasia
                    ? ExperienceBubble.ExperienceTier.FinalBoss
                    : ExperienceBubble.ExperienceTier.Standard;
            State.ExperienceList.Add(new ExperienceBubble(
                enemy.WorldX, enemy.WorldY,
                State.XpMult * (enemy.ExpValue * (State.CurrentStage * State.ExperienceStageMod)),
                enemy.Difficulty, rng, celebration: ReferenceEquals(enemy, State.ActiveBoss), tier: xpTier));
            if (RollFragmentDrop(rng))
                State.FragmentList.Add(new FragmentPickup(enemy.WorldX, enemy.WorldY, rng));

            int volatileCount = enemy.VolatileBurst;
            if (volatileCount > 0)
            {
                float centerX = enemy.WorldX + enemy.Size / 2f, centerY = enemy.WorldY + enemy.Size / 2f;
                for (int index = 0; index < volatileCount; index++)
                {
                    State.EnemyProjectileHolster.Add(new EnemyProjectile(
                        centerX, centerY, index * 2f * MathF.PI / volatileCount, .72f, enemy.Damage * .22f, enemy.Size * .18f,
                        travelRange: Simulation.TileSize * 4.5f, color: UiTheme.Red, shape: "diamond", owner: "volatile_enemy"));
                }
            }

            // Boss key computed up front (rather than inside the boss-only
            // block below, as before) so RollUniqueDrop can add its result
            // to this same crate -- a bonus unique is guaranteed a slot on
            // the boss kill that earns it, independent of the regular
            // RollDropCount roll that still runs (and could otherwise land
            // on 0) for every enemy, boss or not.
            string? defeatedBossKey = ReferenceEquals(enemy, State.ActiveBoss) ? (_activeBossKey ?? BossKeyFor(enemy)) : null;
            bool treasureEncounter = PathRun?.Layout.TreasureRooms.Any(
                room => room.EncounterKey == enemy.EncounterKey) == true;
            int regularDropCount = treasureEncounter
                ? 0
                : PathRun is null
                    ? Items.RollDropCount(rng)
                    : Items.RollPathDropCount(rng);
            string dropPath = CampaignActivitySense ?? GamePaths.Active().Key;
            var drops = Items.GenerateDrops(regularDropCount, rng, State.AnyHardModeActive, dropPath,
                State.NewGamePlusLevel, State.IsTrueHardMode);
            if (defeatedBossKey is not null && Items.RollUniqueDrop(defeatedBossKey, rng, State.NewGamePlusLevel) is { } uniqueDrop)
                drops.Add(uniqueDrop);
            if (drops.Count > 0)
            {
                SpawnLootCrate(enemy.WorldX, enemy.WorldY, drops);
                EmitDropFanfare(new Vector2(enemy.WorldX + enemy.Size / 2f, enemy.WorldY + enemy.Size / 2f), drops);
            }

            if (defeatedBossKey is not null)
            {
                GameProfile.IncrementQuest("bosses_defeated", state: State);
                if (defeatedBossKey == "aphantasia")
                {
                    State.GameCompleted = true;
                    Aphantasia? defeatedAphantasia = enemy as Aphantasia;
                    bool capturedNoHealing = defeatedAphantasia?.CapturedNoHealing ?? State.NoHealing;
                    bool capturedNoExtract = defeatedAphantasia?.CapturedNoExtract ?? State.NoExtract;
                    CampaignProgression.CompleteAphantasia(capturedNoHealing, capturedNoExtract);
                    if (capturedNoHealing && capturedNoExtract)
                        MetaProgression.RecordCoreOfTheVoidDefeat();
                    FinalizeSuccessfulRun(RunOutcomes.AphantasiaDefeated,
                        completed: true);
                }
                else if (defeatedBossKey == GamePaths.BossKey(midpoint: true))
                {
                    State.BeaudisDefeated = true;
                }
                else if (defeatedBossKey == GamePaths.BossKey(midpoint: false)
                    && PathRun?.IsSecretDungeon != true)
                {
                    State.GameCompleted = true;
                    RecordCampaignClear();
                    FinalizeSuccessfulRun(PathRun is null
                            ? RunOutcomes.RunComplete
                            : RunOutcomes.DungeonComplete,
                        completed: true);
                }
                CompleteBossTelemetry(victory: true);
                State.ActiveBoss = null;
                _activeBossKey = null;
                State.EnemySpawningEnabled = !State.GameCompleted;
                ScreenShake = Vector2.Zero;
                State.EnemyProjectileHolster.Clear();
                PathRun?.NotifyBossDefeated();
                if (PathRun?.IsSecretDungeon == true && PathRun.IsComplete)
                    ReturnFromSecretDungeon();
            }
        }
        if (_deadEnemyScratch.Count > 0)
        {
            State.EnemyHolster.RemoveAll(e => _deadEnemyScratch.Contains(e));
            State.CurrEnemyCount = State.EnemyHolster.Count;
        }
        State.BulletHolster.RemoveAll(b => b.RemFlag);
    }

    private static bool HasShieldIntersection(
        IReadOnlyList<(string Part, Rectangle Rect)> hitboxes,
        Rectangle bulletRect)
    {
        for (int index = 0; index < hitboxes.Count; index++)
        {
            var hitbox = hitboxes[index];
            if (hitbox.Part == "shield" && bulletRect.Intersects(hitbox.Rect))
                return true;
        }
        return false;
    }

    private static string? BossKeyFor(Enemy enemy) => enemy switch
    {
        Aphantasia => "aphantasia",
        Beaudis => "beaudis", Dissonance => "dissonance", Chronos => "chronos", Ishe => "ishe",
        Bair => "bair", Sting => "sting", Rot => "rot", Ache => "ache", Kage => "kage", Hypno => "hypno", Malady => "malady",
        _ => null,
    };

    private void UpdateBossTelemetry()
    {
        if (_bossTelemetry is null || State.ActiveBoss is not Enemy boss)
            return;
        double seconds = Simulation.GetTimerStep()
            / Math.Max(1, Simulation.FrameRate);
        string phase = boss switch
        {
            Aphantasia aphantasia =>
                $"PHASE {aphantasia.Phase} // {aphantasia.PhaseLabel}",
            PathGuardianBoss { TrialActive: true } guardian =>
                $"TRIAL // {guardian.PhaseLabel}",
            PathGuardianBoss guardian =>
                $"PHASE {guardian.Phase} // {guardian.PhaseLabel}",
            Beaudis beaudis =>
                $"PHASE {beaudis.Phase} // {beaudis.PhaseLabel}",
            Dissonance dissonance =>
                $"PHASE {dissonance.Phase} // {dissonance.PhaseLabel}",
            PathChaseBoss pathBoss =>
                $"PHASE {pathBoss.Phase} // {pathBoss.PhaseLabel}",
            _ => "ENGAGED",
        };
        bool phaseChanged = _bossTelemetry.ObservePhase(phase, seconds);
        if (phaseChanged && boss is not PathGuardianBoss)
        {
            BossAudio.Emit(
                BossAudioCueKind.Stagger,
                _bossTelemetry.SenseKey);
        }
        if (!_bossTelemetryDeathCueEmitted
            && DeathSpectacleActive(boss))
        {
            _bossTelemetryDeathCueEmitted = true;
            if (boss is not PathGuardianBoss)
            {
                BossAudio.Emit(
                    BossAudioCueKind.Death,
                    _bossTelemetry.SenseKey);
            }
        }
    }

    private void CompleteBossTelemetry(bool victory)
    {
        if (_bossTelemetry is null)
            return;
        var completed = _bossTelemetry.Finish(
            State.RunTimeSeconds,
            victory);
        State.BossEncounterTelemetry.Add(completed);
        GameProfile.RecordBossEncounter(completed);
        _bossTelemetry = null;
        _bossTelemetryDeathCueEmitted = false;
    }

    private static bool DeathSpectacleActive(object? boss) => boss is
        Aphantasia { CompletionReady: true }
        or Beaudis { Dying: true }
        or Dissonance { Dying: true }
        or PathGuardianBoss { Dying: true }
        or PathChaseBoss { Dying: true };

    // ----- Damage text / experience / loot -----

    /// <summary>Ported from character.py's updateDamageTexts(). Split from drawing, same reasoning as UpdateEnemies/DrawEnemies.</summary>
    public void UpdateDamageTexts()
    {
        if (!GameProfile.Profile.DamageNumbers)
        {
            State.DamageTextList.Clear();
            return;
        }
        foreach (var text in State.DamageTextList)
            text.Update();
        State.DamageTextList.RemoveAll(t => t.DeleteMe);
    }

    public void DrawDamageTexts(SpriteBatch spriteBatch)
    {
        foreach (var text in State.DamageTextList)
            text.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake, DamageTextFontSize);
    }

    /// <summary>Ported from character.py's updateExperience().</summary>
    public void UpdateExperience()
    {
        foreach (var bubble in State.ExperienceList)
            bubble.Update((float)State.AuraSpeed, Battleground);
        foreach (var fragment in State.FragmentList)
            fragment.Update((float)State.AuraSpeed, Battleground);
    }

    public void DrawExperience(SpriteBatch spriteBatch)
    {
        foreach (var bubble in State.ExperienceList)
            bubble.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
        foreach (var fragment in State.FragmentList)
            fragment.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
    }

    /// <summary>
    /// Collects touching experience bubbles into the stored EXP bank. It no
    /// longer consumes thresholds or opens the level-up screen: the player
    /// explicitly chooses that through TryPurchaseLevelUp.
    /// </summary>
    public void ExpForPlayer()
    {
        var playerRect = Player.WorldRect(State);
        for (int index = State.ExperienceList.Count - 1; index >= 0; index--)
        {
            ExperienceBubble bubble = State.ExperienceList[index];
            var bubbleRect = bubble.WorldRect();
            if (playerRect.Intersects(bubbleRect))
            {
                _visualEffects.Emit(
                    "pickup", PlayerWorldCenter, UiTheme.Green, UiTheme.Cream,
                    (int)(bubble.WorldX * 19 + bubble.WorldY * 7),
                    _visualDensity.Optional);
                GrantExperience(bubble);
                State.ExperienceList.RemoveAt(index);
                continue;
            }

            var auraRect = playerRect;
            auraRect.Inflate((int)(2 * (State.Aura + bubble.Size)), (int)(2 * (State.Aura + bubble.Size)));
            if (auraRect.Intersects(bubbleRect))
            {
                bubble.NaturalSpawn = false;
                float originX = Player.WorldX + (float)State.PlayerSize / 2f, originY = Player.WorldY + (float)State.PlayerSize / 2f;
                float deltaX = bubble.WorldX - originX, deltaY = bubble.WorldY - originY;
                bubble.Direction = deltaX == 0
                    ? (deltaY > 0 ? MathF.PI / 2f : -MathF.PI / 2f)
                    : (deltaX > 0 ? MathF.Atan(deltaY / deltaX) : -MathF.Atan(deltaY / MathF.Abs(deltaX)) + MathF.PI);
            }
            else
            {
                bubble.NaturalSpawn = true;
            }
        }

        for (int index = State.FragmentList.Count - 1; index >= 0; index--)
        {
            FragmentPickup fragment = State.FragmentList[index];
            var fragmentRect = fragment.WorldRect();
            if (playerRect.Intersects(fragmentRect))
            {
                _visualEffects.Emit(
                    "pickup", PlayerWorldCenter, UiTheme.Gold, UiTheme.Cream,
                    (int)(fragment.WorldX * 11 + fragment.WorldY * 23),
                    _visualDensity.Optional);
                State.Fragments += 1;
                State.FragmentList.RemoveAt(index);
                continue;
            }

            var auraRect = playerRect;
            auraRect.Inflate((int)(2 * (State.Aura + FragmentPickup.Size)),
                (int)(2 * (State.Aura + FragmentPickup.Size)));
            if (auraRect.Intersects(fragmentRect))
            {
                fragment.NaturalSpawn = false;
                float originX = Player.WorldX + (float)State.PlayerSize / 2f;
                float originY = Player.WorldY + (float)State.PlayerSize / 2f;
                float deltaX = fragment.WorldX - originX, deltaY = fragment.WorldY - originY;
                fragment.Direction = MathF.Atan2(deltaY, deltaX);
            }
            else
            {
                fragment.NaturalSpawn = true;
            }
        }
    }

    /// <summary>
    /// Applies one collected bubble's XP to the run. Normally banks bubble.Value
    /// as usual. Under Golden Flame/The Void, bubble.Value is ignored entirely:
    /// Golden Flame banks a flat third of the current level threshold for
    /// Standard-tier bubbles ("no matter the value") and grants instant whole
    /// levels for Guardian/FinalBoss tiers; The Void (taking priority when both
    /// are lit) always grants instant whole levels, bigger at each tier. See
    /// RunState.GoldenFlameMode/VoidMode and ExperienceBubble.ExperienceTier.
    /// </summary>
    private void GrantExperience(ExperienceBubble bubble)
    {
        if (State.VoidMode)
        {
            AdvanceLevel(bubble.Tier switch
            {
                ExperienceBubble.ExperienceTier.Guardian => 3,
                ExperienceBubble.ExperienceTier.FinalBoss => 5,
                _ => 1,
            });
        }
        else if (State.GoldenFlameMode)
        {
            if (bubble.Tier == ExperienceBubble.ExperienceTier.Standard)
                State.ExpCount += State.ExpNeededForNextLevel / 3.0;
            else
                AdvanceLevel(bubble.Tier == ExperienceBubble.ExperienceTier.Guardian ? 1 : 2);
        }
        else
        {
            State.ExpCount += bubble.Value;
        }
    }

    private void HandlePathEnemyCreation(Random rng, bool interactPressed)
    {
        var run = PathRun!;
        if (!run.TitleBannerVisible(State.RunTimeSeconds))
            _ = run.PrepareNextFloorAsync((float)State.PlayerSize);
        _pathWaveSpawnBudgetRemaining = PathWaveSpawnBudgetPerFrame;
        ProcessPendingPathWaves();
        RefreshPathFog();
        if (interactPressed)
        {
            run.Layout.TryRevealTreasure(
                PlayerWorldCenter,
                Simulation.TileSize * 2.1f);
        }
        IReadOnlyList<PathRoom> completedRooms =
            run.CompleteReadyCombatRooms(
                State.EnemyHolster,
                _pendingPathEncounterKeys);
        for (int index = 0; index < completedRooms.Count; index++)
        {
            PathRoom completedRoom = completedRooms[index];
            _roomClearedAt[completedRoom.Id] = State.RunTimeSeconds;
            _visualEffects.Emit(
                "room_release",
                completedRoom.WorldCenter,
                run.CurrentSense.Accent,
                UiTheme.Cream,
                completedRoom.Id * 7919 + run.FloorNumber,
                _visualDensity.Optional);
            if (completedRoom.Type == PathRoomType.Treasure)
                SpawnPathTreasure(completedRoom, rng);
            else if (completedRoom.Type == PathRoomType.Challenge)
                SpawnPathTreasure(completedRoom, rng, bonusItems: 1);
        }
        if (run.ActiveCombatRooms.Count == 0 && State.ActiveBoss is null)
            State.BossAfflictions.Reset();

        if (PathMajorBossGatewayOpen)
        {
            Vector2 gateway = run.Layout.BossRoom.WorldCenter;
            if (interactPressed && PlayerAtPathPortal(gateway))
                EnterPathMajorBossInstance(rng);
            return;
        }

        if (run.ExitPortalOpen)
        {
            if (interactPressed && PlayerAtPathPortal(CurrentPathPortalWorld)
                && run.AdvanceFloor(State.RunTimeSeconds))
            {
                InstallNextPathFloor();
            }
            return;
        }

        PathRoom? occupiedRoom = run.Layout.RoomAt(PlayerWorldCenter);
        PathRoom? room = run.TryActivateRoom(PlayerWorldCenter, State.RunTimeSeconds);
        if (room is not null)
        {
            PreloadPathRoomEncounter(room, rng);
            if (room.Type == PathRoomType.Boss
                && run.FloorNumber is not (5 or 10))
            {
                SpawnPathFloorBoss(room, rng);
            }
        }

        if (occupiedRoom is not null && occupiedRoom.Type != PathRoomType.Boss)
            PreloadAdjacentPathRoomEncounters(occupiedRoom, rng);
    }

    private void RecordCampaignClear()
    {
        string sense = CampaignActivitySense
            ?? PathRun?.CurrentSenseKey
            ?? GamePaths.Active().Key;
        if (CampaignActivity == Systems.CampaignActivity.Arena)
            CampaignProgression.CompleteStatue(sense, StatueMaterial.Silver,
                State.NoHealing, State.NoExtract);
        // The standalone dungeon is intentionally progression-neutral. Gold
        // statues belong exclusively to completed Soul finales.
    }

    private void ReturnFromSecretDungeon()
    {
        ExpeditionRun expedition = Expedition
            ?? throw new InvalidOperationException("Secret dungeon lost its expedition.");
        expedition.CompleteDungeon();
        bool cycleComplete = expedition.Complete;
        string finale = expedition.FinaleSense;
        CampaignWorld world = expedition.World;
        if (cycleComplete && world == CampaignWorld.Body)
        {
            ContinueCompletedBodyIntoSoul();
            return;
        }
        Vector2 returnPosition = expedition.SuspendedReturnPosition
            ?? expedition.Battleground.SpawnPosition;
        PathRun = null;
        PathFog = null;
        Battleground = expedition.Battleground;
        RefreshLightingFixtures();
        Player.SetPosition(returnPosition.X - (float)State.PlayerSize / 2f,
            returnPosition.Y - (float)State.PlayerSize / 2f);
        State.ActiveBoss = null;
        State.EnemyHolster.Clear();
        State.EnemyProjectileHolster.Clear();
        State.BulletHolster.Clear();
        State.GameCompleted = false;
        State.EnemySpawningEnabled = true;
        LastRunRewardSummary = null;
        if (!cycleComplete)
            return;
        if (world == CampaignWorld.Soul)
            CampaignProgression.CompleteSoul(finale, State.NoHealing, State.NoExtract);
    }

    /// <summary>
    /// Populates revealed neighboring combat rooms before the player crosses
    /// their threshold. Preloaded enemies remain dormant until their room is
    /// activated, so they are already distributed when they first become
    /// visible without attacking through a wall.
    /// </summary>
    private void PreloadAdjacentPathRoomEncounters(PathRoom occupiedRoom, Random rng)
    {
        PathFloorLayout layout = PathRun!.Layout;
        foreach (PathConnection connection in layout.Connections)
        {
            if (!connection.IsRevealed)
                continue;
            int adjacentId;
            if (connection.FromRoomId == occupiedRoom.Id)
                adjacentId = connection.ToRoomId;
            else if (connection.ToRoomId == occupiedRoom.Id)
                adjacentId = connection.FromRoomId;
            else
                continue;

            PathRoom adjacent = layout.Rooms.First(value => value.Id == adjacentId);
            if (adjacent.IsRevealed)
                PreloadPathRoomEncounter(adjacent, rng);
        }
    }

    private void PreloadPathRoomEncounter(PathRoom room, Random rng)
    {
        if (!room.IsCombatRoom)
            return;
        bool encounterStillExists = _pendingPathEncounterKeys.Contains(room.EncounterKey)
            || State.EnemyHolster.Any(enemy => enemy.EncounterKey == room.EncounterKey);
        if (_preloadedPathEncounterKeys.Contains(room.EncounterKey)
            && encounterStillExists)
            return;
        _preloadedPathEncounterKeys.Add(room.EncounterKey);

        if (room.Type == PathRoomType.Treasure)
            SpawnPathTreasureEncounter(room, rng);
        else
            SpawnPathRoomWave(room, rng);
    }

    private bool IsDormantPathEnemy(Enemy enemy)
    {
        if (PathRun is null || enemy.EncounterKey is not string encounterKey)
            return false;
        PathRoom? room = PathRun.Layout.Rooms.FirstOrDefault(
            candidate => candidate.EncounterKey == encounterKey);
        return room is not null && !room.IsActivated;
    }

    private void SpawnPathRoomWave(PathRoom room, Random rng, bool guardianStrength = false)
    {
        var run = PathRun!;
        int encounterLevel = Math.Clamp(1 + run.FloorNumber * 2, 1, Progression.MaxLevel);
        int count = room.Type switch
        {
            PathRoomType.Skirmish => 3 + run.FloorNumber / 4,
            PathRoomType.Assault => 5 + run.FloorNumber / 3,
            PathRoomType.Elite => 4 + run.FloorNumber / 3,
            PathRoomType.Challenge => 7 + run.FloorNumber / 2,
            PathRoomType.Treasure => 8 + run.FloorNumber / 2,
            _ => 3,
        };
        count += room.Shape switch
        {
            PathRoomShape.LongHall => 2,
            PathRoomShape.GrandArena => 3,
            PathRoomShape.Maze => 1,
            PathRoomShape.Crossroads => 1,
            PathRoomShape.Ring => 2,
            _ => 0,
        };
        if (run.IsSecondAct)
            count += 2;
        count = Math.Min(room.Type == PathRoomType.Treasure ? 18 : 15, count);

        IReadOnlyList<EnemyDefinition> availableDefinitions =
            EnemyCatalog.Shared.Available(encounterLevel, run.CurrentSenseKey);
        _pendingPathWaves.Add(new PendingPathWave
        {
            Room = room,
            Rng = rng,
            Definitions = availableDefinitions,
            EncounterLevel = encounterLevel,
            Count = count,
            GuardianStrength = guardianStrength,
        });
        _pendingPathEncounterKeys.Add(room.EncounterKey);
        ProcessPendingPathWaves();
    }

    private void ProcessPendingPathWaves()
    {
        while (_pathWaveSpawnBudgetRemaining > 0
            && _pendingPathWaves.Count > 0)
        {
            PendingPathWave wave = _pendingPathWaves[0];
            EnemyDefinition? definition = ChoosePathRoomEnemy(
                wave.Room,
                wave.NextIndex,
                wave.Definitions,
                wave.FamilyCounts,
                wave.Rng);
            if (definition is null)
            {
                CompletePendingPathWave(wave);
                continue;
            }

            int nominalSize =
                (int)(Simulation.TileSize * definition.Size);
            Rectangle spawn = PathRun!.Layout.FindEncounterSpawnRect(
                wave.Room,
                nominalSize,
                wave.NextIndex,
                wave.Count,
                wave.Rng);
            Enemy enemy = EnemyCatalog.Shared.Create(
                definition.Key,
                spawn.X,
                spawn.Y,
                wave.EncounterLevel,
                AwarenessRange,
                wave.Rng,
                Battleground,
                Math.Max(wave.EncounterLevel, State.CurrentLevel));
            EnemyCatalog.Shared.ApplyModifier(
                enemy,
                wave.EncounterLevel,
                wave.Rng);
            ConfigurePathRoomEnemy(wave, enemy);
            wave.Spawned.Add(enemy);
            wave.FamilyCounts[enemy.Family] =
                wave.FamilyCounts.GetValueOrDefault(enemy.Family) + 1;
            wave.NextIndex++;
            _pathWaveSpawnBudgetRemaining--;

            if (!wave.GuardianStrength)
                State.EnemyHolster.Add(enemy);
            if (wave.NextIndex >= wave.Count)
                CompletePendingPathWave(wave);
        }
        State.CurrEnemyCount = State.EnemyHolster.Count;
    }

    private void ConfigurePathRoomEnemy(PendingPathWave wave, Enemy enemy)
    {
        PathRoom room = wave.Room;
        int index = wave.NextIndex;
        enemy.EncounterKey = room.EncounterKey;
        enemy.AwarenessRange = Math.Max(ScreenHeight * 2.25f,
            Math.Max(room.WorldBounds.Width, room.WorldBounds.Height));
        enemy.DisengageRange = enemy.AwarenessRange * 1.5f;
        if ((room.Type == PathRoomType.Elite && index == 0)
            || (room.Type == PathRoomType.Challenge && index < 2)
            || (room.Type == PathRoomType.Treasure && index < 3))
        {
            double healthBoost = room.Type switch
            {
                PathRoomType.Challenge => 1.45,
                PathRoomType.Treasure => 1.35,
                _ => 1.7,
            };
            enemy.MaxHp = (int)Math.Round(enemy.MaxHp * healthBoost);
            enemy.Hp = enemy.MaxHp;
            enemy.Damage = (int)Math.Round(enemy.Damage * 1.2);
            enemy.ExpValue *= 1.8;
            enemy.BehaviorModifier ??= "champion";
            enemy.ModifierColor ??= UiTheme.Gold;
        }
        ApplyRunDifficulty(enemy);
    }

    private void CompletePendingPathWave(PendingPathWave wave)
    {
        if (wave.GuardianStrength && wave.Spawned.Count > 0)
        {
            var run = PathRun!;
            double guardianHealth = (5_800 + run.FloorNumber * 1_550)
                * run.HealthMultiplier * .92;
            double currentHealth =
                wave.Spawned.Sum(enemy => (double)enemy.MaxHp);
            double healthScale = Math.Clamp(
                guardianHealth / Math.Max(1, currentHealth),
                .85,
                3.5);
            foreach (Enemy enemy in wave.Spawned)
            {
                enemy.MaxHp = Math.Max(
                    1,
                    (int)Math.Round(enemy.MaxHp * healthScale));
                enemy.Hp = enemy.MaxHp;
                enemy.Damage = Math.Max(
                    1,
                    (int)Math.Round(enemy.Damage * 1.12));
                enemy.ExpValue *= 1.35;
            }
            State.EnemyHolster.AddRange(wave.Spawned);
        }
        _pendingPathEncounterKeys.Remove(wave.Room.EncounterKey);
        _pendingPathWaves.RemoveAt(0);
    }

    private void SpawnPathTreasureEncounter(PathRoom room, Random rng)
    {
        var run = PathRun!;
        bool miniGuardian = (room.Variant + room.Id + run.FloorNumber) % 2 == 0;
        if (!miniGuardian)
        {
            SpawnPathRoomWave(room, rng, guardianStrength: true);
            return;
        }

        float size = Simulation.TileSize * 1.7f;
        var guardian = new PathGuardianBoss(
            room.WorldCenter.X - size / 2f,
            room.WorldCenter.Y - size / 2f,
            run.CurrentSenseKey,
            run.FloorNumber,
            Math.Max(ScreenHeight * 2.25f,
                Math.Max(room.WorldBounds.Width, room.WorldBounds.Height)),
            rng,
            PathGuardianArenaRadius(room))
        {
            EncounterKey = room.EncounterKey,
            IsMiniGuardian = true,
        };
        guardian.DisengageRange = guardian.AwarenessRange * 1.5f;
        ApplyRunDifficulty(guardian);
        guardian.MaxHp = Math.Max(1, (int)Math.Round(guardian.MaxHp * .65));
        guardian.Hp = guardian.MaxHp;
        guardian.Damage = Math.Max(1, (int)Math.Round(guardian.Damage * .90));
        guardian.ExpValue *= 1.15;
        State.EnemyHolster.Add(guardian);
        State.CurrEnemyCount = State.EnemyHolster.Count;
    }

    private static float PathGuardianArenaRadius(PathRoom room)
    {
        int minimumTiles = Math.Min(room.InteriorTileBounds.Width,
            room.InteriorTileBounds.Height);
        return Simulation.TileSize * Math.Max(3.4f,
            (minimumTiles - 2) * .43f);
    }

    private static EnemyDefinition? ChoosePathRoomEnemy(
        PathRoom room,
        int index,
        IReadOnlyList<EnemyDefinition> definitions,
        IReadOnlyDictionary<string, int> familyCounts,
        Random rng)
    {
        string[] roles = room.Shape switch
        {
            PathRoomShape.LongHall => LongHallRoles,
            PathRoomShape.GrandArena => GrandArenaRoles,
            PathRoomShape.Maze => MazeRoles,
            PathRoomShape.Crossroads => CrossroadsRoles,
            PathRoomShape.Ring => RingRoles,
            PathRoomShape.Ruin => RuinRoles,
            _ => DefaultRoomRoles,
        };
        string desiredRole = roles[index % roles.Length];

        double totalWeight = PathDefinitionWeight(
            definitions, familyCounts, desiredRole);
        string? role = desiredRole;
        if (totalWeight <= 0)
        {
            role = null;
            totalWeight = PathDefinitionWeight(
                definitions, familyCounts, requiredRole: null);
        }
        if (totalWeight <= 0)
            return null;

        double roll = rng.NextDouble() * totalWeight;
        EnemyDefinition? fallback = null;
        foreach (EnemyDefinition definition in definitions)
        {
            if (!PathDefinitionEligible(definition, familyCounts, role))
                continue;
            fallback = definition;
            roll -= definition.Weight;
            if (roll <= 0)
                return definition;
        }
        return fallback;
    }

    private static double PathDefinitionWeight(
        IReadOnlyList<EnemyDefinition> definitions,
        IReadOnlyDictionary<string, int> familyCounts,
        string? requiredRole)
    {
        double total = 0;
        for (int index = 0; index < definitions.Count; index++)
        {
            EnemyDefinition definition = definitions[index];
            if (PathDefinitionEligible(definition, familyCounts, requiredRole))
                total += definition.Weight;
        }
        return total;
    }

    private static bool PathDefinitionEligible(
        EnemyDefinition definition,
        IReadOnlyDictionary<string, int> familyCounts,
        string? requiredRole)
    {
        if (definition.GuaranteedOnly || definition.Family == "banner"
            || familyCounts.GetValueOrDefault(definition.Family) >= definition.MaxActive)
        {
            return false;
        }
        return requiredRole is null
            || (EnemyCatalogData.FamilyIdentities.TryGetValue(
                    definition.Family, out FamilyIdentity identity)
                && identity.CombatRole == requiredRole);
    }

    private void SpawnPathTreasure(PathRoom room, Random rng, int bonusItems = 0)
    {
        var run = PathRun!;
        int count = 3 + bonusItems + rng.Next(2);
        int rewardTier = Math.Max(1,
            run.FloorNumber >= 8 ? 3 : run.FloorNumber >= 5 ? 2 : run.FloorNumber >= 3 ? 1 : 0);
        var drops = Items.GenerateDrops(count, rng, State.AnyHardModeActive, run.CurrentSenseKey, rewardTier, State.IsTrueHardMode);
        if (drops.Count > 0 && drops[0].Rarity is "Common" or "Rare")
            drops[0] = drops[0] with { Rarity = "Epic" };
        float size = Simulation.TileSize * 1.15f;
        var chest = new TreasureChest(
            room.WorldCenter.X - size / 2f,
            room.WorldCenter.Y - size / 2f,
            drops,
            run.CurrentSenseKey);
        State.LootCrateList.Add(chest);
        EmitDropFanfare(room.WorldCenter, drops);
    }

    private void EnterPathMajorBossInstance(Random rng)
    {
        var run = PathRun
            ?? throw new InvalidOperationException("A Path run is required for a dungeon boss instance.");
        if (run.FloorNumber is not (5 or 10) || _dungeonBossInstance is not null)
            return;

        bool midpoint = run.BossTier == PathFloorBossTier.Midpoint;
        string bossKey = midpoint ? run.CurrentSense.MidBoss : run.CurrentSense.FinalBoss;
        if (!BossCatalog.Shared.TryGet(bossKey, out BossDefinition? definition)
            || definition is null)
        {
            throw new InvalidOperationException($"Boss '{bossKey}' is not registered.");
        }

        Battleground suspended = Battleground;
        PathFogOfWar? suspendedFog = PathFog;
        float arenaScale = run.FloorNumber == PathRun.TotalFloors
            && !run.IsSecretDungeon ? 1.5f : 1f;
        Battleground arena = BossArenaFactory.Create(bossKey, run.FloorNumber, arenaScale);
        Vector2 center = new(
            arena.Width * Simulation.TileSize / 2f,
            arena.Height * Simulation.TileSize / 2f);
        _dungeonBossInstance = new DungeonBossInstanceState(
            suspended,
            suspendedFog,
            arena,
            bossKey,
            run.FloorNumber,
            center);

        _pendingPathWaves.Clear();
        _pendingPathEncounterKeys.Clear();
        _preloadedPathEncounterKeys.Clear();
        State.EnemyHolster.Clear();
        State.EnemyProjectileHolster.Clear();
        State.BulletHolster.Clear();
        State.DamageTextList.Clear();
        State.ExperienceList.Clear();
        State.FragmentList.Clear();
        State.LootCrateList.Clear();
        State.NearbyCrate = null;
        State.BossAfflictions.Reset();
        State.DreamState.Reset();
        State.CurrEnemyCount = 0;

        Battleground = arena;
        RefreshLightingFixtures();
        PathFog = null;
        _pathFogActive = false;
        _enemyCollisionGrid.Reset();
        Player.SetPosition(arena.SpawnPosition.X, arena.SpawnPosition.Y);
        ScreenShake = Vector2.Zero;

        float size = Simulation.TileSize * 1.9f;
        var forcedRect = new Rectangle(
            (int)(center.X - size / 2f),
            (int)(center.Y - size / 2f),
            (int)size,
            (int)size);
        SpawnBoss(
            (x, y, r) => definition.Factory(x, y, arena, AwarenessRange, r),
            rng,
            forcedRect,
            bossKey,
            clearFloorLoot: false,
            clearCombatants: false);
        State.GracePeriod = Simulation.FrameRate * 2.0;
    }

    private void SpawnPathFloorBoss(PathRoom room, Random rng)
    {
        var run = PathRun!;
        float size = Simulation.TileSize * 1.9f;
        var forcedRect = new Rectangle(
            (int)(room.WorldCenter.X - size / 2f),
            (int)(room.WorldCenter.Y - size / 2f),
            (int)size, (int)size);

        if (run.BossTier == PathFloorBossTier.Guardian)
        {
            string key = $"path_guardian_{run.CurrentSenseKey}";
            SpawnBoss((x, y, r) => new PathGuardianBoss(x, y, run.CurrentSenseKey,
                run.FloorNumber, float.PositiveInfinity, r,
                PathGuardianArenaRadius(room)), rng, forcedRect, key,
                clearFloorLoot: false, clearCombatants: false);
        }
        else
        {
            bool midpoint = run.BossTier == PathFloorBossTier.Midpoint;
            string bossKey = midpoint ? run.CurrentSense.MidBoss : run.CurrentSense.FinalBoss;
            if (!BossCatalog.Shared.TryGet(bossKey, out var definition) || definition is null)
                throw new InvalidOperationException($"Boss '{bossKey}' is not registered.");
            SpawnBoss((x, y, r) => definition.Factory(x, y, Battleground, AwarenessRange, r),
                rng, forcedRect, bossKey, clearFloorLoot: false, clearCombatants: false);
        }
        if (run.FloorNumber is 5 or 10)
        {
            StepPlayerBackFrom(
                room.WorldCenter,
                run.CurrentSenseKey == "sound"
                    && run.BossTier == PathFloorBossTier.Finale
                    ? Simulation.TileSize * 9.6f
                    : Simulation.TileSize * 2.5f);
        }
    }

    public static bool RollFragmentDrop(Random? rng = null)
    {
        rng ??= Random.Shared;
        return rng.NextDouble() < FragmentDropChance;
    }

    public int PlayerLevelCap => PathRun is null || PathRun.IsSecretDungeon
        ? Progression.MaxLevel
        : Progression.DungeonMaxLevel;

    public bool CanPurchaseLevelUp =>
        State.CurrentLevel < PlayerLevelCap
        && State.ExpCount >= State.ExpNeededForNextLevel;

    /// <summary>Consumes one threshold and queues exactly one card draft.</summary>
    public bool TryPurchaseLevelUp()
    {
        // A purchased or encounter-granted draft is already paid for. This
        // path only reopens its selection screen; it must not consume XP or
        // enqueue another level.
        if (State.PendingLevelUps > 0)
            return true;
        if (!CanPurchaseLevelUp)
            return false;
        State.ExpCount -= State.ExpNeededForNextLevel;
        AdvanceLevel();
        GameProfile.IncrementQuest("levels_gained", state: State);
        return true;
    }

    /// <summary>
    /// Grants `count` levels outright: bumps CurrentLevel/PendingLevelUps and
    /// scales the next threshold the same way a purchased level-up does, but
    /// without touching ExpCount -- used by Golden Flame/The Void's instant
    /// XP-tier grants (see ExpForPlayer) alongside the manual purchase path
    /// above. Stops at PlayerLevelCap. Each level restores Golden Flame's
    /// chunks via FillHealthForMilestone, same as a purchased level-up.
    /// </summary>
    private void AdvanceLevel(int count = 1)
    {
        for (int index = 0; index < count && State.CurrentLevel < PlayerLevelCap; index++)
        {
            State.CurrentLevel += 1;
            State.PendingLevelUps += 1;
            State.ExpNeededForNextLevel *= State.LevelScaleIncreaseFunction;
            State.FillHealthForMilestone();
        }
    }

    /// <summary>Dev/testing hotkey. Ported from character.py's debugForceLevelUp().</summary>
    public void DebugForceLevelUp(Random? rng = null) =>
        State.ExperienceList.Add(new ExperienceBubble(Player.WorldX, Player.WorldY, State.ExpNeededForNextLevel, 1, rng));

    private static readonly Keys[] BossDebugPhaseKeys =
        { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9 };

    /// <summary>
    /// Dev/testing hotkeys. Ported from character.py's handlingBossDebugControls().
    /// `boss.debug_set_phase`/`hasattr(boss, "runeCannonCooldown")` were duck-typed
    /// across every boss type in Python. Here the controls are explicitly
    /// dispatched to Aphantasia, Beaudis, Dissonance, guardians, or the shared
    /// PathChaseBoss family;
    /// the "C" rune-cannon hotkey remains Dissonance-specific.
    ///
    /// Gated behind BossDebugInvincible (the "Y" dev-toggle, see RunState's
    /// "Hidden debug hotkey state" doc comment) -- unlike Python, these raw
    /// 1-9/R/L/F/C key checks never went through Keybinds, so without this
    /// gate they fired on every real bossfight regardless of bindings,
    /// silently resetting the boss's phase.
    /// </summary>
    public void HandleBossDebugControls(IReadOnlySet<Keys> keysPressed)
    {
        if (!State.BossDebugInvincible)
            return;
        if (State.ActiveBoss is Aphantasia aphantasia)
        {
            for (int index = 0; index < 4; index++)
            {
                if (keysPressed.Contains(BossDebugPhaseKeys[index]))
                {
                    aphantasia.DebugSetPhase(index + 1);
                    State.EnemyProjectileHolster.Clear();
                    return;
                }
            }
            if (keysPressed.Contains(Keys.R))
            {
                aphantasia.DebugSetPhase(aphantasia.Phase);
                State.EnemyProjectileHolster.Clear();
            }
            if (keysPressed.Contains(Keys.C))
            {
                aphantasia.DebugStartSurvival();
                State.EnemyProjectileHolster.Clear();
            }
            if (keysPressed.Contains(Keys.F))
            {
                aphantasia.DebugStartFinale();
                State.EnemyProjectileHolster.Clear();
            }
        }
        else if (State.ActiveBoss is Beaudis beaudis)
        {
            for (int index = 0; index < BossDebugPhaseKeys.Length; index++)
            {
                if (keysPressed.Contains(BossDebugPhaseKeys[index]))
                {
                    beaudis.DebugSetPhase(index + 1);
                    State.EnemyProjectileHolster.Clear();
                    return;
                }
            }
            if (keysPressed.Contains(Keys.R))
            {
                beaudis.DebugSetPhase(beaudis.Phase);
                State.EnemyProjectileHolster.Clear();
            }
            if (keysPressed.Contains(Keys.L))
                beaudis.DebugPhaseLocked = !beaudis.DebugPhaseLocked;
            if (keysPressed.Contains(Keys.F) && !beaudis.IsStaggered)
            {
                beaudis.Stagger = beaudis.MaxStagger - beaudis.MinimumStaggerPerHit;
                beaudis.TakeDamage(1);
            }
            // Keys.C (rune-cannon cooldown reset) is Dissonance-only; no-op for Beaudis.
        }
        else if (State.ActiveBoss is Dissonance dissonance)
        {
            for (int index = 0; index < BossDebugPhaseKeys.Length; index++)
            {
                if (keysPressed.Contains(BossDebugPhaseKeys[index]))
                {
                    dissonance.DebugSetPhase(index + 1);
                    State.EnemyProjectileHolster.Clear();
                    return;
                }
            }
            if (keysPressed.Contains(Keys.R))
            {
                dissonance.DebugSetPhase(dissonance.Phase);
                State.EnemyProjectileHolster.Clear();
            }
            if (keysPressed.Contains(Keys.L))
                dissonance.DebugPhaseLocked = !dissonance.DebugPhaseLocked;
            if (keysPressed.Contains(Keys.F) && !dissonance.IsStaggered)
            {
                dissonance.Stagger = dissonance.MaxStagger - dissonance.MinimumStaggerPerHit;
                dissonance.TakeDamage(1);
            }
            if (keysPressed.Contains(Keys.C))
                dissonance.RuneCannonCooldown = 0;
        }
        else if (State.ActiveBoss is PathGuardianBoss guardian)
        {
            for (int index = 0;
                index < Math.Min(3, BossDebugPhaseKeys.Length);
                index++)
            {
                if (keysPressed.Contains(BossDebugPhaseKeys[index]))
                {
                    guardian.DebugSetPhase(index + 1);
                    State.EnemyProjectileHolster.Clear();
                    return;
                }
            }
            if (keysPressed.Contains(Keys.R))
            {
                guardian.DebugSetPhase(guardian.Phase);
                State.EnemyProjectileHolster.Clear();
            }
            if (keysPressed.Contains(Keys.C))
            {
                guardian.DebugStartTrial();
                State.EnemyProjectileHolster.Clear();
            }
        }
        else if (State.ActiveBoss is PathChaseBoss pathBoss)
        {
            for (int index = 0; index < BossDebugPhaseKeys.Length; index++)
            {
                if (keysPressed.Contains(BossDebugPhaseKeys[index]))
                {
                    pathBoss.DebugSetPhase(index + 1);
                    State.EnemyProjectileHolster.Clear();
                    return;
                }
            }
            if (keysPressed.Contains(Keys.R))
            {
                pathBoss.DebugSetPhase(pathBoss.Phase);
                State.EnemyProjectileHolster.Clear();
            }
            if (keysPressed.Contains(Keys.L))
                pathBoss.DebugPhaseLocked = !pathBoss.DebugPhaseLocked;
        }
    }

    private static readonly RasterizerState LootCrateScissorRasterizerState = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    /// <summary>
    /// Ported from character.py's updateLootCrates(): clips crate drawing to
    /// the arena viewport so a crate can't paint over the HUD sidebar. The
    /// rect-intersects culling this used to rely on only skipped crates
    /// whose bounding box missed the viewport entirely -- one straddling the
    /// boundary still bled its far side into the sidebar, since culling
    /// isn't clipping. Opens/closes its own scissor-scoped SpriteBatch pass,
    /// same contract as DrawBackground/ArenaRenderer.Draw -- the caller must
    /// not have a batch already open when calling this.
    /// </summary>
    public void DrawLootCrates(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
    {
        var previousScissor = graphicsDevice.ScissorRectangle;
        graphicsDevice.ScissorRectangle = CombatViewport;
        spriteBatch.Begin(rasterizerState: LootCrateScissorRasterizerState);
        foreach (var crate in State.LootCrateList)
        {
            var screen = Camera.ApplyZoom(Camera.WorldToScreen(new Vector2(crate.WorldX, crate.WorldY), PlayerWorldCenter, ScreenShake));
            int zoomedSize = (int)(crate.Size * Camera.Zoom);
            var rect = new Rectangle((int)screen.X, (int)screen.Y, zoomedSize, zoomedSize);
            if (rect.Intersects(CombatViewport))
                crate.Draw(
                    spriteBatch, Camera, PlayerWorldCenter, ScreenShake,
                    (float)State.RunTimeSeconds);
        }
        spriteBatch.End();
        graphicsDevice.ScissorRectangle = previousScissor;
    }

    /// <summary>
    /// A stationary swirl at <see cref="ArenaCenterWorld"/>, visible exactly
    /// while <see cref="BossPortalOpen"/> -- walking into it (see
    /// <see cref="PlayerAtBossPortal"/>, checked by
    /// <see cref="HandleEnemyCreation"/>) is what actually starts the fight.
    /// No sprite asset: built from Primitives2D like the Soul's DPS
    /// dummy/stations, animated off State.RunTimeSeconds rather than a
    /// dedicated timer field. Same scissor-clipped-batch contract as
    /// <see cref="DrawLootCrates"/> -- call with no batch already open.
    /// </summary>
    public void DrawBossPortal(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
    {
        bool pathExit = PathRun?.ExitPortalOpen == true;
        bool pathGateway = PathMajorBossGatewayOpen;
        if (!BossPortalOpen && !pathExit && !pathGateway)
            return;

        Vector2 portalWorld = pathGateway
            ? PathRun!.Layout.BossRoom.WorldCenter
            : pathExit ? CurrentPathPortalWorld : ArenaCenterWorld;
        Color portalColor = pathExit || pathGateway ? PathRun!.CurrentSense.Accent : UiTheme.Purple;
        var screen = Camera.ApplyZoom(Camera.WorldToScreen(portalWorld, PlayerWorldCenter, ScreenShake));
        float radius = Simulation.TileSize * 1.1f * Camera.Zoom;
        var bounds = CombatViewport;
        if (!new Rectangle((int)(screen.X - radius), (int)(screen.Y - radius), (int)(radius * 2), (int)(radius * 2)).Intersects(bounds))
            return;

        var previousScissor = graphicsDevice.ScissorRectangle;
        graphicsDevice.ScissorRectangle = bounds;
        spriteBatch.Begin(rasterizerState: LootCrateScissorRasterizerState);

        float t = (float)State.RunTimeSeconds;
        float pulse = 1f + .06f * MathF.Sin(t * 2.2f);
        Primitives2D.FillCircle(spriteBatch, screen, radius * .78f * pulse, UiTheme.Ink);
        Primitives2D.CircleOutline(spriteBatch, screen, radius, portalColor, 3);
        for (int index = 0; index < 3; index++)
        {
            float speed = 1.4f + index * .55f;
            float phase = t * speed + index * (MathF.PI * 2f / 3f);
            float ringRadius = radius * (.55f + index * .18f);
            var arcRect = new Rectangle((int)(screen.X - ringRadius), (int)(screen.Y - ringRadius), (int)(ringRadius * 2), (int)(ringRadius * 2));
            Primitives2D.Arc(spriteBatch, arcRect, phase, phase + MathF.PI * .62f, portalColor, 2);
        }
        bool playerAtPortal = pathExit || pathGateway
            ? PlayerAtPathPortal(portalWorld)
            : PlayerAtBossPortal();
        if (playerAtPortal)
        {
            string keyLabel = PreferControllerPrompts
                ? "B"
                : Keybinds.LabelForKey(Keybinds.KeyFor("interact"));
            string action = pathExit ? "NEXT FLOOR" : pathGateway ? "COMMIT // FLOOR CONTENT WILL BE LOST" : "ENTER";
            UiTheme.DrawText(spriteBatch, $"{keyLabel}  //  {action}", 9, portalColor,
                new Vector2(screen.X, screen.Y + radius + 12), "midtop");
        }

        spriteBatch.End();
        graphicsDevice.ScissorRectangle = previousScissor;
    }

    private void DrawBossPortalInWorldPass(SpriteBatch spriteBatch)
    {
        bool pathExit = PathRun?.ExitPortalOpen == true;
        bool pathGateway = PathMajorBossGatewayOpen;
        if (!BossPortalOpen && !pathExit && !pathGateway)
            return;
        Vector2 portalWorld = pathGateway
            ? PathRun!.Layout.BossRoom.WorldCenter
            : pathExit
            ? CurrentPathPortalWorld
            : ArenaCenterWorld;
        Vector2 center = Camera.WorldToScreen(
            portalWorld, PlayerWorldCenter, ScreenShake);
        float radius = Simulation.TileSize * 1.1f;
        float time = (float)State.RunTimeSeconds;
        PathVisualProfile profile = SoulVisualLanguage.Path(
            pathExit || pathGateway ? PathRun!.CurrentSenseKey : GamePaths.Active().Key);
        Color interactable = SoulVisualLanguage.CueColor(
            VisualSemanticCue.Interactable, profile);
        float pulse = 1f + .06f * MathF.Sin(time * 2.2f);

        Primitives2D.FillCircle(spriteBatch, center,
            radius * .78f * pulse, UiTheme.Ink);
        if (pathExit)
        {
            SoulVisualLanguage.DrawRoomGlyph(
                spriteBatch,
                center,
                radius * .66f,
                PathRoomType.Boss,
                profile,
                time,
                .9f,
                -Camera.AngleRadians);
        }
        else
        {
            for (int index = 0; index < GamePaths.Paths.Count; index++)
            {
                GamePath path = GamePaths.Paths[index];
                float angle = -Camera.AngleRadians
                    + index * MathF.Tau / GamePaths.Paths.Count
                    + time * .16f;
                Vector2 lobe = center + new Vector2(
                    MathF.Cos(angle), MathF.Sin(angle)) * radius * .54f;
                Primitives2D.FillQuad(spriteBatch,
                    lobe + new Vector2(0, -radius * .18f),
                    lobe + new Vector2(radius * .15f, 0),
                    lobe + new Vector2(0, radius * .18f),
                    lobe - new Vector2(radius * .15f, 0),
                    path.Accent * .85f);
            }
        }
        Primitives2D.CircleOutline(spriteBatch, center,
            radius, interactable, 3);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)(center.X - radius * .13f),
                (int)(center.Y - radius * .13f),
                Math.Max(3, (int)(radius * .26f)),
                Math.Max(3, (int)(radius * .26f))),
            UiTheme.Purple);
    }

    /// <summary>
    /// Adds a crate to State.LootCrateList, evicting the oldest non-nearby
    /// crate once MaxLootCrates is exceeded -- factored out of
    /// HandleDamagingEnemies' death-loot drop so DevConsole's /spawn command
    /// shares the exact same cap/eviction behavior instead of duplicating it.
    /// </summary>
    public void SpawnLootCrate(float worldX, float worldY, IEnumerable<ItemDrop> drops)
    {
        var crate = new LootCrate(worldX, worldY, drops);
        State.LootCrateList.Add(crate);
        if (State.LootCrateList.Count > MaxLootCrates)
        {
            var evictable = State.LootCrateList.FirstOrDefault(c => c != State.NearbyCrate);
            if (evictable is not null)
                State.LootCrateList.Remove(evictable);
        }
    }

    private static readonly IReadOnlyList<string> DropFanfareRarityOrder =
        new[] { "Common", "Rare", "Epic", "Legendary", "Mythical", "Unique" };

    /// <summary>
    /// Bit-themed rarity fanfare fired the instant a drop lands, not when the
    /// crate is later opened -- Common/Rare stay silent (the crate itself is
    /// tell enough), but Epic and up throw an immediate burst of pixel-shard
    /// debris that scales up in count/speed/lifetime through
    /// Legendary/Mythical/Unique, so a great pull reads as a moment before
    /// you've walked over to open anything. Reuses BitVfxSystem's existing
    /// pixel-debris primitive (see Entities/BitVfxSystem.cs) rather than a
    /// new sprite/particle type, matching the rest of the game's chunky,
    /// digital-artifact visual language -- and keeps this purely additive:
    /// no gameplay state, just accent-colored debris keyed off the best
    /// rarity among what just dropped.
    /// </summary>
    private void EmitDropFanfare(Vector2 worldPosition, IReadOnlyList<ItemDrop> drops)
    {
        if (drops.Count == 0)
            return;
        string best = drops
            .Select(drop => drop.Rarity)
            .OrderByDescending(rarity => DropFanfareRarityOrder.ToList().IndexOf(rarity))
            .First();
        (int count, float speed, float lifetime) tuning = best switch
        {
            "Epic" => (14, 2.4f, .55f),
            "Legendary" => (22, 3.1f, .75f),
            "Mythical" => (30, 3.8f, .95f),
            "Unique" => (42, 4.6f, 1.2f),
            _ => (0, 0f, 0f),
        };
        if (tuning.count == 0)
            return;
        Color accent = UiTheme.RarityColors.GetValueOrDefault(best, UiTheme.Gold);
        int seed = (int)(worldPosition.X * 7 + worldPosition.Y * 13 + tuning.count);
        _visualEffects.EmitBurst(
            worldPosition,
            accent,
            UiTheme.Cream,
            tuning.count,
            tuning.speed * Simulation.TileSize * .12f,
            tuning.lifetime,
            BitVfxLayer.World,
            seed,
            _visualDensity.Optional,
            gravity: -Simulation.TileSize * .01f,
            primitive: VfxPrimitive.Shard);
    }

    /// <summary>Ported from character.py's crateInteractionForPlayer(). The drag-in-progress guard is dropped (InformationSheet's drag UI is deferred).</summary>
    public void UpdateCrateInteraction()
    {
        if (InformationSheet.DragInProgress)
            return;
        var playerRect = Player.WorldRect(State);
        LootCrate? nearest = null;
        double? nearestDistance = null;
        foreach (var crate in State.LootCrateList)
        {
            if (crate.Items.Count == 0)
                continue;
            var auraRect = playerRect;
            auraRect.Inflate((int)(2 * (CrateInteractRadius + crate.Size)), (int)(2 * (CrateInteractRadius + crate.Size)));
            if (auraRect.Intersects(crate.WorldRect()))
            {
                double distance = Vector2.Distance(new Vector2(crate.WorldX, crate.WorldY), new Vector2(Player.WorldX, Player.WorldY));
                if (nearestDistance is null || distance < nearestDistance)
                {
                    nearest = crate;
                    nearestDistance = distance;
                }
            }
        }
        State.NearbyCrate = nearest;
    }

    // ----- Bounty (InformationSheet's objective panel) -----

    /// <summary>
    /// Ported from character.py's selectBountyTarget(): the highest-value
    /// live target or patrol, as a world-space bounty for
    /// InformationSheet.DrawSheet's objective panel (the bounty-arrow HUD
    /// overlay itself is a separate, still-deferred character.py function).
    /// `getattr(enemy, "storedExperience", 0)`/`getattr(enemy, "bossName", ...)`
    /// are dropped -- no current Enemy type sets either (both were always
    /// their default), so the C# ports read `ExpValue`/`Family` directly.
    /// </summary>
    public BountyInfo? SelectBountyTarget()
    {
        PathFogOfWar? fog = ActiveVisibilityFog;
        if (State.ActiveBoss is Enemy boss && !boss.IsDead()
            && (fog is null || fog.IsWorldAreaVisible(boss.WorldRect())))
        {
            return new BountyInfo(
                new Vector2(boss.WorldX + boss.Size / 2f, boss.WorldY + boss.Size / 2f),
                double.PositiveInfinity, boss.Family, boss);
        }

        double bestScore = double.NegativeInfinity;
        Vector2 bestWorld = default;
        string? bestLabel = null;
        object? bestTarget = null;
        _bountyEncounterIdScratch.Clear();
        foreach (var enemy in State.EnemyHolster)
        {
            if (enemy.IsDead()
                || (fog is not null && !fog.IsWorldAreaVisible(enemy.WorldRect())))
                continue;
            var encounter = enemy.Encounter;
            if (encounter is not null)
            {
                if (!_bountyEncounterIdScratch.Add(encounter.Id))
                    continue;
                int livingCount = 0;
                double worldX = 0;
                double worldY = 0;
                double reward = 0;
                double threat = 0;
                for (int index = 0; index < encounter.Members.Count; index++)
                {
                    Enemy member = encounter.Members[index];
                    if (member.IsDead()
                        || (fog is not null
                            && !fog.IsWorldAreaVisible(member.WorldRect())))
                    {
                        continue;
                    }
                    livingCount++;
                    worldX += member.WorldX + member.Size / 2f;
                    worldY += member.WorldY + member.Size / 2f;
                    reward += member.ExpValue;
                    threat += member.ThreatCost;
                }
                if (livingCount == 0)
                    continue;
                double score = reward + threat * 4;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWorld = new Vector2(
                        (float)(worldX / livingCount),
                        (float)(worldY / livingCount));
                    bestLabel = encounter.Key;
                    bestTarget = encounter;
                }
            }
            else
            {
                double eliteBonus = enemy.CombatRole == "elite" ? 500 : 0;
                double score = enemy.ExpValue + enemy.ThreatCost * 4 + eliteBonus;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWorld = new Vector2(
                        enemy.WorldX + enemy.Size / 2f,
                        enemy.WorldY + enemy.Size / 2f);
                    bestLabel = enemy.Family;
                    bestTarget = enemy;
                }
            }
        }
        return bestTarget is null
            ? null
            : new BountyInfo(
                bestWorld,
                bestScore,
                bestLabel!.Replace("_", " ").ToUpperInvariant(),
                bestTarget);
    }

    /// <summary>
    /// Ported from character.py's drawBountyIndicator() + its
    /// _bounty_arrow_geometry helper. Calls SelectBountyTarget() directly
    /// each time rather than reusing a cached value -- DrawInformationSheet
    /// already calls it fresh every frame too (no caching anywhere else in
    /// this port for this same lookup), so this matches existing precedent
    /// rather than reproducing Python's stale-cache-then-recompute quirk.
    /// </summary>
    public void DrawBountyIndicator(SpriteBatch spriteBatch)
    {
        DrawBountyIndicator(spriteBatch, SelectBountyTarget());
    }

    /// <summary>
    /// Developer-only arrangement used to inspect procedural silhouettes and
    /// ordinary actor poses without needing to wait for encounter RNG.
    /// </summary>
    public int DebugSpawnVfxGallery(
        string? pathKey = null,
        string tier = "easy")
    {
        pathKey = SoulVisualLanguage.Paths.ContainsKey(pathKey ?? "")
            ? pathKey!
            : GamePaths.Active().Key;
        tier = SoulVisualLanguage.EnemyTiers.Contains(tier)
            ? tier
            : "easy";
        _debugVisualGallery = true;
        _debugVisualGalleryPath = pathKey;
        _debugVisualGalleryTier = tier;
        string[] hostileShapes =
        {
            "wave", "tuning_fork", "chevron",
            "rivet", "chain_link", "slab",
            "eye", "needle", "lens",
            "ember", "spore", "cracked_core",
            "star", "crescent", "orbit_core",
        };
        string[] friendlyDesigns =
            Cosmetics.ProjectileDesigns.Select(design => design.Id).ToArray();
        Vector2 origin = PlayerWorldCenter
            + Camera.ScreenVectorToWorld(new Vector2(-360, -220));
        int spawned = 0;
        for (int index = 0; index < hostileShapes.Length; index++)
        {
            int column = index % 5;
            int row = index / 5;
            Vector2 position = origin + Camera.ScreenVectorToWorld(
                new Vector2(column * 78, row * 78));
            State.EnemyProjectileHolster.Add(new EnemyProjectile(
                position.X, position.Y, 0f, 0f, 0f, 24f,
                travelRange: float.PositiveInfinity,
                color: SoulVisualLanguage.Path(pathKey).Accent,
                shape: hostileShapes[index],
                lifetime: 120f,
                owner: "vfx_gallery",
                ignoreWalls: true)
            {
                ContentPath = pathKey,
            });
            spawned++;
        }

        for (int index = 0; index < friendlyDesigns.Length; index++)
        {
            int column = index % 5;
            int row = index / 5;
            Vector2 position = origin + Camera.ScreenVectorToWorld(
                new Vector2(column * 78, 270 + row * 70));
            State.BulletHolster.Add(new Bullet(
                position.X, position.Y, 0f, 0f, 99999f, 24f,
                State.BulletColor, 999, 0, false,
                State.BulletEdgeColor, friendlyDesigns[index]));
            spawned++;
        }

        int level = tier switch
        {
            "hard" => 16,
            "medium" => 8,
            _ => 2,
        };
        IReadOnlyList<EnemyDefinition> definitions =
            EnemyCatalog.Shared.Available(level, pathKey)
                .Where(definition =>
                    definition.ProgressionTier == tier
                    && !definition.GuaranteedOnly)
                .GroupBy(definition => definition.Family)
                .Select(group => group.First())
                .OrderBy(definition => definition.Family)
                .ToList();
        int enemyCount = definitions.Count;
        for (int index = 0; index < enemyCount; index++)
        {
            Vector2 requested = origin + Camera.ScreenVectorToWorld(
                new Vector2(430 + index % 4 * 78, index / 4 * 72));
            Rectangle safe = Battleground.FindNearestOpenRect(
                new Rectangle((int)requested.X, (int)requested.Y,
                    Simulation.TileSize, Simulation.TileSize));
            Enemy enemy = EnemyCatalog.Shared.Create(
                definitions[index].Key,
                safe.X,
                safe.Y,
                level,
                AwarenessRange,
                new Random(700 + index),
                Battleground);
            enemy.ContentPath = pathKey;
            enemy.Speed = 0;
            enemy.EngagementAllowed = false;
            State.EnemyHolster.Add(enemy);
            spawned++;
        }
        State.CurrEnemyCount = State.EnemyHolster.Count;
        return spawned;
    }

    public void DrawBountyIndicator(SpriteBatch spriteBatch, BountyInfo? bounty)
    {
        if (bounty is null)
            return;
        var targetScreen = Camera.ApplyZoom(Camera.WorldToScreen(bounty.World, PlayerWorldCenter, ScreenShake));
        Rectangle safe = HudSafeArea;
        // The marker is navigation for off-screen targets only -- once the target's
        // center enters the playable view, the enemy itself is the clearer cue.
        if (safe.Contains(targetScreen.ToPoint()))
            return;

        int topMargin = State.ActiveBoss is not null ? 112 : 44;
        var viewport = new Rectangle(34, topMargin, Math.Max(1, safe.Width - 68),
            Math.Max(1, safe.Bottom - topMargin - 24));
        var geometry = BountyArrowGeometry(Camera.Lock, targetScreen, viewport);
        if (geometry is null)
            return;
        var (points, tip, direction) = geometry.Value;

        var shadow = points.Select(p => p + new Vector2(4, 5)).ToArray();
        Primitives2D.FillPolygon(spriteBatch, shadow, UiTheme.Shadow);
        Primitives2D.FillPolygon(spriteBatch, points, UiTheme.Red);
        Primitives2D.PolygonOutline(spriteBatch, points, UiTheme.Ink, 4);
        // A compact inward label gives the marker meaning without covering the biome.
        var labelPosition = tip - direction * 52f;
        UiTheme.DrawText(spriteBatch, "BOUNTY", 9, UiTheme.Red, labelPosition, "center");
    }

    /// <summary>
    /// Same shape as <see cref="DrawBountyIndicator"/> (reusing
    /// <see cref="BountyArrowGeometry"/> directly) but pointed at the boss
    /// portal instead of the current bounty -- visible for as long as
    /// <see cref="BossPortalOpen"/> is, independent of whatever the bounty
    /// arrow is doing.
    /// </summary>
    public void DrawBossPortalIndicator(SpriteBatch spriteBatch)
    {
        bool pathExit = PathRun?.ExitPortalOpen == true;
        bool pathGateway = PathMajorBossGatewayOpen;
        if (!BossPortalOpen && !pathExit && !pathGateway)
            return;
        Vector2 portalWorld = pathGateway
            ? PathRun!.Layout.BossRoom.WorldCenter
            : pathExit ? CurrentPathPortalWorld : ArenaCenterWorld;
        Color portalColor = pathExit || pathGateway ? PathRun!.CurrentSense.Accent : UiTheme.Purple;
        var targetScreen = Camera.ApplyZoom(Camera.WorldToScreen(portalWorld, PlayerWorldCenter, ScreenShake));
        Rectangle safe = HudSafeArea;
        if (safe.Contains(targetScreen.ToPoint()))
        {
            bool nearby = pathExit || pathGateway
                ? PlayerAtPathPortal(portalWorld)
                : PlayerAtBossPortal();
            if (nearby)
            {
                string keyLabel = PreferControllerPrompts
                    ? "B"
                    : Keybinds.LabelForKey(Keybinds.KeyFor("interact"));
                UiTheme.DrawText(spriteBatch,
                    $"{keyLabel}  //  {(pathExit ? "NEXT FLOOR" : pathGateway ? "COMMIT" : "ENTER")}",
                    9, portalColor,
                    targetScreen + new Vector2(
                        0, Simulation.TileSize * 1.35f * Camera.Zoom),
                    "midtop");
            }
            return;
        }

        int topMargin = State.ActiveBoss is not null ? 112 : 44;
        var viewport = new Rectangle(34, topMargin, Math.Max(1, safe.Width - 68),
            Math.Max(1, safe.Bottom - topMargin - 24));
        var geometry = BountyArrowGeometry(Camera.Lock, targetScreen, viewport);
        if (geometry is null)
            return;
        var (points, tip, direction) = geometry.Value;

        var shadow = points.Select(p => p + new Vector2(4, 5)).ToArray();
        Primitives2D.FillPolygon(spriteBatch, shadow, UiTheme.Shadow);
        Primitives2D.FillPolygon(spriteBatch, points, portalColor);
        Primitives2D.PolygonOutline(spriteBatch, points, UiTheme.Ink, 4);
        var labelPosition = tip - direction * 52f;
        UiTheme.DrawText(spriteBatch,
            pathExit ? "NEXT FLOOR" : pathGateway ? "BOSS GATE" : "PORTAL",
            9, portalColor, labelPosition, "center");
    }

    /// <summary>
    /// Draws the combat-only overlays layered above the arena and below the
    /// sidebar. The aim reticle is drawn separately (see
    /// <see cref="DrawAimReticle"/>) and later in the frame -- it needs to sit
    /// above the configurable Tab details view, which otherwise paints over
    /// the reticle when a quick-view panel is centered within the arena, right
    /// where the reticle draws too.
    /// </summary>
    public void DrawCombatOverlays(SpriteBatch spriteBatch, Point mousePosition)
    {
        DrawPathMinimap(spriteBatch);
        DrawPathTitleBanner(spriteBatch);
        DrawBossHealthBar(spriteBatch);
        DrawLowHealthWarning(spriteBatch);
        DrawRunCompleteBanner(spriteBatch);
        DrawTutorialHint(spriteBatch);
        DrawDebugVisualGalleryOverlay(spriteBatch);
    }

    private void DrawDebugVisualGalleryOverlay(SpriteBatch spriteBatch)
    {
        if (!_debugVisualGallery)
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        var panel = new Rectangle(
            (int)(18 * scale), (int)(18 * scale),
            (int)(430 * scale), (int)(82 * scale));
        PathVisualProfile path =
            SoulVisualLanguage.Path(_debugVisualGalleryPath);
        UiTheme.DrawLivingPanel(
            spriteBatch, panel, _debugVisualGalleryPath,
            (float)State.RunTimeSeconds,
            UiTheme.Panel * .96f, path.Accent, shadow: 5);
        UiTheme.DrawText(spriteBatch,
            $"LIVING SOUL GALLERY // {_debugVisualGalleryPath.ToUpperInvariant()} // {_debugVisualGalleryTier.ToUpperInvariant()}",
            11 * scale, UiTheme.Text,
            new Vector2(panel.X + 12 * scale, panel.Y + 11 * scale));
        float glyphX = panel.X + 25 * scale;
        foreach (PathRoomType roomType in Enum.GetValues<PathRoomType>())
        {
            SoulVisualLanguage.DrawRoomGlyph(
                spriteBatch,
                new Vector2(glyphX, panel.Y + 53 * scale),
                10 * scale,
                roomType,
                path,
                (float)State.RunTimeSeconds,
                .8f);
            glyphX += 42 * scale;
        }
        string density =
            $"AMBIENCE {_visualDensity.Ambience:P0}  TRAILS {_visualDensity.Trails:P0}  DEBRIS {_visualDensity.Debris:P0}";
        UiTheme.DrawText(spriteBatch, density, 8 * scale, UiTheme.Muted,
            new Vector2(panel.Right - 10 * scale,
                panel.Bottom - 8 * scale), "bottomright");
    }

    /// <summary>
    /// Compact graph-and-footprint map for the larger generated floors.
    /// Unvisited rooms stay subdued; branches, the active lock, and the
    /// player's position remain readable without revealing enemy placement.
    /// </summary>
    private void DrawPathMinimap(SpriteBatch spriteBatch)
    {
        if (PathRun is null || _dungeonBossInstance is not null)
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        const int totalFloors = global::RotBoiRemastered.Systems.PathRun.TotalFloors;
        var panel = new Rectangle(
            (int)(14 * scale),
            (int)(14 * scale),
            (int)(224 * scale),
            (int)(144 * scale));
        UiTheme.DrawLivingPanel(
            spriteBatch, panel, PathRun.CurrentSenseKey,
            (float)State.RunTimeSeconds,
            UiTheme.Panel * .94f, PathRun.CurrentSense.Accent,
            shadow: 5);
        var elapsed = TimeSpan.FromSeconds(Math.Max(0, State.RunTimeSeconds));
        UiTheme.DrawText(spriteBatch,
            $"FLOOR {PathRun.FloorNumber:D2}/{totalFloors:D2}  //  {PathRun.SenseDisplayName.ToUpperInvariant()}",
            9 * scale, PathRun.CurrentSense.Accent,
            new Vector2(panel.X + 9 * scale, panel.Y + 7 * scale));
        UiTheme.DrawText(spriteBatch,
            $"{(PathRun.IsSecondAct ? "DESCENT II" : "DESCENT I")}  //  {PathRun.Layout.Style.ToString().ToUpperInvariant()}  //  {elapsed.Minutes:D2}:{elapsed.Seconds:D2}",
            7 * scale, UiTheme.Muted,
            new Vector2(panel.X + 9 * scale, panel.Y + 22 * scale));

        var mapArea = new Rectangle(
            panel.X + (int)(9 * scale),
            panel.Y + (int)(40 * scale),
            panel.Width - (int)(18 * scale),
            panel.Height - (int)(49 * scale));
        Vector2 MapPoint(Point tile) => new(
            mapArea.X + tile.X / (float)Math.Max(1, Battleground.Width) * mapArea.Width,
            mapArea.Y + tile.Y / (float)Math.Max(1, Battleground.Height) * mapArea.Height);

        foreach (var connection in PathRun.Layout.Connections)
        {
            if (connection.Hidden && !connection.IsRevealed)
                continue;
            if (connection.Route is not { Count: > 1 } route)
                continue;
            Color routeColor = PathRun.CurrentSense.Accent * .38f;
            int stride = Math.Max(1, route.Count / 18);
            Point previousTile = route[0];
            for (int index = stride; index < route.Count; index += stride)
            {
                Point nextTile = route[Math.Min(index, route.Count - 1)];
                if (PathFog is null
                    || (PathFog.IsExplored(previousTile.X, previousTile.Y)
                        && PathFog.IsExplored(nextTile.X, nextTile.Y)))
                {
                    Primitives2D.Line(spriteBatch, MapPoint(previousTile), MapPoint(nextTile),
                        routeColor, Math.Max(1, (int)scale));
                }
                previousTile = nextTile;
            }
            if (PathFog is null
                || (PathFog.IsExplored(previousTile.X, previousTile.Y)
                    && PathFog.IsExplored(route[^1].X, route[^1].Y)))
            {
                Primitives2D.Line(spriteBatch, MapPoint(previousTile), MapPoint(route[^1]),
                    routeColor, Math.Max(1, (int)scale));
            }
        }

        foreach (var room in PathRun.Layout.Rooms)
        {
            if (!room.IsRevealed)
                continue;
            if (PathFog is not null && !PathFog.AnyExplored(room.TileBounds))
                continue;
            Rectangle roomRect = PathMinimapRoomRect(room, Battleground, mapArea);
            bool activeRoom = room.IsCombatRoom && room.IsActivated && !room.IsCleared;
            Color color = activeRoom
                ? UiTheme.Red
                : room.IsCleared
                    ? UiTheme.Green
                    : room.Type == PathRoomType.Treasure
                        ? UiTheme.Gold
                        : room.Type == PathRoomType.Challenge
                            ? UiTheme.Purple
                            : room.Type == PathRoomType.Boss
                                ? PathRun.CurrentSense.Accent
                                : room.IsActivated
                                    ? UiTheme.Cream
                                    : UiTheme.Border;
            if (PathFog is not null && !PathFog.AnyVisible(room.TileBounds))
                color *= .58f;
            if (activeRoom)
            {
                float pulse = .72f + .28f * MathF.Sin(
                    (float)State.RunTimeSeconds * 5f + room.Id);
                color = Color.Lerp(color, UiTheme.Cream, pulse * .28f);
            }
            Primitives2D.FillRect(spriteBatch, roomRect, UiTheme.Ink * .9f);
            Primitives2D.RectOutline(spriteBatch, roomRect, color, room.IsMainPath ? 2 : 1);
            float glyphSize = Math.Max(2f,
                Math.Min(roomRect.Width, roomRect.Height) * .2f);
            SoulVisualLanguage.DrawRoomGlyph(
                spriteBatch,
                roomRect.Center.ToVector2(),
                glyphSize,
                room.Type,
                SoulVisualLanguage.Path(PathRun.CurrentSenseKey),
                (float)State.RunTimeSeconds + room.Id * .13f,
                activeRoom ? 1f : room.IsCleared ? .72f : .38f);
        }

        Point playerTile = new(
            (int)(PlayerWorldCenter.X / Battleground.TileSize),
            (int)(PlayerWorldCenter.Y / Battleground.TileSize));
        Vector2 player = MapPoint(playerTile);
        int playerMarker = 7 + (int)MathF.Round(
            (1f + MathF.Sin((float)State.RunTimeSeconds * 6f)) * .8f);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)player.X - playerMarker / 2,
                (int)player.Y - playerMarker / 2, playerMarker, playerMarker), UiTheme.Cream);
        Primitives2D.RectOutline(spriteBatch,
            new Rectangle((int)player.X - playerMarker / 2,
                (int)player.Y - playerMarker / 2, playerMarker, playerMarker), UiTheme.Ink, 1);
    }

    public static Rectangle PathMinimapRoomRect(
        PathRoom room, Battleground battleground, Rectangle mapArea)
    {
        float scaleX = mapArea.Width / (float)Math.Max(1, battleground.Width);
        float scaleY = mapArea.Height / (float)Math.Max(1, battleground.Height);
        return new Rectangle(
            mapArea.X + (int)(room.TileBounds.X * scaleX),
            mapArea.Y + (int)(room.TileBounds.Y * scaleY),
            Math.Max(4, (int)(room.TileBounds.Width * scaleX)),
            Math.Max(4, (int)(room.TileBounds.Height * scaleY)));
    }

    /// <summary>
    /// Lightweight, deterministic ambience anchored to generated rooms. The
    /// emitters are metadata rather than live entities, so rain, wind, stars,
    /// and ash add motion without touching simulation state or allocations.
    /// </summary>
    public void DrawPathAmbience(SpriteBatch spriteBatch)
    {
        if (PathRun is null)
        {
            DrawStandaloneSanctumAccents(spriteBatch);
            return;
        }

        float time = (float)State.RunTimeSeconds;
        float intensity = _visualDensity.Optional;
        RefreshRoomVisualEnergy(time);
        _arenaRenderer.DrawAnimatedFloorAccents(
            spriteBatch,
            Battleground,
            Camera,
            PlayerWorldCenter,
            ScreenShake,
            CombatViewport,
            time,
            intensity,
            _roomVisualEnergy);
        DrawRoomRoleGlyphs(spriteBatch, time);
        DrawRareRoomSpectacle(spriteBatch, time, intensity);
        if (intensity <= 0)
            return;

        Color accent = PathRun.CurrentSense.Accent;
        foreach (var emitter in Battleground.AmbientPathDecorations)
        {
            Vector2 emitterScreen = Camera.WorldToScreen(emitter.WorldPosition, PlayerWorldCenter, ScreenShake);
            if (emitterScreen.X < -100 || emitterScreen.X > ScreenWidth + 100
                || emitterScreen.Y < -100 || emitterScreen.Y > ScreenHeight + 100)
            {
                continue;
            }

            int authoredParticles = PathRun.IsSecondAct ? 6 : 4;
            int particles = Math.Max(1, (int)MathF.Ceiling(authoredParticles * (float)intensity));
            for (int index = 0; index < particles; index++)
            {
                float seed = emitter.Variant * 17.3f + emitter.RoomId * 9.7f + index * 23.1f;
                float phase = (time * (.28f + index * .025f) + seed) % 1f;
                float seamFade = VisualAnimation.SeamFade(phase);
                switch (emitter.Kind)
                {
                    case PathDecorationKind.DripEmitter:
                    {
                        Vector2 world = emitter.WorldPosition + new Vector2(
                            MathF.Sin(seed) * 44f * emitter.Scale,
                            (phase * 100f - 50f) * emitter.Scale);
                        Vector2 p = Camera.WorldToScreen(world, PlayerWorldCenter, ScreenShake);
                        Primitives2D.Line(spriteBatch, p, p + new Vector2(0, 8 * emitter.Scale),
                            accent * ((.42f + phase * .3f) * seamFade), 2);
                        if (phase > .92f)
                            Primitives2D.Line(spriteBatch, p + new Vector2(-5, 8),
                                p + new Vector2(5, 8), accent * (.45f * seamFade), 1);
                        break;
                    }
                    case PathDecorationKind.RippleEmitter:
                    {
                        float radius = (9 + phase * 38) * emitter.Scale;
                        var rect = new Rectangle(
                            (int)(emitterScreen.X - radius),
                            (int)(emitterScreen.Y - radius * .42f),
                            (int)(radius * 2),
                            Math.Max(3, (int)(radius * .84f)));
                        Primitives2D.EllipseOutline(spriteBatch, rect,
                            accent * (seamFade * .46f), 1, 24);
                        break;
                    }
                    case PathDecorationKind.WindEmitter:
                    {
                        float travel = (phase * 150f - 75f) * emitter.Scale;
                        Vector2 p = emitterScreen + new Vector2(travel, MathF.Sin(seed) * 34f);
                        Primitives2D.Line(spriteBatch, p,
                            p + new Vector2(22 * emitter.Scale, -5),
                            accent * (.32f * seamFade), 2);
                        Primitives2D.FillRect(spriteBatch,
                            new Rectangle((int)p.X + 22, (int)p.Y - 7, 4, 3),
                            accent * (.42f * seamFade));
                        break;
                    }
                    case PathDecorationKind.StarEmitter:
                    {
                        float angle = phase * MathF.Tau + seed;
                        float radius = (18 + index * 7) * emitter.Scale;
                        Vector2 p = emitterScreen + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * .52f);
                        int size = index % 3 == 0 ? 4 : 2;
                        Primitives2D.FillRect(spriteBatch, new Rectangle((int)p.X, (int)p.Y, size, size), accent * (.48f + .35f * MathF.Sin(phase * MathF.PI)));
                        break;
                    }
                    case PathDecorationKind.AshEmitter:
                    {
                        Vector2 p = emitterScreen + new Vector2(
                            MathF.Sin(seed + phase * 4f) * 55f * emitter.Scale,
                            (phase * 115f - 58f) * emitter.Scale);
                        Color ash = index % 4 == 0 ? accent : new Color(126, 112, 98);
                        Primitives2D.FillRect(spriteBatch,
                            new Rectangle((int)p.X, (int)p.Y, 3, 3),
                            ash * (.52f * seamFade));
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Standalone runs are the intact central sanctums of their selected
    /// Path. They use the same room-role language as composite floors without
    /// changing arena collision, safe pockets, or encounter timing.
    /// </summary>
    private void DrawStandaloneSanctumAccents(SpriteBatch spriteBatch)
    {
        string pathKey = CampaignActivitySense ?? GamePaths.Active().Key;
        PathVisualProfile profile = SoulVisualLanguage.Path(pathKey);
        VisualRenderContext context = CurrentVisualContext();
        Vector2 center = Camera.WorldToScreen(
            ArenaCenterWorld, PlayerWorldCenter, ScreenShake);
        float time = (float)State.RunTimeSeconds;
        bool bossActive = State.ActiveBoss is not null;
        PathRoomType role = bossActive || BossPortalOpen
            ? PathRoomType.Boss
            : PathRoomType.Start;
        float energy = bossActive ? 1f : .55f;
        float size = Simulation.TileSize * (bossActive ? 1.22f : .86f);

        SoulVisualLanguage.DrawRoomGlyph(
            spriteBatch, center, size, role, profile,
            time, energy, -Camera.AngleRadians);

        int scarTier = SoulVisualLanguage.ProgressionScarTier(
            context.Mastery, context.NewGamePlus, context.HardMode);
        int pips = Math.Min(scarTier, 6);
        for (int index = 0; index < pips; index++)
        {
            float angle = -Camera.AngleRadians
                + index * MathF.Tau / Math.Max(1, pips);
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 pip = center + direction * size * 1.32f;
            int pipSize = 3 + index % 2;
            Color color = context.HardMode && index == pips - 1
                ? UiTheme.Red
                : profile.Secondary;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle(
                    (int)pip.X - pipSize / 2,
                    (int)pip.Y - pipSize / 2,
                    pipSize, pipSize),
                color * .64f);
        }

        if (_visualDensity.Ambience <= 0)
            return;
        int motes = Math.Max(2,
            (int)MathF.Ceiling(8 * _visualDensity.Ambience));
        for (int index = 0; index < motes; index++)
        {
            float phase = (time * (.12f + index * .006f)
                + index * .137f) % 1f;
            float angle = -Camera.AngleRadians
                + index * MathF.Tau / motes
                + time * .07f;
            float radius = size * (1.7f + phase * .7f);
            Vector2 point = center + new Vector2(
                MathF.Cos(angle) * radius,
                MathF.Sin(angle) * radius * .62f);
            int moteSize = index % 3 == 0 ? 4 : 2;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle(
                    (int)point.X - moteSize / 2,
                    (int)point.Y - moteSize / 2,
                    moteSize, moteSize),
                (index % 4 == 0 ? UiTheme.Cream : profile.Accent)
                    * ((.18f + phase * .22f)
                        * VisualAnimation.SeamFade(phase)));
        }
    }

    private void DrawRoomRoleGlyphs(SpriteBatch spriteBatch, float time)
    {
        if (PathRun is null || _dungeonBossInstance is not null)
            return;
        PathVisualProfile profile =
            SoulVisualLanguage.Path(PathRun.CurrentSenseKey);
        Rectangle viewport = new(
            -120, -120,
            ScreenWidth + 240,
            ScreenHeight + 240);
        VisualRenderContext context = CurrentVisualContext();
        int scarTier = SoulVisualLanguage.ProgressionScarTier(
            context.Mastery, context.NewGamePlus, context.HardMode);
        foreach (PathConnection connection in PathRun.Layout.Connections)
        {
            if (!connection.Hidden || connection.IsRevealed
                || connection.ClueTile is not Point clue)
            {
                continue;
            }
            Vector2 world = new(
                (clue.X + .5f) * Simulation.TileSize,
                (clue.Y + .5f) * Simulation.TileSize);
            if (PathFog is not null && !PathFog.IsWorldAreaVisible(new Rectangle(
                    (int)world.X - Simulation.TileSize,
                    (int)world.Y - Simulation.TileSize,
                    Simulation.TileSize * 2,
                    Simulation.TileSize * 2)))
            {
                continue;
            }
            Vector2 screen = Camera.WorldToScreen(world, PlayerWorldCenter, ScreenShake);
            float pulse = .75f + .25f * MathF.Sin(time * 3.2f);
            float radius = Simulation.TileSize * (.30f + pulse * .08f);
            Color accent = PathRun.CurrentSense.Accent;
            Primitives2D.FillCircle(spriteBatch, screen, radius, UiTheme.Ink * .85f);
            Primitives2D.CircleOutline(spriteBatch, screen, radius, accent * (.7f + pulse * .3f), 3);
            Primitives2D.Line(spriteBatch,
                screen + new Vector2(-radius * .65f, 0),
                screen + new Vector2(radius * .65f, 0),
                accent,
                2);
            if (Vector2.DistanceSquared(PlayerWorldCenter, world)
                <= MathF.Pow(Simulation.TileSize * 2.1f, 2))
            {
                string label = connection.ClueKind switch
                {
                    PathSecretClueKind.PressurePlate => "RELEASE PRESSURE",
                    PathSecretClueKind.LensAlignment => "ALIGN LENS",
                    PathSecretClueKind.CleansingMark => "CLEANSE MARK",
                    PathSecretClueKind.TruthGlyph => "SPEAK TRUTH",
                    _ => "ANSWER ECHO",
                };
                string key = PreferControllerPrompts
                    ? "B"
                    : Keybinds.LabelForKey(Keybinds.KeyFor("interact"));
                UiTheme.DrawText(spriteBatch, $"{key} // {label}", 9, accent,
                    screen + new Vector2(0, radius + 8), "midtop");
            }
        }
        foreach (PathRoom room in PathRun.Layout.Rooms)
        {
            if (!room.IsRevealed)
                continue;
            Vector2 center = Camera.WorldToScreen(
                room.WorldCenter, PlayerWorldCenter, ScreenShake);
            if (!viewport.Contains(center.ToPoint()))
                continue;
            float energy = _roomVisualEnergy.GetValueOrDefault(room.Id, .15f);
            float size = room.Type switch
            {
                PathRoomType.Boss => Simulation.TileSize * 1.1f,
                PathRoomType.Treasure or PathRoomType.Challenge =>
                    Simulation.TileSize * .78f,
                _ => Simulation.TileSize * .56f,
            };
            SoulVisualLanguage.DrawRoomGlyph(
                spriteBatch,
                center,
                size,
                room.Type,
                profile,
                time + room.Id * .17f,
                energy * .52f,
                -Camera.AngleRadians);

            if (scarTier <= 0 || !room.IsCleared)
                continue;
            int pips = Math.Min(scarTier, 6);
            for (int index = 0; index < pips; index++)
            {
                float angle = -Camera.AngleRadians
                    + index * MathF.Tau / pips;
                Vector2 pip = center + new Vector2(
                    MathF.Cos(angle), MathF.Sin(angle)) * size * 1.22f;
                int pipSize = 2 + index % 2;
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)pip.X - pipSize / 2,
                        (int)pip.Y - pipSize / 2, pipSize, pipSize),
                    (context.HardMode && index == pips - 1
                        ? UiTheme.Red
                        : profile.Secondary) * .58f);
            }
        }
    }

    private void DrawRareRoomSpectacle(
        SpriteBatch spriteBatch,
        float time,
        float intensity)
    {
        if (PathRun is null || intensity <= 0)
            return;
        PathRoom? room = PathRun.Layout.RoomAt(PlayerWorldCenter);
        if (room is null || !room.IsActivated
            || (room.Id * 17 + room.Variant * 7 + PathRun.FloorNumber) % 4 != 0)
        {
            return;
        }

        float phase = (time + room.Id * 1.37f) % 8.5f;
        if (phase > 1.25f)
            return;
        float progress = phase / 1.25f;
        float alpha = MathF.Sin(progress * MathF.PI) * intensity;
        int count = Math.Max(2, (int)MathF.Ceiling(10 * intensity));
        Color accent = PathRun.CurrentSense.Accent * (alpha * .42f);
        Vector2 center = Camera.WorldToScreen(
            room.WorldCenter, PlayerWorldCenter, ScreenShake);
        Vector2 half = new(
            room.WorldBounds.Width * .42f,
            room.WorldBounds.Height * .34f);

        for (int index = 0; index < count; index++)
        {
            float lane = (index + .5f) / count;
            float seed = room.Id * 2.31f + index * 1.73f;
            Vector2 point = PathRun.CurrentSenseKey switch
            {
                "touch" => center + new Vector2(
                    (progress * 2f - 1f) * half.X,
                    MathF.Sin(seed) * half.Y),
                "sight" => center + new Vector2(
                    MathHelper.Lerp(-half.X, half.X, (progress + lane) % 1f),
                    MathHelper.Lerp(half.Y, -half.Y, lane)),
                "sound" => center + new Vector2(
                    MathHelper.Lerp(-half.X, half.X, lane),
                    MathF.Sin(progress * MathF.Tau * 2f + seed) * half.Y * .65f),
                "phantasia" => center + new Vector2(
                    MathHelper.Lerp(-half.X, half.X, lane),
                    MathHelper.Lerp(-half.Y, half.Y, (progress + lane * .33f) % 1f)),
                _ => center + new Vector2(
                    MathHelper.Lerp(-half.X, half.X, (progress + lane) % 1f),
                    MathF.Sin(seed + progress * 5f) * half.Y),
            };
            int size = index % 4 == 0 ? 5 : 3;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)point.X, (int)point.Y, size, size),
                index % 5 == 0 ? UiTheme.Cream * (alpha * .38f) : accent);
        }
    }

    private void RefreshRoomVisualEnergy(float time)
    {
        _roomVisualEnergy.Clear();
        if (PathRun is null)
            return;

        foreach (PathRoom room in PathRun.Layout.Rooms)
        {
            float energy;
            if (!room.IsActivated)
            {
                energy = .12f;
            }
            else if (!room.IsCleared)
            {
                energy = 1f;
            }
            else
            {
                energy = .42f;
                if (_roomClearedAt.TryGetValue(room.Id, out double clearedAt))
                {
                    float release = 1f - Math.Clamp(
                        (time - (float)clearedAt) / 1.25f, 0f, 1f);
                    energy += release * .58f;
                }
            }

            if (ReferenceEquals(room, PathRun.LastEnteredRoom))
            {
                float entered = Math.Clamp(
                    (time - (float)PathRun.RoomEnteredAtRunSeconds) / 1.1f,
                    0f,
                    1f);
                energy *= .2f + entered * .8f;
            }
            _roomVisualEnergy[room.Id] = energy;
        }
    }

    internal bool PersistentBossArenaActive =>
        State.ActiveBoss is IBossArenaOcclusion;

    /// <summary>
    /// Final world-space arena pass. It runs after depth scenery, projectiles,
    /// effects, lighting, and fog, and is keyed to ActiveBoss rather than the
    /// boss sprite's visibility. Nothing rendered earlier can leak through the
    /// shaped exterior or make the arena vanish when the body is culled.
    /// </summary>
    public void DrawBossArenaOcclusion(SpriteBatch spriteBatch)
    {
        if (State.ActiveBoss is not IBossArenaOcclusion arena)
            return;
        arena.DrawPersistentArena(
            spriteBatch,
            Camera,
            PlayerWorldCenter,
            ScreenShake,
            CombatLogicalViewport());
    }

    /// <summary>
    /// Floor-only boss mask, drawn immediately after the background and
    /// before every entity/projectile -- unlike <see cref="DrawBossArenaOcclusion"/>,
    /// which intentionally runs last so nothing can leak past the arena
    /// boundary. Currently only Aphantasia's end-of-fight void vortex uses
    /// this hook.
    /// </summary>
    public void DrawBossFloorOcclusion(SpriteBatch spriteBatch)
    {
        if (State.ActiveBoss is not IBossFloorOcclusion floor)
            return;
        floor.DrawFloorOcclusion(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
    }

    /// <summary>
    /// Covers unexplored tiles and dims explored tiles outside current line
    /// of sight. This runs after every world object (including loot/portals)
    /// and before the HUD, so discovery applies consistently without
    /// darkening interface panels.
    /// </summary>
    public void DrawPathFogOfWar(SpriteBatch spriteBatch)
    {
        if (!IsPathFogActive || PathFog is not { } fog)
            return;

        DrawFogOfWar(spriteBatch, fog);
    }

    /// <summary>Shared world-space fog renderer used by Path floors and The Mind.</summary>
    public void DrawFogOfWar(SpriteBatch spriteBatch, PathFogOfWar fog)
    {

        var displayViewport = CombatViewport;
        Rectangle logicalViewport = Camera.LogicalViewport(displayViewport);
        Vector2 corner0 = Camera.ScreenToWorld(
            new Vector2(logicalViewport.Left, logicalViewport.Top),
            PlayerWorldCenter, ScreenShake);
        Vector2 corner1 = Camera.ScreenToWorld(
            new Vector2(logicalViewport.Right, logicalViewport.Top),
            PlayerWorldCenter, ScreenShake);
        Vector2 corner2 = Camera.ScreenToWorld(
            new Vector2(logicalViewport.Right, logicalViewport.Bottom),
            PlayerWorldCenter, ScreenShake);
        Vector2 corner3 = Camera.ScreenToWorld(
            new Vector2(logicalViewport.Left, logicalViewport.Bottom),
            PlayerWorldCenter, ScreenShake);
        float minWorldX = Math.Min(Math.Min(corner0.X, corner1.X), Math.Min(corner2.X, corner3.X));
        float maxWorldX = Math.Max(Math.Max(corner0.X, corner1.X), Math.Max(corner2.X, corner3.X));
        float minWorldY = Math.Min(Math.Min(corner0.Y, corner1.Y), Math.Min(corner2.Y, corner3.Y));
        float maxWorldY = Math.Max(Math.Max(corner0.Y, corner1.Y), Math.Max(corner2.Y, corner3.Y));
        int left = Math.Clamp((int)MathF.Floor(minWorldX / Battleground.TileSize) - 2,
            0, Battleground.Width - 1);
        int right = Math.Clamp((int)MathF.Ceiling(maxWorldX / Battleground.TileSize) + 2,
            0, Battleground.Width - 1);
        int top = Math.Clamp((int)MathF.Floor(minWorldY / Battleground.TileSize) - 2,
            0, Battleground.Height - 1);
        int bottom = Math.Clamp((int)MathF.Ceiling(maxWorldY / Battleground.TileSize) + 2,
            0, Battleground.Height - 1);
        float rotation = -MathHelper.ToRadians(Camera.AngleDegrees);

        for (int y = top; y <= bottom; y++)
        {
            int x = left;
            while (x <= right)
            {
                if (fog.IsVisible(x, y))
                {
                    x++;
                    continue;
                }

                bool explored = fog.IsExplored(x, y);
                int runStart = x++;
                while (x <= right
                    && !fog.IsVisible(x, y)
                    && fog.IsExplored(x, y) == explored)
                {
                    x++;
                }
                Color fogColor = explored
                    ? new Color(4, 7, 13, 178)
                    : new Color(2, 3, 7, 250);
                Vector2 topLeft = Camera.WorldToScreen(
                    new Vector2(runStart * Battleground.TileSize, y * Battleground.TileSize),
                    PlayerWorldCenter, ScreenShake);
                Vector2 runSize = new(
                    (x - runStart) * Battleground.TileSize + 1f,
                    Battleground.TileSize + 1f);
                Primitives2D.FillRotatedRect(
                    spriteBatch, topLeft, runSize, rotation, fogColor);
            }

            for (x = left; x <= right; x++)
            {
                if (fog.IsVisible(x, y))
                    continue;
                if (!Battleground.IsRaisedAt(x, y))
                    continue;
                Color fogColor = fog.IsExplored(x, y)
                    ? new Color(4, 7, 13, 178)
                    : new Color(2, 3, 7, 250);
                ArenaRenderer.DrawWallOcclusionMask(
                    spriteBatch, Camera, PlayerWorldCenter, ScreenShake,
                    x, y, Battleground.WallHeight, fogColor);
            }
        }
    }

    private void DrawPathTitleBanner(SpriteBatch spriteBatch)
    {
        if (PathRun is null || !PathRun.TitleBannerVisible(State.RunTimeSeconds))
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(ScreenWidth * .72f, 780 * scale);
        var rect = new Rectangle((ScreenWidth - width) / 2,
            (int)(28 * scale), width, (int)(54 * scale));
        UiTheme.DrawLivingPanel(
            spriteBatch, rect, PathRun.CurrentSenseKey,
            (float)State.RunTimeSeconds,
            UiTheme.PanelRaised, PathRun.CurrentSense.Accent,
            shadow: 7);
        UiTheme.DrawText(spriteBatch, PathRun.TitleBanner, 24 * scale, UiTheme.Text,
            rect.Center.ToVector2(), "center");
    }

    public void DrawAimReticle(SpriteBatch spriteBatch, Point mousePosition)
    {
        if (mousePosition.X < 0 || mousePosition.X >= ScreenWidth
            || mousePosition.Y < 0 || mousePosition.Y >= ScreenHeight
            || FooterHud.Contains(mousePosition) || InformationSheet.DragInProgress)
            return;
        var center = mousePosition.ToVector2();
        Color color = State.AutoFire || InputState.MouseDown || InputState.ControllerFireHeld ? UiTheme.Cream : UiTheme.Text;
        Primitives2D.FillRect(spriteBatch, new Rectangle(mousePosition.X - 3, mousePosition.Y - 3, 6, 6), UiTheme.Ink);
        Primitives2D.RectOutline(spriteBatch, new Rectangle(mousePosition.X - 3, mousePosition.Y - 3, 6, 6), color, 1);
        const int gap = 7, length = 8;
        Primitives2D.Line(spriteBatch, center + new Vector2(-gap - length, 0), center + new Vector2(-gap, 0), color, 2);
        Primitives2D.Line(spriteBatch, center + new Vector2(gap, 0), center + new Vector2(gap + length, 0), color, 2);
        Primitives2D.Line(spriteBatch, center + new Vector2(0, -gap - length), center + new Vector2(0, -gap), color, 2);
        Primitives2D.Line(spriteBatch, center + new Vector2(0, gap), center + new Vector2(0, gap + length), color, 2);
        if (GameProfile.Profile.AimGuide)
        {
            var origin = Camera.Lock;
            var delta = center - origin;
            float distance = Math.Max(1f, delta.Length());
            Primitives2D.Line(spriteBatch, origin, origin + delta / distance * Math.Min(distance, Simulation.TileSize * 3f), UiTheme.Cream, 1);
        }
    }

    private void DrawBossHealthBar(SpriteBatch spriteBatch)
    {
        if (State.ActiveBoss is not Enemy boss
            || (boss is not Aphantasia && boss.Hp <= 0)
            || DeathSpectacleActive(boss)
            || (ActiveVisibilityFog is { } fog
                && !fog.IsWorldAreaVisible(boss.WorldRect())))
            return;
        var presentation = State.ActiveBoss switch
        {
            Aphantasia a => (Accent: a.PhaseAccent, Entrance: a.EntranceRemaining),
            Beaudis b => (Accent: b.PhaseAccent, Entrance: b.EntranceRemaining),
            Dissonance d => (Accent: d.PhaseAccent, Entrance: d.EntranceRemaining),
            PathChaseBoss p => (Accent: p.PhaseAccent, Entrance: p.EntranceRemaining),
            PathGuardianBoss g => (
                Accent: g.TrialActive ? g.SecondaryAccent : g.PhaseAccent,
                Entrance: g.EntranceRemaining),
            _ => (Accent: UiTheme.Red, Entrance: 0.0),
        };
        if (presentation.Entrance > 1.0)
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(ScreenWidth * .62f, 720 * scale);
        bool aphantasiaLayout = boss is Aphantasia;
        int panelHeight = (int)((aphantasiaLayout ? 74 : 58) * scale);
        var rect = new Rectangle((ScreenWidth - width) / 2, (int)(16 * scale), width, panelHeight);
        UiTheme.DrawLivingPanel(
            spriteBatch, rect,
            CampaignActivitySense ?? PathRun?.CurrentSenseKey ?? GamePaths.Active().Key,
            (float)State.RunTimeSeconds,
            UiTheme.PanelRaised, presentation.Accent, shadow: 6);
        string bossKey = _activeBossKey ?? BossKeyFor(boss) ?? boss.Family;
        string name = boss is Aphantasia aphantasia
            ? aphantasia.DisplayName.ToUpperInvariant()
            : CampaignActivity == Systems.CampaignActivity.Core
            && PathRun?.FloorNumber == global::RotBoiRemastered.Systems.PathRun.TotalFloors
            ? bossKey switch
            {
                "dissonance" => "DISSONANCE, CORE OF SOUND",
                "rot" => "ROT, CORE OF TOUCH",
                "malady" => "MALADY, CORE OF PHANTASIA",
                "ache" => "ACHE, LORD OF CHEMESTHESIS",
                "chronos" => "CHRONOS, EMPEROR OF SIGHT",
                _ => bossKey.Replace('_', ' ').ToUpperInvariant(),
            }
            : boss is PathGuardianBoss guardian
            ? guardian.BossDisplayName
            : bossKey
                .Replace('_', ' ').ToUpperInvariant();
        UiTheme.DrawText(spriteBatch, name, 20 * scale, UiTheme.Text, new Vector2(rect.X + 14 * scale, rect.Y + 8 * scale));
        if (boss is Aphantasia objectiveBoss)
        {
            UiTheme.DrawText(spriteBatch, objectiveBoss.ObjectiveText, 9 * scale,
                objectiveBoss.DamageWindowActive ? UiTheme.Cream : presentation.Accent,
                new Vector2(rect.X + 14 * scale, rect.Y + 32 * scale));
            if (objectiveBoss.PresentationSurvivalActive)
            {
                UiTheme.DrawText(spriteBatch, objectiveBoss.SequenceStageLabel, 9 * scale,
                    UiTheme.Muted,
                    new Vector2(rect.Right - 14 * scale, rect.Y + 32 * scale), "topright");
            }
        }
        int hpOffset = aphantasiaLayout ? 50 : 34;
        var hpRect = new Rectangle((int)(rect.X + 14 * scale), (int)(rect.Y + hpOffset * scale), (int)(rect.Width - 28 * scale), (int)(12 * scale));
        float progress = boss switch
        {
            Aphantasia value => Math.Clamp(
                (float)value.DisplayedHp / Math.Max(1, value.DisplayedMaxHp), 0f, 1f),
            PathGuardianBoss { TrialActive: true } trial => Math.Clamp(
                (float)(trial.TrialRemaining / Math.Max(.01, trial.TrialDuration)), 0f, 1f),
            _ => Math.Clamp((float)boss.Hp / Math.Max(1, boss.MaxHp), 0f, 1f),
        };
        Color healthAccent = boss is Aphantasia { DamageWindowActive: true }
            ? Color.Lerp(presentation.Accent, UiTheme.Cream,
                .5f + .5f * MathF.Sin((float)State.RunTimeSeconds * 9f))
            : presentation.Accent;
        UiTheme.DrawProgress(spriteBatch, hpRect, progress, healthAccent, 18);
    }

    private void DrawLowHealthWarning(SpriteBatch spriteBatch)
    {
        // No HP concept in The Void -- any hit is fatal, so a "getting low"
        // vignette would be permanently on and meaningless.
        if (State.VoidMode)
            return;
        // Golden Flame's three chunks don't map onto the normal 30%-of-max
        // threshold below (1/3 alone already clears it) -- key the vignette
        // off "down to the last chunk" instead.
        double ratio = State.GoldenFlameMode
            ? (State.GoldenFlameHitsRemaining <= 1 ? 0.0 : 1.0)
            : State.HealthPoints / Math.Max(1.0, State.MaxHealthPoints);
        if (ratio > .3)
            return;
        int alpha = Math.Clamp((int)(35 + (1 - ratio / .3) * 65), 0, 255);
        int border = Math.Max(8, (int)(22 * UiTheme.DisplayScale(spriteBatch)));
        var color = new Color(UiTheme.Red.R, UiTheme.Red.G, UiTheme.Red.B, (byte)alpha);
        Primitives2D.RectOutline(spriteBatch, CombatViewport, color, border);
    }

    private void DrawRunCompleteBanner(SpriteBatch spriteBatch)
    {
        if (!State.GameCompleted)
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(ScreenWidth * .58f, 680 * scale);
        var rect = new Rectangle((ScreenWidth - width) / 2, (int)(22 * scale), width, (int)(76 * scale));
        UiTheme.DrawLivingPanel(
            spriteBatch, rect,
            PathRun?.CurrentSenseKey ?? CampaignActivitySense ?? GamePaths.Active().Key,
            (float)State.RunTimeSeconds,
            UiTheme.PanelRaised, UiTheme.Cream,
            shadow: 7, composite: PathRun is not null);
        string headline = CampaignActivity == Systems.CampaignActivity.Aphantasia
            ? State.IsTrueHardMode
                ? "THE CORE OF THE VOID ENDED"
                : "APHANTASIA ENDED"
            : PathRun is not null
                ? "THE WOVEN PATH TRAVERSED"
                : $"{GamePaths.BossKey(false).ToUpperInvariant()} ENDED";
        const int totalFloors = global::RotBoiRemastered.Systems.PathRun.TotalFloors;
        string detail = CampaignActivity == Systems.CampaignActivity.Aphantasia
            ? "LEVEL 20 // FINAL CONVERGENCE COMPLETE"
            : PathRun is not null
                ? $"FLOOR {totalFloors:D2} // ALL SENSES COMPLETE"
                : "LEVEL 20 // RUN COMPLETE";
        UiTheme.DrawText(spriteBatch, headline, 24 * scale, UiTheme.Cream,
            new Vector2(rect.Center.X, rect.Y + 10 * scale), "midtop");
        UiTheme.DrawText(spriteBatch, detail, 11 * scale, UiTheme.Purple,
            new Vector2(rect.Center.X, rect.Bottom - 12 * scale), "midbottom");
        UiTheme.DrawText(spriteBatch,
            PreferControllerPrompts ? "A  VIEW RESULTS" : "ENTER  VIEW RESULTS",
            9 * scale, UiTheme.Text,
            new Vector2(rect.Center.X, rect.Bottom + 12 * scale), "midtop");
    }

    private void DrawTutorialHint(SpriteBatch spriteBatch)
    {
        if (!GameProfile.Profile.TutorialHints || State.RunTimeSeconds >= 42 || State.GameCompleted)
            return;
        string text = PreferControllerPrompts
            ? State.RunTimeSeconds switch
            {
                < 8 => "LEFT STICK MOVE  //  RIGHT STICK AIM AND FIRE  //  X TOGGLES AUTOFIRE",
                < 16 => "A DASHES IN YOUR MOVEMENT DIRECTION AND BRIEFLY AVOIDS DAMAGE",
                < 25 => "FOLLOW THE RED BOUNTY ARROW TO HIGH-VALUE PATROLS",
                < 34 => "B INTERACTS WITH PORTALS AND LOOT  //  VIEW OPENS DETAILS",
                _ => "VIEW OPENS DETAILS  //  START PAUSES AND OPENS COMFORT SETTINGS",
            }
            : State.RunTimeSeconds switch
            {
                < 8 => "WASD MOVE  //  MOUSE AIM  //  PRESS I FOR AUTOFIRE",
                < 16 => "SPACE DASHES IN YOUR MOVEMENT DIRECTION AND BRIEFLY AVOIDS DAMAGE",
                < 25 => "FOLLOW THE RED BOUNTY ARROW TO HIGH-VALUE PATROLS",
                < 34 => "Q / E ROTATE THE ARENA  //  MOVEMENT STAYS SCREEN-RELATIVE",
                _ => "TAB OPENS DETAILS  //  ESC PAUSES AND OPENS COMFORT SETTINGS",
            };
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(ScreenWidth * .72f, 760 * scale);
        var rect = new Rectangle((ScreenWidth - width) / 2,
            Math.Max((int)(18 * scale), HudSafeArea.Bottom - (int)(48 * scale)),
            width, (int)(38 * scale));
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Blue, shadow: 4);
        UiTheme.DrawText(spriteBatch, text, 9 * scale, UiTheme.Text, rect.Center.ToVector2(), "center");
    }

    /// <summary>
    /// Ported from character.py's _bounty_arrow_geometry: a short, fat arrow
    /// polygon clamped to the viewport edge, pointing at an off-screen
    /// target. Public/static (pure geometry, no rendering) so it's directly
    /// unit testable, matching this port's established pattern of promoting
    /// pure geometry helpers to public rather than reaching for
    /// `internal`+`InternalsVisibleTo`.
    /// </summary>
    public static (Vector2[] Points, Vector2 Tip, Vector2 Direction)? BountyArrowGeometry(Vector2 origin, Vector2 targetScreen, Rectangle viewport)
    {
        var delta = targetScreen - origin;
        float distance = delta.Length();
        if (distance < 1)
            return null;
        var direction = delta / distance;
        float edgeDistance = float.PositiveInfinity;
        if (direction.X > 0)
        {
            float candidate = (viewport.Right - origin.X) / direction.X;
            if (candidate > 0)
                edgeDistance = Math.Min(edgeDistance, candidate);
        }
        else if (direction.X < 0)
        {
            float candidate = (viewport.Left - origin.X) / direction.X;
            if (candidate > 0)
                edgeDistance = Math.Min(edgeDistance, candidate);
        }
        if (direction.Y > 0)
        {
            float candidate = (viewport.Bottom - origin.Y) / direction.Y;
            if (candidate > 0)
                edgeDistance = Math.Min(edgeDistance, candidate);
        }
        else if (direction.Y < 0)
        {
            float candidate = (viewport.Top - origin.Y) / direction.Y;
            if (candidate > 0)
                edgeDistance = Math.Min(edgeDistance, candidate);
        }
        if (float.IsPositiveInfinity(edgeDistance))
            return null;
        var tip = origin + direction * edgeDistance;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        const float length = 38, headLength = 17, shaftHalf = 6, headHalf = 13;
        var tail = tip - direction * length;
        var neck = tip - direction * headLength;
        var points = new[]
        {
            tail + perpendicular * shaftHalf, neck + perpendicular * shaftHalf, neck + perpendicular * headHalf,
            tip, neck - perpendicular * headHalf, neck - perpendicular * shaftHalf, tail - perpendicular * shaftHalf,
        };
        return (points, tip, direction);
    }

    public void DrawFooter(SpriteBatch spriteBatch, Point mousePosition)
    {
        FooterHud.Draw(spriteBatch, State, mousePosition, PathRun,
            PreferControllerPrompts, InformationSheet.DraggingItem);
        InformationSheet.ConfigureLiveLootLayout(FooterHud.EquipmentSlotRects,
            FooterHud.QuickLootSlotRects, FooterHud.StashSlotRects);
    }

    public FooterAction HandleFooterAction(Point mousePosition, bool mousePressed) =>
        FooterHud.HandleInput(State, mousePosition, mousePressed);

    public bool HandleQuickLootInput(Point mousePosition, bool mouseDown, bool mousePressed)
    {
        InformationSheet.HandleLiveLootDrag(State, PlayerWorldCenter,
            mousePosition, mouseDown, mousePressed);
        QuickLootCommand? command = FooterHud.HandleQuickLootController(State);
        return command is not null
            && InformationSheet.QuickEquipLoot(State, command.LootIndex, command.EquipmentKey);
    }

    /// <summary>
    /// The compact stash panel (see InformationSheet.DrawDossier) reads
    /// equipment from the footer bar rather than drawing its own slots, so
    /// the footer's live rects need to be fed in first -- same
    /// ConfigureLiveLootLayout plumbing DrawFooter already uses, just with
    /// no quick-loot/quick-stash rects (this screen supplies its own stash
    /// rects when it draws).
    /// </summary>
    public void DrawDossier(SpriteBatch spriteBatch, Point mousePosition, float revealT) =>
        InformationSheet.DrawDossier(spriteBatch, State, mousePosition, revealT, Expedition);

    public DossierAction HandleDossierAction(IReadOnlySet<Keys> keysPressed) =>
        InformationSheet.HandleDossierAction(keysPressed);

    public void ScrollDossier(int delta) => InformationSheet.ScrollDossier(delta);

    /// <summary>
    /// The stash strip's number-key shortcut (see RotBoiGame.UpdateGameRun
    /// and Keybinds' stash_swap_1..8): instantly trades the item in stash
    /// slot <paramref name="index"/> with whatever's currently equipped in
    /// its slot type, so pressing the same key again swaps them straight
    /// back. A no-op if that stash slot is empty (there's no type to swap
    /// against) or the index is out of range. Accessories have two
    /// equipment slots, not one -- prefers whichever is empty, same
    /// fallback InformationSheet.HandleLoadoutNavigation's crate quick-equip
    /// already uses, so the two feel consistent.
    /// </summary>
    public bool SwapStashSlotWithEquipment(int index)
    {
        if (index < 0 || index >= InformationSheet.InventorySlotCount)
            return false;
        ItemDrop? stashItem = State.Inventory[index];
        if (stashItem is null)
            return false;
        string key = stashItem.SlotType == "accessory"
            ? (State.Equipment["accessory_1"] is null ? "accessory_1" : "accessory_2")
            : stashItem.SlotType;
        (State.Inventory[index], State.Equipment[key]) = (State.Equipment[key], State.Inventory[index]);
        State.CombinePlayerStats();
        return true;
    }

    public bool HandleLoadoutNavigation(IReadOnlySet<Keys> keysPressed,
        IReadOnlyList<Rectangle>? vaultSlotRects = null, bool dossier = false) =>
        InformationSheet.HandleLoadoutNavigation(State, PlayerWorldCenter,
            keysPressed, vaultSlotRects, dossier);

    public void DrawSoulFooter(SpriteBatch spriteBatch, Point mousePosition, float animationTime) =>
        FooterHud.DrawSoul(spriteBatch, State, mousePosition, animationTime);

    public void DrawSoulLoadoutPanel(SpriteBatch spriteBatch, Rectangle panel,
        Point mousePosition, float animationTime) =>
        InformationSheet.DrawSoulLoadoutPanel(spriteBatch, State, panel,
            mousePosition, animationTime);

    public void RegisterVaultFocus(IReadOnlyList<Rectangle> slotRects) =>
        InformationSheet.RegisterVaultFocus(slotRects);

    public void BeginLoadoutFocus() => InformationSheet.BeginLoadoutFocus();

    public bool IsLoadoutFocused(string id) =>
        InformationSheet.IsLoadoutFocused(id);

    /// <summary>Resolve mouse transfers after the Soul loadout and Vault rects are drawn.</summary>
    public void HandleCarriedLoadoutDrag(Point mousePosition, bool mouseDown, bool mousePressed, IReadOnlyList<Rectangle> vaultSlotRects) =>
        InformationSheet.HandleDrag(State, PlayerWorldCenter, mousePosition, mouseDown, mousePressed, vaultSlotRects, allowWorldDrop: false);

    /// <summary>
    /// Same as <see cref="HandleCarriedLoadoutDrag"/> but sourced from the
    /// Developer Armory's infinite catalog instead of the Vault -- dragging
    /// a card out places a copy into an empty equipment/stash slot and
    /// leaves the armory's own grid untouched (it never runs out; see
    /// InformationSheet's ArmoryDragSource).
    /// </summary>
    public void HandleArmoryLoadoutDrag(Point mousePosition, bool mouseDown, bool mousePressed, IReadOnlyList<Rectangle> armorySlotRects) =>
        InformationSheet.HandleDrag(State, PlayerWorldCenter, mousePosition, mouseDown, mousePressed,
            allowWorldDrop: false, armorySlotRects: armorySlotRects);

    // ----- Health -----

    /// <summary>Ported from character.py's recoverPlayerHealth(). RunState.RecoverHealth() already carries the full port -- this is just the per-frame call site.</summary>
    public void RecoverPlayerHealth() => State.RecoverHealth();

    private static double HostileDamageAfterDefense(double rawDamage, double defense)
    {
        rawDamage = Math.Max(0.0, rawDamage);
        if (rawDamage <= 0)
            return 0;
        return Math.Round(Math.Max(rawDamage - defense,
            Math.Min(rawDamage, Math.Max(HostileMinDamage, rawDamage * HostileDamageFloorRatio))));
    }

    /// <summary>
    /// Ported from character.py's hurtPlayer(). Returns true if the hit was
    /// fatal (caller should transition to the Results state) -- doesn't
    /// mutate game state itself, matching MenuAction's return-a-result
    /// contract.
    /// </summary>
    public bool HurtPlayer()
    {
        double timerStep = Simulation.GetTimerStep();
        State.PlayerInvulnerabilityTimer = Math.Max(0, State.PlayerInvulnerabilityTimer - timerStep);
        State.GracePeriod = Math.Max(0, State.GracePeriod - timerStep);
        if (State.BossDebugInvincible)
        {
            State.HealthPoints = State.MaxHealthPoints;
            return false;
        }
        if (State.PlayerInvulnerabilityTimer > 0 || State.GracePeriod > 0)
            return false;

        int playerHalf = (int)Math.Round(State.PlayerSize / 2f);
        var playerScreenRect = new Rectangle((int)Camera.Lock.X - playerHalf, (int)Camera.Lock.Y - playerHalf,
            (int)State.PlayerSize, (int)State.PlayerSize);
        var playerWorldRect = Player.WorldRect(State);
        bool casualMode = GameProfile.Profile.CasualMode;

        foreach (var projectile in State.EnemyProjectileHolster)
        {
            if (!projectile.Collides(playerWorldRect))
                continue;
            if (projectile.BeliefGain != 0 || projectile.ClarityGain != 0)
            {
                State.DreamState.AlterBelief(projectile.BeliefGain - projectile.ClarityGain,
                    falseRule: projectile.BeliefGain >= 1.0, truth: projectile.ClarityGain > 0);
            }
            if (projectile.Affliction is not null)
            {
                State.BossAfflictions.Apply(projectile.Affliction, projectile.AfflictionDuration,
                    projectile.AfflictionStrength, projectile.Exposure, projectile.AfflictionSource);
            }
            if (!projectile.PersistentHazard)
                projectile.RemFlag = true;
            double trueDamage = HostileDamageAfterDefense(
                NewGamePlus.ScaleEnemyDamage(projectile.Damage, State.NewGamePlusLevel), State.Defense);
            if (casualMode)
                trueDamage = Math.Round(trueDamage * .8);
            State.DamageTextList.Add(new DamageText(Player.WorldX, Player.WorldY, UiTheme.Red, trueDamage, Simulation.TileSize, Simulation.FrameRate));
            int healthBeforeHit = State.HealthPoints;
            bool fatal = ApplyPlayerHit(trueDamage);
            _visualEffects.Emit(
                "impact",
                PlayerWorldCenter,
                UiTheme.Red,
                projectile.Color,
                (int)(projectile.WorldX * 17 + projectile.WorldY * 31),
                _visualDensity.Optional);
            _bossTelemetry?.RecordDamage(State.VoidMode || State.GoldenFlameMode ? trueDamage : healthBeforeHit - State.HealthPoints);
            State.PlayerInvulnerabilityTimer = State.PlayerInvulnerabilityMax;
            return fatal ? FinalizeDefeat() : false;
        }

        foreach (var enemy in State.EnemyHolster)
        {
            if (ReferenceEquals(enemy, State.ActiveBoss) && DeathSpectacleActive(enemy))
                continue;
            var hitboxes = enemy.GetScreenHitboxes(Camera, PlayerWorldCenter, ScreenShake);
            Rectangle? collidedHitbox = null;
            for (int index = 0; index < hitboxes.Count; index++)
            {
                Rectangle hitbox = hitboxes[index].Rect;
                if (playerScreenRect.Intersects(hitbox))
                {
                    collidedHitbox = hitbox;
                    break;
                }
            }
            if (!collidedHitbox.HasValue)
                continue;

            var hitbox2 = collidedHitbox.Value;
            double trueDamage = HostileDamageAfterDefense(
                NewGamePlus.ScaleEnemyDamage(enemy.Damage, State.NewGamePlusLevel), State.Defense);
            if (casualMode)
                trueDamage = Math.Round(trueDamage * .8);
            State.DamageTextList.Add(new DamageText(Player.WorldX, Player.WorldY, UiTheme.Red, trueDamage, Simulation.TileSize, Simulation.FrameRate));
            int healthBeforeHit = State.HealthPoints;
            bool fatal = ApplyPlayerHit(trueDamage);
            _visualEffects.Emit(
                "impact",
                PlayerWorldCenter,
                UiTheme.Red,
                enemy.Color,
                (int)(enemy.WorldX * 23 + enemy.WorldY * 13),
                _visualDensity.Optional);
            _bossTelemetry?.RecordDamage(State.VoidMode || State.GoldenFlameMode ? trueDamage : healthBeforeHit - State.HealthPoints);
            State.PlayerInvulnerabilityTimer = State.PlayerInvulnerabilityMax;

            float deltaX = hitbox2.Center.X - playerScreenRect.Center.X, deltaY = hitbox2.Center.Y - playerScreenRect.Center.Y;
            float distance = Math.Max(1f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
            var knockback = Camera.ScreenVectorToWorld(new Vector2(
                deltaX / distance * Simulation.TileSize * 0.8f, deltaY / distance * Simulation.TileSize * 0.8f));
            enemy.ApplyKnockback(knockback.X, knockback.Y, Battleground);

            return fatal ? FinalizeDefeat() : false;
        }
        return false;
    }

    /// <summary>
    /// Applies one landed hit's death math and returns whether it was fatal.
    /// The Void takes priority over Golden Flame when both are lit (see
    /// RunState.VoidMode/GoldenFlameMode): any hit is instantly fatal in The
    /// Void, regardless of trueDamage/defense; Golden Flame instead spends
    /// one of three chunks (RunState.GoldenFlameHitsRemaining, restored by
    /// FillHealthForMilestone on level-up/boss milestones); otherwise this is
    /// the normal HP subtraction.
    /// </summary>
    private bool ApplyPlayerHit(double trueDamage)
    {
        if (State.VoidMode)
        {
            State.HealthPoints = 0;
            return true;
        }
        if (State.GoldenFlameMode)
            return State.SpendGoldenFlameHit();
        State.HealthPoints = Math.Max(0, State.HealthPoints - (int)trueDamage);
        return State.HealthPoints <= 0;
    }

    private bool FinalizeDefeat()
    {
        CompleteBossTelemetry(victory: false);
        State.RunOutcome = RunOutcomes.Defeated;
        GameProfile.RecordRun(State.CurrentLevel, State.NumOfEnemiesKilled);
        State.HighestLevel = Math.Max(State.HighestLevel, State.CurrentLevel);
        return true;
    }

    // ----- Leveling -----

    private LevelUpStatSnapshot BuildLevelUpStatSnapshot() => new()
    {
        CollectiveStats = State.Stats.ToDictionary(kv => kv.Key, kv => kv.Value.Base),
        CollectiveAddStats = State.Stats.ToDictionary(kv => kv.Key, IReadOnlyList<double> (kv) => kv.Value.Additive),
        CollectiveMultStats = State.Stats.ToDictionary(kv => kv.Key, IReadOnlyList<double> (kv) => kv.Value.Multiplicative),
        UpgradeTypeCounts = State.UpgradeTypeCounts,
        HealthPoints = State.HealthPoints,
        MaxHealthPoints = State.MaxHealthPoints,
        PendingLevelUps = State.PendingLevelUps,
        PathKey = PathRun?.CurrentSenseKey
            ?? CampaignActivitySense
            ?? GamePaths.Active().Key,
        PresentationTime = (float)State.RunTimeSeconds,
    };

    /// <summary>
    /// Ported from character.py's handleLevelingProcess(), split into a draw
    /// step and an input/decision step -- matches Menus.cs's Draw-populates-
    /// clickable-rects-then-Handle-reads-them shape, and keeps the
    /// record-upgrade/stat-stacking logic unit testable without a
    /// GraphicsDevice. Call DrawLevelingScreen once per frame before
    /// HandleLevelingInput, same order character.py called drawCards()
    /// before PlayerClicked().
    /// </summary>
    public void DrawLevelingScreen(SpriteBatch spriteBatch, Point mousePosition, bool mouseDown)
    {
        if (!State.NewRandoUps)
        {
            LevelingHandler.RandomizeLevelUp(State.UpgradeTypeCounts);
            State.NewRandoUps = true;
        }
        LevelingHandler.DrawCards(spriteBatch, BuildLevelUpStatSnapshot(), mousePosition, mouseDown);
    }

    public void DrawReforgeScreen(SpriteBatch spriteBatch, Point mousePosition, bool mouseDown) =>
        ReforgeHandler.Draw(spriteBatch, State, mousePosition, mouseDown);

    public ReforgeOutcome HandleReforgeInput(IReadOnlySet<Keys> keysPressed, Point mousePosition, bool mousePressed,
        Random? rng = null) => ReforgeHandler.HandleInput(keysPressed, mousePosition, mousePressed, State, rng);

    public LevelUpOutcome HandleLevelingInput(IReadOnlySet<Keys> keysPressed, Point mousePosition, bool mouseDown, Random? rng = null)
    {
        string decision = LevelingHandler.PlayerClicked(keysPressed, mousePosition, mouseDown, State.UpgradeTypeCounts, rng);
        if (decision == "none")
            return LevelUpOutcome.StillChoosing;

        var card = LevelingHandler.SelectedCard!;
        State.RecordUpgrade(card);
        foreach (var effect in card.Effects)
        {
            double modifier = Upgrades.EffectModifier(card.Rarity, effect);
            if (effect.MathType == "additive")
                State.Stats[effect.Stat].Additive.Add(modifier);
            else
                State.Stats[effect.Stat].Multiplicative.Add(modifier);
        }
        State.CombinePlayerStats();
        if (State.HardMode || State.GoldenFlameMode || State.VoidMode)
            State.FillHealthForMilestone();
        State.NewRandoUps = false;
        State.PendingLevelUps = Math.Max(0, State.PendingLevelUps - 1);
        if (State.PendingLevelUps > 0)
            return LevelUpOutcome.ContinueLeveling;
        State.GracePeriod = Simulation.FrameRate * 2;
        return LevelUpOutcome.ReturnToGame;
    }
}
