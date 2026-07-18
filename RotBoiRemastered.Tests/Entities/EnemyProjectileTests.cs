using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyProjectile.py's path-driven movement, expiry, and splitting behavior.</summary>
public class EnemyProjectileTests
{
    [Fact]
    public void Linear_AdvancesAndConsumesRange()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(100, 125, direction: 0f, speed: 4, damage: 10, size: 10, travelRange: 5);
        float startX = projectile.WorldX;
        projectile.Update(battleground, casualMode: false);
        Assert.True(projectile.WorldX > startX);
    }

    [Fact]
    public void Linear_FlagsForRemoval_WhenRangeExhausted()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(100, 125, direction: 0f, speed: 4, damage: 10, size: 10, travelRange: 0.01f);
        projectile.Update(battleground, casualMode: false);
        Assert.True(projectile.RemFlag);
    }

    [Fact]
    public void Sine_OscillatesAroundTheStraightLinePath()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(100, 125, direction: 0f, speed: 4, damage: 10, size: 10,
            travelRange: 5000, path: "sine", amplitude: 20f, frequency: .1f);
        for (int i = 0; i < 5; i++)
            projectile.Update(battleground, casualMode: false);
        // Sine path drifts off the pure-horizontal line the "linear" path would follow.
        Assert.NotEqual(125f, projectile.WorldY);
    }

    [Fact]
    public void Orbit_CirclesAroundCenter_WithoutConsumingRange()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var center = new Vector2(125, 125);
        var projectile = new EnemyProjectile(175, 125, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "orbit", orbitCenter: center, orbitRadius: 50, orbitAngle: 0, angularSpeed: 1f);
        float initialRange = projectile.RemainingRange;
        for (int i = 0; i < 10; i++)
            projectile.Update(battleground, casualMode: false);
        Assert.False(projectile.RemFlag);
        Assert.Equal(initialRange, projectile.RemainingRange);
    }

    [Fact]
    public void Bomb_ExplodesAfterFuse_AndSpawnsBurstChildren()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(125, 125, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "bomb");
        projectile.FuseDuration = 0f;
        projectile.Update(battleground, casualMode: false); // age 0 >= fuse 0 -> explodes and bursts
        Assert.True(projectile.Exploded);
        Assert.Equal(projectile.BurstCount, projectile.SpawnedProjectiles.Count);
    }

    [Fact]
    public void Laser_StaysCollisionFree_DuringTelegraph()
    {
        var projectile = new EnemyProjectile(100, 100, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 500);
        Assert.False(projectile.Collides(new Rectangle(100, 100, 10, 10)));
    }

    [Fact]
    public void Split_SpawnsFannedChildren_AtSplitDistance()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(100, 125, direction: 0f, speed: 20, damage: 10, size: 10,
            travelRange: 5000)
        {
            SplitCount = 3,
            SplitAt = 0.01f,
        };
        projectile.Update(battleground, casualMode: false);
        Assert.True(projectile.RemFlag);
        Assert.Equal(3, projectile.SpawnedProjectiles.Count);
    }

    [Fact]
    public void Collides_Default_UsesWorldRectOverlap()
    {
        var projectile = new EnemyProjectile(100, 100, direction: 0f, speed: 0, damage: 10, size: 20);
        Assert.True(projectile.Collides(new Rectangle(90, 90, 30, 30)));
        Assert.False(projectile.Collides(new Rectangle(500, 500, 10, 10)));
    }

    [Fact]
    public void Illusory_NeverCollides()
    {
        var projectile = new EnemyProjectile(100, 100, direction: 0f, speed: 0, damage: 10, size: 20)
        {
            Illusory = true,
        };
        Assert.False(projectile.Collides(new Rectangle(90, 90, 30, 30)));
    }

    [Fact]
    public void BossOwner_ScalesDamageByBossScale()
    {
        var boss = new EnemyProjectile(0, 0, 0f, 1, damage: 1, size: 10, owner: "beaudis_shot");
        var normal = new EnemyProjectile(0, 0, 0f, 1, damage: 1, size: 10, owner: "regular_enemy");
        Assert.Equal(100, boss.Damage);
        Assert.Equal(1, normal.Damage);
    }
}
