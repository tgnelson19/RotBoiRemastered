using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Systems;

public sealed class StatusEffectState
{
    public double Remaining { get; set; }
    public double Potency { get; set; }
    public int Stacks { get; set; }
}

public sealed record StatusControl(double MovementMultiplier = 1, double AttackDelay = 0, bool Stunned = false);

/// <summary>
/// Player-owned ailments. Periodic damage is explicitly tagged as damage over
/// time, so it can hurt bosses without pretending every tick was a fresh hit.
/// </summary>
public static class StatusEffects
{
    public static void Apply(Enemy enemy, string kind, double duration, double potency = 0, int stacks = 1)
    {
        bool isBoss = enemy is Beaudis or Dissonance or PathChaseBoss or SinChemesthesisBoss or PhantasiaBoss or PlagueTouchBoss;
        if (kind == "stun")
        {
            enemy.StatusControlResistance = Math.Min(.8, enemy.StatusControlResistance + (isBoss ? .18 : .08));
            duration *= (1 - enemy.StatusControlResistance) * (isBoss ? .45 : 1.0);
        }
        if (!enemy.StatusEffects.TryGetValue(kind, out var effect))
        {
            effect = new StatusEffectState();
            enemy.StatusEffects[kind] = effect;
        }
        effect.Remaining = Math.Max(effect.Remaining, duration);
        effect.Potency = Math.Max(effect.Potency, potency);
        int stackCap = kind switch { "bleed" => 8, "bane" => 30, _ => 3 };
        effect.Stacks = Math.Min(stackCap, effect.Stacks + stacks);
        GameProfile.IncrementQuest("statuses_applied");
    }

    public static void RollPlayerHit(Enemy enemy, Bullet bullet, IEnumerable<ItemDrop?> equipment, double projectileCount,
        Random? rng = null)
    {
        rng ??= Random.Shared;
        var chances = new Dictionary<string, double>(Items.StatusChances(equipment));
        double coefficient = Math.Max(.22, 1 / Math.Sqrt(Math.Max(1, projectileCount)));
        if (bullet.IsCritical)
            chances["bleed"] = chances.GetValueOrDefault("bleed") + .025;
        var tuning = new Dictionary<string, (double Duration, double Potency)>
        {
            ["poison"] = (4.5, .007), ["bleed"] = (3.2, .006), ["slow"] = (2.4, .22),
            ["daze"] = (2.2, .28), ["stun"] = (.65, 1),
        };
        foreach (var (kind, chance) in chances)
        {
            if (tuning.TryGetValue(kind, out var values) && rng.NextDouble() <= Math.Min(.65, chance * coefficient))
                Apply(enemy, kind, values.Duration, values.Potency);
        }
    }

    public static double DamageMultiplier(Enemy enemy, Bullet? bullet = null)
    {
        double multiplier = 1;
        if (enemy.StatusEffects.ContainsKey("poison") && enemy.StatusEffects.ContainsKey("daze"))
            multiplier += .08;
        if (enemy.StatusEffects.ContainsKey("dread"))
            multiplier += .15;
        if (bullet?.IsCritical == true && enemy.StatusEffects.TryGetValue("bleed", out var bleed))
            multiplier += Math.Min(.20, bleed.Stacks * .025);
        // Unlike bleed's crit-only bonus, Bane (Grimsbane's signature stacking
        // curse -- see UniqueEffects.OnPlayerHit's "bane_on_hit") boosts every
        // hit the target takes, +5% per stack, up to the 30-stack cap (+150%).
        if (enemy.StatusEffects.TryGetValue("bane", out var bane))
            multiplier += bane.Stacks * .05;
        return multiplier;
    }

    public static StatusControl Update(Enemy enemy, double seconds)
    {
        enemy.StatusControlResistance = Math.Max(0, enemy.StatusControlResistance - seconds * .035);
        double dotPerSecond = 0, movement = 1, daze = 0;
        bool stunned = false;
        foreach (var (kind, effect) in enemy.StatusEffects.ToArray())
        {
            effect.Remaining -= seconds;
            if (effect.Remaining <= 0)
            {
                enemy.StatusEffects.Remove(kind);
                continue;
            }
            switch (kind)
            {
                case "poison": dotPerSecond += Math.Max(2, enemy.MaxHp * effect.Potency) * effect.Stacks; break;
                case "bleed": dotPerSecond += Math.Max(1, enemy.MaxHp * effect.Potency) * effect.Stacks; break;
                case "slow": movement *= Math.Max(.45, 1 - effect.Potency); break;
                case "dread": movement *= Math.Max(.40, 1 - effect.Potency); break;
                case "daze": daze = Math.Max(daze, effect.Potency); break;
                case "stun": stunned = true; break;
            }
        }
        if (dotPerSecond > 0 && enemy.Hp > 0)
        {
            enemy.StatusDotBuffer += dotPerSecond * seconds;
            int damage = (int)enemy.StatusDotBuffer;
            if (damage > 0)
            {
                enemy.StatusDotBuffer -= damage;
                enemy.TakeDamage(damage, "body", DamageSource.DamageOverTime);
            }
        }
        return new StatusControl(movement, daze, stunned);
    }
}
