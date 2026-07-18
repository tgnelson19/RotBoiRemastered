namespace RotBoiRemastered.Systems;

/// <summary>
/// One upgradeable stat's base value plus its additive and multiplicative
/// modifier stacks. Replaces characterStats.py's three parallel dicts
/// (`collectiveStats`/`collectiveAddStats`/`collectiveMultStats`, each keyed
/// by the same stat name) with one object per stat -- the three dicts always
/// moved in lockstep (same keys, same lifecycle, reset together) so keeping
/// them separate only made it possible for them to drift out of sync by
/// name. `character.py`'s `_combine_stat`/`combarinoPlayerStats` read this as
/// `(base + sum(additive)) * product(multiplicative)`; `Combined` is exactly
/// that formula.
/// </summary>
public sealed class StatTrack
{
    public double Base { get; set; }
    public List<double> Additive { get; } = new() { 0 };
    public List<double> Multiplicative { get; } = new() { 1 };

    public StatTrack(double baseValue) => Base = baseValue;

    public double Combined => (Base + Additive.Sum()) * Multiplicative.Aggregate(1.0, (acc, v) => acc * v);
}
