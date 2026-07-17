using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyTypes.py's SnakeEnemy segment hitbox/damage-routing behavior.</summary>
public class SnakeEnemyTests
{
    private static SnakeEnemy MakeSnake(int segmentCount = 3) =>
        new(200, 200, speed: 2, bodySize: 40, Color.Purple, damage: 10, hp: 200, expValue: 20, difficulty: 1,
            awarenessRange: 300f, segmentCount: segmentCount, rng: new Random(1));

    [Fact]
    public void GetWorldHitboxes_ListsHeadThenEverySegment()
    {
        var snake = MakeSnake(3);
        var hitboxes = snake.GetWorldHitboxes();
        Assert.Equal(4, hitboxes.Count);
        Assert.Equal("head", hitboxes[0].Part);
        Assert.Equal(new[] { "head", "0", "1", "2" }, hitboxes.Select(h => h.Part));
    }

    [Fact]
    public void TakeDamage_OnHead_IsBlocked_WhileSegmentsRemain()
    {
        var snake = MakeSnake(2);
        var result = snake.TakeDamage(50, "head");
        Assert.True(result.Blocked);
        Assert.False(result.Applied);
    }

    [Fact]
    public void TakeDamage_OnSegment_RemovesItOnceDepleted()
    {
        var snake = MakeSnake(2);
        int segmentHp = snake.GetWorldHitboxes().Count; // sanity: 3 boxes (head + 2 segments) exist first
        Assert.Equal(3, segmentHp);

        var result = snake.TakeDamage(100000, "0");
        Assert.True(result.Applied);
        Assert.Equal(2, snake.GetWorldHitboxes().Count); // segment "0" removed -> head + segment "1"
    }

    [Fact]
    public void TakeDamage_OnHead_AppliesDirectly_OnceAllSegmentsGone()
    {
        var snake = MakeSnake(1);
        snake.TakeDamage(100000, "0"); // removes the only segment
        var result = snake.TakeDamage(50, "head");
        Assert.True(result.Applied);
        Assert.Equal(150, snake.Hp);
    }

    [Fact]
    public void TakeDamage_OnUnknownSegmentId_ReturnsNotApplied()
    {
        var snake = MakeSnake(1);
        var result = snake.TakeDamage(10, "not-a-real-id");
        Assert.False(result.Applied);
    }
}
