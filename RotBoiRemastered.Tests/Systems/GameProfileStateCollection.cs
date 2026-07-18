namespace RotBoiRemastered.Tests.Systems;

/// <summary>
/// GameProfile.Profile/SavePath are shared mutable static state (mirroring
/// gameProfile.py's module-level globals). xUnit runs different test classes
/// in parallel by default, which races on that shared state -- Python's
/// unittest runner has no such concern since it runs sequentially. Any test
/// class that reads or writes GameProfile.Profile/SavePath (directly, or
/// indirectly through Keybinds) must opt into this collection so those tests
/// never run concurrently with each other.
/// </summary>
[CollectionDefinition("GameProfileState")]
public class GameProfileStateCollection
{
}
