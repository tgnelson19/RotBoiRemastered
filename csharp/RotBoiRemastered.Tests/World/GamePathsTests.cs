using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

/// <summary>
/// Ported from tests/test_game_paths.py's catalog-shape coverage (the vast
/// majority of that file is boss-content tests, deferred along with
/// bossTypes.py). Select/Cycle/ActivateSelected/BossKey/IsTouch aren't
/// directly unit-tested in the Python original, so that coverage below is new.
/// </summary>
public class GamePathsTests
{
    public GamePathsTests()
    {
        GamePaths.Select("sound");
        GamePaths.ActivateSelected();
    }

    [Fact]
    public void Catalog_ExposesAllFiveIsolatedContentPaths_InOrder()
    {
        Assert.Equal(new[] { "sound", "touch", "sight", "chemesthesis", "phantasia" },
            GamePaths.Paths.Select(path => path.Key));

        var expected = new Dictionary<string, (string MidBoss, string FinalBoss)>
        {
            ["sound"] = ("beaudis", "dissonance"),
            ["touch"] = ("bair", "sting"),
            ["sight"] = ("ishe", "chronos"),
            ["chemesthesis"] = ("kage", "rot"),
            ["phantasia"] = ("hypno", "malady"),
        };
        foreach (var (key, (midBoss, finalBoss)) in expected)
        {
            Assert.Equal(midBoss, GamePaths.PathsByKey[key].MidBoss);
            Assert.Equal(finalBoss, GamePaths.PathsByKey[key].FinalBoss);
        }
    }

    [Fact]
    public void Select_ThrowsForUnknownKey()
    {
        Assert.Throws<KeyNotFoundException>(() => GamePaths.Select("nonexistent"));
    }

    [Fact]
    public void Cycle_WrapsAroundInBothDirections()
    {
        GamePaths.Cycle(-1);
        Assert.Equal("phantasia", GamePaths.Selected().Key);
        GamePaths.Cycle(1);
        Assert.Equal("sound", GamePaths.Selected().Key);
    }

    [Fact]
    public void ActivateSelected_GeneratesBattlegroundMatchingThePath()
    {
        GamePaths.Select("touch");
        var battleground = GamePaths.ActivateSelected();

        Assert.Equal(22, battleground.WallHeight);
        Assert.True(GamePaths.IsTouch());
    }

    [Fact]
    public void BossKey_ReturnsMidOrFinalBossForTheActivePath()
    {
        GamePaths.Select("sight");
        GamePaths.ActivateSelected();

        Assert.Equal("ishe", GamePaths.BossKey(midpoint: true));
        Assert.Equal("chronos", GamePaths.BossKey(midpoint: false));
    }

    [Fact]
    public void IsTouch_OnlyTrueWhenTouchIsActive()
    {
        Assert.False(GamePaths.IsTouch());
        GamePaths.Select("touch");
        GamePaths.ActivateSelected();
        Assert.True(GamePaths.IsTouch());
    }
}
