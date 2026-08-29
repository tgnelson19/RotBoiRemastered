using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Systems;

/// <summary>
/// Ported from character.py's core gameplay-loop functions (see
/// GameSession.cs's doc comment for the full list of deferred boss/HUD
/// branches). Draw-only paths need a GraphicsDevice and are covered by
/// visual smoke testing instead, same as the rest of this port's UI layer.
/// </summary>
[Collection("GameProfileState")]
public class GameSessionTests
{
    private sealed class MinimumRandom : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static GameSession MakeSession(int level = 1)
    {
        GamePaths.Select("sound");
        var session = new GameSession(GamePaths.ActivateSelected(), 1280, 720, new Random(1));
        session.State.CurrentLevel = level;
        return session;
    }

    [Fact]
    public void ArenaExtractionUnlocksOnlyAfterTheMidpointBoss()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720,
            new Random(1));
        session.StartArena("sound", new Random(2));

        Assert.False(session.CanExtract);
        session.State.BeaudisDefeated = true;
        Assert.True(session.CanExtract);
        session.State.SetNoExtract(true);
        Assert.False(session.CanExtract);
    }

    [Fact]
    public void RestartingArenaPreservesItsCampaignIdentityAndSense()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720,
            new Random(1));
        session.StartArena("touch", new Random(2));
        session.State.CurrentLevel = 12;

        session.RestartCurrentRun(new Random(3));

        Assert.Equal(CampaignActivity.Arena, session.CampaignActivity);
        Assert.Equal("touch", session.CampaignActivitySense);
        Assert.Equal("touch", GamePaths.Active().Key);
        Assert.Equal(0, session.State.CurrentLevel);
        Assert.Equal(GamePaths.PathsByKey["touch"].Title,
            session.EntrySplash.Title);
    }

    [Fact]
    public void ModeEntrySplashProtectsThePlayerForItsFullPresentation()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720,
            new Random(1));

        session.StartArena("sight", new Random(2));

        Assert.True(session.State.GracePeriod
            >= Simulation.FrameRate * ModeEntrySplash.Duration);
    }

    [Fact]
    public void CompletedBodyContinuesIntoSoulWithoutResettingTheLiveBuild()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-body-soul-chain-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData();
            CampaignProgression.Normalize(GameProfile.Profile.Campaign);
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
            session.StartExpedition(CampaignWorld.Body, rng: new Random(2));
            ExpeditionRun body = Assert.IsType<ExpeditionRun>(session.Expedition);
            foreach (ExpeditionSecret secret in body.Secrets.OrderBy(secret => secret.IsFinale))
            {
                Assert.True(body.SolveSecret(secret.SenseKey));
                Assert.True(body.EnterDungeon(secret.SenseKey, secret.WorldPosition));
                Assert.True(body.CompleteDungeon());
            }

            var weapon = new ItemDrop(Items.DefinitionsByName["Iron Dagger"], "Epic");
            session.State.CurrentLevel = 9;
            session.State.Fragments = 17;
            session.State.ExpCount = 321;
            session.State.HealthPoints = 777;
            session.State.RunTimeSeconds = 123;
            session.State.Equipment["weapon"] = weapon;
            session.State.Inventory[0] = weapon;
            session.State.SetNoExtract(true);

            session.ContinueCompletedBodyIntoSoul(new Random(3));

            Assert.Equal(CampaignWorld.Soul, session.Expedition!.World);
            Assert.Equal(CampaignActivity.Soul, session.CampaignActivity);
            Assert.Equal(session.Expedition.FinaleSense, session.CampaignActivitySense);
            Assert.Equal(9, session.State.CurrentLevel);
            Assert.Equal(17, session.State.Fragments);
            Assert.Equal(321, session.State.ExpCount);
            Assert.Equal(777, session.State.HealthPoints);
            Assert.Equal(123, session.State.RunTimeSeconds);
            Assert.Same(weapon, session.State.Equipment["weapon"]);
            Assert.Same(weapon, session.State.Inventory[0]);
            Assert.True(session.State.NoExtract);
            Assert.True(CampaignProgression.Data.BodyCompleted);
            Assert.True(CampaignProgression.Data.SoulUnlocked);
            Assert.DoesNotContain(session.Expedition.FinaleSense,
                CampaignProgression.Data.ArenaUnlocks);
        }
        finally
        {
            GameProfile.Profile = originalProfile;
            GameProfile.SavePath = originalPath;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void StandaloneDungeonCompletionDoesNotAdvanceMetaProgression()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-neutral-dungeon-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData
            {
                CompletedQuests = MetaProgression.Quests.Select(quest => quest.Key).ToList(),
            };
            CampaignProgression.Normalize(GameProfile.Profile.Campaign);
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));

            session.StartPathRun(new Random(2));
            RunRewardSummary reward = session.FinalizeSuccessfulRun("DUNGEON COMPLETE", completed: true);

            Assert.Equal(0, reward.MindTokenDelta);
            Assert.Equal(0, GameProfile.Profile.PathMastery.GetValueOrDefault(NewGamePlus.DungeonKey));
            Assert.Equal(0, NewGamePlus.UnlockedLevel(NewGamePlus.DungeonKey));
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
    public void ActiveBossKeepsArenaOcclusionWhenItsBodyIsAbsentFromDrawHolster()
    {
        GameSession session = MakeSession();
        var boss = new Dissonance(
            1000,
            1000,
            float.PositiveInfinity,
            session.Battleground,
            new Random(92));
        session.State.ActiveBoss = boss;
        session.State.EnemyHolster.Clear();

        Assert.True(session.PersistentBossArenaActive);

        session.State.ActiveBoss = null;
        Assert.False(session.PersistentBossArenaActive);
    }

    /// <summary>
    /// A natural boss encounter no longer starts the instant the level
    /// threshold is reached -- HandleEnemyCreation only spawns the boss once
    /// the player has walked into the portal at the map's center (see
    /// GameSession.PlayerAtBossPortal/ArenaCenterWorld). Tests that want
    /// "the natural trigger fires" call this first to simulate walking in.
    /// </summary>
    private static void MoveToArenaCenter(GameSession session)
    {
        float x = session.Battleground.Width * Simulation.TileSize / 2f;
        float y = session.Battleground.Height * Simulation.TileSize / 2f;
        session.Player.SetPosition(x, y);
    }

    /// <summary>The default spawn position (Battleground.SpawnPosition) *is* the map center, so tests wanting "not at the portal" need to explicitly move away from it rather than just leaving the player at their starting position.</summary>
    private static void MoveAwayFromArenaCenter(GameSession session) => session.Player.SetPosition(0, 0);

    private static void MoveToPathRoom(GameSession session, PathRoomType type)
    {
        if (type == PathRoomType.Treasure)
        {
            foreach (PathConnection connection in session.PathRun!.Layout.Connections
                .Where(value => value.Hidden && !value.IsRevealed))
            {
                Point clue = Assert.IsType<Point>(connection.ClueTile);
                Vector2 clueWorld = new(
                    (clue.X + .5f) * Battleground.TileSize,
                    (clue.Y + .5f) * Battleground.TileSize);
                Assert.True(session.PathRun.Layout.TryRevealTreasure(clueWorld, 1f));
            }
        }
        var room = session.PathRun!.Layout.Rooms.First(value => value.Type == type);
        session.Player.SetPosition(
            room.WorldCenter.X - (float)session.State.PlayerSize / 2f,
            room.WorldCenter.Y - (float)session.State.PlayerSize / 2f);
    }

    private static void AdvancePathToFloor(GameSession session, int floor)
    {
        while (session.PathRun!.FloorNumber < floor)
        {
            session.PathRun.NotifyBossDefeated();
            Vector2 portal = session.DungeonBossInstanceActive
                ? new Vector2(
                    session.Battleground.Width * Simulation.TileSize / 2f,
                    session.Battleground.Height * Simulation.TileSize / 2f)
                : session.PathRun.ExitPortalWorld;
            session.Player.SetPosition(portal.X, portal.Y);
            session.HandleEnemyCreation(
                new Random(8000 + session.PathRun.FloorNumber),
                interactPressed: true);
        }
    }

    private static void FinishPendingPathWaves(
        GameSession session,
        int seed = 9000)
    {
        for (int frame = 0;
            frame < 20 && session.HasPendingPathWaves;
            frame++)
        {
            session.HandleEnemyCreation(new Random(seed + frame));
        }
        Assert.False(session.HasPendingPathWaves);
    }

    [Fact]
    public void Constructor_PositionsPlayerAtBattlegroundSpawn()
    {
        var battleground = Battleground.GenerateSound();
        var session = new GameSession(battleground, 1280, 720);
        Assert.Equal(battleground.SpawnPosition.X, session.Player.WorldX);
        Assert.Equal(battleground.SpawnPosition.Y, session.Player.WorldY);
    }

    [Fact]
    public void Constructor_LoadsCarriedEquipmentFromProfile()
    {
        var original = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.Profile.CarriedEquipment["weapon"] = new StoredItemData("Iron Dagger", "Epic");

            var session = new GameSession(Battleground.GenerateSound(), 1280, 720);

            Assert.Equal("Iron Dagger", session.State.Equipment["weapon"]!.Name);
        }
        finally
        {
            GameProfile.Profile = original;
        }
    }

    [Fact]
    public void HandleBulletCreation_FiresWhenAutoFireAndCooldownReady()
    {
        var session = MakeSession();
        session.State.AutoFire = true;
        session.HandleBulletCreation(new Vector2(700, 400), mouseDown: false, dragInProgress: false, new Random(1));
        Assert.NotEmpty(session.State.BulletHolster);
    }

    [Fact]
    public void HandleBulletCreation_RespectsCooldown_NoDoubleFireSameFrame()
    {
        var session = MakeSession();
        session.State.AutoFire = true;
        session.HandleBulletCreation(new Vector2(700, 400), false, false, new Random(1));
        int countAfterFirst = session.State.BulletHolster.Count;
        session.HandleBulletCreation(new Vector2(700, 400), false, false, new Random(1));
        Assert.Equal(countAfterFirst, session.State.BulletHolster.Count);
    }

    [Fact]
    public void HandleBulletCreation_DoesNothing_WhenDragInProgress()
    {
        var session = MakeSession();
        session.State.AutoFire = true;
        session.HandleBulletCreation(new Vector2(700, 400), false, dragInProgress: true, new Random(1));
        Assert.Empty(session.State.BulletHolster);
    }

    [Fact]
    public void UpdateBullets_RemovesExpiredBullets()
    {
        var session = MakeSession();
        session.State.BulletHolster.Add(new Bullet(
            session.Player.WorldX, session.Player.WorldY, speed: 4, direction: 0f, bulletRange: 0.01f,
            size: 10, color: Color.Gray, pierce: 1, damage: 10, isCritical: false));
        session.UpdateBullets();
        Assert.Empty(session.State.BulletHolster);
    }

    [Fact]
    public void HandleEnemyCreation_DoesNothing_WhenSpawningDisabled()
    {
        var session = MakeSession(level: 5);
        session.State.EnemySpawningEnabled = false;
        session.HandleEnemyCreation(new Random(1));
        Assert.Empty(session.State.EnemyHolster);
    }

    [Fact]
    public void HandleEnemyCreation_SpawnsGuaranteedMiniboss_AtGateLevel()
    {
        var session = MakeSession(level: 5); // miniboss_arsenal gates in at level 5
        session.HandleEnemyCreation(new Random(1));
        Assert.Contains("miniboss_arsenal", session.State.GuaranteedMiniBossesSpawned);
        Assert.Contains(session.State.EnemyHolster, e => e is ArsenalMiniBoss);
    }

    [Fact]
    public void HandleEnemyCreation_NeverSpawnsSameMinibossTwice()
    {
        var session = MakeSession(level: 5);
        session.HandleEnemyCreation(new Random(1));
        int countAfterFirst = session.State.EnemyHolster.Count(e => e is ArsenalMiniBoss);
        session.HandleEnemyCreation(new Random(1));
        int countAfterSecond = session.State.EnemyHolster.Count(e => e is ArsenalMiniBoss);
        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public void HandleEnemyCreation_IdleSpawnTimer_DoesNotAllocatePerFrame()
    {
        var session = MakeSession(level: 1);
        session.State.EnemySpawnTimer = Simulation.FrameRate * 30;
        var rng = new Random(91);
        for (int index = 0; index < 8; index++)
            session.HandleEnemyCreation(rng);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200; index++)
            session.HandleEnemyCreation(rng);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= 1024,
            $"Idle spawning allocated {allocated:N0} bytes across 200 frames.");
    }

    [Fact]
    public void ExpForPlayer_PickupWithinPlayerRect_IncreasesExpCount()
    {
        var session = MakeSession();
        session.State.ExperienceList.Add(new ExperienceBubble(session.Player.WorldX, session.Player.WorldY, value: 5, difficultyDead: 1));
        session.ExpForPlayer();
        Assert.Equal(5, session.State.ExpCount);
        Assert.Empty(session.State.ExperienceList);
    }

    [Fact]
    public void ExpForPlayer_GoldenFlameMode_StandardBubbleGrantsThirdOfLevelRegardlessOfValue()
    {
        var session = MakeSession(level: 0);
        session.State.SetGoldenFlame(true);
        double third = session.State.ExpNeededForNextLevel / 3.0;
        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: 99999, difficultyDead: 1));

        session.ExpForPlayer();

        Assert.Equal(third, session.State.ExpCount, 3);
        Assert.Equal(0, session.State.CurrentLevel);
    }

    [Fact]
    public void ExpForPlayer_GoldenFlameMode_GuardianAndFinalBossGrantInstantLevels()
    {
        var session = MakeSession(level: 0);
        session.State.SetGoldenFlame(true);
        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: 1, difficultyDead: 1,
            tier: ExperienceBubble.ExperienceTier.Guardian));

        session.ExpForPlayer();

        Assert.Equal(1, session.State.CurrentLevel);
        Assert.Equal(1, session.State.PendingLevelUps);

        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: 1, difficultyDead: 1,
            tier: ExperienceBubble.ExperienceTier.FinalBoss));

        session.ExpForPlayer();

        Assert.Equal(3, session.State.CurrentLevel);
        Assert.Equal(3, session.State.PendingLevelUps);
    }

    [Fact]
    public void ExpForPlayer_VoidMode_GrantsBiggerInstantLevelsAndTakesPriorityOverGoldenFlame()
    {
        var session = MakeSession(level: 0);
        session.State.SetGoldenFlame(true);
        session.State.SetVoid(true);
        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: 1, difficultyDead: 1));

        session.ExpForPlayer();

        Assert.Equal(1, session.State.CurrentLevel);
        // Never banked into ExpCount -- Void bypasses XP banking entirely.
        Assert.Equal(0, session.State.ExpCount);

        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: 1, difficultyDead: 1,
            tier: ExperienceBubble.ExperienceTier.Guardian));
        session.ExpForPlayer();
        Assert.Equal(4, session.State.CurrentLevel);

        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: 1, difficultyDead: 1,
            tier: ExperienceBubble.ExperienceTier.FinalBoss));
        session.ExpForPlayer();
        Assert.Equal(9, session.State.CurrentLevel);
    }

    [Fact]
    public void StartPathRun_BeginsInProtectedRoomWhileAdjacentEnemiesStayInactive()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(10));

        Assert.True(session.IsPathMode);
        Assert.NotNull(session.PathRun);
        Assert.Equal(1, session.PathRun!.FloorNumber);
        Assert.True(session.PathRun.Layout.StartRoom.ContainsWorld(session.PlayerWorldCenter));
        Assert.Equal(session.PathRun.CurrentSenseKey, GamePaths.Active().Key);

        session.HandleEnemyCreation(new Random(11));
        Assert.NotEmpty(session.State.EnemyHolster);
        Assert.All(session.State.EnemyHolster, enemy =>
        {
            PathRoom room = Assert.Single(session.PathRun.Layout.Rooms,
                value => value.EncounterKey == enemy.EncounterKey);
            Assert.False(room.IsActivated);
        });
        Assert.Empty(session.PathRun.ActiveCombatRooms);
        Assert.NotNull(session.PathFog);
        Assert.True(session.PathFog!.IsWorldVisible(session.PlayerWorldCenter));
    }

    [Fact]
    public void PlayerLevelCap_IsFortyInDungeonAndTwentyInArena()
    {
        var session = MakeSession();
        session.State.CurrentLevel = Progression.MaxLevel;
        session.State.ExpCount = session.State.ExpNeededForNextLevel;

        Assert.Equal(20, session.PlayerLevelCap);
        Assert.False(session.CanPurchaseLevelUp);

        session.StartPathRun(new Random(101));
        session.State.CurrentLevel = Progression.DungeonMaxLevel - 1;
        session.State.ExpCount = session.State.ExpNeededForNextLevel;

        Assert.Equal(40, session.PlayerLevelCap);
        Assert.True(session.TryPurchaseLevelUp());
        Assert.Equal(40, session.State.CurrentLevel);
        session.State.ExpCount = session.State.ExpNeededForNextLevel;
        Assert.False(session.CanPurchaseLevelUp);
    }

    [Fact]
    public void AuthoredGuardiansAndBossesReceiveDoubleBaseHealth()
    {
        bool originalCasualMode = GameProfile.Profile.CasualMode;
        try
        {
            GameProfile.Profile.CasualMode = false;
            var session = MakeSession();
            session.State.SetNewGamePlusLevel(0);
            var battleground = session.Battleground;
            var guardian = new PathGuardianBoss(1000, 1000, "sight", 1,
                float.PositiveInfinity, new Random(102));
            var boss = new Ishe(1000, 1000, battleground, new Random(103));
            int guardianBaseHealth = guardian.MaxHp;
            int bossBaseHealth = boss.MaxHp;

            session.ApplyRunDifficulty(guardian);
            session.ApplyRunDifficulty(boss);

            Assert.Equal(guardianBaseHealth * 2, guardian.MaxHp);
            Assert.Equal(bossBaseHealth * 2, boss.MaxHp);
            Assert.Equal(guardian.MaxHp, guardian.Hp);
            Assert.Equal(boss.MaxHp, boss.Hp);
        }
        finally
        {
            GameProfile.Profile.CasualMode = originalCasualMode;
        }
    }

    [Fact]
    public void PathFog_RemainsActiveInsideOrdinaryBossArena()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(10));
        Assert.True(session.IsPathFogActive);

        MoveToPathRoom(session, PathRoomType.Boss);
        session.HandleEnemyCreation(new Random(110));
        Assert.NotNull(session.State.ActiveBoss);
        session.MovePlayer(
            moveLeft: false,
            moveRight: false,
            moveUp: false,
            moveDown: false,
            dashPressed: false);

        Assert.True(session.IsPathFogActive);

        PathRoom startRoom = session.PathRun!.Layout.StartRoom;
        session.Player.SetPosition(
            startRoom.WorldCenter.X - (float)session.State.PlayerSize / 2f,
            startRoom.WorldCenter.Y - (float)session.State.PlayerSize / 2f);
        session.MovePlayer(
            moveLeft: false,
            moveRight: false,
            moveUp: false,
            moveDown: false,
            dashPressed: false);

        Assert.True(session.IsPathFogActive);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void PathFog_DisablesOnlyInsideMajorBossArenas(int floor)
    {
        var session = MakeSession();
        session.StartPathRun(new Random(111));
        AdvancePathToFloor(session, floor);
        MoveToPathRoom(session, PathRoomType.Boss);
        session.HandleEnemyCreation(new Random(1100 + floor));
        session.HandleEnemyCreation(new Random(1110 + floor), interactPressed: true);
        Assert.NotNull(session.State.ActiveBoss);

        session.MovePlayer(
            moveLeft: false,
            moveRight: false,
            moveUp: false,
            moveDown: false,
            dashPressed: false);

        Assert.False(session.IsPathFogActive);
    }

    [Fact]
    public void PathFog_ResumesAfterLeavingFloorFive()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(112));
        AdvancePathToFloor(session, 5);
        MoveToPathRoom(session, PathRoomType.Boss);
        session.HandleEnemyCreation(new Random(1114));
        session.HandleEnemyCreation(new Random(1115), interactPressed: true);
        Assert.False(session.IsPathFogActive);

        AdvancePathToFloor(session, 6);

        Assert.True(session.IsPathFogActive);
    }

    [Fact]
    public void PathCombatRoom_SpawnsContainedSenseWaveWithoutLockingThresholds()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(12));
        MoveToPathRoom(session, PathRoomType.Skirmish);

        session.HandleEnemyCreation(new Random(13));

        var room = session.PathRun!.Layout.Rooms.First(
            value => value.Type == PathRoomType.Skirmish);
        Assert.Contains(room, session.PathRun.ActiveCombatRooms);
        Assert.NotEmpty(session.State.EnemyHolster);
        Assert.All(session.State.EnemyHolster, enemy =>
        {
            Assert.Equal(room.EncounterKey, enemy.EncounterKey);
            Assert.True(enemy.AwarenessRange >= session.ScreenHeight * 2.25f);
            Assert.True(enemy.DisengageRange > enemy.AwarenessRange);
            if (session.PathRun.CurrentSenseKey == "sound")
                Assert.True(enemy.ContentPath is null or "sound");
            else
                Assert.Equal(session.PathRun.CurrentSenseKey, enemy.ContentPath);
        });

        MoveToPathRoom(session, PathRoomType.Assault);
        session.HandleEnemyCreation(new Random(14));

        Assert.Equal(2, session.PathRun.ActiveCombatRooms.Count);
        Assert.Contains(session.PathRun.ActiveCombatRooms,
            active => active.Type == PathRoomType.Assault);
        Assert.Contains(session.State.EnemyHolster,
            enemy => enemy.EncounterKey == room.EncounterKey);
    }

    [Fact]
    public void PathCombatRoom_PreloadsFromItsNeighborAndRemainsDormantUntilEntry()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(120));
        PathFloorLayout layout = session.PathRun!.Layout;
        PathConnection connection = layout.Connections.First(value =>
            value.FromRoomId == layout.StartRoom.Id
            || value.ToRoomId == layout.StartRoom.Id);
        int adjacentId = connection.FromRoomId == layout.StartRoom.Id
            ? connection.ToRoomId
            : connection.FromRoomId;
        PathRoom room = layout.Rooms.First(value => value.Id == adjacentId);
        Assert.True(room.IsCombatRoom);

        session.HandleEnemyCreation(new Random(121));
        FinishPendingPathWaves(session);

        Enemy[] preloaded = session.State.EnemyHolster
            .Where(enemy => enemy.EncounterKey == room.EncounterKey)
            .ToArray();
        Assert.NotEmpty(preloaded);
        Assert.False(room.IsActivated);
        Vector2[] positions = preloaded
            .Select(enemy => new Vector2(enemy.WorldX, enemy.WorldY))
            .ToArray();

        session.UpdateEnemies();

        Assert.Equal(positions, preloaded
            .Select(enemy => new Vector2(enemy.WorldX, enemy.WorldY)));

        MoveToPathRoom(session, room.Type);
        int countBeforeEntry = preloaded.Length;
        session.HandleEnemyCreation(new Random(122));

        Assert.True(room.IsActivated);
        Assert.Equal(countBeforeEntry, session.State.EnemyHolster.Count(
            enemy => enemy.EncounterKey == room.EncounterKey));
    }

    [Fact]
    public void PathTreasureRoom_RequiresGuardianStrengthEncounterBeforeChest()
    {
        GameSession? session = null;
        for (int seed = 0; seed < 100; seed++)
        {
            var candidate = MakeSession();
            candidate.StartPathRun(new Random(seed));
            if (candidate.PathRun!.Layout.TreasureRooms.Count > 0)
            {
                session = candidate;
                break;
            }
        }
        Assert.NotNull(session);
        MoveToPathRoom(session!, PathRoomType.Treasure);

        session!.HandleEnemyCreation(new Random(15));
        FinishPendingPathWaves(session);

        var room = session.PathRun!.Layout.TreasureRooms[0];
        Assert.Empty(session.State.LootCrateList);
        Assert.NotEmpty(session.State.EnemyHolster);
        Assert.All(session.State.EnemyHolster.Where(
                enemy => enemy.EncounterKey == room.EncounterKey),
            enemy => Assert.Equal(room.EncounterKey, enemy.EncounterKey));
        double guardianBenchmark = (5800 + session.PathRun.FloorNumber * 1550)
            * session.PathRun.HealthMultiplier;
        Assert.True(session.State.EnemyHolster.Sum(enemy => enemy.MaxHp)
            >= guardianBenchmark * .75);

        session.State.EnemyHolster.Clear();
        session.HandleEnemyCreation(new Random(16));

        var chest = Assert.IsType<TreasureChest>(
            Assert.Single(session.State.LootCrateList));
        Assert.True(chest.Items.Count >= TreasureChest.MinimumItems);
        Assert.True(room.IsCleared);
        Assert.DoesNotContain(room, session.PathRun.ActiveCombatRooms);
    }

    [Fact]
    public void PathChallengeRoom_AwardsAnEnhancedChestWhenCleared()
    {
        GameSession? session = null;
        for (int seed = 0; seed < 40; seed++)
        {
            var candidate = MakeSession();
            candidate.StartPathRun(new Random(seed));
            candidate.PathRun!.NotifyBossDefeated();
            Vector2 portal = candidate.PathRun.ExitPortalWorld;
            candidate.Player.SetPosition(portal.X, portal.Y);
            candidate.HandleEnemyCreation(new Random(seed + 1000), interactPressed: true);
            if (candidate.PathRun!.Layout.Rooms.Any(room => room.Type == PathRoomType.Challenge))
            {
                session = candidate;
                break;
            }
        }
        Assert.NotNull(session);
        MoveToPathRoom(session!, PathRoomType.Challenge);

        session!.HandleEnemyCreation(new Random(70));
        FinishPendingPathWaves(session);
        Assert.Contains(session.PathRun!.ActiveCombatRooms,
            room => room.Type == PathRoomType.Challenge);
        Assert.NotEmpty(session.State.EnemyHolster);

        session.State.EnemyHolster.Clear();
        session.HandleEnemyCreation(new Random(71));

        var chest = Assert.IsType<TreasureChest>(Assert.Single(session.State.LootCrateList));
        Assert.True(chest.Items.Count >= TreasureChest.MinimumItems + 1);
        Assert.DoesNotContain(session.PathRun.ActiveCombatRooms,
            room => room.Type == PathRoomType.Challenge);
    }

    [Fact]
    public void PathRoomWave_ConstructionIsBoundedAcrossFrames()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(212));
        MoveToPathRoom(session, PathRoomType.Assault);

        session.HandleEnemyCreation(new Random(213));

        Assert.InRange(
            session.State.EnemyHolster.Count,
            1,
            3);
        Assert.True(session.HasPendingPathWaves);

        FinishPendingPathWaves(session);

        Assert.True(session.State.EnemyHolster.Count > 3);
    }

    [Fact]
    public void PathMinimapRoomRect_PreservesRelativeFloorFootprints()
    {
        var layout = PathFloorGenerator.Generate("sight", 4, new Random(88));
        var area = new Rectangle(20, 30, 220, 120);
        var roomRects = layout.Rooms.ToDictionary(
            room => room.Id,
            room => GameSession.PathMinimapRoomRect(room, layout.Battleground, area));

        Assert.All(roomRects.Values, rect =>
        {
            Assert.True(area.Contains(rect.Center));
            Assert.True(rect.Width >= 4);
            Assert.True(rect.Height >= 4);
        });
        Assert.True(roomRects[layout.BossRoom.Id].Width
            > roomRects[layout.StartRoom.Id].Width);
    }

    [Fact]
    public void PathBossRoom_UsesGenericSenseGuardianOnOrdinaryFloor()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(16));
        MoveToPathRoom(session, PathRoomType.Boss);

        session.HandleEnemyCreation(new Random(17));

        var guardian = Assert.IsType<PathGuardianBoss>(session.State.ActiveBoss);
        Assert.Equal(session.PathRun!.CurrentSenseKey, guardian.SenseKey);
        Assert.Same(guardian, Assert.Single(session.State.EnemyHolster));
    }

    [Fact]
    public void PathBossRoom_NormalSevenRoomClearSpawnsGuardian()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(1601));

        foreach (PathRoom room in session.PathRun!.Layout.RequiredRoomsBeforeBoss)
        {
            session.Player.SetPosition(
                room.WorldCenter.X - (float)session.State.PlayerSize / 2f,
                room.WorldCenter.Y - (float)session.State.PlayerSize / 2f);
            session.HandleEnemyCreation(new Random(2000 + room.Id));
            FinishPendingPathWaves(session, 3000 + room.Id * 20);
            Assert.Contains(room, session.PathRun.ActiveCombatRooms);

            session.State.EnemyHolster.Clear();
            session.HandleEnemyCreation(new Random(4000 + room.Id));
            Assert.True(room.IsCleared);
        }

        MoveToPathRoom(session, PathRoomType.Boss);
        session.HandleEnemyCreation(new Random(5000));

        var guardian = Assert.IsType<PathGuardianBoss>(session.State.ActiveBoss);
        Assert.Contains(guardian, session.State.EnemyHolster);
    }

    [Fact]
    public void PathBossRoom_RushingAllSevenUnclearedRoomsStillSpawnsGuardian()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(1602));

        foreach (PathRoom room in session.PathRun!.Layout.RequiredRoomsBeforeBoss)
        {
            session.Player.SetPosition(
                room.WorldCenter.X - (float)session.State.PlayerSize / 2f,
                room.WorldCenter.Y - (float)session.State.PlayerSize / 2f);
            session.HandleEnemyCreation(new Random(6000 + room.Id));
            Assert.True(room.IsActivated);
            Assert.False(room.IsCleared);
        }

        Assert.Equal(7, session.PathRun.ActiveCombatRooms.Count);
        MoveToPathRoom(session, PathRoomType.Boss);
        session.HandleEnemyCreation(new Random(7000));

        var guardian = Assert.IsType<PathGuardianBoss>(session.State.ActiveBoss);
        Assert.Contains(guardian, session.State.EnemyHolster);
        Assert.True(session.State.EnemyHolster.Count > 1);
        Assert.All(session.PathRun.Layout.RequiredRoomsBeforeBoss,
            room => Assert.False(room.IsCleared));
    }

    [Fact]
    public void PathRoomEnemy_OptionalKillStillDropsExperience()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(1603));
        PathRoom room = session.PathRun!.Layout.RequiredRoomsBeforeBoss[0];
        session.Player.SetPosition(
            room.WorldCenter.X - (float)session.State.PlayerSize / 2f,
            room.WorldCenter.Y - (float)session.State.PlayerSize / 2f);
        session.HandleEnemyCreation(new Random(8000));
        FinishPendingPathWaves(session, 8100);

        Enemy defeated = session.State.EnemyHolster[0];
        double reward = defeated.ExpValue;
        defeated.TakeDamage(defeated.MaxHp * 10);
        session.HandleDamagingEnemies(new Random(8200));

        ExperienceBubble bubble = Assert.Single(session.State.ExperienceList);
        Assert.True(reward > 0);
        Assert.True(bubble.Value > 0);
    }

    [Fact]
    public void PathBossRoom_OrdinaryFloor_DoesNotTeleportPlayer()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(16));
        MoveToPathRoom(session, PathRoomType.Boss);
        Vector2 enteredPosition = new(
            session.Player.WorldX, session.Player.WorldY);

        session.HandleEnemyCreation(new Random(17));

        Assert.NotNull(session.State.ActiveBoss);
        Assert.Equal(enteredPosition.X, session.Player.WorldX);
        Assert.Equal(enteredPosition.Y, session.Player.WorldY);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void PathBossRoom_MilestoneFloor_RepositionsPlayerBelowBoss(int floor)
    {
        var session = MakeSession();
        session.StartPathRun(new Random(16));
        AdvancePathToFloor(session, floor);
        PathRoom room = session.PathRun!.Layout.BossRoom;
        MoveToPathRoom(session, PathRoomType.Boss);

        session.HandleEnemyCreation(new Random(1600 + floor));
        session.HandleEnemyCreation(new Random(1700 + floor), interactPressed: true);

        var boss = Assert.IsAssignableFrom<Enemy>(session.State.ActiveBoss);
        Assert.True(session.DungeonBossInstanceActive);
        Assert.True(session.PlayerWorldCenter.Y > boss.WorldRect().Center.Y);
        Assert.True(Vector2.Distance(
            session.PlayerWorldCenter,
            new Vector2(boss.WorldRect().Center.X,
                boss.WorldRect().Center.Y)) > Simulation.TileSize);
    }

    [Fact]
    public void PathMajorBossGateway_PreservesHealthAndReplacesTheFloorWithAnIsolatedArena()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(1610));
        AdvancePathToFloor(session, 5);
        Battleground suspended = session.Battleground;
        session.State.HealthPoints = 637;

        MoveToPathRoom(session, PathRoomType.Skirmish);
        session.HandleEnemyCreation(new Random(1611));
        Assert.NotEmpty(session.State.EnemyHolster);
        session.State.EnemyProjectileHolster.Add(new EnemyProjectile(
            session.Player.WorldX, session.Player.WorldY,
            0, .5f, 100, Simulation.TileSize * .4f));

        MoveToPathRoom(session, PathRoomType.Boss);
        session.HandleEnemyCreation(new Random(1612));
        session.HandleEnemyCreation(new Random(1613), interactPressed: true);

        Assert.True(session.DungeonBossInstanceActive);
        Assert.Equal(637, session.State.HealthPoints);
        Assert.NotSame(suspended, session.Battleground);
        Assert.Equal(TileType.OuterVoid, session.Battleground.TileAt(0, 0));
        Assert.Empty(session.State.EnemyProjectileHolster);
        Assert.Same(session.State.ActiveBoss, Assert.Single(session.State.EnemyHolster));
    }

    [Fact]
    public void CombatViewportCull_IsConservativeForLongCrossingHazards()
    {
        var camera = new Camera { Lock = new Vector2(640, 360) };
        var viewport = new Rectangle(0, 0, 1280, 720);
        var player = new Vector2(5000, 5000);

        Assert.True(GameSession.IsWorldAreaNearViewport(
            camera, player, Vector2.Zero, viewport,
            new Rectangle(4900, 4900, 200, 200)));
        Assert.False(GameSession.IsWorldAreaNearViewport(
            camera, player, Vector2.Zero, viewport,
            new Rectangle(9000, 9000, 50, 50)));
        Assert.True(GameSession.IsWorldAreaNearViewport(
            camera, player, Vector2.Zero, viewport,
            new Rectangle(3900, 4950, 2200, 20)));
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void PathGuardian_UsesComparableAuthoredBaselineAcrossSenses(
        string senseKey)
    {
        GameSession? session = null;
        for (int seed = 0; seed < 100; seed++)
        {
            var candidate = MakeSession();
            candidate.StartPathRun(new Random(seed));
            if (candidate.PathRun!.CurrentSenseKey == senseKey)
            {
                session = candidate;
                break;
            }
        }
        Assert.NotNull(session);

        MoveToPathRoom(session!, PathRoomType.Boss);
        session!.HandleEnemyCreation(new Random(170));

        var guardian = Assert.IsType<PathGuardianBoss>(session.State.ActiveBoss);
        int expectedHealth = GameProfile.Profile.CasualMode ? 29_520 : 36_000;
        int expectedDamage = GameProfile.Profile.CasualMode ? 120 : 150;
        Assert.Equal(expectedHealth, guardian.MaxHp);
        Assert.Equal(expectedDamage, guardian.Damage);
        Assert.Equal(senseKey, guardian.ContentPath);
        Assert.True(GameSession.UsesAuthoredBossBalance(guardian));
    }

    [Fact]
    public void PathBossRoom_PreservesEnemiesFromRushedEarlierRooms()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(16));
        MoveToPathRoom(session, PathRoomType.Skirmish);
        session.HandleEnemyCreation(new Random(17));
        var pursuingEnemy = session.State.EnemyHolster.FirstOrDefault(
            enemy => enemy.EncounterKey is not null);
        Assert.NotNull(pursuingEnemy);

        MoveToPathRoom(session, PathRoomType.Boss);
        session.HandleEnemyCreation(new Random(18));

        Assert.NotNull(session.State.ActiveBoss);
        Assert.Contains(pursuingEnemy!, session.State.EnemyHolster);
        Assert.Contains(session.State.ActiveBoss, session.State.EnemyHolster);
        Assert.True(session.State.EnemyHolster.Count > 1);
    }

    [Fact]
    public void PathBossTelemetry_RecordsPacingDamageSkippedPressureAndControllerUse()
    {
        var originalProfile = GameProfile.Profile;
        string originalSavePath = GameProfile.SavePath;
        string tempSavePath = Path.Combine(
            Path.GetTempPath(),
            $"rotboi-boss-telemetry-{Guid.NewGuid():N}.json");
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.SavePath = tempSavePath;
            GameSession? session = null;
            for (int seed = 0; seed < 100; seed++)
            {
                var candidate = MakeSession();
                candidate.StartPathRun(new Random(seed));
                if (candidate.PathRun!.Layout.TreasureRooms.Count > 0)
                {
                    session = candidate;
                    break;
                }
            }
            Assert.NotNull(session);

            MoveToPathRoom(session!, PathRoomType.Treasure);
            session!.HandleEnemyCreation(new Random(191));
            Assert.NotEmpty(session.State.EnemyHolster);

            MoveToPathRoom(session, PathRoomType.Boss);
            session.HandleEnemyCreation(new Random(192));
            var guardian = Assert.IsType<PathGuardianBoss>(
                session.State.ActiveBoss);
            Assert.True(session.BossTelemetryActive);

            session.State.RunTimeSeconds = 10;
            session.RecordControllerActivity(active: true);
            Assert.True(session.PreferControllerPrompts);
            session.UpdateEnemies();

            session.State.GracePeriod = 0;
            session.State.PlayerInvulnerabilityTimer = 0;
            session.State.EnemyProjectileHolster.Add(new EnemyProjectile(
                session.Player.WorldX,
                session.Player.WorldY,
                0,
                0,
                100,
                (float)session.State.PlayerSize,
                color: Color.Red,
                owner: "telemetry-test",
                ignoreWalls: true));
            Assert.False(session.HurtPlayer());

            guardian.DebugSetPhase(3);
            session.State.RunTimeSeconds = 15;
            session.UpdateEnemies();
            guardian.Hp = 10;
            // A lesson only ever surrenders its own damage budget, measured
            // from the health it started the lesson on -- re-baseline after
            // writing health directly or the killing blow is refused.
            guardian.DebugRebasePhaseHealth();
            guardian.TakeDamage(1000);
            for (int frame = 0; frame < 300 && !guardian.IsDead(); frame++)
            {
                session.State.RunTimeSeconds += 1.0 / Simulation.FrameRate;
                session.UpdateEnemies();
            }
            Assert.True(guardian.IsDead());
            session.HandleDamagingEnemies(new Random(193));

            var telemetry = Assert.Single(
                session.State.BossEncounterTelemetry);
            Assert.True(telemetry.Victory);
            Assert.True(telemetry.ClearSeconds >= 5);
            Assert.True(telemetry.DamageTaken > 0);
            Assert.True(telemetry.ControllerUsed);
            Assert.True(telemetry.SkippedBranchRooms >= 1);
            Assert.True(telemetry.SkippedBranchThreat > 0);
            Assert.True(telemetry.CarriedEnemyThreat
                >= telemetry.SkippedBranchThreat);
            Assert.Contains(telemetry.Phases,
                phase => phase.Label.Contains("PHASE 1"));
            Assert.Contains(telemetry.Phases,
                phase => phase.Label.Contains("PHASE 3"));
            Assert.Single(GameProfile.Profile.RecentBossEncounters);
            Assert.False(session.BossTelemetryActive);

            var failedSession = MakeSession();
            failedSession.StartPathRun(new Random(194));
            MoveToPathRoom(failedSession, PathRoomType.Boss);
            failedSession.HandleEnemyCreation(new Random(195));
            failedSession.State.RunTimeSeconds = 20;
            failedSession.UpdateEnemies();
            failedSession.State.HealthPoints = 25;
            failedSession.State.GracePeriod = 0;
            failedSession.State.PlayerInvulnerabilityTimer = 0;
            failedSession.State.EnemyProjectileHolster.Add(new EnemyProjectile(
                failedSession.Player.WorldX,
                failedSession.Player.WorldY,
                0,
                0,
                5000,
                (float)failedSession.State.PlayerSize,
                color: Color.Red,
                owner: "telemetry-failure-test",
                ignoreWalls: true));

            Assert.True(failedSession.HurtPlayer());
            var failedTelemetry = Assert.Single(
                failedSession.State.BossEncounterTelemetry);
            Assert.False(failedTelemetry.Victory);
            Assert.True(failedTelemetry.DamageTaken > 0);
            Assert.Equal(2, GameProfile.Profile.RecentBossEncounters.Count);
            Assert.False(failedSession.BossTelemetryActive);
        }
        finally
        {
            GameProfile.Profile = originalProfile;
            GameProfile.SavePath = originalSavePath;
            if (File.Exists(tempSavePath))
                File.Delete(tempSavePath);
        }
    }

    [Fact]
    public void PathExitPortal_TransitionsWorldButPreservesRunProgress()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(18));
        session.State.CurrentLevel = 4;
        session.State.Fragments = 7;
        var firstBattleground = session.Battleground;
        session.PathRun!.NotifyBossDefeated();
        var portal = session.PathRun.ExitPortalWorld;
        session.Player.SetPosition(portal.X, portal.Y);

        session.HandleEnemyCreation(new Random(19), interactPressed: true);

        Assert.Equal(2, session.PathRun.FloorNumber);
        Assert.NotSame(firstBattleground, session.Battleground);
        Assert.Equal(4, session.State.CurrentLevel);
        Assert.Equal(7, session.State.Fragments);
        Assert.True(session.PathRun.Layout.StartRoom.ContainsWorld(session.PlayerWorldCenter));
        Assert.Empty(session.State.EnemyHolster);
    }

    [Fact]
    public void ExpForPlayer_FragmentPickupIncreasesFragmentsWithoutChangingStoredExperience()
    {
        var session = MakeSession();
        session.State.ExpCount = 12;
        session.State.FragmentList.Add(new FragmentPickup(session.Player.WorldX, session.Player.WorldY, new Random(1)));

        session.ExpForPlayer();

        Assert.Equal(1, session.State.Fragments);
        Assert.Equal(12, session.State.ExpCount);
        Assert.Empty(session.State.FragmentList);
    }

    [Fact]
    public void FragmentDropRoll_IsApproximatelyOneInThree()
    {
        var rng = new Random(882);
        int drops = Enumerable.Range(0, 30_000).Count(_ => GameSession.RollFragmentDrop(rng));

        Assert.InRange(drops, 9_700, 10_300);
        Assert.Equal(1.0 / 3.0, GameSession.FragmentDropChance, precision: 8);
    }

    [Fact]
    public void ExpForPlayer_EnoughExperience_IsStoredUntilPlayerPurchasesLevelUp()
    {
        var session = MakeSession(level: 0);
        double cost = session.State.ExpNeededForNextLevel;
        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: cost, difficultyDead: 1));

        session.ExpForPlayer();

        Assert.Equal(0, session.State.CurrentLevel);
        Assert.Equal(0, session.State.PendingLevelUps);
        Assert.Equal(cost, session.State.ExpCount);
        Assert.True(session.CanPurchaseLevelUp);

        Assert.True(session.TryPurchaseLevelUp());
        Assert.Equal(1, session.State.CurrentLevel);
        Assert.Equal(1, session.State.PendingLevelUps);
        Assert.Equal(0, session.State.ExpCount);
    }

    [Fact]
    public void PurchasedLevelUp_IsHardModesOnlyFullHeal()
    {
        var session = MakeSession(level: 0);
        session.State.SetHardMode(true);
        session.State.HealthPoints = 1;
        session.State.ExpCount = session.State.ExpNeededForNextLevel;

        Assert.True(session.TryPurchaseLevelUp());

        Assert.Equal(session.State.MaxHealthPoints, session.State.HealthPoints);
    }

    [Fact]
    public void HandleDamagingEnemies_KillsWeakEnemy_AndDropsExperienceAndFragmentOnWinningRoll()
    {
        var session = MakeSession();
        var enemy = new Enemy(session.Player.WorldX, session.Player.WorldY, speed: 0, size: 40,
            Color.Red, damage: 10, hp: 10, expValue: 5, difficulty: 1, awarenessRange: 300f);
        session.State.EnemyHolster.Add(enemy);
        session.State.BulletHolster.Add(new Bullet(
            session.Player.WorldX, session.Player.WorldY, speed: 0, direction: 0f, bulletRange: 500,
            size: 40, color: Color.Gray, pierce: 1, damage: 1000, isCritical: false));

        session.HandleDamagingEnemies(new MinimumRandom());

        Assert.True(enemy.IsDead());
        Assert.Single(session.State.ExperienceList);
        Assert.Single(session.State.FragmentList);
        Assert.NotEmpty(session.State.DamageTextList);
    }

    [Fact]
    public void HandleDamagingEnemies_ExhaustsPierce_RemovesBullet()
    {
        var session = MakeSession();
        var enemy = new Enemy(session.Player.WorldX, session.Player.WorldY, speed: 0, size: 40,
            Color.Red, damage: 10, hp: 100000, expValue: 5, difficulty: 1, awarenessRange: 300f);
        session.State.EnemyHolster.Add(enemy);
        session.State.BulletHolster.Add(new Bullet(
            session.Player.WorldX, session.Player.WorldY, speed: 0, direction: 0f, bulletRange: 500,
            size: 40, color: Color.Gray, pierce: 1, damage: 1, isCritical: false));

        session.HandleDamagingEnemies(new Random(1));

        Assert.Empty(session.State.BulletHolster);
    }

    [Fact]
    public void UpdateCrateInteraction_FindsNearestNonEmptyCrate()
    {
        var session = MakeSession();
        var far = new LootCrate(session.Player.WorldX + 5000, session.Player.WorldY, Items.GenerateDrops(1, new Random(1)));
        var near = new LootCrate(session.Player.WorldX + 5, session.Player.WorldY, Items.GenerateDrops(1, new Random(1)));
        session.State.LootCrateList.Add(far);
        session.State.LootCrateList.Add(near);

        session.UpdateCrateInteraction();

        Assert.Same(near, session.State.NearbyCrate);
    }

    [Fact]
    public void UpdateCrateInteraction_IgnoresEmptyCrates()
    {
        var session = MakeSession();
        var empty = new LootCrate(session.Player.WorldX, session.Player.WorldY, Array.Empty<ItemDrop>());
        session.State.LootCrateList.Add(empty);

        session.UpdateCrateInteraction();

        Assert.Null(session.State.NearbyCrate);
    }

    [Fact]
    public void SpawnLootCrate_AddsACrateAtTheGivenPosition()
    {
        var session = MakeSession();
        var drops = Items.GenerateDrops(1, new Random(1));

        session.SpawnLootCrate(123f, 456f, drops);

        var crate = Assert.Single(session.State.LootCrateList);
        Assert.Equal(123f, crate.WorldX);
        Assert.Equal(456f, crate.WorldY);
    }

    /// <summary>DevConsole's /spawn shares this same cap/eviction logic with the normal enemy-death loot drop (see GameSession.SpawnLootCrate's doc comment) -- this is the same behavior HandleDamagingEnemies relied on before the extraction.</summary>
    [Fact]
    public void SpawnLootCrate_EvictsTheOldestNonNearbyCrateOnceOverCapacity()
    {
        var session = MakeSession();
        var oldest = new LootCrate(0, 0, Items.GenerateDrops(1, new Random(1)));
        session.State.LootCrateList.Add(oldest);
        session.State.NearbyCrate = oldest;
        for (int i = 0; i < 39; i++)
            session.State.LootCrateList.Add(new LootCrate(i, 0, Items.GenerateDrops(1, new Random(1))));
        Assert.Equal(40, session.State.LootCrateList.Count);

        session.SpawnLootCrate(999f, 999f, Items.GenerateDrops(1, new Random(1)));

        Assert.Equal(40, session.State.LootCrateList.Count);
        Assert.Contains(oldest, session.State.LootCrateList); // protected: it's NearbyCrate
        Assert.Same(oldest, session.State.LootCrateList[0]); // the next-oldest was evicted instead
    }

    [Fact]
    public void HurtPlayer_DoesNothing_DuringGracePeriod()
    {
        var session = MakeSession();
        Assert.True(session.State.GracePeriod > 0);
        var enemy = new Enemy(session.Player.WorldX, session.Player.WorldY, speed: 0, size: 40,
            Color.Red, damage: 100, hp: 100, expValue: 5, difficulty: 1, awarenessRange: 300f);
        session.State.EnemyHolster.Add(enemy);
        int healthBefore = session.State.HealthPoints;

        session.HurtPlayer();

        Assert.Equal(healthBefore, session.State.HealthPoints);
    }

    [Fact]
    public void HurtPlayer_EnemyContact_DealsDamage_OnceGraceElapses()
    {
        var session = MakeSession();
        session.State.GracePeriod = 0;
        session.State.PlayerInvulnerabilityTimer = 0;
        var enemy = new Enemy(session.Player.WorldX, session.Player.WorldY, speed: 0, size: 40,
            Color.Red, damage: 100, hp: 100, expValue: 5, difficulty: 1, awarenessRange: 300f);
        session.State.EnemyHolster.Add(enemy);
        int healthBefore = session.State.HealthPoints;

        session.HurtPlayer();

        Assert.True(session.State.HealthPoints < healthBefore);
        Assert.True(session.State.PlayerInvulnerabilityTimer > 0);
    }

    /// <summary>Uses a fresh projectile per hit (rather than melee enemy contact) so knockback from a prior hit can't drift the attacker out of range between calls.</summary>
    private static void AddTouchingProjectile(GameSession session, float damage) =>
        session.State.EnemyProjectileHolster.Add(new EnemyProjectile(
            session.Player.WorldX, session.Player.WorldY, 0, 0, damage,
            (float)session.State.PlayerSize, color: Color.Red, owner: "golden-flame-test", ignoreWalls: true));

    [Fact]
    public void HurtPlayer_GoldenFlameMode_DiesOnThirdHitRegardlessOfDamage()
    {
        var session = MakeSession();
        session.State.SetGoldenFlame(true);

        for (int hit = 0; hit < 2; hit++)
        {
            session.State.GracePeriod = 0;
            session.State.PlayerInvulnerabilityTimer = 0;
            AddTouchingProjectile(session, damage: 1);
            Assert.False(session.HurtPlayer());
        }
        Assert.Equal(1, session.State.GoldenFlameHitsRemaining);

        session.State.GracePeriod = 0;
        session.State.PlayerInvulnerabilityTimer = 0;
        AddTouchingProjectile(session, damage: 1);
        Assert.True(session.HurtPlayer());
        Assert.Equal(0, session.State.GoldenFlameHitsRemaining);
    }

    [Fact]
    public void HurtPlayer_VoidMode_DiesOnFirstHitRegardlessOfDamage()
    {
        var session = MakeSession();
        session.State.SetVoid(true);
        session.State.GracePeriod = 0;
        session.State.PlayerInvulnerabilityTimer = 0;
        AddTouchingProjectile(session, damage: 1);

        Assert.True(session.HurtPlayer());
    }

    [Fact]
    public void HurtPlayer_BothModesStacked_VoidTakesPriorityOverGoldenFlame()
    {
        var session = MakeSession();
        session.State.SetGoldenFlame(true);
        session.State.SetVoid(true);
        session.State.GracePeriod = 0;
        session.State.PlayerInvulnerabilityTimer = 0;
        AddTouchingProjectile(session, damage: 1);

        Assert.True(session.HurtPlayer());
        // Golden Flame's chunk count is never touched -- Void wins outright.
        Assert.Equal(3, session.State.GoldenFlameHitsRemaining);
    }

    [Fact]
    public void HurtPlayer_NewGamePlusScalesIncomingDamageBeforeDefenseAndAssist()
    {
        bool originalCasual = GameProfile.Profile.CasualMode;
        try
        {
            GameProfile.Profile.CasualMode = false;
            var session = MakeSession();
            session.State.SetNewGamePlusLevel(1);
            session.State.GracePeriod = 0;
            session.State.PlayerInvulnerabilityTimer = 0;
            var enemy = new Enemy(session.Player.WorldX, session.Player.WorldY, speed: 0, size: 40,
                Color.Red, damage: 100, hp: 100, expValue: 5, difficulty: 1, awarenessRange: 300f);
            session.State.EnemyHolster.Add(enemy);

            session.HurtPlayer();

            Assert.Equal(session.State.MaxHealthPoints - 150, session.State.HealthPoints);
        }
        finally
        {
            GameProfile.Profile.CasualMode = originalCasual;
        }
    }

    [Fact]
    public void SelectBountyTarget_NoEnemies_ReturnsNull()
    {
        var session = MakeSession();
        Assert.Null(session.SelectBountyTarget());
    }

    [Fact]
    public void SelectBountyTarget_PicksHighestScoringLoneEnemy()
    {
        var session = MakeSession();
        var weak = new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 1, difficulty: 1, awarenessRange: 100f);
        var strong = new Enemy(100, 100, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 500, difficulty: 1, awarenessRange: 100f);
        session.State.EnemyHolster.Add(weak);
        session.State.EnemyHolster.Add(strong);

        var bounty = session.SelectBountyTarget();

        Assert.NotNull(bounty);
        Assert.Same(strong, bounty!.Target);
    }

    [Fact]
    public void SelectBountyTarget_IgnoresDeadEnemies()
    {
        var session = MakeSession();
        var dead = new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 999, difficulty: 1, awarenessRange: 100f);
        dead.TakeDamage(10, "body");
        session.State.EnemyHolster.Add(dead);

        Assert.Null(session.SelectBountyTarget());
    }

    [Fact]
    public void SelectBountyTarget_HidesThreatUntilPathLineOfSightRevealsIt()
    {
        var session = MakeSession();
        session.StartPathRun(new Random(33));
        Vector2 distant = session.PathRun!.Layout.BossRoom.WorldCenter;
        var enemy = new Enemy(distant.X, distant.Y, speed: 0, size: 40,
            Color.Red, damage: 10, hp: 100, expValue: 50, difficulty: 1,
            awarenessRange: 300f);
        session.State.EnemyHolster.Add(enemy);

        Assert.False(session.PathFog!.IsWorldAreaVisible(enemy.WorldRect()));
        Assert.Null(session.SelectBountyTarget());

        session.PathFog.Update(distant);

        Assert.True(session.PathFog.IsWorldAreaVisible(enemy.WorldRect()));
        Assert.Same(enemy, session.SelectBountyTarget()!.Target);
    }

    [Fact]
    public void HandleEnemyCreation_LevelThresholdReached_OpensPortalWithoutSpawning()
    {
        var session = MakeSession(level: 10); // Progression.MidBossLevel
        // Reaching the level threshold should only make the portal available,
        // not spawn Beaudis, unless the player is standing on it.
        MoveAwayFromArenaCenter(session);

        session.HandleEnemyCreation(new Random(1));

        Assert.Null(session.State.ActiveBoss);
        Assert.False(session.State.BeaudisEncounterStarted);
    }

    [Fact]
    public void HandleEnemyCreation_PortalOpen_PausesOrdinaryEnemySpawning()
    {
        var session = MakeSession(level: 10); // Progression.MidBossLevel, also past the level-5 arsenal miniboss gate
        MoveAwayFromArenaCenter(session);

        session.HandleEnemyCreation(new Random(1));

        Assert.Empty(session.State.EnemyHolster);
        Assert.DoesNotContain("miniboss_arsenal", session.State.GuaranteedMiniBossesSpawned);
    }

    [Fact]
    public void HandleEnemyCreation_AtPortalWithoutInteracting_DoesNotSpawn()
    {
        var session = MakeSession(level: 10); // Progression.MidBossLevel
        MoveToArenaCenter(session); // standing on the portal...

        session.HandleEnemyCreation(new Random(1)); // ...but not pressing interact

        Assert.Null(session.State.ActiveBoss);
        Assert.False(session.State.BeaudisEncounterStarted);
    }

    [Fact]
    public void HandleEnemyCreation_NaturalBeaudisTrigger_SpawnsBossAndClearsArena()
    {
        var session = MakeSession(level: 10); // Progression.MidBossLevel
        session.State.EnemyHolster.Add(new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 1, difficulty: 1, awarenessRange: 100f));
        session.State.LootCrateList.Add(new LootCrate(0, 0, Array.Empty<ItemDrop>()));
        MoveToArenaCenter(session);

        session.HandleEnemyCreation(new Random(1), interactPressed: true);

        Assert.True(session.State.BeaudisEncounterStarted);
        Assert.IsType<Beaudis>(session.State.ActiveBoss);
        Assert.Same(session.State.ActiveBoss, session.State.EnemyHolster.Single());
        Assert.False(session.State.EnemySpawningEnabled);
        Assert.Empty(session.State.LootCrateList);
    }

    [Fact]
    public void HandleEnemyCreation_NaturalBeaudisTrigger_StepsPlayerBackFromArenaCenter()
    {
        var session = MakeSession(level: 10);
        MoveToArenaCenter(session);
        var center = new Vector2(
            session.Battleground.Width * Simulation.TileSize / 2f, session.Battleground.Height * Simulation.TileSize / 2f);

        session.HandleEnemyCreation(new Random(1), interactPressed: true);

        // The player shouldn't still be standing exactly on the arena center (i.e. on
        // top of the boss) once the fight actually starts.
        float distance = Vector2.Distance(new Vector2(session.Player.WorldX, session.Player.WorldY), center);
        Assert.True(distance > Simulation.TileSize);
    }

    [Fact]
    public void HandleEnemyCreation_BeaudisAlreadyActive_DoesNotSpawnAnother()
    {
        var session = MakeSession(level: 10);
        MoveToArenaCenter(session);
        session.HandleEnemyCreation(new Random(1), interactPressed: true);
        int countAfterFirst = session.State.EnemyHolster.Count;

        session.HandleEnemyCreation(new Random(1));

        Assert.Equal(countAfterFirst, session.State.EnemyHolster.Count);
    }

    [Fact]
    public void HandleDamagingEnemies_KillingBeaudis_MarksDefeatedAndClearsActiveBoss()
    {
        var session = MakeSession(level: 10);
        MoveToArenaCenter(session);
        session.HandleEnemyCreation(new Random(1), interactPressed: true);
        var boss = Assert.IsType<Beaudis>(session.State.ActiveBoss);
        // Beaudis only reaches 0 HP in ordinary combat through its choreographed
        // Persist/death countdown. This test
        // is purely about GameSession's defeat-handling glue once IsDead() is true.
        boss.Hp = 0;

        session.HandleDamagingEnemies(new Random(1));

        Assert.True(session.State.BeaudisDefeated);
        Assert.Null(session.State.ActiveBoss);
        Assert.True(session.State.EnemySpawningEnabled);
    }

    [Fact]
    public void HurtPlayer_BossDebugInvincible_HealsToMaxAndTakesNoDamage()
    {
        var session = MakeSession();
        session.State.GracePeriod = 0;
        session.State.PlayerInvulnerabilityTimer = 0;
        session.State.BossDebugInvincible = true;
        session.State.HealthPoints = 1;
        var enemy = new Enemy(session.Player.WorldX, session.Player.WorldY, speed: 0, size: 40,
            Color.Red, damage: 100, hp: 100, expValue: 5, difficulty: 1, awarenessRange: 300f);
        session.State.EnemyHolster.Add(enemy);

        bool fatal = session.HurtPlayer();

        Assert.False(fatal);
        Assert.Equal(session.State.MaxHealthPoints, session.State.HealthPoints);
    }

    [Fact]
    public void HandleBossDebugControls_NumberKey_JumpsBossToThatPhase()
    {
        var session = MakeSession(level: 10);
        MoveToArenaCenter(session);
        session.HandleEnemyCreation(new Random(1), interactPressed: true);
        var boss = Assert.IsType<Beaudis>(session.State.ActiveBoss);
        session.State.BossDebugInvincible = true; // spawning the boss resets this; set it after

        session.HandleBossDebugControls(new HashSet<Keys> { Keys.D3 });

        Assert.Equal(3, boss.Phase);
    }

    [Fact]
    public void HandleBossDebugControls_FKey_ForcesBossToTheBrinkOfStagger()
    {
        var session = MakeSession(level: 10);
        MoveToArenaCenter(session);
        session.HandleEnemyCreation(new Random(1), interactPressed: true);
        var boss = Assert.IsType<Beaudis>(session.State.ActiveBoss);
        session.State.BossDebugInvincible = true; // spawning the boss resets this; set it after

        session.HandleBossDebugControls(new HashSet<Keys> { Keys.F });

        Assert.True(boss.IsStaggered);
    }

    [Fact]
    public void HandleBossDebugControls_NoActiveBoss_DoesNothing()
    {
        var session = MakeSession();
        session.HandleBossDebugControls(new HashSet<Keys> { Keys.D1 }); // should not throw
        Assert.Null(session.State.ActiveBoss);
    }

    [Fact]
    public void HandleEnemyCreation_FinalBossThresholdReached_OpensPortalWithoutSpawning()
    {
        var session = MakeSession(level: 20); // Progression.FinalBossLevel
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;
        MoveAwayFromArenaCenter(session);

        session.HandleEnemyCreation(new Random(1));

        Assert.Null(session.State.ActiveBoss);
        Assert.False(session.State.DissonanceEncounterStarted);
    }

    [Fact]
    public void HandleEnemyCreation_NaturalDissonanceTrigger_SpawnsBossAtArenaCenter()
    {
        var session = MakeSession(level: 20); // Progression.FinalBossLevel
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;
        MoveToArenaCenter(session);

        session.HandleEnemyCreation(new Random(1), interactPressed: true);

        var boss = Assert.IsType<Dissonance>(session.State.ActiveBoss);
        Assert.True(session.State.DissonanceEncounterStarted);
        // Within a pixel: the shared SpawnBoss placement plumbing routes through an
        // int-valued Rectangle, unlike Python's direct float assignment for this case.
        Assert.True(Math.Abs(boss.ArenaCenter.X - boss.Size / 2f - boss.WorldX) < 1f);
        Assert.True(Math.Abs(boss.ArenaCenter.Y - boss.Size / 2f - boss.WorldY) < 1f);
    }

    [Fact]
    public void HandleEnemyCreation_NaturalDissonanceTrigger_StepsPlayerBackFromArenaCenter()
    {
        var session = MakeSession(level: 20);
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;
        MoveToArenaCenter(session);
        Vector2 center = new(
            session.Battleground.Width * Simulation.TileSize / 2f,
            session.Battleground.Height * Simulation.TileSize / 2f);

        session.HandleEnemyCreation(new Random(1), interactPressed: true);

        Assert.True(Vector2.Distance(session.PlayerWorldCenter, center)
            > Simulation.TileSize * 5);
    }

    [Fact]
    public void HandleDamagingEnemies_KillingDissonance_CompletesTheRun()
    {
        var session = MakeSession(level: 20);
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;
        MoveToArenaCenter(session);
        session.HandleEnemyCreation(new Random(1), interactPressed: true);
        var boss = Assert.IsType<Dissonance>(session.State.ActiveBoss);
        boss.EntranceRemaining = 0;
        boss.CinematicTransitionsEnabled = false;
        boss.DebugSetPhase(9);
        boss.TransitionRemaining = 0;
        boss.SurvivalRemaining = 0;
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = boss.ArenaCenter.X,
            PlayerWorldY = boss.ArenaCenter.Y,
            Battleground = session.Battleground,
        };
        boss.Update(context); // completes Jera and starts the ten-second collapse
        boss.DeathRemaining = 0;
        boss.Update(context);

        session.HandleDamagingEnemies(new Random(1));

        Assert.True(session.State.GameCompleted);
        Assert.Equal("RUN COMPLETE", session.State.RunOutcome);
        Assert.Null(session.State.ActiveBoss);
    }

    [Fact]
    public void MovePlayer_DissonanceActive_ClampsPlayerWithinArenaRadius()
    {
        var session = MakeSession(level: 20);
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;
        MoveToArenaCenter(session);
        session.HandleEnemyCreation(new Random(1), interactPressed: true);
        var boss = Assert.IsType<Dissonance>(session.State.ActiveBoss);
        // Push the player far outside the arena before moving.
        session.Player.SetPosition(boss.ArenaCenter.X + boss.ArenaRadius * 5, boss.ArenaCenter.Y);

        session.MovePlayer(false, false, false, false, false);

        float playerCenterX = session.Player.WorldX + (float)session.State.PlayerSize / 2f;
        float playerCenterY = session.Player.WorldY + (float)session.State.PlayerSize / 2f;
        float distance = Vector2.Distance(new Vector2(playerCenterX, playerCenterY), boss.ArenaCenter);
        Assert.True(distance <= boss.ArenaRadius + 1f);
    }

    [Fact]
    public void HandleBossDebugControls_DissonanceCKey_ResetsRuneCannonCooldown()
    {
        var session = MakeSession(level: 20);
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;
        MoveToArenaCenter(session);
        session.HandleEnemyCreation(new Random(1), interactPressed: true);
        var boss = Assert.IsType<Dissonance>(session.State.ActiveBoss);
        session.State.BossDebugInvincible = true; // spawning the boss resets this; set it after
        boss.RuneCannonCooldown = 5.0;

        session.HandleBossDebugControls(new HashSet<Keys> { Keys.C });

        Assert.Equal(0, boss.RuneCannonCooldown);
    }

    [Fact]
    public void ResetAll_RestoresDefaultsAndRepositionsPlayer()
    {
        var session = MakeSession();
        session.State.HealthPoints = 1;
        session.State.CurrentLevel = 10;

        var newBattleground = Battleground.GenerateTouch();
        session.ResetAll(newBattleground, new Random(1));

        Assert.Equal(1000, session.State.HealthPoints);
        Assert.Equal(0, session.State.CurrentLevel);
        Assert.Equal(newBattleground.SpawnPosition.X, session.Player.WorldX);
        Assert.Equal(newBattleground.SpawnPosition.Y, session.Player.WorldY);
    }

    [Fact]
    public void RecoverPlayerHealth_BelowMaxWithVitality_GraduallyRestoresHealth()
    {
        var session = MakeSession();
        session.State.HealthPoints = 1;

        for (int i = 0; i < 200 && session.State.HealthPoints <= 1; i++)
            session.RecoverPlayerHealth();

        Assert.True(session.State.HealthPoints > 1);
    }

    [Fact]
    public void BountyArrowGeometry_TargetJustOffOrigin_StillProducesAnArrow()
    {
        // Whether the target is inside the arena viewport is DrawBountyIndicator's own
        // check (it skips calling this helper at all in that case) -- this pure helper
        // always projects an edge intersection as long as direction is well-defined.
        var viewport = new Rectangle(0, 0, 800, 600);
        var geometry = GameSession.BountyArrowGeometry(new Vector2(400, 300), new Vector2(410, 310), viewport);
        Assert.NotNull(geometry);
    }

    [Fact]
    public void BountyArrowGeometry_TargetPastRightEdge_PointsRightAndClampsToViewport()
    {
        var viewport = new Rectangle(0, 0, 800, 600);
        var origin = new Vector2(400, 300);
        var geometry = GameSession.BountyArrowGeometry(origin, new Vector2(5000, 300), viewport);

        Assert.NotNull(geometry);
        var (points, tip, direction) = geometry!.Value;
        Assert.Equal(7, points.Length);
        Assert.True(direction.X > .99f); // pointing directly right
        Assert.Equal(viewport.Right, tip.X, 1);
    }

    [Fact]
    public void BountyArrowGeometry_TargetAtOrigin_ReturnsNull()
    {
        var viewport = new Rectangle(0, 0, 800, 600);
        var origin = new Vector2(400, 300);
        var geometry = GameSession.BountyArrowGeometry(origin, origin, viewport);
        Assert.Null(geometry);
    }
}
