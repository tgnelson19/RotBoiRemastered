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
    public void Laser_VisualCullRect_SpansFullRange_DuringTelegraph()
    {
        // Regression test: the telegraph draws its tentacle cluster and range
        // markers across the whole beam before it fires (DrawLaser), but
        // WorldRect() intentionally stays a tiny box at the spawn point during
        // the telegraph so wall-hit checks in Update() don't fire early. Screen
        // culling has to look at the full sweep instead, or a laser spawned far
        // from the player -- with its warning sweeping in toward them -- gets
        // the whole telegraph culled off screen, leaving nothing to dodge once
        // the beam actually fires.
        var projectile = new EnemyProjectile(0, 0, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 2000)
        {
            TelegraphDuration = 1f,
        };

        Rectangle tinyDuringTelegraph = projectile.WorldRect();
        Rectangle cullRect = projectile.VisualCullRect();

        Assert.True(tinyDuringTelegraph.Width < 20);
        Assert.True(cullRect.Width >= 1900);

        // Player standing far down the beam's path, nowhere near the origin.
        var farPlayer = new Rectangle(1800, -50, 100, 100);
        Assert.False(tinyDuringTelegraph.Intersects(farPlayer));
        Assert.True(cullRect.Intersects(farPlayer));
    }

    [Fact]
    public void Laser_VisualCullRect_MatchesWorldRect_OnceFiring()
    {
        var projectile = new EnemyProjectile(0, 0, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 2000)
        {
            TelegraphDuration = 0f,
        };

        Assert.Equal(projectile.WorldRect(), projectile.VisualCullRect());
    }

    [Fact]
    public void Laser_LifetimeAndWallCollisionCannotBeOptedOutOf()
    {
        var projectile = new EnemyProjectile(
            100, 100, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 500, lifetime: 30f,
            ignoreWalls: true);

        Assert.Equal(EnemyProjectile.MaximumLaserLifetime, projectile.Lifetime);
        Assert.False(projectile.IgnoreWalls);
    }

    [Fact]
    public void Laser_StopsAtFirstWallAndCannotHitThroughIt()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        battleground.SetTile(3, 2, RotBoiRemastered.World.TileType.BuildingWall);
        var projectile = new EnemyProjectile(
            75, 125, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 500, lifetime: 2f,
            ignoreWalls: true)
        {
            TelegraphDuration = 0f,
        };

        projectile.Update(battleground, casualMode: false);

        Assert.InRange(projectile.RemainingRange, 69f, 71f);
        Assert.InRange(projectile.LaserSproutProgress, 0f, .1f);
        Assert.False(projectile.Collides(new Rectangle(105, 120, 10, 10)));
        for (int frame = 0; frame < 20; frame++)
            projectile.Update(battleground, casualMode: false);
        Assert.Equal(1f, projectile.LaserSproutProgress);
        Assert.True(projectile.Collides(new Rectangle(105, 120, 10, 10)));
        Assert.False(projectile.Collides(new Rectangle(175, 120, 10, 10)));
    }

    [Fact]
    public void Laser_SproutAndCollisionGrowTogetherFromTheSource()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(
            75, 125, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 70, lifetime: 2f)
        {
            TelegraphDuration = 0f,
        };

        projectile.Update(battleground, casualMode: false);
        Assert.InRange(projectile.LaserSproutProgress, 0f, 1f);
        Assert.False(projectile.Collides(new Rectangle(125, 120, 10, 10)));

        for (int frame = 0; frame < 20; frame++)
            projectile.Update(battleground, casualMode: false);

        Assert.Equal(1f, projectile.LaserSproutProgress);
        Assert.True(projectile.Collides(new Rectangle(125, 120, 10, 10)));
        Assert.True(EnemyProjectile.LaserSproutDuration < .2f);
        Assert.Equal(5, EnemyProjectile.LaserTentacleCount);
        Assert.True(EnemyProjectile.LaserVisualWidthScale >= 1.5f);
        Assert.True(EnemyProjectile.MinimumLaserVisualWidth >= 12f);
    }

    [Fact]
    public void Laser_WithAmplitude_BendsTheHitboxAwayFromTheStraightLineNearItsPeak()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.LargeOpenRoom();
        // Frequency chosen so 200 pixels out along the beam sits exactly at
        // the sine's positive peak (Frequency * 200 == pi/2), well clear of
        // the origin -- every wave, regardless of amplitude, still touches
        // the straight line there.
        float frequency = MathF.PI / 2f / 200f;
        var projectile = new EnemyProjectile(
            100, 100, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 2000, lifetime: 5f,
            amplitude: 80f, frequency: frequency)
        {
            TelegraphDuration = 0f,
        };
        for (int frame = 0; frame < 20; frame++)
            projectile.Update(battleground, casualMode: false);

        // Heading is +X from (100,100), so the unbent point 200px out would
        // be (300,100); the wave should have pushed it up to (300,180)
        // (perpendicular offset == amplitude at the peak) instead.
        Assert.False(projectile.Collides(new Rectangle(295, 95, 10, 10)));
        Assert.True(projectile.Collides(new Rectangle(295, 175, 10, 10)));
    }

    [Fact]
    public void Laser_WithWaveSpeed_ShiftsTheBendAsTimePasses()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.LargeOpenRoom();
        // Frequency 0 makes the whole beam bend by the same amount at every
        // point along its length, isolating LaserWaveSpeed's effect (a
        // uniform perpendicular slide over time) from Frequency's spatial
        // shaping -- covered separately above.
        float waveSpeed = MathF.PI;
        var projectile = new EnemyProjectile(
            100, 100, direction: 0f, speed: 0, damage: 10, size: 10,
            path: "laser", travelRange: 2000, lifetime: 5f,
            amplitude: 80f, frequency: 0f)
        {
            TelegraphDuration = 0f,
            LaserWaveSpeed = waveSpeed,
        };

        // Tick to the wave's trough (phase == pi/2, offset == -80), then on
        // to its peak (phase == 3*pi/2, offset == +80) -- a guaranteed
        // full-amplitude swing regardless of exact per-tick timing, unlike
        // sampling two arbitrary fixed frame counts.
        while (waveSpeed * projectile.Age < MathF.PI / 2f)
            projectile.Update(battleground, casualMode: false);
        float offsetEarly = 80f * MathF.Sin(-waveSpeed * projectile.Age);
        var earlyRect = new Rectangle(295, (int)(100 + offsetEarly) - 5, 10, 10);
        Assert.True(projectile.Collides(earlyRect));

        while (waveSpeed * projectile.Age < 3f * MathF.PI / 2f)
            projectile.Update(battleground, casualMode: false);
        float offsetLater = 80f * MathF.Sin(-waveSpeed * projectile.Age);

        Assert.True(MathF.Abs(offsetLater - offsetEarly) > 100f,
            "Expected the travelling wave's perpendicular offset to have moved noticeably as Age advanced.");
        Assert.False(projectile.Collides(earlyRect),
            "Expected the beam to have slid away from where it collided earlier.");
    }

    [Fact]
    public void AphantasiaLasersCyclePulsingRainbowWhileOtherLasersKeepOwnerColor()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var authored = new Color(37, 149, 211);
        var ordinary = new EnemyProjectile(75, 125, 0, 0, 10, 10,
            color: authored, path: "laser", owner: "chronos_directive");
        var aphantasia = new EnemyProjectile(75, 125, 0, 0, 10, 10,
            color: authored, path: "laser", owner: "aphantasia_laser_light");

        Assert.False(ordinary.UsesRainbowLaserTentacles);
        Assert.Equal(authored, ordinary.LaserTentacleColor(2, .5f));
        Assert.True(aphantasia.UsesRainbowLaserTentacles);
        Color first = aphantasia.LaserTentacleColor(0, .25f);
        for (int frame = 0; frame < 12; frame++)
            aphantasia.Update(battleground, casualMode: false);
        Color later = aphantasia.LaserTentacleColor(0, .25f);

        Assert.NotEqual(authored, first);
        Assert.NotEqual(first, later);
        Assert.NotEqual(
            aphantasia.LaserTentacleColor(0, .5f),
            aphantasia.LaserTentacleColor(4, .5f));
    }

    [Fact]
    public void LaserEnemiesAndArsenalBossesPassTheirOwnColorIntoTentacles()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = 150,
            PlayerWorldY = 150,
            Battleground = battleground,
        };
        var enemyColor = new Color(48, 188, 164);
        var laserEnemy = new LaserEnemy(75, 75, 1, 30, enemyColor,
            10, 100, 0, 1, tier: "small", rng: new Random(3))
        {
            AttackCooldown = 0,
        };
        laserEnemy.Update(context);
        EnemyProjectile enemyLaser = Assert.Single(context.ProjectileSink);
        Assert.Equal(enemyColor, enemyLaser.Color);

        context.ProjectileSink.Clear();
        var bossColor = new Color(194, 71, 132);
        var arsenal = new ArsenalMiniBoss(75, 75, 1, 30, bossColor,
            10, 100, 0, 1, awarenessRange: 1000,
            phaseOrder: ["laser", "laser", "laser"], rng: new Random(4))
        {
            AttackCooldown = 0,
        };
        arsenal.Update(context);
        Assert.NotEmpty(context.ProjectileSink);
        Assert.All(context.ProjectileSink,
            laser => Assert.Equal(bossColor, laser.Color));
    }

    [Fact]
    public void RemoteOriginTelegraph_FreezesAndDisarmsProjectileUntilWarningEnds()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(
            100, 125, direction: 0f, speed: 4, damage: 10, size: 10,
            travelRange: 500)
        {
            OriginTelegraphDuration = .05f,
        };
        float startX = projectile.WorldX;

        projectile.Update(battleground, casualMode: false);

        Assert.Equal(startX, projectile.WorldX);
        Assert.False(projectile.Collides(new Rectangle(100, 125, 10, 10)));

        for (int tick = 0; tick < 8; tick++)
            projectile.Update(battleground, casualMode: false);

        Assert.True(projectile.WorldX > startX);
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
    public void SplitBudgetAndActivationTelegraph_DefaultToLegacyBehavior()
    {
        var projectile = new EnemyProjectile(
            100, 125, direction: 0f, speed: 4, damage: 10, size: 10,
            travelRange: 5000);

        Assert.Equal(1, projectile.ThreatReservationCost);
        Assert.Equal(1f, projectile.SplitTelegraphStartRatio);
        Assert.Equal(1.08f, projectile.SplitSpeedScale);
        Assert.Null(projectile.SplitChildLifetime);
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
    public void Mine_IsHarmlessDuringItsWarningThenBecomesSolid()
    {
        Simulation.ResetForTests();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var projectile = new EnemyProjectile(100, 100, direction: 0f, speed: 0, damage: 10,
            size: 20, path: "mine", lifetime: 5f)
        {
            TelegraphDuration = .01f,
        };
        var target = new Rectangle(100, 100, 20, 20);

        Assert.False(projectile.Collides(target));
        projectile.Update(battleground, casualMode: false);
        projectile.Update(battleground, casualMode: false);
        Assert.True(projectile.Collides(target));
    }

    [Fact]
    public void BossOwner_ScalesDamageByBossScale()
    {
        var boss = new EnemyProjectile(0, 0, 0f, 1, damage: 1, size: 10, owner: "beaudis_shot");
        var normal = new EnemyProjectile(0, 0, 0f, 1, damage: 1, size: 10, owner: "regular_enemy");
        Assert.Equal(100, boss.Damage);
        Assert.Equal(1, normal.Damage);
    }

    [Theory]
    [InlineData("sound", "wave", "tuning_fork", "chevron")]
    [InlineData("touch", "rivet", "chain_link", "slab")]
    [InlineData("sight", "eye", "needle", "lens")]
    [InlineData("chemesthesis", "ember", "spore", "cracked_core")]
    [InlineData("phantasia", "star", "crescent", "orbit_core")]
    public void DefaultShape_ResolvesToPathSpecificVisualVocabulary(
        string path,
        string first,
        string second,
        string third)
    {
        var allowed = new HashSet<string> { first, second, third };
        for (int index = 0; index < 20; index++)
        {
            var projectile = new EnemyProjectile(
                index * 17, index * 31, 0, 0, 1, 10,
                owner: $"gallery_{index}")
            {
                ContentPath = path,
            };
            Assert.Contains(projectile.ResolveVisualShape(), allowed);
        }
    }

    [Fact]
    public void AuthoredShape_IsNotReplacedByPathVocabulary()
    {
        var projectile = new EnemyProjectile(
            0, 0, 0, 0, 1, 10, shape: "diamond")
        {
            ContentPath = "phantasia",
        };

        Assert.Equal("diamond", projectile.ResolveVisualShape());
    }
}
