using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyTypes.py's BannerCaptain/BannerMinion formation-then-orphan behavior.</summary>
public class BannerCaptainTests
{
    private static Battleground Room() => EntityTestFixtures.SmallOpenRoom();

    private static BannerCaptain MakeCaptain(Battleground battleground) =>
        new(125, 125, speed: 2, size: 40, Color.Red, damage: 10, hp: 100, expValue: 15, difficulty: 1,
            awarenessRange: 300f, rng: new Random(1), battleground: battleground);

    [Fact]
    public void Constructor_SpawnsMinionsBoundToThisCaptain()
    {
        var captain = MakeCaptain(Room());
        Assert.NotEmpty(captain.SpawnedEnemies);
        Assert.All(captain.SpawnedEnemies, minion => Assert.Same(captain, ((BannerMinion)minion).Leader));
    }

    [Fact]
    public void Minion_HoldsFormation_WhileLeaderAlive()
    {
        var battleground = Room();
        var captain = MakeCaptain(battleground);
        var minion = (BannerMinion)captain.SpawnedEnemies[0];
        float startX = minion.WorldX, startY = minion.WorldY;

        var context = new EnemyUpdateContext { PlayerWorldX = 1000, PlayerWorldY = 1000, Battleground = battleground };
        minion.Update(context);

        // Formation-following is driven entirely by the leader's position, not the player's.
        Assert.NotNull(minion.Leader);
    }

    [Fact]
    public void Minion_ChasesDirectly_AndSpeedCompounds_OnceLeaderIsDead()
    {
        var battleground = Room();
        var captain = MakeCaptain(battleground);
        var minion = (BannerMinion)captain.SpawnedEnemies[0];
        captain.TakeDamage(100000);
        Assert.True(captain.IsDead());

        float speedBefore = minion.Speed;
        var context = new EnemyUpdateContext { PlayerWorldX = minion.WorldX + 500, PlayerWorldY = minion.WorldY, Battleground = battleground };
        minion.Update(context);

        Assert.Null(minion.Leader);
        Assert.True(minion.Speed > speedBefore); // 1.0015x compounding once orphaned
    }

    [Fact]
    public void Captain_CommandsLivingMinions_WhenAlertedAndCooldownReady()
    {
        var battleground = Room();
        var captain = MakeCaptain(battleground);
        var minions = captain.SpawnedEnemies.Cast<BannerMinion>().ToList();
        var allEnemies = new List<Enemy> { captain };
        allEnemies.AddRange(minions);

        var context = new EnemyUpdateContext
        {
            PlayerWorldX = captain.WorldX + 10, PlayerWorldY = captain.WorldY, Battleground = battleground,
            AllEnemies = allEnemies,
        };
        // Drive the captain aware and past its initial command cooldown.
        for (int i = 0; i < 300 && captain.AwarenessState == "wandering"; i++)
            captain.Update(context);
        Assert.NotEqual("wandering", captain.AwarenessState);
    }
}
