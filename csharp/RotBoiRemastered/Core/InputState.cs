using Microsoft.Xna.Framework.Input;

namespace RotBoiRemastered.Core;

/// <summary>
/// Minimal slice of variableHolster.py's input-tracking globals (keyPressed,
/// keys) -- just enough for Keybinds.cs to check bindings against. This will
/// grow into a full port of variableHolster.py (mouse/controller state,
/// screen dimensions, frame timing) when that module gets its own pass;
/// kept intentionally small for now rather than porting the whole thing
/// speculatively.
/// </summary>
public static class InputState
{
    /// <summary>Keys that produced a KeyDown event this frame (edge-triggered).</summary>
    public static HashSet<Keys> KeysPressed { get; } = new();

    /// <summary>Currently held keyboard state (level-triggered).</summary>
    public static KeyboardState KeyboardState { get; set; }
}
