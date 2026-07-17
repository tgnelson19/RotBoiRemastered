using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
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
    private static GameSession MakeSession(int level = 1)
    {
        var session = new GameSession(Battleground.GenerateSound(), 1280, 720, new Random(1));
        session.State.CurrentLevel = level;
        return session;
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
    public void ExpForPlayer_PickupWithinPlayerRect_IncreasesExpCount()
    {
        var session = MakeSession();
        session.State.ExperienceList.Add(new ExperienceBubble(session.Player.WorldX, session.Player.WorldY, value: 5, difficultyDead: 1));
        session.ExpForPlayer();
        Assert.Equal(5, session.State.ExpCount);
        Assert.Empty(session.State.ExperienceList);
    }

    [Fact]
    public void ExpForPlayer_EnoughExperience_TriggersLevelUp()
    {
        var session = MakeSession(level: 0);
        session.State.ExperienceList.Add(new ExperienceBubble(
            session.Player.WorldX, session.Player.WorldY, value: session.State.ExpNeededForNextLevel, difficultyDead: 1));

        bool leveledUp = session.ExpForPlayer();

        Assert.True(leveledUp);
        Assert.Equal(1, session.State.CurrentLevel);
        Assert.Equal(1, session.State.PendingLevelUps);
    }

    [Fact]
    public void HandleDamagingEnemies_KillsWeakEnemy_AndAwardsExperience()
    {
        var session = MakeSession();
        var enemy = new Enemy(session.Player.WorldX, session.Player.WorldY, speed: 0, size: 40,
            Color.Red, damage: 10, hp: 10, expValue: 5, difficulty: 1, awarenessRange: 300f);
        session.State.EnemyHolster.Add(enemy);
        session.State.BulletHolster.Add(new Bullet(
            session.Player.WorldX, session.Player.WorldY, speed: 0, direction: 0f, bulletRange: 500,
            size: 40, color: Color.Gray, pierce: 1, damage: 1000, isCritical: false));

        session.HandleDamagingEnemies(new Random(1));

        Assert.True(enemy.IsDead());
        Assert.Single(session.State.ExperienceList);
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
}
