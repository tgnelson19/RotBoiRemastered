using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public enum FooterAction { None, OpenDossier, OpenLevelUp }

public sealed record QuickLootCommand(int LootIndex, string EquipmentKey);

public sealed class FooterLayout
{
    public required Rectangle Bounds { get; init; }
    public required Rectangle Health { get; init; }
    public required Rectangle Dash { get; init; }
    public required Rectangle Equipment { get; init; }
    public required Rectangle Resources { get; init; }
    public required Rectangle Stats { get; init; }
    public required Rectangle Experience { get; init; }
    /// <summary>
    /// The carried stash, always visible directly beneath Equipment (not
    /// inside Bounds -- see CalculateLayout's doc comment on why it's its
    /// own band below the main panel rather than squeezed into it).
    /// </summary>
    public required Rectangle Stash { get; init; }
    public required IReadOnlyList<Rectangle> EquipmentSlots { get; init; }
    public required IReadOnlyList<Rectangle> StatSlots { get; init; }
    public required IReadOnlyList<Rectangle> StashSlots { get; init; }
    public bool Compact { get; init; }
}

public sealed class QuickLootLayout
{
    public required Rectangle Bounds { get; init; }
    public required Rectangle LootLabel { get; init; }
    public required IReadOnlyList<Rectangle> LootSlots { get; init; }
}

/// <summary>
/// Compact combat HUD. Equipment and the carried stash are both live during
/// combat now (see FooterLayout.Stash/StashSlots and DrawStash) -- items can
/// be dragged between them, or swapped with the 1-8 keys
/// (RotBoiGame.UpdateGameRun), any time during a run. Only nearby world loot
/// still needs its own transient quick-loot strip (DrawQuickLoot), since a
/// crate isn't always around to show a permanent panel for.
/// </summary>
public sealed class FooterHud
{
    private int _playerLevelCap = Progression.MaxLevel;
    private static readonly string[] EquipmentOrder =
        ["weapon", "armor", "ring", "accessory_1", "accessory_2"];
    private static readonly string[] EquipmentLabels = ["W", "A", "R", "1", "2"];

    private Rectangle _bounds;
    private Rectangle _stashBounds;
    private Rectangle _quickLootBounds;
    private Rectangle _equipmentHit;
    private Rectangle _experienceHit;
    private ItemDrop? _tooltipItem;
    private readonly Dictionary<string, Rectangle> _equipmentSlotRects = new();
    private readonly List<Rectangle> _stashSlotRects = new(InformationSheet.InventorySlotCount);
    private readonly List<Rectangle> _quickLootSlotRects = new(InformationSheet.CrateSlotCount);
    private int _quickLootSelection;
    private int _preferredAccessorySlot;

    public Rectangle Bounds => _bounds;
    public IReadOnlyDictionary<string, Rectangle> EquipmentSlotRects => _equipmentSlotRects;
    public IReadOnlyList<Rectangle> StashSlotRects => _stashSlotRects;
    public IReadOnlyList<Rectangle> QuickLootSlotRects => _quickLootSlotRects;
    // Includes the stash strip (which sits below _bounds, not inside it --
    // see CalculateLayout) so hovering it also suppresses aim/fire the same
    // way hovering the rest of the footer already does (UpdateGameRun).
    public bool Contains(Point point) =>
        _bounds.Contains(point) || _stashBounds.Contains(point) || _quickLootBounds.Contains(point);

    /// <summary>
    /// Extra vertical band CalculateLayout reserves below Bounds for the
    /// always-visible stash row -- shared with ReservedHeight so the world
    /// viewport always shrinks by exactly as much room as the stash row
    /// actually occupies, in both combat and the Mind (CalculateLayout is
    /// shared by Draw and DrawSoul; DrawSoul just doesn't populate Stash).
    /// </summary>
    private static int StashStripExtra(float scale) =>
        Math.Max(22, (int)(34 * scale)) + Math.Max(3, (int)(6 * scale));

    public static int ReservedHeight(int screenWidth, int screenHeight)
    {
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        bool compact = screenWidth < 900 || screenWidth < 1050 * scale;
        return Math.Min(screenHeight / 2,
            Math.Max((int)(compact ? 124 * scale : 91 * scale), (int)(66 * scale)) + StashStripExtra(scale));
    }

    public static Rectangle SafeArea(int screenWidth, int screenHeight)
    {
        int reserved = ReservedHeight(screenWidth, screenHeight);
        return new Rectangle(0, 0, screenWidth, Math.Max(1, screenHeight - reserved));
    }

    public static FooterLayout CalculateLayout(int screenWidth, int screenHeight, float scale)
    {
        bool compact = screenWidth < 900 || screenWidth < 1050 * scale;
        int margin = Math.Max(5, (int)MathF.Round(10 * scale));
        int width = Math.Max(1, Math.Min(screenWidth - margin * 2, (int)(1500 * scale)));
        int height = Math.Min(screenHeight / 2,
            Math.Max((int)((compact ? 116 : 84) * scale), (int)(62 * scale)));
        // The stash row sits below the main panel rather than inside it: the
        // panel's internal geometry (health/dash/equipment/resources/stats)
        // is tuned for its own bodyHeight, and folding an 8-slot row into
        // that would either shrink everything else or need every one of
        // those proportions re-tuned. Reserving a second band underneath
        // instead (bottom-anchored the same way `bounds` always was) keeps
        // all of that untouched and just makes the whole combined panel
        // taller -- see ReservedHeight/StashStripExtra for the matching
        // world-viewport shrink.
        int stashExtra = StashStripExtra(scale);
        var bounds = new Rectangle((screenWidth - width) / 2,
            screenHeight - height - margin - stashExtra, width, height);
        int pad = Math.Max(4, (int)(8 * scale));
        int xpHeight = Math.Max(10, (int)(17 * scale));
        var experience = new Rectangle(bounds.X + pad, bounds.Bottom - xpHeight - pad,
            bounds.Width - pad * 2, xpHeight);
        int bodyHeight = experience.Y - bounds.Y - pad * 2;

        Rectangle health;
        Rectangle dash;
        Rectangle equipment;
        Rectangle resources;
        Rectangle stats;
        if (!compact)
        {
            int bodyY = bounds.Y + pad;
            int healthWidth = (int)(bounds.Width * .22f);
            int equipmentWidth = (int)(bounds.Width * .31f);
            int resourcesWidth = (int)(bounds.Width * .10f);
            health = new Rectangle(bounds.X + pad, bodyY, healthWidth - pad, bodyHeight);
            dash = new Rectangle(health.Right - Math.Max(28, (int)(42 * scale)), bodyY,
                Math.Max(28, (int)(42 * scale)), bodyHeight);
            equipment = new Rectangle(health.Right + pad, bodyY, equipmentWidth - pad, bodyHeight);
            resources = new Rectangle(equipment.Right + pad, bodyY, resourcesWidth - pad, bodyHeight);
            stats = new Rectangle(resources.Right + pad, bodyY,
                bounds.Right - pad - (resources.Right + pad), bodyHeight);
        }
        else
        {
            int rowGap = Math.Max(3, (int)(5 * scale));
            int rowHeight = Math.Max(1, (bodyHeight - rowGap) / 2);
            int leftWidth = (int)(bounds.Width * .38f);
            health = new Rectangle(bounds.X + pad, bounds.Y + pad, leftWidth - pad, rowHeight);
            dash = new Rectangle(health.Right - Math.Max(25, (int)(36 * scale)), health.Y,
                Math.Max(25, (int)(36 * scale)), rowHeight);
            equipment = new Rectangle(health.Right + pad, health.Y,
                bounds.Right - pad - (health.Right + pad), rowHeight);
            resources = new Rectangle(bounds.X + pad, health.Bottom + rowGap,
                Math.Max(70, (int)(bounds.Width * .20f)), rowHeight);
            stats = new Rectangle(resources.Right + pad, resources.Y,
                bounds.Right - pad - (resources.Right + pad), rowHeight);
        }

        int slotGap = Math.Max(2, (int)(5 * scale));
        int slotSize = Math.Max(18, Math.Min(equipment.Height,
            (equipment.Width - slotGap * 4) / 5));
        int slotsWidth = slotSize * 5 + slotGap * 4;
        int slotsX = equipment.Center.X - slotsWidth / 2;
        int slotsY = equipment.Center.Y - slotSize / 2;
        var equipmentSlots = Enumerable.Range(0, 5)
            .Select(index => new Rectangle(slotsX + index * (slotSize + slotGap), slotsY, slotSize, slotSize))
            .ToArray();

        const int statColumns = 5;
        const int statRows = 2;
        int statGap = Math.Max(1, (int)(3 * scale));
        int statWidth = Math.Max(1, (stats.Width - statGap * (statColumns - 1)) / statColumns);
        int statHeight = Math.Max(1, (stats.Height - statGap * (statRows - 1)) / statRows);
        var statSlots = Enumerable.Range(0, statColumns * statRows)
            .Select(index => new Rectangle(
                stats.X + index % statColumns * (statWidth + statGap),
                stats.Y + index / statColumns * (statHeight + statGap),
                statWidth,
                statHeight))
            .ToArray();

        // Below Bounds, not inside it (see the doc comment on `stashExtra`
        // above) -- horizontally aligned under Equipment specifically, so it
        // visibly reads as "the player's stash, right below their gear"
        // rather than just another strip spanning the whole bar.
        int stashGap = Math.Max(3, (int)(6 * scale));
        int stashHeight = Math.Max(22, (int)(34 * scale));
        var stash = new Rectangle(equipment.X, bounds.Bottom + stashGap, equipment.Width, stashHeight);
        int stashSlotGap = Math.Max(2, (int)(4 * scale));
        int stashSlotSize = Math.Max(14, Math.Min(stash.Height,
            (stash.Width - stashSlotGap * (InformationSheet.InventorySlotCount - 1)) / InformationSheet.InventorySlotCount));
        int stashSlotsWidth = stashSlotSize * InformationSheet.InventorySlotCount
            + stashSlotGap * (InformationSheet.InventorySlotCount - 1);
        int stashSlotsX = stash.Center.X - stashSlotsWidth / 2;
        var stashSlots = Enumerable.Range(0, InformationSheet.InventorySlotCount)
            .Select(index => new Rectangle(stashSlotsX + index * (stashSlotSize + stashSlotGap),
                stash.Center.Y - stashSlotSize / 2, stashSlotSize, stashSlotSize))
            .ToArray();

        return new FooterLayout
        {
            Bounds = bounds,
            Health = health,
            Dash = dash,
            Equipment = equipment,
            Resources = resources,
            Stats = stats,
            Experience = experience,
            Stash = stash,
            EquipmentSlots = equipmentSlots,
            StatSlots = statSlots,
            StashSlots = stashSlots,
            Compact = compact,
        };
    }

    public static QuickLootLayout CalculateQuickLootLayout(FooterLayout footer, float scale,
        int lootSlotCount = InformationSheet.CrateSlotCount)
    {
        lootSlotCount = Math.Clamp(lootSlotCount, 1, InformationSheet.CrateSlotCount);
        int gap = Math.Max(2, (int)MathF.Round(4 * scale));
        // Loot-only now: the stash used to share this strip as a read-only
        // "TAB ONLY" preview, but it's a real, always-visible, always-live
        // panel of its own below Equipment now (see DrawStash), so showing
        // it a second time here would just be a stale duplicate.
        int pad = Math.Max(3, (int)MathF.Round(6 * scale));
        int lootLabelWidth = Math.Max(28, (int)MathF.Round((footer.Compact ? 42 : 66) * scale));
        int availableForSlots = footer.Bounds.Width - pad * 2 - lootLabelWidth
            - gap * (lootSlotCount + 1);
        int slotSize = Math.Clamp(availableForSlots / Math.Max(1, lootSlotCount),
            Math.Max(16, (int)(22 * scale)), Math.Max(22, (int)(44 * scale)));
        int contentWidth = lootLabelWidth + slotSize * lootSlotCount
            + gap * (lootSlotCount + 1);
        int height = slotSize + pad * 2;
        int x = footer.Bounds.Center.X - contentWidth / 2;
        int y = footer.Bounds.Y - height - gap;
        var bounds = new Rectangle(x, y, contentWidth, height);
        int cursor = bounds.X + pad;
        var lootLabel = new Rectangle(cursor, bounds.Y, lootLabelWidth - pad, bounds.Height);
        cursor += lootLabelWidth;
        var lootSlots = new List<Rectangle>(lootSlotCount);
        for (int index = 0; index < lootSlotCount; index++)
        {
            lootSlots.Add(new Rectangle(cursor, bounds.Y + pad, slotSize, slotSize));
            cursor += slotSize + gap;
        }
        return new QuickLootLayout
        {
            Bounds = bounds,
            LootLabel = lootLabel,
            LootSlots = lootSlots,
        };
    }

    public void Draw(SpriteBatch spriteBatch, RunState state, Point mousePosition, PathRun? pathRun = null,
        bool preferControllerPrompts = false, ItemDrop? draggedItem = null)
    {
        _playerLevelCap = pathRun is null
            ? Progression.MaxLevel
            : Progression.DungeonMaxLevel;
        float scale = UiTheme.DisplayScale(spriteBatch);
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        FooterLayout layout = CalculateLayout(viewport.Width, viewport.Height, scale);
        _bounds = layout.Bounds;
        _stashBounds = layout.Stash;
        _equipmentHit = layout.Equipment;
        _experienceHit = layout.Experience;
        _quickLootBounds = Rectangle.Empty;
        _quickLootSlotRects.Clear();
        _tooltipItem = null;

        float chromeTime = (float)(state.RunTimeSeconds * Math.Clamp(
            GameProfile.Profile.VisualEffectsIntensity, 0, 1));
        UiTheme.DrawFramedPanel(spriteBatch, layout.Bounds,
            UiTheme.Void * .96f, UiTheme.Cream, shadow: 7);

        DrawHealth(spriteBatch, layout, state, scale);
        DrawEquipment(spriteBatch, layout, state, mousePosition, scale);
        DrawResources(spriteBatch, layout, state, scale);
        DrawStats(spriteBatch, layout, state, scale);
        DrawExperience(spriteBatch, layout, state, scale, _playerLevelCap);
        DrawStash(spriteBatch, layout, state, mousePosition, scale);

        if (state.NearbyCrate is { Items.Count: > 0 })
            DrawQuickLoot(spriteBatch, layout, state, mousePosition, scale, chromeTime,
                preferControllerPrompts);

        if (draggedItem is not null)
        {
            int dragSize = Math.Max(30, (int)(48 * scale));
            var dragRect = new Rectangle(mousePosition.X - dragSize / 2,
                mousePosition.Y - dragSize / 2, dragSize, dragSize);
            ItemCards.DrawItemCard(spriteBatch, dragRect, draggedItem, hovered: true,
                (float)state.RunTimeSeconds);
        }

        if (_tooltipItem is not null && draggedItem is null)
            DrawTooltip(spriteBatch, _tooltipItem, mousePosition, scale, viewport.Bounds);
    }

    public void DrawSoul(SpriteBatch spriteBatch, RunState state, Point mousePosition, float time)
    {
        _playerLevelCap = Progression.MaxLevel;
        float scale = UiTheme.DisplayScale(spriteBatch);
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        FooterLayout layout = CalculateLayout(viewport.Width, viewport.Height, scale);
        _bounds = layout.Bounds;
        // The Mind keeps its own, better stash+equipment merge at the
        // Storage station (SoulHub/DrawSoulLoadoutPanel) -- this footer
        // doesn't duplicate a stash strip, so there's nothing live below it.
        _stashBounds = Rectangle.Empty;
        _stashSlotRects.Clear();
        _equipmentHit = layout.Equipment;
        _experienceHit = Rectangle.Empty;
        _quickLootBounds = Rectangle.Empty;
        _quickLootSlotRects.Clear();
        _tooltipItem = null;
        UiTheme.DrawFramedPanel(spriteBatch, layout.Bounds,
            UiTheme.Void * .96f, UiTheme.Cream, shadow: 7);
        DrawEquipment(spriteBatch, layout, state, mousePosition, scale);

        // The hub reuses the exact same Health/Dash/Resources/Stats geometry
        // combat uses (see CalculateLayout), but combat's own DrawHealth/
        // DrawStats have nothing to show here -- no active run stats exist
        // outside a run. Filling every reserved region with hub-relevant
        // cards (instead of a couple of text lines dropped in corners)
        // is what keeps the bar reading as a deliberate hub layout rather
        // than a combat layout with holes in it.
        DrawMindStatusCard(spriteBatch, layout, scale);
        DrawStats(spriteBatch, layout, state, scale, StatDisplay.HubDefinitions);
        UiTheme.DrawText(spriteBatch, "VISIT THE VAULT TO MANAGE CARRIED RELICS", 8 * scale, UiTheme.Muted,
            layout.Experience.Center.ToVector2(), "center");
        if (_tooltipItem is not null)
            DrawTooltip(spriteBatch, _tooltipItem, mousePosition, scale, viewport.Bounds);
    }

    public FooterAction HandleInput(RunState state, Point mousePosition, bool mousePressed)
    {
        if (!mousePressed)
            return FooterAction.None;
        bool canLevel = state.PendingLevelUps > 0
            || state.CurrentLevel < _playerLevelCap
                && state.ExpCount >= state.ExpNeededForNextLevel;
        if (canLevel && _experienceHit.Contains(mousePosition))
            return FooterAction.OpenLevelUp;
        // A press directly on an equipped item now starts a drag instead
        // (InformationSheet.HandleLiveLootDrag, called before this every
        // frame -- see GameSession.HandleQuickLootInput) -- opening the Tab
        // menu out from under that same press would strand the drag mid-air
        // on a screen that no longer has anywhere to drop it. Clicking
        // anywhere else on the bar (background, health, stats, an empty
        // gear slot) still opens it, same as before.
        bool overEquippedSlot = _equipmentSlotRects.Any(pair =>
            pair.Value.Contains(mousePosition) && state.Equipment.GetValueOrDefault(pair.Key) is not null);
        if (!overEquippedSlot && _bounds.Contains(mousePosition))
            return FooterAction.OpenDossier;
        return FooterAction.None;
    }

    private void DrawHealth(SpriteBatch spriteBatch, FooterLayout layout, RunState state, float scale)
    {
        int dashWidth = Math.Max(25, layout.Dash.Width);
        int barWidth = Math.Max(20, layout.Health.Width - dashWidth - (int)(5 * scale));
        var bar = new Rectangle(layout.Health.X, layout.Health.Bottom - Math.Max(9, (int)(13 * scale)),
            barWidth, Math.Max(7, (int)(10 * scale)));
        if (state.VoidMode)
        {
            UiTheme.DrawText(spriteBatch, "THE VOID  //  ONE HIT", 7 * scale,
                UiTheme.Muted, new Vector2(layout.Health.X, layout.Health.Y + 1));
        }
        else if (state.GoldenFlameMode)
        {
            UiTheme.DrawText(spriteBatch, $"HITS  {state.GoldenFlameHitsRemaining}/3", 7 * scale,
                UiTheme.Text, new Vector2(layout.Health.X, layout.Health.Y + 1));
            UiTheme.DrawProgress(spriteBatch, bar, state.GoldenFlameHitsRemaining / 3f, UiTheme.Gold, 3);
        }
        else
        {
            Color health = state.HealthPoints > state.MaxHealthPoints * .3 ? UiTheme.Green : UiTheme.Red;
            UiTheme.DrawText(spriteBatch, $"HEALTH  {state.HealthPoints:N0}/{state.MaxHealthPoints:N0}", 7 * scale,
                UiTheme.Text, new Vector2(layout.Health.X, layout.Health.Y + 1));
            UiTheme.DrawProgress(spriteBatch, bar,
                (float)(state.HealthPoints / Math.Max(1.0, state.MaxHealthPoints)), health, 10);
        }
        double dashReady = Math.Clamp((state.DashCooldownMax - state.CurrDashCooldown)
            / Math.Max(1, state.DashCooldownMax), 0, 1);
        string dashLabel = state.CurrDashCooldown <= 0
            ? layout.Compact ? "RDY" : "DASH READY"
            : "DASH";
        UiTheme.DrawText(spriteBatch, dashLabel, 6.5 * scale,
            state.CurrDashCooldown <= 0 ? UiTheme.Blue : UiTheme.Muted,
            new Vector2(layout.Dash.Center.X, layout.Dash.Y + 1), "midtop");
        var dashRect = new Rectangle(layout.Dash.X + Math.Max(2, (int)(4 * scale)),
            layout.Dash.Bottom - Math.Max(9, (int)(13 * scale)),
            Math.Max(8, layout.Dash.Width - Math.Max(4, (int)(8 * scale))), Math.Max(7, (int)(10 * scale)));
        UiTheme.DrawProgress(spriteBatch, dashRect, (float)dashReady, UiTheme.Blue, 4);
    }

    /// <summary>
    /// Hub-only replacement for DrawHealth: there's no health/dash to show
    /// outside a run, so this fills the same Health+Dash+Resources footprint
    /// with a single card of hub-relevant status (which campaign stage is
    /// open, and which optional braziers are currently lit) instead of
    /// leaving that whole left side of the bar blank.
    /// </summary>
    private static void DrawMindStatusCard(SpriteBatch spriteBatch, FooterLayout layout, float scale)
    {
        var rect = Rectangle.Union(Rectangle.Union(layout.Health, layout.Dash), layout.Resources);
        int radius = UiTheme.SmallCornerRadius(scale);
        Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink, radius);
        Primitives2D.RoundedRectOutline(spriteBatch, rect, UiTheme.Border * .72f, Math.Max(1, (int)scale), radius);

        int pad = Math.Max(3, (int)(6 * scale));
        string campaignLabel = CampaignProgression.Data.SoulUnlocked ? "THE SOUL"
            : CampaignProgression.Data.BodyUnlocked ? "THE BODY"
            : "THE BODY  //  LOCKED";
        UiTheme.DrawText(spriteBatch, "CAMPAIGN", 6.5 * scale, UiTheme.Muted,
            new Vector2(rect.X + pad, rect.Y + Math.Max(1, 2 * scale)));
        UiTheme.DrawText(spriteBatch, campaignLabel, 11 * scale, UiTheme.Text,
            new Vector2(rect.X + pad, rect.Y + Math.Max(1, 2 * scale) + 8 * scale));

        var flags = new List<(string Label, bool Active, Color Color)>
        {
            ("HARD", GameProfile.Profile.HardModeEnabled, UiTheme.Red),
            ("NO-EXTRACT", GameProfile.Profile.NoExtractEnabled, UiTheme.Gold),
            ("GOLDEN", GameProfile.Profile.GoldenFlameEnabled, UiTheme.Gold),
            ("VOID", GameProfile.Profile.VoidEnabled, UiTheme.Purple),
        };
        string flagLine = string.Join("   ", flags.Where(f => f.Active).Select(f => f.Label));
        UiTheme.DrawText(spriteBatch,
            flagLine.Length > 0 ? $"LIT  {flagLine}" : "NO BRAZIERS LIT",
            6.5 * scale, flags.Any(f => f.Active) ? UiTheme.Gold : UiTheme.Muted,
            new Vector2(rect.X + pad, rect.Bottom - Math.Max(3, (int)(6 * scale))), "bottomleft");
    }

    private void DrawEquipment(SpriteBatch spriteBatch, FooterLayout layout, RunState state,
        Point mousePosition, float scale)
    {
        _equipmentSlotRects.Clear();
        for (int index = 0; index < EquipmentOrder.Length; index++)
        {
            string key = EquipmentOrder[index];
            Rectangle rect = layout.EquipmentSlots[index];
            _equipmentSlotRects[key] = rect;
            ItemDrop? item = state.Equipment.GetValueOrDefault(key);
            bool hovered = rect.Contains(mousePosition);
            if (item is not null)
            {
                ItemCards.DrawItemCard(spriteBatch, rect, item, hovered, (float)state.RunTimeSeconds);
                if (hovered)
                    _tooltipItem = item;
            }
            else
            {
                Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink, UiTheme.SmallCornerRadius(scale));
                Primitives2D.RoundedRectOutline(spriteBatch, rect,
                    hovered ? UiTheme.Cream : UiTheme.Border, Math.Max(1, (int)(2 * scale)), UiTheme.SmallCornerRadius(scale));
                UiTheme.DrawText(spriteBatch, EquipmentLabels[index], 7 * scale, UiTheme.Muted,
                    rect.Center.ToVector2(), "center");
            }
        }
    }

    /// <summary>
    /// The carried stash, always visible directly below Equipment and always
    /// live -- draggable to/from equipment any time during a run (see
    /// InformationSheet.HandleLiveLootDrag, which no longer requires a
    /// nearby crate), or swapped with the 1-8 keys (RotBoiGame.UpdateGameRun,
    /// GameSession.SwapStashSlotWithEquipment). Replaces the old
    /// paused-Dossier-only stash grid and the read-only "TAB ONLY" quick-loot
    /// preview -- both are gone now that this is the one live stash panel.
    /// </summary>
    private void DrawStash(SpriteBatch spriteBatch, FooterLayout layout, RunState state,
        Point mousePosition, float scale)
    {
        _stashSlotRects.Clear();
        int radius = UiTheme.SmallCornerRadius(scale);
        Primitives2D.FillRoundedRect(spriteBatch, layout.Stash, UiTheme.Void * .9f, radius);
        Primitives2D.RoundedRectOutline(spriteBatch, layout.Stash, UiTheme.Border, Math.Max(1, (int)scale), radius);
        for (int index = 0; index < layout.StashSlots.Count; index++)
        {
            Rectangle rect = layout.StashSlots[index];
            _stashSlotRects.Add(rect);
            ItemDrop? item = state.Inventory[index];
            bool hovered = rect.Contains(mousePosition);
            if (item is not null)
            {
                ItemCards.DrawItemCard(spriteBatch, rect, item, hovered, (float)state.RunTimeSeconds);
                if (hovered)
                    _tooltipItem = item;
            }
            else
            {
                Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink, UiTheme.SmallCornerRadius(scale));
                Primitives2D.RoundedRectOutline(spriteBatch, rect,
                    hovered ? UiTheme.Purple : UiTheme.Border * .7f, Math.Max(1, (int)(1.5f * scale)),
                    UiTheme.SmallCornerRadius(scale));
            }
            // Matches the "Stash: Swap Slot N" keybind badge the old Dossier
            // stash grid used to draw -- same purpose, new home.
            if (Keybinds.KeyFor($"stash_swap_{index + 1}") is not null)
            {
                int badgeSize = Math.Max(9, (int)(11 * scale));
                var badge = new Rectangle(rect.X + 1, rect.Y + 1, badgeSize, badgeSize);
                Primitives2D.FillRect(spriteBatch, badge, UiTheme.Shadow * .78f);
                UiTheme.DrawText(spriteBatch, index + 1, rect.Width * .3, UiTheme.Muted,
                    badge.Center.ToVector2(), "center");
            }
        }
    }

    private void DrawQuickLoot(SpriteBatch spriteBatch, FooterLayout footer, RunState state,
        Point mousePosition, float scale, float chromeTime, bool preferControllerPrompts)
    {
        int visibleLootCount = Math.Min(state.NearbyCrate!.Items.Count,
            InformationSheet.CrateSlotCount);
        QuickLootLayout layout = CalculateQuickLootLayout(footer, scale, visibleLootCount);
        _quickLootBounds = layout.Bounds;
        _quickLootSlotRects.AddRange(layout.LootSlots);
        int lootCount = layout.LootSlots.Count;
        _quickLootSelection = Math.Clamp(_quickLootSelection, 0, Math.Max(0, lootCount - 1));

        UiTheme.DrawFramedPanel(spriteBatch, layout.Bounds,
            UiTheme.PanelRaised * .98f, UiTheme.Gold, shadow: 4);
        UiTheme.DrawText(spriteBatch, "LOOT", 6.5 * scale, UiTheme.Gold,
            layout.LootLabel.Center.ToVector2(), "center", bold: true);

        string? targetKey = null;
        for (int index = 0; index < layout.LootSlots.Count; index++)
        {
            Rectangle rect = layout.LootSlots[index];
            bool selected = preferControllerPrompts && index == _quickLootSelection;
            bool hovered = rect.Contains(mousePosition);
            if (index < lootCount)
            {
                ItemDrop item = state.NearbyCrate.Items[index];
                ItemCards.DrawItemCard(spriteBatch, rect, item, hovered, (float)state.RunTimeSeconds);
                if (hovered)
                    _tooltipItem = item;
                if (selected)
                    targetKey = EquipmentTargetFor(item, state, _preferredAccessorySlot);
            }
            else
            {
                Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink,
                    UiTheme.SmallCornerRadius(scale));
                Primitives2D.RoundedRectOutline(spriteBatch, rect, UiTheme.Border * .6f,
                    1, UiTheme.SmallCornerRadius(scale));
            }
            if (selected)
                Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream,
                    Math.Max(1, (int)(2 * scale)));
        }

        if (targetKey is not null && _equipmentSlotRects.TryGetValue(targetKey, out Rectangle targetRect))
            Primitives2D.RectOutline(spriteBatch, targetRect, UiTheme.Gold,
                Math.Max(1, (int)(2 * scale)));

        if (preferControllerPrompts)
        {
            string prompt = lootCount > 0 ? "D-PAD SELECT  //  A SWAP" : "";
            UiTheme.DrawText(spriteBatch, prompt, 5.5 * scale, UiTheme.Cream,
                new Vector2(layout.Bounds.Center.X, layout.Bounds.Y - Math.Max(3, 4 * scale)), "midbottom");
        }
    }

    public QuickLootCommand? HandleQuickLootController(RunState state)
    {
        if (state.NearbyCrate is not { Items.Count: > 0 } crate)
        {
            _quickLootSelection = 0;
            return null;
        }
        int count = Math.Min(crate.Items.Count, InformationSheet.CrateSlotCount);
        if (InputState.ControllerDpadLeftPressed)
            _quickLootSelection = (_quickLootSelection - 1 + count) % count;
        if (InputState.ControllerDpadRightPressed)
            _quickLootSelection = (_quickLootSelection + 1) % count;
        _quickLootSelection = Math.Clamp(_quickLootSelection, 0, count - 1);
        ItemDrop selected = crate.Items[_quickLootSelection];
        if (selected.SlotType == "accessory"
            && (InputState.ControllerDpadUpPressed || InputState.ControllerDpadDownPressed))
            _preferredAccessorySlot = 1 - _preferredAccessorySlot;
        if (!InputState.ControllerConfirmPressed)
            return null;
        return new QuickLootCommand(_quickLootSelection,
            EquipmentTargetFor(selected, state, _preferredAccessorySlot));
    }

    public static string EquipmentTargetFor(ItemDrop item, RunState state, int preferredAccessorySlot = 0)
    {
        if (item.SlotType != "accessory")
            return item.SlotType;
        if (state.Equipment["accessory_1"] is null)
            return "accessory_1";
        if (state.Equipment["accessory_2"] is null)
            return "accessory_2";
        return preferredAccessorySlot == 0 ? "accessory_1" : "accessory_2";
    }

    private static void DrawResources(SpriteBatch spriteBatch, FooterLayout layout, RunState state, float scale)
    {
        UiTheme.DrawText(spriteBatch, "FRAGMENTS", 6.5 * scale, UiTheme.Muted,
            new Vector2(layout.Resources.Center.X, layout.Resources.Y + 1), "midtop");
        UiTheme.DrawText(spriteBatch, state.Fragments.ToString("N0"), 14 * scale, UiTheme.Purple,
            new Vector2(layout.Resources.Center.X, layout.Resources.Center.Y + 4 * scale), "center");
    }

    private static void DrawStats(SpriteBatch spriteBatch, FooterLayout layout, RunState state, float scale) =>
        DrawStats(spriteBatch, layout, state, scale, StatDisplay.Definitions);

    private static void DrawStats(SpriteBatch spriteBatch, FooterLayout layout, RunState state, float scale,
        IReadOnlyList<StatDisplayDefinition> statDefinitions)
    {
        IReadOnlyList<StatDisplayDefinition> definitions = statDefinitions.Take(10).ToList();
        for (int index = 0; index < definitions.Count; index++)
        {
            StatDisplayDefinition definition = definitions[index];
            Rectangle rect = layout.StatSlots[index];
            int radius = UiTheme.SmallCornerRadius(scale);
            Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink, radius);
            Primitives2D.RoundedRectOutline(spriteBatch, rect, UiTheme.Border * .72f,
                Math.Max(1, (int)scale), radius);

            bool showIcon = !layout.Compact && rect.Width >= 72 * scale && rect.Height >= 28 * scale;
            int textLeft = rect.X + Math.Max(3, (int)(5 * scale));
            if (showIcon)
            {
                int iconSize = Math.Min(rect.Height - Math.Max(4, (int)(6 * scale)),
                    Math.Max(12, (int)(20 * scale)));
                var icon = new Rectangle(rect.X + Math.Max(3, (int)(4 * scale)),
                    rect.Center.Y - iconSize / 2, iconSize, iconSize);
                StatCards.DrawStatSymbol(spriteBatch, definition.IconKey, icon, UiTheme.Cream);
                textLeft = icon.Right + Math.Max(2, (int)(4 * scale));
            }

            double labelSize = (layout.Compact ? 5.2 : 5.7) * scale;
            double valueSize = (layout.Compact ? 7.2 : 8.2) * scale;
            UiTheme.DrawText(spriteBatch, definition.Abbreviation, labelSize, UiTheme.Muted,
                new Vector2(textLeft, rect.Y + Math.Max(1, 2 * scale)));
            UiTheme.DrawText(spriteBatch, definition.Format(state), valueSize, UiTheme.Text,
                new Vector2(rect.Right - Math.Max(3, (int)(5 * scale)), rect.Center.Y), "midright");
        }
    }

    private static void DrawExperience(SpriteBatch spriteBatch, FooterLayout layout,
        RunState state, float scale, int playerLevelCap)
    {
        bool queued = state.PendingLevelUps > 0;
        bool maximum = state.CurrentLevel >= playerLevelCap && !queued;
        bool ready = queued
            || !maximum && state.ExpCount >= state.ExpNeededForNextLevel;
        float ratio = maximum || queued
            ? 1f
            : (float)(state.ExpCount / Math.Max(1, state.ExpNeededForNextLevel));
        Color color = ready ? UiTheme.Gold : UiTheme.Purple;
        UiTheme.DrawProgress(spriteBatch, layout.Experience, ratio, color, 16);
        if (ready)
        {
            float pulse = .55f + .45f * MathF.Sin((float)state.RunTimeSeconds * 5f);
            var glow = layout.Experience;
            glow.Inflate(Math.Max(1, (int)(2 * scale)),
                Math.Max(1, (int)(2 * scale)));
            Primitives2D.RectOutline(spriteBatch, glow,
                Color.Lerp(UiTheme.Gold, UiTheme.Cream, pulse),
                Math.Max(1, (int)(2 * scale)));
        }
        string text = queued
            ? $"LEVEL {state.CurrentLevel:D2}  //  {state.PendingLevelUps} DRAFTS QUEUED"
            : maximum
            ? $"LEVEL {state.CurrentLevel:D2}  //  MAXIMUM"
            : ready
                ? $"LEVEL {state.CurrentLevel:D2}  //  LEVEL READY"
                : $"LEVEL {state.CurrentLevel:D2}  //  {state.ExpCount:0}/{state.ExpNeededForNextLevel:0} XP";
        UiTheme.DrawText(spriteBatch, text, 7 * scale, ready ? UiTheme.Ink : UiTheme.Text,
            layout.Experience.Center.ToVector2(), "center", bold: ready);
    }

    /// <summary>
    /// Sized to its actual wrapped description (see <see cref="UiTheme.WrapLines"/>)
    /// rather than a fixed height -- a long unique's description used to run
    /// past the panel's own border instead of wrapping. Anchored above the
    /// cursor by default (hotbar/equipment slots live at the bottom of the
    /// screen), and <see cref="UiTheme.ClampTooltipRect"/> then pushes it
    /// further up -- or, if it still doesn't fully fit, as far up as
    /// `bounds` allows -- so the whole card, including every description
    /// line, always stays on screen instead of clipping off the top.
    /// </summary>
    private static void DrawTooltip(SpriteBatch spriteBatch, ItemDrop item, Point mouse, float scale, Rectangle bounds)
    {
        int width = Math.Min((int)(300 * scale), bounds.Width - 12);
        float padding = 10 * scale;
        float descriptionTop = 48 * scale;
        float descriptionLineHeight = 12 * scale;
        var descriptionLines = UiTheme.WrapLines(item.Definition.Description, 7 * scale, width - padding * 2);
        int ladderRowSize = (int)(7 * scale);
        int ladderHeight = ItemCards.MeasureModifierLadder(ladderRowSize, item);
        int height = Math.Max((int)(58 * scale),
            (int)(descriptionTop + descriptionLines.Count * descriptionLineHeight + 8 * scale)
                + ladderHeight + (ladderHeight > 0 ? (int)(10 * scale) : 0));
        int x = mouse.X - width / 2;
        int y = mouse.Y - height - (int)(14 * scale);
        var rect = UiTheme.ClampTooltipRect(new Rectangle(x, y, width, height), bounds, 6);
        Color rarity = UiTheme.RarityColors.GetValueOrDefault(item.Rarity, UiTheme.Border);
        UiTheme.DrawFramedPanel(spriteBatch, rect, UiTheme.PanelRaised, rarity, shadow: 5);
        UiTheme.DrawText(spriteBatch, item.DisplayName.ToUpperInvariant(), 10 * scale, UiTheme.Text,
            new Vector2(rect.X + padding, rect.Y + 9 * scale));
        UiTheme.DrawText(spriteBatch,
            $"{item.Rarity.ToUpperInvariant()}  //  {Items.ModifierUnlockCount(item.Rarity)}/{item.Definition.ModifierLadder.Count} MODIFIERS",
            7 * scale, rarity, new Vector2(rect.X + padding, rect.Y + 29 * scale));
        float descriptionBottom = rect.Y + descriptionTop;
        for (int index = 0; index < descriptionLines.Count; index++)
        {
            UiTheme.DrawText(spriteBatch, descriptionLines[index], 7 * scale, UiTheme.Muted,
                new Vector2(rect.X + padding, descriptionBottom + index * descriptionLineHeight));
        }
        descriptionBottom += descriptionLines.Count * descriptionLineHeight;
        if (ladderHeight > 0)
            ItemCards.DrawModifierLadder(spriteBatch, new Vector2(rect.X + padding, descriptionBottom + 10 * scale), ladderRowSize, item);
    }
}
