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
    public required IReadOnlyList<Rectangle> EquipmentSlots { get; init; }
    public required IReadOnlyList<Rectangle> StatSlots { get; init; }
    public bool Compact { get; init; }
}

public sealed class QuickLootLayout
{
    public required Rectangle Bounds { get; init; }
    public required Rectangle LootLabel { get; init; }
    public required Rectangle StashLabel { get; init; }
    public required IReadOnlyList<Rectangle> LootSlots { get; init; }
    public required IReadOnlyList<Rectangle> StashSlots { get; init; }
}

/// <summary>
/// Compact combat HUD. Equipped and stashed items remain read-only during combat;
/// only nearby world loot can be moved through the transient quick-loot strip.
/// </summary>
public sealed class FooterHud
{
    private int _playerLevelCap = Progression.MaxLevel;
    private static readonly string[] EquipmentOrder =
        ["weapon", "armor", "ring", "accessory_1", "accessory_2"];
    private static readonly string[] EquipmentLabels = ["W", "A", "R", "1", "2"];

    private Rectangle _bounds;
    private Rectangle _quickLootBounds;
    private Rectangle _equipmentHit;
    private Rectangle _experienceHit;
    private ItemDrop? _tooltipItem;
    private readonly Dictionary<string, Rectangle> _equipmentSlotRects = new();
    private readonly List<Rectangle> _quickLootSlotRects = new(InformationSheet.CrateSlotCount);
    private readonly List<Rectangle> _quickStashSlotRects = new(InformationSheet.InventorySlotCount);
    private int _quickLootSelection;
    private int _preferredAccessorySlot;

    public Rectangle Bounds => _bounds;
    public IReadOnlyDictionary<string, Rectangle> EquipmentSlotRects => _equipmentSlotRects;
    public IReadOnlyList<Rectangle> QuickLootSlotRects => _quickLootSlotRects;
    public IReadOnlyList<Rectangle> QuickStashSlotRects => _quickStashSlotRects;
    public bool Contains(Point point) => _bounds.Contains(point) || _quickLootBounds.Contains(point);

    public static int ReservedHeight(int screenWidth, int screenHeight)
    {
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        bool compact = screenWidth < 900 || screenWidth < 1050 * scale;
        return Math.Min(screenHeight / 2,
            Math.Max((int)(compact ? 124 * scale : 91 * scale), (int)(66 * scale)));
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
        var bounds = new Rectangle((screenWidth - width) / 2, screenHeight - height - margin, width, height);
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

        int statGap = Math.Max(2, (int)(4 * scale));
        int statWidth = Math.Max(1, (stats.Width - statGap * 2) / 3);
        var statSlots = Enumerable.Range(0, 3)
            .Select(index => new Rectangle(stats.X + index * (statWidth + statGap), stats.Y, statWidth, stats.Height))
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
            EquipmentSlots = equipmentSlots,
            StatSlots = statSlots,
            Compact = compact,
        };
    }

    public static QuickLootLayout CalculateQuickLootLayout(FooterLayout footer, float scale,
        int lootSlotCount = InformationSheet.CrateSlotCount)
    {
        lootSlotCount = Math.Clamp(lootSlotCount, 1, InformationSheet.CrateSlotCount);
        int gap = Math.Max(2, (int)MathF.Round(4 * scale));
        int pad = Math.Max(3, (int)MathF.Round(6 * scale));
        int lootLabelWidth = Math.Max(28, (int)MathF.Round((footer.Compact ? 42 : 66) * scale));
        int stashLabelWidth = Math.Max(34, (int)MathF.Round((footer.Compact ? 52 : 86) * scale));
        int slotCount = lootSlotCount + InformationSheet.InventorySlotCount;
        int availableForSlots = footer.Bounds.Width - pad * 2 - lootLabelWidth - stashLabelWidth
            - gap * (slotCount + 1);
        int slotSize = Math.Clamp(availableForSlots / slotCount,
            Math.Max(16, (int)(22 * scale)), Math.Max(22, (int)(44 * scale)));
        int contentWidth = lootLabelWidth + stashLabelWidth + slotSize * slotCount
            + gap * (slotCount + 1);
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
        var stashLabel = new Rectangle(cursor, bounds.Y, stashLabelWidth - gap, bounds.Height);
        cursor += stashLabelWidth;
        var stashSlots = new List<Rectangle>(InformationSheet.InventorySlotCount);
        for (int index = 0; index < InformationSheet.InventorySlotCount; index++)
        {
            stashSlots.Add(new Rectangle(cursor, bounds.Y + pad, slotSize, slotSize));
            cursor += slotSize + gap;
        }
        return new QuickLootLayout
        {
            Bounds = bounds,
            LootLabel = lootLabel,
            StashLabel = stashLabel,
            LootSlots = lootSlots,
            StashSlots = stashSlots,
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
        _equipmentHit = layout.Equipment;
        _experienceHit = layout.Experience;
        _quickLootBounds = Rectangle.Empty;
        _quickLootSlotRects.Clear();
        _quickStashSlotRects.Clear();
        _tooltipItem = null;

        float chromeTime = (float)(state.RunTimeSeconds * Math.Clamp(
            GameProfile.Profile.VisualEffectsIntensity, 0, 1));
        UiTheme.DrawCompositePanel(spriteBatch, layout.Bounds, chromeTime,
            UiTheme.Void * .96f, UiTheme.Cream, shadow: 7);

        DrawHealth(spriteBatch, layout, state, scale);
        DrawEquipment(spriteBatch, layout, state, mousePosition, scale);
        DrawResources(spriteBatch, layout, state, scale);
        DrawStats(spriteBatch, layout, state, scale);
        DrawExperience(spriteBatch, layout, state, scale, _playerLevelCap);

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
        _equipmentHit = layout.Equipment;
        _experienceHit = Rectangle.Empty;
        _quickLootBounds = Rectangle.Empty;
        _quickLootSlotRects.Clear();
        _quickStashSlotRects.Clear();
        _tooltipItem = null;
        UiTheme.DrawCompositePanel(spriteBatch, layout.Bounds, time,
            UiTheme.Void * .96f, UiTheme.Cream, shadow: 7);
        DrawEquipment(spriteBatch, layout, state, mousePosition, scale);

        int stashCount = state.Inventory.Count(item => item is not null);
        UiTheme.DrawText(spriteBatch, "CARRIED STASH", 7 * scale, UiTheme.Muted,
            new Vector2(layout.Health.X, layout.Health.Y + 2 * scale));
        UiTheme.DrawText(spriteBatch, $"{stashCount}/{InformationSheet.InventorySlotCount}", 15 * scale, UiTheme.Text,
            new Vector2(layout.Health.X, layout.Health.Center.Y + 2 * scale), "midleft");
        UiTheme.DrawText(spriteBatch, $"SOUL TOKENS  {GameProfile.Profile.SoulTokens:N0}", 8 * scale, UiTheme.Purple,
            new Vector2(layout.Resources.X, layout.Resources.Center.Y), "midleft");
        UiTheme.DrawText(spriteBatch,
            $"VAULT  {GameProfile.Profile.Storage.Count}/{MetaProgression.StorageCapacity}", 8 * scale, UiTheme.Gold,
            new Vector2(layout.Stats.Right, layout.Stats.Center.Y), "midright");
        UiTheme.DrawText(spriteBatch, "VISIT THE VAULT TO MANAGE CARRIED RELICS", 8 * scale, UiTheme.Muted,
            layout.Experience.Center.ToVector2(), "center");
        if (_tooltipItem is not null)
            DrawTooltip(spriteBatch, _tooltipItem, mousePosition, scale, viewport.Bounds);
    }

    public FooterAction HandleInput(RunState state, Point mousePosition, bool mousePressed)
    {
        if (!mousePressed)
            return FooterAction.None;
        bool canLevel = state.CurrentLevel < _playerLevelCap
            && state.ExpCount >= state.ExpNeededForNextLevel;
        if (canLevel && _experienceHit.Contains(mousePosition))
            return FooterAction.OpenLevelUp;
        if (_equipmentHit.Contains(mousePosition) || _bounds.Contains(mousePosition))
            return FooterAction.OpenDossier;
        return FooterAction.None;
    }

    private void DrawHealth(SpriteBatch spriteBatch, FooterLayout layout, RunState state, float scale)
    {
        Color health = state.HealthPoints > state.MaxHealthPoints * .3 ? UiTheme.Green : UiTheme.Red;
        int dashWidth = Math.Max(25, layout.Dash.Width);
        int barWidth = Math.Max(20, layout.Health.Width - dashWidth - (int)(5 * scale));
        UiTheme.DrawText(spriteBatch, $"HEALTH  {state.HealthPoints:N0}/{state.MaxHealthPoints:N0}", 7 * scale,
            UiTheme.Text, new Vector2(layout.Health.X, layout.Health.Y + 1));
        var bar = new Rectangle(layout.Health.X, layout.Health.Bottom - Math.Max(9, (int)(13 * scale)),
            barWidth, Math.Max(7, (int)(10 * scale)));
        UiTheme.DrawProgress(spriteBatch, bar,
            (float)(state.HealthPoints / Math.Max(1.0, state.MaxHealthPoints)), health, 10);
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
                Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink, Math.Max(2, (int)(4 * scale)));
                Primitives2D.RoundedRectOutline(spriteBatch, rect,
                    hovered ? UiTheme.Cream : UiTheme.Border, Math.Max(1, (int)(2 * scale)), Math.Max(2, (int)(4 * scale)));
                UiTheme.DrawText(spriteBatch, EquipmentLabels[index], 7 * scale, UiTheme.Muted,
                    rect.Center.ToVector2(), "center");
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
        _quickStashSlotRects.AddRange(layout.StashSlots);
        int lootCount = layout.LootSlots.Count;
        _quickLootSelection = Math.Clamp(_quickLootSelection, 0, Math.Max(0, lootCount - 1));

        UiTheme.DrawCompositePanel(spriteBatch, layout.Bounds, chromeTime,
            UiTheme.PanelRaised * .98f, UiTheme.Gold, shadow: 4);
        UiTheme.DrawText(spriteBatch, "LOOT", 6.5 * scale, UiTheme.Gold,
            layout.LootLabel.Center.ToVector2(), "center", bold: true);
        float stashLabelY = footer.Compact
            ? layout.StashLabel.Center.Y
            : layout.StashLabel.Center.Y - 6 * scale;
        UiTheme.DrawText(spriteBatch, "STASH", footer.Compact ? 6 * scale : 5.5 * scale,
            UiTheme.Purple, new Vector2(layout.StashLabel.Center.X, stashLabelY), "center");
        if (!footer.Compact)
            UiTheme.DrawText(spriteBatch, "TAB ONLY", 4.75 * scale, UiTheme.Muted,
                new Vector2(layout.StashLabel.Center.X, layout.StashLabel.Center.Y + 7 * scale), "center");

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
                    Math.Max(2, (int)(3 * scale)));
                Primitives2D.RoundedRectOutline(spriteBatch, rect, UiTheme.Border * .6f,
                    1, Math.Max(2, (int)(3 * scale)));
            }
            if (selected)
                Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream,
                    Math.Max(1, (int)(2 * scale)));
        }

        for (int index = 0; index < layout.StashSlots.Count; index++)
        {
            Rectangle rect = layout.StashSlots[index];
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
                Primitives2D.FillRoundedRect(spriteBatch, rect, UiTheme.Ink,
                    Math.Max(2, (int)(3 * scale)));
                Primitives2D.RoundedRectOutline(spriteBatch, rect,
                    hovered ? UiTheme.Purple : UiTheme.Border, Math.Max(1, (int)(1.5f * scale)),
                    Math.Max(2, (int)(3 * scale)));
                UiTheme.DrawText(spriteBatch, (index + 1).ToString(), 5.5 * scale, UiTheme.Muted,
                    rect.Center.ToVector2(), "center");
            }
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

    private static void DrawStats(SpriteBatch spriteBatch, FooterLayout layout, RunState state, float scale)
    {
        IReadOnlyList<string> selected = FooterStats.NormalizeSelection(GameProfile.Profile.FooterStats);
        for (int index = 0; index < FooterStats.SelectionCount; index++)
        {
            FooterStatDefinition definition = FooterStats.ById[selected[index]];
            Rectangle rect = layout.StatSlots[index];
            UiTheme.DrawText(spriteBatch, layout.Compact ? definition.ShortLabel : definition.Label,
                6.5 * scale, UiTheme.Muted, new Vector2(rect.Center.X, rect.Y + 1), "midtop");
            UiTheme.DrawText(spriteBatch, definition.Value(state), 10 * scale, UiTheme.Text,
                new Vector2(rect.Center.X, rect.Center.Y + 4 * scale), "center");
        }
    }

    private static void DrawExperience(SpriteBatch spriteBatch, FooterLayout layout,
        RunState state, float scale, int playerLevelCap)
    {
        bool maximum = state.CurrentLevel >= playerLevelCap;
        bool ready = !maximum && state.ExpCount >= state.ExpNeededForNextLevel;
        float ratio = maximum ? 1f : (float)(state.ExpCount / Math.Max(1, state.ExpNeededForNextLevel));
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
        string text = maximum
            ? $"LEVEL {state.CurrentLevel:D2}  //  MAXIMUM"
            : ready
                ? $"LEVEL {state.CurrentLevel:D2}  //  LEVEL READY"
                : $"LEVEL {state.CurrentLevel:D2}  //  {state.ExpCount:0}/{state.ExpNeededForNextLevel:0} XP";
        UiTheme.DrawText(spriteBatch, text, 7 * scale, ready ? UiTheme.Ink : UiTheme.Text,
            layout.Experience.Center.ToVector2(), "center", bold: ready);
    }

    private static void DrawTooltip(SpriteBatch spriteBatch, ItemDrop item, Point mouse, float scale, Rectangle bounds)
    {
        int width = Math.Min((int)(300 * scale), bounds.Width - 12);
        int height = Math.Max(58, (int)(78 * scale));
        int x = Math.Clamp(mouse.X - width / 2, 6, Math.Max(6, bounds.Right - width - 6));
        int y = Math.Max(6, mouse.Y - height - (int)(14 * scale));
        var rect = new Rectangle(x, y, width, height);
        Color rarity = UiTheme.RarityColors.GetValueOrDefault(item.Rarity, UiTheme.Border);
        UiTheme.DrawCompositePanel(spriteBatch, rect, 0, UiTheme.PanelRaised, rarity, shadow: 5);
        UiTheme.DrawText(spriteBatch, item.DisplayName.ToUpperInvariant(), 10 * scale, UiTheme.Text,
            new Vector2(rect.X + 10 * scale, rect.Y + 9 * scale));
        UiTheme.DrawText(spriteBatch,
            $"{item.Rarity.ToUpperInvariant()}  //  GRADE {item.Grade}  //  {item.Modifier.ToUpperInvariant()}",
            7 * scale, rarity, new Vector2(rect.X + 10 * scale, rect.Y + 29 * scale));
        UiTheme.DrawText(spriteBatch, item.Definition.Description, 7 * scale, UiTheme.Muted,
            new Vector2(rect.X + 10 * scale, rect.Bottom - 9 * scale), "bottomleft");
    }
}
