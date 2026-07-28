using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
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
    /// <summary>
    /// Emergency safety ceiling, not a pattern-density tool. Boss patterns
    /// are authored to reach the arena boundary and expire naturally before
    /// this limit; exceeding it means old bullets are being truncated.
    /// </summary>
    public const int MaxBossProjectiles = 150;
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
    public PathRun? PathRun { get; private set; }
    public PathFogOfWar? PathFog { get; private set; }
    public bool IsPathMode => PathRun is not null;
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }
    public Vector2 ScreenShake { get; set; } = Vector2.Zero;

    // Survives ResetAll -- it re-bakes lazily against the new Battleground
    // reference on the next DrawBackground call, no explicit reset needed.
    private readonly ArenaRenderer _arenaRenderer = new();
    private string? _activeBossKey;
    private BossEncounterTelemetryTracker? _bossTelemetry;
    private bool _bossTelemetryDeathCueEmitted;
    private double _controllerPromptRemaining;

    public bool BossTelemetryActive => _bossTelemetry is not null;
    public bool PreferControllerPrompts => _controllerPromptRemaining > 0;

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
        Battleground = battleground;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Player = new Player(battleground.SpawnPosition.X, battleground.SpawnPosition.Y);
        LevelingHandler = new LevelingHandler(screenWidth, screenHeight, rng);
        ReforgeHandler = new ReforgeHandler(screenWidth, screenHeight);
        InformationSheet = new InformationSheet(screenWidth, screenHeight);
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, screenHeight / 2f);
        Camera.ConfigureViewport(screenWidth, screenHeight, GameProfile.Profile.CameraZoom, resetZoom: true);
        LoadCarriedItems();
    }

    /// <summary>Ported from character.py's resetAllStats() (the parts not already covered by RunState.Reset()).</summary>
    public void ResetAll(Battleground battleground, Random? rng = null)
    {
        PathRun = null;
        PathFog = null;
        State.Reset();
        Battleground = battleground;
        Player = new Player(battleground.SpawnPosition.X, battleground.SpawnPosition.Y);
        Camera.SetAngle(0);
        ScreenShake = Vector2.Zero;
        LevelingHandler = new LevelingHandler(ScreenWidth, ScreenHeight, rng);
        ReforgeHandler = new ReforgeHandler(ScreenWidth, ScreenHeight);
        InformationSheet = new InformationSheet(ScreenWidth, ScreenHeight);
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, ScreenHeight / 2f);
        Camera.ConfigureViewport(ScreenWidth, ScreenHeight, GameProfile.Profile.CameraZoom, resetZoom: true);
        _activeBossKey = null;
        _bossTelemetry = null;
        _bossTelemetryDeathCueEmitted = false;
        _controllerPromptRemaining = 0;
        LoadCarriedItems();
    }

    /// <summary>Starts a fresh ten-floor composite Path run.</summary>
    public void StartPathRun(Random? rng = null)
    {
        rng ??= Random.Shared;
        var pathRun = new PathRun(rng);
        GamePaths.SetActive(pathRun.CurrentSenseKey);
        PathRun = pathRun;
        State.Reset();
        // Per-sense NG+ selectors belong to the authored arena paths. The
        // composite mode owns its explicit second-act difficulty curve.
        State.SetNewGamePlusLevel(0);
        Battleground = pathRun.Layout.Battleground;
        Player = new Player(Battleground.SpawnPosition.X, Battleground.SpawnPosition.Y);
        PathFog = new PathFogOfWar(Battleground);
        PathFog.Update(PlayerWorldCenter);
        Camera.SetAngle(0);
        ScreenShake = Vector2.Zero;
        LevelingHandler = new LevelingHandler(ScreenWidth, ScreenHeight, rng);
        ReforgeHandler = new ReforgeHandler(ScreenWidth, ScreenHeight);
        InformationSheet = new InformationSheet(ScreenWidth, ScreenHeight);
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, ScreenHeight / 2f);
        Camera.ConfigureViewport(ScreenWidth, ScreenHeight, GameProfile.Profile.CameraZoom, resetZoom: true);
        _activeBossKey = null;
        _bossTelemetry = null;
        _bossTelemetryDeathCueEmitted = false;
        _controllerPromptRemaining = 0;
        State.CurrentStage = 1;
        LoadCarriedItems();
    }

    /// <summary>Restarts whichever run mode is currently represented by this session.</summary>
    public void RestartCurrentRun(Random? rng = null)
    {
        if (PathRun is not null)
            StartPathRun(rng);
        else
            ResetAll(GamePaths.ActivateSelected(), rng);
    }

    private void InstallNextPathFloor()
    {
        if (PathRun is null)
            return;
        GamePaths.SetActive(PathRun.CurrentSenseKey);
        Battleground = PathRun.Layout.Battleground;
        Player = new Player(Battleground.SpawnPosition.X, Battleground.SpawnPosition.Y);
        PathFog = new PathFogOfWar(Battleground);
        PathFog.Update(PlayerWorldCenter);
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
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, screenHeight / 2f);
        Camera.ConfigureViewport(screenWidth, screenHeight, GameProfile.Profile.CameraZoom);
    }

    /// <summary>
    /// Tab opens/closes the configured run-details view instead of toggling
    /// the sidebar's own width -- the sidebar is a single fixed width now, so
    /// there's no ArenaWidth change here to re-center the camera for
    /// (unlike the old HudMode toggle this replaced).
    /// </summary>
    public void ToggleTabDetails() => InformationSheet.ToggleTabDetails();

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
            new Rectangle(0, 0, InformationSheet.ArenaWidth, ScreenHeight));
    }

    public void DrawBackgroundFull(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
    {
        Camera.Lock = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
        _arenaRenderer.EnsureBaked(graphicsDevice, spriteBatch, Battleground);
        _arenaRenderer.Draw(spriteBatch, graphicsDevice, Camera, PlayerWorldCenter, ScreenShake,
            new Rectangle(0, 0, ScreenWidth, ScreenHeight));
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
        var obstacles = State.ActiveBoss is SinChemesthesisBoss chemicalBoss
            ? chemicalBoss.MovementObstacles()
            : null;
        Player.Move(State, Battleground, Camera, moveLeft, moveRight, moveUp, moveDown, dashPressed, obstacles, controllerMove);
        if (State.ActiveBoss is PathChaseBoss pathBoss)
        {
            var constrained = pathBoss.ConstrainPlayerPosition(Player.WorldX, Player.WorldY, (float)State.PlayerSize);
            Player.SetPosition(constrained.X, constrained.Y);
        }
        else if (State.ActiveBoss is Dissonance dissonance)
        {
            float playerX = Player.WorldX + (float)State.PlayerSize / 2f, playerY = Player.WorldY + (float)State.PlayerSize / 2f;
            float deltaX = playerX - dissonance.ArenaCenter.X, deltaY = playerY - dissonance.ArenaCenter.Y;
            float distance = Math.Max(1f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
            float limit = dissonance.ArenaRadius - (float)State.PlayerSize * .7f;
            if (distance > limit)
            {
                Player.SetPosition(
                    dissonance.ArenaCenter.X + deltaX / distance * limit - (float)State.PlayerSize / 2f,
                    dissonance.ArenaCenter.Y + deltaY / distance * limit - (float)State.PlayerSize / 2f);
            }
        }
        double traveled = Vector2.Distance(before, new Vector2(Player.WorldX, Player.WorldY));
        if (traveled >= 1)
            GameProfile.IncrementQuest("distance_traveled", (long)Math.Round(traveled));
        PathFog?.Update(PlayerWorldCenter);
    }

    public void DrawPlayer(SpriteBatch spriteBatch, float sizeScale = 1f) => Player.Draw(spriteBatch, State, Camera, sizeScale);

    /// <summary>Ported from character.py's handlingBulletCreation() for mouse and controller aiming.</summary>
    public void HandleBulletCreation(Vector2 mouseScreenPosition, bool mouseDown, bool dragInProgress, Random? rng = null, bool controllerFiring = false)
    {
        rng ??= Random.Shared;
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
            GameProfile.IncrementQuest("shots_fired", currProjectileCount);
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

    /// <summary>
    /// Moves the player straight down from the arena center by `distance`,
    /// snapped to the nearest open tile -- entering the portal used to leave
    /// the player exactly where they walked in (i.e. on top of the boss);
    /// this keeps them a step away instead. Shared by every boss spawn
    /// (Dissonance passes its own much larger distance to match its bigger
    /// ArenaRadius; every other boss uses a small generic step-back).
    /// </summary>
    private void StepPlayerBackFromArenaCenter(float distance)
    {
        var center = ArenaCenterWorld;
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

            Rectangle? forcedRect = null;
            if (bossKey == "dissonance")
            {
                float arenaX = ArenaCenterWorld.X, arenaY = ArenaCenterWorld.Y;
                float size = Simulation.TileSize * 1.9f;
                forcedRect = new Rectangle((int)(arenaX - size / 2f), (int)(arenaY - size / 2f), (int)size, (int)size);
                SpawnBoss((x, y, r) => definition.Factory(x, y, Battleground, AwarenessRange, r), rng, forcedRect, bossKey);
                StepPlayerBackFromArenaCenter(Simulation.TileSize * 9.6f);
            }
            else
            {
                SpawnBoss((x, y, r) => definition.Factory(x, y, Battleground, AwarenessRange, r), rng, bossKey: bossKey);
                StepPlayerBackFromArenaCenter(Simulation.TileSize * 2.5f);
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
        foreach (var (unlockLevel, key) in Progression.MinibossGates)
        {
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
        double currentThreat = State.EnemyHolster.Sum(e => e.ThreatCost);
        var pacing = Progression.EncounterPacing(State.CurrentLevel);
        var worldEncounters = State.EnemyHolster.Where(e => e.Encounter is not null).Select(e => e.Encounter!.Id).ToHashSet();
        if (worldEncounters.Count < pacing.MaxWorldEncounters && State.CurrEnemyCount < State.EnemyCap
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
                double groupThreat = group.Sum(e => e.ThreatCost);
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
        enemy is PathGuardianBoss or PathChaseBoss or Beaudis or Dissonance;

    /// <summary>
    /// Applies ordinary sense identity only to catalog enemies and adds. Boss
    /// classes keep their authored baseline, then receive the shared NG+/Path
    /// run curve exactly once.
    /// </summary>
    private void ApplyRunDifficulty(Enemy enemy)
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
        var playerCenter = new Vector2(Player.WorldX + (float)State.PlayerSize / 2f, Player.WorldY + (float)State.PlayerSize / 2f);
        double pressureUsed = 0.0;
        var encounters = new List<RuntimeEncounter>();
        var seen = new HashSet<int>();
        var ungrouped = new List<Enemy>();
        foreach (var enemy in State.EnemyHolster)
        {
            var encounter = enemy.Encounter;
            if (encounter is null)
                ungrouped.Add(enemy);
            else if (seen.Add(encounter.Id))
                encounters.Add(encounter);
        }

        encounters.Sort((a, b) =>
        {
            int engagedCompare = (a.State != "engaged").CompareTo(b.State != "engaged");
            return engagedCompare != 0 ? engagedCompare
                : a.DistanceTo(playerCenter.X, playerCenter.Y).CompareTo(b.DistanceTo(playerCenter.X, playerCenter.Y));
        });
        foreach (var encounter in encounters)
        {
            bool wantsPressure = encounter.State == "engaged" || encounter.DistanceTo(playerCenter.X, playerCenter.Y) <= encounter.ActivationRange;
            bool allowed = !wantsPressure || pressureUsed + encounter.ThreatCost <= State.EnemyThreatCap;
            encounter.Update(playerCenter.X, playerCenter.Y, Battleground, allowed);
            if (encounter.EngagementAllowed)
                pressureUsed += encounter.ThreatCost;
        }

        foreach (var enemy in ungrouped.OrderBy(e => Vector2.Distance(new Vector2(e.WorldX + e.Size / 2f, e.WorldY + e.Size / 2f), playerCenter)))
        {
            double cost = enemy.ThreatCost;
            bool isBoss = ReferenceEquals(enemy, State.ActiveBoss);
            enemy.EngagementAllowed = isBoss || pressureUsed + cost <= State.EnemyThreatCap;
            if (enemy.EngagementAllowed)
                pressureUsed += cost;
        }

        var spawnedGroups = new List<(Enemy Owner, List<Enemy> Group, bool Atomic)>();
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = playerCenter.X, PlayerWorldY = playerCenter.Y, Battleground = Battleground,
            ProjectileSink = State.EnemyProjectileHolster, AllEnemies = State.EnemyHolster, ExperienceBubbles = State.ExperienceList,
            Camera = Camera, BossAfflictions = State.BossAfflictions, PlayerBuildSnapshot = State.BuildSnapshot(),
            PlayerBullets = State.BulletHolster, DreamState = State.DreamState,
        };
        foreach (var enemy in State.EnemyHolster)
        {
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
            var newProjectiles = State.EnemyProjectileHolster.Skip(projectileStart).ToList();
            if (UsesAuthoredBossBalance(enemy))
            {
                string pathKey = enemy.ContentPath ?? GamePaths.Active().Key;
                foreach (var projectile in newProjectiles)
                    projectile.ContentPath ??= pathKey;
            }
            else
            {
                GamePaths.TuneNewProjectiles(newProjectiles);
            }
            if (enemy.TransitionCleanupRequested)
            {
                if (enemy.TransitionCleanupOwner is not null)
                    State.EnemyProjectileHolster.RemoveAll(p => p.Owner == enemy.TransitionCleanupOwner);
                else
                    State.EnemyProjectileHolster.Clear();
                enemy.TransitionCleanupRequested = false;
            }
            if (enemy.SpawnedEnemies.Count > 0)
                spawnedGroups.Add((enemy, new List<Enemy>(enemy.SpawnedEnemies), enemy.AtomicSpawnGroup));
            enemy.SpawnedEnemies.Clear();
        }

        var rejectedAtomicOwners = new HashSet<Enemy>();
        foreach (var (owner, group, atomic) in spawnedGroups)
        {
            double currentThreat = State.EnemyHolster.Sum(e => e.ThreatCost);
            double groupThreat = group.Sum(e => e.ThreatCost);
            if (atomic && (State.EnemyHolster.Count + group.Count > State.EnemyCap || currentThreat + groupThreat > State.EnemyPopulationThreatCap))
            {
                rejectedAtomicOwners.Add(owner);
                continue;
            }
            foreach (var enemy in group)
            {
                ApplyRunDifficulty(enemy);
                currentThreat = State.EnemyHolster.Sum(e => e.ThreatCost);
                if (State.EnemyHolster.Count >= State.EnemyCap)
                    break;
                if (currentThreat + enemy.ThreatCost > State.EnemyPopulationThreatCap)
                    break;
                if (owner.Encounter is not null && enemy.Encounter is null)
                {
                    enemy.Encounter = owner.Encounter;
                    enemy.EncounterSlot = owner.Encounter.Members.Count;
                    enemy.CombatSide = enemy.EncounterSlot % 2 != 0 ? -1 : 1;
                    owner.Encounter.Members.Add(enemy);
                }
                State.EnemyHolster.Add(enemy);
            }
        }
        if (rejectedAtomicOwners.Count > 0)
            State.EnemyHolster.RemoveAll(e => rejectedAtomicOwners.Contains(e));
        State.CurrEnemyCount = State.EnemyHolster.Count;
        UpdateBossTelemetry();

        // Ported from Dissonance._update_visuals's `vH.screenShakeX/Y` global write --
        // computed here instead and assigned to this session's own ScreenShake, matching
        // this port's "explicit parameter over hidden global" convention (see Dissonance.cs).
        if (State.ActiveBoss is Dissonance dissonance)
            ScreenShake = dissonance.ComputeScreenShake(GameProfile.Profile.ScreenShake);
    }

    public void DrawEnemies(SpriteBatch spriteBatch)
    {
        var seen = new HashSet<int>();
        foreach (var enemy in State.EnemyHolster.Where(enemy =>
                     PathFog is null || PathFog.IsWorldAreaVisible(enemy.WorldRect())))
        {
            var encounter = enemy.Encounter;
            if (encounter is not null && seen.Add(encounter.Id))
                encounter.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
            enemy.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
        }
    }

    /// <summary>Ported from character.py's handlingEnemyProjectileUpdating(), including boss-arena containment and overflow trimming.</summary>
    public void UpdateEnemyProjectiles()
    {
        var spawnedProjectiles = new List<EnemyProjectile>();
        bool casualMode = GameProfile.Profile.CasualMode;
        (Vector2 Center, float Radius)? radialArena = State.ActiveBoss switch
        {
            Dissonance dissonance => (dissonance.ArenaCenter, dissonance.ArenaRadius),
            PathGuardianBoss guardian => (guardian.ArenaCenter, guardian.ArenaRadius),
            _ => null,
        };
        var pathArena = State.ActiveBoss as PathChaseBoss;
        bool bossDying = DeathSpectacleActive(State.ActiveBoss);
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
            projectile.Update(Battleground, casualMode);
            spawnedProjectiles.AddRange(projectile.SpawnedProjectiles);
            projectile.SpawnedProjectiles.Clear();
        }
        State.EnemyProjectileHolster.RemoveAll(p => p.RemFlag);
        GamePaths.TuneNewProjectiles(spawnedProjectiles);
        State.EnemyProjectileHolster.AddRange(spawnedProjectiles);
        if (State.ActiveBoss is not null && State.EnemyProjectileHolster.Count > MaxBossProjectiles)
            State.EnemyProjectileHolster.RemoveRange(0, State.EnemyProjectileHolster.Count - MaxBossProjectiles);
    }

    /// <summary>Persistent pools are ground hazards and render below every combat actor.</summary>
    public void DrawGroundEnemyProjectiles(SpriteBatch spriteBatch)
    {
        bool highContrast = GameProfile.Profile.HighContrast;
        foreach (var projectile in State.EnemyProjectileHolster.Where(projectile =>
                     projectile.Path == "pool"
                     && (PathFog is null || PathFog.IsWorldAreaVisible(projectile.WorldRect()))))
            projectile.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake, highContrast);
    }

    /// <summary>Airborne hostile shots and telegraphs render above combat actors.</summary>
    public void DrawEnemyProjectiles(SpriteBatch spriteBatch)
    {
        bool highContrast = GameProfile.Profile.HighContrast;
        foreach (var projectile in State.EnemyProjectileHolster.Where(projectile =>
                     projectile.Path != "pool"
                     && (PathFog is null || PathFog.IsWorldAreaVisible(projectile.WorldRect()))))
            projectile.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake, highContrast);
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
        var items = new List<WorldDepthDrawItem>(
            State.BulletHolster.Count + State.EnemyHolster.Count + State.EnemyProjectileHolster.Count + 8);
        int stableOrder = 0;

        foreach (var bullet in State.BulletHolster)
        {
            Vector2 anchor = new(bullet.WorldX + bullet.Size / 2f, bullet.WorldY + bullet.Size / 2f);
            items.Add(new WorldDepthDrawItem(anchor, 10, stableOrder++,
                batch => bullet.Draw(batch, Camera, PlayerWorldCenter, ScreenShake)));
        }

        var seenEncounters = new HashSet<int>();
        foreach (var enemy in State.EnemyHolster.Where(enemy =>
                     PathFog is null || PathFog.IsWorldAreaVisible(enemy.WorldRect())))
        {
            var encounter = enemy.Encounter;
            if (encounter is not null && seenEncounters.Add(encounter.Id))
            {
                items.Add(new WorldDepthDrawItem(encounter.Center(), 15, stableOrder++,
                    batch => encounter.Draw(batch, Camera, PlayerWorldCenter, ScreenShake)));
            }

            Vector2 anchor = new(enemy.WorldX + enemy.Size / 2f, enemy.WorldY + enemy.Size / 2f);
            items.Add(new WorldDepthDrawItem(anchor, 20, stableOrder++,
                batch => enemy.Draw(batch, Camera, PlayerWorldCenter, ScreenShake)));
        }

        items.Add(new WorldDepthDrawItem(PlayerWorldCenter, 30, stableOrder++,
            batch => Player.Draw(batch, State, Camera)));

        bool highContrast = GameProfile.Profile.HighContrast;
        foreach (var projectile in State.EnemyProjectileHolster.Where(projectile =>
                     projectile.Path != "pool"
                     && (PathFog is null || PathFog.IsWorldAreaVisible(projectile.WorldRect()))))
        {
            Vector2 anchor = new(projectile.WorldX + projectile.Size / 2f, projectile.WorldY + projectile.Size / 2f);
            items.Add(new WorldDepthDrawItem(anchor, 40, stableOrder++,
                batch => projectile.Draw(batch, Camera, PlayerWorldCenter, ScreenShake, highContrast)));
        }

        _arenaRenderer.DrawDepthSortedWorld(
            spriteBatch,
            Camera,
            PlayerWorldCenter,
            ScreenShake,
            new Rectangle(0, 0, InformationSheet.ArenaWidth, ScreenHeight),
            items);
    }

    /// <summary>Ported from character.py's handlingDamagingEnemies(). Portal-hit routing is deferred (no current enemy type implements it).</summary>
    public void HandleDamagingEnemies(Random? rng = null)
    {
        rng ??= Random.Shared;
        var enemyGrid = new SpatialHash<Enemy>(Math.Max(64, (int)(Simulation.TileSize * 2)));
        foreach (var enemy in State.EnemyHolster)
        {
            if (ReferenceEquals(enemy, State.ActiveBoss) && DeathSpectacleActive(enemy))
                continue;
            foreach (var (_, hitbox) in enemy.GetScreenHitboxes(Camera, PlayerWorldCenter, ScreenShake))
                enemyGrid.Insert(enemy, hitbox);
        }

        var deadEnemies = new HashSet<Enemy>();
        foreach (var bullet in State.BulletHolster)
        {
            var bulletScreenPos = Camera.WorldToScreen(new Vector2(bullet.WorldX, bullet.WorldY), PlayerWorldCenter, ScreenShake);
            var bulletRect = new Rectangle((int)bulletScreenPos.X, (int)bulletScreenPos.Y, (int)bullet.Size, (int)bullet.Size);
            var candidates = enemyGrid.Query(bulletRect)
                .Select(enemy => (Enemy: enemy, HasShield: enemy.GetScreenHitboxes(Camera, PlayerWorldCenter, ScreenShake)
                    .Any(h => h.Part == "shield" && bulletRect.Intersects(h.Rect))))
                .OrderBy(item => item.HasShield ? 0 : 1)
                .Select(item => item.Enemy)
                .ToList();

            foreach (var enemy in candidates)
            {
                if (deadEnemies.Contains(enemy))
                    continue;
                var hitboxes = enemy.GetScreenHitboxes(Camera, PlayerWorldCenter, ScreenShake);
                var collided = hitboxes.FirstOrDefault(h => bulletRect.Intersects(h.Rect));
                if (collided.Part is null)
                    continue;
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
                    if (State.Equipment.GetValueOrDefault("weapon") is { Definition.EffectIds.Count: > 0 } weapon)
                        UniqueEffects.OnPlayerHit(enemy, bullet, weapon, State, rng);
                }
                if (result.Applied)
                {
                    GameProfile.IncrementQuest("damage_dealt", Math.Max(0, (long)Math.Round(result.Amount)));
                    if (bullet.IsCritical)
                        GameProfile.IncrementQuest("critical_hits");
                }
                if (enemy.TransitionCleanupRequested)
                {
                    if (enemy.TransitionCleanupOwner is not null)
                        State.EnemyProjectileHolster.RemoveAll(p => p.Owner == enemy.TransitionCleanupOwner);
                    else
                        State.EnemyProjectileHolster.Clear();
                    enemy.TransitionCleanupRequested = false;
                }
                Color currColor = bullet.IsCritical ? UiTheme.Purple : UiTheme.Gold;
                object displayValue = result.Applied ? Math.Round(result.Amount) : "BLOCK";
                var textWorld = Camera.ScreenToWorld(new Vector2(collided.Rect.X, collided.Rect.Y), PlayerWorldCenter, ScreenShake);
                State.DamageTextList.Add(new DamageText(textWorld.X, textWorld.Y, currColor, displayValue, collided.Rect.Width, Simulation.FrameRate));
                if (result.Killed)
                    deadEnemies.Add(enemy);
            }
        }

        foreach (var enemy in State.EnemyHolster)
            if (enemy.IsDead())
                deadEnemies.Add(enemy);

        foreach (var enemy in deadEnemies)
        {
            State.NumOfEnemiesKilled += 1;
            GameProfile.IncrementQuest("enemies_defeated");
            State.ExperienceList.Add(new ExperienceBubble(
                enemy.WorldX, enemy.WorldY,
                State.XpMult * (enemy.ExpValue * (State.CurrentStage * State.ExperienceStageMod)),
                enemy.Difficulty, rng, celebration: ReferenceEquals(enemy, State.ActiveBoss)));
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
            var drops = Items.GenerateDrops(regularDropCount, rng, State.HardMode, GamePaths.Active().Key,
                State.NewGamePlusLevel);
            if (defeatedBossKey is not null && Items.RollUniqueDrop(defeatedBossKey, rng, State.NewGamePlusLevel) is { } uniqueDrop)
                drops.Add(uniqueDrop);
            if (drops.Count > 0)
                SpawnLootCrate(enemy.WorldX, enemy.WorldY, drops);

            if (defeatedBossKey is not null)
            {
                GameProfile.IncrementQuest("bosses_defeated");
                if (defeatedBossKey == GamePaths.BossKey(midpoint: true))
                {
                    State.BeaudisDefeated = true;
                }
                else if (defeatedBossKey == GamePaths.BossKey(midpoint: false))
                {
                    State.GameCompleted = true;
                    State.RunOutcome = "RUN COMPLETE";
                    MetaProgression.RecordExtraction(State, PathRun?.CurrentSenseKey ?? GamePaths.Selected().Key, completed: true);
                    MetaProgression.SyncCarriedItems(State);
                    GameProfile.RecordRun(State.CurrentLevel, State.NumOfEnemiesKilled, completed: true);
                }
                CompleteBossTelemetry(victory: true);
                State.ActiveBoss = null;
                _activeBossKey = null;
                State.EnemySpawningEnabled = !State.GameCompleted;
                ScreenShake = Vector2.Zero;
                State.EnemyProjectileHolster.Clear();
                PathRun?.NotifyBossDefeated();
            }
        }
        if (deadEnemies.Count > 0)
        {
            State.EnemyHolster.RemoveAll(e => deadEnemies.Contains(e));
            State.CurrEnemyCount = State.EnemyHolster.Count;
        }
        State.BulletHolster.RemoveAll(b => b.RemFlag);
    }

    private static string? BossKeyFor(Enemy enemy) => enemy switch
    {
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
        Beaudis { Dying: true }
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
        foreach (var bubble in State.ExperienceList.ToList())
        {
            var bubbleRect = bubble.WorldRect();
            if (playerRect.Intersects(bubbleRect))
            {
                State.ExpCount += bubble.Value;
                State.ExperienceList.Remove(bubble);
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

        foreach (var fragment in State.FragmentList.ToList())
        {
            var fragmentRect = fragment.WorldRect();
            if (playerRect.Intersects(fragmentRect))
            {
                State.Fragments += 1;
                State.FragmentList.Remove(fragment);
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

    private void HandlePathEnemyCreation(Random rng, bool interactPressed)
    {
        var run = PathRun!;
        PathFog?.Update(PlayerWorldCenter);
        foreach (var completedRoom in run.CompleteReadyCombatRooms(State.EnemyHolster))
        {
            if (completedRoom.Type == PathRoomType.Treasure)
                SpawnPathTreasure(completedRoom, rng);
            else if (completedRoom.Type == PathRoomType.Challenge)
                SpawnPathTreasure(completedRoom, rng, bonusItems: 1);
        }
        if (run.ActiveCombatRooms.Count == 0 && State.ActiveBoss is null)
            State.BossAfflictions.Reset();

        if (run.ExitPortalOpen)
        {
            if (interactPressed && run.PlayerAtExitPortal(Player.WorldRect(State), BossPortalInteractRadius)
                && run.AdvanceFloor(State.RunTimeSeconds))
            {
                InstallNextPathFloor();
            }
            return;
        }

        var room = run.TryActivateRoom(PlayerWorldCenter, State.RunTimeSeconds);
        if (room is null)
            return;
        switch (room.Type)
        {
            case PathRoomType.Skirmish:
            case PathRoomType.Assault:
            case PathRoomType.Elite:
            case PathRoomType.Challenge:
                SpawnPathRoomWave(room, rng);
                break;
            case PathRoomType.Treasure:
                SpawnPathTreasureEncounter(room, rng);
                break;
            case PathRoomType.Boss:
                SpawnPathFloorBoss(room, rng);
                break;
        }
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

        var spawned = new List<Enemy>();
        for (int index = 0; index < count; index++)
        {
            var definition = ChoosePathRoomEnemy(
                room, encounterLevel, index, spawned, run.CurrentSenseKey, rng);
            if (definition is null)
                break;
            int nominalSize = (int)(Simulation.TileSize * definition.Size);
            var spawn = run.Layout.FindEncounterSpawnRect(room, nominalSize, index, count, rng);
            var enemy = EnemyCatalog.Shared.Create(definition.Key, spawn.X, spawn.Y,
                encounterLevel, AwarenessRange, rng, Battleground);
            EnemyCatalog.Shared.ApplyModifier(enemy, encounterLevel, rng);
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
            spawned.Add(enemy);
        }
        if (guardianStrength && spawned.Count > 0)
        {
            double guardianHealth = (5_800 + run.FloorNumber * 1_550)
                * run.HealthMultiplier * .92;
            double currentHealth = spawned.Sum(enemy => (double)enemy.MaxHp);
            double healthScale = Math.Clamp(guardianHealth / Math.Max(1, currentHealth), .85, 3.5);
            foreach (var enemy in spawned)
            {
                enemy.MaxHp = Math.Max(1, (int)Math.Round(enemy.MaxHp * healthScale));
                enemy.Hp = enemy.MaxHp;
                enemy.Damage = Math.Max(1, (int)Math.Round(enemy.Damage * 1.12));
                enemy.ExpValue *= 1.35;
            }
        }
        State.EnemyHolster.AddRange(spawned);
        State.CurrEnemyCount = State.EnemyHolster.Count;
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
        };
        guardian.DisengageRange = guardian.AwarenessRange * 1.5f;
        ApplyRunDifficulty(guardian);
        guardian.MaxHp = Math.Max(1, (int)Math.Round(guardian.MaxHp * .9));
        guardian.Hp = guardian.MaxHp;
        guardian.Damage = Math.Max(1, (int)Math.Round(guardian.Damage * .92));
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
        int encounterLevel,
        int index,
        IReadOnlyList<Enemy> existing,
        string senseKey,
        Random rng)
    {
        string[] roles = room.Shape switch
        {
            PathRoomShape.LongHall => ["artillery", "control", "artillery", "pressure"],
            PathRoomShape.GrandArena => ["pressure", "artillery", "tank", "support"],
            PathRoomShape.Maze => ["pressure", "tank", "control", "pressure"],
            PathRoomShape.Crossroads => ["control", "pressure", "artillery", "pressure"],
            PathRoomShape.Ring => ["artillery", "control", "pressure", "support"],
            PathRoomShape.Ruin => ["pressure", "control", "tank"],
            _ => ["pressure", "artillery", "control", "tank"],
        };
        string desiredRole = roles[index % roles.Length];
        var available = EnemyCatalog.Shared.Available(encounterLevel, senseKey)
            .Where(definition => !definition.GuaranteedOnly && definition.Family != "banner")
            .Where(definition =>
                EnemyCatalogData.FamilyIdentities.TryGetValue(definition.Family, out var identity)
                && identity.CombatRole == desiredRole)
            .Where(definition => existing.Count(enemy => enemy.Family == definition.Family) < definition.MaxActive)
            .ToList();
        if (available.Count == 0)
        {
            return EnemyCatalog.Shared.Choose(
                encounterLevel, rng, existing: existing, contentPath: senseKey);
        }

        double totalWeight = available.Sum(definition => definition.Weight);
        double roll = rng.NextDouble() * totalWeight;
        foreach (var definition in available)
        {
            roll -= definition.Weight;
            if (roll <= 0)
                return definition;
        }
        return available[^1];
    }

    private void SpawnPathTreasure(PathRoom room, Random rng, int bonusItems = 0)
    {
        var run = PathRun!;
        int bonusMaximum = run.IsSecondAct ? 3 : 2;
        int count = TreasureChest.MinimumItems + bonusItems + rng.Next(bonusMaximum);
        int rewardTier = run.FloorNumber >= 8 ? 3 : run.FloorNumber >= 5 ? 2 : run.FloorNumber >= 3 ? 1 : 0;
        var drops = Items.GenerateDrops(count, rng, State.HardMode, run.CurrentSenseKey, rewardTier);
        float size = Simulation.TileSize * 1.15f;
        var chest = new TreasureChest(
            room.WorldCenter.X - size / 2f,
            room.WorldCenter.Y - size / 2f,
            drops,
            run.CurrentSenseKey);
        State.LootCrateList.Add(chest);
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
        StepPlayerBackFromArenaCenter(
            run.CurrentSenseKey == "sound" && run.BossTier == PathFloorBossTier.Finale
                ? Simulation.TileSize * 9.6f
                : Simulation.TileSize * 2.5f);
    }

    public static bool RollFragmentDrop(Random? rng = null)
    {
        rng ??= Random.Shared;
        return rng.NextDouble() < FragmentDropChance;
    }

    public bool CanPurchaseLevelUp =>
        State.CurrentLevel < Progression.MaxLevel && State.ExpCount >= State.ExpNeededForNextLevel;

    /// <summary>Consumes one threshold and queues exactly one card draft.</summary>
    public bool TryPurchaseLevelUp()
    {
        if (!CanPurchaseLevelUp)
            return false;
        State.ExpCount -= State.ExpNeededForNextLevel;
        State.CurrentLevel += 1;
        State.PendingLevelUps += 1;
        State.ExpNeededForNextLevel *= State.LevelScaleIncreaseFunction;
        State.FillHealthForMilestone();
        GameProfile.IncrementQuest("levels_gained");
        return true;
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
    /// dispatched to Beaudis, Dissonance, or the shared PathChaseBoss family;
    /// the "C" rune-cannon hotkey remains Dissonance-specific.
    ///
    /// Gated behind BossDebugInvincible (the "Y" dev-toggle, see RunState's
    /// "Hidden debug hotkey state" doc comment) -- unlike Python, these raw
    /// 1-9/R/L/F/C key checks never went through Keybinds, so without this
    /// gate they fired on every real bossfight regardless of what the player
    /// had "restart" (also R by default) bound to, silently resetting the
    /// boss's phase.
    /// </summary>
    public void HandleBossDebugControls(IReadOnlySet<Keys> keysPressed)
    {
        if (!State.BossDebugInvincible)
            return;
        if (State.ActiveBoss is Beaudis beaudis)
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
        graphicsDevice.ScissorRectangle = new Rectangle(0, 0, InformationSheet.ArenaWidth, ScreenHeight);
        spriteBatch.Begin(rasterizerState: LootCrateScissorRasterizerState);
        foreach (var crate in State.LootCrateList)
        {
            var screen = Camera.ApplyZoom(Camera.WorldToScreen(new Vector2(crate.WorldX, crate.WorldY), PlayerWorldCenter, ScreenShake));
            int zoomedSize = (int)(crate.Size * Camera.Zoom);
            var rect = new Rectangle((int)screen.X, (int)screen.Y, zoomedSize, zoomedSize);
            if (rect.Intersects(new Rectangle(0, 0, InformationSheet.ArenaWidth, ScreenHeight)))
                crate.Draw(spriteBatch, Camera, PlayerWorldCenter, ScreenShake);
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
        if (!BossPortalOpen && !pathExit)
            return;

        Vector2 portalWorld = pathExit ? PathRun!.ExitPortalWorld : ArenaCenterWorld;
        Color portalColor = pathExit ? PathRun!.CurrentSense.Accent : UiTheme.Purple;
        var screen = Camera.ApplyZoom(Camera.WorldToScreen(portalWorld, PlayerWorldCenter, ScreenShake));
        float radius = Simulation.TileSize * 1.1f * Camera.Zoom;
        var bounds = new Rectangle(0, 0, InformationSheet.ArenaWidth, ScreenHeight);
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
        bool playerAtPortal = pathExit
            ? PathRun!.PlayerAtExitPortal(Player.WorldRect(State), BossPortalInteractRadius)
            : PlayerAtBossPortal();
        if (playerAtPortal)
        {
            string keyLabel = PreferControllerPrompts
                ? "B"
                : Keybinds.LabelForKey(Keybinds.KeyFor("interact"));
            string action = pathExit ? "NEXT FLOOR" : "ENTER";
            UiTheme.DrawText(spriteBatch, $"{keyLabel}  //  {action}", 9, portalColor,
                new Vector2(screen.X, screen.Y + radius + 12), "midtop");
        }

        spriteBatch.End();
        graphicsDevice.ScissorRectangle = previousScissor;
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
        if (State.ActiveBoss is Enemy boss && !boss.IsDead()
            && (PathFog is null || PathFog.IsWorldAreaVisible(boss.WorldRect())))
        {
            return new BountyInfo(
                new Vector2(boss.WorldX + boss.Size / 2f, boss.WorldY + boss.Size / 2f),
                double.PositiveInfinity, boss.Family, boss);
        }

        BountyInfo? best = null;
        double bestScore = double.NegativeInfinity;
        var seenEncounters = new HashSet<int>();
        foreach (var enemy in State.EnemyHolster)
        {
            if (enemy.IsDead()
                || (PathFog is not null && !PathFog.IsWorldAreaVisible(enemy.WorldRect())))
                continue;
            var encounter = enemy.Encounter;
            if (encounter is not null)
            {
                if (!seenEncounters.Add(encounter.Id))
                    continue;
                var living = encounter.Members.Where(member =>
                    !member.IsDead()
                    && (PathFog is null || PathFog.IsWorldAreaVisible(member.WorldRect()))).ToList();
                if (living.Count == 0)
                    continue;
                var world = new Vector2(
                    living.Average(member => member.WorldX + member.Size / 2f),
                    living.Average(member => member.WorldY + member.Size / 2f));
                double reward = living.Sum(member => member.ExpValue);
                double threat = living.Sum(member => member.ThreatCost);
                double score = reward + threat * 4;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new BountyInfo(world, score, encounter.Key.Replace("_", " ").ToUpperInvariant(), encounter);
                }
            }
            else
            {
                double eliteBonus = enemy.CombatRole == "elite" ? 500 : 0;
                double score = enemy.ExpValue + enemy.ThreatCost * 4 + eliteBonus;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new BountyInfo(
                        new Vector2(enemy.WorldX + enemy.Size / 2f, enemy.WorldY + enemy.Size / 2f),
                        score, enemy.Family.Replace("_", " ").ToUpperInvariant(), enemy);
                }
            }
        }
        return best;
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
        var bounty = SelectBountyTarget();
        if (bounty is null)
            return;
        var targetScreen = Camera.ApplyZoom(Camera.WorldToScreen(bounty.World, PlayerWorldCenter, ScreenShake));
        int arenaWidth = InformationSheet.ArenaWidth;
        // The marker is navigation for off-screen targets only -- once the target's
        // center enters the playable view, the enemy itself is the clearer cue.
        if (new Rectangle(0, 0, arenaWidth, ScreenHeight).Contains(targetScreen.ToPoint()))
            return;

        int topMargin = State.ActiveBoss is not null ? 112 : 44;
        var viewport = new Rectangle(34, topMargin, Math.Max(1, arenaWidth - 68), Math.Max(1, ScreenHeight - topMargin - 42));
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
        if (!BossPortalOpen && !pathExit)
            return;
        Vector2 portalWorld = pathExit ? PathRun!.ExitPortalWorld : ArenaCenterWorld;
        Color portalColor = pathExit ? PathRun!.CurrentSense.Accent : UiTheme.Purple;
        var targetScreen = Camera.ApplyZoom(Camera.WorldToScreen(portalWorld, PlayerWorldCenter, ScreenShake));
        int arenaWidth = InformationSheet.ArenaWidth;
        if (new Rectangle(0, 0, arenaWidth, ScreenHeight).Contains(targetScreen.ToPoint()))
            return;

        int topMargin = State.ActiveBoss is not null ? 112 : 44;
        var viewport = new Rectangle(34, topMargin, Math.Max(1, arenaWidth - 68), Math.Max(1, ScreenHeight - topMargin - 42));
        var geometry = BountyArrowGeometry(Camera.Lock, targetScreen, viewport);
        if (geometry is null)
            return;
        var (points, tip, direction) = geometry.Value;

        var shadow = points.Select(p => p + new Vector2(4, 5)).ToArray();
        Primitives2D.FillPolygon(spriteBatch, shadow, UiTheme.Shadow);
        Primitives2D.FillPolygon(spriteBatch, points, portalColor);
        Primitives2D.PolygonOutline(spriteBatch, points, UiTheme.Ink, 4);
        var labelPosition = tip - direction * 52f;
        UiTheme.DrawText(spriteBatch, pathExit ? "NEXT FLOOR" : "PORTAL", 9, portalColor, labelPosition, "center");
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
        DrawPathRoomBanner(spriteBatch);
        DrawBossHealthBar(spriteBatch);
        DrawLowHealthWarning(spriteBatch);
        DrawRunCompleteBanner(spriteBatch);
        DrawTutorialHint(spriteBatch);
    }

    /// <summary>
    /// Compact graph-and-footprint map for the larger generated floors.
    /// Unvisited rooms stay subdued; branches, the active lock, and the
    /// player's position remain readable without revealing enemy placement.
    /// </summary>
    private void DrawPathMinimap(SpriteBatch spriteBatch)
    {
        if (PathRun is null)
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        var panel = new Rectangle(
            (int)(14 * scale),
            ScreenHeight - (int)(142 * scale),
            (int)(224 * scale),
            (int)(126 * scale));
        UiTheme.DrawPanel(spriteBatch, panel, UiTheme.Panel * .94f, PathRun.CurrentSense.Accent, shadow: 5);
        UiTheme.DrawText(spriteBatch, $"FLOOR MAP // {PathRun.Layout.Style.ToString().ToUpperInvariant()}",
            9 * scale, UiTheme.Muted, new Vector2(panel.X + 9 * scale, panel.Y + 7 * scale));

        var mapArea = new Rectangle(
            panel.X + (int)(9 * scale),
            panel.Y + (int)(25 * scale),
            panel.Width - (int)(18 * scale),
            panel.Height - (int)(34 * scale));
        Vector2 MapPoint(Point tile) => new(
            mapArea.X + tile.X / (float)Math.Max(1, Battleground.Width) * mapArea.Width,
            mapArea.Y + tile.Y / (float)Math.Max(1, Battleground.Height) * mapArea.Height);

        foreach (var connection in PathRun.Layout.Connections)
        {
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

        var activeRooms = PathRun.ActiveCombatRooms.Select(room => room.Id).ToHashSet();
        foreach (var room in PathRun.Layout.Rooms)
        {
            if (PathFog is not null && !PathFog.AnyExplored(room.TileBounds))
                continue;
            Rectangle roomRect = PathMinimapRoomRect(room, Battleground, mapArea);
            Color color = activeRooms.Contains(room.Id)
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
            Primitives2D.FillRect(spriteBatch, roomRect, UiTheme.Ink * .9f);
            Primitives2D.RectOutline(spriteBatch, roomRect, color, room.IsMainPath ? 2 : 1);
            if (room.Type == PathRoomType.Treasure)
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle(roomRect.Center.X - 1, roomRect.Center.Y - 1, 3, 3), UiTheme.Gold);
        }

        Point playerTile = new(
            (int)(PlayerWorldCenter.X / Battleground.TileSize),
            (int)(PlayerWorldCenter.Y / Battleground.TileSize));
        Vector2 player = MapPoint(playerTile);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)player.X - 3, (int)player.Y - 3, 7, 7), UiTheme.Cream);
        Primitives2D.RectOutline(spriteBatch,
            new Rectangle((int)player.X - 3, (int)player.Y - 3, 7, 7), UiTheme.Ink, 1);
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
            return;

        float time = (float)State.RunTimeSeconds;
        Color accent = PathRun.CurrentSense.Accent;
        foreach (var emitter in Battleground.PathDecorations.Where(
            decoration => decoration.Layer == PathDecorationLayer.Ambient))
        {
            Vector2 emitterScreen = Camera.WorldToScreen(emitter.WorldPosition, PlayerWorldCenter, ScreenShake);
            if (emitterScreen.X < -100 || emitterScreen.X > InformationSheet.ArenaWidth + 100
                || emitterScreen.Y < -100 || emitterScreen.Y > ScreenHeight + 100)
            {
                continue;
            }

            int particles = PathRun.IsSecondAct ? 6 : 4;
            for (int index = 0; index < particles; index++)
            {
                float seed = emitter.Variant * 17.3f + emitter.RoomId * 9.7f + index * 23.1f;
                float phase = (time * (.28f + index * .025f) + seed) % 1f;
                switch (emitter.Kind)
                {
                    case PathDecorationKind.DripEmitter:
                    {
                        Vector2 world = emitter.WorldPosition + new Vector2(
                            MathF.Sin(seed) * 44f * emitter.Scale,
                            (phase * 100f - 50f) * emitter.Scale);
                        Vector2 p = Camera.WorldToScreen(world, PlayerWorldCenter, ScreenShake);
                        Primitives2D.Line(spriteBatch, p, p + new Vector2(0, 8 * emitter.Scale), accent * (.42f + phase * .3f), 2);
                        if (phase > .92f)
                            Primitives2D.Line(spriteBatch, p + new Vector2(-5, 8), p + new Vector2(5, 8), accent * .45f, 1);
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
                        Primitives2D.EllipseOutline(spriteBatch, rect, accent * ((1f - phase) * .46f), 1, 24);
                        break;
                    }
                    case PathDecorationKind.WindEmitter:
                    {
                        float travel = (phase * 150f - 75f) * emitter.Scale;
                        Vector2 p = emitterScreen + new Vector2(travel, MathF.Sin(seed) * 34f);
                        Primitives2D.Line(spriteBatch, p, p + new Vector2(22 * emitter.Scale, -5), accent * .32f, 2);
                        Primitives2D.FillRect(spriteBatch, new Rectangle((int)p.X + 22, (int)p.Y - 7, 4, 3), accent * .42f);
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
                        Primitives2D.FillRect(spriteBatch, new Rectangle((int)p.X, (int)p.Y, 3, 3), ash * .52f);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Covers unexplored tiles and dims explored tiles outside current line
    /// of sight. This runs after every world object (including loot/portals)
    /// and before the HUD, so discovery applies consistently without
    /// darkening interface panels.
    /// </summary>
    public void DrawPathFogOfWar(SpriteBatch spriteBatch)
    {
        if (PathFog is null)
            return;

        var displayViewport = new Rectangle(0, 0, InformationSheet.ArenaWidth, ScreenHeight);
        Rectangle logicalViewport = Camera.LogicalViewport(displayViewport);
        Vector2[] viewportWorld =
        {
            Camera.ScreenToWorld(new Vector2(logicalViewport.Left, logicalViewport.Top), PlayerWorldCenter, ScreenShake),
            Camera.ScreenToWorld(new Vector2(logicalViewport.Right, logicalViewport.Top), PlayerWorldCenter, ScreenShake),
            Camera.ScreenToWorld(new Vector2(logicalViewport.Right, logicalViewport.Bottom), PlayerWorldCenter, ScreenShake),
            Camera.ScreenToWorld(new Vector2(logicalViewport.Left, logicalViewport.Bottom), PlayerWorldCenter, ScreenShake),
        };
        int left = Math.Clamp((int)MathF.Floor(viewportWorld.Min(point => point.X) / Battleground.TileSize) - 2,
            0, Battleground.Width - 1);
        int right = Math.Clamp((int)MathF.Ceiling(viewportWorld.Max(point => point.X) / Battleground.TileSize) + 2,
            0, Battleground.Width - 1);
        int top = Math.Clamp((int)MathF.Floor(viewportWorld.Min(point => point.Y) / Battleground.TileSize) - 2,
            0, Battleground.Height - 1);
        int bottom = Math.Clamp((int)MathF.Ceiling(viewportWorld.Max(point => point.Y) / Battleground.TileSize) + 2,
            0, Battleground.Height - 1);
        float rotation = -MathHelper.ToRadians(Camera.AngleDegrees);
        Vector2 tileSize = new(Battleground.TileSize + 1f, Battleground.TileSize + 1f);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (PathFog.IsVisible(x, y))
                    continue;
                Color fog = PathFog.IsExplored(x, y)
                    ? new Color(4, 7, 13, 178)
                    : new Color(2, 3, 7, 250);
                Vector2 topLeft = Camera.WorldToScreen(
                    new Vector2(x * Battleground.TileSize, y * Battleground.TileSize),
                    PlayerWorldCenter, ScreenShake);
                Primitives2D.FillRotatedRect(spriteBatch, topLeft, tileSize, rotation, fog);

                if (!Battleground.IsRaisedAt(x, y))
                    continue;
                var (ground, cap) = ArenaRenderer.WallScreenGeometry(
                    Camera, PlayerWorldCenter, ScreenShake, x, y, Battleground.WallHeight);
                Primitives2D.FillPolygon(spriteBatch, cap, fog);
                for (int edge = 0; edge < 4; edge++)
                {
                    int next = (edge + 1) % 4;
                    Primitives2D.FillPolygon(spriteBatch,
                        [cap[edge], cap[next], ground[next], ground[edge]], fog);
                }
            }
        }
    }

    private void DrawPathTitleBanner(SpriteBatch spriteBatch)
    {
        if (PathRun is null || !PathRun.TitleBannerVisible(State.RunTimeSeconds))
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(InformationSheet.ArenaWidth * .72f, 780 * scale);
        var rect = new Rectangle((InformationSheet.ArenaWidth - width) / 2,
            (int)(28 * scale), width, (int)(76 * scale));
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, PathRun.CurrentSense.Accent, shadow: 7);
        UiTheme.DrawText(spriteBatch, PathRun.TitleBanner, 24 * scale, UiTheme.Text,
            new Vector2(rect.Center.X, rect.Y + 10 * scale), "midtop");
        string act = PathRun.IsSecondAct ? "DESCENT II // HARDENED" : "DESCENT I";
        const int totalFloors = global::RotBoiRemastered.Systems.PathRun.TotalFloors;
        UiTheme.DrawText(spriteBatch, $"FLOOR {PathRun.FloorNumber:D2} / {totalFloors:D2} // {act}",
            10 * scale, PathRun.CurrentSense.Accent,
            new Vector2(rect.Center.X, rect.Bottom - 11 * scale), "midbottom");
    }

    private void DrawPathRoomBanner(SpriteBatch spriteBatch)
    {
        if (PathRun?.LastEnteredRoom is not { } room
            || !PathRun.RoomBannerVisible(State.RunTimeSeconds)
            || PathRun.TitleBannerVisible(State.RunTimeSeconds))
        {
            return;
        }

        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(InformationSheet.ArenaWidth * .48f, 520 * scale);
        var rect = new Rectangle(
            (InformationSheet.ArenaWidth - width) / 2,
            (int)(34 * scale),
            width,
            (int)(48 * scale));
        Color accent = room.Type == PathRoomType.Challenge ? UiTheme.Gold : PathRun.CurrentSense.Accent;
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, accent, shadow: 5);
        UiTheme.DrawText(spriteBatch, room.EntryBanner, 15 * scale, UiTheme.Text,
            rect.Center.ToVector2(), "center");
    }

    public void DrawAimReticle(SpriteBatch spriteBatch, Point mousePosition)
    {
        if (mousePosition.X < 0 || mousePosition.X >= InformationSheet.ArenaWidth
            || mousePosition.Y < 0 || mousePosition.Y >= ScreenHeight || InformationSheet.DragInProgress)
            return;
        var center = mousePosition.ToVector2();
        Color color = State.AutoFire || InputState.MouseDown ? UiTheme.Cream : UiTheme.Text;
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
        if (State.ActiveBoss is not Enemy boss || boss.Hp <= 0 || DeathSpectacleActive(boss)
            || (PathFog is not null && !PathFog.IsWorldAreaVisible(boss.WorldRect())))
            return;
        var phase = State.ActiveBoss switch
        {
            Beaudis b => (b.Phase, b.PhaseLabel, b.PhaseAccent, b.EntranceRemaining),
            Dissonance d => (d.Phase, d.PhaseLabel, d.PhaseAccent, d.EntranceRemaining),
            PathChaseBoss p => (p.Phase, p.PhaseLabel, p.PhaseAccent, p.EntranceRemaining),
            PathGuardianBoss g => (g.Phase, g.PhaseLabel,
                g.TrialActive ? g.SecondaryAccent : g.PhaseAccent,
                g.EntranceRemaining),
            _ => (1, "ENGAGED", UiTheme.Red, 0.0),
        };
        if (phase.Item4 > 1.0)
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(InformationSheet.ArenaWidth * .62f, 720 * scale);
        var rect = new Rectangle((InformationSheet.ArenaWidth - width) / 2, (int)(16 * scale), width, (int)(70 * scale));
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, phase.Item3, shadow: 6);
        string name = boss is PathGuardianBoss guardian
            ? guardian.BossDisplayName
            : (_activeBossKey ?? BossKeyFor(boss) ?? boss.Family)
                .Replace('_', ' ').ToUpperInvariant();
        UiTheme.DrawText(spriteBatch, name, 20 * scale, UiTheme.Text, new Vector2(rect.X + 14 * scale, rect.Y + 8 * scale));
        string phasePrefix = boss is PathGuardianBoss { TrialActive: true }
            ? "TRIAL"
            : $"PHASE {phase.Item1}";
        UiTheme.DrawText(spriteBatch, $"{phasePrefix} // {phase.Item2}", 10 * scale, phase.Item3,
            new Vector2(rect.Right - 14 * scale, rect.Y + 13 * scale), "topright");
        var hpRect = new Rectangle((int)(rect.X + 14 * scale), (int)(rect.Y + 43 * scale), (int)(rect.Width - 28 * scale), (int)(12 * scale));
        float progress = boss is PathGuardianBoss { TrialActive: true } trial
            ? Math.Clamp((float)(trial.TrialRemaining /
                Math.Max(.01, trial.TrialDuration)), 0f, 1f)
            : Math.Clamp((float)boss.Hp / Math.Max(1, boss.MaxHp), 0f, 1f);
        UiTheme.DrawProgress(spriteBatch, hpRect, progress, phase.Item3, 18);
    }

    private void DrawLowHealthWarning(SpriteBatch spriteBatch)
    {
        double ratio = State.HealthPoints / Math.Max(1.0, State.MaxHealthPoints);
        if (ratio > .3)
            return;
        int alpha = Math.Clamp((int)(35 + (1 - ratio / .3) * 65), 0, 255);
        int border = Math.Max(8, (int)(22 * UiTheme.DisplayScale(spriteBatch)));
        var color = new Color(UiTheme.Red.R, UiTheme.Red.G, UiTheme.Red.B, (byte)alpha);
        Primitives2D.RectOutline(spriteBatch, new Rectangle(0, 0, InformationSheet.ArenaWidth, ScreenHeight), color, border);
    }

    private void DrawRunCompleteBanner(SpriteBatch spriteBatch)
    {
        if (!State.GameCompleted)
            return;
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(InformationSheet.ArenaWidth * .58f, 680 * scale);
        var rect = new Rectangle((InformationSheet.ArenaWidth - width) / 2, (int)(22 * scale), width, (int)(76 * scale));
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Cream, shadow: 7);
        string headline = PathRun is not null
            ? "THE WOVEN PATH TRAVERSED"
            : $"{GamePaths.BossKey(false).ToUpperInvariant()} ENDED";
        const int totalFloors = global::RotBoiRemastered.Systems.PathRun.TotalFloors;
        string detail = PathRun is not null
            ? $"FLOOR {totalFloors:D2} // ALL SENSES COMPLETE"
            : "LEVEL 20 // RUN COMPLETE";
        UiTheme.DrawText(spriteBatch, headline, 24 * scale, UiTheme.Cream,
            new Vector2(rect.Center.X, rect.Y + 10 * scale), "midtop");
        UiTheme.DrawText(spriteBatch, detail, 11 * scale, UiTheme.Purple,
            new Vector2(rect.Center.X, rect.Bottom - 12 * scale), "midbottom");
        UiTheme.DrawText(spriteBatch, "ENTER  VIEW RESULTS", 9 * scale, UiTheme.Text,
            new Vector2(rect.Center.X, rect.Bottom + 12 * scale), "midtop");
    }

    private void DrawTutorialHint(SpriteBatch spriteBatch)
    {
        if (!GameProfile.Profile.TutorialHints || State.RunTimeSeconds >= 42 || State.GameCompleted)
            return;
        string text = State.RunTimeSeconds switch
        {
            < 8 => "WASD MOVE  //  MOUSE AIM  //  PRESS I FOR AUTOFIRE",
            < 16 => "SPACE DASHES IN YOUR MOVEMENT DIRECTION AND BRIEFLY AVOIDS DAMAGE",
            < 25 => "FOLLOW THE RED BOUNTY ARROW TO HIGH-VALUE PATROLS",
            < 34 => "Q / E ROTATE THE ARENA  //  MOVEMENT STAYS SCREEN-RELATIVE",
            _ => "TAB OPENS DETAILS  //  ESC PAUSES AND OPENS COMFORT SETTINGS",
        };
        float scale = UiTheme.DisplayScale(spriteBatch);
        int width = (int)Math.Min(InformationSheet.ArenaWidth * .72f, 760 * scale);
        var rect = new Rectangle((InformationSheet.ArenaWidth - width) / 2, (int)(ScreenHeight - 58 * scale), width, (int)(38 * scale));
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
        var intersections = new List<float>();
        if (direction.X > 0)
            intersections.Add((viewport.Right - origin.X) / direction.X);
        else if (direction.X < 0)
            intersections.Add((viewport.Left - origin.X) / direction.X);
        if (direction.Y > 0)
            intersections.Add((viewport.Bottom - origin.Y) / direction.Y);
        else if (direction.Y < 0)
            intersections.Add((viewport.Top - origin.Y) / direction.Y);
        var positive = intersections.Where(value => value > 0).ToList();
        if (positive.Count == 0)
            return null;
        float edgeDistance = positive.Min();
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

    /// <summary>Convenience wrapper matching MovePlayer/DrawPlayer's shape: call once per frame, before <see cref="HandleInformationSheetDrag"/>.</summary>
    public void DrawInformationSheet(SpriteBatch spriteBatch, Point mousePosition) =>
        InformationSheet.DrawSheet(spriteBatch, State, PlayerWorldCenter, SelectBountyTarget(), mousePosition, PathRun);

    public SidebarAction HandleInformationSheetAction(Point mousePosition, bool mousePressed) =>
        InformationSheet.HandleAction(State, mousePosition, mousePressed);

    /// <summary>Convenience wrapper: call once per frame, after <see cref="DrawInformationSheet"/>.</summary>
    public void HandleInformationSheetDrag(Point mousePosition, bool mouseDown, bool mousePressed) =>
        InformationSheet.HandleDrag(State, PlayerWorldCenter, mousePosition, mouseDown, mousePressed);

    /// <summary>The Soul's counterpart to DrawInformationSheet/HandleInformationSheetDrag -- see InformationSheet.DrawCarriedLoadout's doc comment.</summary>
    public void DrawCarriedLoadout(SpriteBatch spriteBatch, Point mousePosition) =>
        InformationSheet.DrawCarriedLoadout(spriteBatch, State, mousePosition);

    /// <summary>Call once per frame, after <see cref="DrawCarriedLoadout"/>, while in the Soul.</summary>
    public void HandleCarriedLoadoutDrag(Point mousePosition, bool mouseDown, bool mousePressed, IReadOnlyList<Rectangle> vaultSlotRects) =>
        InformationSheet.HandleDrag(State, PlayerWorldCenter, mousePosition, mouseDown, mousePressed, vaultSlotRects, allowWorldDrop: false);

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
            State.HealthPoints = Math.Max(0, State.HealthPoints - (int)trueDamage);
            _bossTelemetry?.RecordDamage(healthBeforeHit - State.HealthPoints);
            State.PlayerInvulnerabilityTimer = State.PlayerInvulnerabilityMax;
            return State.HealthPoints <= 0 ? FinalizeDefeat() : false;
        }

        foreach (var enemy in State.EnemyHolster)
        {
            if (ReferenceEquals(enemy, State.ActiveBoss) && DeathSpectacleActive(enemy))
                continue;
            var hitboxes = enemy.GetScreenHitboxes(Camera, PlayerWorldCenter, ScreenShake);
            Rectangle? collidedHitbox = null;
            foreach (var (_, hitbox) in hitboxes)
            {
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
            State.HealthPoints = Math.Max(0, State.HealthPoints - (int)trueDamage);
            _bossTelemetry?.RecordDamage(healthBeforeHit - State.HealthPoints);
            State.PlayerInvulnerabilityTimer = State.PlayerInvulnerabilityMax;

            float deltaX = hitbox2.Center.X - playerScreenRect.Center.X, deltaY = hitbox2.Center.Y - playerScreenRect.Center.Y;
            float distance = Math.Max(1f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
            var knockback = Camera.ScreenVectorToWorld(new Vector2(
                deltaX / distance * Simulation.TileSize * 0.8f, deltaY / distance * Simulation.TileSize * 0.8f));
            enemy.ApplyKnockback(knockback.X, knockback.Y, Battleground);

            return State.HealthPoints <= 0 ? FinalizeDefeat() : false;
        }
        return false;
    }

    private bool FinalizeDefeat()
    {
        CompleteBossTelemetry(victory: false);
        State.RunOutcome = "DEFEATED";
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
        State.RecordUpgrade(card.Name, card.Rarity, card.MathType);
        double modifier = Upgrades.CardModifier(card);
        if (card.MathType == "additive")
            State.Stats[card.Name].Additive.Add(modifier);
        else
            State.Stats[card.Name].Multiplicative.Add(modifier);
        State.CombinePlayerStats();
        if (State.HardMode)
            State.FillHealthForMilestone();
        State.NewRandoUps = false;
        State.PendingLevelUps = Math.Max(0, State.PendingLevelUps - 1);
        if (State.PendingLevelUps > 0)
            return LevelUpOutcome.ContinueLeveling;
        State.GracePeriod = Simulation.FrameRate * 2;
        return LevelUpOutcome.ReturnToGame;
    }
}
