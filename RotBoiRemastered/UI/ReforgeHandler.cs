using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public enum ReforgeOutcome { StillOpen, Closed }

/// <summary>
/// Full-screen, combat-pausing forge where collected Fragments raise an
/// equipped item's Rarity. Rarity is now the item's only power dial (Grade
/// is gone) and Modifiers are never rerolled -- an item's ModifierLadder is
/// fixed at authoring time (see ItemDefinition.ModifierLadder), so the one
/// thing left for Fragments to buy here is climbing that same ladder by
/// spending them to raise Rarity a step, which is exactly what "the item
/// itself holds the power, Reforge structures the build" means in practice.
/// </summary>
public sealed class ReforgeHandler
{
    private static readonly string[] SlotOrder =
        { "weapon", "armor", "ring", "accessory_1", "accessory_2" };

    private int _screenWidth;
    private int _screenHeight;
    private float _scale;
    private readonly Dictionary<string, Rectangle> _slotRects = new();
    private Rectangle _upgradeRect;
    private Rectangle _backRect;
    private string? _selectedSlot;

    public string? SelectedSlot => _selectedSlot;

    public ReforgeHandler(int screenWidth, int screenHeight) => UpdateLayout(screenWidth, screenHeight);

    private int Px(double value) => Math.Max(1, (int)Math.Round(value * _scale));

    public void UpdateLayout(int screenWidth, int screenHeight)
    {
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
        _scale = UiTheme.DisplayScale(screenWidth, screenHeight);

        int slotSize = Px(112);
        int gap = Px(18);
        int totalWidth = SlotOrder.Length * slotSize + (SlotOrder.Length - 1) * gap;
        int startX = (screenWidth - totalWidth) / 2;
        int slotY = Px(150);
        _slotRects.Clear();
        for (int index = 0; index < SlotOrder.Length; index++)
            _slotRects[SlotOrder[index]] = new Rectangle(startX + index * (slotSize + gap), slotY, slotSize, slotSize);

        int actionWidth = Math.Min(Px(360), screenWidth - Px(80));
        int actionY = screenHeight - Px(116);
        _upgradeRect = new Rectangle(screenWidth / 2 - actionWidth / 2, actionY, actionWidth, Px(58));
        _backRect = new Rectangle(Px(24), Px(24), Px(145), Px(44));
    }

    private void EnsureSelection(RunState state)
    {
        if (_selectedSlot is not null && state.Equipment.GetValueOrDefault(_selectedSlot) is not null)
            return;
        _selectedSlot = SlotOrder.FirstOrDefault(slot => state.Equipment.GetValueOrDefault(slot) is not null);
    }

    public ItemDrop? SelectedItem(RunState state)
    {
        EnsureSelection(state);
        return _selectedSlot is null ? null : state.Equipment.GetValueOrDefault(_selectedSlot);
    }

    public bool TryUpgradeRarity(RunState state)
    {
        var item = SelectedItem(state);
        if (item is null || _selectedSlot is null || Items.RarityUpgradeCost(item) is not int cost || state.Fragments < cost)
            return false;
        state.Fragments -= cost;
        state.Equipment[_selectedSlot] = Items.UpgradeRarity(item);
        state.CombinePlayerStats();
        state.ReforgeUsedThisRun = true;
        return true;
    }

    public ReforgeOutcome HandleInput(IReadOnlySet<Keys> keysPressed, Point mousePosition, bool mousePressed,
        RunState state, Random? rng = null)
    {
        EnsureSelection(state);
        if (keysPressed.Contains(Keys.Escape) || (mousePressed && _backRect.Contains(mousePosition)))
            return ReforgeOutcome.Closed;
        if (!mousePressed)
            return ReforgeOutcome.StillOpen;
        foreach (var (slot, rect) in _slotRects)
        {
            if (rect.Contains(mousePosition) && state.Equipment.GetValueOrDefault(slot) is not null)
            {
                _selectedSlot = slot;
                return ReforgeOutcome.StillOpen;
            }
        }
        if (_upgradeRect.Contains(mousePosition))
            TryUpgradeRarity(state);
        return ReforgeOutcome.StillOpen;
    }

    public void Draw(SpriteBatch spriteBatch, RunState state, Point mousePosition, bool mouseDown)
    {
        EnsureSelection(state);
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, _screenWidth, _screenHeight), UiTheme.Void);
        int frameMargin = Px(18);
        float chromeTime = (float)(state.RunTimeSeconds
            * GameProfile.Profile.VisualEffectsIntensity);
        UiTheme.DrawFramedPanel(spriteBatch,
            new Rectangle(frameMargin, frameMargin,
                _screenWidth - frameMargin * 2, _screenHeight - frameMargin * 2),
            UiTheme.Panel, UiTheme.Border, 7);

        UiTheme.DrawButton(spriteBatch, _backRect, "BACK", mousePosition, mouseDown,
            accentColor: UiTheme.Cream, keyHint: "ESC", textSize: Px(12));
        UiTheme.DrawText(spriteBatch, "THE GOLDEN FORGE", Px(34), UiTheme.Gold,
            new Vector2(_screenWidth / 2f, Px(30)), "midtop");
        UiTheme.DrawText(spriteBatch, $"SPEND {Items.ReforgeFragmentCost} FRAGMENTS // RAISE RARITY // MODIFIERS UNLOCK AS YOU CLIMB", Px(12), UiTheme.Muted,
            new Vector2(_screenWidth / 2f, Px(79)), "midtop");
        UiTheme.DrawTag(spriteBatch, $"FRAGMENTS  {state.Fragments:N0}",
            new Vector2(_screenWidth - Px(210), Px(31)), UiTheme.Gold, Px(11));

        foreach (var slot in SlotOrder)
        {
            var rect = _slotRects[slot];
            var item = state.Equipment.GetValueOrDefault(slot);
            bool selected = slot == _selectedSlot;
            var panel = rect;
            panel.Inflate(Px(8), Px(8));
            UiTheme.DrawFramedPanel(spriteBatch, panel,
                selected ? UiTheme.PanelHover : UiTheme.Panel,
                selected ? UiTheme.Gold : UiTheme.Border,
                shadow: selected ? 8 : 4,
                hovered: rect.Contains(mousePosition));
            if (item is not null)
                ItemCards.DrawItemCard(
                    spriteBatch, rect, item, rect.Contains(mousePosition),
                    (float)state.RunTimeSeconds);
            else
            {
                Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
                Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Border, Px(2));
            }
            UiTheme.DrawText(spriteBatch, slot.Replace('_', ' ').ToUpperInvariant(), Px(10),
                selected ? UiTheme.Gold : UiTheme.Muted, new Vector2(rect.Center.X, panel.Bottom + Px(6)), "midtop");
        }

        var selectedItem = SelectedItem(state);
        if (selectedItem is not null)
            DrawSelectedItem(spriteBatch, selectedItem);

        int? upgradeCost = selectedItem is null ? null : Items.RarityUpgradeCost(selectedItem);
        bool canUpgrade = upgradeCost is int cost && state.Fragments >= cost;
        string upgradeLabel = selectedItem is null ? "NO ITEM SELECTED"
            : upgradeCost is int fragCost ? $"RAISE RARITY: {selectedItem.Rarity.ToUpperInvariant()} → {Items.UpgradeRarity(selectedItem).Rarity.ToUpperInvariant()}  //  {fragCost} FRAGMENTS"
            : $"{selectedItem.Rarity.ToUpperInvariant()} // MAXIMUM RARITY";
        UiTheme.DrawButton(spriteBatch, _upgradeRect, upgradeLabel, mousePosition, mouseDown, canUpgrade,
            UiTheme.Gold, textSize: Px(14));
    }

    private void DrawSelectedItem(SpriteBatch spriteBatch, ItemDrop item)
    {
        int top = Px(310);
        int width = Math.Min(Px(760), _screenWidth - Px(80));
        int ladderHeight = ItemCards.MeasureModifierLadder(Px(11), item);
        var rect = new Rectangle((_screenWidth - width) / 2, top, width, Px(150) + ladderHeight);
        Color rarity = UiTheme.RarityColors.GetValueOrDefault(item.Rarity, UiTheme.Border);
        UiTheme.DrawFramedPanel(spriteBatch, rect,
            UiTheme.PanelRaised, rarity, shadow: 7);
        UiTheme.DrawText(spriteBatch, item.DisplayName.ToUpperInvariant(), Px(24), UiTheme.Text,
            new Vector2(rect.X + Px(20), rect.Y + Px(15)));
        UiTheme.DrawText(spriteBatch,
            $"{item.Rarity.ToUpperInvariant()} RARITY  //  {Items.ModifierUnlockCount(item.Rarity)} OF {item.Definition.ModifierLadder.Count} MODIFIERS UNLOCKED",
            Px(11), rarity, new Vector2(rect.X + Px(20), rect.Y + Px(51)));

        var core = Items.CoreForgeFor(item);
        if (core is not null)
        {
            Color coreColor = GamePaths.PathsByKey[core.PathKey].Accent;
            UiTheme.DrawTag(spriteBatch, core.DisplayName, new Vector2(rect.Right - Px(170), rect.Y + Px(20)), coreColor, Px(10));
        }

        var effects = Items.Effects(item);
        int columnX = rect.Center.X + Px(20);
        int rowY = rect.Y + Px(74);
        foreach (var effect in effects.Take(6))
        {
            Color color = effect.IsBeneficial ? UiTheme.Green : UiTheme.Red;
            UiTheme.DrawText(spriteBatch, effect.Stat.ToUpperInvariant(), Px(10), UiTheme.Muted,
                new Vector2(columnX, rowY));
            UiTheme.DrawText(spriteBatch, effect.DisplayValue, Px(12), color,
                new Vector2(rect.Right - Px(20), rowY), "topright");
            rowY += Px(23);
        }

        // The Modifier/Signature unlock ladder for this item, at the bottom
        // of the panel -- exactly where the venture asked for it, and this
        // is Reforge's own natural home for it, since raising Rarity here is
        // literally what climbs the ladder being shown.
        ItemCards.DrawModifierLadder(spriteBatch, new Vector2(rect.X + Px(20), rect.Y + Px(150)), Px(11), item);
    }
}
