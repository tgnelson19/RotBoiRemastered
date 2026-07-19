using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Systems;

/// <summary>
/// Bespoke on-hit behavior for unique weapons (see Items.Uniques),
/// dispatched by ItemDefinition.EffectIds -- the counterpart to
/// StatusEffects.RollPlayerHit, which only knows how to apply the same
/// handful of generic, chance-driven ailments any item can roll into via
/// Items.StatusChances. Each EffectId is a one-off hook instead: add a case
/// here (and, if it leans on a new status kind like "dread" below, a case in
/// StatusEffects.Update/DamageMultiplier too) for each new named effect,
/// rather than trying to force every future effect into the generic
/// chance-dictionary shape that's shared across ordinary items.
///
/// A weapon can list more than one EffectId -- OnPlayerHit runs every one of
/// them on every non-killing hit, so e.g. Bow of Dread stacks a
/// crowd-control effect (dread_on_hit) and a sustain effect
/// (dread_lifesteal) on the same item, neither aware the other exists.
/// </summary>
public static class UniqueEffects
{
    /// <summary>Called from GameSession.HandleDamagingEnemies right alongside StatusEffects.RollPlayerHit, once per non-killing hit, only when the equipped weapon has at least one EffectId.</summary>
    public static void OnPlayerHit(Enemy enemy, Bullet bullet, ItemDrop weapon, RunState state, Random? rng = null)
    {
        rng ??= Random.Shared;
        foreach (var effectId in weapon.Definition.EffectIds ?? Array.Empty<string>())
        {
            switch (effectId)
            {
                case "dread_on_hit":
                    // Roughly 1-in-5 hits (better than 1-in-3 on a crit) afflict
                    // Dread: a strong slow plus +15% damage taken for 3.5s -- see
                    // StatusEffects.Update's "dread" case for the slow and
                    // DamageMultiplier's for the damage-taken bonus.
                    double dreadChance = bullet.IsCritical ? .32 : .18;
                    if (rng.NextDouble() <= dreadChance)
                        StatusEffects.Apply(enemy, "dread", duration: 3.5, potency: .55);
                    break;

                case "dread_lifesteal":
                    // A second, independent effect on the same weapon: a 10%
                    // chance per hit to heal the player for 2% of that hit's
                    // damage. Doesn't touch dread_on_hit's roll or state at all --
                    // that's the point of listing effects separately instead of
                    // cramming multiple behaviors into one case.
                    if (rng.NextDouble() <= .10)
                    {
                        int heal = Math.Max(1, (int)Math.Round(bullet.Damage * .02));
                        state.HealthPoints = Math.Min(state.MaxHealthPoints, state.HealthPoints + heal);
                    }
                    break;
            }
        }
    }
}
