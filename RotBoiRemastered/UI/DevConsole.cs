using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

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
///   /extract                                -- runs the same sequence as the pause menu's
///                                               Extract button, bypassing its BeaudisDefeated gate
///   /help                                   -- lists the above
/// </summary>
public sealed class DevConsole
{
    private const int MaxHistoryLines = 50;
    private const int VisibleHistoryLines = 10;
    private const int MaxBufferLength = 80;

    private readonly List<string> _history = new();
    private string _buffer = "";
    private double _seconds;
    private bool _open;

    public bool IsOpen => _open;

    public void Open()
    {
        _open = true;
        _buffer = "";
    }

    public void Close()
    {
        _open = false;
        _buffer = "";
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

    /// <summary>Call once per frame regardless of IsOpen. Backspace/Enter come through KeysPressed (edge-triggered), same as every other bound action in this codebase.</summary>
    public ConsoleResult Update(GameSession? session, IReadOnlySet<Keys> keysPressed, double elapsedSeconds)
    {
        _seconds += elapsedSeconds;
        if (!_open)
            return default;
        if (keysPressed.Contains(Keys.Back) && _buffer.Length > 0)
            _buffer = _buffer[..^1];
        if (keysPressed.Contains(Keys.Enter) && _buffer.Length > 0)
        {
            var result = Execute(_buffer.Trim(), session);
            _buffer = "";
            return result;
        }
        return default;
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
            Log("/extract                                -- end the run as an extraction");
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
            case "extract": return HandleExtract();
            default:
                Log($"Unknown command: {command} (try /help)");
                return default;
        }
    }

    private ConsoleResult HandleSpawn(IReadOnlyList<string> tokens, GameSession session)
    {
        if (!TryParseItemArgs(tokens, "/spawn", out int count, out var definition, out string rarity))
            return default;
        var drops = Enumerable.Range(0, count).Select(_ => new ItemDrop(definition, rarity)).ToList();
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
            inventory[i] = new ItemDrop(definition, rarity);
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
        Log("Forced a level up.");
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

    /// <summary>Uniques always drop at "Unique" rarity (see Items.RarityPower) regardless of any rarity token given; a regular item takes the requested tier if it's a real one, else defaults to Legendary -- admin testing usually wants strong stats, not a Common roll.</summary>
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
        int panelHeight = (int)(screenHeight * .32f);
        var panel = new Rectangle(0, 0, screenWidth, panelHeight);
        Primitives2D.FillRect(spriteBatch, panel, UiTheme.Void * .92f);
        Primitives2D.Line(spriteBatch, new Vector2(0, panelHeight), new Vector2(screenWidth, panelHeight), UiTheme.Border, 2);

        int y = 10;
        int shown = Math.Min(VisibleHistoryLines, _history.Count);
        for (int index = _history.Count - shown; index < _history.Count; index++)
        {
            UiTheme.DrawText(spriteBatch, _history[index], 12, UiTheme.Cream, new Vector2(12, y));
            y += 18;
        }

        var inputRect = new Rectangle(0, panelHeight - 30, screenWidth, 30);
        Primitives2D.FillRect(spriteBatch, inputRect, UiTheme.Panel);
        bool caretOn = (int)(_seconds * 2) % 2 == 0;
        UiTheme.DrawText(spriteBatch, $"> {_buffer}{(caretOn ? "_" : "")}", 13, UiTheme.Text, new Vector2(12, inputRect.Y + 6));
    }
}
