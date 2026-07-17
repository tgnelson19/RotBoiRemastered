using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RotBoiRemastered.Core;

/// <summary>
/// Minimal slice of variableHolster.py's input-tracking globals (keyPressed,
/// keys, mouseX/mouseY, mouseDown/mousePressed) -- just enough for
/// Keybinds.cs and the UI/ menu screens to check input against. This will
/// grow into a full port of variableHolster.py (controller state, screen
/// dimensions, frame timing) when that module gets its own pass; kept
/// intentionally small for now rather than porting the whole thing
/// speculatively. Menus.cs/LevelingHandler.cs don't read this directly --
/// they take mouse position/state as explicit method parameters (matching
/// UiTheme.DrawButton's existing shape), so only the eventual game-loop
/// entry point needs to touch this class at all.
/// </summary>
public static class InputState
{
    /// <summary>Keys that produced a KeyDown event this frame (edge-triggered).</summary>
    public static HashSet<Keys> KeysPressed { get; } = new();

    /// <summary>Currently held keyboard state (level-triggered).</summary>
    public static KeyboardState KeyboardState { get; set; }

    /// <summary>Current mouse position in screen space.</summary>
    public static Point MousePosition { get; set; }

    /// <summary>True while the left mouse button is held (level-triggered).</summary>
    public static bool MouseDown { get; set; }

    /// <summary>True on the frame the left mouse button was first pressed (edge-triggered).</summary>
    public static bool MousePressed { get; set; }
}
