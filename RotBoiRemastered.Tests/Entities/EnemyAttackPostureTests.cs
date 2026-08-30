using System.Reflection;
using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public sealed class EnemyAttackPostureTests
{
    private static EnemyAttackPosture Braced(
        double windup = 1.0, double recover = .5, bool immunity = true)
    {
        var posture = new EnemyAttackPosture();
        Assert.True(posture.BeginWindup(windup, recover, immunity));
        return posture;
    }

    [Fact]
    public void WalksIdleThroughWindupReleaseAndRecoverBackToIdle()
    {
        var posture = Braced();
        Assert.Equal(EnemyAttackStance.Windup, posture.Stance);

        posture.Tick(1.0);
        Assert.Equal(EnemyAttackStance.Release, posture.Stance);

        Assert.True(posture.ConsumeRelease());
        Assert.Equal(EnemyAttackStance.Recover, posture.Stance);

        posture.Tick(.5);
        Assert.Equal(EnemyAttackStance.Idle, posture.Stance);
    }

    [Fact]
    public void ReleaseIsConsumedExactlyOncePerCommittedAttack()
    {
        var posture = Braced();
        posture.Tick(1.0);

        Assert.True(posture.ConsumeRelease());
        Assert.False(posture.ConsumeRelease());
        Assert.False(posture.ConsumeRelease());
    }

    [Fact]
    public void BeginWindupIsRefusedWhileAlreadyCommitted()
    {
        var posture = Braced();
        Assert.False(posture.BeginWindup(9.0, 9.0, true));

        posture.Tick(1.0);
        posture.ConsumeRelease();
        // Still recovering -- the punish window cannot be cut short.
        Assert.False(posture.BeginWindup(9.0, 9.0, true));

        posture.Tick(.5);
        Assert.True(posture.BeginWindup(1.0, .5, true));
    }

    [Fact]
    public void InvincibleOnlyDuringWindupAndOnlyWhenTheTierGrantsIt()
    {
        var granting = Braced(immunity: true);
        Assert.True(granting.Invincible);
        granting.Tick(1.0);
        Assert.False(granting.Invincible); // Release
        granting.ConsumeRelease();
        Assert.False(granting.Invincible); // Recover is the punish window

        var plain = Braced(immunity: false);
        Assert.Equal(EnemyAttackStance.Windup, plain.Stance);
        Assert.False(plain.Invincible);
    }

    [Fact]
    public void AnAbandonedReleaseRecoversRatherThanWedging()
    {
        var posture = Braced();
        posture.Tick(1.0);
        Assert.Equal(EnemyAttackStance.Release, posture.Stance);

        // Nothing ever consumes it -- an enemy that lost its target mid-wind-up.
        for (int tick = 0; tick < 200; tick++)
            posture.Tick(1.0 / Simulation.FrameRate);

        Assert.NotEqual(EnemyAttackStance.Release, posture.Stance);
        Assert.False(posture.Invincible);
    }

    [Fact]
    public void WindupProgressRunsZeroToOne()
    {
        var posture = Braced(windup: 1.0);
        Assert.Equal(0f, posture.WindupProgress, 2);
        posture.Tick(.5);
        Assert.Equal(.5f, posture.WindupProgress, 2);
        posture.Tick(.5);
        Assert.Equal(0f, posture.WindupProgress, 2); // no longer winding up
    }

    [Fact]
    public void ResetAbandonsAnyCommitment()
    {
        var posture = Braced();
        posture.Reset();
        Assert.Equal(EnemyAttackStance.Idle, posture.Stance);
        Assert.False(posture.Invincible);
        Assert.False(posture.Busy);
    }
}

public sealed class EnemyPostureIntegrationTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static Enemy MakeEnemy() =>
        new(1000, 1000, .5f, 32, Color.White, damage: 10, hp: 1000,
            expValue: 1, difficulty: 1, awarenessRange: 400f);

    [Fact]
    public void BracedEnemyRefusesDirectDamageButNotStatusTicks()
    {
        var enemy = MakeEnemy();
        enemy.Posture.BeginWindup(1.0, .5, grantsImmunity: true);
        int before = enemy.Hp;

        HitResult direct = enemy.TakeDamage(100, "body", DamageSource.Direct);
        Assert.True(direct.Blocked);
        Assert.False(direct.Applied);
        Assert.Equal(before, enemy.Hp);

        // Affliction builds must keep working, or a crowd of braced enemies
        // stalls a run outright.
        HitResult overTime = enemy.TakeDamage(100, "body", DamageSource.DamageOverTime);
        Assert.True(overTime.Applied);
        Assert.False(overTime.Blocked);
        Assert.Equal(before - 100, enemy.Hp);
    }

    [Fact]
    public void RecoveringEnemyIsFullyVulnerable()
    {
        var enemy = MakeEnemy();
        enemy.Posture.BeginWindup(1.0, .5, grantsImmunity: true);
        enemy.Posture.Tick(1.0);
        enemy.Posture.ConsumeRelease();
        Assert.Equal(EnemyAttackStance.Recover, enemy.Posture.Stance);

        HitResult hit = enemy.TakeDamage(100);
        Assert.True(hit.Applied);
        Assert.False(hit.Blocked);
    }

    [Fact]
    public void AnEnemyThatNeverAttacksStaysIdle()
    {
        var enemy = MakeEnemy();
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = enemy.WorldX + 200,
            PlayerWorldY = enemy.WorldY,
            Battleground = MakeBattleground(),
        };
        for (int tick = 0; tick < 600; tick++)
            enemy.Update(context);

        Assert.Equal(EnemyAttackStance.Idle, enemy.Posture.Stance);
        Assert.False(enemy.Posture.Invincible);
    }

    /// <summary>
    /// The posture clock rides <see cref="Enemy.AdvanceAge"/> because that is
    /// the one method every enemy's frame reaches exactly once -- including the
    /// subclasses that replace `Update` wholesale and return early before ever
    /// touching the base body. This guards that invariant: a future subclass
    /// that forgets to advance its age would silently freeze its own attack
    /// posture, and this test fails loudly instead.
    /// </summary>
    [Fact]
    public void EveryEnemySubclassUpdateAdvancesAge()
    {
        var battleground = MakeBattleground();
        var skipped = new List<string>();
        var frozen = new List<string>();
        var verified = new List<string>();

        foreach (Type type in typeof(Enemy).Assembly.GetTypes())
        {
            if (!typeof(Enemy).IsAssignableFrom(type) || type.IsAbstract)
                continue;
            MethodInfo? update = type.GetMethod(
                nameof(Enemy.Update), BindingFlags.Public | BindingFlags.Instance);
            if (update is null || update.DeclaringType != type)
                continue; // inherits a body already covered by its base

            Enemy? enemy = TryConstruct(type, battleground);
            if (enemy is null)
            {
                skipped.Add(type.Name);
                continue;
            }

            var context = new EnemyUpdateContext
            {
                PlayerWorldX = enemy.WorldX + 260,
                PlayerWorldY = enemy.WorldY + 40,
                Battleground = battleground,
                BossAfflictions = new RotBoiRemastered.Systems.BossAfflictions(),
            };
            float before = enemy.Age;
            enemy.Update(context);
            if (enemy.Age <= before)
                frozen.Add(type.Name);
            else
                verified.Add(type.Name);
        }

        Assert.True(frozen.Count == 0,
            "These Update overrides never advance Age, so their attack posture "
            + "would freeze: " + string.Join(", ", frozen));
        // Guards against the sweep passing vacuously: if construction started
        // failing for every type, `frozen` would also be empty and this test
        // would go quietly green while checking nothing.
        Assert.True(verified.Count >= 8,
            $"Only verified {verified.Count} Update overrides "
            + $"({string.Join(", ", verified)}); skipped {string.Join(", ", skipped)}.");
    }

    private static Enemy? TryConstruct(Type type, Battleground battleground)
    {
        foreach (ConstructorInfo ctor in type.GetConstructors()
            .OrderBy(c => c.GetParameters().Length))
        {
            var args = new List<object?>();
            bool usable = true;
            foreach (ParameterInfo parameter in ctor.GetParameters())
            {
                if (parameter.ParameterType == typeof(float)) args.Add(1000f);
                else if (parameter.ParameterType == typeof(int)) args.Add(1);
                else if (parameter.ParameterType == typeof(double)) args.Add(1.0);
                else if (parameter.ParameterType == typeof(string)) args.Add("drifter");
                else if (parameter.ParameterType == typeof(Color)) args.Add(Color.White);
                else if (parameter.ParameterType == typeof(Battleground)) args.Add(battleground);
                else if (parameter.ParameterType == typeof(Random)) args.Add(new Random(1));
                else if (parameter.HasDefaultValue) args.Add(parameter.DefaultValue);
                else { usable = false; break; }
            }
            if (!usable)
                continue;
            try
            {
                return (Enemy?)ctor.Invoke(args.ToArray());
            }
            catch
            {
                // Constructor rejected the synthetic arguments; try the next.
            }
        }
        return null;
    }
}
