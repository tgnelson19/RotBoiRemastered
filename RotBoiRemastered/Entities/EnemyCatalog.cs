using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Registry and spawn rules for every enemy type. Ported from
/// enemyTypes.py's EnemyCatalog + module-level ENEMY_CATALOG/`_register_defaults`.
///
/// Cleanup vs. the Python original: the module-level `ENEMY_CATALOG`
/// singleton, auto-populated by `_register_defaults()` purely as an import
/// side effect, becomes `EnemyCatalog.Shared` -- a lazily-created static
/// field built by an explicit `CreateDefault()` factory. A plain `new
/// EnemyCatalog()` still gives an empty, unregistered catalog (matching
/// `EnemyCatalog.__init__`), useful for tests that want an isolated catalog
/// with only a few hand-registered definitions.
/// </summary>
public sealed class EnemyCatalog
{
    private readonly Dictionary<string, EnemyDefinition> _definitions = new();

    private static readonly IReadOnlyDictionary<string, string[]> RolePairs = new Dictionary<string, string[]>
    {
        ["pressure"] = new[] { "tank", "artillery", "support" },
        ["tank"] = new[] { "pressure", "artillery" },
        ["artillery"] = new[] { "pressure", "tank", "support" },
        ["control"] = new[] { "pressure", "tank" },
        ["support"] = new[] { "pressure", "artillery" },
        ["squad"] = new[] { "control", "artillery" },
        ["economy"] = new[] { "tank", "pressure" },
    };

    public void Register(EnemyDefinition definition)
    {
        if (!_definitions.TryAdd(definition.Key, definition))
            throw new ArgumentException($"Enemy type already registered: {definition.Key}");
    }

    public List<EnemyDefinition> Available(int level) =>
        _definitions.Values.Where(d => d.MinLevel <= level && level <= d.MaxLevel).ToList();

    public EnemyDefinition? Choose(int level, Random? rng = null, double? maxThreat = null, IReadOnlyList<Enemy>? existing = null)
    {
        rng ??= Random.Shared;
        var available = Available(level).Where(d => !d.GuaranteedOnly && d.Family != "banner").ToList();
        if (maxThreat.HasValue)
        {
            var familyCounts = new Dictionary<string, int>();
            foreach (var enemy in existing ?? Array.Empty<Enemy>())
                familyCounts[enemy.Family] = familyCounts.GetValueOrDefault(enemy.Family) + 1;
            available = available.Where(d => d.ThreatCost <= maxThreat.Value && familyCounts.GetValueOrDefault(d.Family) < d.MaxActive).ToList();
        }
        return available.Count == 0 ? null : WeightedChoice(available, available.Select(d => d.Weight).ToList(), rng);
    }

    public EnemyDefinition? DefinitionForFamily(string family, int level)
    {
        var candidates = Available(level).Where(d => d.Family == family && !d.GuaranteedOnly).ToList();
        return candidates.Count == 0 ? null : candidates.OrderByDescending(d => d.MinLevel).First();
    }

    private static RuntimeEncounter AttachEncounter(string key, List<Enemy> group, Vector2 anchor, int level, float screenHeight, Random rng)
    {
        var encounter = new RuntimeEncounter(key, group, anchor, level, screenHeight, rng);
        foreach (var enemy in group)
            enemy.EncounterKey = key;
        return encounter;
    }

    private static List<Enemy> ExpandAtomicMembers(Enemy enemy, string key)
    {
        var group = new List<Enemy> { enemy };
        if (enemy.AtomicSpawnGroup)
        {
            var minions = new List<Enemy>(enemy.SpawnedEnemies);
            enemy.SpawnedEnemies.Clear();
            foreach (var minion in minions)
                minion.EncounterKey = key;
            group.AddRange(minions);
        }
        return group;
    }

    public Enemy ApplyModifier(Enemy enemy, int level, Random? rng = null, string? forced = null)
    {
        rng ??= Random.Shared;
        string role = enemy.CombatRole;
        var eligible = EnemyCatalogData.ModifierRules
            .Where(pair => level >= pair.Value.MinLevel && pair.Value.Roles.Contains(role))
            .Select(pair => pair.Key)
            .ToList();
        if (eligible.Count == 0 || (forced is null && rng.NextDouble() > Math.Min(.28, .06 + level * .011)))
            return enemy;
        string modifier = forced ?? eligible[rng.Next(eligible.Count)];
        if (!eligible.Contains(modifier))
            return enemy;
        enemy.BehaviorModifier = modifier;
        enemy.ModifierColor = EnemyCatalogData.ModifierRules[modifier].Color;
        switch (modifier)
        {
            case "hasty":
                enemy.Speed *= 1.18f;
                if (enemy.AttackCooldownMax.HasValue)
                    enemy.AttackCooldownMax *= .72f;
                enemy.MaxHp = (int)Math.Round(enemy.MaxHp * .82);
                enemy.Hp = enemy.MaxHp;
                enemy.ExpValue *= 1.2;
                break;
            case "armored":
                enemy.MaxHp = (int)Math.Round(enemy.MaxHp * 1.75);
                enemy.Hp = enemy.MaxHp;
                enemy.Speed *= .82f;
                enemy.ExpValue *= 1.45;
                break;
            case "volatile":
                enemy.VolatileBurst = 4 + enemy.TierRank * 2;
                enemy.ExpValue *= 1.3;
                break;
            case "regenerating":
                enemy.RegenerationRate = enemy.MaxHp / (Simulation.FrameRate * 14.0);
                enemy.ExpValue *= 1.35;
                break;
            case "champion":
                enemy.Size *= 1.18f;
                enemy.MaxHp = (int)Math.Round(enemy.MaxHp * 1.55);
                enemy.Hp = enemy.MaxHp;
                enemy.Damage = (int)Math.Round(enemy.Damage * 1.25);
                enemy.ThreatCost *= 1.5;
                enemy.ExpValue *= 2.0;
                break;
        }
        return enemy;
    }

    public Enemy Create(string key, float worldX, float worldY, int level, float awarenessRange, Random? rng = null,
        Battleground? battleground = null)
    {
        rng ??= Random.Shared;
        var definition = _definitions[key];
        var scales = Progression.EnemyStatScales(level);
        var tier = EnemyCatalogData.TierBalance[definition.ProgressionTier];
        float variation = (float)(rng.NextDouble() * (1.12 - .9) + .9);
        double difficulty = rng.NextDouble() * (1.25 - .92) + .92;
        float size = Simulation.TileSize * (float)definition.Size / variation;

        var args = new EnemyConstructionArgs(
            worldX, worldY,
            (float)(EnemyCatalogData.BaseEnemySpeedScale * scales.Speed * tier.Speed * definition.Speed * variation),
            size, definition.Color,
            Math.Round(90 * scales.Damage * tier.Damage * definition.Damage / variation),
            Math.Round(220 * scales.Health * tier.Health * definition.Health / variation),
            2.4 * scales.Experience * tier.Experience * definition.Experience * difficulty,
            difficulty, awarenessRange, definition.ProgressionTier, tier.Rank, rng, battleground);

        var enemy = definition.Factory(args);
        enemy.ThreatCost = definition.ThreatCost * tier.Threat;
        enemy.Family = definition.Family;
        enemy.SpawnDefinitionKey = definition.Key;
        var identity = EnemyCatalogData.FamilyIdentities.GetValueOrDefault(
            definition.Family, new FamilyIdentity("pressure", new HashSet<string>()));
        enemy.CombatRole = identity.CombatRole;
        enemy.InteractionTags = identity.Tags;
        return enemy;
    }

    public Enemy? Spawn(int level, Battleground battleground, Vector2 playerWorldPosition, float awarenessRange,
        Random? rng = null, string? key = null, double? maxThreat = null, IReadOnlyList<Enemy>? existing = null,
        int minDistanceTiles = 4)
    {
        rng ??= Random.Shared;
        var definition = key is not null ? _definitions[key] : Choose(level, rng, maxThreat, existing);
        if (definition is null)
            return null;
        // Find a fitting spawn using the definition's nominal body size.
        float nominalSize = Simulation.TileSize * (float)definition.Size;
        var spawnRect = battleground.FindSpawnRect((int)nominalSize, playerWorldPosition, minDistanceTiles, rng);
        var enemy = Create(definition.Key, spawnRect.X, spawnRect.Y, level, awarenessRange, rng, battleground);
        if (key is null)
            ApplyModifier(enemy, level, rng);
        return enemy;
    }

    /// <summary>Build an atomic curated composition using the tier live at this level.</summary>
    public (EncounterPackage Package, List<Enemy> Group)? SpawnEncounter(
        int level, double maxThreat, Battleground battleground, Vector2 playerWorldPosition, float awarenessRange,
        float screenHeight, IReadOnlyList<Enemy>? existing = null, Random? rng = null)
    {
        rng ??= Random.Shared;
        var activeKeys = (existing ?? Array.Empty<Enemy>()).Select(e => e.EncounterKey).ToList();
        var packages = EnemyCatalogData.EncounterPackages
            .Where(p => p.MinLevel <= level && level <= p.MaxLevel && activeKeys.Count(k => k == p.Key) < p.MaxConcurrent)
            .ToList();
        if (packages.Count == 0)
            return null;
        var package = WeightedChoice(packages, packages.Select(p => p.Weight).ToList(), rng);
        var definitions = package.Families.Select(family => DefinitionForFamily(family, level)).ToList();
        if (definitions.Any(d => d is null))
            return null;
        double estimated = definitions.Sum(d => d!.ThreatCost * EnemyCatalogData.TierBalance[d.ProgressionTier].Threat);
        if (estimated > maxThreat)
            return null;

        var anchor = battleground.FindSpawnRect((int)(Simulation.TileSize * 1.2f), playerWorldPosition, 5, rng);
        var group = new List<Enemy>();
        for (int index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index]!;
            float angle = index * 2f * MathF.PI / Math.Max(1, definitions.Count);
            var candidate = new Rectangle(
                (int)(anchor.X + MathF.Cos(angle) * Simulation.TileSize * 1.8f),
                (int)(anchor.Y + MathF.Sin(angle) * Simulation.TileSize * 1.8f),
                Simulation.TileSize, Simulation.TileSize);
            var safe = battleground.FindNearestOpenRect(candidate);
            var enemy = Create(definition.Key, safe.X, safe.Y, level, awarenessRange, rng, battleground);
            enemy.EncounterKey = package.Key;
            if (index == 0 && level >= 8)
                ApplyModifier(enemy, level, rng);
            group.AddRange(ExpandAtomicMembers(enemy, package.Key));
        }
        AttachEncounter(package.Key, group, new Vector2(anchor.Center.X, anchor.Center.Y), level, screenHeight, rng);
        return (package, group);
    }

    /// <summary>Compose a coherent ambient encounter instead of loose random bodies.</summary>
    public (RuntimeEncounter Encounter, List<Enemy> Group)? SpawnPatrol(
        int level, double maxThreat, Battleground battleground, Vector2 playerWorldPosition, float awarenessRange,
        float screenHeight, IReadOnlyList<Enemy>? existing = null, Random? rng = null)
    {
        rng ??= Random.Shared;
        existing ??= Array.Empty<Enemy>();
        var available = Available(level).Where(d => !d.GuaranteedOnly && d.Family != "banner").ToList();
        if (available.Count == 0)
            return null;
        int targetSize = Progression.EncounterPacing(level).PatrolSize;
        var primary = Choose(level, rng, maxThreat, existing);
        if (primary is null)
            return null;

        string primaryRole = EnemyCatalogData.FamilyIdentities.GetValueOrDefault(
            primary.Family, new FamilyIdentity("pressure", new HashSet<string>())).CombatRole;
        var preferred = RolePairs.GetValueOrDefault(primaryRole, new[] { "pressure" });
        var definitions = new List<EnemyDefinition> { primary };
        for (int i = 0; i < targetSize - 1; i++)
        {
            var candidates = available.Where(d =>
                preferred.Contains(EnemyCatalogData.FamilyIdentities.GetValueOrDefault(
                    d.Family, new FamilyIdentity("pressure", new HashSet<string>())).CombatRole)).ToList();
            if (candidates.Count == 0)
                candidates = available;
            definitions.Add(WeightedChoice(candidates, candidates.Select(d => d.Weight).ToList(), rng));
        }

        double estimated = definitions.Sum(d => d.ThreatCost * EnemyCatalogData.TierBalance[d.ProgressionTier].Threat);
        while (definitions.Count > 1 && estimated > maxThreat)
        {
            var removed = definitions[^1];
            definitions.RemoveAt(definitions.Count - 1);
            estimated -= removed.ThreatCost * EnemyCatalogData.TierBalance[removed.ProgressionTier].Threat;
        }
        if (estimated > maxThreat)
            return null;

        var anchor = battleground.FindSpawnRect((int)(Simulation.TileSize * 1.1f), playerWorldPosition, 5, rng);
        string key = $"patrol_{RuntimeEncounter.NextId}";
        var group = new List<Enemy>();
        for (int index = 0; index < definitions.Count; index++)
        {
            float angle = index * 2f * MathF.PI / Math.Max(1, definitions.Count);
            var candidate = new Rectangle(
                (int)(anchor.Center.X + MathF.Cos(angle) * Simulation.TileSize * 1.4f),
                (int)(anchor.Center.Y + MathF.Sin(angle) * Simulation.TileSize * 1.4f),
                Simulation.TileSize, Simulation.TileSize);
            var safe = battleground.FindNearestOpenRect(candidate);
            var enemy = Create(definitions[index].Key, safe.X, safe.Y, level, awarenessRange, rng, battleground);
            ApplyModifier(enemy, level, rng);
            group.AddRange(ExpandAtomicMembers(enemy, key));
        }
        if (group.Count + existing.Count > 60)
            return null;
        double actualThreat = group.Sum(enemy => enemy.ThreatCost);
        if (actualThreat > maxThreat)
            return null;
        var encounter = AttachEncounter(key, group, new Vector2(anchor.Center.X, anchor.Center.Y), level, screenHeight, rng);
        return (encounter, group);
    }

    /// <summary>Weighted pick matching Python's random.choices(items, weights=weights, k=1)[0].</summary>
    private static T WeightedChoice<T>(IReadOnlyList<T> items, IReadOnlyList<double> weights, Random rng)
    {
        double total = weights.Sum();
        double roll = rng.NextDouble() * total;
        double cumulative = 0;
        for (int i = 0; i < items.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return items[i];
        }
        return items[^1];
    }

    private static Color TierColor(Color color, int rank)
    {
        if (rank == 1)
            return color;
        int amount = rank == 2 ? 20 : 38;
        return new Color(Math.Min(255, color.R + amount), Math.Min(255, color.G + amount / 2), Math.Min(255, color.B + amount));
    }

    /// <summary>Build compatible easy/medium/hard definitions for one enemy family.</summary>
    private static IReadOnlyList<EnemyDefinition> TieredFamily(
        string key, EnemyFactory factory,
        double weight, double speed, double size, double damage, double health, double experience, Color color,
        IReadOnlyList<(int MinLevel, int MaxLevel)> gates, double threatCost = 1.0, int maxActive = 99)
    {
        string[] tiers = { "easy", "medium", "hard" };
        string[] suffixes = { "", "_medium", "_hard" };
        var definitions = new List<EnemyDefinition>();
        for (int rank = 1; rank <= 3; rank++)
        {
            var gate = gates[rank - 1];
            definitions.Add(new EnemyDefinition(
                $"{key}{suffixes[rank - 1]}", factory,
                weight * (rank == 1 ? 1.0 : rank == 2 ? .72 : .48),
                gate.MinLevel, speed, size * (1 + .08 * (rank - 1)), damage, health, experience, TierColor(color, rank),
                ThreatCost: threatCost, Family: key, MaxActive: maxActive,
                MaxLevel: gate.MaxLevel, ProgressionTier: tiers[rank - 1]));
        }
        return definitions;
    }

    /// <summary>A fresh, empty catalog with nothing registered -- matches Python's `EnemyCatalog()`.</summary>
    public EnemyCatalog()
    {
    }

    /// <summary>The full default roster, equivalent to Python's `_register_defaults()` populating `ENEMY_CATALOG`.</summary>
    public static EnemyCatalog CreateDefault()
    {
        var catalog = new EnemyCatalog();
        foreach (var definition in BuildDefaultDefinitions())
            catalog.Register(definition);
        return catalog;
    }

    /// <summary>Shared default-roster instance, equivalent to Python's module-level ENEMY_CATALOG singleton.</summary>
    public static readonly EnemyCatalog Shared = CreateDefault();

    private static IEnumerable<EnemyDefinition> BuildDefaultDefinitions()
    {
        var entries = new List<EnemyDefinition>();

        entries.AddRange(TieredFamily("runner",
            args => new Enemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "runner", args.DifficultyTier, args.Rng),
            22, 1.42, .58, .72, .62, .8, new Color(221, 76, 73), new[] { (0, 6), (4, 13), (10, 20) }));

        entries.AddRange(TieredFamily("drifter",
            args => new Enemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "drifter", args.DifficultyTier, args.Rng),
            30, 1.0, .76, 1.0, 1.0, 1.0, new Color(184, 66, 75), new[] { (0, 7), (4, 14), (10, 20) }));

        entries.AddRange(TieredFamily("skirmisher",
            args => new Enemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "skirmisher", args.DifficultyTier, args.Rng),
            18, 1.08, .82, .92, 1.18, 1.3, new Color(68, 151, 142), new[] { (0, 8), (4, 14), (10, 20) }));

        entries.AddRange(TieredFamily("bulwark",
            args => new Enemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "bulwark", args.DifficultyTier, args.Rng),
            12, .58, 1.18, 1.52, 2.65, 2.1, new Color(200, 132, 56), new[] { (1, 8), (5, 14), (11, 20) }));

        entries.AddRange(TieredFamily("ranged_wanderer",
            args => new WanderingRangedEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "ranged_wanderer", args.DifficultyTier, args.Rng),
            10, .62, .82, .85, 1.45, 1.7, new Color(82, 126, 190), new[] { (0, 8), (4, 14), (10, 20) }, 1.2, 6));

        entries.AddRange(TieredFamily("shotgunner",
            args => new ShotgunEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "shotgunner", args.DifficultyTier, args.Rng),
            5, .56, .96, 1.0, 2.0, 2.4, new Color(188, 112, 61), new[] { (3, 9), (6, 14), (11, 20) }, 1.5, 5));

        entries.AddRange(TieredFamily("snake",
            args => new SnakeEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, 3 + args.TierRank * 2,
                archetype: "snake", difficultyTier: args.DifficultyTier, rng: args.Rng),
            3, .92, .86, 1.15, 2.15, 5.2, new Color(142, 83, 184), new[] { (5, 9), (8, 15), (12, 20) }, 2.5, 3));

        entries.AddRange(TieredFamily("parent",
            args => new ParentEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "parent", args.DifficultyTier, args.Rng),
            4, .54, 1.42, 1.15, 4.1, 5.8, new Color(126, 67, 146), new[] { (4, 9), (7, 15), (12, 20) }, 3.0, 2));

        entries.AddRange(TieredFamily("pillar",
            args => new PillarEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "pillar", args.DifficultyTier, args.Rng),
            4, 0, 1.12, 1.0, 3.2, 4.0, new Color(89, 103, 126), new[] { (4, 9), (7, 15), (12, 20) }, 4.0, 2));

        entries.AddRange(TieredFamily("banner",
            args => new BannerCaptain(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "banner", args.DifficultyTier, args.Rng, args.Battleground),
            5, .68, 1.05, .9, 2.6, 3.8, new Color(176, 78, 70), new[] { (2, 8), (6, 14), (11, 20) }, 3.0, 2));

        entries.AddRange(TieredFamily("rammer",
            args => new RammerEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "rammer", args.DifficultyTier, args.Rng),
            5, .74, 1.08, 1.35, 2.5, 3.2, new Color(169, 91, 58), new[] { (3, 8), (6, 14), (11, 20) }, 2.5, 3));

        entries.AddRange(TieredFamily("warder",
            args => new WarderEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "warder", args.DifficultyTier, args.Rng),
            4, .48, 1.0, .72, 2.5, 3.4, new Color(64, 112, 158), new[] { (4, 9), (7, 14), (11, 20) }, 2.8, 3));

        entries.AddRange(TieredFamily("splitter",
            args => new SplitterEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "splitter", args.DifficultyTier, args.Rng),
            5, .55, .86, .88, 1.8, 2.6, new Color(126, 73, 166), new[] { (3, 8), (6, 14), (10, 20) }, 2.0, 4));

        entries.AddRange(TieredFamily("collector",
            args => new CollectorEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, "collector", args.DifficultyTier, args.Rng),
            3, .82, .72, .55, 1.6, 1.4, new Color(64, 158, 92), new[] { (2, 8), (6, 14), (11, 20) }, 1.8, 2));

        entries.Add(new EnemyDefinition("volley_small",
            args => new VolleyEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "small", "volley", args.DifficultyTier, args.Rng),
            8, 0, .70, .74, .82, 1.2, 1.6, new Color(201, 139, 55),
            ThreatCost: 1.5, Family: "volley", MaxActive: 5, MaxLevel: 8, ProgressionTier: "easy"));
        entries.Add(new EnemyDefinition("volley_medium",
            args => new VolleyEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "medium", "volley", args.DifficultyTier, args.Rng),
            5, 6, .55, .98, 1.05, 2.3, 3.0, new Color(211, 132, 67),
            ThreatCost: 2.0, Family: "volley", MaxActive: 5, MaxLevel: 14, ProgressionTier: "medium"));
        entries.Add(new EnemyDefinition("volley_large",
            args => new VolleyEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "large", "volley", args.DifficultyTier, args.Rng),
            2, 12, .38, 1.32, 1.3, 4.3, 5.3, new Color(202, 108, 85),
            ThreatCost: 2.25, Family: "volley", MaxActive: 5, MaxLevel: 20, ProgressionTier: "hard"));

        entries.Add(new EnemyDefinition("laser_small",
            args => new LaserEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "small", "laser", args.DifficultyTier, args.Rng),
            7, 2, .58, .72, .85, 1.25, 1.8, new Color(174, 63, 77),
            ThreatCost: 1.5, Family: "laser", MaxActive: 2, MaxLevel: 8, ProgressionTier: "easy"));
        entries.Add(new EnemyDefinition("laser_medium",
            args => new LaserEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "medium", "laser", args.DifficultyTier, args.Rng),
            4, 7, .43, .98, 1.05, 2.4, 3.2, new Color(178, 72, 104),
            ThreatCost: 2.0, Family: "laser", MaxActive: 2, MaxLevel: 14, ProgressionTier: "medium"));
        entries.Add(new EnemyDefinition("laser_large",
            args => new LaserEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "large", "laser", args.DifficultyTier, args.Rng),
            2, 13, .28, 1.28, 1.25, 4.5, 5.4, new Color(164, 72, 122),
            ThreatCost: 2.25, Family: "laser", MaxActive: 2, MaxLevel: 20, ProgressionTier: "hard"));

        entries.Add(new EnemyDefinition("bomb_small",
            args => new BombEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "small", "bomb", args.DifficultyTier, args.Rng),
            7, 2, .72, .7, .9, 1.3, 1.8, new Color(190, 147, 57),
            ThreatCost: 1.5, Family: "bomb", MaxActive: 4, MaxLevel: 8, ProgressionTier: "easy"));
        entries.Add(new EnemyDefinition("bomb_medium",
            args => new BombEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "medium", "bomb", args.DifficultyTier, args.Rng),
            4, 8, .54, .96, 1.08, 2.5, 3.2, new Color(200, 132, 72),
            ThreatCost: 2.0, Family: "bomb", MaxActive: 4, MaxLevel: 14, ProgressionTier: "medium"));
        entries.Add(new EnemyDefinition("bomb_large",
            args => new BombEnemy(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, "large", "bomb", args.DifficultyTier, args.Rng),
            2, 14, .34, 1.3, 1.3, 4.6, 5.5, new Color(194, 116, 87),
            ThreatCost: 2.25, Family: "bomb", MaxActive: 4, MaxLevel: 20, ProgressionTier: "hard"));

        entries.Add(new EnemyDefinition("miniboss_arsenal",
            args => new ArsenalMiniBoss(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, new[] { "volley", "laser", "bomb" },
                difficultyTier: args.DifficultyTier, rng: args.Rng),
            .7, 5, .34, 1.75, 1.45, 10.0, 15.0, new Color(84, 72, 118),
            ThreatCost: 12.0, Family: "miniboss", MaxActive: 1, GuaranteedOnly: true, MaxLevel: 20, ProgressionTier: "medium"));
        entries.Add(new EnemyDefinition("miniboss_siege",
            args => new ArsenalMiniBoss(args.WorldX, args.WorldY, args.Speed, args.Size, args.Color, args.Damage, args.Hp,
                args.ExpValue, args.Difficulty, args.AwarenessRange, new[] { "bomb", "volley", "laser" },
                difficultyTier: args.DifficultyTier, rng: args.Rng),
            .7, 15, .3, 1.85, 1.55, 11.0, 16.0, new Color(78, 91, 112),
            ThreatCost: 13.0, Family: "miniboss", MaxActive: 1, GuaranteedOnly: true, MaxLevel: 20, ProgressionTier: "hard"));

        return entries;
    }
}
