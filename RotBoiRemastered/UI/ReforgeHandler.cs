using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

public enum ReforgeOutcome { StillOpen, Closed }

/// <summary>
/// Full-screen, combat-pausing forge where collected Fragments can improve an
/// equipped item's grade or replace its affix. Rarity is intentionally never
/// written by this class.
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
    private Rectangle _rerollRect;
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

        int actionWidth = Math.Min(Px(300), (screenWidth - Px(80)) / 2);
        int actionY = screenHeight - Px(116);
        _upgradeRect = new Rectangle(screenWidth / 2 - actionWidth - Px(10), actionY, actionWidth, Px(58));
        _rerollRect = new Rectangle(screenWidth / 2 + Px(10), actionY, actionWidth, Px(58));
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

    public bool TryUpgradeGrade(RunState state)
    {
        var item = SelectedItem(state);
        if (item is null || _selectedSlot is null || Items.GradeUpgradeCost(item) is not int cost || state.Fragments < cost)
            return false;
        state.Fragments -= cost;
        state.Equipment[_selectedSlot] = Items.UpgradeGrade(item);
        state.CombinePlayerStats();
        return true;
    }

    public bool TryRerollModifier(RunState state, Random? rng = null)
    {
        var item = SelectedItem(state);
        if (item is null || _selectedSlot is null)
            return false;
        int cost = Items.ModifierRerollCost(item);
        if (state.Fragments < cost)
            return false;
        state.Fragments -= cost;
        state.Equipment[_selectedSlot] = Items.RerollModifier(item, rng);
        state.CombinePlayerStats();
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
            TryUpgradeGrade(state);
        else if (_rerollRect.Contains(mousePosition))
            TryRerollModifier(state, rng);
        return ReforgeOutcome.StillOpen;
    }

    public void Draw(SpriteBatch spriteBatch, RunState state, Point mousePosition, bool mouseDown)
    {
        EnsureSelection(state);
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, _screenWidth, _screenHeight), UiTheme.Void);
        int grid = Px(36);
        Color gridColor = new(23, 27, 35);
        for (int x = 0; x < _screenWidth; x += grid)
            Primitives2D.Line(spriteBatch, new Vector2(x, 0), new Vector2(x, _screenHeight), gridColor, 1);
        for (int y = 0; y < _screenHeight; y += grid)
            Primitives2D.Line(spriteBatch, new Vector2(0, y), new Vector2(_screenWidth, y), gridColor, 1);

        UiTheme.DrawButton(spriteBatch, _backRect, "BACK", mousePosition, mouseDown,
            accentColor: UiTheme.Cream, keyHint: "ESC", textSize: Px(12));
        UiTheme.DrawText(spriteBatch, "THE GOLDEN FORGE", Px(34), UiTheme.Gold,
            new Vector2(_screenWidth / 2f, Px(30)), "midtop");
        UiTheme.DrawText(spriteBatch, "SPEND 5 FRAGMENTS // GRADE OR MODIFIER // RARITY IS PERMANENT", Px(12), UiTheme.Muted,
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
            UiTheme.DrawPanel(spriteBatch, panel, selected ? UiTheme.PanelHover : UiTheme.Panel,
                selected ? UiTheme.Gold : UiTheme.Border, shadow: selected ? 8 : 4, hovered: rect.Contains(mousePosition));
            if (item is not null)
                ItemCards.DrawItemCard(spriteBatch, rect, item, rect.Contains(mousePosition));
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

        int? upgradeCost = selectedItem is null ? null : Items.GradeUpgradeCost(selectedItem);
        bool canUpgrade = upgradeCost is int gradeCost && state.Fragments >= gradeCost;
        bool canReroll = selectedItem is not null && state.Fragments >= Items.ModifierRerollCost(selectedItem);
        string upgradeLabel = selectedItem is null ? "NO ITEM SELECTED"
            : upgradeCost is int cost ? $"GRADE {selectedItem.Grade} → {Items.UpgradeGrade(selectedItem).Grade}  //  {cost} FRAGMENTS"
            : "GRADE S // MAXIMUM";
        string rerollLabel = selectedItem is null ? "NO ITEM SELECTED"
            : $"REROLL {selectedItem.Modifier.ToUpperInvariant()}  //  {Items.ModifierRerollCost(selectedItem)} FRAGMENTS";
        UiTheme.DrawButton(spriteBatch, _upgradeRect, upgradeLabel, mousePosition, mouseDown, canUpgrade,
            UiTheme.Gold, textSize: Px(13));
        UiTheme.DrawButton(spriteBatch, _rerollRect, rerollLabel, mousePosition, mouseDown, canReroll,
            UiTheme.Purple, textSize: Px(13));
    }

    private void DrawSelectedItem(SpriteBatch spriteBatch, ItemDrop item)
    {
        int top = Px(310);
        int width = Math.Min(Px(720), _screenWidth - Px(80));
        var rect = new Rectangle((_screenWidth - width) / 2, top, width, Px(225));
        Color rarity = UiTheme.RarityColors.GetValueOrDefault(item.Rarity, UiTheme.Border);
        Color grade = UiTheme.GradeColors.GetValueOrDefault(item.Grade, UiTheme.Gold);
        UiTheme.DrawPanel(spriteBatch, rect, UiTheme.PanelRaised, rarity, shadow: 7);
        UiTheme.DrawText(spriteBatch, item.DisplayName.ToUpperInvariant(), Px(24), UiTheme.Text,
            new Vector2(rect.X + Px(20), rect.Y + Px(15)));
        UiTheme.DrawText(spriteBatch, $"{item.Rarity.ToUpperInvariant()} RARITY  //  GRADE {item.Grade} ({Items.GradePower(item.Grade):P0} STATS)",
            Px(11), grade, new Vector2(rect.X + Px(20), rect.Y + Px(51)));

        var core = Items.CoreForgeFor(item);
        if (core is not null)
        {
            Color coreColor = GamePaths.PathsByKey[core.PathKey].Accent;
            UiTheme.DrawTag(spriteBatch, core.DisplayName, new Vector2(rect.Right - Px(170), rect.Y + Px(20)), coreColor, Px(10));
        }

        var affix = Items.ModifierDefinition(item);
        UiTheme.DrawText(spriteBatch, $"{affix.Name.ToUpperInvariant()} MODIFIER", Px(15), UiTheme.Purple,
            new Vector2(rect.X + Px(20), rect.Y + Px(82)));
        UiTheme.DrawWrappedText(spriteBatch, affix.Description, Px(11), UiTheme.Cream,
            new Vector2(rect.X + rect.Width * .245f, rect.Y + Px(135)), rect.Width * .45f);

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
    }
}
