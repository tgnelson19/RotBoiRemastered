using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public enum DossierAction { None, Close, LevelUp, Reforge }

/// <summary>
/// Paused run dossier and shared loadout interaction surface.
///
/// Design notes vs. the Python original:
/// - <see cref="RunState"/> is taken directly rather than a purpose-built
///   snapshot type (contrast <see cref="LevelUpStatSnapshot"/>/
///   <see cref="RunResultReport"/>): this sheet reads nearly RunState's entire
///   surface area, so a snapshot would just duplicate every field. Now that
///   RunState/GameSession exist, this pairing (deferred together in
///   UI/README.md) is resolved.
/// - `nearby_crate` lives on <see cref="RunState"/> instead of here (see
///   RunState.cs's doc comment) -- everything else the Python class owned
///   (uiScale/mode/tooltip/drag state/slot rects) stays instance state here.
/// - Camera re-centering (`bG.lockX = self.arena_width / 2`) is
///   GameSession's job, not this class's -- Camera isn't visible from here,
///   and GameSession already owns it. See GameSession.cs's constructor/
///   Resize/ResetAll.
/// - No implicit per-frame `_sync_layout()` self-check: <see cref="SyncLayout"/>
///   is called explicitly by GameSession.Resize, matching
///   <c>LevelingHandler.UpdateLayout</c>'s existing contract instead of
///   re-deriving screen size from a hidden global every frame.
/// - The Python original combined drawing, hit-rect population, drag-press
///   capture, drag-release resolution, and the cursor-following drag icon
///   into one `drawSheet()` + `_handle_equipment_drag()` pair. Split here
///   into <see cref="DrawSheet"/> (draw everything, populate this frame's
///   hit rects, draw the dragged icon/tooltip) and <see cref="HandleDrag"/>
///   (press capture / release resolution against those hit rects) --
///   DrawSheet always runs first each frame, exactly like
///   `LevelingHandler.DrawCards` before `PlayerClicked`. One accepted,
///   cosmetic-only difference from Python: on the exact frame a drag is
///   captured, Python suppresses the tooltip and starts drawing the dragged
///   icon that same frame (`_handle_equipment_drag` ran inside `drawSheet`);
///   here that first frame still shows the tooltip/no-icon since
///   <see cref="HandleDrag"/> hasn't run yet -- one frame of lag on a single
///   mouse-press edge, not worth reordering draw-before-input for.
/// - Equipment/crate drag "source" (`("equipment", key)` /
///   `("crate", crate, index)` tuples) becomes a small closed
///   <c>DragSource</c> record hierarchy instead of a tagged tuple.
/// - `updateCurrLevel()` is dropped -- its body was `return None` (confirmed
///   by reading informationSheet.py:513-514), a no-op stub character.py
///   called once per frame for no effect.
/// - Bounty tracking (`cS.currentBounty`/`selectBountyTarget()`) is computed
///   by <see cref="GameSession.SelectBountyTarget"/> (it only reads
///   RunState's enemy/boss data, no HUD concern) and passed into
///   <see cref="DrawSheet"/> explicitly, rather than this class reaching
///   into GameSession itself.
/// </summary>
public sealed class InformationSheet
{
    private static readonly RasterizerState ScissorRasterizerState = new()
    {
        ScissorTestEnable = true,
        CullMode = CullMode.None,
    };
    private static readonly IReadOnlyDictionary<string, string> EquipmentSlotTypes = new Dictionary<string, string>
    {
        ["weapon"] = "weapon",
        ["armor"] = "armor",
        ["ring"] = "ring",
        ["accessory_1"] = "accessory",
        ["accessory_2"] = "accessory",
    };

    private static readonly IReadOnlyDictionary<string, (string Title, string Strength)> BuildNames =
        new Dictionary<string, (string, string)>
        {
            ["volley"] = ("BULLET STORM", "More shots fill more of the arena."),
            ["critical"] = ("CRITICAL STRIKER", "Critical hits create sudden bursts of power."),
            ["harvest"] = ("EXPERIENCE MAGNET", "Fast collection keeps the upgrades coming."),
            ["survival"] = ("ARMORED RUNNER", "Defense and movement keep danger manageable."),
            ["tempo"] = ("RAPID FIRE", "A steady stream of shots controls nearby space."),
            ["precision"] = ("LONGSHOT", "Fast, far-reaching shots reward clean aim."),
            ["power"] = ("HEAVY GUNNER", "Each projectile lands with extra weight."),
        };

    private static readonly (string Label, string Key, float AngleDegrees)[]
        EquipmentSlots =
        [
            ("WEAPON", "weapon", 90f),
            ("RING", "ring", 18f),
            ("ACC 2", "accessory_2", -54f),
            ("ACC 1", "accessory_1", -126f),
            ("ARMOR", "armor", 162f),
        ];

    private static readonly (int Level, string Name)[] MilestoneGates =
        BuildMilestoneGates();

    public const int CrateSlotCount = 4;

    public const int InventorySlotCount = 8;

    private abstract record DragSource;
    private sealed record EquipmentDragSource(string Key) : DragSource;
    private sealed record CrateDragSource(LootCrate Crate, int Index) : DragSource;
    private sealed record InventoryDragSource(int Index) : DragSource;
    /// <summary>
    /// The Soul's Vault (GameProfile.Profile.Storage) -- only reachable when the caller
    /// passes vaultSlotRects into HandleDrag (the Soul does; normal gameplay doesn't, so
    /// this stays entirely inert during a real run). Lets the Vault share the exact same
    /// drag mechanic/feel as equipment/stash/crate instead of a separate implementation.
    /// </summary>
    private sealed record VaultDragSource(int Index) : DragSource;

    private float _uiScale;
    private int _screenWidth;
    private int _screenHeight;
    private int _totalLength;
    private int _totalHeight;
    private int _posX;
    private int _padding;

    private string? _tooltip;
    private ItemDrop? _tooltipItem;
    private ItemDrop? _draggingItem;
    private DragSource? _draggingSource;
    private readonly Dictionary<string, Rectangle> _equipmentSlotRects = new();
    private readonly List<Rectangle> _lootPanelSlotRects = new(CrateSlotCount);
    private readonly List<Rectangle> _inventorySlotRects = new(InventorySlotCount);
    private readonly Dictionary<string, int> _summaryUpgradeCounts = new();
    private readonly Dictionary<string, int> _summaryCategoryCounts = new();
    private readonly List<(string Category, int Count)> _summaryFamilies = new();
    private readonly List<string> _summaryStrengthLines = new();
    private readonly UiFocusNavigator _loadoutFocus = new();
    private string _summaryTitle = "FRESH START";
    private string _summaryStrength = "Your first picks will shape this run.";
    private string _summaryCaution = "No weakness yet";
    private int _summaryTextWidth = -1;
    private double _summaryTextScale;
    private int _playerLevelCap = Progression.MaxLevel;
    private int _summaryDefense;
    private double _summaryPlayerSpeed;
    private int _summaryBulletDamage;
    private double _summaryProjectileCount;
    private double _summaryBulletRange;
    private IReadOnlyList<Rectangle> _vaultSlotRects = Array.Empty<Rectangle>();
    private bool _allowWorldDrop = true;
    private Rectangle _levelUpButtonRect;
    private Rectangle _reforgeButtonRect;
    private Rectangle _dossierLevelRect;
    private Rectangle _dossierReforgeRect;
    private Rectangle _dossierDropRect;
    private Rectangle _explicitDropRect;
    private double _dossierScroll;
    private int _dossierContentHeight;
    private string _presentationPath = "sound";
    private float _presentationTime;

    public bool DragInProgress { get; private set; }
    public ItemDrop? DraggingItem => _draggingItem;

    /// <summary>Abandons the current drag without moving either item.</summary>
    public void CancelDrag()
    {
        _draggingItem = null;
        _draggingSource = null;
        DragInProgress = false;
    }

    /// <summary>
    /// Reuses the dossier's transfer destinations for the live quick-loot strip.
    /// The caller replaces these every draw, so resize and GUI-scale changes cannot
    /// leave stale combat hit targets behind.
    /// </summary>
    public void ConfigureLiveLootLayout(
        IReadOnlyDictionary<string, Rectangle> equipmentSlotRects,
        IReadOnlyList<Rectangle> lootSlotRects,
        IReadOnlyList<Rectangle> stashSlotRects)
    {
        _equipmentSlotRects.Clear();
        foreach (var (key, rect) in equipmentSlotRects)
            _equipmentSlotRects[key] = rect;
        _lootPanelSlotRects.Clear();
        _lootPanelSlotRects.AddRange(lootSlotRects);
        _inventorySlotRects.Clear();
        _inventorySlotRects.AddRange(stashSlotRects);
    }

    public void HandleLiveLootDrag(RunState state, Vector2 playerWorldPosition,
        Point mousePosition, bool mouseDown, bool mousePressed)
    {
        if (state.NearbyCrate is not { Items.Count: > 0 })
        {
            if (DragInProgress)
                CancelDrag();
            return;
        }
        HandleDrag(state, playerWorldPosition, mousePosition, mouseDown, mousePressed,
            allowWorldDrop: false, lootOnlyPickup: true);
    }

    public bool QuickEquipLoot(RunState state, int lootIndex, string equipmentKey)
    {
        if (state.NearbyCrate is not { } crate
            || lootIndex < 0 || lootIndex >= crate.Items.Count
            || !EquipmentSlotTypes.TryGetValue(equipmentKey, out string? slotType))
            return false;
        ItemDrop item = crate.Items[lootIndex];
        if (item.SlotType != slotType)
            return false;
        ItemDrop? displaced = state.Equipment[equipmentKey];
        state.Equipment[equipmentKey] = item;
        GameProfile.DiscoverItem(item.Name);
        ReturnDisplacedToCrate(state, new CrateDragSource(crate, lootIndex), displaced);
        state.CombinePlayerStats();
        return true;
    }

    public InformationSheet(int screenWidth, int screenHeight)
    {
        BuildLayout(screenWidth, screenHeight);
    }

    private int Px(double value) => Math.Max(1, (int)Math.Round(value * _uiScale));

    /// <summary>
    /// This sidebar is a fixed, tightly packed stack of roughly eight
    /// panels, one of them (Recent Picks) anchored to the *bottom* of the
    /// screen independently of how tall everything above it grows. Growing
    /// row heights/panel heights to track TextSize was tried and made
    /// things worse, not better: growth compounds across every stacked
    /// panel, so even a modest setting pushed the total content well past
    /// the screen height, and panels started colliding with *each other*
    /// instead of just their own text overlapping. There's no room in this
    /// specific layout to honor the full TextSize range without a much
    /// larger reflow (variable-height rows, or making the sidebar
    /// scrollable) -- capping the boost this sidebar's own text uses keeps
    /// TextSize's benefit everywhere else in the game (title screen,
    /// level-up cards, menus) while keeping this panel internally
    /// consistent with itself. Set to UiTheme's own "LARGE" preset (one
    /// step below "MAX") now that TextSize itself is capped at 2.0 and
    /// preset-only -- this sidebar can absorb that much growth safely, just
    /// not the full range.
    /// </summary>
    private const double MaxLocalTextBoost = 1.4;

    private static double LocalTextScale() => Math.Min(UiTheme.TextScaleMultiplier(), MaxLocalTextBoost);

    /// <summary>
    /// Drop-in replacement for UiTheme.DrawText within this file: renders
    /// through UiTheme.DrawRawText/RawFont, which skip UiTheme.Font's own
    /// (uncapped) TextScaleMultiplier, applying LocalTextScale's capped
    /// boost instead.
    /// </summary>
    private static Rectangle DrawSheetText(SpriteBatch spriteBatch, object value, double size, Color color,
        Vector2 position, string anchor = "topleft")
        => UiTheme.DrawRawText(spriteBatch, value, size * LocalTextScale(), color, position, anchor);

    private void BuildLayout(int screenWidth, int screenHeight)
    {
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
        _uiScale = UiTheme.DisplayScale(screenWidth, screenHeight);
        // A little more horizontal room lets build guidance wrap cleanly now
        // that it lives here, while still returning the arena space formerly
        // covered by the separate build overlay.
        _totalLength = Math.Max(Px(260), Math.Min(Px(320), (int)(screenWidth * .18)));
        // Never consume more than 42% of a narrow display.
        _totalLength = Math.Min(_totalLength, (int)(screenWidth * .42));
        _totalHeight = screenHeight;
        _posX = screenWidth - _totalLength;
        _padding = Px(7);
    }

    /// <summary>Call from GameSession.Resize whenever the window size or GuiScale changes.</summary>
    public void SyncLayout(int screenWidth, int screenHeight)
    {
        float nextScale = UiTheme.DisplayScale(screenWidth, screenHeight);
        if (nextScale != _uiScale || screenWidth != _screenWidth || screenHeight != _screenHeight)
            BuildLayout(screenWidth, screenHeight);
    }

    public void ScrollDossier(int delta)
    {
        if (delta == 0)
            return;
        _dossierScroll = Math.Clamp(_dossierScroll - delta * .35, 0,
            Math.Max(0, _dossierContentHeight - Math.Max(1, _screenHeight - Px(96))));
    }

    public DossierAction HandleDossierAction(
        RunState state,
        IReadOnlySet<Microsoft.Xna.Framework.Input.Keys> keysPressed,
        Point mousePosition,
        bool mousePressed)
    {
        if (keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Tab)
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Escape))
        {
            if (DragInProgress)
            {
                CancelDrag();
                return DossierAction.None;
            }
            CancelDrag();
            return DossierAction.Close;
        }
        bool confirm = InputState.ControllerConfirmPressed
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Enter)
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Space);
        if (DragInProgress)
            return DossierAction.None;
        bool levelActivated = mousePressed && _dossierLevelRect.Contains(mousePosition)
            || confirm && _loadoutFocus.IsFocused("dossier:level");
        if (levelActivated
            && state.CurrentLevel < _playerLevelCap
            && state.ExpCount >= state.ExpNeededForNextLevel)
        {
            return DossierAction.LevelUp;
        }
        bool reforgeActivated = mousePressed && _dossierReforgeRect.Contains(mousePosition)
            || confirm && _loadoutFocus.IsFocused("dossier:reforge");
        if (reforgeActivated
            && state.Equipment.Values.Any(item => item is not null))
        {
            return DossierAction.Reforge;
        }
        return DossierAction.None;
    }

    private Rectangle Panel(SpriteBatch spriteBatch, int y, int height, Color? accent = null, Color? fill = null)
    {
        var rect = new Rectangle(_posX + _padding, y, _totalLength - _padding * 2, height);
        UiTheme.DrawLivingPanel(
            spriteBatch, rect, _presentationPath, _presentationTime,
            fill ?? UiTheme.PanelRaised, accent ?? UiTheme.Border,
            shadow: 3);
        return rect;
    }

    private void Bar(SpriteBatch spriteBatch, Rectangle rect, int y, string label, double value, double maximum,
        Color color, string valueText)
    {
        DrawSheetText(spriteBatch, label, Px(10), UiTheme.Muted, new Vector2(rect.X + Px(11), y));
        DrawSheetText(spriteBatch, valueText, Px(10), UiTheme.Text, new Vector2(rect.Right - Px(11), y), "topright");
        double ratio = maximum != 0 ? value / maximum : 0;
        UiTheme.DrawProgress(spriteBatch, new Rectangle(rect.X + Px(11), y + Px(14), rect.Width - Px(22), Px(11)),
            (float)ratio, color, 10);
    }

    // ----- Pure derived-value helpers (public static: unit-testable without a GraphicsDevice, same reasoning as LevelingHandler.ProjectedValue/Recommendation) -----

    public static List<(string Category, int Count)> FamilyCounts(RunState state)
    {
        var counts = new Dictionary<string, int>();
        foreach (var (name, count) in state.UpgradeTypeCounts)
        {
            if (Upgrades.DefinitionsByName.TryGetValue(name, out var definition))
                counts[definition.Category] = counts.GetValueOrDefault(definition.Category) + count;
        }
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    public static (string Title, string Strength, string Caution) BuildIdentity(RunState state)
    {
        var families = FamilyCounts(state);
        if (families.Count == 0)
            return ("FRESH START", "Your first picks will shape this run.", "No weakness yet");
        string family = families[0].Category;
        var (title, strength) = BuildNames.TryGetValue(family, out var names)
            ? names
            : (family.ToUpperInvariant(), "A flexible set of upgrades.");
        string caution;
        if (state.Defense < 1 && state.PlayerSpeed < 2.5)
            caution = "Fragile if cornered";
        else if (state.BulletDamage < 1.35 && state.ProjectileCount >= 2)
            caution = "Relies on shot volume";
        else if (state.BulletRange < Simulation.TileSize * 5)
            caution = "Best at close range";
        else
            caution = "No clear weakness";
        return (title, strength, caution);
    }

    private bool SummaryCacheMatches(RunState state, int textWidth)
    {
        if (_summaryTextWidth != textWidth
            || _summaryTextScale != LocalTextScale()
            || _summaryDefense != state.Defense
            || _summaryPlayerSpeed != state.PlayerSpeed
            || _summaryBulletDamage != state.BulletDamage
            || _summaryProjectileCount != state.ProjectileCount
            || _summaryBulletRange != state.BulletRange
            || _summaryUpgradeCounts.Count != state.UpgradeTypeCounts.Count)
        {
            return false;
        }
        foreach (var (name, count) in state.UpgradeTypeCounts)
        {
            if (!_summaryUpgradeCounts.TryGetValue(name, out int cached)
                || cached != count)
            {
                return false;
            }
        }
        return true;
    }

    private void RefreshSummaryCache(RunState state, int textWidth)
    {
        if (SummaryCacheMatches(state, textWidth))
            return;

        _summaryTextWidth = textWidth;
        _summaryTextScale = LocalTextScale();
        _summaryDefense = state.Defense;
        _summaryPlayerSpeed = state.PlayerSpeed;
        _summaryBulletDamage = state.BulletDamage;
        _summaryProjectileCount = state.ProjectileCount;
        _summaryBulletRange = state.BulletRange;
        _summaryUpgradeCounts.Clear();
        _summaryCategoryCounts.Clear();
        foreach (var (name, count) in state.UpgradeTypeCounts)
        {
            _summaryUpgradeCounts[name] = count;
            if (Upgrades.DefinitionsByName.TryGetValue(name, out var definition))
            {
                _summaryCategoryCounts[definition.Category] =
                    _summaryCategoryCounts.GetValueOrDefault(definition.Category)
                    + count;
            }
        }

        _summaryFamilies.Clear();
        foreach (var (category, count) in _summaryCategoryCounts)
            _summaryFamilies.Add((category, count));
        _summaryFamilies.Sort(static (left, right) =>
        {
            int countComparison = right.Count.CompareTo(left.Count);
            return countComparison != 0
                ? countComparison
                : string.CompareOrdinal(left.Category, right.Category);
        });

        if (_summaryFamilies.Count == 0)
        {
            _summaryTitle = "FRESH START";
            _summaryStrength = "Your first picks will shape this run.";
            _summaryCaution = "No weakness yet";
        }
        else
        {
            string family = _summaryFamilies[0].Category;
            (_summaryTitle, _summaryStrength) =
                BuildNames.TryGetValue(family, out var names)
                    ? names
                    : (family.ToUpperInvariant(), "A flexible set of upgrades.");
            if (state.Defense < 1 && state.PlayerSpeed < 2.5)
                _summaryCaution = "Fragile if cornered";
            else if (state.BulletDamage < 1.35
                && state.ProjectileCount >= 2)
                _summaryCaution = "Relies on shot volume";
            else if (state.BulletRange < Simulation.TileSize * 5)
                _summaryCaution = "Best at close range";
            else
                _summaryCaution = "No clear weakness";
        }

        _summaryStrengthLines.Clear();
        var font = UiTheme.RawFont(Px(9) * LocalTextScale());
        string[] words = _summaryStrength.Split(' ');
        string line = "";
        foreach (string word in words)
        {
            string candidate = (line + " " + word).Trim();
            if (line.Length > 0
                && font.MeasureString(candidate).X > textWidth)
            {
                _summaryStrengthLines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        _summaryStrengthLines.Add(line);
    }

    /// <summary>Ported from `_combat_values`. The Python original also computed an expected-DPS value that its only call site immediately discarded (`attacks, _ = self._combat_values()`) -- dropped here since it's dead within this file too.</summary>
    public static double AttacksPerSecond(RunState state) => Simulation.FrameRate / Math.Max(1, state.AttackCooldownStat);

    public static string Rating(double value, double baseline, bool inverse = false)
    {
        if (value <= 0)
            return "None";
        double ratio = inverse ? baseline / Math.Max(.001, value) : value / Math.Max(.001, baseline);
        if (ratio >= 2.0)
            return "Exceptional";
        if (ratio >= 1.45)
            return "Very strong";
        if (ratio >= 1.12)
            return "Strong";
        return "Normal";
    }

    public static string ShotText(RunState state)
    {
        int whole = (int)Math.Floor(state.ProjectileCount);
        int chance = (int)Math.Round((state.ProjectileCount - whole) * 100);
        return chance != 0 ? $"{whole} shots + {chance}% bonus" : $"{whole} shot{(whole != 1 ? "s" : "")}";
    }

    /// <summary>A pierce value of one allows the initial target plus one pass-through.</summary>
    public static string PierceText(RunState state)
    {
        int whole = (int)Math.Floor(state.BulletPierce) + 1;
        int chance = (int)Math.Round((state.BulletPierce - Math.Floor(state.BulletPierce)) * 100);
        return chance != 0 ? $"Hits {whole} + {chance}% extra" : $"Hits up to {whole} enemies";
    }

    public static (string Label, Color Color, double Ratio) Pressure(RunState state)
    {
        if (state.GameCompleted)
            return ("RUN COMPLETE", UiTheme.Cream, 0);
        if (state.ActiveBoss is not null)
            return ("BOSS", UiTheme.Red, 1);
        double threat = 0;
        bool eliteNearby = false;
        for (int index = 0; index < state.EnemyHolster.Count; index++)
        {
            Enemy enemy = state.EnemyHolster[index];
            if (enemy.IsDead())
                continue;
            threat += enemy.ThreatCost;
            eliteNearby |= enemy.CombatRole == "elite";
        }
        double ratio = Math.Min(1, threat / Math.Max(1, state.EnemyThreatCap));
        if (state.EnemyHolster.Count == 0)
            return ("CALM", UiTheme.Green, ratio);
        if (eliteNearby)
            return ("ELITE NEARBY", UiTheme.Purple, ratio);
        if (ratio >= .72)
            return ("DANGEROUS", UiTheme.Red, ratio);
        return ("ACTIVE", UiTheme.Gold, ratio);
    }

    public static (string Name, string Detail) BountyDetails(BountyInfo? bounty, RunState state, Vector2 playerWorldCenter)
    {
        if (bounty is null)
            return ("Explore the arena", "No active target");
        double dx = bounty.World.X - playerWorldCenter.X;
        double dy = bounty.World.Y - playerWorldCenter.Y;
        double tiles = Math.Sqrt(dx * dx + dy * dy) / Math.Max(1, Simulation.TileSize);
        int count = 1;
        if (bounty.Target is RuntimeEncounter encounter)
        {
            count = 0;
            for (int index = 0; index < encounter.Members.Count; index++)
            {
                if (!encounter.Members[index].IsDead())
                    count++;
            }
        }
        string distance = tiles < 8 ? "Target nearby" : $"About {tiles:F0} tiles away";
        return (ToTitleCase(bounty.Label), $"{count} hostile{(count != 1 ? "s" : "")}  •  {distance}");
    }

    public static (int Level, string Milestone) NextMilestone(RunState state)
    {
        foreach (var gate in MilestoneGates)
            if (gate.Level > state.CurrentLevel)
                return gate;
        return (Progression.FinalBossLevel, "Complete");
    }

    private static (int Level, string Name)[] BuildMilestoneGates()
    {
        var gates = new List<(int Level, string Name)>();
        foreach (var (level, key) in Progression.MinibossGates)
        {
            gates.Add((
                level,
                ToTitleCase(key.Replace("miniboss_", ""))));
        }
        gates.Add((Progression.MidBossLevel, "Beaudis"));
        gates.Add((Progression.FinalBossLevel, "Dissonance"));
        gates.Sort(static (left, right) =>
        {
            int levelComparison = left.Level.CompareTo(right.Level);
            return levelComparison != 0
                ? levelComparison
                : string.CompareOrdinal(left.Name, right.Name);
        });
        return gates.ToArray();
    }

    /// <summary>Matches Python's str.title(): first letter of each space-separated word capitalized, rest lowercase.</summary>
    private static string ToTitleCase(string text) => string.Join(" ", text.Split(' ').Select(
        word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    // ----- Draw -----

    private int DrawRunSummary(SpriteBatch spriteBatch, RunState state, PathRun? pathRun)
    {
        int textWidth = _totalLength - _padding * 2 - Px(22);
        RefreshSummaryCache(state, textWidth);
        int lineHeight = Px(14);
        int pathOffset = pathRun is null ? 0 : Px(25);
        int cautionY = Px(82) + pathOffset
            + _summaryStrengthLines.Count * lineHeight + Px(2);
        int familyY = cautionY + Px(17);
        int height = familyY + Px(19);

        var rect = Panel(spriteBatch, _padding, height, pathRun?.CurrentSense.Accent ?? UiTheme.Purple);
        if (pathRun is not null)
        {
            var elapsed = TimeSpan.FromSeconds(Math.Max(0, state.RunTimeSeconds));
            string timer = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            DrawSheetText(spriteBatch,
                $"FLOOR {pathRun.FloorNumber:D2}/{PathRun.TotalFloors:D2}  //  {pathRun.SenseDisplayName.ToUpperInvariant()}",
                Px(10), pathRun.CurrentSense.Accent, new Vector2(rect.X + Px(11), rect.Y + Px(8)));
            DrawSheetText(spriteBatch, timer, Px(10), UiTheme.Gold,
                new Vector2(rect.Right - Px(11), rect.Y + Px(8)), "topright");
            Primitives2D.Line(spriteBatch, new Vector2(rect.X + Px(10), rect.Y + Px(28)),
                new Vector2(rect.Right - Px(10), rect.Y + Px(28)), UiTheme.Border, 1);
        }
        DrawSheetText(spriteBatch, $"LEVEL {state.CurrentLevel:D2}", Px(18), UiTheme.Text,
            new Vector2(rect.X + Px(11), rect.Y + Px(9) + pathOffset));
        var (pressureLabel, color, _) = Pressure(state);
        DrawSheetText(spriteBatch, pressureLabel, Px(10), color,
            new Vector2(rect.Right - Px(11), rect.Y + Px(14) + pathOffset), "topright");
        string challenge = "TAB: OPEN DOSSIER";
        if (state.HardMode)
            challenge = $"HARD MODE  //  {challenge}";
        if (state.NewGamePlusLevel > 0)
            challenge = $"NG+{state.NewGamePlusLevel}  //  {challenge}";
        if (pathRun is not null)
            challenge = $"{(pathRun.IsSecondAct ? "PATH II" : "PATH I")}  //  {challenge}";
        DrawSheetText(spriteBatch, challenge, Px(9),
            state.HardMode ? UiTheme.Red : state.NewGamePlusLevel > 0 ? UiTheme.Gold : UiTheme.Muted,
            new Vector2(rect.X + Px(11), rect.Y + Px(36) + pathOffset));

        Primitives2D.Line(spriteBatch, new Vector2(rect.X + Px(10), rect.Y + Px(52) + pathOffset),
            new Vector2(rect.Right - Px(10), rect.Y + Px(52) + pathOffset), UiTheme.Border, 1);
        DrawSheetText(spriteBatch, _summaryTitle, Px(15), UiTheme.Purple,
            new Vector2(rect.X + Px(11), rect.Y + Px(58) + pathOffset));
        for (int index = 0; index < _summaryStrengthLines.Count; index++)
            DrawSheetText(spriteBatch, _summaryStrengthLines[index], Px(9), UiTheme.Text,
                new Vector2(rect.X + Px(11), rect.Y + Px(82) + pathOffset + index * lineHeight));
        DrawSheetText(spriteBatch, _summaryCaution, Px(9), UiTheme.Muted,
            new Vector2(rect.X + Px(11), rect.Y + cautionY));

        if (_summaryFamilies.Count == 0)
        {
            DrawSheetText(spriteBatch, "NO UPGRADES COLLECTED", Px(8), UiTheme.Muted,
                new Vector2(rect.X + Px(11), rect.Y + familyY));
        }
        else
        {
            int shown = Math.Min(_summaryFamilies.Count, 2);
            float columnWidth = (rect.Width - Px(22)) / 2f;
            for (int index = 0; index < shown; index++)
            {
                var (family, count) = _summaryFamilies[index];
                DrawSheetText(spriteBatch, $"{ToTitleCase(family).ToUpperInvariant()}  x{count}", Px(8),
                    index == 0 ? UiTheme.Purple : UiTheme.Muted,
                    new Vector2(rect.X + Px(11) + columnWidth * index, rect.Y + familyY));
            }
        }
        return rect.Bottom + _padding;
    }

    private int DrawStatus(SpriteBatch spriteBatch, RunState state, Point mousePosition, int y)
    {
        Color healthColor = state.HealthPoints > state.MaxHealthPoints * .3 ? UiTheme.Green : UiTheme.Red;
        var rect = Panel(spriteBatch, y, Px(152), healthColor);
        Bar(spriteBatch, rect, rect.Y + Px(7), "HEALTH", state.HealthPoints, state.MaxHealthPoints, healthColor,
            $"{state.HealthPoints} / {state.MaxHealthPoints}");
        double dashValue = Math.Max(0, state.DashCooldownMax - state.CurrDashCooldown);
        string dashText = state.CurrDashCooldown <= 0 ? "READY" : $"{state.CurrDashCooldown / Simulation.FrameRate:F1} sec";
        Bar(spriteBatch, rect, rect.Y + Px(35), "DASH", dashValue, state.DashCooldownMax, UiTheme.Blue, dashText);
        double percent = state.ExpCount / Math.Max(1, state.ExpNeededForNextLevel) * 100;
        bool canLevel = state.CurrentLevel < _playerLevelCap
            && state.ExpCount >= state.ExpNeededForNextLevel;
        string pickText = canLevel ? "READY" : percent >= 82 ? $"SOON / {percent:F0}%" : $"{percent:F0}%";
        Bar(spriteBatch, rect, rect.Y + Px(63), "STORED EXP", state.ExpCount, state.ExpNeededForNextLevel, UiTheme.Gold,
            pickText);
        DrawSheetText(spriteBatch, $"FRAGMENTS  {state.Fragments:N0}  //  FORGE COST  {Items.ReforgeFragmentCost}", Px(8),
            UiTheme.Purple, new Vector2(rect.X + Px(11), rect.Y + Px(91)));

        int gap = Px(6);
        int buttonY = rect.Y + Px(112);
        int buttonWidth = (rect.Width - Px(22) - gap) / 2;
        _levelUpButtonRect = new Rectangle(rect.X + Px(11), buttonY, buttonWidth, Px(30));
        _reforgeButtonRect = new Rectangle(_levelUpButtonRect.Right + gap, buttonY, buttonWidth, Px(30));

        if (canLevel)
        {
            double pulse = (Math.Sin(state.RunTimeSeconds * 5.5) + 1) / 2;
            var glow = _levelUpButtonRect;
            glow.Inflate(Px(3), Px(3));
            Primitives2D.RoundedRectOutline(spriteBatch, glow, Color.Lerp(UiTheme.Gold, Color.White, (float)(pulse * .45)),
                Px(2), Px(5));
        }
        string levelLabel = state.CurrentLevel >= _playerLevelCap
            ? "MAX LEVEL"
            : $"LEVEL  {Math.Ceiling(state.ExpNeededForNextLevel):0} XP";
        UiTheme.DrawButton(spriteBatch, _levelUpButtonRect, levelLabel, mousePosition,
            enabled: canLevel, accentColor: UiTheme.Gold, textSize: Px(8));
        bool hasEquipment = state.Equipment.Values.Any(item => item is not null);
        UiTheme.DrawButton(spriteBatch, _reforgeButtonRect, "REFORGE", mousePosition,
            enabled: hasEquipment, accentColor: UiTheme.Purple, textSize: Px(8));
        return rect.Bottom + _padding;
    }

    private int DrawInventory(SpriteBatch spriteBatch, RunState state, Point mousePosition, int y)
    {
        int headerHeight = Px(22);
        int hubHeight = Px(122);
        int height = headerHeight + hubHeight + Px(10);
        var rect = Panel(spriteBatch, y, height, UiTheme.Border);
        DrawSheetText(spriteBatch, "EQUIPMENT", Px(10), UiTheme.Muted, new Vector2(rect.X + Px(10), rect.Y + Px(8)));

        float hubX = rect.Center.X;
        float hubY = rect.Y + headerHeight + hubHeight / 2f;
        float radiusX = rect.Width * .28f;
        float radiusY = hubHeight * .38f;
        int slotSize = Px(38);

        _equipmentSlotRects.Clear();
        foreach (var (label, key, angleDegrees) in EquipmentSlots)
        {
            float angle = MathHelper.ToRadians(angleDegrees);
            var center = new Vector2(hubX + MathF.Cos(angle) * radiusX, hubY - MathF.Sin(angle) * radiusY);
            var slotRect = new Rectangle((int)(center.X - slotSize / 2f), (int)(center.Y - slotSize / 2f), slotSize, slotSize);
            _equipmentSlotRects[key] = slotRect;
            var item = state.Equipment[key];
            bool draggingThis = _draggingSource is EquipmentDragSource eq && eq.Key == key;
            if (item is not null && !draggingThis)
            {
                bool hovered = slotRect.Contains(mousePosition);
                ItemCards.DrawItemCard(
                    spriteBatch, slotRect, item, hovered,
                    _presentationTime);
                if (hovered)
                    _tooltipItem = item;
            }
            else
            {
                Primitives2D.FillRect(spriteBatch, slotRect, UiTheme.Ink);
                Primitives2D.RectOutline(spriteBatch, slotRect, UiTheme.Border, Px(2));
            }
            DrawSheetText(spriteBatch, label, Px(8), UiTheme.Muted, new Vector2(center.X, slotRect.Bottom + Px(3)), "midtop");
        }
        return rect.Bottom + _padding;
    }

    /// <summary>
    /// Eight general-purpose hoarding slots, directly below the equipment
    /// hub. Unlike equipment, a stash slot accepts any item regardless of
    /// SlotType (see ResolveDrop) and never contributes to stats (see
    /// RunState.Inventory's doc comment) -- it's purely for carrying extra
    /// loot toward extraction without committing to equip it.
    /// </summary>
    private int DrawStash(SpriteBatch spriteBatch, RunState state, Point mousePosition, int y)
    {
        const int columns = 4;
        int rows = (InventorySlotCount + columns - 1) / columns;
        int headerHeight = Px(22);
        int slotSize = Px(38);
        int gap = Px(6);
        int height = headerHeight + rows * slotSize + (rows - 1) * gap + Px(10);
        var rect = Panel(spriteBatch, y, height, UiTheme.Border);
        DrawSheetText(spriteBatch, "STASH", Px(10), UiTheme.Muted, new Vector2(rect.X + Px(10), rect.Y + Px(8)));

        int totalWidth = columns * slotSize + (columns - 1) * gap;
        float startX = rect.Center.X - totalWidth / 2f;
        int startY = rect.Y + headerHeight;
        _inventorySlotRects.Clear();
        for (int index = 0; index < InventorySlotCount; index++)
        {
            int column = index % columns, row = index / columns;
            var slotRect = new Rectangle((int)(startX + column * (slotSize + gap)), startY + row * (slotSize + gap), slotSize, slotSize);
            _inventorySlotRects.Add(slotRect);
            var item = state.Inventory[index];
            bool draggingThis = _draggingSource is InventoryDragSource inv && inv.Index == index;
            if (item is not null && !draggingThis)
            {
                bool hovered = slotRect.Contains(mousePosition);
                ItemCards.DrawItemCard(
                    spriteBatch, slotRect, item, hovered,
                    _presentationTime);
                if (hovered)
                    _tooltipItem = item;
            }
            else
            {
                Primitives2D.FillRect(spriteBatch, slotRect, UiTheme.Ink);
                Primitives2D.RectOutline(spriteBatch, slotRect, UiTheme.Border, Px(2));
            }
        }
        return rect.Bottom + _padding;
    }

    private int DrawLootPanel(SpriteBatch spriteBatch, RunState state, Point mousePosition, int y)
    {
        var crate = state.NearbyCrate;
        if (crate is null || crate.Items.Count == 0)
            return y;
        int headerHeight = Px(22);
        int slotSize = Px(38);
        int height = headerHeight + slotSize + Px(12);
        var rect = Panel(spriteBatch, y, height, UiTheme.Cream);
        DrawSheetText(spriteBatch, "NEARBY LOOT", Px(10), UiTheme.Cream, new Vector2(rect.X + Px(10), rect.Y + Px(8)));

        int gap = Px(10);
        int totalWidth = CrateSlotCount * slotSize + (CrateSlotCount - 1) * gap;
        float startX = rect.Center.X - totalWidth / 2f;
        int slotY = rect.Y + headerHeight + Px(4);
        _lootPanelSlotRects.Clear();
        for (int index = 0; index < CrateSlotCount; index++)
        {
            var slotRect = new Rectangle((int)(startX + index * (slotSize + gap)), slotY, slotSize, slotSize);
            _lootPanelSlotRects.Add(slotRect);
            if (index >= crate.Items.Count)
            {
                Primitives2D.FillRect(spriteBatch, slotRect, UiTheme.Ink);
                Primitives2D.RectOutline(spriteBatch, slotRect, UiTheme.Border, Px(2));
                continue;
            }
            var item = crate.Items[index];
            bool draggingThis = _draggingSource is CrateDragSource crateSource
                && ReferenceEquals(crateSource.Crate, crate) && crateSource.Index == index;
            if (!draggingThis)
            {
                bool hovered = slotRect.Contains(mousePosition);
                ItemCards.DrawItemCard(
                    spriteBatch, slotRect, item, hovered,
                    _presentationTime);
                if (hovered)
                    _tooltipItem = item;
            }
            else
            {
                Primitives2D.FillRect(spriteBatch, slotRect, UiTheme.Ink);
                Primitives2D.RectOutline(spriteBatch, slotRect, UiTheme.Border, Px(2));
            }
        }
        return rect.Bottom + _padding;
    }

    private static Rectangle Inflated(Rectangle rect, int dx, int dy)
    {
        rect.Inflate(dx, dy);
        return rect;
    }

    private Rectangle StatRow(SpriteBatch spriteBatch, Rectangle rect, int y, string symbol, string label, string value,
        string? rating, string? helpText, Point mousePosition)
    {
        var iconRect = new Rectangle(rect.X + Px(10), y - Px(3), Px(24), Px(24));
        Primitives2D.FillRoundedRect(spriteBatch, iconRect, UiTheme.Ink, Px(3));
        StatCards.DrawStatSymbol(spriteBatch, symbol, Inflated(iconRect, -Px(4), -Px(4)), UiTheme.Cream);
        var labelRect = DrawSheetText(spriteBatch, label, Px(9), UiTheme.Muted, new Vector2(iconRect.Right + Px(7), y));
        DrawSheetText(spriteBatch, value, Px(10), UiTheme.Text, new Vector2(iconRect.Right + Px(7), y + Px(12)));
        if (!string.IsNullOrEmpty(rating))
            DrawSheetText(spriteBatch, rating, Px(8), UiTheme.Green, new Vector2(rect.Right - Px(10), y + Px(1)), "topright");
        var hoverRect = new Rectangle(iconRect.X, y - Px(4), rect.Right - iconRect.X, Px(31));
        if (!string.IsNullOrEmpty(helpText) && hoverRect.Contains(mousePosition))
            _tooltip = helpText;
        return labelRect;
    }

    /// <summary>The four unfinished quests nearest completion, with catalog order breaking ties.</summary>
    public static List<QuestDefinition> ActiveTrackedQuests(GameProfileData profile, int maximum = 4)
    {
        return MetaProgression.Quests
            .Where(quest => !profile.CompletedQuests.Contains(quest.Key))
            .OrderByDescending(quest => Math.Min(1,
                profile.QuestProgress.GetValueOrDefault(quest.Counter) / (double)Math.Max(1, quest.Target)))
            .Take(Math.Max(0, maximum))
            .ToList();
    }

    private void DrawWeaponStatsPanel(SpriteBatch spriteBatch, Rectangle rect, RunState state, Point mousePosition)
    {
        double attacksPerSecond = AttacksPerSecond(state);
        var rows = new List<(string Symbol, string Label, string Value, string? Rating, string HelpText)>
        {
            ("Bullet Damage", "Damage", $"{state.BulletDamage} / hit", Rating(state.BulletDamage, 100),
                "The exact damage dealt by a normal projectile hit."),
            ("Attack Speed", "Fire rate", $"{attacksPerSecond:F2} / sec", Rating(attacksPerSecond, Simulation.FrameRate / 40.0),
                "The exact number of volleys fired each second."),
            ("Bullet Count", "Projectiles", ShotText(state), null,
                "Fractional projectile count becomes a chance to fire one bonus shot."),
            ("Crit Chance", "Critical", $"{state.CritChance * 100:F0}% chance / +{Math.Max(0, (state.CritDamage - 1) * 100):F0}% damage", null,
                "Critical chance and the bonus damage applied when it succeeds."),
            ("Bullet Pierce", "Piercing", PierceText(state), null,
                "How many enemies one projectile can damage before disappearing."),
            ("Defense", "Defense", $"Blocks {state.Defense} damage", Rating(state.Defense, 100),
                "Flat damage removed from every incoming hit."),
            ("Vitality", "Vitality", $"{state.Vitality} HP / sec", Rating(state.Vitality, 25),
                "Health recovered continuously each second."),
            ("Bullet Range", "Range", $"{state.BulletRange / Simulation.TileSize:F1} tiles", Rating(state.BulletRange, 250),
                "Approximate projectile travel distance."),
        };
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Blue, shadow: 6);
        DrawSheetText(spriteBatch, "YOUR WEAPON", Px(10), UiTheme.Blue, new Vector2(rect.X + Px(10), rect.Y + Px(8)));
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            StatRow(spriteBatch, rect, rect.Y + Px(29 + index * 31), row.Symbol, row.Label, row.Value, row.Rating, row.HelpText, mousePosition);
        }
    }

    private int QuestPanelHeight(int count, int columns)
    {
        int rows = Math.Max(1, (count + columns - 1) / columns);
        return Px(39 + rows * 31);
    }

    private void DrawQuestPanel(SpriteBatch spriteBatch, Rectangle rect, IReadOnlyList<QuestDefinition> quests,
        bool showAll, Point mousePosition)
    {
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Green, shadow: 6);
        DrawSheetText(spriteBatch, showAll ? "ALL QUESTS" : "TRACKED QUESTS", Px(10), UiTheme.Green,
            new Vector2(rect.X + Px(10), rect.Y + Px(8)));
        DrawSheetText(spriteBatch, showAll ? $"{quests.Count} TOTAL" : "TOP 4 ACTIVE", Px(8), UiTheme.Muted,
            new Vector2(rect.Right - Px(10), rect.Y + Px(9)), "topright");
        if (quests.Count == 0)
        {
            DrawSheetText(spriteBatch, "Every quest is complete.", Px(9), UiTheme.Cream,
                new Vector2(rect.Center.X, rect.Y + Px(47)), "center");
            return;
        }

        int columns = showAll && rect.Width >= Px(480) ? 3 : showAll ? 2 : 1;
        int gap = Px(5);
        int cellWidth = (rect.Width - Px(20) - gap * (columns - 1)) / columns;
        int cellHeight = Px(27);
        int top = rect.Y + Px(31);
        for (int index = 0; index < quests.Count; index++)
        {
            var quest = quests[index];
            int column = index % columns, row = index / columns;
            var cell = new Rectangle(rect.X + Px(10) + column * (cellWidth + gap), top + row * Px(31), cellWidth, cellHeight);
            bool complete = GameProfile.Profile.CompletedQuests.Contains(quest.Key);
            long value = Math.Min(quest.Target, GameProfile.Profile.QuestProgress.GetValueOrDefault(quest.Counter));
            Primitives2D.FillRect(spriteBatch, cell, UiTheme.Panel);
            Primitives2D.RectOutline(spriteBatch, cell, complete ? UiTheme.Green : UiTheme.Border, 1);
            string name = quest.Name.Length > 18 ? quest.Name[..18] : quest.Name;
            DrawSheetText(spriteBatch, name.ToUpperInvariant(), Px(8), complete ? UiTheme.Green : UiTheme.Text,
                new Vector2(cell.X + Px(6), cell.Y + Px(4)));
            string progressText = complete ? "DONE" : showAll
                ? $"{value / (double)Math.Max(1, quest.Target):P0}"
                : $"{value:N0}/{quest.Target:N0}";
            DrawSheetText(spriteBatch, progressText, Px(8),
                complete ? UiTheme.Green : UiTheme.Muted, new Vector2(cell.Right - Px(6), cell.Y + Px(4)), "topright");
            UiTheme.DrawProgress(spriteBatch, new Rectangle(cell.X + Px(6), cell.Bottom - Px(7), cell.Width - Px(12), Px(4)),
                (float)value / Math.Max(1, quest.Target), UiTheme.Green, segments: 8);
            if (cell.Contains(mousePosition))
                _tooltip = $"{quest.Description} Progress: {value:N0}/{quest.Target:N0}. Reward: {quest.Reward} Soul token{(quest.Reward == 1 ? "" : "s")}.";
        }
    }

    private void DrawCosmeticPanel(SpriteBatch spriteBatch, Rectangle rect)
    {
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Purple, shadow: 6);
        DrawSheetText(spriteBatch, "COSMETIC CONFIGURATION", Px(10), UiTheme.Purple,
            new Vector2(rect.X + Px(10), rect.Y + Px(8)));

        var entries = new (string Label, string Value, Color? Core, Color? Edge)[]
        {
            ("CORE", Cosmetics.SelectedCore.Name, Cosmetics.SelectedCore.Color, null),
            ("EDGE", Cosmetics.SelectedEdge.Name, Cosmetics.SelectedEdge.Color, null),
            ("SHOT", Cosmetics.SelectedProjectile.Name, Cosmetics.SelectedProjectile.Core, Cosmetics.SelectedProjectile.Edge),
            ("DESIGN", Cosmetics.SelectedDesign.Name, null, null),
        };
        int top = rect.Y + Px(31);
        int columnWidth = (rect.Width - Px(25)) / 2;
        for (int index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            int column = index % 2, row = index / 2;
            var cell = new Rectangle(rect.X + Px(10) + column * (columnWidth + Px(5)), top + row * Px(39),
                columnWidth, Px(34));
            Primitives2D.FillRect(spriteBatch, cell, UiTheme.Panel);
            int textX = cell.X + Px(7);
            if (entry.Core is Color core)
            {
                var swatch = new Rectangle(cell.X + Px(6), cell.Center.Y - Px(9), Px(18), Px(18));
                Primitives2D.FillRect(spriteBatch, swatch, entry.Edge ?? core);
                if (entry.Edge is Color edge)
                {
                    var inner = swatch;
                    inner.Inflate(-Px(4), -Px(4));
                    Primitives2D.FillRect(spriteBatch, inner, core);
                    Primitives2D.RectOutline(spriteBatch, swatch, edge, 1);
                }
                else
                    Primitives2D.RectOutline(spriteBatch, swatch, UiTheme.Cream, 1);
                textX = swatch.Right + Px(6);
            }
            DrawSheetText(spriteBatch, entry.Label, Px(8), UiTheme.Muted, new Vector2(textX, cell.Y + Px(4)));
            DrawSheetText(spriteBatch, entry.Value.ToUpperInvariant(), Px(9), UiTheme.Text,
                new Vector2(textX, cell.Bottom - Px(4)), "bottomleft");
        }
        DrawSheetText(spriteBatch, "Change selections in the Soul Wardrobe.", Px(8), UiTheme.Muted,
            new Vector2(rect.X + Px(10), rect.Bottom - Px(8)), "bottomleft");
    }

    private int DrawObjective(SpriteBatch spriteBatch, RunState state, BountyInfo? bounty, Vector2 playerWorldPosition, int y)
    {
        var rect = Panel(spriteBatch, y, Px(72), UiTheme.Gold);
        var (_, color, ratio) = Pressure(state);
        var (name, detail) = BountyDetails(bounty, state, playerWorldPosition);
        string truncatedName = name.Length > 32 ? name[..32] : name;
        DrawSheetText(spriteBatch, truncatedName, Px(12), color, new Vector2(rect.X + Px(10), rect.Y + Px(8)));
        DrawSheetText(spriteBatch, detail, Px(9), UiTheme.Text, new Vector2(rect.X + Px(10), rect.Y + Px(26)));
        UiTheme.DrawProgress(spriteBatch, new Rectangle(rect.X + Px(10), rect.Y + Px(42), rect.Width - Px(20), Px(8)),
            (float)ratio, color, 8);
        var (level, milestone) = NextMilestone(state);
        DrawSheetText(spriteBatch, $"Next: level {level} • {milestone}", Px(8), UiTheme.Muted,
            new Vector2(rect.X + Px(10), rect.Bottom - Px(9)), "bottomleft");
        return rect.Bottom + _padding;
    }

    private void DrawRecentTable(SpriteBatch spriteBatch, RunState state, Point mousePosition, int minimumY)
    {
        int available = _totalHeight - minimumY - _padding;
        int height = Math.Max(Px(70), Math.Min(Px(96), available));
        int y = _totalHeight - height - _padding;
        var rect = Panel(spriteBatch, y, height, UiTheme.Cream, UiTheme.Panel);
        DrawSheetText(spriteBatch, "RECENT PICKS", Px(9), UiTheme.Muted, new Vector2(rect.X + Px(10), rect.Y + Px(7)));
        int tableY = rect.Y + Px(25);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(rect.X + Px(7), tableY, rect.Width - Px(14), rect.Bottom - tableY - Px(7)), UiTheme.Ink);
        Primitives2D.Line(spriteBatch, new Vector2(rect.X + Px(7), tableY), new Vector2(rect.Right - Px(7), tableY),
            UiTheme.Gold, Px(2));

        int historyCount = Math.Min(5, state.UpgradeHistory.Count);
        int historyStart = state.UpgradeHistory.Count - historyCount;
        if (historyCount == 0)
        {
            DrawSheetText(spriteBatch, "Your upgrade cards will collect here.", Px(8), UiTheme.Muted,
                new Vector2(rect.Center.X, tableY + (rect.Bottom - tableY) / 2f), "center");
            return;
        }
        int gap = Px(5);
        float maxCardWidth =
            (rect.Width - Px(24) - (historyCount - 1) * gap)
            / (float)historyCount;
        float cardHeight = Math.Min(Math.Min(Px(58), rect.Bottom - tableY - Px(10)), maxCardWidth / .72f);
        int cardWidth = (int)(cardHeight * .72f);
        float total =
            historyCount * cardWidth + (historyCount - 1) * gap;
        float startX = rect.Center.X - total / 2f;
        for (int index = 0; index < historyCount; index++)
        {
            var entry = state.UpgradeHistory[historyStart + index];
            var cardRect = new Rectangle((int)(startX + index * (cardWidth + gap)), tableY + Px(5), cardWidth, (int)cardHeight);
            bool hovered = cardRect.Contains(mousePosition);
            StatCards.DrawUpgradeCard(spriteBatch, cardRect, entry.Name, entry.Rarity, entry.MathType, hovered);
            if (hovered)
            {
                string mode = entry.MathType == "additive" ? "Flat increase" : "Multiplicative increase";
                _tooltip = $"{entry.Rarity} {entry.Name} • {mode}";
            }
        }
    }

    private static Rectangle ClampToBounds(Rectangle rect, Rectangle bounds)
    {
        int x = Math.Clamp(rect.X, bounds.X, Math.Max(bounds.X, bounds.Right - rect.Width));
        int y = Math.Clamp(rect.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - rect.Height));
        return new Rectangle(x, y, rect.Width, rect.Height);
    }

    /// <summary>Breaks text into lines no wider than maxWidth at fontSize, so callers can size a panel to the wrapped line count before drawing rather than letting long text run past a fixed-height box.</summary>
    private List<string> WrapText(string text, double fontSize, int maxWidth)
    {
        var font = UiTheme.RawFont(fontSize * LocalTextScale());
        var words = text.Split(' ');
        var lines = new List<string>();
        string line = "";
        foreach (var word in words)
        {
            string candidate = (line + " " + word).Trim();
            if (font.MeasureString(candidate).X > maxWidth && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        lines.Add(line);
        return lines;
    }

    private void DrawTooltip(SpriteBatch spriteBatch, Point mousePosition)
    {
        if (_tooltipItem is not null)
        {
            DrawItemTooltip(spriteBatch, mousePosition, _tooltipItem);
            return;
        }
        if (string.IsNullOrEmpty(_tooltip))
            return;
        int width = Math.Min(Px(250), (int)(_screenWidth * .24));
        var lines = WrapText(_tooltip, Px(9), width - Px(18));
        var rect = new Rectangle(mousePosition.X - width - Px(10), mousePosition.Y + Px(10), width, Px(15 + lines.Count * 14));
        rect = ClampToBounds(rect, new Rectangle(_posX, 0, _totalLength, _totalHeight));
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Cream, shadow: 4);
        for (int index = 0; index < lines.Count; index++)
            DrawSheetText(spriteBatch, lines[index], Px(9), UiTheme.Text,
                new Vector2(rect.X + Px(9), rect.Y + Px(8 + index * 14)));
    }

    private void DrawItemTooltip(SpriteBatch spriteBatch, Point mousePosition, ItemDrop item)
    {
        var effects = Items.Effects(item);
        var statuses = Items.EffectiveStatusChances(item);
        var coreForge = Items.CoreForgeFor(item);
        int width = Math.Min(Px(320), (int)(_screenWidth * .34));
        int headerHeight = Px(coreForge is null ? 74 : 94);
        int rowHeight = Px(38);
        // Wrapped up front (rather than drawn at a fixed one-line height) so
        // long flavor text -- e.g. Grimsbane's -- breaks onto extra lines
        // instead of running past the panel's right edge, and the panel is
        // sized to actually fit however many lines that took.
        var descriptionLines = WrapText($"“{item.Definition.Description}”", Px(10), width - Px(30));
        // A unique's EffectFlavorText callout (see ItemDefinition's doc
        // comment) sits where the StatusChances "X% ON HIT" rows go, for
        // signature effects like Grimsbane's Bane stacking that aren't
        // chance-based and so never generate one of those rows themselves.
        var effectFlavorLines = item.Definition.EffectFlavorText is { } effectFlavorText
            ? WrapText(effectFlavorText, Px(11), width - Px(32))
            : new List<string>();
        int effectFlavorHeight = effectFlavorLines.Count > 0 ? effectFlavorLines.Count * Px(15) + Px(10) : 0;
        int height = headerHeight + effects.Count * rowHeight + statuses.Count * Px(30) + effectFlavorHeight
            + Px(34) + descriptionLines.Count * Px(14);
        var rect = new Rectangle(mousePosition.X - width - Px(12), mousePosition.Y + Px(10), width, height);
        rect = ClampToBounds(rect, new Rectangle(0, 0, _screenWidth, _totalHeight));
        Color rarity = UiTheme.RarityColors.TryGetValue(item.Rarity, out var rarityColor) ? rarityColor : UiTheme.Border;
        Color? coreColor = coreForge is not null ? GamePaths.PathsByKey[coreForge.PathKey].Accent : null;
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, coreColor ?? rarity, shadow: 7);

        // Same dark-backdrop-plus-shine treatment as ItemCards.DrawItemCard's
        // Unique branch, kept in sync manually since this header icon is a
        // separate small draw path (a plain rect, not a rounded card).
        bool isUnique = item.Rarity == "Unique";
        var symbolRect = new Rectangle(rect.X + Px(12), rect.Y + Px(12), Px(50), Px(50));
        Primitives2D.FillRect(spriteBatch, symbolRect, isUnique ? UiTheme.Ink : rarity);
        Primitives2D.RectOutline(spriteBatch, symbolRect, isUnique ? rarity : UiTheme.Ink, Px(2));
        var symbolInner = symbolRect;
        symbolInner.Inflate(-Px(7), -Px(7));
        ItemCards.DrawItemSymbol(spriteBatch, item.SlotType, symbolInner, isUnique ? UiTheme.Gold : UiTheme.Ink, item.Definition.VisualKind, item.Name);
        if (isUnique)
            ItemCards.DrawUniqueSheen(spriteBatch, symbolRect);
        DrawSheetText(spriteBatch, item.DisplayName.ToUpperInvariant(), Px(15), UiTheme.Text,
            new Vector2(symbolRect.Right + Px(11), rect.Y + Px(14)));
        Color gradeColor = UiTheme.GradeColors.GetValueOrDefault(item.Grade, UiTheme.Gold);
        DrawSheetText(spriteBatch, $"{item.Rarity.ToUpperInvariant()}  //  GRADE {item.Grade}  //  {item.Modifier.ToUpperInvariant()}", Px(9), gradeColor,
            new Vector2(symbolRect.Right + Px(11), rect.Y + Px(40)));
        if (coreForge is not null)
            DrawSheetText(spriteBatch, $"✦  {coreForge.DisplayName.ToUpperInvariant()}  //  HARD MODE FORGED", Px(9), coreColor ?? UiTheme.Gold,
                new Vector2(rect.X + Px(14), rect.Y + Px(68)));

        int y = rect.Y + headerHeight;
        foreach (var effect in effects)
        {
            var row = new Rectangle(rect.X + Px(10), y, rect.Width - Px(20), rowHeight - Px(4));
            Primitives2D.FillRect(spriteBatch, row, UiTheme.Panel);
            var icon = new Rectangle(row.X + Px(6), row.Y + Px(4), Px(27), Px(27));
            StatCards.DrawStatSymbol(spriteBatch, effect.Stat, icon, rarity);
            DrawSheetText(spriteBatch, effect.Stat.ToUpperInvariant(), Px(9), UiTheme.Muted,
                new Vector2(icon.Right + Px(8), row.Center.Y), "midleft");
            Color valueColor = effect.IsBeneficial ? UiTheme.Green : UiTheme.Red;
            DrawSheetText(spriteBatch, effect.DisplayValue, Px(16), valueColor,
                new Vector2(row.Right - Px(8), row.Center.Y), "midright");
            y += rowHeight;
        }
        foreach (var (kind, chance) in statuses)
        {
            DrawSheetText(spriteBatch, $"✦  {kind.ToUpperInvariant()}  {chance * 100:0}% ON HIT", Px(11), UiTheme.Green,
                new Vector2(rect.X + Px(16), y + Px(5)));
            y += Px(30);
        }
        if (effectFlavorLines.Count > 0)
        {
            // Fixed red for every unique's callout for now -- EffectFlavorText
            // is authored per item, so a per-item color is a natural follow-up
            // once more than one is wanted, without touching this draw path.
            Color effectFlavorColor = UiTheme.Red;
            y += Px(5);
            foreach (var line in effectFlavorLines)
            {
                DrawSheetText(spriteBatch, line, Px(11), effectFlavorColor, new Vector2(rect.X + Px(16), y));
                y += Px(15);
            }
            y += Px(5);
        }
        Primitives2D.Line(spriteBatch, new Vector2(rect.X + Px(12), y), new Vector2(rect.Right - Px(12), y), UiTheme.Border, 1);
        for (int index = 0; index < descriptionLines.Count; index++)
            DrawSheetText(spriteBatch, descriptionLines[index], Px(10), UiTheme.Cream,
                new Vector2(rect.X + Px(15), y + Px(12) + index * Px(14)));
    }

    /// <summary>
    /// Paused, scrollable inspection surface. It deliberately reuses this
    /// class's established item hit-rect and drag controller so loadout rules
    /// remain identical in the dossier, nearby loot, and the Soul Vault.
    /// </summary>
    public void DrawDossier(
        SpriteBatch spriteBatch,
        RunState state,
        Vector2 playerWorldPosition,
        BountyInfo? currentBounty,
        Point mousePosition,
        PathRun? pathRun = null)
    {
        _playerLevelCap = pathRun is null
            ? Progression.MaxLevel
            : Progression.DungeonMaxLevel;
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        SyncLayout(viewport.Width, viewport.Height);
        _presentationPath = pathRun?.CurrentSenseKey ?? GamePaths.Active().Key;
        _presentationTime = (float)state.RunTimeSeconds;
        _tooltip = null;
        _tooltipItem = null;
        _equipmentSlotRects.Clear();
        _inventorySlotRects.Clear();
        _lootPanelSlotRects.Clear();
        _loadoutFocus.BeginFrame();

        Primitives2D.FillRect(spriteBatch, viewport.Bounds, UiTheme.Ink * .86f);
        int margin = Math.Max(Px(12), (int)(viewport.Width * .025f));
        var frame = new Rectangle(margin, margin,
            viewport.Width - margin * 2, viewport.Height - margin * 2);
        UiTheme.DrawCompositePanel(spriteBatch, frame, _presentationTime,
            UiTheme.Void * .98f, UiTheme.Cream, shadow: 9);

        int headerHeight = Px(66);
        int contentTop = frame.Y + headerHeight;
        int contentBottom = frame.Bottom - Px(14);
        int contentLeft = frame.X + Px(18);
        int contentWidth = frame.Width - Px(48);
        DrawSheetText(spriteBatch, "RUN DOSSIER", Px(22), UiTheme.Text,
            new Vector2(frame.X + Px(22), frame.Y + Px(15)));
        string pathLabel = pathRun is null
            ? GamePaths.Active().Title.ToUpperInvariant()
            : $"FLOOR {pathRun.FloorNumber:D2}/{PathRun.TotalFloors:D2}  //  {pathRun.SenseDisplayName.ToUpperInvariant()}  //  {(pathRun.IsSecondAct ? "DESCENT II" : "DESCENT I")}";
        DrawSheetText(spriteBatch, pathLabel, Px(9),
            pathRun?.CurrentSense.Accent ?? UiTheme.Purple,
            new Vector2(frame.X + Px(24), frame.Y + Px(44)));
        var elapsed = TimeSpan.FromSeconds(Math.Max(0, state.RunTimeSeconds));
        DrawSheetText(spriteBatch,
            $"LEVEL {state.CurrentLevel:D2}  //  {elapsed.Minutes:D2}:{elapsed.Seconds:D2}{(state.HardMode ? "  //  HARD MODE" : "")}{(state.NewGamePlusLevel > 0 ? $"  //  NG+{state.NewGamePlusLevel}" : "")}",
            Px(9), UiTheme.Muted, new Vector2(frame.Right - Px(24), frame.Y + Px(44)), "topright");
        Primitives2D.Line(spriteBatch, new Vector2(frame.X + Px(16), contentTop - Px(5)),
            new Vector2(frame.Right - Px(16), contentTop - Px(5)), UiTheme.Border, Px(1));

        GraphicsDevice graphicsDevice = spriteBatch.GraphicsDevice;
        Rectangle previousScissor = graphicsDevice.ScissorRectangle;
        spriteBatch.End();
        graphicsDevice.ScissorRectangle = new Rectangle(contentLeft, contentTop,
            contentWidth, Math.Max(1, contentBottom - contentTop));
        spriteBatch.Begin(rasterizerState: ScissorRasterizerState);

        int y = contentTop - (int)_dossierScroll;
        int sectionGap = Px(10);

        int runHeight = Px(112);
        var runRect = new Rectangle(contentLeft, y, contentWidth, runHeight);
        if (runRect.Bottom >= contentTop && runRect.Y <= contentBottom)
            DrawDossierRunHeader(spriteBatch, runRect, state, currentBounty, playerWorldPosition, mousePosition);
        y = runRect.Bottom + sectionGap;

        int loadoutHeight = Px(state.NearbyCrate is { Items.Count: > 0 } ? 225 : 169);
        var loadoutRect = new Rectangle(contentLeft, y, contentWidth, loadoutHeight);
        if (loadoutRect.Bottom >= contentTop && loadoutRect.Y <= contentBottom)
            DrawDossierLoadout(spriteBatch, loadoutRect, state, mousePosition);
        y = loadoutRect.Bottom + sectionGap;

        int buildHeight = Px(226);
        var buildRect = new Rectangle(contentLeft, y, contentWidth, buildHeight);
        if (buildRect.Bottom >= contentTop && buildRect.Y <= contentBottom)
            DrawDossierBuild(spriteBatch, buildRect, state, mousePosition);
        y = buildRect.Bottom + sectionGap;

        IReadOnlyList<QuestDefinition> quests = ActiveTrackedQuests(GameProfile.Profile);
        int progressHeight = Px(150 + Math.Max(1, quests.Count) * 26);
        var progressRect = new Rectangle(contentLeft, y, contentWidth, progressHeight);
        if (progressRect.Bottom >= contentTop && progressRect.Y <= contentBottom)
            DrawDossierProgress(spriteBatch, progressRect, state, currentBounty, playerWorldPosition, quests);
        y = progressRect.Bottom + Px(18);
        _dossierContentHeight = Math.Max(0, y - contentTop);
        int visibleHeight = Math.Max(1, contentBottom - contentTop);
        _dossierScroll = Math.Clamp(_dossierScroll, 0, Math.Max(0, _dossierContentHeight - visibleHeight));

        spriteBatch.End();
        graphicsDevice.ScissorRectangle = previousScissor;
        spriteBatch.Begin();

        if (_dossierContentHeight > visibleHeight)
        {
            var track = new Rectangle(frame.Right - Px(18), contentTop, Px(4), visibleHeight);
            Primitives2D.FillRect(spriteBatch, track, UiTheme.Ink);
            int thumbHeight = Math.Max(Px(24), (int)(visibleHeight * (visibleHeight / (float)_dossierContentHeight)));
            int thumbY = track.Y + (int)((track.Height - thumbHeight)
                * (_dossierScroll / Math.Max(1, _dossierContentHeight - visibleHeight)));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle(track.X, thumbY, track.Width, thumbHeight), UiTheme.Purple);
        }

        if (_draggingItem is not null)
        {
            int slotSize = Px(44);
            ItemCards.DrawItemCard(spriteBatch,
                new Rectangle(mousePosition.X - slotSize / 2, mousePosition.Y - slotSize / 2, slotSize, slotSize),
                _draggingItem, hovered: true, animationTime: _presentationTime);
        }
        else
        {
            DrawTooltip(spriteBatch, mousePosition);
        }
    }

    public void HandleDossierDrag(
        RunState state,
        Vector2 playerWorldPosition,
        Point mousePosition,
        bool mouseDown,
        bool mousePressed) =>
        HandleDrag(state, playerWorldPosition, mousePosition, mouseDown, mousePressed,
            allowWorldDrop: false, explicitDropRect: _dossierDropRect);

    public void RegisterVaultFocus(IReadOnlyList<Rectangle> vaultSlotRects)
    {
        _vaultSlotRects = vaultSlotRects;
        for (int index = 0; index < vaultSlotRects.Count; index++)
            _loadoutFocus.Register($"vault:{index}", vaultSlotRects[index],
                index < GameProfile.Profile.Storage.Count || _draggingItem is not null);
    }

    public bool IsLoadoutFocused(string id) => _loadoutFocus.IsFocused(id);

    public void BeginLoadoutFocus() => _loadoutFocus.BeginFrame();

    /// <summary>
    /// Keyboard/controller counterpart to mouse drag/drop. Confirm picks up
    /// or places the focused item; Back cancels a held item.
    /// </summary>
    public bool HandleLoadoutNavigation(RunState state,
        Vector2 playerWorldPosition,
        IReadOnlySet<Microsoft.Xna.Framework.Input.Keys> keysPressed,
        IReadOnlyList<Rectangle>? vaultSlotRects = null,
        bool dossier = false)
    {
        if (vaultSlotRects is not null)
            _vaultSlotRects = vaultSlotRects;
        if ((InputState.ControllerBackPressed
                || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Escape))
            && DragInProgress)
        {
            CancelDrag();
            return true;
        }

        bool up = InputState.UiUpPressed
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Up)
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.W);
        bool down = InputState.UiDownPressed
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Down)
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.S);
        bool left = InputState.UiLeftPressed
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Left)
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.A);
        bool right = InputState.UiRightPressed
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Right)
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.D);
        string? before = _loadoutFocus.FocusedId;
        if (up) _loadoutFocus.Move(0, -1);
        if (down) _loadoutFocus.Move(0, 1);
        if (left) _loadoutFocus.Move(-1, 0);
        if (right) _loadoutFocus.Move(1, 0);
        if (dossier && before == _loadoutFocus.FocusedId)
        {
            if (down) _dossierScroll += Px(70);
            if (up) _dossierScroll = Math.Max(0, _dossierScroll - Px(70));
        }

        bool confirm = InputState.ControllerConfirmPressed
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Enter)
            || keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Space);
        if (!confirm || _loadoutFocus.FocusedId is not { } focusedId)
            return up || down || left || right;
        if (focusedId is "dossier:level" or "dossier:reforge")
            return false;

        UiFocusTarget target = _loadoutFocus.Targets.LastOrDefault(
            candidate => candidate.Id == focusedId);
        if (target.Id is null)
            return false;
        if (focusedId == "dossier:drop" && !DragInProgress)
            return true;
        if (!focusedId.StartsWith("equipment:")
            && !focusedId.StartsWith("inventory:")
            && !focusedId.StartsWith("crate:")
            && !focusedId.StartsWith("vault:")
            && focusedId != "dossier:drop")
        {
            return false;
        }

        Point point = target.Rect.Center;
        if (!DragInProgress)
            HandleDrag(state, playerWorldPosition, point, mouseDown: true,
                mousePressed: true, vaultSlotRects: _vaultSlotRects, allowWorldDrop: false,
                explicitDropRect: dossier ? _dossierDropRect : null);
        else
            HandleDrag(state, playerWorldPosition, point, mouseDown: false,
                mousePressed: false, vaultSlotRects: _vaultSlotRects, allowWorldDrop: false,
                explicitDropRect: dossier ? _dossierDropRect : null);
        return true;
    }

    private void DrawDossierRunHeader(
        SpriteBatch spriteBatch,
        Rectangle rect,
        RunState state,
        BountyInfo? bounty,
        Vector2 playerWorldPosition,
        Point mousePosition)
    {
        UiTheme.DrawCompositePanel(spriteBatch, rect, _presentationTime,
            UiTheme.PanelRaised, UiTheme.Purple, shadow: 4);
        RefreshSummaryCache(state, rect.Width - Px(32));
        DrawSheetText(spriteBatch, _summaryTitle, Px(16), UiTheme.Purple,
            new Vector2(rect.X + Px(14), rect.Y + Px(12)));
        DrawSheetText(spriteBatch, _summaryStrength, Px(9), UiTheme.Text,
            new Vector2(rect.X + Px(14), rect.Y + Px(37)));
        DrawSheetText(spriteBatch, _summaryCaution, Px(8), UiTheme.Muted,
            new Vector2(rect.X + Px(14), rect.Y + Px(55)));
        var (name, detail) = BountyDetails(bounty, state, playerWorldPosition);
        DrawSheetText(spriteBatch, name, Px(10), UiTheme.Gold,
            new Vector2(rect.Center.X, rect.Y + Px(16)), "midtop");
        DrawSheetText(spriteBatch, detail, Px(8), UiTheme.Muted,
            new Vector2(rect.Center.X, rect.Y + Px(39)), "midtop");

        bool canLevel = state.CurrentLevel < _playerLevelCap
            && state.ExpCount >= state.ExpNeededForNextLevel;
        _dossierLevelRect = new Rectangle(rect.Right - Px(190), rect.Y + Px(18), Px(170), Px(36));
        _loadoutFocus.Register("dossier:level", _dossierLevelRect, canLevel);
        UiTheme.DrawButton(spriteBatch, _dossierLevelRect,
            state.CurrentLevel >= _playerLevelCap ? "MAX LEVEL" : canLevel ? "CHOOSE UPGRADE" : $"{state.ExpCount:0}/{state.ExpNeededForNextLevel:0} XP",
            mousePosition, enabled: canLevel, accentColor: UiTheme.Gold, textSize: Px(9));
        if (_loadoutFocus.IsFocused("dossier:level") && canLevel)
            Primitives2D.RectOutline(spriteBatch, _dossierLevelRect, UiTheme.Cream, Px(2));
        DrawSheetText(spriteBatch, "RUN STATE", Px(8), UiTheme.Muted,
            new Vector2(rect.Right - Px(105), rect.Bottom - Px(16)), "center");
    }

    private void DrawDossierLoadout(SpriteBatch spriteBatch, Rectangle rect, RunState state, Point mousePosition)
    {
        UiTheme.DrawCompositePanel(spriteBatch, rect, _presentationTime,
            UiTheme.PanelRaised, UiTheme.Cream, shadow: 4);
        DrawSheetText(spriteBatch, "LOADOUT", Px(12), UiTheme.Text,
            new Vector2(rect.X + Px(14), rect.Y + Px(10)));
        DrawSheetText(spriteBatch, "EQUIPPED", Px(8), UiTheme.Muted,
            new Vector2(rect.X + Px(16), rect.Y + Px(36)));

        string[] keys = ["weapon", "armor", "ring", "accessory_1", "accessory_2"];
        int slot = Px(43), gap = Px(7);
        int equipmentX = rect.X + Px(16);
        int slotY = rect.Y + Px(53);
        for (int index = 0; index < keys.Length; index++)
        {
            Rectangle slotRect = new(equipmentX + index * (slot + gap), slotY, slot, slot);
            _equipmentSlotRects[keys[index]] = slotRect;
            DrawDossierItem(spriteBatch, $"equipment:{keys[index]}", slotRect, state.Equipment.GetValueOrDefault(keys[index]),
                _draggingSource is EquipmentDragSource source && source.Key == keys[index], mousePosition);
        }

        int stashX = equipmentX + keys.Length * (slot + gap) + Px(32);
        DrawSheetText(spriteBatch, "STASH", Px(8), UiTheme.Muted,
            new Vector2(stashX, rect.Y + Px(36)));
        for (int index = 0; index < InventorySlotCount; index++)
        {
            int column = index % 4, row = index / 4;
            Rectangle slotRect = new(stashX + column * (slot + gap), slotY + row * (slot + gap), slot, slot);
            _inventorySlotRects.Add(slotRect);
            DrawDossierItem(spriteBatch, $"inventory:{index}", slotRect, state.Inventory[index],
                _draggingSource is InventoryDragSource source && source.Index == index, mousePosition);
        }

        _dossierReforgeRect = new Rectangle(rect.Right - Px(174), rect.Y + Px(48), Px(154), Px(36));
        bool canReforge = state.Equipment.Values.Any(item => item is not null);
        _loadoutFocus.Register("dossier:reforge", _dossierReforgeRect, canReforge);
        UiTheme.DrawButton(spriteBatch, _dossierReforgeRect, "REFORGE EQUIPMENT", mousePosition,
            enabled: canReforge, accentColor: UiTheme.Purple, textSize: Px(8));
        _dossierDropRect = new Rectangle(rect.Right - Px(174), rect.Y + Px(94), Px(154), Px(36));
        _loadoutFocus.Register("dossier:drop", _dossierDropRect, _draggingItem is not null);
        UiTheme.DrawButton(spriteBatch, _dossierDropRect, "DROP TO GROUND", mousePosition,
            enabled: _draggingItem is not null, accentColor: UiTheme.Red, textSize: Px(8));
        if (_loadoutFocus.IsFocused("dossier:reforge") && canReforge)
            Primitives2D.RectOutline(spriteBatch, _dossierReforgeRect, UiTheme.Cream, Px(2));
        if (_loadoutFocus.IsFocused("dossier:drop") && _draggingItem is not null)
            Primitives2D.RectOutline(spriteBatch, _dossierDropRect, UiTheme.Cream, Px(2));

        if (state.NearbyCrate is { } crate && crate.Items.Count > 0)
        {
            int lootY = rect.Bottom - Px(61);
            DrawSheetText(spriteBatch, "NEARBY LOOT", Px(8), UiTheme.Gold,
                new Vector2(rect.X + Px(16), lootY));
            int lootSlotY = lootY + Px(15);
            for (int index = 0; index < CrateSlotCount; index++)
            {
                Rectangle slotRect = new(rect.X + Px(120) + index * (slot + gap), lootSlotY, slot, slot);
                _lootPanelSlotRects.Add(slotRect);
                ItemDrop? item = index < crate.Items.Count ? crate.Items[index] : null;
                DrawDossierItem(spriteBatch, $"crate:{index}", slotRect, item,
                    _draggingSource is CrateDragSource source
                        && ReferenceEquals(source.Crate, crate) && source.Index == index, mousePosition);
            }
        }
    }

    private void DrawDossierItem(SpriteBatch spriteBatch, string focusId, Rectangle rect, ItemDrop? item,
        bool dragging, Point mousePosition)
    {
        _loadoutFocus.Register(focusId, rect, item is not null || _draggingItem is not null);
        if (item is not null && !dragging)
        {
            bool hovered = rect.Contains(mousePosition);
            ItemCards.DrawItemCard(spriteBatch, rect, item, hovered, _presentationTime);
            if (hovered)
                _tooltipItem = item;
            if (_loadoutFocus.IsFocused(focusId))
                Primitives2D.RoundedRectOutline(spriteBatch, rect, UiTheme.Cream, Px(2), Px(4));
            return;
        }
        Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink, Px(4));
        Primitives2D.RoundedRectOutline(spriteBatch, rect,
            rect.Contains(mousePosition) ? UiTheme.Cream : UiTheme.Border, Px(2), Px(4));
        if (_loadoutFocus.IsFocused(focusId))
            Primitives2D.RoundedRectOutline(spriteBatch, rect, UiTheme.Cream, Px(2), Px(4));
    }

    private void DrawDossierBuild(SpriteBatch spriteBatch, Rectangle rect, RunState state, Point mousePosition)
    {
        UiTheme.DrawCompositePanel(spriteBatch, rect, _presentationTime,
            UiTheme.PanelRaised, UiTheme.Blue, shadow: 4);
        DrawSheetText(spriteBatch, "BUILD READOUT", Px(12), UiTheme.Blue,
            new Vector2(rect.X + Px(14), rect.Y + Px(10)));
        var rows = new (string Symbol, string Label, string Value)[]
        {
            ("Bullet Damage", "Damage", $"{state.BulletDamage:N0}"),
            ("Attack Speed", "Attack rate", $"{AttacksPerSecond(state):0.00}/s"),
            ("Bullet Count", "Projectiles", $"{state.ProjectileCount:0.##}"),
            ("Crit Chance", "Critical", $"{state.CritChance * 100:0}% / +{Math.Max(0, state.CritDamage - 1) * 100:0}%"),
            ("Bullet Pierce", "Pierce", $"{state.BulletPierce:0.##}"),
            ("Defense", "Defense", $"{state.Defense:N0}"),
            ("Vitality", "Vitality", $"{state.Vitality:N0}/s"),
            ("Player Speed", "Move speed", $"{state.PlayerSpeed:0.00}"),
            ("Bullet Range", "Range", $"{state.BulletRange / Simulation.TileSize:0.0} tiles"),
        };
        int columns = 3;
        int cellGap = Px(7);
        int cellWidth = (rect.Width - Px(28) - cellGap * (columns - 1)) / columns;
        int startY = rect.Y + Px(38);
        for (int index = 0; index < rows.Length; index++)
        {
            int column = index % columns, row = index / columns;
            var cell = new Rectangle(rect.X + Px(14) + column * (cellWidth + cellGap),
                startY + row * Px(57), cellWidth, Px(50));
            Primitives2D.FillRoundedRect(spriteBatch, cell, UiTheme.Panel, Px(4));
            var icon = new Rectangle(cell.X + Px(8), cell.Y + Px(9), Px(28), Px(28));
            StatCards.DrawStatSymbol(spriteBatch, rows[index].Symbol, icon, UiTheme.Cream);
            DrawSheetText(spriteBatch, rows[index].Label.ToUpperInvariant(), Px(7), UiTheme.Muted,
                new Vector2(icon.Right + Px(7), cell.Y + Px(8)));
            DrawSheetText(spriteBatch, rows[index].Value, Px(10), UiTheme.Text,
                new Vector2(icon.Right + Px(7), cell.Bottom - Px(9)), "bottomleft");
        }
        IReadOnlyList<(string Category, int Count)> families = FamilyCounts(state);
        string familyText = families.Count == 0
            ? "NO UPGRADE FAMILY YET"
            : string.Join("   ", families.Take(4).Select(family => $"{family.Category.ToUpperInvariant()} x{family.Count}"));
        DrawSheetText(spriteBatch, familyText, Px(8), UiTheme.Purple,
            new Vector2(rect.X + Px(14), rect.Bottom - Px(12)), "bottomleft");
    }

    private void DrawDossierProgress(
        SpriteBatch spriteBatch,
        Rectangle rect,
        RunState state,
        BountyInfo? bounty,
        Vector2 playerWorldPosition,
        IReadOnlyList<QuestDefinition> quests)
    {
        UiTheme.DrawCompositePanel(spriteBatch, rect, _presentationTime,
            UiTheme.PanelRaised, UiTheme.Green, shadow: 4);
        DrawSheetText(spriteBatch, "PROGRESS", Px(12), UiTheme.Green,
            new Vector2(rect.X + Px(14), rect.Y + Px(10)));
        var (name, detail) = BountyDetails(bounty, state, playerWorldPosition);
        var (level, milestone) = NextMilestone(state);
        DrawSheetText(spriteBatch, name, Px(10), UiTheme.Gold,
            new Vector2(rect.X + Px(16), rect.Y + Px(38)));
        DrawSheetText(spriteBatch, detail, Px(8), UiTheme.Muted,
            new Vector2(rect.X + Px(16), rect.Y + Px(58)));
        DrawSheetText(spriteBatch, $"NEXT  //  LEVEL {level}  //  {milestone.ToUpperInvariant()}", Px(8), UiTheme.Text,
            new Vector2(rect.X + Px(16), rect.Y + Px(79)));

        DrawSheetText(spriteBatch, "ACTIVE QUESTS", Px(8), UiTheme.Muted,
            new Vector2(rect.X + Px(16), rect.Y + Px(108)));
        int y = rect.Y + Px(128);
        foreach (QuestDefinition quest in quests)
        {
            long value = Math.Min(quest.Target, GameProfile.Profile.QuestProgress.GetValueOrDefault(quest.Counter));
            DrawSheetText(spriteBatch, quest.Name.ToUpperInvariant(), Px(8), UiTheme.Text,
                new Vector2(rect.X + Px(18), y));
            DrawSheetText(spriteBatch, $"{value:N0}/{quest.Target:N0}", Px(8), UiTheme.Muted,
                new Vector2(rect.Right - Px(18), y), "topright");
            UiTheme.DrawProgress(spriteBatch,
                new Rectangle(rect.X + Px(18), y + Px(15), rect.Width - Px(36), Px(5)),
                (float)value / Math.Max(1, quest.Target), UiTheme.Green, 10);
            y += Px(26);
        }
        if (quests.Count == 0)
            DrawSheetText(spriteBatch, "ALL QUESTS COMPLETE", Px(9), UiTheme.Cream,
                new Vector2(rect.X + Px(18), y));
    }

    /// <summary>
    /// Draws the whole sidebar and refreshes every hit-test rect used by
    /// <see cref="HandleDrag"/>. Call once per frame, before HandleDrag --
    /// see this class's doc comment for why the order matters.
    /// </summary>
    public void DrawSheet(SpriteBatch spriteBatch, RunState state, Vector2 playerWorldPosition, BountyInfo? currentBounty,
        Point mousePosition, PathRun? pathRun = null)
    {
        _playerLevelCap = pathRun is null
            ? Progression.MaxLevel
            : Progression.DungeonMaxLevel;
        _presentationPath = pathRun?.CurrentSenseKey
            ?? GamePaths.Active().Key;
        _presentationTime = (float)state.RunTimeSeconds;
        _tooltip = null;
        _tooltipItem = null;
        _lootPanelSlotRects.Clear();
        Primitives2D.FillRect(spriteBatch, new Rectangle(_posX, 0, _totalLength, _totalHeight), UiTheme.Void);
        Primitives2D.FillRect(spriteBatch, new Rectangle(_posX, 0, Px(6), _totalHeight), UiTheme.Ink);

        int y = DrawRunSummary(spriteBatch, state, pathRun);
        y = DrawStatus(spriteBatch, state, mousePosition, y);
        y = DrawInventory(spriteBatch, state, mousePosition, y);
        y = DrawStash(spriteBatch, state, mousePosition, y);
        if (state.NearbyCrate is not null && y + Px(70) < _totalHeight - Px(82))
            y = DrawLootPanel(spriteBatch, state, mousePosition, y);
        if (y + Px(90) < _totalHeight - Px(82))
            y = DrawObjective(spriteBatch, state, currentBounty, playerWorldPosition, y);
        DrawRecentTable(spriteBatch, state, mousePosition, y);


        if (_draggingItem is not null)
        {
            int slotSize = Px(38);
            var iconRect = new Rectangle(mousePosition.X - slotSize / 2, mousePosition.Y - slotSize / 2, slotSize, slotSize);
            ItemCards.DrawItemCard(
                spriteBatch, iconRect, _draggingItem, hovered: true,
                animationTime: _presentationTime);
        }
        else
        {
            DrawTooltip(spriteBatch, mousePosition);
        }
    }

    /// <summary>
    /// A lighter sidebar for the Soul: just the equipment hub and stash, no
    /// health/status/loot/objective/recent-picks -- none of that means
    /// anything outside a run. Same visual language and the same
    /// DrawInventory/DrawStash this class already uses in DrawSheet, so the
    /// Soul's "your inventory" panel looks and behaves identically to the
    /// in-run sidebar. Call once per frame, before HandleDrag, same contract
    /// as DrawSheet.
    /// </summary>
    public void DrawSoulLoadoutPanel(
        SpriteBatch spriteBatch,
        RunState state,
        Rectangle panel,
        Point mousePosition,
        float animationTime = 0f)
    {
        _presentationTime = animationTime;
        _tooltipItem = null;
        UiTheme.DrawCompositePanel(spriteBatch, panel, animationTime,
            UiTheme.PanelRaised, UiTheme.Purple, 4);
        int pad = Math.Max(7, (int)(11 * _uiScale));
        UiTheme.DrawText(spriteBatch, "CARRIED LOADOUT", 13 * _uiScale,
            UiTheme.Text, new Vector2(panel.X + pad, panel.Y + pad));
        UiTheme.DrawText(spriteBatch, "DRAG BETWEEN EQUIPMENT, STASH, AND VAULT",
            7 * _uiScale, UiTheme.Muted,
            new Vector2(panel.X + pad, panel.Y + 34 * _uiScale));

        int equipmentTop = panel.Y + Math.Max(43, (int)(52 * _uiScale));
        int available = panel.Width - pad * 2;
        int gap = Math.Max(3, (int)(6 * _uiScale));
        int slotSize = Math.Clamp((available - gap * 4) / 5, 24,
            Math.Max(24, (int)(52 * _uiScale)));
        int equipmentWidth = slotSize * 5 + gap * 4;
        int equipmentLeft = panel.Center.X - equipmentWidth / 2;
        _equipmentSlotRects.Clear();
        for (int index = 0; index < EquipmentSlots.Length; index++)
        {
            var (label, key, _) = EquipmentSlots[index];
            var rect = new Rectangle(equipmentLeft + index * (slotSize + gap),
                equipmentTop, slotSize, slotSize);
            _equipmentSlotRects[key] = rect;
            DrawSoulSlot(spriteBatch, $"equipment:{key}", rect, state.Equipment[key],
                _draggingSource is EquipmentDragSource source && source.Key == key,
                mousePosition);
            UiTheme.DrawText(spriteBatch, label, 6 * _uiScale, UiTheme.Muted,
                new Vector2(rect.Center.X, rect.Bottom + 2 * _uiScale), "midtop");
        }

        int stashTitleY = equipmentTop + slotSize + Math.Max(18, (int)(23 * _uiScale));
        UiTheme.DrawText(spriteBatch, "STASH", 9 * _uiScale, UiTheme.Cream,
            new Vector2(panel.X + pad, stashTitleY));
        int stashTop = stashTitleY + Math.Max(15, (int)(20 * _uiScale));
        const int columns = 4;
        int stashGap = Math.Max(3, (int)(6 * _uiScale));
        int stashSlot = Math.Min(slotSize,
            Math.Max(22, (available - stashGap * (columns - 1)) / columns));
        int stashWidth = stashSlot * columns + stashGap * (columns - 1);
        int stashLeft = panel.Center.X - stashWidth / 2;
        _inventorySlotRects.Clear();
        for (int index = 0; index < InventorySlotCount; index++)
        {
            int column = index % columns;
            int row = index / columns;
            var rect = new Rectangle(stashLeft + column * (stashSlot + stashGap),
                stashTop + row * (stashSlot + stashGap), stashSlot, stashSlot);
            _inventorySlotRects.Add(rect);
            DrawSoulSlot(spriteBatch, $"inventory:{index}", rect, state.Inventory[index],
                _draggingSource is InventoryDragSource source && source.Index == index,
                mousePosition);
        }
        UiTheme.DrawText(spriteBatch,
            $"{state.Inventory.Count(item => item is not null)}/{InventorySlotCount} CARRIED  //  {GameProfile.Profile.SoulTokens} SOUL TOKENS",
            7 * _uiScale, UiTheme.Muted,
            new Vector2(panel.X + pad, panel.Bottom - pad), "bottomleft");

        if (_draggingItem is not null)
        {
            int iconSize = Math.Max(28, (int)(38 * _uiScale));
            ItemCards.DrawItemCard(spriteBatch,
                new Rectangle(mousePosition.X - iconSize / 2,
                    mousePosition.Y - iconSize / 2, iconSize, iconSize),
                _draggingItem, hovered: true, animationTime: _presentationTime);
        }
        else
        {
            DrawTooltip(spriteBatch, mousePosition);
        }
    }

    private void DrawSoulSlot(SpriteBatch spriteBatch, string focusId, Rectangle rect,
        ItemDrop? item, bool dragging, Point mousePosition)
    {
        _loadoutFocus.Register(focusId, rect, item is not null || _draggingItem is not null);
        if (item is not null && !dragging)
        {
            bool hovered = rect.Contains(mousePosition);
            ItemCards.DrawItemCard(spriteBatch, rect, item, hovered, _presentationTime);
            if (hovered) _tooltipItem = item;
            if (_loadoutFocus.IsFocused(focusId))
                Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream,
                    Math.Max(1, (int)(2 * _uiScale)));
            return;
        }
        Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Border,
            Math.Max(1, (int)(2 * _uiScale)));
        if (_loadoutFocus.IsFocused(focusId))
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream,
                Math.Max(1, (int)(2 * _uiScale)));
    }

    /// <summary>
    /// Press-capture / release-resolution half of the drag gesture. Call once per frame,
    /// after the dossier or Soul loadout panel has populated its slot rects.
    /// <paramref name="vaultSlotRects"/> is only ever non-null while in the Soul (see
    /// SoulHub) -- a normal run leaves it null/empty, so the Vault-drag branches below
    /// (and in ResolveDrop) never trigger during gameplay. <paramref name="allowWorldDrop"/>
    /// is false only from the Soul too: the Soul never draws or lets you interact with
    /// loot crates, so ejecting an invalid drop into one there would make the item
    /// disappear for good -- cancel instead (see ResolveDrop's invalid-drop fallback).
    /// </summary>
    public void HandleDrag(RunState state, Vector2 playerWorldPosition, Point mousePosition, bool mouseDown, bool mousePressed,
        IReadOnlyList<Rectangle>? vaultSlotRects = null, bool allowWorldDrop = true,
        Rectangle? explicitDropRect = null, bool lootOnlyPickup = false)
    {
        _vaultSlotRects = vaultSlotRects ?? Array.Empty<Rectangle>();
        _allowWorldDrop = allowWorldDrop;
        _explicitDropRect = explicitDropRect ?? Rectangle.Empty;
        if (mousePressed)
            _loadoutFocus.Focus(_loadoutFocus.At(mousePosition));
        if (_draggingItem is null)
        {
            if (!mousePressed)
                return;
            if (!lootOnlyPickup)
            {
                foreach (var (key, rect) in _equipmentSlotRects)
                {
                    if (rect.Contains(mousePosition) && state.Equipment[key] is not null)
                    {
                        _draggingItem = state.Equipment[key];
                        _draggingSource = new EquipmentDragSource(key);
                        DragInProgress = true;
                        return;
                    }
                }
            }
            for (int index = 0; index < _lootPanelSlotRects.Count; index++)
            {
                if (_lootPanelSlotRects[index].Contains(mousePosition) && state.NearbyCrate is not null
                    && index < state.NearbyCrate.Items.Count)
                {
                    _draggingItem = state.NearbyCrate.Items[index];
                    _draggingSource = new CrateDragSource(state.NearbyCrate, index);
                    DragInProgress = true;
                    return;
                }
            }
            if (!lootOnlyPickup)
            {
                for (int index = 0; index < _inventorySlotRects.Count; index++)
                {
                    if (_inventorySlotRects[index].Contains(mousePosition) && state.Inventory[index] is not null)
                    {
                        _draggingItem = state.Inventory[index];
                        _draggingSource = new InventoryDragSource(index);
                        DragInProgress = true;
                        return;
                    }
                }
                for (int index = 0; index < _vaultSlotRects.Count; index++)
                {
                    if (_vaultSlotRects[index].Contains(mousePosition) && index < GameProfile.Profile.Storage.Count)
                    {
                        _draggingItem = Items.Deserialize(GameProfile.Profile.Storage[index]);
                        _draggingSource = new VaultDragSource(index);
                        DragInProgress = true;
                        return;
                    }
                }
            }
            return;
        }

        if (mouseDown)
            return;

        ResolveDrop(state, playerWorldPosition, mousePosition);
        state.CombinePlayerStats();
        _draggingItem = null;
        _draggingSource = null;
        DragInProgress = false;
    }

    private void ResolveDrop(RunState state, Vector2 playerWorldPosition, Point mousePosition)
    {
        var item = _draggingItem!;
        var source = _draggingSource!;

        string? targetKey = null;
        foreach (var (key, rect) in _equipmentSlotRects)
        {
            if (rect.Contains(mousePosition) && EquipmentSlotTypes[key] == item.SlotType)
            {
                targetKey = key;
                break;
            }
        }

        if (targetKey is not null)
        {
            if (source is EquipmentDragSource equipmentSource)
            {
                if (equipmentSource.Key == targetKey)
                    return; // released back over its own slot -- treat as a cancelled drag
                // Swap: works whether the target slot is occupied or empty.
                (state.Equipment[equipmentSource.Key], state.Equipment[targetKey]) =
                    (state.Equipment[targetKey], state.Equipment[equipmentSource.Key]);
            }
            else if (source is CrateDragSource crateSource)
            {
                var displaced = state.Equipment[targetKey];
                state.Equipment[targetKey] = item;
                GameProfile.DiscoverItem(item.Name);
                ReturnDisplacedToCrate(state, crateSource, displaced);
            }
            else if (source is InventoryDragSource equipFromStash)
            {
                // Swap: works whether the equipment slot is occupied or empty.
                (state.Inventory[equipFromStash.Index], state.Equipment[targetKey]) =
                    (state.Equipment[targetKey], state.Inventory[equipFromStash.Index]);
            }
            else if (source is VaultDragSource equipFromVault)
            {
                var displaced = state.Equipment[targetKey];
                state.Equipment[targetKey] = item;
                PlaceInVault(equipFromVault.Index, displaced);
                MetaProgression.SyncCarriedItems(state);
            }
            return;
        }

        int targetInventoryIndex = -1;
        for (int index = 0; index < _inventorySlotRects.Count; index++)
        {
            if (_inventorySlotRects[index].Contains(mousePosition))
            {
                targetInventoryIndex = index;
                break;
            }
        }

        if (targetInventoryIndex >= 0)
        {
            // Unlike equipment slots, a stash slot has no SlotType restriction -- it can hold anything.
            if (source is InventoryDragSource stashSource)
            {
                if (stashSource.Index == targetInventoryIndex)
                    return; // released back over its own slot -- treat as a cancelled drag
                (state.Inventory[stashSource.Index], state.Inventory[targetInventoryIndex]) =
                    (state.Inventory[targetInventoryIndex], state.Inventory[stashSource.Index]);
            }
            else if (source is EquipmentDragSource stashFromEquipment)
            {
                (state.Equipment[stashFromEquipment.Key], state.Inventory[targetInventoryIndex]) =
                    (state.Inventory[targetInventoryIndex], state.Equipment[stashFromEquipment.Key]);
            }
            else if (source is CrateDragSource crateSource)
            {
                var displaced = state.Inventory[targetInventoryIndex];
                state.Inventory[targetInventoryIndex] = item;
                GameProfile.DiscoverItem(item.Name);
                ReturnDisplacedToCrate(state, crateSource, displaced);
            }
            else if (source is VaultDragSource stashFromVault)
            {
                var displaced = state.Inventory[targetInventoryIndex];
                state.Inventory[targetInventoryIndex] = item;
                PlaceInVault(stashFromVault.Index, displaced);
                MetaProgression.SyncCarriedItems(state);
            }
            return;
        }

        int targetVaultIndex = -1;
        for (int index = 0; index < _vaultSlotRects.Count; index++)
        {
            if (_vaultSlotRects[index].Contains(mousePosition))
            {
                targetVaultIndex = index;
                break;
            }
        }

        if (targetVaultIndex >= 0)
        {
            var vault = GameProfile.Profile.Storage;
            if (source is VaultDragSource selfVault)
            {
                if (selfVault.Index == targetVaultIndex)
                    return; // released back over its own slot -- treat as a cancelled drag
            }
            if (targetVaultIndex >= vault.Count && vault.Count >= MetaProgression.StorageCapacity)
                return; // vault full and this slot is past the current items -- cancel

            ItemDrop? displaced = null;
            if (targetVaultIndex < vault.Count)
            {
                displaced = Items.Deserialize(vault[targetVaultIndex]);
                vault[targetVaultIndex] = Items.Serialize(item);
            }
            else
            {
                vault.Add(Items.Serialize(item));
            }
            if (source is EquipmentDragSource vaultFromEquipment) state.Equipment[vaultFromEquipment.Key] = displaced;
            else if (source is InventoryDragSource vaultFromStash) state.Inventory[vaultFromStash.Index] = displaced;
            else if (source is VaultDragSource vaultFromVault) PlaceInVault(vaultFromVault.Index, displaced);
            else if (source is CrateDragSource vaultFromCrate)
            {
                // Not reachable today (no loot crates exist in the Soul, the only place
                // vaultSlotRects is ever populated), but handled properly rather than left
                // as a silent duplication bug if that ever changes.
                GameProfile.DiscoverItem(item.Name);
                ReturnDisplacedToCrate(state, vaultFromCrate, displaced);
            }
            MetaProgression.SyncCarriedItems(state);
            return;
        }

        if (_explicitDropRect.Contains(mousePosition))
        {
            if (source is EquipmentDragSource explicitUnequip)
            {
                state.Equipment[explicitUnequip.Key] = null;
                DropIntoWorld(state, playerWorldPosition, item);
            }
            else if (source is InventoryDragSource explicitUnstash)
            {
                state.Inventory[explicitUnstash.Index] = null;
                DropIntoWorld(state, playerWorldPosition, item);
            }
            return;
        }

        if (!_allowWorldDrop)
            return; // the Soul has nowhere to eject an invalid drop to -- cancel, item stays put.

        if (source is EquipmentDragSource unequip)
        {
            state.Equipment[unequip.Key] = null;
            DropIntoWorld(state, playerWorldPosition, item);
        }
        else if (source is InventoryDragSource unstash)
        {
            state.Inventory[unstash.Index] = null;
            DropIntoWorld(state, playerWorldPosition, item);
        }
        // source is CrateDragSource or VaultDragSource and the drop target was invalid: no-op, item stays put.
    }

    /// <summary>Swaps the vault slot a drag came from back to `displaced`, or removes it if nothing displaced the dragged item.</summary>
    private static void PlaceInVault(int index, ItemDrop? displaced)
    {
        var vault = GameProfile.Profile.Storage;
        if (displaced is not null)
            vault[index] = Items.Serialize(displaced);
        else
            vault.RemoveAt(index);
    }

    private static void ReturnDisplacedToCrate(RunState state, CrateDragSource crateSource, ItemDrop? displaced)
    {
        if (displaced is not null)
        {
            crateSource.Crate.Items[crateSource.Index] = displaced;
        }
        else
        {
            crateSource.Crate.Items.RemoveAt(crateSource.Index);
            if (crateSource.Crate.Items.Count == 0)
            {
                state.LootCrateList.Remove(crateSource.Crate);
                if (ReferenceEquals(state.NearbyCrate, crateSource.Crate))
                    state.NearbyCrate = null;
            }
        }
    }

    private static void DropIntoWorld(RunState state, Vector2 playerWorldPosition, ItemDrop item)
    {
        var crate = state.NearbyCrate;
        if (crate is not null && crate.Items.Count < CrateSlotCount)
            crate.Items.Add(item);
        else
            state.LootCrateList.Add(new LootCrate(playerWorldPosition.X, playerWorldPosition.Y, new[] { item }));
    }
}
