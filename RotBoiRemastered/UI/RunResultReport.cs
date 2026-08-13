using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public sealed record RunItemSummary(string Slot, string Name, string Rarity);

public enum RunPaceBand { UnderTarget, OnTarget, OverTarget }

public static class RunPacing
{
    public const double TargetMinimumSeconds = 25 * 60;
    public const double TargetMaximumSeconds = 35 * 60;
    public const string TargetLabel = "25-35 MIN TARGET";

    public static RunPaceBand Assess(double seconds) => seconds switch
    {
        < TargetMinimumSeconds => RunPaceBand.UnderTarget,
        <= TargetMaximumSeconds => RunPaceBand.OnTarget,
        _ => RunPaceBand.OverTarget,
    };
}

/// <summary>
/// Immutable end-of-run debrief. It snapshots both live run state and exact
/// persistence deltas once, so later Soul/profile changes cannot rewrite it.
/// </summary>
public sealed record RunResultReport
{
    public required string Outcome { get; init; }
    public required string PathKey { get; init; }
    public required string PathTitle { get; init; }
    public required string BuildIdentity { get; init; }
    public required IReadOnlyList<string> DominantFamilies { get; init; }
    public required IReadOnlyList<RunItemSummary> RetainedLoadout { get; init; }
    public required IReadOnlyList<RunItemSummary> LostLoadout { get; init; }
    public int Level { get; init; }
    public int Kills { get; init; }
    public double Seconds { get; init; }
    public bool HardMode { get; init; }
    public bool NoExtract { get; init; }
    public int NewGamePlusLevel { get; init; }
    public int MindTokenReward { get; init; }
    public int PathMasteryBefore { get; init; }
    public int PathMasteryAfter { get; init; }
    public int NewGamePlusBefore { get; init; }
    public int NewGamePlusAfter { get; init; }
    public int UpgradeCount { get; init; }
    public double FieldSeconds { get; init; }
    public double BossSeconds { get; init; }
    public RunPaceBand PaceBand { get; init; }

    public static RunResultReport Capture(RunState state, string pathKey,
        bool retained, RunRewardSummary? rewards)
    {
        var families = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (upgrade, count) in state.UpgradeTypeCounts)
        {
            if (Upgrades.DefinitionsByName.TryGetValue(upgrade, out var definition))
                families[definition.Category] = families.GetValueOrDefault(definition.Category) + count;
        }
        var dominant = families.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(3)
            .Select(pair => $"{pair.Key.ToUpperInvariant()} {pair.Value}")
            .ToArray();
        string identity = dominant.Length == 0
            ? "UNSHAPED SOUL"
            : $"{dominant[0].Split(' ')[0]} SOUL";

        var items = new List<RunItemSummary>();
        foreach (var (slot, item) in state.Equipment)
            if (item is not null)
                items.Add(new RunItemSummary(slot.ToUpperInvariant(), item.DisplayName, item.Rarity));
        for (int index = 0; index < state.Inventory.Count; index++)
            if (state.Inventory[index] is { } item)
                items.Add(new RunItemSummary($"STASH {index + 1}", item.DisplayName, item.Rarity));

        string pathTitle = pathKey == NewGamePlus.DungeonKey
            ? "THE DUNGEON"
            : state.RunOutcome == RunOutcomes.AphantasiaDefeated
                ? "APHANTASIA"
                : GamePaths.PathsByKey.GetValueOrDefault(pathKey)?.Title
                    ?? GamePaths.Active().Title;
        int mastery = GameProfile.Profile.PathMastery.GetValueOrDefault(pathKey);
        int newGamePlus = GameProfile.Profile.NewGamePlusUnlocked.GetValueOrDefault(pathKey);
        double bossSeconds = Math.Clamp(
            state.BossEncounterTelemetry.Sum(encounter => encounter.ClearSeconds),
            0, Math.Max(0, state.RunTimeSeconds));
        return new RunResultReport
        {
            Outcome = state.RunOutcome,
            PathKey = pathKey,
            PathTitle = pathTitle,
            BuildIdentity = identity,
            DominantFamilies = dominant,
            RetainedLoadout = retained ? items.ToArray() : Array.Empty<RunItemSummary>(),
            LostLoadout = retained ? Array.Empty<RunItemSummary>() : items.ToArray(),
            Level = state.CurrentLevel,
            Kills = state.NumOfEnemiesKilled,
            Seconds = state.RunTimeSeconds,
            HardMode = state.HardMode,
            NoExtract = state.NoExtract,
            NewGamePlusLevel = state.NewGamePlusLevel,
            MindTokenReward = rewards?.MindTokenDelta ?? 0,
            PathMasteryBefore = rewards?.PathMasteryBefore ?? mastery,
            PathMasteryAfter = rewards?.PathMasteryAfter ?? mastery,
            NewGamePlusBefore = rewards?.NewGamePlusBefore ?? newGamePlus,
            NewGamePlusAfter = rewards?.NewGamePlusAfter ?? newGamePlus,
            UpgradeCount = Math.Max(state.UpgradeHistory.Count,
                state.UpgradeRarityCounts.Values.Sum()),
            BossSeconds = bossSeconds,
            FieldSeconds = Math.Max(0, state.RunTimeSeconds - bossSeconds),
            PaceBand = RunPacing.Assess(state.RunTimeSeconds),
        };
    }
}
