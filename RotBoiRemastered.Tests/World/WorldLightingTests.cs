using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

public sealed class WorldLightingTests
{
    [Fact]
    public void ThemesAreDarkButKeepDistinctPathColoredLight()
    {
        var expectedAlpha = new Dictionary<string, byte>
        {
            ["sound"] = 145,
            ["touch"] = 158,
            ["sight"] = 138,
            ["phantasia"] = 154,
            ["chemesthesis"] = 151,
        };
        var themes = GamePaths.Paths.ToDictionary(
            path => path.Key,
            path => WorldLighting.ThemeFor(path.Key));

        Assert.All(themes, pair =>
        {
            LightingTheme theme = pair.Value;
            Assert.Equal(expectedAlpha[pair.Key], theme.DarknessAlpha);
            Assert.InRange(theme.PlayerRadiusTiles, 3.7f, 4.3f);
            Assert.InRange(theme.FixtureRadiusTiles, 5.3f, 6.1f);
        });
        Assert.Equal(GamePaths.Paths.Count,
            themes.Values.Select(theme => theme.Glow).Distinct().Count());
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
        List<ArenaLightPost> repeated =
            WorldLighting.BuildArenaLightPosts(battleground);

        Assert.InRange(posts.Count, 12, 80);
        Assert.Equal(posts, repeated);
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
        WorldLightSource postLight = WorldLighting.SourceFor(
            posts[0], WorldLighting.ThemeFor(pathKey), pathKey);
        Assert.Equal(WorldLighting.StyleForPath(pathKey), postLight.MotionStyle);
        Assert.True(postLight.Radius >= Battleground.TileSize * 5.3f);

        for (int y = 2; y < battleground.Height - 2; y++)
        {
            for (int x = 2; x < battleground.Width - 2; x++)
            {
                if (battleground.TileAt(x, y).IsSolid())
                    continue;
                var tileCenter = new Microsoft.Xna.Framework.Vector2(
                    (x + .5f) * Battleground.TileSize,
                    (y + .5f) * Battleground.TileSize);
                float nearestTiles = posts.Min(post =>
                    Microsoft.Xna.Framework.Vector2.Distance(
                        tileCenter, post.WorldPosition) / Battleground.TileSize);
                Assert.InRange(nearestTiles, 0f, 14f);
            }
        }
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
            Assert.True(light.Radius >= Battleground.TileSize * 5.3f);
            Assert.InRange(light.Intensity, .5f, .9f);
            Assert.Contains(layout.Decorations, decoration =>
                decoration.WorldPosition == light.WorldPosition
                && decoration.Layer == PathDecorationLayer.Raised
                && WorldLighting.IsLuminousDecoration(decoration.Kind)
                && light.MotionStyle == WorldLighting.StyleForDecoration(decoration.Kind));
        });
    }

    [Theory]
    [InlineData(LightMotionStyle.Touch)]
    [InlineData(LightMotionStyle.Sight)]
    [InlineData(LightMotionStyle.Sound)]
    [InlineData(LightMotionStyle.Phantasia)]
    [InlineData(LightMotionStyle.Chemesthesis)]
    public void MotionCurveIsContinuousAndBounded(LightMotionStyle style)
    {
        LightAnimationSample previous = WorldLighting.SampleMotion(
            10f, 2.7f, style, 1f);
        for (int index = 1; index <= 1200; index++)
        {
            LightAnimationSample current = WorldLighting.SampleMotion(
                10f + index / 120f, 2.7f, style, 1f);
            Assert.InRange(current.Intensity, .88f, 1.07f);
            Assert.InRange(current.Radius, .97f, 1.03f);
            Assert.InRange(current.Halo, .88f, 1.12f);
            Assert.InRange(current.VerticalDrift, -1.8f, 1.8f);
            Assert.InRange(MathF.Abs(current.Intensity - previous.Intensity), 0f, .003f);
            Assert.InRange(MathF.Abs(current.Radius - previous.Radius), 0f, .001f);
            Assert.InRange(MathF.Abs(current.Halo - previous.Halo), 0f, .003f);
            Assert.InRange(MathF.Abs(current.VerticalDrift - previous.VerticalDrift), 0f, .02f);
            previous = current;
        }
    }

    [Fact]
    public void MotionSamplingIsDeterministicSeededAndNeutralAtZeroStrength()
    {
        LightAnimationSample sample = WorldLighting.SampleMotion(
            17.25f, 3.4f, LightMotionStyle.Phantasia, .7f);

        Assert.Equal(sample, WorldLighting.SampleMotion(
            17.25f, 3.4f, LightMotionStyle.Phantasia, .7f));
        Assert.NotEqual(sample, WorldLighting.SampleMotion(
            17.25f, 5.1f, LightMotionStyle.Phantasia, .7f));
        Assert.Equal(new LightAnimationSample(1f, 1f, 1f, 0f),
            WorldLighting.SampleMotion(
                17.25f, 3.4f, LightMotionStyle.Phantasia, 0f));
    }

    [Theory]
    [InlineData(PathDecorationKind.Valve, LightMotionStyle.Touch)]
    [InlineData(PathDecorationKind.Pump, LightMotionStyle.Touch)]
    [InlineData(PathDecorationKind.PressureTank, LightMotionStyle.Touch)]
    [InlineData(PathDecorationKind.LensBuoy, LightMotionStyle.Sight)]
    [InlineData(PathDecorationKind.MirrorArch, LightMotionStyle.Sight)]
    [InlineData(PathDecorationKind.LightningRod, LightMotionStyle.Sight)]
    [InlineData(PathDecorationKind.EchoPylon, LightMotionStyle.Sound)]
    [InlineData(PathDecorationKind.Chime, LightMotionStyle.Sound)]
    [InlineData(PathDecorationKind.OrganStack, LightMotionStyle.Sound)]
    [InlineData(PathDecorationKind.PrismObelisk, LightMotionStyle.Phantasia)]
    [InlineData(PathDecorationKind.OrbitShrine, LightMotionStyle.Phantasia)]
    [InlineData(PathDecorationKind.LanternSpire, LightMotionStyle.Chemesthesis)]
    [InlineData(PathDecorationKind.FurnaceIdol, LightMotionStyle.Chemesthesis)]
    public void LuminousDecorationsUseTheirPathMotionStyle(
        PathDecorationKind kind,
        LightMotionStyle expected)
    {
        Assert.True(WorldLighting.IsLuminousDecoration(kind));
        Assert.Equal(expected, WorldLighting.StyleForDecoration(kind));
    }
}
