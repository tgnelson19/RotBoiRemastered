using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyTypes.py's ParentEnemy threshold-crossing child-spawn behavior.</summary>
public class ParentEnemyTests
{
    private static ParentEnemy MakeParent(double hp = 100) =>
        new(125, 125, speed: 1, size: 60, Color.Purple, damage: 10, hp: hp, expValue: 10, difficulty: 1,
            awarenessRange: 500f, rng: new Random(1));

    [Fact]
    public void TakeDamage_CrossingSeventyPercentThreshold_QueuesChildren()
    {
        var parent = MakeParent(100);
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var context = new EnemyUpdateContext { PlayerWorldX = 130, PlayerWorldY = 130, Battleground = battleground };

        parent.TakeDamage(35); // ratio -> .65, crosses the .70 threshold -> pendingChildren += tierRank(1)+1 = 2
        parent.Update(context);
        Assert.Single(parent.SpawnedEnemies);

        parent.Update(context);
        Assert.Equal(2, parent.SpawnedEnemies.Count);

        // No more thresholds crossed -- a third update births nothing further.
        parent.Update(context);
        Assert.Equal(2, parent.SpawnedEnemies.Count);
    }

    [Fact]
    public void TakeDamage_CrossingSameThresholdTwice_OnlyQueuesOnce()
    {
        var parent = MakeParent(100);
        parent.TakeDamage(35); // crosses .70
        parent.TakeDamage(1);  // still above .40, no new threshold crossed
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var context = new EnemyUpdateContext { PlayerWorldX = 130, PlayerWorldY = 130, Battleground = battleground };

        parent.Update(context);
        parent.Update(context);
        parent.Update(context);
        // Only the single .70 crossing's 2 pending children, never re-triggered.
        Assert.Equal(2, parent.SpawnedEnemies.Count);
    }

    [Fact]
    public void SpawnedChildren_AreChildEnemyType()
    {
        var parent = MakeParent(100);
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var context = new EnemyUpdateContext { PlayerWorldX = 130, PlayerWorldY = 130, Battleground = battleground };
        parent.TakeDamage(35);
        parent.Update(context);
        Assert.IsType<ChildEnemy>(parent.SpawnedEnemies[0]);
    }
}
