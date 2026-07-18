using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

/// <summary>Ported from tests/test_keybinds.py.</summary>
[Collection("GameProfileState")]
public class KeybindsTests : IDisposable
{
    private readonly Dictionary<string, Keys?> _originalBindings;
    private readonly string _originalSavePath;
    private readonly GameProfileData _originalProfile;
    private readonly string _tempDir;

    public KeybindsTests()
    {
        _originalBindings = new Dictionary<string, Keys?>(Keybinds.Bindings);
        _originalSavePath = GameProfile.SavePath;
        _originalProfile = GameProfile.Profile;

        _tempDir = Directory.CreateTempSubdirectory("rotboi-keybinds-tests-").FullName;
        GameProfile.SavePath = Path.Combine(_tempDir, "profile.json");
        GameProfile.Profile = new GameProfileData();
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
    public void SetBinding_UpdatesTheAction()
    {
        Keybinds.SetBinding("dash", Keys.LeftShift);
        Assert.Equal(Keys.LeftShift, Keybinds.KeyFor("dash"));
    }

    [Fact]
    public void CameraReset_DefaultsToX()
    {
        Assert.Equal(Keys.X, Keybinds.ActionDefaults["camera_reset"]);
    }

    [Fact]
    public void SetBinding_ClearsAnyOtherActionUsingThatKey()
    {
        Keybinds.SetBinding("move_up", Keys.Up);
        Keybinds.SetBinding("dash", Keys.Up);
        Assert.Null(Keybinds.KeyFor("move_up"));
        Assert.Equal(Keys.Up, Keybinds.KeyFor("dash"));
    }

    [Fact]
    public void SetBinding_RefusesEscape()
    {
        var original = Keybinds.KeyFor("dash");
        Keybinds.SetBinding("dash", Keys.Escape);
        Assert.Equal(original, Keybinds.KeyFor("dash"));
    }

    [Fact]
    public void ClearBinding_UnbindsTheAction()
    {
        Keybinds.ClearBinding("autofire");
        Assert.Null(Keybinds.KeyFor("autofire"));
    }

    [Fact]
    public void Pressed_ReadsFromKeyPressedSet()
    {
        Keybinds.SetBinding("dash", Keys.LeftControl);
        var original = new HashSet<Keys>(InputState.KeysPressed);
        try
        {
            InputState.KeysPressed.Clear();
            InputState.KeysPressed.Add(Keys.LeftControl);
            Assert.True(Keybinds.Pressed("dash"));

            InputState.KeysPressed.Clear();
            Assert.False(Keybinds.Pressed("dash"));
        }
        finally
        {
            InputState.KeysPressed.Clear();
            foreach (var key in original) InputState.KeysPressed.Add(key);
        }
    }

    [Fact]
    public void Pressed_IsFalseWhenUnbound()
    {
        Keybinds.ClearBinding("dash");
        var original = new HashSet<Keys>(InputState.KeysPressed);
        try
        {
            InputState.KeysPressed.Clear();
            InputState.KeysPressed.Add(Keys.Space);
            Assert.False(Keybinds.Pressed("dash"));
        }
        finally
        {
            InputState.KeysPressed.Clear();
            foreach (var key in original) InputState.KeysPressed.Add(key);
        }
    }

    [Fact]
    public void Held_ReadsFromKeyboardState()
    {
        Keybinds.SetBinding("move_up", Keys.Up);
        var original = InputState.KeyboardState;
        try
        {
            InputState.KeyboardState = new KeyboardState(Keys.Up);
            Assert.True(Keybinds.Held("move_up"));
            Assert.False(Keybinds.Held("move_down"));
        }
        finally
        {
            InputState.KeyboardState = original;
        }
    }

    [Fact]
    public void Held_IsFalseWhenUnbound()
    {
        // Python's equivalent test also checks that indexing with None never
        // happens; that's a non-issue here since KeyFor() returns a typed
        // Keys? and Held() short-circuits on HasValue before touching
        // InputState at all.
        Keybinds.ClearBinding("move_up");
        Assert.False(Keybinds.Held("move_up"));
    }

    [Fact]
    public void SetBinding_PersistsToProfile()
    {
        Keybinds.SetBinding("dash", Keys.LeftShift);
        Assert.Equal((int)Keys.LeftShift, GameProfile.Profile.Keybinds["dash"]);
    }
}
