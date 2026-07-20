namespace RotBoiRemastered.Core;

/// <summary>
/// Mirrors variableHolster.States from the Python original. Keep this enum's
/// members in lockstep with that one while the port is in progress.
/// </summary>
public enum GameState
{
    TitleScreen,
    GameRun,
    Leveling,
    Reforging,
    Paused,
    Results,
    Soul,
}
