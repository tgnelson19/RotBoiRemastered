using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

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
///                                               phase/pattern/sequence; type a space
///                                               after "/testphase" for a live, filterable
///                                               dropdown of every key that boss supports
///   /help                                   -- lists the above
/// </summary>
public sealed class DevConsole
{
    private const int MaxHistoryLines = 50;
    private const int VisibleHistoryLines = 10;
    private const int MaxBufferLength = 80;
    private const string TestPhasePrefix = "/testphase ";
    private const int MaxTestPhaseMenuRows = 8;

    private readonly List<string> _history = new();
    private string _buffer = "";
    private double _seconds;
    private bool _open;
    /// <summary>
    /// Live-filtered candidates for the current buffer, recomputed each
    /// <see cref="Update"/> call (see <see cref="RefreshTestPhaseMenu"/>) and
    /// read back by <see cref="Draw"/> -- Draw itself has no GameSession
    /// reference, so it can't query <see cref="GameSession.DebugTestPhaseOptions"/>
    /// directly.
    /// </summary>
    private readonly List<(string Key, string Label)> _testPhaseCandidates = new();
    private int _testPhaseSelection;
    private string _testPhaseFilter = "";
    /// <summary>
    /// Set by <see cref="DismissTestPhaseMenu"/> (the console's Escape
    /// handler in RotBoiGame, so a first Escape closes just the dropdown
    /// rather than the whole console) and cleared the moment the filter text
    /// changes again, so resuming typing brings the menu back.
    /// </summary>
    private bool _testPhaseMenuDismissed;

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
        _testPhaseCandidates.Clear();
        _testPhaseFilter = "";
        _testPhaseMenuDismissed = false;
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

        RefreshTestPhaseMenu(session);
        if (_testPhaseCandidates.Count > 0)
        {
            if (keysPressed.Contains(Keys.Down))
                _testPhaseSelection = (_testPhaseSelection + 1) % _testPhaseCandidates.Count;
            if (keysPressed.Contains(Keys.Up))
            {
                _testPhaseSelection = (_testPhaseSelection - 1 + _testPhaseCandidates.Count)
                    % _testPhaseCandidates.Count;
            }
        }

        if (keysPressed.Contains(Keys.Enter) && (_buffer.Length > 0 || _testPhaseCandidates.Count > 0))
        {
            // A visible dropdown wins over the raw buffer -- the player may
            // have only typed a partial filter, so the highlighted candidate
            // (not the literal buffer text) is what actually runs.
            string command = _testPhaseCandidates.Count > 0
                ? $"/testphase {_testPhaseCandidates[Math.Clamp(_testPhaseSelection, 0, _testPhaseCandidates.Count - 1)].Key}"
                : _buffer.Trim();
            var result = Execute(command, session);
            _buffer = "";
            _testPhaseCandidates.Clear();
            _testPhaseFilter = "";
            _testPhaseMenuDismissed = false;
            return result;
        }
        return default;
    }

    /// <summary>
    /// Recomputes <see cref="_testPhaseCandidates"/> from the current buffer.
    /// The dropdown appears the instant the buffer reads "/testphase " (right
    /// after the trailing space -- so typing the bare command alone shows
    /// nothing yet, matching the console's own doc comment), filtered by
    /// whatever comes after that prefix against both key and label, and
    /// disappears again the moment the buffer no longer matches (backspaced
    /// short, or a different command entirely).
    /// </summary>
    private void RefreshTestPhaseMenu(GameSession? session)
    {
        if (session is null || !_buffer.StartsWith(TestPhasePrefix, StringComparison.OrdinalIgnoreCase))
        {
            _testPhaseCandidates.Clear();
            _testPhaseFilter = "";
            _testPhaseMenuDismissed = false;
            return;
        }
        string filter = _buffer[TestPhasePrefix.Length..].TrimStart();
        if (filter != _testPhaseFilter)
        {
            _testPhaseFilter = filter;
            _testPhaseSelection = 0;
            _testPhaseMenuDismissed = false;
        }
        _testPhaseCandidates.Clear();
        if (_testPhaseMenuDismissed)
            return;
        foreach (var option in session.DebugTestPhaseOptions())
        {
            if (filter.Length == 0
                || option.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || option.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _testPhaseCandidates.Add(option);
            }
        }
        if (_testPhaseSelection >= _testPhaseCandidates.Count)
            _testPhaseSelection = Math.Max(0, _testPhaseCandidates.Count - 1);
    }

    /// <summary>
    /// Called from RotBoiGame's Escape handling: dismisses the /testphase
    /// dropdown without closing the console or touching the typed buffer,
    /// returning true iff there was a menu open to dismiss. The caller only
    /// proceeds to close the whole console when this returns false, so the
    /// first Escape while suggestions are showing just clears them.
    /// </summary>
    public bool DismissTestPhaseMenu()
    {
        if (_testPhaseCandidates.Count == 0 || _testPhaseMenuDismissed)
            return false;
        _testPhaseMenuDismissed = true;
        _testPhaseCandidates.Clear();
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
            Log("                                            (space after \"/testphase\" for a filterable dropdown)");
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
    /// be driven through the dropdown (type "/testphase " and either arrow
    /// down to a candidate and press Enter, or keep typing to filter first) --
    /// see <see cref="RefreshTestPhaseMenu"/> -- but also accepts a key typed
    /// out in full, e.g. "/testphase blender".
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

        if (_testPhaseCandidates.Count > 0)
            DrawTestPhaseMenu(spriteBatch, screenWidth, inputRect, scale);
    }

    /// <summary>
    /// The "/testphase " autocomplete dropdown, anchored directly under the
    /// input bar -- up to <see cref="MaxTestPhaseMenuRows"/> candidates, the
    /// current one (<see cref="_testPhaseSelection"/>, moved by Up/Down in
    /// <see cref="Update"/>) highlighted, with an "and N more" hint when the
    /// live filter still matches more than fit on screen.
    /// </summary>
    private void DrawTestPhaseMenu(SpriteBatch spriteBatch, int screenWidth, Rectangle inputRect, float scale)
    {
        int Px(float value) => Math.Max(1, (int)MathF.Round(value * scale));
        int rowHeight = Px(18);
        int shown = Math.Min(MaxTestPhaseMenuRows, _testPhaseCandidates.Count);
        bool overflow = _testPhaseCandidates.Count > shown;
        int menuHeight = shown * rowHeight + Px(6) + (overflow ? Px(14) : 0);
        var menuRect = new Rectangle(inputRect.X, inputRect.Bottom, screenWidth, menuHeight);
        UiTheme.DrawPanel(spriteBatch, menuRect, UiTheme.Panel * .97f, UiTheme.Border, shadow: 3);

        int y = menuRect.Y + Px(3);
        int selected = Math.Clamp(_testPhaseSelection, 0, shown - 1);
        for (int index = 0; index < shown; index++)
        {
            bool isSelected = index == selected;
            (string key, string label) = _testPhaseCandidates[index];
            if (isSelected)
                Primitives2D.FillRect(spriteBatch, new Rectangle(menuRect.X, y, screenWidth, rowHeight), UiTheme.PanelHover);
            UiTheme.DrawText(spriteBatch, $"{(isSelected ? ">" : " ")} {label}  ({key})",
                12 * scale, isSelected ? UiTheme.Gold : UiTheme.Text, new Vector2(Px(12), y + Px(2)));
            y += rowHeight;
        }
        if (overflow)
        {
            UiTheme.DrawText(spriteBatch,
                $"...and {_testPhaseCandidates.Count - shown} more (keep typing to narrow)",
                11 * scale, UiTheme.Muted, new Vector2(Px(12), y + Px(2)));
        }
    }
}
