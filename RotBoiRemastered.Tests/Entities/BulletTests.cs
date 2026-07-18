using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bullet.py's movement/removal-flag behavior.</summary>
public class BulletTests
{
    [Fact]
    public void Update_AdvancesAlongDirection()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var bullet = new Bullet(100, 125, speed: 4, direction: 0f, bulletRange: 500, size: 10,
            color: Color.Gray, pierce: 1, damage: 100, isCritical: false);
        float startX = bullet.WorldX;
        bullet.Update(battleground);
        Assert.True(bullet.WorldX > startX); // direction 0 -> +cos(0) -> moves +x
    }

    [Fact]
    public void Update_FlagsForRemoval_WhenRangeExhausted()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var bullet = new Bullet(100, 125, speed: 4, direction: 0f, bulletRange: 1, size: 10,
            color: Color.Gray, pierce: 1, damage: 100, isCritical: false);
        Assert.False(bullet.RemFlag);
        bullet.Update(battleground);
        Assert.True(bullet.RemFlag);
    }

    [Fact]
    public void Update_FlagsForRemoval_OnWallHit()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        // Near the right wall (x=200 is the wall column), heading straight into it.
        var bullet = new Bullet(195, 125, speed: 4, direction: 0f, bulletRange: 5000, size: 10,
            color: Color.Gray, pierce: 1, damage: 100, isCritical: false);
        for (int i = 0; i < 5 && !bullet.RemFlag; i++)
            bullet.Update(battleground);
        Assert.True(bullet.RemFlag);
    }
}
