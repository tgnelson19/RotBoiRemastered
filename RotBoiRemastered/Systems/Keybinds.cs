using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.Systems;

/// <summary>
/// Rebindable action -> key mapping, persisted through GameProfile. Ported
/// from keybinds.py. Escape is reserved: it can never be bound to an action,
/// and pressing it while listening for a new key clears that action's
/// binding instead of setting it (see menus.py's rebind UI once ported).
///
/// Unlike Upgrades.cs/Items.cs, this has a real MonoGame dependency (the
/// Keys enum, and InputState for live keyboard state) just as keybinds.py
/// depends on pygame's key constants and variableHolster's live input --
/// it's not one of the "pygame-free" modules.
/// </summary>
public static class Keybinds
{
    public static readonly IReadOnlyList<(string Id, string Label, Keys Default)> Actions = new[]
    {
        ("move_up", "Move Up", Keys.W),
        ("move_left", "Move Left", Keys.A),
        ("move_down", "Move Down", Keys.S),
        ("move_right", "Move Right", Keys.D),
        ("dash", "Dash", Keys.Space),
        ("rotate_left", "Rotate Camera Left", Keys.Q),
        ("rotate_right", "Rotate Camera Right", Keys.E),
        ("camera_reset", "Reset Camera", Keys.X),
        ("zoom_out", "Zoom Camera Out", Keys.O),
        ("zoom_in", "Zoom Camera In", Keys.P),
        ("autofire", "Toggle Autofire", Keys.I),
        ("interact", "Interact", Keys.F),
        ("extract", "Extract to The Mind", Keys.R),
        ("hud_toggle", "Toggle HUD Detail", Keys.Tab),
        ("console_toggle", "DEV: Toggle Console", Keys.OemTilde),
        // Stash panel only (see GameSession.SwapStashSlotWithEquipment): instantly
        // trades the item in that stash slot with whatever's equipped in its slot
        // type. Default to the number row, 1 per slot, matching the panel's own
        // left-to-right order.
        ("stash_swap_1", "Stash: Swap Slot 1", Keys.D1),
        ("stash_swap_2", "Stash: Swap Slot 2", Keys.D2),
        ("stash_swap_3", "Stash: Swap Slot 3", Keys.D3),
        ("stash_swap_4", "Stash: Swap Slot 4", Keys.D4),
        ("stash_swap_5", "Stash: Swap Slot 5", Keys.D5),
        ("stash_swap_6", "Stash: Swap Slot 6", Keys.D6),
        ("stash_swap_7", "Stash: Swap Slot 7", Keys.D7),
        ("stash_swap_8", "Stash: Swap Slot 8", Keys.D8),
    };

    public static readonly IReadOnlyDictionary<string, Keys> ActionDefaults =
        Actions.ToDictionary(action => action.Id, action => action.Default);

    public static readonly IReadOnlyDictionary<string, string> ActionLabels =
        Actions.ToDictionary(action => action.Id, action => action.Label);

    public static Dictionary<string, Keys?> Bindings { get; private set; } = Load();

    private static Dictionary<string, Keys?> Load()
    {
        var loaded = ActionDefaults.ToDictionary(kv => kv.Key, kv => (Keys?)kv.Value);
        foreach (var (actionId, key) in GameProfile.Profile.Keybinds)
        {
            if (loaded.ContainsKey(actionId))
                loaded[actionId] = key.HasValue ? (Keys)key.Value : null;
        }
        return loaded;
    }

    private static void Save()
    {
        GameProfile.Profile.Keybinds = Bindings.ToDictionary(
            kv => kv.Key, kv => kv.Value.HasValue ? (int?)kv.Value.Value : null);
        GameProfile.SaveProfile();
    }

    public static Keys? KeyFor(string actionId) => Bindings.GetValueOrDefault(actionId);

    /// <summary>True if this action's bound key was pressed this frame (edge-triggered).</summary>
    public static bool Pressed(string actionId)
    {
        var key = KeyFor(actionId);
        return key.HasValue && InputState.KeysPressed.Contains(key.Value);
    }

    /// <summary>True if this action's bound key is currently held down.</summary>
    public static bool Held(string actionId)
    {
        var key = KeyFor(actionId);
        return key.HasValue && InputState.KeyboardState.IsKeyDown(key.Value);
    }

    /// <summary>Bind actionId to key, clearing any other action already using it.</summary>
    public static void SetBinding(string actionId, Keys key)
    {
        if (!Bindings.ContainsKey(actionId) || key == Keys.Escape)
            return;
        foreach (var otherId in Bindings.Keys.ToList())
        {
            if (otherId != actionId && Bindings[otherId] == key)
                Bindings[otherId] = null;
        }
        Bindings[actionId] = key;
        Save();
    }

    public static void ClearBinding(string actionId)
    {
        if (Bindings.ContainsKey(actionId))
        {
            Bindings[actionId] = null;
            Save();
        }
    }

    public static void ResetDefaults()
    {
        Bindings = ActionDefaults.ToDictionary(pair => pair.Key, pair => (Keys?)pair.Value);
        Save();
    }

    /// <summary>
    /// Human-readable key name. Not byte-identical to pygame's key.name()
    /// output (e.g. "LEFTSHIFT" here vs. "left shift" there) but equally
    /// legible; there's no requirement these strings match across the port.
    /// </summary>
    public static string LabelForKey(Keys? key) =>
        key is null ? "UNBOUND" : key.Value.ToString().ToUpperInvariant();
}
