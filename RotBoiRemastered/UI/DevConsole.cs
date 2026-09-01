using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;
using ArgumentProvider = System.Func<RotBoiRemastered.Systems.GameSession,
    System.Collections.Generic.IReadOnlyList<string>,
    System.Collections.Generic.IReadOnlyList<(string Value, string Label)>>;

namespace RotBoiRemastered.UI;

/// <summary>What Update just decided should happen -- RotBoiGame.cs applies the actual state transition, matching Menus.cs's return-a-result shape (see /extract's doc comment on HandleExtract).</summary>
public enum ConsoleActionKind { None, ExtractRequested }

public readonly record struct ConsoleResult(ConsoleActionKind Kind = ConsoleActionKind.None);

/// <summary>
/// Quake-style admin command line, toggled by the "console_toggle" keybind
/// (backtick by default). Opening it pauses simulation -- RotBoiGame skips
/// calling the active state's Update entirely while IsOpen, since typing
/// hijacks WASD/etc. for text rather than movement/firing, and swallowing
/// input without pausing would leave the player defenseless mid-command.
/// Draw still runs underneath as normal (see RotBoiGame.Draw), so the arena
/// stays visible behind the console panel.
///
/// Supported commands (all case-insensitive on both command and item name;
/// item names with spaces need quotes, e.g. /give 2 "Bow of Dread"):
///   /spawn &lt;count&gt; "&lt;item name&gt;" [rarity]  -- drops a loot crate at the player
///   /give &lt;count&gt; "&lt;item name&gt;" [rarity]   -- fills empty inventory slots directly
///   /god                                    -- toggles RunState.BossDebugInvincible
///   /boss                                   -- forces the level's boss encounter to start
///   /levelup                                -- forces an immediate level up
///   /killall                                -- kills every enemy in the arena, boss included
///   /extract                                -- runs the same sequence as the pause menu's
///                                               Extract button, bypassing its BeaudisDefeated gate
///   /vfxgallery [0-100] [path] [tier]       -- spawns the visual-language gallery
///   /testphase &lt;key&gt;                        -- jump the active boss to a specific
///                                               phase/pattern/sequence
///   /help                                   -- lists the above
///
/// Autocomplete: pressing "/" opens a live, arrow-key-navigable, type-to-
/// filter dropdown of every command above (see <see cref="Commands"/>).
/// Pressing Enter on one that takes no arguments runs it immediately; one
/// that does instead fills "/name " into the buffer and swaps in a dropdown
/// of that argument's own candidates (item names, rarities, a boss's
/// test-phase keys, ...) -- selecting one of those chains the same way into
/// the next argument's dropdown, or runs the finished command once there is
/// nothing left to fill in. Pressing Tab instead of Enter fills in the
/// selected candidate the same way but never runs anything, even on that
/// last, otherwise-executing slot -- useful for lining up a command before
/// committing to it. Down past the last visible row (and Up past the first)
/// scrolls the dropdown to reveal candidates <see cref="MaxCommandMenuRows"/>
/// would otherwise hide, one row at a time; the mouse scroll wheel moves the
/// selection the same way Up/Down do (see <see cref="Update"/>'s
/// <c>scrollWheelDelta</c> parameter). See <see cref="RefreshCommandMenu"/>
/// and <see cref="SyncScrollToSelection"/>.
/// </summary>
public sealed class DevConsole
{
    private const int MaxHistoryLines = 50;
    private const int VisibleHistoryLines = 10;
    private const int MaxBufferLength = 80;
    private const int MaxCommandMenuRows = 8;
    /// <summary>MonoGame's ScrollWheelValue changes by this much per physical notch -- same constant RotBoiGame's camera zoom and SoulHub's dossier scroll divide by.</summary>
    private const int WheelNotchSize = 120;

    /// <summary>
    /// One authored command: its display name, the one-line usage shown in
    /// both /help and the top-level "/" dropdown, and one candidate-provider
    /// per positional argument (null for an argument with no sensible
    /// enumerable set, e.g. /spawn's free-form &lt;count&gt;). An empty list
    /// means "takes no arguments" -- see <see cref="RefreshCommandMenu"/>'s
    /// doc comment for what that changes about selecting it from a dropdown.
    /// </summary>
    private sealed record ConsoleCommandSpec(
        string Name, string Usage, IReadOnlyList<ArgumentProvider?> ArgumentProviders);

    private enum MenuMode { None, CommandName, Argument }

    private static readonly IReadOnlyList<ConsoleCommandSpec> Commands = new ConsoleCommandSpec[]
    {
        new("spawn", "/spawn <count> \"<item>\" [rarity] -- drop a crate at your position",
            new ArgumentProvider?[] { null, ItemNameOptions, RarityOptions }),
        new("give", "/give <count> \"<item>\" [rarity] -- add straight to inventory",
            new ArgumentProvider?[] { null, ItemNameOptions, RarityOptions }),
        new("god", "/god -- toggle invincibility", Array.Empty<ArgumentProvider?>()),
        new("boss", "/boss -- force the boss encounter to start", Array.Empty<ArgumentProvider?>()),
        new("levelup", "/levelup -- force an immediate level up", Array.Empty<ArgumentProvider?>()),
        new("killall", "/killall -- kill every enemy, boss included", Array.Empty<ArgumentProvider?>()),
        new("extract", "/extract -- end the run as an extraction", Array.Empty<ArgumentProvider?>()),
        new("vfxgallery", "/vfxgallery [0-100] [path] [tier] -- spawn the visual-language gallery",
            Array.Empty<ArgumentProvider?>()),
        new("testphase", "/testphase <key> -- jump the active boss to a phase/pattern/sequence",
            new ArgumentProvider?[] { TestPhaseOptions }),
        new("help", "/help -- list every command", Array.Empty<ArgumentProvider?>()),
    };

    private readonly List<string> _history = new();
    private string _buffer = "";
    private double _seconds;
    private bool _open;

    /// <summary>
    /// Live-filtered candidates for the current buffer, recomputed each
    /// <see cref="Update"/> call (see <see cref="RefreshCommandMenu"/>) and
    /// read back by <see cref="Draw"/> -- Draw itself has no GameSession
    /// reference, so it can't recompute this on its own.
    /// </summary>
    private readonly List<(string Value, string Label)> _menuCandidates = new();
    private int _menuSelection;
    /// <summary>
    /// Index of the first candidate currently drawn -- lets Down keep moving
    /// <see cref="_menuSelection"/> past the last visible row without
    /// wrapping the on-screen window back to the top; see
    /// <see cref="SyncScrollToSelection"/>.
    /// </summary>
    private int _menuScrollOffset;
    private MenuMode _menuMode;
    /// <summary>Set alongside <see cref="_menuMode"/> being Argument -- which command's dropdown is showing and which of its argument slots.</summary>
    private ConsoleCommandSpec? _menuCommand;
    private string? _menuCommandName;
    private int _menuArgIndex = -1;
    /// <summary>Already-confirmed argument tokens before the slot currently being filled -- carried into the rebuilt command when a candidate is selected.</summary>
    private IReadOnlyList<string> _menuPrecedingArgs = Array.Empty<string>();
    /// <summary>
    /// Identifies *what* is currently being filtered (mode + command + slot +
    /// filter text) -- a change resets <see cref="_menuSelection"/> to 0 and
    /// clears <see cref="_menuDismissed"/>, same as <see cref="Update"/>'s
    /// Up/Down navigation working from a stable list frame to frame.
    /// </summary>
    private string _menuStateKey = "";
    /// <summary>
    /// Set by <see cref="DismissCommandMenu"/> (the console's Escape handler
    /// in RotBoiGame, so a first Escape closes just the dropdown rather than
    /// the whole console) and cleared the moment <see cref="_menuStateKey"/>
    /// changes again, so resuming typing brings the menu back.
    /// </summary>
    private bool _menuDismissed;

    public bool IsOpen => _open;

    /// <summary>Test-only window onto the live dropdown state (selection index, first drawn row, candidate count) -- exposed via `InternalsVisibleTo` rather than public since nothing outside tests and this class's own Draw/Update needs it.</summary>
    internal (int Selection, int ScrollOffset, int CandidateCount) DebugMenuState =>
        (_menuSelection, _menuScrollOffset, _menuCandidates.Count);

    public void Open()
    {
        _open = true;
        _buffer = "";
    }

    public void Close()
    {
        _open = false;
        ResetBuffer();
    }

    /// <summary>Fed from RotBoiGame's Window.TextInput subscription (see Initialize) -- MonoGame's only source of actual typed characters, as opposed to InputState's raw Keys tracking.</summary>
    public void HandleTextInput(char character)
    {
        // Backtick/tilde is reserved for the toggle keybind, not typed text
        // (dodges any same-frame ordering question between the TextInput
        // event and the KeysPressed-driven Open() call); control characters
        // (Backspace, Enter, ...) are handled via KeysPressed in Update instead.
        if (!_open || char.IsControl(character) || character is '`' or '~')
            return;
        if (_buffer.Length < MaxBufferLength)
            _buffer += character;
    }

    /// <summary>
    /// Call once per frame regardless of IsOpen. Backspace/Enter come through
    /// KeysPressed (edge-triggered), same as every other bound action in this
    /// codebase. <paramref name="scrollWheelDelta"/> is the raw frame-to-frame
    /// change in MonoGame's cumulative <c>ScrollWheelValue</c> (see
    /// RotBoiGame.CollectInput's <c>InputState.ScrollWheelDelta</c>) -- +-120
    /// per notch -- and moves the dropdown selection the same way Up/Down do,
    /// scrolling the window with it; 0 (the default) means no caller has
    /// wired up a mouse, which every existing call site relied on before this
    /// parameter existed.
    /// </summary>
    public ConsoleResult Update(GameSession? session, IReadOnlySet<Keys> keysPressed, double elapsedSeconds, int scrollWheelDelta = 0)
    {
        _seconds += elapsedSeconds;
        if (!_open)
            return default;
        if (keysPressed.Contains(Keys.Back) && _buffer.Length > 0)
            _buffer = _buffer[..^1];

        RefreshCommandMenu(session);
        if (_menuCandidates.Count > 0)
        {
            if (keysPressed.Contains(Keys.Down))
            {
                _menuSelection = (_menuSelection + 1) % _menuCandidates.Count;
                SyncScrollToSelection();
            }
            if (keysPressed.Contains(Keys.Up))
            {
                _menuSelection = (_menuSelection - 1 + _menuCandidates.Count) % _menuCandidates.Count;
                SyncScrollToSelection();
            }
            if (scrollWheelDelta != 0)
            {
                // Scrolling the wheel away from you (a negative delta) steps
                // the selection down, the same direction Down does; toward
                // you steps it up -- matches every other scrollable list in
                // this codebase (see SoulHub's dossier scroll). Applied one
                // notch at a time (rather than jumping straight to the final
                // index) so a multi-notch scroll drags the visible window
                // along exactly the way that many individual Down/Up presses
                // would, instead of only snapping it into view at the end.
                int steps = -scrollWheelDelta / WheelNotchSize;
                int count = _menuCandidates.Count;
                for (int i = 0; i < Math.Abs(steps); i++)
                {
                    _menuSelection = steps > 0
                        ? (_menuSelection + 1) % count
                        : (_menuSelection - 1 + count) % count;
                    SyncScrollToSelection();
                }
            }
            // Tab fills the selected candidate into the buffer exactly like
            // Enter does, but never executes -- even a no-argument command or
            // an argument's last slot, which Enter would run immediately.
            // That's the whole point: line up a command/argument without
            // committing to running it yet.
            if (keysPressed.Contains(Keys.Tab) && TryComposeSelection(out string tabBuffer, out _))
            {
                _buffer = tabBuffer + " ";
                return default;
            }
        }

        if (!keysPressed.Contains(Keys.Enter) || (_buffer.Length == 0 && _menuCandidates.Count == 0))
            return default;

        if (!TryComposeSelection(out string newBuffer, out bool isTerminal))
        {
            var result = Execute(_buffer.Trim(), session);
            ResetBuffer();
            return result;
        }

        if (isTerminal)
        {
            // Nothing left to fill in -- either the selected command takes
            // no arguments, or this was the last argument slot -- so Enter
            // runs it immediately rather than making the developer press
            // Enter a second time on a finished prompt.
            var result2 = Execute(newBuffer, session);
            ResetBuffer();
            return result2;
        }
        _buffer = newBuffer + " ";
        return default;
    }

    /// <summary>
    /// Builds the buffer that selecting the current <see cref="_menuCandidates"/>
    /// entry would produce, shared by Enter (which executes it when
    /// <paramref name="isTerminal"/> comes back true) and Tab (which never
    /// executes, regardless of <paramref name="isTerminal"/>). Returns false
    /// when there is no menu to select from.
    /// </summary>
    private bool TryComposeSelection(out string newBuffer, out bool isTerminal)
    {
        newBuffer = "";
        isTerminal = false;
        if (_menuCandidates.Count == 0)
            return false;

        string selectedValue = _menuCandidates[Math.Clamp(_menuSelection, 0, _menuCandidates.Count - 1)].Value;
        if (_menuMode == MenuMode.CommandName)
        {
            ConsoleCommandSpec? spec = FindCommand(selectedValue);
            newBuffer = "/" + selectedValue;
            isTerminal = spec is null || spec.ArgumentProviders.Count == 0;
            return true;
        }

        // MenuMode.Argument: rebuild the command from every argument
        // confirmed so far plus this selection; terminal iff nothing is left
        // to fill in after it.
        var args = new List<string>(_menuPrecedingArgs) { selectedValue };
        newBuffer = ComposeCommand(_menuCommandName!, args);
        isTerminal = _menuArgIndex + 1 >= _menuCommand!.ArgumentProviders.Count;
        return true;
    }

    /// <summary>
    /// Recomputes <see cref="_menuCandidates"/> (and the mode/slot fields
    /// Update's Enter handler reads) from the current buffer:
    /// - Buffer is "/" plus no space yet -&gt; <see cref="MenuMode.CommandName"/>,
    ///   candidates are every <see cref="Commands"/> entry whose name matches
    ///   what follows the "/".
    /// - Buffer is "/&lt;command&gt; " (a recognized command, at least one space
    ///   after it) -&gt; <see cref="MenuMode.Argument"/>, candidates come from
    ///   that command's provider for whichever argument slot the buffer is
    ///   currently on (the slot after the last complete token when the
    ///   buffer ends with a space, otherwise the slot still being typed,
    ///   filtered by its partial text).
    /// - Anything else (no session, empty buffer, unrecognized command, a
    ///   slot with no provider or past the command's last slot) -&gt; empty,
    ///   same as no dropdown at all.
    /// Tokenizes with <see cref="Tokenize"/> (quote-aware, same as Execute)
    /// rather than a raw space-split, so a quoted item name's internal
    /// spaces don't get mistaken for argument boundaries.
    /// </summary>
    private void RefreshCommandMenu(GameSession? session)
    {
        _menuCandidates.Clear();
        _menuMode = MenuMode.None;
        _menuCommand = null;
        _menuCommandName = null;
        _menuArgIndex = -1;
        _menuPrecedingArgs = Array.Empty<string>();

        if (session is null || !_buffer.StartsWith("/", StringComparison.Ordinal))
        {
            ApplyStateKey("");
            return;
        }

        if (!_buffer.Contains(' '))
        {
            string filter = _buffer[1..];
            _menuMode = MenuMode.CommandName;
            ApplyStateKey($"name{filter}");
            if (_menuDismissed)
                return;
            foreach (ConsoleCommandSpec spec in Commands)
            {
                if (filter.Length == 0 || spec.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    _menuCandidates.Add((spec.Name, spec.Usage));
            }
            ClampSelection();
            return;
        }

        List<string> tokens = Tokenize(_buffer);
        if (tokens.Count == 0)
        {
            ApplyStateKey("");
            return;
        }
        string commandName = tokens[0].TrimStart('/').ToLowerInvariant();
        ConsoleCommandSpec? spec2 = FindCommand(commandName);
        if (spec2 is null)
        {
            ApplyStateKey("");
            return;
        }

        List<string> argTokens = tokens.Skip(1).ToList();
        bool endsWithSpace = _buffer.Length > 0 && char.IsWhiteSpace(_buffer[^1]);
        int argIndex = endsWithSpace ? argTokens.Count : argTokens.Count - 1;
        string argFilter = endsWithSpace ? "" : argTokens[^1];
        List<string> precedingArgs = endsWithSpace
            ? argTokens
            : argTokens.Take(argTokens.Count - 1).ToList();

        _menuMode = MenuMode.Argument;
        _menuCommand = spec2;
        _menuCommandName = commandName;
        _menuArgIndex = argIndex;
        _menuPrecedingArgs = precedingArgs;
        ApplyStateKey($"arg{commandName}{argIndex}{argFilter}");

        if (_menuDismissed || argIndex < 0 || argIndex >= spec2.ArgumentProviders.Count)
            return;
        ArgumentProvider? provider = spec2.ArgumentProviders[argIndex];
        if (provider is null)
            return;
        foreach ((string value, string label) in provider(session, precedingArgs))
        {
            if (argFilter.Length == 0
                || value.Contains(argFilter, StringComparison.OrdinalIgnoreCase)
                || label.Contains(argFilter, StringComparison.OrdinalIgnoreCase))
            {
                _menuCandidates.Add((value, label));
            }
        }
        ClampSelection();
    }

    /// <summary>Resets selection and un-dismisses whenever what's being filtered actually changes -- called once per <see cref="RefreshCommandMenu"/> pass with a key describing the current mode/command/slot/filter-text.</summary>
    private void ApplyStateKey(string key)
    {
        if (key == _menuStateKey)
            return;
        _menuStateKey = key;
        _menuSelection = 0;
        _menuScrollOffset = 0;
        _menuDismissed = false;
    }

    private void ClampSelection()
    {
        if (_menuSelection >= _menuCandidates.Count)
            _menuSelection = Math.Max(0, _menuCandidates.Count - 1);
        SyncScrollToSelection();
    }

    /// <summary>
    /// Keeps <see cref="_menuScrollOffset"/> -- the first candidate <see
    /// cref="DrawCommandMenu"/> draws -- following <see cref="_menuSelection"/>:
    /// scrolls down one row at a time as Down moves the selection past the
    /// last visible row (revealing entries otherwise hidden by <see
    /// cref="MaxCommandMenuRows"/> overflow), scrolls up the same way, and
    /// snaps back to the top once the selection wraps back to index 0. Also
    /// reclamps against a shrinking candidate list (e.g. the developer typed
    /// another filter character), so a stale offset never points past the end.
    /// </summary>
    private void SyncScrollToSelection()
    {
        int shown = Math.Min(MaxCommandMenuRows, _menuCandidates.Count);
        if (_menuSelection < _menuScrollOffset)
            _menuScrollOffset = _menuSelection;
        else if (_menuSelection >= _menuScrollOffset + shown)
            _menuScrollOffset = _menuSelection - shown + 1;
        int maxScroll = Math.Max(0, _menuCandidates.Count - shown);
        _menuScrollOffset = Math.Clamp(_menuScrollOffset, 0, maxScroll);
    }

    private void ResetBuffer()
    {
        _buffer = "";
        _menuCandidates.Clear();
        _menuStateKey = "";
        _menuScrollOffset = 0;
        _menuDismissed = false;
    }

    private static ConsoleCommandSpec? FindCommand(string name) =>
        Commands.FirstOrDefault(spec => string.Equals(spec.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Rebuilds "/name arg1 arg2 ..." from scratch, quoting any argument that contains whitespace (item names) -- used instead of splicing the buffer in place, matching how a selection already replaced the whole partially-typed argument rather than trying to edit it mid-word.</summary>
    private static string ComposeCommand(string commandName, IEnumerable<string> args)
    {
        var parts = new List<string> { "/" + commandName };
        parts.AddRange(args.Select(arg => arg.Any(char.IsWhiteSpace) ? $"\"{arg}\"" : arg));
        return string.Join(' ', parts);
    }

    private static IReadOnlyList<(string Value, string Label)> ItemNameOptions(
        GameSession session, IReadOnlyList<string> precedingArgs) =>
        Items.Uniques.Select(item => (item.Name, $"{item.Name} (Unique)"))
            .Concat(Items.Definitions.Select(item => (item.Name, item.Name)))
            .ToArray();

    private static IReadOnlyList<(string Value, string Label)> RarityOptions(
        GameSession session, IReadOnlyList<string> precedingArgs) =>
        Upgrades.RarityOrder.Select(rarity => (rarity, rarity)).ToArray();

    private static IReadOnlyList<(string Value, string Label)> TestPhaseOptions(
        GameSession session, IReadOnlyList<string> precedingArgs) =>
        session.DebugTestPhaseOptions().Select(option => (option.Key, option.Label)).ToArray();

    /// <summary>
    /// Called from RotBoiGame's Escape handling: dismisses the "/" dropdown
    /// without closing the console or touching the typed buffer, returning
    /// true iff there was a menu open to dismiss. The caller only proceeds
    /// to close the whole console when this returns false, so the first
    /// Escape while suggestions are showing just clears them.
    /// </summary>
    public bool DismissCommandMenu()
    {
        if (_menuCandidates.Count == 0 || _menuDismissed)
            return false;
        _menuDismissed = true;
        _menuCandidates.Clear();
        return true;
    }

    private void Log(string message)
    {
        _history.Add(message);
        if (_history.Count > MaxHistoryLines)
            _history.RemoveAt(0);
    }

    private ConsoleResult Execute(string input, GameSession? session)
    {
        Log("> " + input);
        var tokens = Tokenize(input);
        if (tokens.Count == 0)
            return default;
        string command = tokens[0].TrimStart('/').ToLowerInvariant();

        if (command == "help")
        {
            Log("/spawn <count> \"<item name>\" [rarity]  -- drop a crate at your position");
            Log("/give <count> \"<item name>\" [rarity]   -- add straight to inventory");
            Log("/god                                    -- toggle invincibility");
            Log("/boss                                   -- force the boss encounter to start");
            Log("/levelup                                -- force an immediate level up");
            Log("/killall                                -- kill every enemy in the arena, boss included");
            Log("/extract                                -- end the run as an extraction");
            Log("/vfxgallery [0-100] [path] [tier]       -- spawn the visual-language gallery");
            Log("/testphase <key>                        -- jump the active boss to a phase/pattern/sequence");
            Log("Press \"/\" for a dropdown of every command; keep typing to filter it.");
            return default;
        }
        if (session is null)
        {
            Log("No active run.");
            return default;
        }
        switch (command)
        {
            case "spawn": return HandleSpawn(tokens, session);
            case "give": return HandleGive(tokens, session);
            case "god": return HandleGod(session);
            case "boss": return HandleBoss(session);
            case "levelup": return HandleLevelUp(session);
            case "killall": return HandleKillAll(session);
            case "extract": return HandleExtract();
            case "vfxgallery": return HandleVfxGallery(tokens, session);
            case "testphase": return HandleTestPhase(tokens, session);
            default:
                Log($"Unknown command: {command} (try /help)");
                return default;
        }
    }

    private ConsoleResult HandleSpawn(IReadOnlyList<string> tokens, GameSession session)
    {
        if (!TryParseItemArgs(tokens, "/spawn", out int count, out var definition, out string rarity))
            return default;
        var drops = Enumerable.Range(0, count).Select(_ => Items.GenerateDrop(definition, rarity)).ToList();
        session.SpawnLootCrate(session.PlayerWorldCenter.X, session.PlayerWorldCenter.Y, drops);
        Log($"Spawned {count}x {definition.Name} ({rarity}).");
        return default;
    }

    private ConsoleResult HandleGive(IReadOnlyList<string> tokens, GameSession session)
    {
        if (!TryParseItemArgs(tokens, "/give", out int count, out var definition, out string rarity))
            return default;
        int given = 0;
        var inventory = session.State.Inventory;
        for (int i = 0; i < inventory.Count && given < count; i++)
        {
            if (inventory[i] is not null)
                continue;
            inventory[i] = Items.GenerateDrop(definition, rarity);
            given++;
        }
        Log(given == count
            ? $"Gave {given}x {definition.Name} ({rarity})."
            : $"Gave {given}x {definition.Name} ({rarity}) -- inventory full, {count - given} not given.");
        return default;
    }

    private ConsoleResult HandleGod(GameSession session)
    {
        session.State.BossDebugInvincible = !session.State.BossDebugInvincible;
        Log($"God mode: {(session.State.BossDebugInvincible ? "ON" : "OFF")}");
        return default;
    }

    private ConsoleResult HandleBoss(GameSession session)
    {
        if (session.State.ActiveBoss is not null || session.State.BossDebugRequested)
        {
            Log("A boss encounter is already active or pending.");
            return default;
        }
        session.State.BossDebugRequested = true;
        Log("Boss encounter requested.");
        return default;
    }

    private ConsoleResult HandleLevelUp(GameSession session)
    {
        session.DebugForceLevelUp();
        Log("Added enough stored EXP for a level up.");
        return default;
    }

    /// <summary>
    /// Jumps the active boss to a specific phase/pattern/sequence. Meant to
    /// be driven through the "/" dropdown (type "/testphase " and either
    /// arrow down to a candidate and press Enter, or keep typing to filter
    /// first) -- see <see cref="RefreshCommandMenu"/> -- but also accepts a
    /// key typed out in full, e.g. "/testphase blender".
    /// </summary>
    private ConsoleResult HandleTestPhase(IReadOnlyList<string> tokens, GameSession session)
    {
        if (session.State.ActiveBoss is null)
        {
            Log("No active boss to test.");
            return default;
        }
        if (session.DebugTestPhaseOptions().Count == 0)
        {
            Log("The active boss has no debug test phases.");
            return default;
        }
        if (tokens.Count < 2)
        {
            Log("Usage: /testphase <key> -- type a space after \"/testphase\" for the list.");
            return default;
        }
        string key = tokens[1];
        if (session.DebugJumpToTestPhase(key))
            Log($"Jumped to test phase: {key}");
        else
            Log($"Unknown test phase: {key} (space after \"/testphase\" for the list)");
        return default;
    }

    private ConsoleResult HandleVfxGallery(
        IReadOnlyList<string> tokens,
        GameSession session)
    {
        int next = 1;
        if (tokens.Count > 1)
        {
            if (int.TryParse(tokens[1], out int percent))
            {
                if (percent is < 0 or > 100)
                {
                    Log("Usage: /vfxgallery [0-100] [path] [easy|medium|hard]");
                    return default;
                }
                GameProfile.Profile.VisualEffectsIntensity = percent / 100.0;
                GameProfile.SaveProfile();
                next++;
            }
        }
        string path = tokens.Count > next
            ? tokens[next++].ToLowerInvariant()
            : GamePaths.Active().Key;
        string tier = tokens.Count > next
            ? tokens[next].ToLowerInvariant()
            : "easy";
        if (!GamePaths.PathsByKey.ContainsKey(path)
            || !SoulVisualLanguage.EnemyTiers.Contains(tier))
        {
            Log("Usage: /vfxgallery [0-100] [path] [easy|medium|hard]");
            return default;
        }
        int spawned = session.DebugSpawnVfxGallery(path, tier);
        Log($"Spawned {spawned} {path}/{tier} visual samples at {GameProfile.Profile.VisualEffectsIntensity * 100:0}%.");
        return default;
    }

    /// <summary>
    /// Sets Hp straight to 0 on every enemy instead of routing through
    /// TakeDamage -- bosses like Beaudis clamp/refuse damage mid-scripted-phase
    /// (see Beaudis.TakeDamage's Dying/SurvivalActive/_phaseProtectionTimer
    /// gate, which a real kill only ever gets past via a dedicated Dying flag,
    /// never through repeated TakeDamage calls), so trying to "deal lethal
    /// damage" the normal way could leave a boss stuck alive. GameSession's
    /// own per-frame sweep (HandleDamagingEnemies: `if (enemy.IsDead())
    /// deadEnemies.Add(enemy)`) already treats Hp &lt;= 0 as equally valid to a
    /// TakeDamage-reported kill for every enemy type with no override needed,
    /// so it picks these up next frame and runs the exact same loot/XP/boss-
    /// defeat pipeline a normal kill would.
    /// </summary>
    private ConsoleResult HandleKillAll(GameSession session)
    {
        int count = session.State.EnemyHolster.Count;
        foreach (var enemy in session.State.EnemyHolster)
            enemy.Hp = 0;
        Log(count == 0 ? "No enemies to kill." : $"Killed {count} enem{(count == 1 ? "y" : "ies")}.");
        return default;
    }

    /// <summary>
    /// Mirrors RotBoiGame.UpdatePaused's MenuAction.Extract case exactly
    /// (RunOutcome, RecordExtraction, SyncCarriedItems, RecordRun) but
    /// bypasses that button's BeaudisDefeated gate -- the whole point of a
    /// dev command. The actual State = GameState.Results assignment has to
    /// happen in RotBoiGame (this class has no reference to it), so this
    /// just reports the request back through ConsoleResult.
    /// </summary>
    private ConsoleResult HandleExtract()
    {
        Log("Extracting...");
        return new ConsoleResult(ConsoleActionKind.ExtractRequested);
    }

    private bool TryParseItemArgs(IReadOnlyList<string> tokens, string usage, out int count, out ItemDefinition definition, out string rarity)
    {
        count = 0;
        definition = null!;
        rarity = "";
        if (tokens.Count < 3 || !int.TryParse(tokens[1], out count) || count <= 0)
        {
            Log($"Usage: {usage} <count> \"<item name>\" [rarity]");
            return false;
        }
        var found = FindItemDefinition(tokens[2]);
        if (found is null)
        {
            Log($"Unknown item: {tokens[2]}");
            return false;
        }
        definition = found;
        rarity = ResolveRarity(found, tokens.Count > 3 ? tokens[3] : null);
        return true;
    }

    private static ItemDefinition? FindItemDefinition(string name) =>
        Items.Uniques.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Items.Definitions.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Uniques always drop at "Unique" rarity regardless of any rarity token given; a regular item takes the requested tier if it's a real one, else defaults to Legendary -- admin testing usually wants a fuller Modifier ladder unlocked, not a Common roll.</summary>
    private static string ResolveRarity(ItemDefinition definition, string? requested)
    {
        if (Items.UniquesByName.ContainsKey(definition.Name))
            return "Unique";
        if (requested is not null)
        {
            var match = Upgrades.RarityOrder.FirstOrDefault(r => string.Equals(r, requested, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }
        return "Legendary";
    }

    /// <summary>Splits on whitespace, treating a "double-quoted span" as one token -- needed since most item names contain spaces (e.g. "Bow of Dread").</summary>
    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;
            if (i >= input.Length)
                break;
            if (input[i] == '"')
            {
                int end = input.IndexOf('"', i + 1);
                if (end < 0)
                    end = input.Length;
                tokens.Add(input[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                int start = i;
                while (i < input.Length && !char.IsWhiteSpace(input[i]))
                    i++;
                tokens.Add(input[start..i]);
            }
        }
        return tokens;
    }

    public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
    {
        if (!_open)
            return;
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        int Px(float value) => Math.Max(1, (int)MathF.Round(value * scale));
        int panelHeight = Math.Max(Px(190), (int)(screenHeight * .32f));
        var panel = new Rectangle(0, 0, screenWidth, panelHeight);
        // Same border/shadow/top-highlight treatment as every other overlay
        // in the game (UiTheme.DrawPanel), rather than a bare fill -- this
        // is the one overlay that used to skip it entirely.
        UiTheme.DrawPanel(spriteBatch, panel, UiTheme.Void * .92f, UiTheme.Border, shadow: 4);

        int y = Px(10), lineStep = Px(18), inputHeight = Px(30);
        int capacity = Math.Max(1, (panelHeight - inputHeight - Px(14)) / lineStep);
        int shown = Math.Min(Math.Min(VisibleHistoryLines, capacity), _history.Count);
        for (int index = _history.Count - shown; index < _history.Count; index++)
        {
            UiTheme.DrawText(spriteBatch, _history[index], 12 * scale, UiTheme.Cream, new Vector2(Px(12), y));
            y += lineStep;
        }

        var inputRect = new Rectangle(0, panelHeight - inputHeight, screenWidth, inputHeight);
        Primitives2D.FillRect(spriteBatch, inputRect, UiTheme.Panel);
        bool caretOn = (int)(_seconds * 2) % 2 == 0;
        UiTheme.DrawText(spriteBatch, $"> {_buffer}{(caretOn ? "_" : "")}", 13 * scale, UiTheme.Text,
            new Vector2(Px(12), inputRect.Y + Px(6)));

        if (_menuCandidates.Count > 0)
            DrawCommandMenu(spriteBatch, screenWidth, inputRect, scale);
    }

    /// <summary>
    /// The "/" autocomplete dropdown, anchored directly under the input bar
    /// -- up to <see cref="MaxCommandMenuRows"/> candidates starting at <see
    /// cref="_menuScrollOffset"/>, the current one (<see cref="_menuSelection"/>,
    /// moved by Up/Down in <see cref="Update"/>) highlighted. Pressing Down
    /// past the last visible row scrolls this window down one entry at a
    /// time rather than hiding everything past <see cref="MaxCommandMenuRows"/>
    /// behind an uninspectable "N more" count; the hint lines above/below
    /// only report what's still scrolled out of view in that direction.
    /// </summary>
    private void DrawCommandMenu(SpriteBatch spriteBatch, int screenWidth, Rectangle inputRect, float scale)
    {
        int Px(float value) => Math.Max(1, (int)MathF.Round(value * scale));
        int rowHeight = Px(18);
        int shown = Math.Min(MaxCommandMenuRows, _menuCandidates.Count);
        int scrollOffset = Math.Clamp(_menuScrollOffset, 0, Math.Max(0, _menuCandidates.Count - shown));
        bool moreAbove = scrollOffset > 0;
        bool moreBelow = scrollOffset + shown < _menuCandidates.Count;
        int menuHeight = shown * rowHeight + Px(6) + (moreAbove ? Px(14) : 0) + (moreBelow ? Px(14) : 0);
        var menuRect = new Rectangle(inputRect.X, inputRect.Bottom, screenWidth, menuHeight);
        UiTheme.DrawPanel(spriteBatch, menuRect, UiTheme.Panel * .97f, UiTheme.Border, shadow: 3);

        int y = menuRect.Y + Px(3);
        if (moreAbove)
        {
            UiTheme.DrawText(spriteBatch, $"^ {scrollOffset} more above (scroll up)",
                11 * scale, UiTheme.Muted, new Vector2(Px(12), y + Px(2)));
            y += Px(14);
        }
        for (int row = 0; row < shown; row++)
        {
            int index = scrollOffset + row;
            bool isSelected = index == _menuSelection;
            (string value, string label) = _menuCandidates[index];
            if (isSelected)
                Primitives2D.FillRect(spriteBatch, new Rectangle(menuRect.X, y, screenWidth, rowHeight), UiTheme.PanelHover);
            UiTheme.DrawText(spriteBatch, $"{(isSelected ? ">" : " ")} {label}",
                12 * scale, isSelected ? UiTheme.Gold : UiTheme.Text, new Vector2(Px(12), y + Px(2)));
            y += rowHeight;
        }
        if (moreBelow)
        {
            int hiddenBelow = _menuCandidates.Count - (scrollOffset + shown);
            UiTheme.DrawText(spriteBatch, $"v {hiddenBelow} more below (scroll down)",
                11 * scale, UiTheme.Muted, new Vector2(Px(12), y + Px(2)));
        }
    }
}
