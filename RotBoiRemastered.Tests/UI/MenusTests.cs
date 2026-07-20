using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

/// <summary>
/// Ported from the keyboard-shortcut paths in menus.py's handle_pause/
/// handle_results (no existing Python tests to mirror). The mouse-click
/// paths (_activated checks against `_buttons`) need a prior Draw call
/// against a real GraphicsDevice to populate button rects, so those are
/// left to visual smoke testing, same as the rest of this port's UI layer.
/// </summary>
[Collection("GameProfileState")]
public class MenusTests : IDisposable
{
    private readonly Dictionary<string, Keys?> _originalBindings;
    private readonly string _originalSavePath;
    private readonly GameProfileData _originalProfile;
    private readonly string _tempDir;

    // GameProfile.SavePath now defaults to a real per-user AppData path
    // (see GameProfile.cs) rather than a working-directory-relative one, so
    // this class -- like KeybindsTests -- must redirect to a scratch file
    // and reset Profile/Bindings to defaults before every test. Without
    // this, a machine with a real saved profile.json (e.g. one where
    // "restart" was ever explicitly unbound) would make
    // HandlePause_RestartKeybind_ReturnsRestart fail by loading that real,
    // non-default state instead of Keybinds.ActionDefaults.
    public MenusTests()
    {
        _originalBindings = new Dictionary<string, Keys?>(Keybinds.Bindings);
        _originalSavePath = GameProfile.SavePath;
        _originalProfile = GameProfile.Profile;
        _tempDir = Directory.CreateTempSubdirectory("rotboi-menus-tests-").FullName;
        GameProfile.SavePath = Path.Combine(_tempDir, "profile.json");
        GameProfile.Profile = new GameProfileData();
        Keybinds.Bindings.Clear();
        foreach (var (actionId, defaultKey) in Keybinds.ActionDefaults)
            Keybinds.Bindings[actionId] = defaultKey;
    }

    public void Dispose()
    {
        Keybinds.Bindings.Clear();
        foreach (var (key, value) in _originalBindings)
            Keybinds.Bindings[key] = value;
        GameProfile.SavePath = _originalSavePath;
        GameProfile.Profile = _originalProfile;
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void HandlePause_Escape_ReturnsResume()
    {
        var menus = new Menus();
        var result = menus.HandlePause(new HashSet<Keys> { Keys.Escape }, Point.Zero, false, false);
        Assert.Equal(MenuAction.Resume, result);
    }

    [Fact]
    public void HandlePause_Q_ReturnsReturnToTitle()
    {
        var menus = new Menus();
        var result = menus.HandlePause(new HashSet<Keys> { Keys.Q }, Point.Zero, false, false);
        Assert.Equal(MenuAction.ReturnToTitle, result);
    }

    [Fact]
    public void HandlePause_RestartKeybind_ReturnsRestart()
    {
        var menus = new Menus();
        var restartKey = Keybinds.KeyFor("restart");
        Assert.NotNull(restartKey);

        var result = menus.HandlePause(new HashSet<Keys> { restartKey!.Value }, Point.Zero, false, false);

        Assert.Equal(MenuAction.Restart, result);
    }

    [Fact]
    public void HandlePause_NoRelevantInput_ReturnsNone()
    {
        var menus = new Menus();
        var result = menus.HandlePause(new HashSet<Keys>(), Point.Zero, false, false);
        Assert.Equal(MenuAction.None, result);
    }

    [Fact]
    public void ToggleQuickViewOption_PersistsSelection()
    {
        Assert.False(GameProfile.Profile.TabShowCosmetics);

        var result = Menus.ToggleQuickViewOption("TabShowCosmetics");

        Assert.True(result);
        Assert.True(GameProfile.Profile.TabShowCosmetics);
        Assert.True(File.Exists(GameProfile.SavePath));
    }

    [Fact]
    public void ToggleQuickViewOption_QuestScopesAreMutuallyExclusive()
    {
        Assert.True(Menus.ToggleQuickViewOption("TabShowActiveQuests"));
        Assert.True(GameProfile.Profile.TabShowActiveQuests);

        Assert.True(Menus.ToggleQuickViewOption("TabShowAllQuests"));

        Assert.False(GameProfile.Profile.TabShowActiveQuests);
        Assert.True(GameProfile.Profile.TabShowAllQuests);
    }

    [Fact]
    public void HandleResults_Enter_ReturnsRestart()
    {
        var menus = new Menus();
        var result = menus.HandleResults(new HashSet<Keys> { Keys.Enter }, Point.Zero, false);
        Assert.Equal(MenuAction.Restart, result);
    }

    [Fact]
    public void HandleResults_Escape_ReturnsReturnToTitle()
    {
        var menus = new Menus();
        var result = menus.HandleResults(new HashSet<Keys> { Keys.Escape }, Point.Zero, false);
        Assert.Equal(MenuAction.ReturnToTitle, result);
    }

    [Fact]
    public void HandleResults_NoInput_ReturnsNone()
    {
        var menus = new Menus();
        var result = menus.HandleResults(new HashSet<Keys>(), Point.Zero, false);
        Assert.Equal(MenuAction.None, result);
    }

    [Theory]
    [InlineData(-20, 0.75)]
    [InlineData(50, 1.875)]
    [InlineData(120, 3.0)]
    public void SliderValue_ClampsAndInterpolates(int mouseX, double expected)
    {
        double value = Menus.SliderValue(new Rectangle(0, 0, 100, 8), mouseX, .75, 3.0);
        Assert.Equal(expected, value, precision: 3);
    }
}
