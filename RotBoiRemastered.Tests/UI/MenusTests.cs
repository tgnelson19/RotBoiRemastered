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
public class MenusTests
{
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
}
