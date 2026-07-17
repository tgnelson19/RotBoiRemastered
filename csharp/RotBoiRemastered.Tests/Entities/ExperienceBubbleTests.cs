using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from the movement/collision behavior in experienceBubble.py.</summary>
public class ExperienceBubbleTests
{
    [Fact]
    public void Constructor_ScalesSizeByDifficulty()
    {
        var bubble = new ExperienceBubble(10, 20, value: 5, difficultyDead: 2, rng: new Random(1));
        Assert.Equal(40, bubble.Size);
        var rect = bubble.WorldRect();
        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
    }

    [Fact]
    public void Update_HomesTowardPlayer_WhenNotNaturalSpawn()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var bubble = new ExperienceBubble(150, 150, value: 5, difficultyDead: 1, rng: new Random(1))
        {
            NaturalSpawn = false,
            Direction = 0f,
        };
        float startX = bubble.WorldX;
        bubble.Update(playerAuraSpeed: 3f, battleground);
        Assert.True(bubble.WorldX < startX);
    }

    [Fact]
    public void Update_NaturalSpawn_NeverEntersAWall()
    {
        var battleground = EntityTestFixtures.SmallOpenRoom();
        // Just inside the interior next to the left wall, direction 0 pushes it further left.
        var bubble = new ExperienceBubble(52, 125, value: 1, difficultyDead: 1, rng: new Random(2))
        {
            Direction = 0f,
        };
        for (int i = 0; i < 50; i++)
            bubble.Update(playerAuraSpeed: 0f, battleground);
        Assert.False(battleground.RectHitsWall(bubble.WorldRect()));
    }

    [Fact]
    public void Celebration_SpawnsFiftySixParticles()
    {
        var bubble = new ExperienceBubble(0, 0, value: 1, difficultyDead: 1, rng: new Random(3), celebration: true);
        Assert.Equal(56, bubble.CelebrationParticleCount);
    }

    [Fact]
    public void NonCelebration_SpawnsNoParticles()
    {
        var bubble = new ExperienceBubble(0, 0, value: 1, difficultyDead: 1, rng: new Random(3));
        Assert.Equal(0, bubble.CelebrationParticleCount);
    }
}
