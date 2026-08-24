using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// Covers the new laser-report primitives added to EnemyProjectile: proximity
/// mines, homing, arena-boundary bounce, breathing pools, tethered pairs, and
/// a per-instance laser sprout duration.
/// </summary>
public class EnemyProjectileNewPrimitivesTests
{
    private static readonly Vector2 FarAway = new(100_000, 100_000);

    [Fact]
    public void ProximityMine_StaysUncollidable_UntilPlayerEntersRadius()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var mine = new EnemyProjectile(125, 125, 0f, 0f, 10, 10, path: "mine")
        {
            ProximityRadius = 50f,
            TelegraphDuration = .1f,
        };
        var rect = mine.WorldRect();
        rect.Inflate(20, 20);

        // Player far away: still dormant even once enough time has passed
        // that an ordinary mine would already be armed.
        for (int i = 0; i < 10; i++)
            mine.Update(battleground, casualMode: false, playerWorldPosition: FarAway);
        Assert.False(mine.Collides(rect));
    }

    [Fact]
    public void ProximityMine_Arms_OnceTriggeredAndTelegraphed()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var mine = new EnemyProjectile(125, 125, 0f, 0f, 10, 10, path: "mine")
        {
            ProximityRadius = 50f,
            TelegraphDuration = .05f,
        };
        var rect = mine.WorldRect();
        rect.Inflate(20, 20);
        var nearbyPlayer = new Vector2(130, 130);

        for (int i = 0; i < 10; i++)
            mine.Update(battleground, casualMode: false, playerWorldPosition: nearbyPlayer);
        Assert.True(mine.Collides(rect));
    }

    [Fact]
    public void ProximityMine_ZeroRadius_BehavesLikeAnOrdinaryMine()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var mine = new EnemyProjectile(125, 125, 0f, 0f, 10, 10, path: "mine")
        {
            TelegraphDuration = .05f,
        };
        var rect = mine.WorldRect();
        rect.Inflate(20, 20);

        for (int i = 0; i < 10; i++)
            mine.Update(battleground, casualMode: false); // no player position at all
        Assert.True(mine.Collides(rect));
    }

    [Fact]
    public void Homing_TurnsHeadingTowardThePlayer()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        // Spawns aimed straight along +X; the player is directly "north"
        // (+Y in screen space), so a homing shot should rotate that way.
        var projectile = new EnemyProjectile(125, 125, direction: 0f, speed: 4, damage: 10, size: 10,
            travelRange: 5000)
        {
            HomingTurnRate = 3f,
        };
        var player = new Vector2(125, 225);
        for (int i = 0; i < 5; i++)
            projectile.Update(battleground, casualMode: false, playerWorldPosition: player);
        Assert.True(projectile.Direction > 0.05f);
    }

    [Fact]
    public void Homing_WithoutPlayerPosition_HoldsItsSpawnHeading()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(125, 125, direction: 0f, speed: 4, damage: 10, size: 10,
            travelRange: 5000)
        {
            HomingTurnRate = 3f,
        };
        for (int i = 0; i < 5; i++)
            projectile.Update(battleground, casualMode: false);
        Assert.Equal(0f, projectile.Direction);
    }

    [Fact]
    public void Bounce_ReflectsOffTheBoundary_InsteadOfEscaping()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var center = new Vector2(200, 200);
        var projectile = new EnemyProjectile(200, 200, direction: 0f, speed: 40, damage: 10, size: 10,
            travelRange: 100_000, path: "bounce", ignoreWalls: true)
        {
            BounceCenter = center,
            BounceRadius = 60f,
            BouncesRemaining = 500,
        };
        for (int i = 0; i < 60; i++)
            projectile.Update(battleground, casualMode: false);
        Assert.False(projectile.RemFlag);
        // A shot that only ever bounces stays within a small margin of the
        // boundary radius rather than drifting arbitrarily far away.
        float distance = Vector2.Distance(projectile.Center(), center);
        Assert.True(distance <= 70f, $"expected shot to stay near the boundary, was {distance} from center");
    }

    [Fact]
    public void Bounce_WithNoBouncesRemaining_FliesThroughUnaffected()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var center = new Vector2(200, 200);
        var projectile = new EnemyProjectile(200, 200, direction: 0f, speed: 40, damage: 10, size: 10,
            travelRange: 100_000, path: "bounce", ignoreWalls: true)
        {
            BounceCenter = center,
            BounceRadius = 60f,
            BouncesRemaining = 0,
        };
        for (int i = 0; i < 10; i++)
            projectile.Update(battleground, casualMode: false);
        float distance = Vector2.Distance(projectile.Center(), center);
        Assert.True(distance > 60f);
    }

    [Fact]
    public void BreathingPool_GrowsBeyondItsBaseRadius_AtQuarterPeriod()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var pool = new EnemyProjectile(125, 125, 0f, 0f, 10, 100, path: "pool")
        {
            TelegraphDuration = 0f,
            PoolPulseAmplitude = .3f,
            PoolPulseFrequency = 1f,
        };
        float baseRadius = pool.Size * .46f;
        var center = new Vector2(pool.WorldX + pool.Size / 2f, pool.WorldY + pool.Size / 2f);
        var justOutsideBase = new Rectangle((int)(center.X + baseRadius + 5), (int)center.Y, 2, 2);

        // Advance to roughly a quarter period (Simulation.FrameRate ticks/sec), where the pulse peaks.
        for (int i = 0; i < Simulation.FrameRate / 4; i++)
            pool.Update(battleground, casualMode: false);
        Assert.True(pool.Collides(justOutsideBase),
            "expected the breathing pool to have grown past its base radius by now");
    }

    [Fact]
    public void OrdinaryPool_NeverGrowsBeyondItsBaseRadius()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var pool = new EnemyProjectile(125, 125, 0f, 0f, 10, 100, path: "pool")
        {
            TelegraphDuration = 0f,
        };
        float baseRadius = pool.Size * .46f;
        var center = new Vector2(pool.WorldX + pool.Size / 2f, pool.WorldY + pool.Size / 2f);
        var justOutsideBase = new Rectangle((int)(center.X + baseRadius + 5), (int)center.Y, 2, 2);

        for (int i = 0; i < 15; i++)
            pool.Update(battleground, casualMode: false);
        Assert.False(pool.Collides(justOutsideBase));
    }

    [Fact]
    public void Tether_HitsAlongTheLineBetweenBothEnds()
    {
        var start = new EnemyProjectile(0, 100, 0f, 0f, 10, 10, path: "linear");
        var end = new EnemyProjectile(200, 100, 0f, 0f, 10, 10, path: "linear");
        var tether = new EnemyProjectile(100, 100, 0f, 0f, 10, 6, path: "tether")
        {
            TetherStart = start,
            TetherEnd = end,
        };
        var midpointRect = new Rectangle(95, 95, 10, 10);
        var farRect = new Rectangle(95, 900, 10, 10);
        Assert.True(tether.Collides(midpointRect));
        Assert.False(tether.Collides(farRect));
    }

    [Fact]
    public void Tether_Expires_WhenEitherEndDespawns()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var start = new EnemyProjectile(0, 100, 0f, 0f, 10, 10, path: "linear");
        var end = new EnemyProjectile(200, 100, 0f, 0f, 10, 10, path: "linear");
        var tether = new EnemyProjectile(100, 100, 0f, 0f, 10, 6, path: "tether")
        {
            TetherStart = start,
            TetherEnd = end,
        };
        end.RemFlag = true;
        tether.Update(battleground, casualMode: false);
        Assert.True(tether.RemFlag);
    }

    [Fact]
    public void SproutSeconds_LengthensTheLaserGrowthWindow()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var laser = new EnemyProjectile(100, 100, 0f, 0f, 10, 10,
            travelRange: 500, path: "laser")
        {
            TelegraphDuration = 0f,
            SproutSeconds = 2f,
        };
        laser.Update(battleground, casualMode: false);
        Assert.True(laser.LaserSproutProgress < .2f,
            "a laser with a 2-second sprout window should still be barely grown after one frame");
    }

    [Fact]
    public void DefaultSproutSeconds_MatchesTheOriginalConstant()
    {
        var laser = new EnemyProjectile(100, 100, 0f, 0f, 10, 10, path: "laser");
        Assert.Equal(EnemyProjectile.LaserSproutDuration, laser.SproutSeconds);
    }
}
