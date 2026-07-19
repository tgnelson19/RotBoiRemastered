using Microsoft.Xna.Framework;

namespace RotBoiRemastered.Entities;

/// <summary>
/// The Soul hub's stationary DPS target. A real Enemy (not just a world
/// position) so it can carry the same StatusEffects dictionary every other
/// enemy does -- that's what lets bleed/bane/poison ticks and on-hit unique
/// effects actually land on it via the normal StatusEffects/UniqueEffects
/// pipeline, instead of the dummy silently only ever taking raw bullet
/// damage. Effectively unkillable: TakeDamage records the hit for SoulHub's
/// DPS meter (see DrainUnrecordedDamage) and resets Hp back to MaxHp rather
/// than letting it reach zero.
/// </summary>
public sealed class TrainingDummy : Enemy
{
    public double UnrecordedDamage { get; private set; }

    /// <summary>
    /// Boss-tier HP (matches Beaudis's 26,000), not some enormous "surely
    /// never runs out" number -- StatusEffects.Update's bleed/poison DoT
    /// ticks scale off enemy.MaxHp (percent-of-max-health per stack), so an
    /// artificially huge MaxHp made bleed/poison ticks explode into millions
    /// of damage per proc instead of the modest, boss-realistic numbers a
    /// DPS dummy should show. TakeDamage below is what actually makes this
    /// dummy unkillable (it resets Hp to MaxHp every hit) -- MaxHp itself
    /// only needs to be a believable enemy HP pool, not a huge one.
    /// </summary>
    public TrainingDummy(float worldX, float worldY)
        : base(worldX, worldY, speed: 0, size: 68, Color.White, damage: 0, hp: 26_000,
            expValue: 0, difficulty: 0, awarenessRange: 0)
    {
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        int rounded = Math.Max(0, (int)Math.Round(amount));
        UnrecordedDamage += rounded;
        Hp = MaxHp;
        return new HitResult(true, false, rounded);
    }

    /// <summary>Pulls and clears whatever damage has landed (direct hits or status-effect ticks) since the last drain, for SoulHub to fold into its DPS window.</summary>
    public double DrainUnrecordedDamage()
    {
        double amount = UnrecordedDamage;
        UnrecordedDamage = 0;
        return amount;
    }
}
