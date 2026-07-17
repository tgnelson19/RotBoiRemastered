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
/// Explicitly deferred (all documented per-method below, not silently
/// dropped): every boss-specific branch (bossTypes.py doesn't exist yet --
/// natural Beaudis/Dissonance triggers are detected but don't spawn
/// anything; boss arena constraints, arena-radius projectile clipping, and
/// the debug phase-control hotkeys are all absent); gamePaths.py's
/// per-path enemy identity/projectile tuning (`apply_enemy_identity`/
/// `tune_new_projectiles`) -- enemies spawn via `EnemyCatalog.Shared`
/// directly rather than gamePaths.py's thin per-path wrapper around it;
/// portal-hit bullet routing (no current enemy type implements it); and the
/// HUD-overlay draw functions character.py layers on top of the sidebar
/// (bounty indicator *arrow* rendering, boss health bar, tutorial hints,
/// low-health warning, run-complete banner) and the title screen -- these
/// are separate character.py functions from `InformationSheet.cs` itself
/// (now ported, see UI/README.md) and remain deferred for their own sake,
/// not because `InformationSheet.arena_width` was missing. Loot-crate
/// viewport clipping against the HUD sidebar is also still deferred
/// (crates draw across the full screen for now).
/// </summary>
public sealed class GameSession
{
    private const int CrateInteractRadius = 24;
    private const int MaxLootCrates = 40;
    private const double HostileMinDamage = 25;
    private const double HostileDamageFloorRatio = .1;

    public RunState State { get; } = new();
    public Player Player { get; private set; }
    public Battleground Battleground { get; private set; }
    public Camera Camera { get; } = new();
    public LevelingHandler LevelingHandler { get; private set; }
    public InformationSheet InformationSheet { get; private set; }
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }
    public Vector2 ScreenShake { get; set; } = Vector2.Zero;

    private Vector2 PlayerWorldPosition => new(Player.WorldX, Player.WorldY);

    /// <summary>
    /// Screen-height-derived default awareness range, matching the value
    /// Python's Enemy.__init__ used to compute internally from `vH.sH`
    /// before that became an explicit constructor parameter (see
    /// Entities/Enemy.cs's cleanup notes).
    /// </summary>
    public float AwarenessRange => ScreenHeight * .5f;

    /// <summary>Matches variableHolster.py's damageTextSize, scaled the same way uiTheme.display_scale is.</summary>
    public double DamageTextFontSize => Math.Max(9, Math.Round(40 * Math.Clamp(Math.Min(ScreenWidth / 1024.0, ScreenHeight / 768.0), .7, 3.2)));

    public GameSession(Battleground battleground, int screenWidth, int screenHeight, Random? rng = null)
    {
        Battleground = battleground;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Player = new Player(battleground.SpawnPosition.X, battleground.SpawnPosition.Y);
        LevelingHandler = new LevelingHandler(screenWidth, screenHeight, rng);
        InformationSheet = new InformationSheet(screenWidth, screenHeight);
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, screenHeight / 2f);
    }

    /// <summary>Ported from character.py's resetAllStats() (the parts not already covered by RunState.Reset()).</summary>
    public void ResetAll(Battleground battleground, Random? rng = null)
    {
        State.Reset();
        Battleground = battleground;
        Player = new Player(battleground.SpawnPosition.X, battleground.SpawnPosition.Y);
        Camera.SetAngle(0);
        ScreenShake = Vector2.Zero;
        LevelingHandler = new LevelingHandler(ScreenWidth, ScreenHeight, rng);
        InformationSheet = new InformationSheet(ScreenWidth, ScreenHeight);
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, ScreenHeight / 2f);
    }

    public void Resize(int screenWidth, int screenHeight)
    {
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        LevelingHandler.UpdateLayout(screenWidth, screenHeight);
        InformationSheet.SyncLayout(screenWidth, screenHeight);
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, screenHeight / 2f);
    }

    /// <summary>Ported from informationSheet.py's toggle_mode(), plus the Camera re-centering character.py's caller performs alongside it.</summary>
    public void ToggleHudMode()
    {
        InformationSheet.ToggleMode();
        Camera.Lock = new Vector2(InformationSheet.ArenaWidth / 2f, ScreenHeight / 2f);
    }

    // ----- Player movement/combat -----

    /// <summary>Ported from character.py's movePlayer().</summary>
    /// <summary>
    /// Ported from character.py's movePlayer(). The `movement_obstacles`
    /// branch (PathChaseBoss-family arena shapes) stays deferred with that
    /// family; the `arenaRadius` branch is wired now that Dissonance exists
    /// -- done here rather than inside Player.Move so Player.cs stays
    /// boss-agnostic (see its own doc comment).
    /// </summary>
    public void MovePlayer(bool moveLeft, bool moveRight, bool moveUp, bool moveDown, bool dashPressed)
    {
        Player.Move(State, Battleground, Camera, moveLeft, moveRight, moveUp, moveDown, dashPressed);
        if (State.ActiveBoss is Dissonance dissonance)
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
    }

    public void DrawPlayer(SpriteBatch spriteBatch) => Player.Draw(spriteBatch, State, Camera);

    /// <summary>Ported from character.py's handlingBulletCreation(). Controller aiming isn't wired up yet -- mouse aim only.</summary>
    public void HandleBulletCreation(Vector2 mouseScreenPosition, bool mouseDown, bool dragInProgress, Random? rng = null)
    {
        rng ??= Random.Shared;
        if (State.AttackCooldownTimer <= 0 && !dragInProgress && (State.AutoFire || mouseDown))
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

            float screenOriginX = Camera.Lock.X + (float)State.PlayerSize / 2f, screenOriginY = Camera.Lock.Y + (float)State.PlayerSize / 2f;
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
                    State.BulletColor, currPierce, (float)currDamage, currCrit));
            }
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
            bullet.Draw(spriteBatch, Camera, PlayerWorldPosition, ScreenShake);
    }

    // ----- Enemies -----

    /// <summary>
    /// Ported from character.py's handlingEnemyCreation(). The natural
    /// Beaudis/Dissonance triggers now really spawn (matching Python's
    /// "else" branch resolving to the final-boss content key for every
    /// trigger reason that isn't the natural Beaudis one -- including the
    /// hidden debug-summon hotkey, which therefore also spawns Dissonance,
    /// never Beaudis, exactly like Python). Simplified vs. Python:
    /// gamePaths.py's path-based boss-content-key lookup isn't ported, so
    /// this always resolves to the "sound" path's bosses directly rather
    /// than asking gamePaths which boss counts as "the mid/final boss" on
    /// the active path.
    /// </summary>
    public void HandleEnemyCreation(Random? rng = null)
    {
        rng ??= Random.Shared;
        bool naturalBeaudisRequested = State.CurrentLevel >= Progression.MidBossLevel && !State.BeaudisEncounterStarted && State.ActiveBoss is null;
        bool naturalDissonanceRequested = State.CurrentLevel >= Progression.FinalBossLevel && State.BeaudisDefeated && !State.DissonanceEncounterStarted && State.ActiveBoss is null;
        if (State.BossDebugRequested || naturalBeaudisRequested || naturalDissonanceRequested)
        {
            bool naturalEncounter = !State.BossDebugRequested;
            if (naturalBeaudisRequested && naturalEncounter)
            {
                State.BeaudisEncounterStarted = true;
                SpawnBoss((x, y, r) => new Beaudis(x, y, AwarenessRange, r), rng);
            }
            else
            {
                if (naturalDissonanceRequested && naturalEncounter)
                    State.DissonanceEncounterStarted = true;
                float arenaX = Battleground.Width * Simulation.TileSize / 2f;
                float arenaY = Battleground.Height * Simulation.TileSize / 2f;
                float size = Simulation.TileSize * 1.9f;
                var forcedRect = new Rectangle((int)(arenaX - size / 2f), (int)(arenaY - size / 2f), (int)size, (int)size);
                SpawnBoss((x, y, r) => new Dissonance(x, y, AwarenessRange, Battleground, r), rng, forcedRect);
                var playerSpawn = Battleground.FindNearestOpenRect(new Rectangle(
                    (int)(arenaX - State.PlayerSize / 2f), (int)(arenaY + Simulation.TileSize * 9.6f - State.PlayerSize / 2f),
                    (int)State.PlayerSize, (int)State.PlayerSize));
                Player.SetPosition(playerSpawn.X, playerSpawn.Y);
            }
            State.BossDebugRequested = false;
            return;
        }

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
                var miniboss = EnemyCatalog.Shared.Spawn(State.CurrentLevel, Battleground, PlayerWorldPosition, AwarenessRange,
                    rng, key: key, minDistanceTiles: outsideAwarenessTiles);
                if (miniboss is not null)
                    State.EnemyHolster.Add(miniboss);
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
                    PlayerWorldPosition, AwarenessRange, ScreenHeight, State.EnemyHolster, rng);
                if (curated.HasValue)
                    encounterResult = (curated.Value.Package.Key, curated.Value.Group);
            }
            if (encounterResult is null)
            {
                var patrol = EnemyCatalog.Shared.SpawnPatrol(State.CurrentLevel, remainingThreat, Battleground,
                    PlayerWorldPosition, AwarenessRange, ScreenHeight, State.EnemyHolster, rng);
                if (patrol.HasValue)
                    encounterResult = (patrol.Value.Encounter.Key, patrol.Value.Group);
            }
            if (encounterResult.HasValue)
            {
                var (key, group) = encounterResult.Value;
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
    private void SpawnBoss(Func<float, float, Random, Enemy> factory, Random rng, Rectangle? forcedSpawnRect = null)
    {
        State.EnemyHolster.Clear();
        State.EnemyProjectileHolster.Clear();
        State.DamageTextList.Clear();
        State.ExperienceList.Clear();
        State.LootCrateList.Clear();
        State.NearbyCrate = null;

        Rectangle spawnRect;
        if (forcedSpawnRect.HasValue)
        {
            spawnRect = forcedSpawnRect.Value;
        }
        else
        {
            float footprint = Simulation.TileSize * 1.9f;
            float centerX = Battleground.Width * Simulation.TileSize / 2f;
            float centerY = Battleground.Height * Simulation.TileSize / 2f;
            var requested = new Rectangle((int)(centerX - footprint / 2f), (int)(centerY - footprint / 2f), (int)footprint, (int)footprint);
            spawnRect = Battleground.FindNearestOpenRect(requested);
        }

        var boss = factory(spawnRect.X, spawnRect.Y, rng);
        State.EnemyHolster.Add(boss);
        State.ActiveBoss = boss;
        State.BossDebugInvincible = false;
        State.CurrEnemyCount = 1;
        State.EnemySpawningEnabled = false;
        State.GracePeriod = Simulation.FrameRate * 2;
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
        };
        foreach (var enemy in State.EnemyHolster)
        {
            enemy.Update(context);
            // gamePaths.py's tune_new_projectiles (per-path projectile tuning) is deferred.
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
                // gamePaths.py's apply_enemy_identity (per-path flavor) is deferred.
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

        // Ported from Dissonance._update_visuals's `vH.screenShakeX/Y` global write --
        // computed here instead and assigned to this session's own ScreenShake, matching
        // this port's "explicit parameter over hidden global" convention (see Dissonance.cs).
        if (State.ActiveBoss is Dissonance dissonance)
            ScreenShake = dissonance.ComputeScreenShake(GameProfile.Profile.ScreenShake);
    }

    public void DrawEnemies(SpriteBatch spriteBatch)
    {
        var seen = new HashSet<int>();
        foreach (var enemy in State.EnemyHolster)
        {
            var encounter = enemy.Encounter;
            if (encounter is not null && seen.Add(encounter.Id))
                encounter.Draw(spriteBatch, Camera, PlayerWorldPosition, ScreenShake);
            enemy.Draw(spriteBatch, Camera, PlayerWorldPosition, ScreenShake);
        }
    }

    /// <summary>Ported from character.py's handlingEnemyProjectileUpdating(). Boss arena-radius clipping and the >150 overflow trim are boss-only, deferred.</summary>
    public void UpdateEnemyProjectiles()
    {
        var spawnedProjectiles = new List<EnemyProjectile>();
        bool casualMode = GameProfile.Profile.CasualMode;
        foreach (var projectile in State.EnemyProjectileHolster)
        {
            projectile.Update(Battleground, casualMode);
            spawnedProjectiles.AddRange(projectile.SpawnedProjectiles);
            projectile.SpawnedProjectiles.Clear();
        }
        State.EnemyProjectileHolster.RemoveAll(p => p.RemFlag);
        State.EnemyProjectileHolster.AddRange(spawnedProjectiles);
    }

    public void DrawEnemyProjectiles(SpriteBatch spriteBatch)
    {
        bool highContrast = GameProfile.Profile.HighContrast;
        foreach (var projectile in State.EnemyProjectileHolster)
            projectile.Draw(spriteBatch, Camera, PlayerWorldPosition, ScreenShake, highContrast);
    }

    /// <summary>Ported from character.py's handlingDamagingEnemies(). Portal-hit routing is deferred (no current enemy type implements it).</summary>
    public void HandleDamagingEnemies(Random? rng = null)
    {
        rng ??= Random.Shared;
        var enemyGrid = new SpatialHash<Enemy>(Math.Max(64, (int)(Simulation.TileSize * 2)));
        foreach (var enemy in State.EnemyHolster)
            foreach (var (_, hitbox) in enemy.GetScreenHitboxes(Camera, PlayerWorldPosition, ScreenShake))
                enemyGrid.Insert(enemy, hitbox);

        var deadEnemies = new HashSet<Enemy>();
        foreach (var bullet in State.BulletHolster)
        {
            var bulletScreenPos = Camera.WorldToScreen(new Vector2(bullet.WorldX, bullet.WorldY), PlayerWorldPosition, ScreenShake);
            var bulletRect = new Rectangle((int)bulletScreenPos.X, (int)bulletScreenPos.Y, (int)bullet.Size, (int)bullet.Size);
            var candidates = enemyGrid.Query(bulletRect)
                .Select(enemy => (Enemy: enemy, HasShield: enemy.GetScreenHitboxes(Camera, PlayerWorldPosition, ScreenShake)
                    .Any(h => h.Part == "shield" && bulletRect.Intersects(h.Rect))))
                .OrderBy(item => item.HasShield ? 0 : 1)
                .Select(item => item.Enemy)
                .ToList();

            foreach (var enemy in candidates)
            {
                if (deadEnemies.Contains(enemy))
                    continue;
                var hitboxes = enemy.GetScreenHitboxes(Camera, PlayerWorldPosition, ScreenShake);
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
                var result = enemy.TakeDamage(bullet.Damage, collided.Part);
                if (enemy.TransitionCleanupRequested)
                {
                    if (enemy.TransitionCleanupOwner is not null)
                        State.EnemyProjectileHolster.RemoveAll(p => p.Owner == enemy.TransitionCleanupOwner);
                    else
                        State.EnemyProjectileHolster.Clear();
                    enemy.TransitionCleanupRequested = false;
                }
                Color currColor = bullet.IsCritical ? UiTheme.Purple : UiTheme.Gold;
                object displayValue = result.Applied ? Math.Round(bullet.Damage) : "BLOCK";
                var textWorld = Camera.ScreenToWorld(new Vector2(collided.Rect.X, collided.Rect.Y), PlayerWorldPosition, ScreenShake);
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
            State.ExperienceList.Add(new ExperienceBubble(
                enemy.WorldX, enemy.WorldY,
                State.XpMult * (enemy.ExpValue * (State.CurrentStage * State.ExperienceStageMod)),
                enemy.Difficulty, rng, celebration: ReferenceEquals(enemy, State.ActiveBoss)));

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

            int dropCount = Items.RollDropCount(rng);
            if (dropCount > 0)
            {
                var crate = new LootCrate(enemy.WorldX, enemy.WorldY, Items.GenerateDrops(dropCount, rng));
                State.LootCrateList.Add(crate);
                if (State.LootCrateList.Count > MaxLootCrates)
                {
                    var evictable = State.LootCrateList.FirstOrDefault(c => c != State.NearbyCrate);
                    if (evictable is not null)
                        State.LootCrateList.Remove(evictable);
                }
            }

            if (ReferenceEquals(enemy, State.ActiveBoss))
            {
                // Ported from character.py's per-content-key outcome branch, simplified to a
                // direct type check since gamePaths.py's path-based boss-content-key lookup
                // (which boss variant counts as "the mid boss"/"the final boss" on the active
                // path) isn't ported -- only the "sound" path's bosses (Beaudis/Dissonance) exist.
                if (enemy is Beaudis)
                {
                    State.BeaudisDefeated = true;
                }
                else if (enemy is Dissonance)
                {
                    State.GameCompleted = true;
                    State.RunOutcome = "RUN COMPLETE";
                    GameProfile.RecordRun(State.CurrentLevel, State.NumOfEnemiesKilled, completed: true);
                }
                State.ActiveBoss = null;
                State.EnemySpawningEnabled = !State.GameCompleted;
                ScreenShake = Vector2.Zero;
                State.EnemyProjectileHolster.Clear();
            }
        }
        if (deadEnemies.Count > 0)
        {
            State.EnemyHolster.RemoveAll(e => deadEnemies.Contains(e));
            State.CurrEnemyCount = State.EnemyHolster.Count;
        }
        State.BulletHolster.RemoveAll(b => b.RemFlag);
    }

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
            text.Draw(spriteBatch, Camera, PlayerWorldPosition, ScreenShake, DamageTextFontSize);
    }

    /// <summary>Ported from character.py's updateExperience().</summary>
    public void UpdateExperience()
    {
        foreach (var bubble in State.ExperienceList)
            bubble.Update((float)State.AuraSpeed, Battleground);
    }

    public void DrawExperience(SpriteBatch spriteBatch)
    {
        foreach (var bubble in State.ExperienceList)
            bubble.Draw(spriteBatch, Camera, PlayerWorldPosition, ScreenShake);
    }

    /// <summary>Ported from character.py's expForPlayer(). Returns true if a level-up was triggered (caller should switch to the Leveling state).</summary>
    public bool ExpForPlayer()
    {
        var playerRect = Player.WorldRect(State);
        bool enteredLeveling = false;
        foreach (var bubble in State.ExperienceList.ToList())
        {
            var bubbleRect = bubble.WorldRect();
            if (playerRect.Intersects(bubbleRect))
            {
                State.ExpCount += bubble.Value;
                while (State.CurrentLevel < Progression.MaxLevel && State.ExpCount >= State.ExpNeededForNextLevel)
                {
                    State.CurrentLevel += 1;
                    State.PendingLevelUps += 1;
                    State.ExpCount -= State.ExpNeededForNextLevel;
                    State.ExpNeededForNextLevel *= State.LevelScaleIncreaseFunction;
                    State.HealthPoints = State.MaxHealthPoints;
                    enteredLeveling = true;
                }
                if (State.CurrentLevel >= Progression.MaxLevel)
                    State.ExpCount = Math.Min(State.ExpCount, State.ExpNeededForNextLevel);
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
        return enteredLeveling;
    }

    /// <summary>Dev/testing hotkey. Ported from character.py's debugForceLevelUp().</summary>
    public void DebugForceLevelUp(Random? rng = null) =>
        State.ExperienceList.Add(new ExperienceBubble(Player.WorldX, Player.WorldY, State.ExpNeededForNextLevel, 1, rng));

    private static readonly Keys[] BossDebugPhaseKeys =
        { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9 };

    /// <summary>
    /// Dev/testing hotkeys. Ported from character.py's handlingBossDebugControls().
    /// `boss.debug_set_phase`/`hasattr(boss, "runeCannonCooldown")` were duck-typed
    /// across every boss type in Python -- pattern-matched against `Beaudis`
    /// here since `ActiveBoss` is untyped and it's the only boss ported so
    /// far, so the "C" rune-cannon hotkey (Dissonance-only in Python) is a
    /// no-op until that boss exists.
    /// </summary>
    public void HandleBossDebugControls(IReadOnlySet<Keys> keysPressed)
    {
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
    }

    /// <summary>Ported from character.py's updateLootCrates(). Viewport clipping against the (not yet built) HUD sidebar is deferred.</summary>
    public void DrawLootCrates(SpriteBatch spriteBatch)
    {
        foreach (var crate in State.LootCrateList)
            crate.Draw(spriteBatch, Camera, PlayerWorldPosition, ScreenShake);
    }

    /// <summary>Ported from character.py's crateInteractionForPlayer(). The drag-in-progress guard is dropped (InformationSheet's drag UI is deferred).</summary>
    public void UpdateCrateInteraction()
    {
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
        if (State.ActiveBoss is Enemy boss && !boss.IsDead())
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
            if (enemy.IsDead())
                continue;
            var encounter = enemy.Encounter;
            if (encounter is not null)
            {
                if (!seenEncounters.Add(encounter.Id))
                    continue;
                var living = encounter.Members.Where(member => !member.IsDead()).ToList();
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

    /// <summary>Convenience wrapper matching MovePlayer/DrawPlayer's shape: call once per frame, before <see cref="HandleInformationSheetDrag"/>.</summary>
    public void DrawInformationSheet(SpriteBatch spriteBatch, Point mousePosition) =>
        InformationSheet.DrawSheet(spriteBatch, State, PlayerWorldPosition, SelectBountyTarget(), mousePosition);

    /// <summary>Convenience wrapper: call once per frame, after <see cref="DrawInformationSheet"/>.</summary>
    public void HandleInformationSheetDrag(Point mousePosition, bool mouseDown, bool mousePressed) =>
        InformationSheet.HandleDrag(State, PlayerWorldPosition, mousePosition, mouseDown, mousePressed);

    // ----- Health -----

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

        var playerScreenRect = new Rectangle((int)Camera.Lock.X, (int)Camera.Lock.Y, (int)State.PlayerSize, (int)State.PlayerSize);
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
            // Boss-affliction projectiles (bossTypes.py) are deferred.
            if (!projectile.PersistentHazard)
                projectile.RemFlag = true;
            double trueDamage = HostileDamageAfterDefense(projectile.Damage, State.Defense);
            if (casualMode)
                trueDamage = Math.Round(trueDamage * .8);
            State.DamageTextList.Add(new DamageText(Player.WorldX, Player.WorldY, UiTheme.Red, trueDamage, Simulation.TileSize, Simulation.FrameRate));
            State.HealthPoints = Math.Max(0, State.HealthPoints - (int)trueDamage);
            State.PlayerInvulnerabilityTimer = State.PlayerInvulnerabilityMax;
            return State.HealthPoints <= 0 ? FinalizeDefeat() : false;
        }

        foreach (var enemy in State.EnemyHolster)
        {
            var hitboxes = enemy.GetScreenHitboxes(Camera, PlayerWorldPosition, ScreenShake);
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
            double trueDamage = HostileDamageAfterDefense(enemy.Damage, State.Defense);
            if (casualMode)
                trueDamage = Math.Round(trueDamage * .8);
            State.DamageTextList.Add(new DamageText(Player.WorldX, Player.WorldY, UiTheme.Red, trueDamage, Simulation.TileSize, Simulation.FrameRate));
            State.HealthPoints = Math.Max(0, State.HealthPoints - (int)trueDamage);
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
        State.NewRandoUps = false;
        State.PendingLevelUps = Math.Max(0, State.PendingLevelUps - 1);
        if (State.PendingLevelUps > 0)
            return LevelUpOutcome.ContinueLeveling;
        State.GracePeriod = Simulation.FrameRate * 2;
        return LevelUpOutcome.ReturnToGame;
    }
}
