using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

/// <summary>
/// Ported from the layout/derived-value logic in informationSheet.py (no
/// existing Python tests to mirror). The rect-hit-testing drag paths
/// (DrawSheet populating _equipmentSlotRects/_lootPanelSlotRects, then
/// HandleDrag reading them) need a prior Draw call against a real
/// GraphicsDevice, so those are left to visual smoke testing, same as
/// Menus.cs/LevelingHandler.cs's mouse-click paths.
/// </summary>
[Collection("GameProfileState")]
public class InformationSheetTests
{
    [Fact]
    public void Constructor_CompactMode_NarrowerThanExpanded()
    {
        GameProfile.Profile.HudMode = "compact";
        var compact = new InformationSheet(1920, 1080);
        GameProfile.Profile.HudMode = "expanded";
        var expanded = new InformationSheet(1920, 1080);

        Assert.True(compact.ArenaWidth > expanded.ArenaWidth);
    }

    [Fact]
    public void ArenaWidth_NeverExceeds42PercentOfScreen()
    {
        GameProfile.Profile.HudMode = "expanded";
        var sheet = new InformationSheet(800, 600);
        Assert.True(800 - sheet.ArenaWidth <= 800 * .42);
    }

    [Fact]
    public void ToggleMode_PersistsToProfileAndChangesArenaWidth()
    {
        GameProfile.Profile.HudMode = "compact";
        var sheet = new InformationSheet(1920, 1080);
        int before = sheet.ArenaWidth;

        sheet.ToggleMode();

        Assert.Equal("expanded", GameProfile.Profile.HudMode);
        Assert.NotEqual(before, sheet.ArenaWidth);
    }

    [Fact]
    public void SyncLayout_SameSize_DoesNotChangeArenaWidth()
    {
        var sheet = new InformationSheet(1920, 1080);
        int before = sheet.ArenaWidth;
        sheet.SyncLayout(1920, 1080);
        Assert.Equal(before, sheet.ArenaWidth);
    }

    [Fact]
    public void SyncLayout_DifferentSize_RebuildsLayout()
    {
        var sheet = new InformationSheet(1920, 1080);
        sheet.SyncLayout(800, 600);
        Assert.True(sheet.ArenaWidth < 800);
    }

    private static RunState MakeState()
    {
        var state = new RunState();
        return state;
    }

    [Fact]
    public void FamilyCounts_OrdersByCountDescendingThenNameAscending()
    {
        var state = MakeState();
        state.RecordUpgrade("Bullet Damage", "Common"); // power
        state.RecordUpgrade("Bullet Size", "Common"); // power
        state.RecordUpgrade("Defense", "Common"); // survival

        var families = InformationSheet.FamilyCounts(state);

        Assert.Equal(("power", 2), families[0]);
        Assert.Equal(("survival", 1), families[1]);
    }

    [Fact]
    public void BuildIdentity_NoUpgrades_ReturnsFreshStart()
    {
        var (title, _, caution) = InformationSheet.BuildIdentity(MakeState());
        Assert.Equal("FRESH START", title);
        Assert.Equal("No weakness yet", caution);
    }

    [Fact]
    public void BuildIdentity_KnownFamily_UsesBuildName()
    {
        var state = MakeState();
        state.RecordUpgrade("Bullet Pierce", "Common"); // volley

        var (title, _, _) = InformationSheet.BuildIdentity(state);

        Assert.Equal("BULLET STORM", title);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(150, false)]
    [InlineData(300, false)]
    public void Rating_NonPositiveValue_ReturnsNone(double value, bool inverse)
    {
        Assert.Equal("None", InformationSheet.Rating(value <= 0 ? 0 : -1, 100, inverse));
    }

    [Fact]
    public void Rating_ExceedsBaselineByDouble_ReturnsExceptional()
    {
        Assert.Equal("Exceptional", InformationSheet.Rating(200, 100));
    }

    [Fact]
    public void Rating_AtBaseline_ReturnsNormal()
    {
        Assert.Equal("Normal", InformationSheet.Rating(100, 100));
    }

    [Fact]
    public void ShotText_WholeNumber_NoBonusClause()
    {
        var state = MakeState();
        // ProjectileCount is derived (CombinePlayerStats), but Reset() leaves it at the raw base of 1.
        Assert.Equal("1 shot", InformationSheet.ShotText(state));
    }

    [Fact]
    public void Pressure_NoEnemiesNoBoss_ReturnsCalm()
    {
        var (label, _, _) = InformationSheet.Pressure(MakeState());
        Assert.Equal("CALM", label);
    }

    [Fact]
    public void Pressure_GameCompleted_ReturnsRunComplete()
    {
        var state = MakeState();
        state.GameCompleted = true;
        var (label, _, _) = InformationSheet.Pressure(state);
        Assert.Equal("RUN COMPLETE", label);
    }

    [Fact]
    public void Pressure_EliteEnemyPresent_ReturnsEliteNearby()
    {
        var state = MakeState();
        var enemy = new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 1, difficulty: 1, awarenessRange: 100f)
        {
            CombatRole = "elite",
        };
        state.EnemyHolster.Add(enemy);

        var (label, _, _) = InformationSheet.Pressure(state);

        Assert.Equal("ELITE NEARBY", label);
    }

    [Fact]
    public void BountyDetails_NullBounty_ReturnsExploreArena()
    {
        var (name, detail) = InformationSheet.BountyDetails(null, MakeState(), Vector2.Zero);
        Assert.Equal("Explore the arena", name);
        Assert.Equal("No active target", detail);
    }

    [Fact]
    public void BountyDetails_NearbySingleEnemy_ReportsOneHostileAndNearby()
    {
        var state = MakeState();
        var enemy = new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 1, difficulty: 1, awarenessRange: 100f);
        var bounty = new BountyInfo(new Vector2(5, 5), 10, "TEST TARGET", enemy);

        var (name, detail) = InformationSheet.BountyDetails(bounty, state, Vector2.Zero);

        Assert.Equal("Test Target", name);
        Assert.Contains("1 hostile", detail);
        Assert.Contains("Target nearby", detail);
    }

    [Fact]
    public void BountyDetails_UsesPlayerCenterWithoutAddingAnotherHalfSizeOffset()
    {
        var state = MakeState();
        var enemy = new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 1, difficulty: 1, awarenessRange: 100f);
        var playerCenter = new Vector2(100, 100);
        var bounty = new BountyInfo(new Vector2(100 + Simulation.TileSize * 10, 100), 10, "TEST TARGET", enemy);

        var (_, detail) = InformationSheet.BountyDetails(bounty, state, playerCenter);

        Assert.Contains("About 10 tiles away", detail);
    }

    [Fact]
    public void NextMilestone_LevelZero_ReturnsFirstMinibossGate()
    {
        var state = MakeState();
        state.CurrentLevel = 0;
        var (level, milestone) = InformationSheet.NextMilestone(state);
        Assert.Equal(5, level);
        Assert.Equal("Arsenal", milestone);
    }

    [Fact]
    public void NextMilestone_PastFinalBoss_ReturnsComplete()
    {
        var state = MakeState();
        state.CurrentLevel = 999;
        var (level, milestone) = InformationSheet.NextMilestone(state);
        Assert.Equal(20, level);
        Assert.Equal("Complete", milestone);
    }
}
