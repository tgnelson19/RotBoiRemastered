using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyTypes.py's PillarEnemy waiting/telegraph/landed/firing state machine.</summary>
public class PillarEnemyTests
{
    private static PillarEnemy MakePillar() =>
        new(125, 125, speed: 0, size: 40, Color.Gray, damage: 10, hp: 200, expValue: 20, difficulty: 1,
            awarenessRange: 500f, rng: new Random(1));

    [Fact]
    public void Update_EventuallyFires_AfterCyclingThroughItsStates()
    {
        var pillar = MakePillar();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var context = new EnemyUpdateContext { PlayerWorldX = 135, PlayerWorldY = 135, Battleground = battleground };

        for (int i = 0; i < 2000 && context.ProjectileSink.Count == 0; i++)
            pillar.Update(context);

        Assert.NotEmpty(context.ProjectileSink);
    }
}
