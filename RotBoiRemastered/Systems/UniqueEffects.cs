using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Systems;

/// <summary>
/// Bespoke on-hit behavior for unique weapons (see Items.Uniques),
/// dispatched by ItemDefinition.EffectId -- the counterpart to
/// StatusEffects.RollPlayerHit, which only knows how to apply the same
/// handful of generic, chance-driven ailments any item can roll into via
/// Items.StatusChances. A unique's EffectId is a one-off hook instead: add a
/// case here (and, if it leans on a new status kind like "dread" below, a
/// case in StatusEffects.Update/DamageMultiplier too) for each new unique,
/// rather than trying to force every future effect into the generic
/// chance-dictionary shape that's shared across ordinary items.
/// </summary>
public static class UniqueEffects
{
    /// <summary>Called from GameSession.HandleDamagingEnemies right alongside StatusEffects.RollPlayerHit, once per non-killing hit, only when the equipped weapon has an EffectId.</summary>
    public static void OnPlayerHit(Enemy enemy, Bullet bullet, ItemDrop weapon, Random? rng = null)
    {
        rng ??= Random.Shared;
        switch (weapon.Definition.EffectId)
        {
            case "dread_on_hit":
                // Roughly 1-in-5 hits (better than 1-in-3 on a crit) afflict
                // Dread: a strong slow plus +15% damage taken for 3.5s -- see
                // StatusEffects.Update's "dread" case for the slow and
                // DamageMultiplier's for the damage-taken bonus.
                double chance = bullet.IsCritical ? .32 : .18;
                if (rng.NextDouble() <= chance)
                    StatusEffects.Apply(enemy, "dread", duration: 3.5, potency: .55);
                break;
        }
    }
}
