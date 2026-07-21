using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

/// <summary>Ported from characterStats.py's `upgradeCollection["history"]` entries.</summary>
public sealed record UpgradeHistoryEntry(string Name, string Rarity, string MathType);

/// <summary>
/// Ported from characterStats.py's `player_build_snapshot()` return value
/// (an immutable summary bosses may inspect without mutating the build --
/// used by `Entities/Rot.cs`'s Envy phase). `MappingProxyType`'s
/// immutability guarantee is naturally covered by using `IReadOnlyDictionary`.
/// </summary>
public sealed record PlayerBuildSnapshot(
    IReadOnlyDictionary<string, int> Types,
    IReadOnlyDictionary<string, int> Categories,
    IReadOnlyDictionary<string, double> Stats,
    string DominantOffense);

/// <summary>
/// Belief/clarity meta-progression toward a run's dream-logic content.
/// Ported from characterStats.py's `dreamState` dict + its free functions
/// (`alter_belief`/`update_dream_state`) -- module-level dict and functions
/// become instance state and methods on one class, same cleanup as every
/// other stateful module ported so far. Phantasia projectiles can now supply
/// nonzero `EnemyProjectile.BeliefGain`/`ClarityGain`; encounters that do not
/// use dream rules leave those values at zero.
/// </summary>
public sealed class DreamState
{
    public double Belief { get; private set; }
    public double Clarity { get; private set; }
    public double PeakBelief { get; private set; }
    public double DecayDelay { get; private set; }
    public int FalseRules { get; private set; }
    public int TruthsRead { get; private set; }

    public void Reset()
    {
        Belief = 0; Clarity = 0; PeakBelief = 0; DecayDelay = 0; FalseRules = 0; TruthsRead = 0;
    }

    public void AlterBelief(double amount, bool falseRule = false, bool truth = false)
    {
        Belief = Math.Clamp(Belief + amount, 0.0, 10.0);
        PeakBelief = Math.Max(PeakBelief, Belief);
        if (amount > 0)
            DecayDelay = 2.0;
        if (falseRule)
            FalseRules += 1;
        if (truth)
        {
            TruthsRead += 1;
            Clarity = Math.Min(5.0, Clarity + 1.0);
        }
    }

    public void Update(double seconds)
    {
        DecayDelay = Math.Max(0.0, DecayDelay - seconds);
        Clarity = Math.Max(0.0, Clarity - seconds * .18);
        if (DecayDelay <= 0)
        {
            double decay = .3 + Clarity * .12;
            Belief = Math.Max(0.0, Belief - seconds * decay);
        }
    }
}

/// <summary>
/// Reusable movement afflictions (slow/pull) bosses can apply without
/// permanently changing player stats. Ported from characterStats.py's
/// `bossAfflictions` dict + its free functions. Same cleanup as
/// <see cref="DreamState"/>. Meaningful once boss content sets these via
/// hurtPlayer's projectile-affliction handling (deferred with bossTypes.py);
/// <see cref="MovementMultiplier"/> degrades to 1.0 with nothing active.
/// </summary>
public sealed class BossAfflictions
{
    public double Exposure { get; private set; }
    public double DecayDelay { get; private set; }
    public double Slow { get; private set; }
    public double SlowRemaining { get; private set; }
    public double Pull { get; private set; }
    public double PullRemaining { get; private set; }
    public Vector2? PullSource { get; private set; }

    public void Reset()
    {
        Exposure = 0; DecayDelay = 0; Slow = 0; SlowRemaining = 0; Pull = 0; PullRemaining = 0; PullSource = null;
    }

    public void Apply(string kind, double duration = 0, double strength = 0, double exposure = 0, Vector2? source = null)
    {
        Exposure = Math.Min(10.0, Exposure + exposure);
        DecayDelay = Math.Max(DecayDelay, 2.25);
        if (kind == "slow")
        {
            Slow = Math.Max(Slow, strength);
            SlowRemaining = Math.Max(SlowRemaining, duration);
        }
        else if (kind == "pull")
        {
            Pull = Math.Max(Pull, strength);
            PullRemaining = Math.Max(PullRemaining, duration);
            PullSource = source;
        }
    }

    public void Update(double seconds)
    {
        SlowRemaining = Math.Max(0.0, SlowRemaining - seconds);
        PullRemaining = Math.Max(0.0, PullRemaining - seconds);
        DecayDelay = Math.Max(0.0, DecayDelay - seconds);
        if (SlowRemaining <= 0)
            Slow = 0.0;
        if (PullRemaining <= 0)
        {
            Pull = 0.0;
            PullSource = null;
        }
        if (DecayDelay <= 0)
            Exposure = Math.Max(0.0, Exposure - seconds * .8);
    }

    public double MovementMultiplier()
    {
        double exposurePenalty = Exposure * .025;
        return Math.Max(.58, 1.0 - exposurePenalty - Slow);
    }
}

/// <summary>
/// One run's mutable state: derived combat stats, level/exp progression,
/// entity holsters, equipment, and upgrade/dream/affliction tracking.
/// Ported from characterStats.py.
///
/// Cleanup vs. the Python original:
/// - The three parallel `collectiveStats`/`collectiveAddStats`/
///   `collectiveMultStats` dicts become one `Stats` dict of
///   <see cref="StatTrack"/> keyed by the same upgrade-stat names.
/// - `nearby_crate`/`dragging_source` lived on `informationSheet.py`'s
///   `InformationSheet` class. `NearbyCrate` lives directly on RunState
///   instead, since it's genuinely run state (which crate the player is
///   standing near), not a HUD concern; `dragging_source`/`dragging_item`
///   stay on `UI/InformationSheet.cs` itself (see UI/README.md), since
///   those are purely drag-gesture UI state.
/// - Dropped fields that were assigned but never read anywhere in the repo
///   (verified by grep): `baseExpNeededForNextLevel`, `enemyOneInFramesChance`
///   (divided into itself every level-up but the result never consumed),
///   `currTileX`/`currTileY`, `autoFlop`, `lastUpgrade`/`lastUpgradeAt`.
///   `playerRect` is gone too -- it was a screen-space rect recomputed from
///   the camera lock position every frame purely for drawing, which
///   `Player.Draw` now computes on demand instead of caching.
/// - `bossDebugRequested`/`bossDebugInvincible` are dropped for now --
///   they only matter for `handlingBossDebugControls`, which has no boss to
///   debug without bossTypes.py. They'll come back with that pass.
/// - `activeBoss` is typed `object?` as a placeholder until bossTypes.py
///   defines a real boss base type.
/// </summary>
public sealed class RunState
{
    public int HighestLevel { get; set; }
    public double RunTimeSeconds { get; set; }
    public string RunOutcome { get; set; } = "DEFEATED";
    public bool HardMode { get; private set; }
    public int NewGamePlusLevel { get; private set; }

    public double PlayerSpeed { get; private set; }
    public float PlayerSize { get; private set; }
    public Color PlayerColor { get; private set; }
    public Color PlayerEdgeColor { get; private set; }

    public double ProjectileCount { get; private set; }
    public double AzimuthalProjectileAngle { get; private set; }
    public double AttackCooldownStat { get; private set; }
    public double AttackCooldownTimer { get; set; }

    public int BulletDamage { get; private set; }
    public double BulletSpeed { get; private set; }
    public double BulletRange { get; private set; }
    public double BulletSize { get; private set; }
    public Color BulletColor { get; private set; }
    public Color BulletEdgeColor { get; private set; }
    public string BulletDesign { get; private set; } = "bulb";
    public double BulletPierce { get; private set; }
    public double CritChance { get; private set; }
    public double CritDamage { get; private set; }

    public double Aura { get; private set; }
    public double AuraSpeed { get; private set; }
    public double LevelMod { get; set; }
    public double XpMult { get; private set; }
    public int CurrentLevel { get; set; }
    public int PendingLevelUps { get; set; }
    public double ExpCount { get; set; }
    public int Fragments { get; set; }
    public double ExpNeededForNextLevel { get; set; }
    public double LevelScaleIncreaseFunction { get; set; }

    public int HealthPoints { get; set; }
    public int MaxHealthPoints { get; private set; }
    public int Vitality { get; private set; }
    public double HealthRecoveryBuffer { get; set; }
    public int Defense { get; private set; }

    public int EnemyCap { get; set; }
    public double EnemyThreatCap { get; set; }
    public double EnemyPopulationThreatCap { get; set; }
    public int CurrEnemyCount { get; set; }
    public double EnemySpawnTimer { get; set; }
    public double EncounterSpawnCooldown { get; set; }

    public int NumOfEnemiesKilled { get; set; }
    public int CurrentStage { get; set; }
    public double ExperienceStageMod { get; set; }

    public double DashDuration { get; set; }
    public bool Dashing { get; set; }
    public double DashModifier { get; set; }
    public double DashCooldownMax { get; set; }
    public double CurrDashCooldown { get; set; }

    public bool AutoFire { get; set; }

    public float DX { get; set; }
    public float DY { get; set; }
    public float FdX { get; set; }
    public float FdY { get; set; }

    public double GracePeriod { get; set; }
    public double PlayerInvulnerabilityTimer { get; set; }
    public double PlayerInvulnerabilityMax { get; set; }

    public List<Bullet> BulletHolster { get; } = new();
    public List<Enemy> EnemyHolster { get; } = new();
    public List<DamageText> DamageTextList { get; } = new();
    public List<ExperienceBubble> ExperienceList { get; } = new();
    public List<FragmentPickup> FragmentList { get; } = new();
    public List<EnemyProjectile> EnemyProjectileHolster { get; } = new();
    public List<LootCrate> LootCrateList { get; } = new();
    public Dictionary<string, ItemDrop?> Equipment { get; private set; } = new();
    /// <summary>Eight general-purpose hoarding slots -- unlike Equipment, never read by CombinePlayerStats, so contents never affect stats.</summary>
    public List<ItemDrop?> Inventory { get; private set; } = new();
    public LootCrate? NearbyCrate { get; set; }

    public object? ActiveBoss { get; set; }
    public bool BeaudisEncounterStarted { get; set; }
    public bool BeaudisDefeated { get; set; }
    public bool DissonanceEncounterStarted { get; set; }
    public bool GameCompleted { get; set; }

    /// <summary>Hidden debug hotkey state (character.py's `bossDebugRequested`/`bossDebugInvincible`), back per Entities/README.md's promise once a boss existed. `BossDebugRequested`'s spawn-branch is unwired for now -- Python's own debug shortcut always summons the *final* boss (Dissonance), not Beaudis, and Dissonance isn't ported yet.</summary>
    public bool BossDebugRequested { get; set; }
    public bool BossDebugInvincible { get; set; }
    public HashSet<string> GuaranteedMiniBossesSpawned { get; private set; } = new();
    public bool EnemySpawningEnabled { get; set; } = true;

    public bool NewRandoUps { get; set; }

    public Dictionary<string, StatTrack> Stats { get; private set; } = new();
    public Dictionary<string, int> UpgradeTypeCounts { get; private set; } = new();
    public Dictionary<string, int> UpgradeRarityCounts { get; private set; } = new();
    public List<UpgradeHistoryEntry> UpgradeHistory { get; private set; } = new();

    public DreamState DreamState { get; } = new();
    public BossAfflictions BossAfflictions { get; } = new();

    public RunState()
    {
        HighestLevel = GameProfile.Profile.BestLevel;
        Reset();
    }

    /// <summary>Ported from character.py's resetAllStats() (the characterStats.py-side fields only -- Player/world reset is Player.cs's job).</summary>
    public void Reset()
    {
        HardMode = GameProfile.Profile.HardModeEnabled;
        NewGamePlusLevel = NewGamePlus.SelectedLevel(GamePaths.Active().Key);
        PlayerSpeed = 2.1;
        PlayerSize = Simulation.TileSize * .75f;
        ApplyCosmetics();

        ProjectileCount = 1;
        AzimuthalProjectileAngle = Math.PI / 8;

        AttackCooldownStat = 40;
        AttackCooldownTimer = 0;

        BulletDamage = 100;
        BulletSpeed = 4;
        BulletRange = 250;
        BulletSize = Simulation.TileSize / 2.0;
        BulletPierce = 1;
        CritChance = 0.05;
        CritDamage = 2;

        Aura = 50;
        AuraSpeed = 2;
        LevelMod = 1.04;
        XpMult = 1;
        CurrentLevel = 0;
        PendingLevelUps = 0;
        ExpCount = 0;
        Fragments = 0;
        ExpNeededForNextLevel = 40;
        LevelScaleIncreaseFunction = 1.15;

        HealthPoints = 1000;
        MaxHealthPoints = 1000;
        Vitality = 25;
        HealthRecoveryBuffer = 0.0;
        Defense = 0;
        CurrEnemyCount = 0;
        EnemyCap = 50;
        EnemyThreatCap = 36.0;
        EnemyPopulationThreatCap = 60.0;
        PlayerInvulnerabilityTimer = 0;
        PlayerInvulnerabilityMax = Simulation.FrameRate * 0.55;
        GracePeriod = Simulation.FrameRate * 1.25;

        EnemySpawnTimer = Simulation.FrameRate * 1.0;
        EncounterSpawnCooldown = 0;

        NumOfEnemiesKilled = 0;
        CurrentStage = 1;
        ExperienceStageMod = 1.1;

        DashDuration = Simulation.FrameRate * 0.15;
        Dashing = false;
        DashModifier = 4;
        DashCooldownMax = Simulation.FrameRate * 1;
        CurrDashCooldown = 0;

        DX = 0; DY = 0; FdX = 0; FdY = 0;

        BulletHolster.Clear();
        EnemyHolster.Clear();
        DamageTextList.Clear();
        ExperienceList.Clear();
        FragmentList.Clear();
        EnemyProjectileHolster.Clear();
        LootCrateList.Clear();
        NearbyCrate = null;
        Equipment = new Dictionary<string, ItemDrop?>
        {
            ["weapon"] = null, ["armor"] = null, ["ring"] = null, ["accessory_1"] = null, ["accessory_2"] = null,
        };
        Inventory = Enumerable.Repeat<ItemDrop?>(null, 8).ToList();
        ActiveBoss = null;
        BeaudisEncounterStarted = false;
        BeaudisDefeated = false;
        DissonanceEncounterStarted = false;
        GameCompleted = false;
        BossDebugRequested = false;
        BossDebugInvincible = false;
        RunTimeSeconds = 0.0;
        RunOutcome = "DEFEATED";
        GuaranteedMiniBossesSpawned = new HashSet<string>();
        EnemySpawningEnabled = true;
        AutoFire = GameProfile.Profile.AutoFire;

        ResetUpgradeTracking();
        BossAfflictions.Reset();
        DreamState.Reset();

        NewRandoUps = false;

        Stats = new Dictionary<string, StatTrack>
        {
            ["Defense"] = new(Defense),
            ["Health"] = new(MaxHealthPoints),
            ["Vitality"] = new(Vitality),
            ["Bullet Pierce"] = new(BulletPierce),
            ["Bullet Count"] = new(ProjectileCount),
            ["Spread Angle"] = new(AzimuthalProjectileAngle),
            ["Attack Speed"] = new(AttackCooldownStat),
            ["Bullet Speed"] = new(BulletSpeed),
            ["Bullet Range"] = new(BulletRange),
            ["Bullet Damage"] = new(BulletDamage),
            ["Bullet Size"] = new(BulletSize),
            ["Player Speed"] = new(PlayerSpeed),
            ["Crit Chance"] = new(CritChance),
            ["Crit Damage"] = new(CritDamage),
            ["Aura Size"] = new(Aura),
            ["Aura Strength"] = new(AuraSpeed),
            ["Exp Multiplier"] = new(XpMult),
        };
        MetaProgression.ApplySkills(this);
        CombinePlayerStats();
        FillHealthForMilestone();
    }

    /// <summary>Used by the Soul challenge station before a new run captures the persisted setting.</summary>
    public void SetHardMode(bool enabled) => HardMode = enabled;

    public void SetNewGamePlusLevel(int level) => NewGamePlusLevel = NewGamePlus.ClampLevel(level);

    /// <summary>The only full restore used by Hard Mode: run start and purchased level-ups.</summary>
    public void FillHealthForMilestone()
    {
        HealthPoints = MaxHealthPoints;
        HealthRecoveryBuffer = 0;
    }

    /// <summary>Refresh gameplay-neutral wardrobe selections without resetting the run.</summary>
    public void ApplyCosmetics()
    {
        PlayerColor = Cosmetics.SelectedCore.Color;
        PlayerEdgeColor = Cosmetics.SelectedEdge.Color;
        BulletColor = Cosmetics.SelectedProjectile.Core;
        BulletEdgeColor = Cosmetics.SelectedProjectile.Edge;
        BulletDesign = Cosmetics.SelectedDesign.Id;
    }

    public void SetEquipment(Dictionary<string, ItemDrop?> equipment)
    {
        foreach (string slot in Equipment.Keys.ToArray())
            Equipment[slot] = equipment.GetValueOrDefault(slot);
        CombinePlayerStats();
    }

    public void ResetUpgradeTracking()
    {
        UpgradeTypeCounts = new Dictionary<string, int>();
        UpgradeRarityCounts = new Dictionary<string, int>();
        UpgradeHistory = new List<UpgradeHistoryEntry>();
    }

    public void RecordUpgrade(string upgradeType, string rarity, string? mathType = null)
    {
        UpgradeTypeCounts[upgradeType] = UpgradeTypeCounts.GetValueOrDefault(upgradeType) + 1;
        UpgradeRarityCounts[rarity] = UpgradeRarityCounts.GetValueOrDefault(rarity) + 1;
        if (mathType is not null)
            UpgradeHistory.Add(new UpgradeHistoryEntry(upgradeType, rarity, mathType));
    }

    /// <summary>
    /// Ported from character.py's recoverPlayerHealth(). The
    /// `boss.vitalitySuppressed` check is dropped -- no boss type exists yet
    /// (see bossTypes.py in Entities/README.md's deferred list) to ever set it.
    /// </summary>
    public void RecoverHealth()
    {
        if (HardMode)
        {
            HealthRecoveryBuffer = 0;
            return;
        }
        if (HealthPoints >= MaxHealthPoints || Vitality <= 0)
        {
            HealthRecoveryBuffer = 0.0;
            return;
        }
        HealthRecoveryBuffer += Vitality * Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        int recovered = (int)HealthRecoveryBuffer;
        if (recovered != 0)
        {
            HealthPoints = Math.Min(MaxHealthPoints, HealthPoints + recovered);
            HealthRecoveryBuffer -= recovered;
        }
    }

    /// <summary>Ported from character.py's combarinoPlayerStats(): recompute derived stats from Stats' current stacks.</summary>
    public void CombinePlayerStats()
    {
        int previousMaxHealth = MaxHealthPoints;

        double Equipped(string stat) => Items.AdjustStat(stat, Stats[stat].Combined, Equipment.Values);

        ProjectileCount = Math.Clamp(Equipped("Bullet Count"), 1, 12);
        AzimuthalProjectileAngle = Stats["Spread Angle"].Combined;
        PlayerSpeed = Equipped("Player Speed");
        AttackCooldownStat = Equipped("Attack Speed");
        BulletSpeed = Equipped("Bullet Speed");
        BulletRange = Equipped("Bullet Range");
        BulletSize = Math.Clamp(Equipped("Bullet Size"), 4, Simulation.TileSize * 2.5);
        BulletDamage = (int)Math.Round(Equipped("Bullet Damage"));
        BulletPierce = Math.Clamp(Equipped("Bullet Pierce"), 1, 8);
        Defense = (int)Math.Round(Equipped("Defense"));
        MaxHealthPoints = Math.Clamp((int)Math.Round(Equipped("Health")), 1, 5000);
        HealthPoints = Math.Min(MaxHealthPoints, HardMode
            ? HealthPoints
            : HealthPoints + Math.Max(0, MaxHealthPoints - previousMaxHealth));
        Vitality = Math.Clamp((int)Math.Round(Equipped("Vitality")), 0, 200);
        CritChance = Math.Clamp(Equipped("Crit Chance"), 0, .85);
        CritDamage = Math.Clamp(Equipped("Crit Damage"), 1, 5);
        Aura = Math.Clamp(Equipped("Aura Size"), 20, 400);
        AuraSpeed = Math.Clamp(Equipped("Aura Strength"), .5, 12);
        XpMult = Math.Clamp(Equipped("Exp Multiplier"), .5, 4);
    }

    /// <summary>Ported from characterStats.py's player_build_snapshot(). Returns an immutable summary bosses may inspect without mutating the build (used by Rot's Envy phase).</summary>
    public PlayerBuildSnapshot BuildSnapshot()
    {
        var categories = new Dictionary<string, int>();
        foreach (var (name, count) in UpgradeTypeCounts)
        {
            if (Upgrades.DefinitionsByName.TryGetValue(name, out var definition))
                categories[definition.Category] = categories.GetValueOrDefault(definition.Category) + count;
        }
        var offenseKeys = new[] { "volley", "tempo", "precision", "power", "critical" };
        var offense = offenseKeys.ToDictionary(key => key, key => categories.GetValueOrDefault(key));
        string dominant = offense.Values.Any(value => value != 0)
            ? offense.OrderByDescending(kv => kv.Value).First().Key
            : "power";
        var stats = new Dictionary<string, double>
        {
            ["projectile_count"] = ProjectileCount,
            ["pierce"] = BulletPierce,
            ["crit_chance"] = CritChance,
            ["crit_damage"] = CritDamage,
            ["bullet_speed"] = BulletSpeed,
            ["bullet_size"] = BulletSize,
        };
        return new PlayerBuildSnapshot(new Dictionary<string, int>(UpgradeTypeCounts), categories, stats, dominant);
    }
}
