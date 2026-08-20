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

    /// <summary>
    /// Right-stick aim *direction* (unit-length once past the deadzone, else
    /// Vector2.Zero) -- magnitude is not distance/speed here. RotBoiGame
    /// combines this with a fixed on-screen radius to place an orbiting
    /// reticle around the player, rather than treating the raw stick vector
    /// as a free-roaming virtual mouse position.
    /// </summary>
    public static Vector2 ControllerAim { get; set; }

    /// <summary>True while the right trigger is held past its threshold (fires, mirroring MouseDown).</summary>
    public static bool ControllerFireHeld { get; set; }

    /// <summary>
    /// Where the aim reticle should actually render this frame: the real
    /// cursor position, or -- while beta controller support has the right
    /// stick deflected -- the fixed-radius orbiting point RotBoiGame
    /// computes from InputState.ControllerAim. Set once per frame alongside
    /// bullet-creation's own aim target so Draw doesn't recompute it.
    /// </summary>
    public static Point EffectiveAimPosition { get; set; }
    public static bool ControllerDashPressed { get; set; }
    public static bool ControllerAutofirePressed { get; set; }
    public static bool ControllerInteractPressed { get; set; }
    public static bool ControllerPausePressed { get; set; }
    public static bool ControllerViewPressed { get; set; }
    public static bool ControllerConfirmPressed { get; set; }
    public static bool ControllerBackPressed { get; set; }
    public static bool ControllerDpadUpPressed { get; set; }
    public static bool ControllerDpadDownPressed { get; set; }
    public static bool ControllerDpadLeftPressed { get; set; }
    public static bool ControllerDpadRightPressed { get; set; }
    public static bool UiUpPressed { get; set; }
    public static bool UiDownPressed { get; set; }
    public static bool UiLeftPressed { get; set; }
    public static bool UiRightPressed { get; set; }
}
