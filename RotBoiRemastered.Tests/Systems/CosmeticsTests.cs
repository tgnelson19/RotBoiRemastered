using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

[Collection("GameProfileState")]
public sealed class CosmeticsTests : IDisposable
{
    private readonly GameProfileData _originalProfile = GameProfile.Profile;
    private readonly string _originalSavePath = GameProfile.SavePath;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("rotboi-cosmetics-tests-").FullName;

    public CosmeticsTests()
    {
        GameProfile.Profile = new GameProfileData();
        GameProfile.SavePath = Path.Combine(_tempDir, "profile.json");
    }

    public void Dispose()
    {
        GameProfile.Profile = _originalProfile;
        GameProfile.SavePath = _originalSavePath;
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Defaults_UseReferenceBulbProjectile()
    {
        Assert.Equal("reference", Cosmetics.SelectedProjectile.Id);
        Assert.Equal("bulb", Cosmetics.SelectedDesign.Id);
        Assert.Equal(17, Cosmetics.ProjectileDesigns.Count);
        Assert.Contains(Cosmetics.ProjectileDesigns, design => design.Id == "prism");
        Assert.Contains(Cosmetics.ProjectileDesigns, design => design.Id == "sigil");
    }

    [Fact]
    public void Selection_PersistsAndAppliesToRunState()
    {
        Assert.True(Cosmetics.Select("core", "emerald"));
        Assert.True(Cosmetics.Select("edge", "gold"));
        Assert.True(Cosmetics.Select("projectile", "arcane"));
        Assert.True(Cosmetics.Select("design", "lance"));

        var reloaded = GameProfile.LoadProfile();
        Assert.Equal("emerald", reloaded.PlayerCoreColor);
        Assert.Equal("lance", reloaded.ProjectileDesign);

        var state = new RunState();
        Assert.Equal(Cosmetics.SelectedCore.Color, state.PlayerColor);
        Assert.Equal(Cosmetics.SelectedEdge.Color, state.PlayerEdgeColor);
        Assert.Equal(Cosmetics.SelectedProjectile.Core, state.BulletColor);
        Assert.Equal("lance", state.BulletDesign);
    }

    [Fact]
    public void Selection_RejectsUnknownOptions()
    {
        Assert.False(Cosmetics.Select("design", "not-a-design"));
        Assert.Equal("bulb", GameProfile.Profile.ProjectileDesign);
    }

    [Fact]
    public void LaunchCatalogEntries_AreAlwaysUnlockedOnAFreshProfile()
    {
        Assert.True(Cosmetics.IsUnlocked("core", "emerald"));
        Assert.True(Cosmetics.IsUnlocked("edge", "gold"));
        Assert.True(Cosmetics.IsUnlocked("projectile", "arcane"));
        Assert.True(Cosmetics.IsUnlocked("design", "lance"));
    }

    [Fact]
    public void GatedCosmetic_IsLockedUntilItsConditionIsMet_ThenSelectable()
    {
        Assert.False(Cosmetics.IsUnlocked("core", "coral"));
        Assert.False(Cosmetics.Select("core", "coral"));
        Assert.Equal("Extract from one run.", Cosmetics.LockDescription("core", "coral"));

        GameProfile.Profile.QuestProgress["runs_extracted"] = 1;

        Assert.True(Cosmetics.IsUnlocked("core", "coral"));
        Assert.True(Cosmetics.Select("core", "coral"));
        Assert.Equal("coral", GameProfile.Profile.PlayerCoreColor);
    }

    [Fact]
    public void TierThreeCosmetics_HideTheirUnlockHintAsQuestionMarks()
    {
        Assert.Equal(Cosmetics.LockedHint, Cosmetics.LockDescription("core", "voidbloom"));
        Assert.Equal(Cosmetics.LockedHint, Cosmetics.LockDescription("design", "halo"));
        Assert.False(Cosmetics.IsUnlocked("core", "voidbloom"));
        Assert.False(Cosmetics.IsUnlocked("design", "halo"));

        GameProfile.Profile.DefeatedCoreOfTheVoid = true;

        Assert.True(Cosmetics.IsUnlocked("core", "voidbloom"));
        Assert.True(Cosmetics.IsUnlocked("design", "halo"));
    }

    [Fact]
    public void GrandfatheredSelection_StaysUnlockedEvenIfItsConditionIsNeverMet()
    {
        GameProfile.Profile.UnlockedCosmetics.Add("core:coral");
        Assert.True(Cosmetics.IsUnlocked("core", "coral"));
        Assert.True(Cosmetics.Select("core", "coral"));
    }
}
