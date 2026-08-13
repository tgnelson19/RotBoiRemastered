namespace RotBoiRemastered.Systems;

/// <summary>Pure, deterministic combat math used by regression tests and future balance tooling.</summary>
public static class BalanceAnalysis
{
    public sealed record BuildInput(
        double Damage = 100,
        double AttackCooldown = 40,
        double ProjectileCount = 1,
        double Pierce = 1,
        double CritChance = .05,
        double CritDamage = 2,
        double Range = 250,
        IReadOnlyList<ItemDrop?>? Equipment = null,
        IReadOnlyList<UpgradeCard>? Cards = null,
        IReadOnlyDictionary<string, int>? MetaRanks = null);

    public sealed record CombatEstimate(
        double Damage,
        double AttacksPerSecond,
        double ExpectedProjectiles,
        double ExpectedCritMultiplier,
        double SingleTargetDps,
        double CrowdDps,
        double Range,
        double SafetyValue);

    public static CombatEstimate Estimate(BuildInput input)
    {
        var stats = new Dictionary<string, StatTrack>
        {
            ["Bullet Damage"] = new(input.Damage), ["Attack Speed"] = new(input.AttackCooldown),
            ["Bullet Count"] = new(input.ProjectileCount), ["Bullet Pierce"] = new(input.Pierce),
            ["Crit Chance"] = new(input.CritChance), ["Crit Damage"] = new(input.CritDamage),
            ["Bullet Range"] = new(input.Range),
        };

        foreach (var (key, ranks) in input.MetaRanks ?? new Dictionary<string, int>())
        {
            if (!MetaProgression.SkillNodesByKey.TryGetValue(key, out var node) || !stats.TryGetValue(node.Stat, out var stat))
                continue;
            for (int rank = 0; rank < Math.Min(ranks, node.MaxLevel); rank++)
                if (node.Mode == "multiplicative") stat.Multiplicative.Add(node.ValuePerLevel);
                else stat.Additive.Add(node.ValuePerLevel);
        }

        foreach (var card in input.Cards ?? Array.Empty<UpgradeCard>())
            foreach (var effect in card.Effects)
                if (stats.TryGetValue(effect.Stat, out var stat))
                {
                    double value = Upgrades.EffectModifier(card.Rarity, effect);
                    if (effect.MathType == "additive") stat.Additive.Add(value);
                    else stat.Multiplicative.Add(value);
                }

        var equipment = input.Equipment ?? Array.Empty<ItemDrop?>();
        double damage = Items.AdjustStat("Bullet Damage", stats["Bullet Damage"].Combined, equipment);
        double cooldown = Items.AdjustStat("Attack Speed", stats["Attack Speed"].Combined, equipment);
        double count = Math.Clamp(Items.AdjustStat("Bullet Count", stats["Bullet Count"].Combined, equipment), 1, 12);
        double pierce = Math.Clamp(Items.AdjustStat("Bullet Pierce", stats["Bullet Pierce"].Combined, equipment), 1, 8);
        double critChance = Math.Clamp(Items.AdjustStat("Crit Chance", stats["Crit Chance"].Combined, equipment), 0, .85);
        double critDamage = Math.Clamp(Items.AdjustStat("Crit Damage", stats["Crit Damage"].Combined, equipment), 1, 5);
        double range = Items.AdjustStat("Bullet Range", stats["Bullet Range"].Combined, equipment);
        double attacks = 60.0 / cooldown;
        double crit = 1 + critChance * (critDamage - 1);
        double single = damage * attacks * count * crit;
        double crowd = single * Math.Min(pierce, 1 + (pierce - 1) * .55);
        double safety = single * Math.Sqrt(range / 250.0);
        return new CombatEstimate(damage, attacks, count, crit, single, crowd, range, safety);
    }

    /// <summary>Expected strong-but-plausible DPS growth used to enforce progression pressure bands.</summary>
    public static double ExpectedPlayerPowerGrowth(int level, bool pathMode) =>
        Math.Pow(pathMode ? 1.085 : 1.076, Math.Clamp(level, 0, pathMode ? 40 : 20));

    public static double RelativeTimeToKill(int level, bool pathMode) =>
        World.Progression.EnemyStatScales(level).Health / ExpectedPlayerPowerGrowth(level, pathMode);

    public static (double MeanItems, IReadOnlyDictionary<string, double> RarityRates) SimulateDrops(
        int kills, bool pathMode, int seed = 1, int newGamePlusLevel = 0)
    {
        var rng = new Random(seed);
        var rarityCounts = Upgrades.RarityOrder.ToDictionary(rarity => rarity, _ => 0);
        int itemCount = 0;
        for (int kill = 0; kill < kills; kill++)
        {
            int count = pathMode ? Items.RollPathDropCount(rng) : Items.RollDropCount(rng);
            foreach (var drop in Items.GenerateDrops(count, rng, newGamePlusLevel: newGamePlusLevel))
            {
                itemCount++;
                rarityCounts[drop.Rarity]++;
            }
        }
        var rates = rarityCounts.ToDictionary(pair => pair.Key,
            pair => itemCount == 0 ? 0 : pair.Value / (double)itemCount);
        return (itemCount / (double)Math.Max(1, kills), rates);
    }

    public static double ExpectedReforges(int enemiesDefeated, double fragmentCollectionRate = 1.0) =>
        enemiesDefeated * GameSession.FragmentDropChance * Math.Clamp(fragmentCollectionRate, 0, 1)
            / Items.ReforgeFragmentCost;
}
