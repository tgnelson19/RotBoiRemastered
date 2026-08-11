using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

public enum SecretArchetype { KeyDoor, Holdout, GuardianHunt, SwitchCircuit, PatternPuzzle }
public enum SecretState { Hidden, Discovered, Solved, DungeonOpen, GuardianDefeated }

public sealed record ExpeditionSecret(
    string SenseKey,
    SecretArchetype Archetype,
    Vector2 WorldPosition,
    string JournalClue,
    bool IsFinale)
{
    public SecretState State { get; set; }
    public bool IsAvailable(int defeatedGuardians) => !IsFinale || defeatedGuardians >= 4;
}

/// <summary>
/// Runtime-only expedition state. It intentionally is not serialized: returning
/// from a dungeon preserves this object, while extraction, death, or process exit
/// abandons the current map without touching permanent CampaignProgressData.
/// </summary>
public sealed class ExpeditionRun
{
    private static readonly string[] BodyClues =
    [
        "A borrowed tooth remembers the lock.",
        "Silence follows the last sentinel.",
        "Something old waits where the tunnels breathe.",
        "Five marks answer only in their proper order.",
        "The floor remembers every measured step.",
    ];
    private static readonly string[] SoulClues =
    [
        "The key is carried by the room that has no door.",
        "Let every voice rise before the chamber falls quiet.",
        "The hunter watches from the path behind you.",
        "An answer given twice becomes a lie.",
        "Walk the wound without touching its light.",
    ];

    public CampaignWorld World { get; }
    public int Seed { get; }
    public string FinaleSense { get; }
    public Battleground Battleground { get; }
    public IReadOnlyList<ExpeditionSecret> Secrets { get; }
    public int DefeatedGuardians => Secrets.Count(secret => secret.State == SecretState.GuardianDefeated);
    public bool Complete => DefeatedGuardians == Secrets.Count;
    public Vector2? SuspendedReturnPosition { get; private set; }
    public ExpeditionSecret? ActiveDungeonSecret { get; private set; }

    public ExpeditionRun(CampaignWorld world, int? seed = null, string? finaleSense = null)
    {
        World = world;
        Seed = seed ?? Random.Shared.Next();
        var rng = new Random(Seed);
        FinaleSense = finaleSense ?? CampaignProgression.SenseKeys[rng.Next(CampaignProgression.SenseKeys.Length)];
        if (!CampaignProgression.SenseKeys.Contains(FinaleSense))
            throw new ArgumentOutOfRangeException(nameof(finaleSense));
        Battleground = ExpeditionWorldGenerator.Generate(world, rng);

        var positions = ExpeditionWorldGenerator.SecretPositions(Battleground, rng, 5);
        var archetypes = Enum.GetValues<SecretArchetype>().OrderBy(_ => rng.Next()).ToArray();
        string[] clues = world == CampaignWorld.Body ? BodyClues : SoulClues;
        Secrets = CampaignProgression.SenseKeys
            .Select((sense, index) => new ExpeditionSecret(sense, archetypes[index], positions[index],
                clues[(index + rng.Next(clues.Length)) % clues.Length], sense == FinaleSense))
            .ToArray();
    }

    public bool SolveSecret(string sense)
    {
        ExpeditionSecret secret = Secrets.Single(item => item.SenseKey == sense);
        if (!secret.IsAvailable(DefeatedGuardians) || secret.State >= SecretState.Solved)
            return false;
        secret.State = SecretState.DungeonOpen;
        return true;
    }

    public bool EnterDungeon(string sense, Vector2 returnPosition)
    {
        ExpeditionSecret secret = Secrets.Single(item => item.SenseKey == sense);
        if (secret.State != SecretState.DungeonOpen)
            return false;
        ActiveDungeonSecret = secret;
        SuspendedReturnPosition = returnPosition;
        return true;
    }

    public bool CompleteDungeon()
    {
        if (ActiveDungeonSecret is null)
            return false;
        ActiveDungeonSecret.State = SecretState.GuardianDefeated;
        ActiveDungeonSecret = null;
        return true;
    }
}
