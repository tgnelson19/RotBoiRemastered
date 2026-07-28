namespace RotBoiRemastered.Systems;

/// <summary>Persisted time spent in one readable boss phase or trial.</summary>
public sealed class BossPhaseTelemetryData
{
    public string Label { get; set; } = "UNKNOWN";
    public double Seconds { get; set; }
}

/// <summary>
/// Compact balance telemetry for one completed or failed boss encounter.
/// It intentionally stores no player identity, input events, or frame-level
/// history: only the aggregate values needed to compare encounter pacing.
/// </summary>
public sealed class BossEncounterTelemetryData
{
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public string BossKey { get; set; } = "unknown";
    public string SenseKey { get; set; } = "unknown";
    public int FloorNumber { get; set; }
    public bool Victory { get; set; }
    public double ClearSeconds { get; set; }
    public int DamageTaken { get; set; }
    public int SkippedBranchRooms { get; set; }
    public double SkippedBranchThreat { get; set; }
    public double CarriedEnemyThreat { get; set; }
    public bool ControllerUsed { get; set; }
    public int LocalPlayerCount { get; set; } = 1;
    public List<BossPhaseTelemetryData> Phases { get; set; } = new();
}

/// <summary>
/// Mutable per-encounter accumulator. GameSession owns at most one tracker
/// and converts it to the persisted, immutable-in-practice data shape only
/// when the boss dies or the player is defeated.
/// </summary>
internal sealed class BossEncounterTelemetryTracker
{
    private readonly Dictionary<string, double> _phaseSeconds =
        new(StringComparer.Ordinal);

    public string BossKey { get; }
    public string SenseKey { get; }
    public int FloorNumber { get; }
    public double StartedAtRunSeconds { get; }
    public int SkippedBranchRooms { get; }
    public double SkippedBranchThreat { get; }
    public double CarriedEnemyThreat { get; }
    public int DamageTaken { get; private set; }
    public bool ControllerUsed { get; private set; }
    public string? CurrentPhase { get; private set; }

    public BossEncounterTelemetryTracker(
        string bossKey,
        string senseKey,
        int floorNumber,
        double startedAtRunSeconds,
        int skippedBranchRooms,
        double skippedBranchThreat,
        double carriedEnemyThreat)
    {
        BossKey = bossKey;
        SenseKey = senseKey;
        FloorNumber = floorNumber;
        StartedAtRunSeconds = startedAtRunSeconds;
        SkippedBranchRooms = skippedBranchRooms;
        SkippedBranchThreat = skippedBranchThreat;
        CarriedEnemyThreat = carriedEnemyThreat;
    }

    public bool ObservePhase(string label, double seconds)
    {
        label = string.IsNullOrWhiteSpace(label) ? "UNKNOWN" : label;
        bool changed = CurrentPhase is not null
            && !string.Equals(CurrentPhase, label, StringComparison.Ordinal);
        CurrentPhase = label;
        _phaseSeconds[label] = _phaseSeconds.GetValueOrDefault(label)
            + Math.Max(0, seconds);
        return changed;
    }

    public void RecordDamage(double damage) =>
        DamageTaken += Math.Max(0, (int)Math.Round(damage));

    public void RecordControllerUse(bool used) =>
        ControllerUsed |= used;

    public BossEncounterTelemetryData Finish(double runTimeSeconds, bool victory) =>
        new()
        {
            BossKey = BossKey,
            SenseKey = SenseKey,
            FloorNumber = FloorNumber,
            Victory = victory,
            ClearSeconds = Math.Max(0, runTimeSeconds - StartedAtRunSeconds),
            DamageTaken = DamageTaken,
            SkippedBranchRooms = SkippedBranchRooms,
            SkippedBranchThreat = SkippedBranchThreat,
            CarriedEnemyThreat = CarriedEnemyThreat,
            ControllerUsed = ControllerUsed,
            LocalPlayerCount = 1,
            Phases = _phaseSeconds
                .Select(entry => new BossPhaseTelemetryData
                {
                    Label = entry.Key,
                    Seconds = Math.Round(entry.Value, 3),
                })
                .ToList(),
        };
}
