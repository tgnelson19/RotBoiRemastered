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
        var text = new DamageText(0, 0, Color.White, 42, 20, lifetimeFrames: 4);
        Assert.False(text.DeleteMe);
        text.Update();
        Assert.False(text.DeleteMe); // redesigned text intentionally lingers
        for (int i = 0; i < 100 && !text.DeleteMe; i++)
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

    [Fact]
    public void Update_RisesOnlyShortDistanceAndOscillatesSideways()
    {
        var text = new DamageText(0, 0, Color.White, 42, 40, lifetimeFrames: 60);
        float firstX = text.VisualOffset.X;
        for (int i = 0; i < 20; i++) text.Update();

        Assert.InRange(text.VisualOffset.Y, -22f, -4f);
        Assert.NotEqual(firstX, text.VisualOffset.X);
        Assert.InRange(Math.Abs(text.VisualOffset.X), 0, 6f);
    }
}
