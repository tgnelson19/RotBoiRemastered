using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public enum DossierAction { None, Close }

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
    /// <summary>
    /// The Soul's Developer Armory (SoulHub.DeveloperArmoryItems) -- only
    /// reachable when the caller passes armorySlotRects into HandleDrag, same
    /// gating as VaultDragSource. Unlike every other source, this one never
    /// actually removes anything: the armory is an infinite catalog, so
    /// ResolveDrop only ever *places* the dragged copy and requires an empty
    /// destination (see its ArmoryDragSource branches) rather than swapping,
    /// which both matches the old click-to-copy behavior (also required a
    /// free slot) and means nothing is ever silently discarded.
    /// </summary>
    private sealed record ArmoryDragSource(int Index) : DragSource;

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
    private readonly UiFocusNavigator _loadoutFocus = new();
    private IReadOnlyList<Rectangle> _vaultSlotRects = Array.Empty<Rectangle>();
    private IReadOnlyList<Rectangle> _armorySlotRects = Array.Empty<Rectangle>();
    private bool _allowWorldDrop = true;
    // The compact stash panel (DrawDossier) is fixed-size and has no
    // explicit "drop to world" zone -- a leftover from the old full-screen
    // dossier it replaced (see the doc comment on DrawDossier). Left at its
    // default/empty value on purpose rather than removing the read sites.
    private Rectangle _dossierDropRect = Rectangle.Empty;
    private Rectangle _explicitDropRect;
    // _dossierScroll/_dossierContentHeight were dead weight before the
    // Progress tab below: the old stash-only panel never grew tall enough to
    // need them. Now DrawDossierProgressTab is the one real consumer.
    private double _dossierScroll;
    private int _dossierContentHeight = 0;
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

    /// <summary>
    /// The live combat-footer drag handler: equipment and the always-visible
    /// stash strip (FooterHud.DrawStash) are draggable any time during a
    /// run, not just while a crate happens to be nearby -- lootOnlyPickup
    /// used to gate this entirely behind NearbyCrate (see git history), but
    /// that was really only ever needed to keep the *crate* popup's own
    /// pickup from also grabbing equipment/stash underneath it. HandleDrag's
    /// crate-pickup branch already no-ops on its own when NearbyCrate is
    /// null, so calling it unconditionally with lootOnlyPickup: false is
    /// enough to cover crate + equipment + stash, whichever are actually
    /// present this frame.
    /// </summary>
    public void HandleLiveLootDrag(RunState state, Vector2 playerWorldPosition,
        Point mousePosition, bool mouseDown, bool mousePressed) =>
        HandleDrag(state, playerWorldPosition, mousePosition, mouseDown, mousePressed,
            allowWorldDrop: false, lootOnlyPickup: false);

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
        GameProfile.DiscoverItem(item.Name, state);
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

    /// <summary>
    /// Was also a Level Up/Reforge activation check (DossierAction.LevelUp/
    /// .Reforge), but this screen never actually drew those buttons -- their
    /// hit rects (_dossierLevelRect/_dossierReforgeRect) were declared and
    /// read here but never assigned by anything, and "dossier:level"/
    /// "dossier:reforge" were never registered as focus targets either, so
    /// both branches were permanently unreachable dead code. Leveling up and
    /// reforging both already have a real, reachable path (the footer's
    /// experience bar / the Dossier's actual buttons render nowhere close to
    /// here) -- see FooterHud.HandleInput's OpenLevelUp and
    /// GameSession.TryPurchaseLevelUp/State=Reforging in RotBoiGame. This is
    /// just Close now.
    /// </summary>
    public DossierAction HandleDossierAction(
        IReadOnlySet<Microsoft.Xna.Framework.Input.Keys> keysPressed)
    {
        if (!keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Tab)
            && !keysPressed.Contains(Microsoft.Xna.Framework.Input.Keys.Escape))
        {
            return DossierAction.None;
        }
        if (DragInProgress)
        {
            CancelDrag();
            return DossierAction.None;
        }
        CancelDrag();
        return DossierAction.Close;
    }

    private Rectangle Panel(SpriteBatch spriteBatch, int y, int height, Color? accent = null, Color? fill = null)
    {
        var rect = new Rectangle(_posX + _padding, y, _totalLength - _padding * 2, height);
        UiTheme.DrawFramedPanel(spriteBatch, rect, fill ?? UiTheme.PanelRaised,
            accent ?? UiTheme.Border, shadow: 3);
        return rect;
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

    /// <summary>Ported from `_combat_values`. The Python original also computed an expected-DPS value that its only call site immediately discarded (`attacks, _ = self._combat_values()`) -- dropped here since it's dead within this file too.</summary>
    public static double AttacksPerSecond(RunState state) => StatDisplay.AttacksPerSecond(state);

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

    /// <summary>Thin wrapper kept for call-site familiarity; the actual "stretch upward instead of clipping" logic lives in <see cref="UiTheme.ClampTooltipRect"/> so every tooltip in the game shares it.</summary>
    private static Rectangle ClampToBounds(Rectangle rect, Rectangle bounds) => UiTheme.ClampTooltipRect(rect, bounds);

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
        // A unique's (or, once Legendary/Mythical unlocks it, a regular
        // item's Signature's) EffectFlavorText callout sits where the
        // StatusChances "X% ON HIT" rows go, for signature effects like
        // Grimsbane's Bane stacking that aren't chance-based and so never
        // generate one of those rows themselves.
        var effectFlavorLines = Items.ActiveEffectFlavorText(item) is { } effectFlavorText
            ? WrapText(effectFlavorText, Px(11), width - Px(32))
            : new List<string>();
        int effectFlavorHeight = effectFlavorLines.Count > 0 ? effectFlavorLines.Count * Px(15) + Px(10) : 0;
        int ladderHeight = ItemCards.MeasureModifierLadder(Px(10), item);
        int height = headerHeight + effects.Count * rowHeight + statuses.Count * Px(30) + effectFlavorHeight
            + Px(34) + descriptionLines.Count * Px(14) + ladderHeight + Px(ladderHeight > 0 ? 14 : 0);
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
        int unlockedModifiers = Items.ModifierUnlockCount(item.Rarity);
        DrawSheetText(spriteBatch, $"{item.Rarity.ToUpperInvariant()}  //  {unlockedModifiers}/{item.Definition.ModifierLadder.Count} MODIFIERS UNLOCKED", Px(9), rarity,
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
        y += Px(12) + descriptionLines.Count * Px(14);

        if (ladderHeight > 0)
        {
            y += Px(6);
            Primitives2D.Line(spriteBatch, new Vector2(rect.X + Px(12), y), new Vector2(rect.Right - Px(12), y), UiTheme.Border, 1);
            ItemCards.DrawModifierLadder(spriteBatch, new Vector2(rect.X + Px(15), y + Px(8)), Px(9), item);
        }
    }

    /// <summary>
    /// The Tab/hud_toggle "Progress" panel -- meta road map (BuildRoadMapNodes)
    /// plus this run's live objectives (DrawExpeditionObjectives), scrollable
    /// (_dossierScroll). No stash grid and no equipment slots here anymore:
    /// both are always-visible and always-draggable in the combat footer
    /// instead (FooterHud.DrawStash/DrawEquipment), so nothing on this screen
    /// needs to be paused to reach -- opening it is purely informational now.
    /// Identical whether opened from a run or from The Mind.
    /// <paramref name="revealT"/> (0 = just opened, 1 = fully settled) drives
    /// the open animation: the panel slides up and fades in over its reveal
    /// window, and detailed content only renders once mostly settled so
    /// nothing has to lay out at a transitional size.
    /// </summary>
    public void DrawDossier(SpriteBatch spriteBatch, RunState state, Point mousePosition, float revealT,
        ExpeditionRun? expedition = null)
    {
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        SyncLayout(viewport.Width, viewport.Height);
        _presentationTime = (float)state.RunTimeSeconds;
        _tooltip = null;
        _tooltipItem = null;
        _loadoutFocus.BeginFrame();

        float eased = Math.Clamp(revealT, 0f, 1f);
        eased = eased * eased * (3f - 2f * eased);

        int margin = (int)(viewport.Width * .10f);
        int footerReserved = FooterHud.ReservedHeight(viewport.Width, viewport.Height);
        var panel = new Rectangle(margin, margin,
            Math.Max(Px(200), viewport.Width - margin * 2),
            Math.Max(Px(160), viewport.Height - footerReserved - margin * 2));

        Primitives2D.FillRect(spriteBatch, viewport.Bounds, UiTheme.Ink * (.7f * eased));
        int slide = (int)((1f - eased) * Px(46));
        var animatedPanel = new Rectangle(panel.X, panel.Y + slide, panel.Width, panel.Height);
        UiTheme.DrawFramedPanel(spriteBatch, animatedPanel,
            UiTheme.Void * Math.Max(.08f, .97f * eased), UiTheme.Cream * Math.Max(.15f, eased), shadow: 9);

        if (eased < .4f)
            return;

        // Stash/equipment used to live here (a paused-only panel) -- they're
        // both always-visible and always-draggable in the combat footer now
        // (see FooterHud.DrawStash), so this whole screen is just the
        // progression road map + this run's objectives. No tabs needed
        // anymore now that there's only one page.
        var contentPanel = new Rectangle(animatedPanel.X, animatedPanel.Y + Px(14),
            animatedPanel.Width, animatedPanel.Height - Px(14));
        DrawDossierProgressTab(spriteBatch, state, mousePosition, contentPanel, expedition);

        if (_draggingItem is not null)
        {
            int dragSize = Px(44);
            ItemCards.DrawItemCard(spriteBatch,
                new Rectangle(mousePosition.X - dragSize / 2, mousePosition.Y - dragSize / 2, dragSize, dragSize),
                _draggingItem, hovered: true, animationTime: _presentationTime);
        }
        else
        {
            DrawTooltip(spriteBatch, mousePosition);
        }
    }

    /// <summary>One card on the meta progression road map -- see BuildRoadMapNodes.</summary>
    private readonly record struct RoadMapNode(string Title, string Status, Color StatusColor, string Description);

    /// <summary>
    /// Every meta task the player can see progress on, as a flat ordered
    /// list -- rendered as a connected vertical "path" by DrawDossierProgressTab.
    /// Per the content policy (disregard past vagueness except one exception),
    /// every node here spells out real numbers/requirements in Description
    /// EXCEPT the Aphantasia/Core of the Void node, whose unlock condition is
    /// deliberately withheld (matching the portal's own "// SEALED" treatment
    /// in SoulHub) -- its existence isn't hidden, just how to reach it.
    /// </summary>
    private static IReadOnlyList<RoadMapNode> BuildRoadMapNodes()
    {
        var data = CampaignProgression.Data;
        var nodes = new List<RoadMapNode>
        {
            new("THE BODY", data.BodyCompleted ? "COMPLETE" : "IN PROGRESS",
                data.BodyCompleted ? UiTheme.Gold : UiTheme.Cream,
                "Defeat every sense's guardian across five hidden secrets to complete the Body and unlock the Soul."),
        };
        foreach (string sense in CampaignProgression.SenseKeys)
        {
            bool silver = data.SilverStatues.GetValueOrDefault(sense)?.Unlocked == true;
            bool gold = data.GoldStatues.GetValueOrDefault(sense)?.Unlocked == true;
            string status = gold ? "GOLD" : silver ? "SILVER" : "LOCKED";
            Color color = gold ? UiTheme.Gold : silver ? UiTheme.Cream : UiTheme.Muted;
            nodes.Add(new RoadMapNode($"{sense.ToUpperInvariant()} STATUE", status, color,
                silver
                    ? "Silver earned in the Body. Return as the Soul's finale for that sense to earn Gold."
                    : "Find and clear this sense's hidden guardian in the Body to earn its Silver statue."));
        }
        nodes.Add(new("THE SOUL", data.SoulUnlocked ? "OPEN" : "LOCKED",
            data.SoulUnlocked ? UiTheme.Cream : UiTheme.Muted,
            "Unlocks once the Body is complete. Clear all five senses again, tougher, to earn each Gold statue."));
        nodes.Add(new("SKILL TREE",
            $"{GameProfile.Profile.SkillLevels.Count(pair => pair.Value > 0)}/{MetaProgression.SkillNodes.Count} PURCHASED",
            UiTheme.Cream, "Permanent stat nodes bought with Mind Tokens at the Mind's skill station."));
        nodes.Add(new("QUESTS",
            $"{GameProfile.Profile.CompletedQuests.Count}/{MetaProgression.Quests.Count} COMPLETE",
            UiTheme.Cream, "Meta-progression tasks tracked automatically as you play; claim them at the quest board."));
        nodes.Add(new("VAULT CAPACITY",
            $"{MetaProgression.StorageCapacity} SLOTS",
            UiTheme.Cream, "Permanent item storage. Buy more rows with Mind Tokens at the Vault."));
        var litBraziers = new List<string>();
        if (GameProfile.Profile.HardModeEnabled) litBraziers.Add("HARD MODE");
        if (GameProfile.Profile.NoExtractEnabled) litBraziers.Add("NO-EXTRACT");
        if (GameProfile.Profile.GoldenFlameEnabled) litBraziers.Add("GOLDEN FLAME");
        if (GameProfile.Profile.VoidEnabled) litBraziers.Add("THE VOID");
        nodes.Add(new("OPTIONAL BRAZIERS", litBraziers.Count > 0 ? string.Join(", ", litBraziers) : "NONE LIT",
            litBraziers.Count > 0 ? UiTheme.Gold : UiTheme.Muted,
            "Four braziers around the Mind toggle optional run modifiers -- light them for a harder, better-rewarded run."));
        nodes.Add(new("THE VOID PASSAGE", GameProfile.Profile.VoidPassageDiscovered ? "DISCOVERED" : "HIDDEN",
            GameProfile.Profile.VoidPassageDiscovered ? UiTheme.Purple : UiTheme.Muted,
            GameProfile.Profile.VoidPassageDiscovered
                ? "A secret alcove found somewhere in the Mind's walls."
                : "Somewhere in the Mind's walls is a passage that doesn't open by walking -- the Golden Flame helps find it."));
        nodes.Add(new("APHANTASIA, CORE OF THE VOID", data.AphantasiaUnlocked ? "UNLOCKED" : "SEALED",
            data.AphantasiaUnlocked ? UiTheme.Gold : UiTheme.Muted,
            data.AphantasiaUnlocked
                ? "The Mind's final trial stands open." + (GameProfile.Profile.DefeatedCoreOfTheVoid ? " Already conquered." : "")
                : "The Mind's final secret. How it opens is not written here -- you'll know it when you've earned it."));
        return nodes;
    }

    private void DrawDossierProgressTab(SpriteBatch spriteBatch, RunState state, Point mousePosition,
        Rectangle panel, ExpeditionRun? expedition)
    {
        var content = new Rectangle(panel.X + Px(20), panel.Y, panel.Width - Px(40), panel.Height);
        BeginScissor(spriteBatch, content);
        int y = content.Y - (int)_dossierScroll;
        int lineWidth = content.Width;

        if (expedition is not null)
        {
            y = DrawExpeditionObjectives(spriteBatch, expedition, content, y);
            y += Px(18);
        }

        UiTheme.DrawText(spriteBatch, "MIND PROGRESSION", Px(15), UiTheme.Text,
            new Vector2(content.Center.X, y), "midtop");
        y += Px(24);

        IReadOnlyList<RoadMapNode> nodes = BuildRoadMapNodes();
        int cardHeight = Px(66);
        int cardGap = Px(10);
        for (int index = 0; index < nodes.Count; index++)
        {
            RoadMapNode node = nodes[index];
            var card = new Rectangle(content.X, y, lineWidth, cardHeight);
            if (index > 0)
            {
                // Connector line from the previous card's bottom-center to
                // this one's top-center -- what actually reads as a "path".
                Primitives2D.Line(spriteBatch,
                    new Vector2(card.Center.X, card.Y - cardGap), new Vector2(card.Center.X, card.Y),
                    UiTheme.Border, Px(2));
            }
            int radius = UiTheme.SmallCornerRadius(1f);
            Primitives2D.FillRoundedRect(spriteBatch, card, UiTheme.PanelRaised, radius);
            Primitives2D.RoundedRectOutline(spriteBatch, card, node.StatusColor * .8f, Px(2), radius);
            UiTheme.DrawText(spriteBatch, node.Title, Px(11), UiTheme.Text,
                new Vector2(card.X + Px(12), card.Y + Px(8)));
            UiTheme.DrawText(spriteBatch, node.Status, Px(10), node.StatusColor,
                new Vector2(card.Right - Px(12), card.Y + Px(8)), "topright");
            UiTheme.DrawWrappedText(spriteBatch, node.Description, Px(8.5), UiTheme.Muted,
                new Vector2(card.Center.X, card.Y + Px(45)), card.Width - Px(24));
            y += cardHeight + cardGap;
        }
        y += Px(12);

        _dossierContentHeight = y - (content.Y - (int)_dossierScroll);
        EndScissor(spriteBatch);
    }

    /// <summary>
    /// The current Body/Soul run's per-sense objectives, spelled out
    /// explicitly (per the content policy on BuildRoadMapNodes) rather than
    /// left to the deliberately cryptic JournalClue flavor text alone.
    /// Returns the y cursor after everything it drew.
    /// </summary>
    private int DrawExpeditionObjectives(SpriteBatch spriteBatch, ExpeditionRun expedition, Rectangle content, int y)
    {
        UiTheme.DrawText(spriteBatch,
            $"CURRENT EXPEDITION -- THE {expedition.World.ToString().ToUpperInvariant()}",
            Px(15), UiTheme.Text, new Vector2(content.Center.X, y), "midtop");
        y += Px(20);
        UiTheme.DrawText(spriteBatch,
            $"{expedition.DefeatedGuardians}/5 GUARDIANS DEFEATED",
            Px(10), UiTheme.Muted, new Vector2(content.Center.X, y), "midtop");
        y += Px(20);

        foreach (ExpeditionSecret secret in expedition.Secrets)
        {
            string statusText = secret.State switch
            {
                SecretState.Hidden => "UNDISCOVERED",
                SecretState.Discovered => "CLUE KNOWN",
                SecretState.Solved => "DUNGEON OPENING",
                SecretState.DungeonOpen => "DUNGEON READY",
                SecretState.GuardianDefeated => "GUARDIAN DEFEATED",
                _ => secret.State.ToString(),
            };
            Color statusColor = secret.State switch
            {
                SecretState.GuardianDefeated => UiTheme.Gold,
                SecretState.Hidden => UiTheme.Muted,
                _ => UiTheme.Cream,
            };
            string senseLabel = secret.SenseKey.ToUpperInvariant();
            string finaleNote = secret.IsFinale
                ? secret.IsAvailable(expedition.DefeatedGuardians)
                    ? "  //  FINALE, AVAILABLE NOW"
                    : "  //  FINALE -- DEFEAT 4/5 OTHER GUARDIANS TO UNLOCK"
                : "";
            UiTheme.DrawText(spriteBatch, $"{senseLabel}{finaleNote}", Px(9.5), UiTheme.Text,
                new Vector2(content.X, y));
            UiTheme.DrawText(spriteBatch, statusText, Px(9.5), statusColor,
                new Vector2(content.Right, y), "topright");
            y += Px(16);
        }
        return y;
    }

    private static void BeginScissor(SpriteBatch spriteBatch, Rectangle clip)
    {
        spriteBatch.End();
        var device = spriteBatch.GraphicsDevice;
        Rectangle viewport = device.Viewport.Bounds;
        device.ScissorRectangle = Rectangle.Intersect(clip, viewport) is { Width: > 0, Height: > 0 } clamped
            ? clamped
            : new Rectangle(viewport.X, viewport.Y, 1, 1);
        spriteBatch.Begin(rasterizerState: ScissorRasterizerState);
    }

    private static void EndScissor(SpriteBatch spriteBatch)
    {
        spriteBatch.End();
        spriteBatch.Begin();
    }

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

        if (!DragInProgress && focusedId.StartsWith("crate:")
            && int.TryParse(focusedId[6..], out int crateIndex)
            && state.NearbyCrate is { } nearby
            && crateIndex >= 0 && crateIndex < nearby.Items.Count)
        {
            ItemDrop loot = nearby.Items[crateIndex];
            string equipmentKey = loot.SlotType == "accessory"
                ? state.Equipment["accessory_1"] is null ? "accessory_1" : "accessory_2"
                : loot.SlotType;
            return QuickEquipLoot(state, crateIndex, equipmentKey);
        }

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

    public static IReadOnlyList<(string Stat, double Delta)> ItemEffectDeltas(
        ItemDrop candidate, ItemDrop? equipped)
    {
        static Dictionary<string, double> Values(ItemDrop? item)
        {
            var values = new Dictionary<string, double>();
            if (item is null) return values;
            foreach (ItemEffectView effect in Items.Effects(item))
                values[effect.Stat] = values.GetValueOrDefault(effect.Stat)
                    + effect.Additive + (effect.Multiplier - 1) * 100;
            return values;
        }
        Dictionary<string, double> next = Values(candidate);
        Dictionary<string, double> current = Values(equipped);
        return next.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal)
            .Select(stat => (stat, next.GetValueOrDefault(stat) - current.GetValueOrDefault(stat)))
            .OrderByDescending(entry => Math.Abs(entry.Item2)).ThenBy(entry => entry.Item1, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Draws the whole sidebar and refreshes every hit-test rect used by
    /// <see cref="HandleDrag"/>. Call once per frame, before HandleDrag --
    /// see this class's doc comment for why the order matters.
    /// </summary>
    /// <summary>
    /// A lighter sidebar for the Soul: just the equipment hub and stash, no
    /// health/status/loot/objective/recent-picks -- none of that means
    /// anything outside a run. Same visual language and the same
    /// DrawInventory/DrawStash this class already uses elsewhere, so the
    /// Soul's "your inventory" panel looks and behaves identically to the
    /// in-run sidebar. Call once per frame, before HandleDrag.
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
        UiTheme.DrawFramedPanel(spriteBatch, panel,
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
            $"{state.Inventory.Count(item => item is not null)}/{InventorySlotCount} CARRIED  //  {GameProfile.Profile.MindTokens} MIND TOKENS",
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
        Rectangle? explicitDropRect = null, bool lootOnlyPickup = false,
        IReadOnlyList<Rectangle>? armorySlotRects = null)
    {
        _vaultSlotRects = vaultSlotRects ?? Array.Empty<Rectangle>();
        _armorySlotRects = armorySlotRects ?? Array.Empty<Rectangle>();
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
                for (int index = 0; index < _armorySlotRects.Count; index++)
                {
                    if (_armorySlotRects[index].Contains(mousePosition) && index < SoulHub.DeveloperArmoryItems.Count)
                    {
                        _draggingItem = Items.DeveloperArmoryDrop(SoulHub.DeveloperArmoryItems[index]);
                        _draggingSource = new ArmoryDragSource(index);
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
                GameProfile.DiscoverItem(item.Name, state);
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
            else if (source is ArmoryDragSource)
            {
                // Infinite catalog, not a real slot to swap with -- only ever
                // places into an empty slot; an occupied one just cancels the
                // drag instead of discarding whatever's equipped there.
                if (state.Equipment[targetKey] is not null)
                    return;
                state.Equipment[targetKey] = item;
                GameProfile.IncrementQuest("items_found", state: state);
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
                GameProfile.DiscoverItem(item.Name, state);
                ReturnDisplacedToCrate(state, crateSource, displaced);
            }
            else if (source is VaultDragSource stashFromVault)
            {
                var displaced = state.Inventory[targetInventoryIndex];
                state.Inventory[targetInventoryIndex] = item;
                PlaceInVault(stashFromVault.Index, displaced);
                MetaProgression.SyncCarriedItems(state);
            }
            else if (source is ArmoryDragSource)
            {
                // See the equipment-slot ArmoryDragSource branch above -- same
                // "only into an empty slot, nothing ever displaced" rule.
                if (state.Inventory[targetInventoryIndex] is not null)
                    return;
                state.Inventory[targetInventoryIndex] = item;
                GameProfile.IncrementQuest("items_found", state: state);
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
                GameProfile.DiscoverItem(item.Name, state);
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
