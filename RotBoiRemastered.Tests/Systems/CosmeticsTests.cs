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
}
