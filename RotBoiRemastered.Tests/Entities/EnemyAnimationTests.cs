using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

public sealed class EnemyAnimationTests
{
    [Fact]
    public void AttackPulseUsesTheDurationAuthoredByTheGameplayEvent()
    {
        Simulation.ResetForTests();
        var enemy = new Enemy(
            60, 60, 1, 20, Color.Red,
            damage: 10, hp: 50, expValue: 5, difficulty: 1,
            awarenessRange: 300, rng: new Random(1));
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = 150,
            PlayerWorldY = 150,
            Battleground = battleground,
        };

        enemy.MarkAttack(.8f);
        Assert.Equal(0f, enemy.VisualAttackPulse, 5);

        Simulation.SetDeltaTime(50);
        enemy.Update(context);

        Assert.InRange(enemy.VisualAttackPulse, .15f, .3f);
    }
}
