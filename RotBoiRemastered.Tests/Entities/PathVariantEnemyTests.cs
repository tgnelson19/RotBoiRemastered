using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

public class PathVariantEnemyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> FamiliesByPath =
        new Dictionary<string, string[]>
        {
            ["sound"] = new[] { "sound_echoer", "sound_resonator" },
            ["touch"] = new[] { "touch_clasper", "touch_mirekeeper" },
            ["sight"] = new[] { "sight_blinker", "sight_lens" },
            ["chemesthesis"] = new[] { "chem_cinderpod", "chem_sporecaster" },
            ["phantasia"] = new[] { "phantasia_mirage", "phantasia_dreamweaver" },
        };

    [Fact]
    public void EveryPath_HasTwoExclusiveFamilies_WithThreeRunTiers()
    {
        foreach (var (path, expectedFamilies) in FamiliesByPath)
        {
            var pathDefinitions = Enumerable.Range(0, 21)
                .SelectMany(level => EnemyCatalog.Shared.Available(level, path))
                .Where(definition => definition.SpawnPath is not null)
                .DistinctBy(definition => definition.Key)
                .ToList();

            Assert.Equal(expectedFamilies.Order(), pathDefinitions.Select(d => d.Family).Distinct().Order());
            foreach (string family in expectedFamilies)
            {
                var tiers = pathDefinitions.Where(d => d.Family == family).ToList();
                Assert.Equal(3, tiers.Count);
                Assert.Equal(new[] { "easy", "hard", "medium" }, tiers.Select(d => d.ProgressionTier).Order());
                Assert.All(tiers, definition => Assert.Equal(path, definition.SpawnPath));
            }

            var foreignFamilies = FamiliesByPath
                .Where(pair => pair.Key != path)
                .SelectMany(pair => pair.Value)
                .ToHashSet();
            Assert.DoesNotContain(pathDefinitions, definition => foreignFamilies.Contains(definition.Family));
        }
    }

    [Theory]
    [InlineData("sound_echoer_hard", 4, "sine")]
    [InlineData("sound_resonator_hard", 10, "linear")]
    [InlineData("touch_clasper_hard", 3, "bank")]
    [InlineData("touch_mirekeeper_hard", 3, "pool")]
    [InlineData("sight_blinker_hard", 5, "linear")]
    [InlineData("sight_lens_hard", 2, "laser")]
    [InlineData("chem_cinderpod_hard", 4, "mine")]
    [InlineData("chem_sporecaster_hard", 2, "sine")]
    [InlineData("phantasia_mirage_hard", 9, "linear")]
    [InlineData("phantasia_dreamweaver_hard", 5, "orbit")]
    public void StrongVariant_FiresItsDistinctPattern(string key, int projectileCount, string path)
    {
        var enemy = Assert.IsType<PathVariantEnemy>(
            EnemyCatalog.Shared.Create(key, 100, 100, level: 12, awarenessRange: 500f, rng: new Random(7)));
        enemy.AttackCooldown = 0;
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = 135,
            PlayerWorldY = 135,
            Battleground = EntityTestFixtures.SmallOpenRoom(),
        };

        enemy.Update(context);

        Assert.Equal(projectileCount, context.ProjectileSink.Count);
        Assert.All(context.ProjectileSink, projectile => Assert.Equal(path, projectile.Path));
    }

    [Fact]
    public void Mirage_MixesTruthMarkedThreatsWithHarmlessIllusions()
    {
        var enemy = Assert.IsType<PathVariantEnemy>(
            EnemyCatalog.Shared.Create("phantasia_mirage_hard", 100, 100, 12, 500f, new Random(8)));
        enemy.AttackCooldown = 0;
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = 135,
            PlayerWorldY = 135,
            Battleground = EntityTestFixtures.SmallOpenRoom(),
        };

        enemy.Update(context);

        Assert.Contains(context.ProjectileSink, projectile => projectile.TruthMarked && !projectile.Illusory);
        Assert.Contains(context.ProjectileSink, projectile => projectile.Illusory && !projectile.TruthMarked);
    }

    [Fact]
    public void Sporecaster_StrongSporesSplitAndReproduceOnce()
    {
        var enemy = Assert.IsType<PathVariantEnemy>(
            EnemyCatalog.Shared.Create("chem_sporecaster_hard", 100, 100, 12, 500f, new Random(9)));
        enemy.AttackCooldown = 0;
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = 135,
            PlayerWorldY = 135,
            Battleground = EntityTestFixtures.SmallOpenRoom(),
        };

        enemy.Update(context);

        Assert.All(context.ProjectileSink, projectile =>
        {
            Assert.Equal(4, projectile.SplitCount);
            Assert.Equal(1, projectile.SplitGeneration);
        });
    }
}
