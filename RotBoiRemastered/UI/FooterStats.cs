using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

/// <summary>A stable, persisted footer-stat choice and its live value formatter.</summary>
public sealed record FooterStatDefinition(
    string Id,
    string Label,
    string ShortLabel,
    string Symbol,
    Func<RunState, string> Value);

public static class FooterStats
{
    public const int SelectionCount = 3;

    public static readonly IReadOnlyList<string> Defaults =
        new[] { "damage", "attack_rate", "defense" };

    public static readonly IReadOnlyList<FooterStatDefinition> Definitions =
        new FooterStatDefinition[]
        {
            new("damage", "DAMAGE", "DMG", "Bullet Damage", state => $"{state.BulletDamage:N0}"),
            new("attack_rate", "ATTACK RATE", "RATE", "Attack Speed",
                state => $"{InformationSheet.AttacksPerSecond(state):0.00}/s"),
            new("projectiles", "PROJECTILES", "SHOTS", "Bullet Count",
                state => $"{state.ProjectileCount:0.##}"),
            new("critical", "CRITICAL", "CRIT", "Crit Chance",
                state => $"{state.CritChance * 100:0}%"),
            new("pierce", "PIERCE", "PIERCE", "Bullet Pierce",
                state => $"{state.BulletPierce:0.##}"),
            new("defense", "DEFENSE", "DEF", "Defense", state => $"{state.Defense:N0}"),
            new("vitality", "VITALITY", "VIT", "Vitality", state => $"{state.Vitality:N0}/s"),
            new("move_speed", "MOVE SPEED", "MOVE", "Player Speed",
                state => $"{state.PlayerSpeed:0.00}"),
            new("range", "RANGE", "RANGE", "Bullet Range",
                state => $"{state.BulletRange / Simulation.TileSize:0.0} tiles"),
        };

    public static readonly IReadOnlyDictionary<string, FooterStatDefinition> ById =
        Definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    /// <summary>
    /// Keeps exactly three valid, unique IDs. Missing/corrupt old profiles are
    /// filled from the authored defaults and then registry order.
    /// </summary>
    public static List<string> NormalizeSelection(IEnumerable<string>? selected)
    {
        var result = new List<string>(SelectionCount);
        void Add(string? id)
        {
            if (id is not null && ById.ContainsKey(id) && !result.Contains(id, StringComparer.Ordinal))
                result.Add(id);
        }

        if (selected is not null)
            foreach (string id in selected)
                Add(id);
        foreach (string id in Defaults)
            Add(id);
        foreach (FooterStatDefinition definition in Definitions)
            Add(definition.Id);
        return result.Take(SelectionCount).ToList();
    }

    /// <summary>Selects a stat for one slot, swapping when it is already selected elsewhere.</summary>
    public static List<string> Select(IReadOnlyList<string> current, int slot, string id)
    {
        var result = NormalizeSelection(current);
        if (slot < 0 || slot >= SelectionCount || !ById.ContainsKey(id))
            return result;
        int existing = result.IndexOf(id);
        if (existing >= 0 && existing != slot)
            (result[existing], result[slot]) = (result[slot], result[existing]);
        else
            result[slot] = id;
        return result;
    }

    public static List<string> Cycle(IReadOnlyList<string> current, int slot, int direction)
    {
        var result = NormalizeSelection(current);
        if (slot < 0 || slot >= SelectionCount || direction == 0)
            return result;
        int index = Definitions.ToList().FindIndex(definition => definition.Id == result[slot]);
        index = (index + Math.Sign(direction) + Definitions.Count) % Definitions.Count;
        return Select(result, slot, Definitions[index].Id);
    }
}
