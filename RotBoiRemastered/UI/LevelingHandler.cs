using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

/// <summary>
/// Everything LevelingHandler's stat-preview and recommendation logic needs
/// from the run's build state. Ported from the `cS` (characterStats.py)
/// reads inside levelingHandler.py's `_projected_value`/`_recommendation`/
/// `drawCards` -- characterStats.py itself isn't ported yet (see
/// Entities/README.md's Player.cs note), so this snapshot is the explicit
/// seam: whatever eventually owns real run state builds one of these each
/// frame instead of LevelingHandler reaching into a global.
/// </summary>
public sealed class LevelUpStatSnapshot
{
    public required IReadOnlyDictionary<string, double> CollectiveStats { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<double>> CollectiveAddStats { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<double>> CollectiveMultStats { get; init; }
    public required IReadOnlyDictionary<string, int> UpgradeTypeCounts { get; init; }
    public double HealthPoints { get; init; }
    public double MaxHealthPoints { get; init; }
    public int PendingLevelUps { get; init; }
}

/// <summary>
/// Upgrade-card presentation and input handling. Ported from
/// levelingHandler.py.
///
/// Cleanup vs. the Python original: dropped several fields that were
/// assigned in `__init__` but never read anywhere else in the file or the
/// rest of the repo (verified by grep) -- `titleFont`/`descFont`/
/// `smallFont` (every draw call resolves its font fresh through
/// `UiTheme.Font` instead), `cardHoverColor` (`DrawPanel`'s own `hovered`
/// flag already picks `PanelHover` internally), `upgradeRarity`/
/// `upgradeTypesList`/`upgradeBasicTypesAdd`/`upgradeBasicTypesMult`, and
/// `frameRate`. Also dropped `_draw_centered` (defined, never called) and
/// `_sync_legacy_fields` (a Python-side "keep old attribute names working
/// while callers migrate" shim with nothing left in this codebase to
/// migrate).
/// </summary>
public sealed class LevelingHandler
{
    private static readonly Keys[] CardKeys = { Keys.D1, Keys.D2, Keys.D3 };
    private static readonly string[] CardSlotNames = { "leftCard", "midCard", "rightCard" };

    public int Rerolls { get; private set; } = 2;
    public List<UpgradeCard> Cards { get; private set; }
    public UpgradeCard? SelectedCard { get; private set; }
    public bool Randomizing { get; private set; }

    public Rectangle RerollButton { get; private set; }
    public Rectangle LeftCard { get; private set; }
    public Rectangle MidCard { get; private set; }
    public Rectangle RightCard { get; private set; }
    public IReadOnlyList<Rectangle> CardRects { get; private set; } = Array.Empty<Rectangle>();

    private int _screenWidth, _screenHeight;
    private float _tileSize;
    private bool _firstClick = true;

    public LevelingHandler(int screenWidth, int screenHeight, Random? rng = null)
    {
        Cards = Upgrades.GenerateOffer(count: 3, rng: rng);
        UpdateLayout(screenWidth, screenHeight);
    }

    private float Px(float value) => Math.Max(1, MathF.Round(value * UiTheme.DisplayScale(_screenWidth, _screenHeight)));

    public void UpdateLayout(int screenWidth, int screenHeight)
    {
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
        _tileSize = Math.Min(screenWidth, screenHeight) / 20f;

        float cardWidth = (screenWidth - _tileSize * 5) / 3f;
        float cardHeight = screenHeight * 0.62f;
        float cardY = (screenHeight - cardHeight) / 2f - _tileSize * 0.35f;

        RerollButton = new Rectangle(
            (int)(screenWidth / 2f - _tileSize * 2.1f), (int)(screenHeight * 0.87f),
            (int)(_tileSize * 4.2f), (int)_tileSize);
        LeftCard = new Rectangle((int)(_tileSize * 2), (int)cardY, (int)cardWidth, (int)cardHeight);
        MidCard = new Rectangle((int)(_tileSize * 3 + cardWidth), (int)cardY, (int)cardWidth, (int)cardHeight);
        RightCard = new Rectangle((int)(_tileSize * 4 + 2 * cardWidth), (int)cardY, (int)cardWidth, (int)cardHeight);
        CardRects = new[] { LeftCard, MidCard, RightCard };
    }

    /// <summary>Public (Python's `_projected_value` was underscore-private in name only).</summary>
    public static (double Current, double Projected) ProjectedValue(UpgradeCard card, LevelUpStatSnapshot stats)
    {
        double baseValue = stats.CollectiveStats[card.Name];
        double additive = stats.CollectiveAddStats[card.Name].Sum();
        double multiplicative = stats.CollectiveMultStats[card.Name].Aggregate(1.0, (acc, v) => acc * v);
        double modifier = Upgrades.CardModifier(card);
        double current = (baseValue + additive) * multiplicative;
        double projected = card.MathType == "additive"
            ? (baseValue + additive + modifier) * multiplicative
            : current * modifier;
        return (current, projected);
    }

    /// <summary>Public (Python's `_recommendation` was underscore-private in name only).</summary>
    public static (string? Label, Color? Accent) Recommendation(UpgradeCard card, LevelUpStatSnapshot stats)
    {
        var ownedCategories = new Dictionary<string, int>();
        foreach (var (name, count) in stats.UpgradeTypeCounts)
        {
            if (Upgrades.DefinitionsByName.TryGetValue(name, out var definition))
                ownedCategories[definition.Category] = ownedCategories.GetValueOrDefault(definition.Category) + count;
        }
        if (stats.HealthPoints <= stats.MaxHealthPoints * .45 && card.Definition.Category == "survival")
            return ("SAFE PICK", UiTheme.Green);
        if (ownedCategories.GetValueOrDefault(card.Definition.Category) >= 2)
            return ("BUILD MATCH", UiTheme.Purple);
        return (null, null);
    }

    public void DrawCards(SpriteBatch spriteBatch, LevelUpStatSnapshot stats, Point mousePosition, bool mouseDown)
    {
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, _screenWidth, _screenHeight), UiTheme.Void);
        int gridSize = Math.Max(24, (int)(_tileSize * 0.55f));
        var gridColor = new Color(23, 27, 35);
        for (int x = 0; x < _screenWidth; x += gridSize)
            Primitives2D.Line(spriteBatch, new Vector2(x, 0), new Vector2(x, _screenHeight), gridColor, 1);
        for (int y = 0; y < _screenHeight; y += gridSize)
            Primitives2D.Line(spriteBatch, new Vector2(0, y), new Vector2(_screenWidth, y), gridColor, 1);

        UiTheme.DrawText(spriteBatch, "LEVEL SECURED", _tileSize * 0.34, UiTheme.Green,
            new Vector2(_screenWidth / 2f, _tileSize * 0.3f), "midtop");
        UiTheme.DrawText(spriteBatch, "CHOOSE ONE // SHAPE THE RUN", _tileSize * 0.72, UiTheme.Text,
            new Vector2(_screenWidth / 2f, _tileSize * 0.75f), "midtop");
        UiTheme.DrawText(spriteBatch, "Every card stays with you until the run ends.", _tileSize * 0.3, UiTheme.Muted,
            new Vector2(_screenWidth / 2f, _tileSize * 1.65f), "midtop");
        if (stats.PendingLevelUps > 1)
        {
            UiTheme.DrawTag(spriteBatch, $"{stats.PendingLevelUps} DRAFTS QUEUED",
                new Vector2(_tileSize * 2, _tileSize * 1.25f), UiTheme.Gold, _tileSize * .21);
        }

        double textScale = UiTheme.TextScaleMultiplier();
        for (int index = 0; index < CardRects.Count; index++)
        {
            var rect = CardRects[index];
            var card = Cards[index];
            bool hovered = rect.Contains(mousePosition);
            Color accent = UiTheme.RarityColors.TryGetValue(card.Rarity, out var rarityColor) ? rarityColor : UiTheme.Border;
            bool pressed = hovered && mouseDown;
            var visualRect = new Rectangle(rect.X, rect.Y + (int)(pressed ? Px(2) : hovered ? -Px(7) : 0), rect.Width, rect.Height);
            UiTheme.DrawPanel(spriteBatch, visualRect, UiTheme.Panel, hovered ? accent : UiTheme.Border,
                shadow: pressed ? 3 : 7, hovered: hovered);
            Primitives2D.FillRect(spriteBatch, new Rectangle(visualRect.X, visualRect.Y, visualRect.Width, (int)Px(9)), accent);

            var keyRect = new Rectangle((int)(visualRect.X + Px(18)), (int)(visualRect.Y + Px(24)), (int)Px(38), (int)Px(38));
            Primitives2D.FillRect(spriteBatch, keyRect, UiTheme.Ink);
            Primitives2D.RectOutline(spriteBatch, keyRect, accent, (int)Px(2));
            UiTheme.DrawText(spriteBatch, index + 1, _tileSize * 0.4, accent,
                new Vector2(keyRect.Center.X, keyRect.Center.Y), "center");

            float rarityWidth = UiTheme.Font(_tileSize * .23).MeasureString(card.Rarity.ToUpperInvariant()).X;
            UiTheme.DrawTag(spriteBatch, card.Rarity,
                new Vector2(visualRect.Right - Px(22) - rarityWidth, visualRect.Y + Px(29)), accent, _tileSize * .23);
            UiTheme.DrawText(spriteBatch, card.Definition.Category.ToUpperInvariant() + " CARD", _tileSize * 0.24, UiTheme.Muted,
                new Vector2(visualRect.Center.X, visualRect.Y + _tileSize * 1.55f), "center");
            UiTheme.DrawText(spriteBatch, card.Name, _tileSize * 0.58, UiTheme.Text,
                new Vector2(visualRect.Center.X, visualRect.Y + _tileSize * 2.15f), "center");
            Primitives2D.Line(spriteBatch, new Vector2(visualRect.X + Px(28), visualRect.Y + _tileSize * 2.72f),
                new Vector2(visualRect.Right - Px(28), visualRect.Y + _tileSize * 2.72f), accent, (int)Px(2));
            UiTheme.DrawText(spriteBatch, card.Definition.Description, _tileSize * 0.38, UiTheme.Text,
                new Vector2(visualRect.Center.X, visualRect.Center.Y - _tileSize * 0.2f), "center");

            string mode = card.MathType == "additive" ? "Flat bonus" : "Scaling bonus";
            float modeWidth = UiTheme.Font(_tileSize * .2).MeasureString(mode.ToUpperInvariant()).X;
            UiTheme.DrawTag(spriteBatch, mode,
                new Vector2(visualRect.Center.X - modeWidth / 2, visualRect.Center.Y + _tileSize * 0.65f), UiTheme.Blue, _tileSize * .2);
            UiTheme.DrawText(spriteBatch, Upgrades.FormatCardValue(card), _tileSize * 0.78, accent,
                new Vector2(visualRect.Center.X, visualRect.Bottom - _tileSize * 1.25f * (float)textScale), "center");

            var (current, projected) = ProjectedValue(card, stats);
            string direction = card.Name == "Attack Speed" ? "LOWER IS FASTER" : "PROJECTED STAT";
            UiTheme.DrawText(spriteBatch, $"{direction}  //  {current:F2} → {projected:F2}", _tileSize * .19,
                projected != current ? UiTheme.Green : UiTheme.Muted,
                new Vector2(visualRect.Center.X, visualRect.Bottom - _tileSize * .76f * (float)textScale), "center");

            int owned = stats.UpgradeTypeCounts.GetValueOrDefault(card.Name);
            UiTheme.DrawText(spriteBatch, $"OWNED  {owned}", _tileSize * 0.22, UiTheme.Muted,
                new Vector2(visualRect.Center.X, visualRect.Bottom - _tileSize * 0.45f * (float)textScale), "center");

            var (recommendation, recommendationColor) = Recommendation(card, stats);
            if (recommendation is not null)
            {
                UiTheme.DrawTag(spriteBatch, recommendation,
                    new Vector2(visualRect.X + Px(18), visualRect.Bottom - _tileSize * .62f), recommendationColor, _tileSize * .18);
            }
        }

        UiTheme.DrawButton(spriteBatch, RerollButton, $"RUN REROLLS  //  {Rerolls} LEFT", mousePosition,
            mouseDown, Rerolls > 0, UiTheme.Red, "R", _tileSize * 0.31);
    }

    /// <summary>
    /// Returns "none" or one of "leftCard"/"midCard"/"rightCard" -- kept as
    /// the exact string contract levelingHandler.py's caller (character.py's
    /// not-yet-ported handleLevelingProcess) expects, since that's the only
    /// consumer and its final shape isn't decided yet.
    /// </summary>
    public string PlayerClicked(IReadOnlySet<Keys> keysPressed, Point mousePosition, bool mouseDown,
        IReadOnlyDictionary<string, int> upgradeTypeCounts, Random? rng = null)
    {
        if (keysPressed.Contains(Keys.R) && Rerolls > 0)
        {
            RandomizeLevelUp(upgradeTypeCounts, rng);
            Rerolls -= 1;
            return "none";
        }

        for (int index = 0; index < CardKeys.Length; index++)
        {
            if (keysPressed.Contains(CardKeys[index]))
            {
                SelectedCard = Cards[index];
                return CardSlotNames[index];
            }
        }

        if (!mouseDown)
            _firstClick = false;
        if (mouseDown && !_firstClick)
        {
            if (RerollButton.Contains(mousePosition))
            {
                _firstClick = true;
                if (Rerolls > 0)
                {
                    RandomizeLevelUp(upgradeTypeCounts, rng);
                    Rerolls -= 1;
                }
                return "none";
            }
            for (int index = 0; index < CardRects.Count; index++)
            {
                var clickable = CardRects[index];
                clickable.Inflate(0, (int)Px(14));
                clickable.Y -= (int)Px(7);
                if (clickable.Contains(mousePosition))
                {
                    _firstClick = true;
                    SelectedCard = Cards[index];
                    return CardSlotNames[index];
                }
            }
        }
        return "none";
    }

    public void RandomizeLevelUp(IReadOnlyDictionary<string, int> upgradeTypeCounts, Random? rng = null)
    {
        Cards = Upgrades.GenerateOffer(upgradeTypeCounts, count: 3, rng: rng);
        SelectedCard = null;
        Randomizing = true;
    }
}
