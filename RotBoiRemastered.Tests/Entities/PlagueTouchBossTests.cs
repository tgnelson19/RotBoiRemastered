using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bossTypes.py's PlagueTouchBoss/Bair/Sting (no dedicated Python test file to mirror).</summary>
public class PlagueTouchBossTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(Bair boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 500, PlayerWorldY = boss.WorldY, Battleground = battleground,
    };

    [Fact]
    public void Constructor_Bair_UsesMidStatsAndFivePhases()
    {
        var bair = new Bair(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal(29000, bair.Hp);
        Assert.Equal(1, bair.Phase);
        Assert.Equal("RIVER", bair.PhaseLabel);
    }

    [Fact]
    public void Constructor_Sting_UsesFinalStatsAndTenPhases()
    {
        var sting = new Sting(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal(240000, sting.Hp);
        Assert.Equal("BLOOD", sting.PhaseLabel);
    }

    [Fact]
    public void TakeDamage_PinsHealthToCurrentPhaseFloor()
    {
        var bair = new Bair(1000, 1000, MakeBattleground(), new Random(1));
        // 5 phases: phase 1's floor is maxHp * (5-1)/5 == 80% of max.
        var result = bair.TakeDamage(1_000_000);

        Assert.True(result.Applied);
        Assert.Equal((int)Math.Round(bair.MaxHp * 4.0 / 5.0), bair.Hp);
        Assert.Equal(1, bair.Phase); // pinned, not advanced -- phase only changes via UpdatePhase on the next Update
    }

    [Fact]
    public void TakeDamage_PortalPart_OnlyDamagesThatPortalAndNeverBlocks()
    {
        var bair = new Bair(1000, 1000, MakeBattleground(), new Random(1));
        bair.DebugSetPhase(2); // phase 2 deploys touch portals
        var context = MakeContext(bair, MakeBattleground());
        bair.Update(context); // let portals reposition/reset once

        var hitboxes = bair.GetScreenHitboxes(new Camera(), new Microsoft.Xna.Framework.Vector2(bair.WorldX, bair.WorldY), Microsoft.Xna.Framework.Vector2.Zero);
        Assert.Contains(hitboxes, h => h.Part == "portal:0");

        double hpBefore = bair.Hp;
        var result = bair.TakeDamage(1, "portal:0");

        Assert.True(result.Applied);
        Assert.False(result.Blocked);
        Assert.Equal(hpBefore, bair.Hp); // portal hits never touch the boss's own HP
    }

    [Fact]
    public void DebugSetPhase_GatedPhase_DeploysTouchPortals()
    {
        var bair = new Bair(1000, 1000, MakeBattleground(), new Random(1));
        Assert.DoesNotContain(bair.GetScreenHitboxes(new Camera(), new Microsoft.Xna.Framework.Vector2(bair.WorldX, bair.WorldY), Microsoft.Xna.Framework.Vector2.Zero),
            h => h.Part.StartsWith("portal:"));

        bair.DebugSetPhase(2); // Bair's own gate: phase in (2, 4) for a non-final boss

        var hitboxes = bair.GetScreenHitboxes(new Camera(), new Microsoft.Xna.Framework.Vector2(bair.WorldX, bair.WorldY), Microsoft.Xna.Framework.Vector2.Zero);
        Assert.Contains(hitboxes, h => h.Part.StartsWith("portal:"));
    }

    [Fact]
    public void Update_AfterEntranceAndCooldownElapse_FiresBairsPhaseOnePattern()
    {
        var battleground = MakeBattleground();
        var bair = new Bair(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(bair, battleground);

        for (int i = 0; i < 400 && context.ProjectileSink.Count == 0; i++)
            bair.Update(context);

        Assert.NotEmpty(context.ProjectileSink);
        Assert.Contains(context.ProjectileSink, p => p.Owner == "bair_touch_river");
    }

    [Fact]
    public void Update_StaticMovementPhase_DoesNotMove()
    {
        var battleground = MakeBattleground();
        var bair = new Bair(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        bair.DebugSetPhase(3); // Bair's movementModes[2] == "static"
        float startX = bair.WorldX, startY = bair.WorldY;
        var context = MakeContext(bair, battleground);

        for (int i = 0; i < 10; i++)
            bair.Update(context);

        Assert.Equal(startX, bair.WorldX, 2);
        Assert.Equal(startY, bair.WorldY, 2);
    }
}
