using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public sealed class SoulHubTests
{
    [Theory]
    [InlineData("armory:0", 0)]
    [InlineData("armory:7", 7)]
    [InlineData("armory:10", 10)]
    [InlineData("armory:123", 123)]
    public void DeveloperArmoryClickTargetsPreserveTheCompleteItemIndex(
        string target, int expected)
    {
        Assert.True(SoulHub.TryArmoryIndex(target, out int actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("armory:")]
    [InlineData("armory:item")]
    [InlineData("armory:-1")]
    [InlineData("storage")]
    public void MalformedDeveloperArmoryClickTargetsAreIgnored(string target)
    {
        Assert.False(SoulHub.TryArmoryIndex(target, out _));
    }

    [Fact]
    public void DeveloperArmoryContainsAndDispensesPerfectCopiesOfEveryItem()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData { DeveloperArmory = true };
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));

            Assert.Equal(Items.Definitions.Count + Items.Uniques.Count,
                SoulHub.DeveloperArmoryItems.Count);
            Assert.True(SoulHub.TakeArmoryItem(session, 0));
            ItemDrop item = Assert.IsType<ItemDrop>(session.State.Inventory[0]);
            Assert.Equal("S", item.Grade);
            Assert.Equal("Godly", item.Modifier);
            Assert.Equal("Mythical", item.Rarity);

            int uniqueIndex = SoulHub.DeveloperArmoryItems.Count - 1;
            Assert.True(SoulHub.TakeArmoryItem(session, uniqueIndex));
            Assert.Equal("Unique", session.State.Inventory[1]!.Rarity);

            for (int i = 2; i < session.State.Inventory.Count; i++)
                session.State.Inventory[i] = item;
            Assert.False(SoulHub.TakeArmoryItem(session, 0));
        }
        finally
        {
            GameProfile.Profile = originalProfile;
        }
    }

    [Fact]
    public void DeveloperArmoryRejectsItemsWhenDisabled()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData();
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
            Assert.False(SoulHub.TakeArmoryItem(session, 0));
        }
        finally
        {
            GameProfile.Profile = originalProfile;
        }
    }

    [Fact]
    public void F8_TogglesDevUnlockTestingAndImmediatelyRebuildsTheMind()
    {
        var originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-mind-dev-tests-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");
            CampaignDevOverrides.Reset();
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
            var mindHub = new MindHub();
            mindHub.Enter(session);

            mindHub.HandleInput(session, new HashSet<Keys> { Keys.F8 },
                Point.Zero, false, false);

            Assert.True(GameProfile.Profile.DevUnlockTesting);
            Assert.Equal(SoulLayout.SpawnTile,
                new Point(
                    (int)(session.PlayerWorldCenter.X / Battleground.TileSize),
                    (int)(session.PlayerWorldCenter.Y / Battleground.TileSize)));
            Assert.True(File.Exists(GameProfile.SavePath));

            mindHub.HandleInput(session, new HashSet<Keys> { Keys.F8 },
                Point.Zero, false, false);
            Assert.False(GameProfile.Profile.DevUnlockTesting);
        }
        finally
        {
            CampaignDevOverrides.Reset();
            GameProfile.Profile = originalProfile;
            GameProfile.SavePath = originalPath;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DevPortalControlImmediatelyRebuildsItsPhysicalMindGate()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData { DevUnlockTesting = true };
            CampaignDevOverrides.Reset();
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
            var mindHub = new MindHub();
            mindHub.Enter(session);
            int lockedWalls = CountTiles(session.Battleground, TileType.BuildingWall);

            mindHub.HandleDevAction(session, "portal:core");

            Assert.True(CampaignProgression.PortalUnlocked("core"));
            Assert.True(CountTiles(session.Battleground, TileType.BuildingWall) < lockedWalls);

            mindHub.HandleDevAction(session, "reset");
            Assert.False(CampaignProgression.PortalUnlocked("core"));
            Assert.Equal(lockedWalls,
                CountTiles(session.Battleground, TileType.BuildingWall));
        }
        finally
        {
            CampaignDevOverrides.Reset();
            GameProfile.Profile = originalProfile;
        }
    }

    private static int CountTiles(Battleground battleground, TileType tile)
    {
        int count = 0;
        for (int y = 0; y < battleground.Height; y++)
            for (int x = 0; x < battleground.Width; x++)
                if (battleground.TileAt(x, y) == tile)
                    count++;
        return count;
    }

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
    public void BothBraziersCanBeEnabledBeforeAnyCampaignCompletion()
    {
        var originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-free-braziers-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData();
            CampaignProgression.Normalize(GameProfile.Profile.Campaign);
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));

            SoulHub.ToggleHardMode(session);
            SoulHub.ToggleNoExtract(session);

            Assert.True(session.State.NoHealing);
            Assert.True(session.State.NoExtract);
            Assert.False(CampaignProgression.Data.BodyUnlocked);
            Assert.False(CampaignProgression.Data.AphantasiaUnlocked);
        }
        finally
        {
            GameProfile.Profile = originalProfile;
            GameProfile.SavePath = originalPath;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AdjustNewGamePlus_StopsAtThePathsUnlockedTier()
    {
        var originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-ng-plus-tests-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");
            GameProfile.Profile.NewGamePlusUnlocked["sound"] = 2;

            Assert.True(SoulHub.AdjustNewGamePlus("sound", 1));
            Assert.True(SoulHub.AdjustNewGamePlus("sound", 1));
            Assert.False(SoulHub.AdjustNewGamePlus("sound", 1));
            Assert.Equal(2, NewGamePlus.SelectedLevel("sound"));
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

    [Theory]
    [InlineData(1000, 1000, 0)]
    [InlineData(750, 1000, .5)]
    [InlineData(500, 1000, 1)]
    [InlineData(250, 1000, 1)]
    public void TunnelAwakening_AdvancesNorthWithThePlayer(float playerY, float tunnelStartY, float expected)
    {
        Assert.Equal(expected, SoulHub.TunnelAwakening(playerY, tunnelStartY, 500), precision: 3);
    }

    [Fact]
    public void PortalCorruptionScale_GrowsWithSelectedNgTier()
    {
        Assert.Equal(1f, SoulHub.PortalCorruptionScale(0));
        Assert.True(SoulHub.PortalCorruptionScale(7) > SoulHub.PortalCorruptionScale(3));
        Assert.Equal(SoulHub.PortalCorruptionScale(7), SoulHub.PortalCorruptionScale(99));
    }

    [Fact]
    public void SoulLayout_DrivesEveryInteractionAnchor()
    {
        var session = new GameSession(Battleground.GenerateSoul(), 1280, 720, new Random(1));
        var soulHub = new SoulHub();
        soulHub.Enter(session);

        Assert.Equal(SoulLayout.TileWorldCenter(SoulLayout.DummyTile), soulHub.DummyWorld);
        Assert.Equal(SoulLayout.TileWorldCenter(SoulLayout.NexusTile), soulHub.CompositePortalWorld);
        Assert.Equal(SoulLayout.TileWorldCenter(SoulLayout.NexusTile),
            soulHub.PortalWorld(SoulHub.CorePortalKey));
        Assert.Equal(SoulLayout.TileWorldCenter(SoulLayout.CorePortalTile),
            soulHub.PortalWorld(SoulHub.BodyPortalKey));
        Assert.Equal(SoulLayout.TileWorldCenter(SoulLayout.AphantasiaPortalTile),
            soulHub.PortalWorld(SoulHub.AphantasiaPortalKey));
        Assert.Throws<KeyNotFoundException>(() => soulHub.PortalWorld("__soul_world"));
        foreach (var (key, tile) in SoulLayout.StationTiles)
            Assert.Equal(SoulLayout.TileWorldCenter(tile), soulHub.StationWorld(key));
        foreach (var (key, tile) in SoulLayout.PortalTiles)
            Assert.Equal(SoulLayout.TileWorldCenter(tile), soulHub.PortalWorld(key));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(99, 5)]
    public void MasteryTier_UsesABoundedArchitecturalProgression(int mastery, int expected)
    {
        Assert.Equal(expected, SoulVisualRenderer.MasteryTier(mastery));
    }

    [Fact]
    public void OptionalSoulEffects_DisappearAtZeroIntensity()
    {
        Assert.Equal(0, SoulVisualRenderer.OptionalEffectCount(24, 0));
        Assert.Equal(12, SoulVisualRenderer.OptionalEffectCount(24, .5f));
        Assert.Equal(24, SoulVisualRenderer.OptionalEffectCount(24, 1));
    }

    [Fact]
    public void SoulVeinsWakeLocallyAsThePlayerCrossesThem()
    {
        Vector2 start = Vector2.Zero;
        Vector2 end = new(Battleground.TileSize * 4, 0);

        float crossing = SoulVisualRenderer.VeinProximity(
            new Vector2(Battleground.TileSize * 2, 0),
            start, end);
        float nearby = SoulVisualRenderer.VeinProximity(
            new Vector2(
                Battleground.TileSize * 2,
                Battleground.TileSize * 1.25f),
            start, end);
        float distant = SoulVisualRenderer.VeinProximity(
            new Vector2(
                Battleground.TileSize * 2,
                Battleground.TileSize * 8),
            start, end);

        Assert.Equal(1, crossing);
        Assert.InRange(nearby, .4f, .7f);
        Assert.Equal(0, distant);
    }

    [Theory]
    [InlineData(null, null, null, 0)]
    [InlineData("sound", null, null, 1)]
    [InlineData("sound", "sound", null, 2)]
    [InlineData("sound", "sound", "sound", 3)]
    public void PortalPresentationState_FollowsInteractionPriority(
        string? nearby,
        string? confirming,
        string? entering,
        int expected)
    {
        Assert.Equal(expected,
            (int)SoulVisualRenderer.ResolvePortalState("sound", nearby, confirming, entering));
    }

    [Fact]
    public void EnteringPortalChamber_DoesNotChangeCameraZoom()
    {
        var session = new GameSession(Battleground.GenerateSoul(), 1280, 720, new Random(1));
        session.Camera.SetZoom(1f);
        var soulHub = new SoulHub();
        soulHub.Enter(session);
        float originalZoom = session.Camera.Zoom;

        Vector2 chamberThreshold = session.Battleground.SpawnPosition
            + new Vector2(Battleground.TileSize / 2f, Battleground.TileSize * -30f);
        session.Player.SetPosition(chamberThreshold.X - (float)session.State.PlayerSize / 2f,
            chamberThreshold.Y - (float)session.State.PlayerSize / 2f);
        for (int tick = 0; tick < 184; tick++)
            soulHub.Update(session, 1.0 / 60);

        Assert.Equal(originalZoom, session.Camera.Zoom, precision: 3);
    }

    [Fact]
    public void ConvergencePortal_ConfirmsAndReturnsCompositePathDestination()
    {
        var session = new GameSession(Battleground.GenerateSoul(), 1280, 720, new Random(1));
        var soulHub = new SoulHub();
        soulHub.Enter(session);
        Vector2 portal = soulHub.CompositePortalWorld;
        float half = (float)session.State.PlayerSize / 2f;
        session.Player.SetPosition(portal.X - half, portal.Y - half);

        Assert.Null(soulHub.HandleInput(
            session, new HashSet<Keys> { Keys.F }, Point.Zero, false, false));
        Assert.True(soulHub.OverlayOpen);
        Assert.Null(soulHub.HandleInput(
            session, new HashSet<Keys> { Keys.F }, Point.Zero, false, false));
        Assert.True(soulHub.IsEnteringPortal);

        for (int tick = 0; tick < 30; tick++)
            soulHub.Update(session, .05);

        Assert.Equal(SoulHub.CorePortalKey, soulHub.HandleInput(
            session, new HashSet<Keys>(), Point.Zero, false, false));
    }

    [Fact]
    public void ControllerCanOpenAndConfirmCriticalMindPortalsWithoutAKeyboard()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
        var mindHub = new MindHub();
        mindHub.Enter(session);
        Vector2 portal = mindHub.CompositePortalWorld;
        float half = (float)session.State.PlayerSize / 2f;
        session.Player.SetPosition(portal.X - half, portal.Y - half);
        try
        {
            InputState.ControllerInteractPressed = true;
            Assert.Null(mindHub.HandleInput(
                session, new HashSet<Keys>(), Point.Zero, false, false));
            Assert.True(mindHub.OverlayOpen);

            InputState.ControllerInteractPressed = false;
            InputState.ControllerBackPressed = false;
            InputState.ControllerConfirmPressed = true;
            Assert.Null(mindHub.HandleInput(
                session, new HashSet<Keys>(), Point.Zero, false, false));
            Assert.True(mindHub.IsEnteringPortal);
        }
        finally
        {
            InputState.ControllerInteractPressed = false;
            InputState.ControllerConfirmPressed = false;
            InputState.ControllerBackPressed = false;
        }
    }
}
