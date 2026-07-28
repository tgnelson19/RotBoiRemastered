using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Systems;

public sealed class PathRunTests
{
    [Fact]
    public void Constructor_BuildsTwoCompleteSenseActsWithoutBoundaryRepeat()
    {
        var expected = GamePaths.Paths.Select(path => path.Key).OrderBy(key => key).ToArray();
        for (int seed = 0; seed < 100; seed++)
        {
            var run = new PathRun(new Random(seed));
            Assert.Equal(PathRun.TotalFloors, run.SenseOrder.Count);
            Assert.Equal(expected, run.SenseOrder.Take(5).OrderBy(key => key));
            Assert.Equal(expected, run.SenseOrder.Skip(5).Take(5).OrderBy(key => key));
            Assert.NotEqual(run.SenseOrder[4], run.SenseOrder[5]);
        }
    }

    [Fact]
    public void TitleBanner_UsesRequiredCopyAndExpires()
    {
        var run = new PathRun(new Random(2));

        Assert.Equal($"Traversing the Path of {run.SenseDisplayName}", run.TitleBanner);
        Assert.True(run.TitleBannerVisible(0));
        Assert.False(run.TitleBannerVisible(PathRun.TitleBannerSeconds + .01));
    }

    [Fact]
    public void CombatRooms_CanOverlapAndCompleteIndependentlyWhileRushing()
    {
        var run = new PathRun(new Random(3));
        var skirmish = run.Layout.Rooms.Single(value => value.Type == PathRoomType.Skirmish);
        var assault = run.Layout.Rooms.Single(value => value.Type == PathRoomType.Assault);

        Assert.Same(skirmish, run.TryActivateRoom(skirmish.WorldCenter));
        Assert.Same(assault, run.TryActivateRoom(assault.WorldCenter));
        Assert.Equal(new[] { skirmish, assault }, run.ActiveCombatRooms);

        var enemy = new Enemy(skirmish.WorldCenter.X, skirmish.WorldCenter.Y, 1, 20, Color.Red,
            10, 10, 1, 1, 100) { EncounterKey = skirmish.EncounterKey };
        IReadOnlyList<PathRoom> firstCompletion =
            run.CompleteReadyCombatRooms(new[] { enemy });
        Assert.Equal(new[] { assault }, firstCompletion);
        Assert.False(skirmish.IsCleared);
        Assert.True(assault.IsCleared);

        Assert.Equal(new[] { skirmish }, run.CompleteReadyCombatRooms(Array.Empty<Enemy>()));
        Assert.Equal(new[] { assault }, firstCompletion);
        Assert.True(skirmish.IsCleared);
        Assert.Empty(run.ActiveCombatRooms);
    }

    [Fact]
    public void BossDefeat_OpensPortalAndAdvanceBuildsNextSenseFloor()
    {
        var run = new PathRun(new Random(8));
        string firstSense = run.CurrentSenseKey;

        run.NotifyBossDefeated();
        Assert.True(run.ExitPortalOpen);
        Assert.True(run.AdvanceFloor(42.5));

        Assert.Equal(2, run.FloorNumber);
        Assert.NotEqual(firstSense, run.CurrentSenseKey);
        Assert.False(run.ExitPortalOpen);
        Assert.Equal(42.5, run.FloorStartedAtRunSeconds);
        Assert.True(run.Layout.StartRoom.IsCleared);
    }

    [Fact]
    public void Difficulty_JumpsSharplyAtSecondAct()
    {
        var run = new PathRun(new Random(9));
        while (run.FloorNumber < 5)
        {
            run.NotifyBossDefeated();
            Assert.True(run.AdvanceFloor(run.FloorNumber * 10));
        }
        double floorFiveHealth = run.HealthMultiplier;
        run.NotifyBossDefeated();
        Assert.True(run.AdvanceFloor(50));

        Assert.True(run.IsSecondAct);
        Assert.True(run.HealthMultiplier >= floorFiveHealth * 1.3);
        Assert.True(run.DamageMultiplier > 1.4);
    }

    [Fact]
    public void RoomEntry_ExposesShortShapeAwareAnnouncement()
    {
        var run = new PathRun(new Random(22));
        var room = run.Layout.Rooms.First(value => value.IsCombatRoom);

        Assert.Same(room, run.TryActivateRoom(room.WorldCenter, 12.0));
        Assert.Same(room, run.LastEnteredRoom);
        Assert.Contains(room.Type.ToString().ToUpperInvariant(), room.EntryBanner);
        Assert.Contains(room.ShapeDisplayName.ToUpperInvariant(), room.EntryBanner);
        Assert.True(run.RoomBannerVisible(12.0));
        Assert.False(run.RoomBannerVisible(12.0 + PathRun.RoomBannerSeconds + .01));
    }
}
