using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

/// <summary>Ported from the layout/input/stat-preview logic in levelingHandler.py (no existing Python tests to mirror).</summary>
public class LevelingHandlerTests
{
    private static LevelUpStatSnapshot MakeSnapshot(
        double defenseBase = 100, double[]? add = null, double[]? mult = null,
        IReadOnlyDictionary<string, int>? owned = null, double health = 1000, double maxHealth = 1000, int pending = 1)
    {
        return new LevelUpStatSnapshot
        {
            CollectiveStats = new Dictionary<string, double> { ["Defense"] = defenseBase, ["Bullet Damage"] = 100 },
            CollectiveAddStats = new Dictionary<string, IReadOnlyList<double>>
            {
                ["Defense"] = add ?? new double[] { 10, 5 },
                ["Bullet Damage"] = new double[] { 0 },
            },
            CollectiveMultStats = new Dictionary<string, IReadOnlyList<double>>
            {
                ["Defense"] = mult ?? new double[] { 1.0, 1.2 },
                ["Bullet Damage"] = new double[] { 1.0 },
            },
            UpgradeTypeCounts = owned ?? new Dictionary<string, int>(),
            HealthPoints = health,
            MaxHealthPoints = maxHealth,
            PendingLevelUps = pending,
        };
    }

    private static UpgradeCard DefenseCard(string mathType, string rarity = "Common") =>
        new(Upgrades.DefinitionsByName["Defense"], rarity, mathType);

    [Fact]
    public void UpdateLayout_ProducesNonOverlappingCardsWithinScreen()
    {
        var handler = new LevelingHandler(1920, 1080, new Random(1));
        foreach (var rect in handler.CardRects)
        {
            Assert.True(rect.X >= 0 && rect.Right <= 1920);
            Assert.True(rect.Y >= 0 && rect.Bottom <= 1080);
        }
        Assert.True(handler.LeftCard.Right <= handler.MidCard.Left);
        Assert.True(handler.MidCard.Right <= handler.RightCard.Left);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void UpdateLayout_WindowedAndFullscreenSizesKeepAllControlsWithinViewport(int width, int height)
    {
        var handler = new LevelingHandler(width, height, new Random(1));

        foreach (var rect in handler.CardRects.Append(handler.RerollButton))
        {
            Assert.True(rect.Width > 0 && rect.Height > 0);
            Assert.True(rect.X >= 0 && rect.Y >= 0);
            Assert.True(rect.Right <= width && rect.Bottom <= height);
        }
    }

    [Fact]
    public void Constructor_GeneratesThreeCards()
    {
        var handler = new LevelingHandler(1920, 1080, new Random(1));
        Assert.Equal(3, handler.Cards.Count);
    }

    [Theory]
    [InlineData(Keys.D1, "leftCard", 0)]
    [InlineData(Keys.D2, "midCard", 1)]
    [InlineData(Keys.D3, "rightCard", 2)]
    public void PlayerClicked_NumberKey_SelectsMatchingCard(Keys key, string expectedSlot, int cardIndex)
    {
        var handler = new LevelingHandler(1920, 1080, new Random(1));
        var keysPressed = new HashSet<Keys> { key };

        string result = handler.PlayerClicked(keysPressed, Point.Zero, false, new Dictionary<string, int>());

        Assert.Equal(expectedSlot, result);
        Assert.Same(handler.Cards[cardIndex], handler.SelectedCard);
    }

    [Fact]
    public void PlayerClicked_RKeyWithRerollsAvailable_ConsumesARerollAndReturnsNone()
    {
        var handler = new LevelingHandler(1920, 1080, new Random(1));
        int rerollsBefore = handler.Rerolls;

        string result = handler.PlayerClicked(new HashSet<Keys> { Keys.R }, Point.Zero, false, new Dictionary<string, int>());

        Assert.Equal("none", result);
        Assert.Equal(rerollsBefore - 1, handler.Rerolls);
    }

    [Fact]
    public void PlayerClicked_RKeyWithNoRerollsLeft_DoesNothing()
    {
        var handler = new LevelingHandler(1920, 1080, new Random(1));
        var counts = new Dictionary<string, int>();
        handler.PlayerClicked(new HashSet<Keys> { Keys.R }, Point.Zero, false, counts);
        handler.PlayerClicked(new HashSet<Keys> { Keys.R }, Point.Zero, false, counts);
        Assert.Equal(0, handler.Rerolls);

        string result = handler.PlayerClicked(new HashSet<Keys> { Keys.R }, Point.Zero, false, counts);

        Assert.Equal("none", result);
        Assert.Equal(0, handler.Rerolls);
    }

    [Fact]
    public void PlayerClicked_ClickingRerollButton_ConsumesAReroll()
    {
        var handler = new LevelingHandler(1920, 1080, new Random(1));
        var buttonCenter = new Point(handler.RerollButton.Center.X, handler.RerollButton.Center.Y);
        int rerollsBefore = handler.Rerolls;

        // First call with mouseDown establishes "not first click yet was released" gating,
        // matching Python's firstClick bookkeeping (a fresh press only counts once released once).
        handler.PlayerClicked(new HashSet<Keys>(), Point.Zero, false, new Dictionary<string, int>());
        string result = handler.PlayerClicked(new HashSet<Keys>(), buttonCenter, true, new Dictionary<string, int>());

        Assert.Equal("none", result);
        Assert.Equal(rerollsBefore - 1, handler.Rerolls);
    }

    [Fact]
    public void RandomizeLevelUp_ClearsSelectionAndSetsRandomizing()
    {
        var handler = new LevelingHandler(1920, 1080, new Random(1));
        handler.PlayerClicked(new HashSet<Keys> { Keys.D1 }, Point.Zero, false, new Dictionary<string, int>());
        Assert.NotNull(handler.SelectedCard);

        handler.RandomizeLevelUp(new Dictionary<string, int>(), new Random(2));

        Assert.Null(handler.SelectedCard);
        Assert.True(handler.Randomizing);
    }

    [Fact]
    public void ProjectedValue_Additive_MatchesHandComputedStack()
    {
        var stats = MakeSnapshot();
        var card = DefenseCard("additive"); // Defense: Additive=100, Multiplicative=0.12; Common rarity x1.0
        var (current, projected) = LevelingHandler.ProjectedValue(card, stats);

        Assert.Equal(138.0, current, 3); // (100 + 10 + 5) * (1.0 * 1.2)
        Assert.Equal(258.0, projected, 3); // (100 + 15 + 100) * 1.2 -- modifier = 100 * 1.0
    }

    [Fact]
    public void ProjectedValue_Multiplicative_ScalesCurrentByModifier()
    {
        var stats = MakeSnapshot();
        var card = DefenseCard("multiplicative");
        var (current, projected) = LevelingHandler.ProjectedValue(card, stats);

        Assert.Equal(138.0, current, 3);
        Assert.Equal(154.56, projected, 2); // current * (1 + 0.12 * 1.0)
    }

    [Fact]
    public void Recommendation_SafePick_WhenLowHealthAndSurvivalCategory()
    {
        var stats = MakeSnapshot(health: 400, maxHealth: 1000); // 400 <= 1000*.45
        var card = DefenseCard("additive");
        var (label, _) = LevelingHandler.Recommendation(card, stats);
        Assert.Equal("SAFE PICK", label);
    }

    [Fact]
    public void Recommendation_BuildMatch_WhenCategoryAlreadyOwnedTwice()
    {
        var stats = MakeSnapshot(health: 1000, maxHealth: 1000, owned: new Dictionary<string, int> { ["Defense"] = 2 });
        var card = DefenseCard("additive");
        var (label, _) = LevelingHandler.Recommendation(card, stats);
        Assert.Equal("BUILD MATCH", label);
    }

    [Fact]
    public void Recommendation_Null_WhenNeitherConditionApplies()
    {
        var stats = MakeSnapshot(health: 1000, maxHealth: 1000);
        var card = DefenseCard("additive");
        var (label, accent) = LevelingHandler.Recommendation(card, stats);
        Assert.Null(label);
        Assert.Null(accent);
    }
}
