using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemy.py's awareness state machine, wall-slide movement, and combat contract.</summary>
public class EnemyTests
{
    private static Enemy MakeEnemy(float x, float y, float awarenessRange = 300f, float speed = 4f) =>
        new(x, y, speed, size: 20, Color.Red, damage: 10, hp: 50, expValue: 5, difficulty: 1,
            awarenessRange: awarenessRange, rng: new Random(1));

    [Fact]
    public void Update_MovesToward_PlayerWithinAwarenessRange()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var enemy = MakeEnemy(60, 125, awarenessRange: 300f);
        float startX = enemy.WorldX;
        enemy.Update(playerWorldX: 190, playerWorldY: 125, battleground);
        Assert.True(enemy.WorldX > startX);
        Assert.Equal("alerted", enemy.AwarenessState);
    }

    [Fact]
    public void Update_Wanders_WhenPlayerOutsideAwarenessRange()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var enemy = MakeEnemy(125, 125, awarenessRange: 10f);
        enemy.Update(playerWorldX: 100000, playerWorldY: 100000, battleground);
        Assert.Equal("wandering", enemy.AwarenessState);
    }

    [Fact]
    public void Update_Disengages_WithHysteresis_BeforeReturningToWandering()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        // speed 0 isolates the awareness state machine from the enemy's own
        // movement, which would otherwise close the gap to the player and
        // invalidate the fixed distances this test relies on.
        // Awareness range 20, disengage range 25 (1.25x) -- a distance of 22 should
        // read as "disengaging", not snap straight back to "wandering".
        var enemy = MakeEnemy(125, 125, awarenessRange: 20f, speed: 0f); // center at (135, 135)
        enemy.Update(playerWorldX: 145, playerWorldY: 135, battleground); // distance 10 -> alerted
        Assert.Equal("alerted", enemy.AwarenessState);
        enemy.Update(playerWorldX: 157, playerWorldY: 135, battleground); // distance 22 -> disengaging
        Assert.Equal("disengaging", enemy.AwarenessState);
    }

    [Fact]
    public void Update_NeverEntersAWall_WhenChasingThroughOne()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var enemy = MakeEnemy(60, 125, awarenessRange: 1000f, speed: 50f);
        for (int i = 0; i < 20; i++)
            enemy.Update(playerWorldX: -1000, playerWorldY: 125, battleground); // player beyond the left wall
        Assert.False(battleground.RectHitsWall(enemy.WorldRect()));
    }

    [Fact]
    public void EngagementDisallowed_StaysWandering_EvenWhenPlayerIsClose()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var enemy = MakeEnemy(125, 125, awarenessRange: 300f);
        enemy.EngagementAllowed = false;
        enemy.Update(playerWorldX: 126, playerWorldY: 125, battleground);
        Assert.Equal("wandering", enemy.AwarenessState);
    }

    [Fact]
    public void TakeDamage_ReducesHp_AndReportsKilled()
    {
        var enemy = MakeEnemy(0, 0);
        var result = enemy.TakeDamage(1000);
        Assert.True(result.Applied);
        Assert.True(result.Killed);
        Assert.True(enemy.IsDead());
    }

    [Fact]
    public void TakeDamage_SurvivesPartialDamage()
    {
        var enemy = MakeEnemy(0, 0);
        var result = enemy.TakeDamage(10);
        Assert.False(result.Killed);
        Assert.Equal(40, enemy.Hp);
        Assert.False(enemy.IsDead());
    }

    [Fact]
    public void ApplyKnockback_RespectsWalls()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var enemy = MakeEnemy(52, 125);
        enemy.ApplyKnockback(-1000, 0, battleground); // shove hard into the left wall
        Assert.False(battleground.RectHitsWall(enemy.WorldRect()));
    }
}
