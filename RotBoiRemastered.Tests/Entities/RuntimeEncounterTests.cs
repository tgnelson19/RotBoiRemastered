using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyTypes.py's RuntimeEncounter patrol/engage/disengage state machine.</summary>
public class RuntimeEncounterTests
{
    private static Enemy MakeMember(float x, float y) =>
        new(x, y, speed: 2, size: 20, Color.Red, damage: 10, hp: 50, expValue: 5, difficulty: 1,
            awarenessRange: 300f, rng: new Random(1));

    [Fact]
    public void Constructor_AssignsSlotsAndAlternatingSides()
    {
        var members = new[] { MakeMember(0, 0), MakeMember(10, 10), MakeMember(20, 20) };
        var encounter = new RuntimeEncounter("test", members, Vector2.Zero, level: 5, screenHeight: 1080f, rng: new Random(1));

        Assert.Equal(0, members[0].EncounterSlot);
        Assert.Equal(1, members[1].EncounterSlot);
        Assert.Equal(2, members[2].EncounterSlot);
        Assert.Equal(1, members[0].CombatSide);
        Assert.Equal(-1, members[1].CombatSide);
        Assert.Equal(1, members[2].CombatSide);
        Assert.All(members, member => Assert.Same(encounter, member.Encounter));
    }

    [Fact]
    public void Update_TransitionsToEngaged_WithinActivationRange()
    {
        var members = new[] { MakeMember(100, 100) };
        var encounter = new RuntimeEncounter("test", members, new Vector2(100, 100), level: 1, screenHeight: 1080f, rng: new Random(1));
        var battleground = EntityTestFixtures.SmallOpenRoom();

        Assert.Equal("patrolling", encounter.State);
        encounter.Update(playerX: 105, playerY: 105, battleground, allowed: true);
        Assert.Equal("engaged", encounter.State);
        Assert.True(encounter.EngagementAllowed);
    }

    [Fact]
    public void Update_Disengages_WhenPlayerLeavesDisengageRange()
    {
        var members = new[] { MakeMember(100, 100) };
        var encounter = new RuntimeEncounter("test", members, new Vector2(100, 100), level: 1, screenHeight: 1080f, rng: new Random(1));
        var battleground = EntityTestFixtures.SmallOpenRoom();

        encounter.Update(playerX: 105, playerY: 105, battleground, allowed: true);
        Assert.Equal("engaged", encounter.State);

        encounter.Update(playerX: 100000, playerY: 100000, battleground, allowed: true);
        Assert.Equal("patrolling", encounter.State);
        Assert.False(encounter.EngagementAllowed);
    }

    [Fact]
    public void Update_PrunesDeadMembers()
    {
        var alive = MakeMember(0, 0);
        var dead = MakeMember(10, 10);
        dead.TakeDamage(10000);
        var encounter = new RuntimeEncounter("test", new[] { alive, dead }, Vector2.Zero, level: 1, screenHeight: 1080f, rng: new Random(1));
        var battleground = EntityTestFixtures.SmallOpenRoom();

        encounter.Update(playerX: 0, playerY: 0, battleground);

        Assert.Single(encounter.Members);
        Assert.Same(alive, encounter.Members[0]);
    }

    [Fact]
    public void ThreatCost_SumsOnlyLivingMembers()
    {
        var alive = MakeMember(0, 0);
        alive.ThreatCost = 2.0;
        var dead = MakeMember(10, 10);
        dead.ThreatCost = 3.0;
        dead.TakeDamage(10000);
        var encounter = new RuntimeEncounter("test", new[] { alive, dead }, Vector2.Zero, level: 1, screenHeight: 1080f, rng: new Random(1));

        Assert.Equal(2.0, encounter.ThreatCost);
    }
}
