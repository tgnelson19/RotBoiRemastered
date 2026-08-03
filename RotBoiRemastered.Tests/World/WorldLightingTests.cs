using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

public sealed class WorldLightingTests
{
    [Fact]
    public void ThemesAreDarkButKeepDistinctPathColoredLight()
    {
        var themes = GamePaths.Paths
            .Select(path => WorldLighting.ThemeFor(path.Key))
            .ToList();

        Assert.All(themes, theme =>
        {
            Assert.InRange(theme.DarknessAlpha, (byte)90, (byte)140);
            Assert.True(theme.PlayerRadiusTiles >= 4.5f);
            Assert.True(theme.FixtureRadiusTiles >= 4.4f);
        });
        Assert.Equal(GamePaths.Paths.Count,
            themes.Select(theme => theme.Glow).Distinct().Count());
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("phantasia")]
    [InlineData("chemesthesis")]
    public void StandaloneArenasReceiveWalkableLightPosts(string pathKey)
    {
        Battleground battleground = Battleground.CreateForPath(pathKey);

        List<ArenaLightPost> posts =
            WorldLighting.BuildArenaLightPosts(battleground);

        Assert.InRange(posts.Count, 12, 80);
        Assert.All(posts, post =>
        {
            int x = (int)(post.WorldPosition.X / Battleground.TileSize);
            int y = (int)(post.WorldPosition.Y / Battleground.TileSize);
            Assert.InRange(x, 2, battleground.Width - 3);
            Assert.InRange(y, 2, battleground.Height - 3);
            Assert.False(battleground.TileAt(x, y).IsSolid());
            Assert.Equal(battleground.BiomeForTile(x, y), post.Biome);
        });
        Assert.Equal(posts.Count,
            posts.Select(post => post.WorldPosition).Distinct().Count());
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("phantasia")]
    [InlineData("chemesthesis")]
    public void CompositePathsLightTheirAuthoredRaisedLandmarks(string pathKey)
    {
        PathFloorLayout layout = PathFloorGenerator.Generate(
            pathKey, 4, new Random(913));

        List<WorldLightSource> lights =
            WorldLighting.BuildPathLightSources(
                layout.Battleground, pathKey);

        Assert.NotEmpty(lights);
        Assert.All(lights, light =>
        {
            Assert.True(light.Radius >= Battleground.TileSize * 4.4f);
            Assert.InRange(light.Intensity, .5f, .9f);
            Assert.Contains(layout.Decorations, decoration =>
                decoration.WorldPosition == light.WorldPosition
                && decoration.Layer == PathDecorationLayer.Raised
                && WorldLighting.IsLuminousDecoration(decoration.Kind));
        });
    }

    [Fact]
    public void FlickerCurveIsContinuousAndBounded()
    {
        float previous = WorldLighting.Flicker(10f, 2.7f);
        for (int index = 1; index <= 100; index++)
        {
            float current = WorldLighting.Flicker(
                10f + index * .0001f, 2.7f);
            Assert.InRange(current, .84f, 1f);
            Assert.InRange(MathF.Abs(current - previous), 0f, .001f);
            previous = current;
        }
    }
}
