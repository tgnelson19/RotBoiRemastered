using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RotBoiRemastered.Core;

/// <summary>
/// Live keyboard, mouse, and first-controller input state derived once per
/// frame by RotBoiGame. Menus.cs/LevelingHandler.cs don't read this directly --
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

    /// <summary>Change in the mouse's scroll wheel value this frame (positive = scrolled up/away from the user).</summary>
    public static int ScrollWheelDelta { get; set; }

    public static Vector2 ControllerMove { get; set; }
    public static Vector2 ControllerAim { get; set; }
    public static bool ControllerDashPressed { get; set; }
    public static bool ControllerAutofirePressed { get; set; }
    public static bool ControllerPausePressed { get; set; }
}
