using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

/// <summary>
/// Friendly, outcome-first run sidebar with collectible upgrade icon cards.
/// Ported from informationSheet.py.
///
/// Design notes vs. the Python original:
/// - <see cref="RunState"/> is taken directly rather than a purpose-built
///   snapshot type (contrast <see cref="LevelUpStatSnapshot"/>/
///   <c>RunResultsSnapshot</c>): this sheet reads nearly RunState's entire
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
/// - The old compact/expanded HudMode is gone: the sidebar is a single
///   fixed width now. The build-identity panel moved to its own arena
///   overlay (<see cref="DrawBuildIdentityOverlay"/>) and the weapon-stats
///   section moved into a Tab-toggled popup (<see cref="DrawWeaponStatsPopup"/>,
///   <see cref="ToggleWeaponStats"/>) instead of always occupying sidebar
///   space.
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

    public const int CrateSlotCount = 4;

    private abstract record DragSource;
    private sealed record EquipmentDragSource(string Key) : DragSource;
    private sealed record CrateDragSource(LootCrate Crate, int Index) : DragSource;

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
    private Dictionary<string, Rectangle> _equipmentSlotRects = new();
    private List<Rectangle> _lootPanelSlotRects = new();
    private bool _weaponStatsOpen;

    public int ArenaWidth => _posX;
    public bool DragInProgress { get; private set; }

    /// <summary>Abandons the current drag without moving either item.</summary>
    public void CancelDrag()
    {
        _draggingItem = null;
        _draggingSource = null;
        DragInProgress = false;
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
        _totalLength = Math.Max(Px(220), Math.Min(Px(320), (int)(screenWidth * .15)));
        // Never consume more than 42% of a narrow display.
        _totalLength = Math.Min(_totalLength, (int)(screenWidth * .42));
        _totalHeight = screenHeight;
        _posX = screenWidth - _totalLength;
        _padding = Px(9);
    }

    /// <summary>Call from GameSession.Resize whenever the window size or GuiScale changes.</summary>
    public void SyncLayout(int screenWidth, int screenHeight)
    {
        float nextScale = UiTheme.DisplayScale(screenWidth, screenHeight);
        if (nextScale != _uiScale || screenWidth != _screenWidth || screenHeight != _screenHeight)
            BuildLayout(screenWidth, screenHeight);
    }

    /// <summary>
    /// TAB now opens/closes the weapon-stats popup (see DrawWeaponStatsPopup)
    /// instead of switching the sidebar's own compact/expanded width -- the
    /// sidebar is down to one fixed width now that the build-identity panel
    /// moved to its own arena overlay and the weapon section moved into this
    /// popup. Purely transient view state, not a persisted preference like
    /// the old HudMode was, so nothing here touches GameProfile.
    /// </summary>
    public void ToggleWeaponStats() => _weaponStatsOpen = !_weaponStatsOpen;

    private Rectangle Panel(SpriteBatch spriteBatch, int y, int height, Color? accent = null, Color? fill = null)
    {
        var rect = new Rectangle(_posX + _padding, y, _totalLength - _padding * 2, height);
        UiTheme.DrawPanel(spriteBatch, rect, fill ?? UiTheme.PanelRaised, accent ?? UiTheme.Border, shadow: 3);
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
        double threat = state.EnemyHolster.Where(enemy => !enemy.IsDead()).Sum(enemy => enemy.ThreatCost);
        double ratio = Math.Min(1, threat / Math.Max(1, state.EnemyThreatCap));
        if (state.EnemyHolster.Count == 0)
            return ("CALM", UiTheme.Green, ratio);
        if (state.EnemyHolster.Any(enemy => enemy.CombatRole == "elite"))
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
        int count = bounty.Target is RuntimeEncounter encounter
            ? encounter.Members.Count(member => !member.IsDead())
            : 1;
        string distance = tiles < 8 ? "Target nearby" : $"About {tiles:F0} tiles away";
        return (ToTitleCase(bounty.Label), $"{count} hostile{(count != 1 ? "s" : "")}  •  {distance}");
    }

    public static (int Level, string Milestone) NextMilestone(RunState state)
    {
        var gates = new List<(int Level, string Name)>();
        foreach (var (level, key) in Progression.MinibossGates)
            gates.Add((level, ToTitleCase(key.Replace("miniboss_", ""))));
        gates.Add((Progression.MidBossLevel, "Beaudis"));
        gates.Add((Progression.FinalBossLevel, "Dissonance"));
        gates.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.CompareOrdinal(a.Name, b.Name));
        foreach (var gate in gates)
            if (gate.Level > state.CurrentLevel)
                return gate;
        return (Progression.FinalBossLevel, "Complete");
    }

    /// <summary>Matches Python's str.title(): first letter of each space-separated word capitalized, rest lowercase.</summary>
    private static string ToTitleCase(string text) => string.Join(" ", text.Split(' ').Select(
        word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    // ----- Draw -----

    private int DrawHeader(SpriteBatch spriteBatch, RunState state)
    {
        var rect = Panel(spriteBatch, _padding, Px(62), UiTheme.Cream);
        DrawSheetText(spriteBatch, $"LEVEL {state.CurrentLevel:D2}", Px(18), UiTheme.Text,
            new Vector2(rect.X + Px(11), rect.Y + Px(9)));
        var (pressureLabel, color, _) = Pressure(state);
        DrawSheetText(spriteBatch, pressureLabel, Px(10), color,
            new Vector2(rect.Right - Px(11), rect.Y + Px(14)), "topright");
        DrawSheetText(spriteBatch, _weaponStatsOpen ? "Tab: close weapon stats" : "Tab: weapon stats", Px(9), UiTheme.Muted,
            new Vector2(rect.X + Px(11), rect.Bottom - Px(15)));
        return rect.Bottom + _padding;
    }

    private int DrawStatus(SpriteBatch spriteBatch, RunState state, int y)
    {
        Color healthColor = state.HealthPoints > state.MaxHealthPoints * .3 ? UiTheme.Green : UiTheme.Red;
        var rect = Panel(spriteBatch, y, Px(112), healthColor);
        Bar(spriteBatch, rect, rect.Y + Px(9), "HEALTH", state.HealthPoints, state.MaxHealthPoints, healthColor,
            $"{state.HealthPoints} / {state.MaxHealthPoints}");
        double dashValue = Math.Max(0, state.DashCooldownMax - state.CurrDashCooldown);
        string dashText = state.CurrDashCooldown <= 0 ? "READY" : $"{state.CurrDashCooldown / Simulation.FrameRate:F1} sec";
        Bar(spriteBatch, rect, rect.Y + Px(39), "DASH", dashValue, state.DashCooldownMax, UiTheme.Blue, dashText);
        double percent = state.ExpCount / Math.Max(1, state.ExpNeededForNextLevel) * 100;
        Bar(spriteBatch, rect, rect.Y + Px(69), "NEXT PICK", state.ExpCount, state.ExpNeededForNextLevel, UiTheme.Gold,
            $"{percent:F0}%");
        if (percent >= 82)
            DrawSheetText(spriteBatch, "Next pick soon", Px(9), UiTheme.Gold,
                new Vector2(rect.Right - Px(11), rect.Bottom - Px(11)), "bottomright");
        return rect.Bottom + _padding;
    }

    private int DrawInventory(SpriteBatch spriteBatch, RunState state, Point mousePosition, int y)
    {
        int headerHeight = Px(24);
        int hubHeight = Px(140);
        int height = headerHeight + hubHeight + Px(12);
        var rect = Panel(spriteBatch, y, height, UiTheme.Border);
        DrawSheetText(spriteBatch, "EQUIPMENT", Px(10), UiTheme.Muted, new Vector2(rect.X + Px(10), rect.Y + Px(8)));

        float hubX = rect.Center.X;
        float hubY = rect.Y + headerHeight + hubHeight / 2f;
        float radiusX = rect.Width * .28f;
        float radiusY = hubHeight * .38f;
        int slotSize = Px(38);

        var slots = new (string Label, string Key, float AngleDegrees)[]
        {
            ("WEAPON", "weapon", 90f),
            ("RING", "ring", 18f),
            ("ACC 2", "accessory_2", -54f),
            ("ACC 1", "accessory_1", -126f),
            ("ARMOR", "armor", 162f),
        };

        _equipmentSlotRects = new Dictionary<string, Rectangle>();
        foreach (var (label, key, angleDegrees) in slots)
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
                ItemCards.DrawItemCard(spriteBatch, slotRect, item, hovered);
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

    private int DrawLootPanel(SpriteBatch spriteBatch, RunState state, Point mousePosition, int y)
    {
        var crate = state.NearbyCrate;
        if (crate is null || crate.Items.Count == 0)
            return y;
        int headerHeight = Px(24);
        int slotSize = Px(38);
        int height = headerHeight + slotSize + Px(20);
        var rect = Panel(spriteBatch, y, height, UiTheme.Cream);
        DrawSheetText(spriteBatch, "NEARBY LOOT", Px(10), UiTheme.Cream, new Vector2(rect.X + Px(10), rect.Y + Px(8)));

        int gap = Px(10);
        int totalWidth = CrateSlotCount * slotSize + (CrateSlotCount - 1) * gap;
        float startX = rect.Center.X - totalWidth / 2f;
        int slotY = rect.Y + headerHeight + Px(4);
        _lootPanelSlotRects = new List<Rectangle>();
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
                ItemCards.DrawItemCard(spriteBatch, slotRect, item, hovered);
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

    /// <summary>
    /// Floats at the top of the arena, immediately left of the sidebar,
    /// rather than consuming vertical space among the sidebar's stacked
    /// panels -- moved out per user request to declutter the sidebar.
    /// Family rows shown is now always up to 4; there's no more compact/
    /// expanded distinction to gate it at 2.
    /// </summary>
    public void DrawBuildIdentityOverlay(SpriteBatch spriteBatch, RunState state)
    {
        int width = Math.Max(Px(220), Math.Min(Px(280), _posX - Px(24)));
        int height = Px(134);
        var rect = new Rectangle(_posX - _padding - width, _padding, width, height);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Purple, shadow: 3);
        var (title, strength, caution) = BuildIdentity(state);
        DrawSheetText(spriteBatch, title, Px(15), UiTheme.Purple, new Vector2(rect.X + Px(11), rect.Y + Px(9)));
        DrawSheetText(spriteBatch, strength, Px(9), UiTheme.Text, new Vector2(rect.X + Px(11), rect.Y + Px(31)));
        DrawSheetText(spriteBatch, caution, Px(9), UiTheme.Muted, new Vector2(rect.X + Px(11), rect.Y + Px(49)));
        var families = FamilyCounts(state);
        if (families.Count > 0)
        {
            const int maxPips = 5;
            int shown = Math.Min(families.Count, 4);
            for (int index = 0; index < shown; index++)
            {
                var (family, count) = families[index];
                int rowY = rect.Y + Px(68 + index * 16);
                DrawSheetText(spriteBatch, ToTitleCase(family), Px(9), UiTheme.Muted, new Vector2(rect.X + Px(11), rowY));
                for (int pip = 0; pip < maxPips; pip++)
                {
                    var pipRect = new Rectangle(rect.Right - Px(11 + (maxPips - pip) * 11), rowY + Px(1), Px(7), Px(7));
                    Primitives2D.FillRect(spriteBatch, pipRect, pip < count ? UiTheme.Purple : UiTheme.Ink);
                    Primitives2D.RectOutline(spriteBatch, pipRect, UiTheme.Border, 1);
                }
            }
        }
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

    /// <summary>
    /// Replaces the old always-on "YOUR WEAPON" sidebar section: this is
    /// now an on-demand popup toggled via Tab (see ToggleWeaponStats),
    /// centered over the arena so it doesn't compete with the sidebar for
    /// room. Always shows every stat row -- no more compact/expanded
    /// row-count split, since it's a deliberate look rather than
    /// always-on real estate.
    /// </summary>
    public void DrawWeaponStatsPopup(SpriteBatch spriteBatch, RunState state, Point mousePosition)
    {
        if (!_weaponStatsOpen)
            return;

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
        int width = Math.Min(Px(300), Math.Max(Px(220), _posX / 3));
        int height = Px(29 + rows.Count * 31);
        var rect = new Rectangle(_posX - _padding * 2 - width, (_totalHeight - height) / 2, width, height);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, UiTheme.Blue, shadow: 6);
        DrawSheetText(spriteBatch, "YOUR WEAPON", Px(10), UiTheme.Blue, new Vector2(rect.X + Px(10), rect.Y + Px(8)));
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            StatRow(spriteBatch, rect, rect.Y + Px(29 + index * 31), row.Symbol, row.Label, row.Value, row.Rating, row.HelpText, mousePosition);
        }
    }

    private int DrawObjective(SpriteBatch spriteBatch, RunState state, BountyInfo? bounty, Vector2 playerWorldPosition, int y)
    {
        var rect = Panel(spriteBatch, y, Px(80), UiTheme.Gold);
        var (_, color, ratio) = Pressure(state);
        var (name, detail) = BountyDetails(bounty, state, playerWorldPosition);
        string truncatedName = name.Length > 32 ? name[..32] : name;
        DrawSheetText(spriteBatch, truncatedName, Px(12), color, new Vector2(rect.X + Px(10), rect.Y + Px(8)));
        DrawSheetText(spriteBatch, detail, Px(9), UiTheme.Text, new Vector2(rect.X + Px(10), rect.Y + Px(29)));
        UiTheme.DrawProgress(spriteBatch, new Rectangle(rect.X + Px(10), rect.Y + Px(47), rect.Width - Px(20), Px(8)),
            (float)ratio, color, 8);
        var (level, milestone) = NextMilestone(state);
        DrawSheetText(spriteBatch, $"Next: level {level} • {milestone}", Px(8), UiTheme.Muted,
            new Vector2(rect.X + Px(10), rect.Bottom - Px(9)), "bottomleft");
        return rect.Bottom + _padding;
    }

    private void DrawRecentTable(SpriteBatch spriteBatch, RunState state, Point mousePosition, int minimumY)
    {
        int available = _totalHeight - minimumY - _padding;
        int height = Math.Max(Px(76), Math.Min(Px(112), available));
        int y = _totalHeight - height - _padding;
        var rect = Panel(spriteBatch, y, height, UiTheme.Cream, UiTheme.Panel);
        DrawSheetText(spriteBatch, "RECENT PICKS", Px(9), UiTheme.Muted, new Vector2(rect.X + Px(10), rect.Y + Px(7)));
        int tableY = rect.Y + Px(25);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(rect.X + Px(7), tableY, rect.Width - Px(14), rect.Bottom - tableY - Px(7)), UiTheme.Ink);
        Primitives2D.Line(spriteBatch, new Vector2(rect.X + Px(7), tableY), new Vector2(rect.Right - Px(7), tableY),
            UiTheme.Gold, Px(2));

        var history = state.UpgradeHistory.Count > 5
            ? state.UpgradeHistory.Skip(state.UpgradeHistory.Count - 5).ToList()
            : state.UpgradeHistory;
        if (history.Count == 0)
        {
            DrawSheetText(spriteBatch, "Your upgrade cards will collect here.", Px(8), UiTheme.Muted,
                new Vector2(rect.Center.X, tableY + (rect.Bottom - tableY) / 2f), "center");
            return;
        }
        int gap = Px(5);
        float maxCardWidth = (rect.Width - Px(24) - (history.Count - 1) * gap) / (float)history.Count;
        float cardHeight = Math.Min(Math.Min(Px(58), rect.Bottom - tableY - Px(10)), maxCardWidth / .72f);
        int cardWidth = (int)(cardHeight * .72f);
        float total = history.Count * cardWidth + (history.Count - 1) * gap;
        float startX = rect.Center.X - total / 2f;
        for (int index = 0; index < history.Count; index++)
        {
            var entry = history[index];
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
        var font = UiTheme.RawFont(Px(9) * LocalTextScale());
        var words = _tooltip.Split(' ');
        var lines = new List<string>();
        string line = "";
        foreach (var word in words)
        {
            string candidate = (line + " " + word).Trim();
            if (font.MeasureString(candidate).X > width - Px(18) && line.Length > 0)
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
        var statuses = item.Definition.StatusChances ?? new Dictionary<string, double>();
        int width = Math.Min(Px(320), (int)(_screenWidth * .34));
        int headerHeight = Px(74);
        int rowHeight = Px(38);
        int height = headerHeight + effects.Count * rowHeight + statuses.Count * Px(30) + Px(48);
        var rect = new Rectangle(mousePosition.X - width - Px(12), mousePosition.Y + Px(10), width, height);
        rect = ClampToBounds(rect, new Rectangle(0, 0, _screenWidth, _totalHeight));
        Color rarity = UiTheme.RarityColors.TryGetValue(item.Rarity, out var rarityColor) ? rarityColor : UiTheme.Border;
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, rarity, shadow: 7);

        var symbolRect = new Rectangle(rect.X + Px(12), rect.Y + Px(12), Px(50), Px(50));
        Primitives2D.FillRect(spriteBatch, symbolRect, rarity);
        Primitives2D.RectOutline(spriteBatch, symbolRect, UiTheme.Ink, Px(2));
        var symbolInner = symbolRect;
        symbolInner.Inflate(-Px(7), -Px(7));
        ItemCards.DrawItemSymbol(spriteBatch, item.SlotType, symbolInner, UiTheme.Ink, item.Definition.VisualKind);
        DrawSheetText(spriteBatch, item.Name.ToUpperInvariant(), Px(15), UiTheme.Text,
            new Vector2(symbolRect.Right + Px(11), rect.Y + Px(14)));
        DrawSheetText(spriteBatch, $"{item.Rarity.ToUpperInvariant()}  //  {item.SlotType.ToUpperInvariant()}", Px(9), rarity,
            new Vector2(symbolRect.Right + Px(11), rect.Y + Px(40)));

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
            double scaled = chance * Items.RarityPower(item.Rarity) * 100;
            DrawSheetText(spriteBatch, $"✦  {kind.ToUpperInvariant()}  {scaled:0}% ON HIT", Px(11), UiTheme.Green,
                new Vector2(rect.X + Px(16), y + Px(5)));
            y += Px(30);
        }
        Primitives2D.Line(spriteBatch, new Vector2(rect.X + Px(12), y), new Vector2(rect.Right - Px(12), y), UiTheme.Border, 1);
        DrawSheetText(spriteBatch, $"“{item.Definition.Description}”", Px(10), UiTheme.Cream,
            new Vector2(rect.X + Px(15), y + Px(12)));
    }

    /// <summary>
    /// Draws the whole sidebar and refreshes every hit-test rect used by
    /// <see cref="HandleDrag"/>. Call once per frame, before HandleDrag --
    /// see this class's doc comment for why the order matters.
    /// </summary>
    public void DrawSheet(SpriteBatch spriteBatch, RunState state, Vector2 playerWorldPosition, BountyInfo? currentBounty,
        Point mousePosition)
    {
        _tooltip = null;
        _tooltipItem = null;
        Primitives2D.FillRect(spriteBatch, new Rectangle(_posX, 0, _totalLength, _totalHeight), UiTheme.Void);
        Primitives2D.FillRect(spriteBatch, new Rectangle(_posX, 0, Px(6), _totalHeight), UiTheme.Ink);

        DrawBuildIdentityOverlay(spriteBatch, state);

        int y = DrawHeader(spriteBatch, state);
        y = DrawStatus(spriteBatch, state, y);
        y = DrawInventory(spriteBatch, state, mousePosition, y);
        if (state.NearbyCrate is not null && y + Px(70) < _totalHeight - Px(82))
            y = DrawLootPanel(spriteBatch, state, mousePosition, y);
        if (y + Px(90) < _totalHeight - Px(82))
            y = DrawObjective(spriteBatch, state, currentBounty, playerWorldPosition, y);
        DrawRecentTable(spriteBatch, state, mousePosition, y);

        DrawWeaponStatsPopup(spriteBatch, state, mousePosition);

        if (_draggingItem is not null)
        {
            int slotSize = Px(38);
            var iconRect = new Rectangle(mousePosition.X - slotSize / 2, mousePosition.Y - slotSize / 2, slotSize, slotSize);
            ItemCards.DrawItemCard(spriteBatch, iconRect, _draggingItem, hovered: true);
        }
        else
        {
            DrawTooltip(spriteBatch, mousePosition);
        }
    }

    /// <summary>
    /// Press-capture / release-resolution half of the drag gesture. Call
    /// once per frame, after <see cref="DrawSheet"/>.
    /// </summary>
    public void HandleDrag(RunState state, Vector2 playerWorldPosition, Point mousePosition, bool mouseDown, bool mousePressed)
    {
        if (_draggingItem is null)
        {
            if (!mousePressed)
                return;
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
            return;
        }

        if (source is EquipmentDragSource unequip)
        {
            state.Equipment[unequip.Key] = null;
            var crate = state.NearbyCrate;
            if (crate is not null && crate.Items.Count < CrateSlotCount)
                crate.Items.Add(item);
            else
                state.LootCrateList.Add(new LootCrate(playerWorldPosition.X, playerWorldPosition.Y, new[] { item }));
        }
        // source is CrateDragSource and the drop target was invalid: no-op, item stays put.
    }
}
