using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Systems;

public sealed class BossEncounterFrameworkTests
{
    [Fact]
    public void FloorProfiles_MatchTheNormalizedTenFloorCurve()
    {
        var expected = new (double Health, double Damage, double Timing, int Complexity)[]
        {
            (1.00, 1.00, 1.00, 0), (1.10, 1.05, .98, 0),
            (1.20, 1.10, .96, 1), (1.32, 1.15, .94, 1),
            (1.50, 1.22, .92, 2), (1.80, 1.38, .88, 3),
            (1.98, 1.46, .86, 3), (2.16, 1.54, .84, 4),
            (2.36, 1.63, .82, 4), (2.65, 1.75, .80, 5),
        };

        for (int floor = 1; floor <= expected.Length; floor++)
        {
            DungeonFloorDifficultyProfile profile =
                DungeonFloorDifficultyProfile.ForFloor(floor);
            Assert.Equal(expected[floor - 1].Health, profile.Health, 2);
            Assert.Equal(expected[floor - 1].Damage, profile.Damage, 2);
            Assert.Equal(expected[floor - 1].Timing, profile.Timing, 2);
            Assert.Equal(expected[floor - 1].Complexity, profile.Complexity);
        }
    }

    [Fact]
    public void AttackDirector_PreventsRepeatsAndGuaranteesSignatureWithinThreeDeclarations()
    {
        var director = new BossAttackDirector();
        var rng = new Random(17);
        var choices = Enumerable.Range(0, 30)
            .Select(_ => director.Choose(4, 3, [8f, 4f, 2f, .01f], rng))
            .ToArray();

        Assert.All(choices.Zip(choices.Skip(1)), pair =>
            Assert.NotEqual(pair.First, pair.Second));
        for (int start = 0; start <= choices.Length - 3; start += 3)
            Assert.Contains(3, choices.Skip(start).Take(3));
    }

    [Fact]
    public void EveryMajorBossHasAStandaloneVoidBackedArenaAndEncounterDefinition()
    {
        string[] keys =
        [
            "beaudis", "bair", "ishe", "kage", "hypno",
            "dissonance", "rot", "chronos", "ache", "malady",
            "aphantasia",
        ];

        Assert.Equal(keys.Order(), BossEncounterCatalog.All.Select(value => value.BossKey).Order());
        foreach (string key in keys)
        {
            BossEncounterDefinition encounter = BossEncounterCatalog.DefinitionFor(key);
            Battleground arena = BossArenaFactory.Create(key, encounter.Tier);
            Assert.Equal(key, encounter.Arena.BossKey);
            Assert.Equal(TileType.OuterVoid, arena.TileAt(0, 0));
            Assert.False(arena.TileAt(arena.Width / 2, arena.Height / 2).IsSolid());
            Assert.NotEmpty(encounter.Phases);
        }
    }

    [Fact]
    public void EveryBossWithAnAuthoredShapeProvidesPersistentArenaOcclusion()
    {
        string[] shapedBosses =
        [
            "bair", "ishe", "kage", "hypno",
            "dissonance", "rot", "chronos", "ache", "malady",
            "aphantasia",
        ];

        foreach (string key in shapedBosses)
        {
            BossEncounterDefinition encounter =
                BossEncounterCatalog.DefinitionFor(key);
            Battleground arena = BossArenaFactory.Create(
                key,
                encounter.Tier);
            Enemy boss = BossCatalog.Shared.Spawn(
                key,
                arena,
                float.PositiveInfinity,
                new Random(91));

            Assert.IsAssignableFrom<IBossArenaOcclusion>(boss);
        }
    }

    [Fact]
    public void PathRun_SelectsExactlyTwoHiddenTreasureFloorsPerAct()
    {
        for (int seed = 0; seed < 12; seed++)
        {
            var run = new PathRun(new Random(seed));
            Assert.Equal(2, run.TreasureFloors.Count(floor => floor is >= 1 and <= 4));
            Assert.Equal(2, run.TreasureFloors.Count(floor => floor is >= 6 and <= 9));
            Assert.DoesNotContain(5, run.TreasureFloors);
            Assert.DoesNotContain(10, run.TreasureFloors);
        }
    }

    [Fact]
    public void ForcedTreasureArena_IsHiddenLargeAndOpensFromItsClue()
    {
        PathFloorLayout layout = PathFloorGenerator.Generate(
            "sight", 3, new Random(73), containsTreasureArena: true);
        PathRoom treasure = Assert.Single(layout.TreasureRooms);
        PathConnection connection = Assert.Single(layout.Connections, value => value.Hidden);

        Assert.True(treasure.TileBounds.Width >= 23);
        Assert.True(treasure.TileBounds.Height >= 23);
        Assert.False(treasure.IsRevealed);
        Assert.False(connection.IsRevealed);
        Assert.Null(layout.RoomAt(treasure.WorldCenter));

        Point clue = Assert.IsType<Point>(connection.ClueTile);
        var clueWorld = new Vector2(
            (clue.X + .5f) * Battleground.TileSize,
            (clue.Y + .5f) * Battleground.TileSize);
        Assert.True(layout.TryRevealTreasure(clueWorld, 1f));
        Assert.True(treasure.IsRevealed);
        Assert.True(connection.IsRevealed);
        Assert.Same(treasure, layout.RoomAt(treasure.WorldCenter));
    }
}
