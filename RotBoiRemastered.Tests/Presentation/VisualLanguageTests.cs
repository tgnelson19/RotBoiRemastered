using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Presentation;

public sealed class VisualLanguageTests
{
    [Fact]
    public void RegistryCoversEveryPathRoomFamilyAndTier()
    {
        Assert.Equal(5, SoulVisualLanguage.Paths.Count);
        Assert.Equal(Enum.GetValues<PathRoomType>().Length,
            SoulVisualLanguage.RoomRoles.Count);

        foreach (string path in SoulVisualLanguage.Paths.Keys)
        foreach (string family in SoulVisualLanguage.EnemyFamilies)
        foreach (string tier in SoulVisualLanguage.EnemyTiers)
        {
            EnemyVisualProfile profile =
                SoulVisualLanguage.Enemy(path, family, tier);
            Assert.Equal(path, profile.PathKey);
            Assert.Equal(family, profile.Family);
            Assert.Equal(tier, profile.Tier);
            Assert.InRange(profile.ConstructionModules, 1, 3);
            Assert.False(string.IsNullOrWhiteSpace(profile.Anchors.RoleKey));
        }
    }

    [Fact]
    public void RegistryCoversEveryRuntimeCatalogFamily()
    {
        string?[] contentPaths =
        {
            null, "sound", "touch", "sight", "chemesthesis", "phantasia",
        };
        var runtimeFamilies = contentPaths
            .SelectMany(path => Enumerable.Range(0, 21)
                .SelectMany(level =>
                    EnemyCatalog.Shared.Available(level, path)))
            .Select(definition => definition.Family)
            .Append("child")
            .Distinct(StringComparer.Ordinal);

        Assert.All(runtimeFamilies, family =>
            Assert.Contains(family, SoulVisualLanguage.EnemyFamilies));
    }

    [Fact]
    public void VisualProfileLookupDoesNotMutateGameplayState()
    {
        var enemy = new Enemy(
            12, 34, 2.5f, 26, Color.Red,
            damage: 7, hp: 80, expValue: 11,
            difficulty: 2, awarenessRange: 300,
            difficultyTier: "medium",
            rng: new Random(4))
        {
            Family = "shotgunner",
            ContentPath = "touch",
        };
        var before = (
            enemy.WorldX, enemy.WorldY, enemy.Speed, enemy.Size,
            enemy.Damage, enemy.Hp, enemy.MaxHp, enemy.ExpValue,
            enemy.AwarenessRange);

        EnemyVisualProfile profile = SoulVisualLanguage.Enemy(
            enemy.ContentPath, enemy.Family, enemy.DifficultyTier);

        Assert.Equal("touch", profile.PathKey);
        Assert.Equal("vents", profile.Anchors.RoleKey);
        Assert.Equal(before, (
            enemy.WorldX, enemy.WorldY, enemy.Speed, enemy.Size,
            enemy.Damage, enemy.Hp, enemy.MaxHp, enemy.ExpValue,
            enemy.AwarenessRange));
    }

    [Theory]
    [InlineData(false, false, 3, 3, RoomPresentationState.Dormant)]
    [InlineData(true, false, .5, 3, RoomPresentationState.Awakening)]
    [InlineData(true, false, 2, 3, RoomPresentationState.Combat)]
    [InlineData(true, true, 2, .5, RoomPresentationState.Release)]
    [InlineData(true, true, 2, 2, RoomPresentationState.Residual)]
    public void RoomStateDerivationIsDeterministic(
        bool activated,
        bool cleared,
        float entered,
        float clearedFor,
        RoomPresentationState expected)
    {
        Assert.Equal(expected, SoulVisualLanguage.DeriveRoomState(
            activated, cleared, entered, clearedFor));
    }

    [Fact]
    public void HostileCueRetainsUniversalTrim()
    {
        foreach (PathVisualProfile path in SoulVisualLanguage.Paths.Values)
        {
            Assert.Equal(new Color(214, 78, 74),
                SoulVisualLanguage.CueColor(
                    VisualSemanticCue.Hostile, path));
            Assert.Equal(Color.White,
                SoulVisualLanguage.CueColor(
                    VisualSemanticCue.HostileIgnition, path, highContrast: true));
        }
    }

    [Fact]
    public void AdaptiveDirectorPreservesUserZeroAndSuppressesBusyScenes()
    {
        VisualDensity zero = VisualDensityDirector.Calculate(
            0, 200, 1000, 1, bossActive: true);
        Assert.Equal(0, zero.Optional);

        VisualDensity calm = VisualDensityDirector.Calculate(
            1, 2, 4, 0, bossActive: false);
        VisualDensity busy = VisualDensityDirector.Calculate(
            1, 200, 1000, 1, bossActive: true);
        Assert.Equal(1, calm.EffectiveIntensity);
        Assert.True(busy.EffectiveIntensity < calm.EffectiveIntensity);
        Assert.True(busy.EffectiveIntensity >= .22f);
    }

    [Fact]
    public void EssentialRecipesStayIdentifiable()
    {
        Assert.True(SoulVisualLanguage.VfxRecipes["impact"].Essential);
        Assert.True(SoulVisualLanguage.VfxRecipes["critical"].Essential);
        Assert.True(SoulVisualLanguage.VfxRecipes["shield"].Essential);
        Assert.False(SoulVisualLanguage.VfxRecipes["death"].Essential);
    }

    [Fact]
    public void PresentationClockOnlyMovesWhenExplicitlyAdvanced()
    {
        var clock = new PresentationClock();
        Assert.Equal(0, clock.Seconds);
        clock.Advance(.02);
        float advanced = clock.Seconds;
        Assert.True(advanced > 0);
        Assert.Equal(advanced, clock.Seconds);
        clock.Advance(4);
        Assert.Equal(advanced + .05f, clock.Seconds, 4);
        clock.Reset();
        Assert.Equal(0, clock.Seconds);
    }
}
