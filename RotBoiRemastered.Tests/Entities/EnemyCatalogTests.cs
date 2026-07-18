using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from the catalog/registration/spawn-rule coverage in enemyTypes.py's EnemyCatalog.</summary>
public class EnemyCatalogTests
{
    private static Battleground Room() => EntityTestFixtures.SmallOpenRoom();

    [Fact]
    public void Shared_RegistersEveryBaseFamily()
    {
        var keys = Enumerable.Range(0, 21)
            .SelectMany(level => EnemyCatalog.Shared.Available(level))
            .Select(d => d.Key)
            .Distinct()
            .ToList();
        foreach (var expected in new[]
                 {
                     "runner", "drifter", "skirmisher", "bulwark", "ranged_wanderer", "shotgunner", "snake",
                     "parent", "pillar", "banner", "rammer", "warder", "splitter", "collector",
                     "volley_small", "laser_small", "bomb_small", "miniboss_arsenal", "miniboss_siege",
                 })
        {
            Assert.Contains(expected, keys);
        }
    }

    [Fact]
    public void Available_RespectsLevelGates()
    {
        // runner: ((0,6),(4,13),(10,20)) -- at level 0 only the base tier qualifies.
        var atZero = EnemyCatalog.Shared.Available(0).Select(d => d.Key).ToList();
        Assert.Contains("runner", atZero);
        Assert.DoesNotContain("runner_hard", atZero);

        var atTwenty = EnemyCatalog.Shared.Available(20).Select(d => d.Key).ToList();
        Assert.Contains("runner_hard", atTwenty);
    }

    [Fact]
    public void Choose_NeverReturnsGuaranteedOnlyOrBannerFamily()
    {
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var chosen = EnemyCatalog.Shared.Choose(20, rng);
            Assert.NotNull(chosen);
            Assert.False(chosen!.GuaranteedOnly);
            Assert.NotEqual("banner", chosen.Family);
        }
    }

    [Fact]
    public void Create_Runner_ProducesPlainEnemy()
    {
        var enemy = EnemyCatalog.Shared.Create("runner", 100, 100, level: 1, awarenessRange: 300f, rng: new Random(1));
        Assert.Equal(typeof(Enemy), enemy.GetType());
        Assert.Equal("runner", enemy.Family);
        Assert.Equal("runner", enemy.SpawnDefinitionKey);
        Assert.Equal("pressure", enemy.CombatRole);
    }

    [Fact]
    public void Create_Snake_BuildsExpectedSegmentCount()
    {
        // "snake" (easy tier, rank 1) -> segmentCount = 3 + 1*2 = 5; +1 head hitbox.
        var enemy = EnemyCatalog.Shared.Create("snake", 100, 100, level: 5, awarenessRange: 300f, rng: new Random(1));
        var snake = Assert.IsType<SnakeEnemy>(enemy);
        Assert.Equal(6, snake.GetWorldHitboxes().Count);
    }

    [Fact]
    public void Create_Banner_SpawnsMinionsAsSpawnedEnemies()
    {
        var battleground = Room();
        var enemy = EnemyCatalog.Shared.Create("banner", 125, 125, level: 3, awarenessRange: 300f, rng: new Random(1), battleground: battleground);
        var captain = Assert.IsType<BannerCaptain>(enemy);
        Assert.NotEmpty(captain.SpawnedEnemies);
        Assert.All(captain.SpawnedEnemies, minion => Assert.IsType<BannerMinion>(minion));
        Assert.True(captain.AtomicSpawnGroup);
    }

    [Fact]
    public void ApplyModifier_Champion_ScalesStatsUp()
    {
        var enemy = EnemyCatalog.Shared.Create("bulwark", 100, 100, level: 12, awarenessRange: 300f, rng: new Random(1));
        int originalMaxHp = enemy.MaxHp;
        float originalSize = enemy.Size;
        double originalExp = enemy.ExpValue;

        EnemyCatalog.Shared.ApplyModifier(enemy, level: 20, rng: new Random(1), forced: "champion");

        Assert.Equal("champion", enemy.BehaviorModifier);
        Assert.True(enemy.MaxHp > originalMaxHp);
        Assert.True(enemy.Size > originalSize);
        Assert.True(enemy.ExpValue > originalExp);
    }

    [Fact]
    public void SpawnEncounter_FailsClosed_WhenThreatBudgetTooLow()
    {
        var battleground = Battleground.GenerateSound();
        var result = EnemyCatalog.Shared.SpawnEncounter(
            level: 10, maxThreat: 0.01, battleground, playerWorldPosition: battleground.SpawnPosition,
            awarenessRange: 300f, screenHeight: 1080f, rng: new Random(1));
        Assert.Null(result);
    }

    [Fact]
    public void SpawnEncounter_Succeeds_WithGenerousThreatBudget_AndAttachesEncounter()
    {
        var battleground = Battleground.GenerateSound();
        var result = EnemyCatalog.Shared.SpawnEncounter(
            level: 10, maxThreat: 100.0, battleground, playerWorldPosition: battleground.SpawnPosition,
            awarenessRange: 300f, screenHeight: 1080f, rng: new Random(3));
        Assert.NotNull(result);
        var (package, group) = result!.Value;
        Assert.True(group.Count >= package.Families.Count);
        Assert.All(group, enemy => Assert.NotNull(enemy.Encounter));
        Assert.All(group, enemy => Assert.Equal(package.Key, enemy.EncounterKey));
    }

    [Fact]
    public void SpawnPatrol_Succeeds_WithGenerousThreatBudget()
    {
        var battleground = Battleground.GenerateSound();
        var result = EnemyCatalog.Shared.SpawnPatrol(
            level: 5, maxThreat: 100.0, battleground, playerWorldPosition: battleground.SpawnPosition,
            awarenessRange: 300f, screenHeight: 1080f, rng: new Random(5));
        Assert.NotNull(result);
        var (encounter, group) = result!.Value;
        Assert.NotEmpty(group);
        Assert.All(group, enemy => Assert.Same(encounter, enemy.Encounter));
    }
}
