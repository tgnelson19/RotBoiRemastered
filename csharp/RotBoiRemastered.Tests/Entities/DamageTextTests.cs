using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// Ported from the countdown/expiry behavior in damageText.py -- there's no
/// dedicated Python test file for it, so this is new coverage enabled by
/// splitting Update out of the combined drawAndUpdateDamageText.
/// </summary>
public class DamageTextTests
{
    [Fact]
    public void Update_FlagsForDeletion_OnceLifetimeExpires()
    {
        // lifetimeFrames=4 -> Lifetime starts at 2; GetFrameScale() defaults to
        // a fixed 2.0 before Simulation.SetDeltaTime is ever called.
        var text = new DamageText(0, 0, Color.White, 42, 20, lifetimeFrames: 4);
        Assert.False(text.DeleteMe);
        text.Update();
        Assert.True(text.DeleteMe);
    }

    [Fact]
    public void Update_AcceptsStringValues_LikeMissOrBlock()
    {
        var text = new DamageText(0, 0, Color.White, "MISS", 20, lifetimeFrames: 20);
        text.Update();
        Assert.False(text.DeleteMe);
    }
}
