using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public sealed class SoulHubTests
{
    [Fact]
    public void ToggleHardMode_PersistsSelectionAndUpdatesCurrentSoulState()
    {
        var originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-hard-mode-tests-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");
            var session = new GameSession(Battleground.GenerateSound(), 1280, 720, new Random(1));

            SoulHub.ToggleHardMode(session);

            Assert.True(GameProfile.Profile.HardModeEnabled);
            Assert.True(session.State.HardMode);
            Assert.True(File.Exists(GameProfile.SavePath));
        }
        finally
        {
            GameProfile.Profile = originalProfile;
            GameProfile.SavePath = originalPath;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void StationRadius_ClosesOnlyAfterPlayerWalksBeyondDismissalDistance()
    {
        var station = new Vector2(400, 300);
        var justInside = station + new Vector2(Simulation.TileSize * 1.84f, 0);
        var justOutside = station + new Vector2(Simulation.TileSize * 1.86f, 0);

        Assert.True(SoulHub.WithinStationRadius(justInside, station, 1.85f));
        Assert.False(SoulHub.WithinStationRadius(justOutside, station, 1.85f));
    }

    [Fact]
    public void Update_LetsGrimsbaneStackBaneOnTheDummy_NotJustRawBulletDamage()
    {
        // Regression test: the DPS dummy used to be a bare world position with
        // no Enemy/StatusEffects state, so status effects (bleed, bane, dread,
        // ...) and unique on-hit effects could never land on it -- only the
        // bullet's raw, un-modified damage counted. SoulHub.Update now routes
        // dummy hits through StatusEffects.RollPlayerHit/UniqueEffects.OnPlayerHit
        // exactly like a real enemy would (see SoulHub's TrainingDummy field).
        var session = new GameSession(Battleground.GenerateSound(), 1280, 720, new Random(1));
        var grimsbane = new ItemDrop(Items.UniquesByName["Grimsbane"], "Unique");
        session.State.SetEquipment(new Dictionary<string, ItemDrop?> { ["weapon"] = grimsbane });
        var soulHub = new SoulHub();
        soulHub.Enter(session);

        var bullet = new Bullet(soulHub.DummyWorld.X, soulHub.DummyWorld.Y, speed: 0, direction: 0,
            bulletRange: 100, size: 10, Color.White, pierce: 1, damage: 10, isCritical: false);
        session.State.BulletHolster.Add(bullet);

        soulHub.Update(session, 1.0 / 60);

        Assert.True(soulHub.DummyHasStatus("bane"), "bane_on_hit should stack on every hit, with no roll needed.");
        Assert.True(soulHub.CurrentDps > 0);
    }

    [Fact]
    public void TrainingDummy_BleedTicks_ScaleFromBossTierHp_NotMillions()
    {
        // Regression test: the dummy used to be built with a billion-HP pool
        // to make it "unkillable" for the DPS meter, but StatusEffects.Update's
        // bleed DoT scales off enemy.MaxHp (percent-of-max-health per stack)
        // -- against that billion-HP pool, a handful of bleed stacks ticked
        // for millions of damage per second instead of a boss-realistic
        // amount. TrainingDummy is unkillable via its TakeDamage override
        // resetting Hp every hit, not via an inflated MaxHp, so this should
        // never regress back to that scale.
        var dummy = new TrainingDummy(0, 0);
        StatusEffects.Apply(dummy, "bleed", duration: 3.2, potency: .006, stacks: 8);

        StatusEffects.Update(dummy, 1.0);

        Assert.True(dummy.UnrecordedDamage < 5000, $"Expected boss-realistic bleed damage for one second at 8 stacks, got {dummy.UnrecordedDamage}.");
    }
}
