using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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
    public void HandleEnemyCreation_NaturalBeaudisTrigger_SpawnsBossAndClearsArena()
    {
        var session = MakeSession(level: 10); // Progression.MidBossLevel
        session.State.EnemyHolster.Add(new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10, expValue: 1, difficulty: 1, awarenessRange: 100f));
        session.State.LootCrateList.Add(new LootCrate(0, 0, Array.Empty<ItemDrop>()));

        session.HandleEnemyCreation(new Random(1));

        Assert.True(session.State.BeaudisEncounterStarted);
        Assert.IsType<Beaudis>(session.State.ActiveBoss);
        Assert.Same(session.State.ActiveBoss, session.State.EnemyHolster.Single());
        Assert.False(session.State.EnemySpawningEnabled);
        Assert.Empty(session.State.LootCrateList);
    }

    [Fact]
    public void HandleEnemyCreation_BeaudisAlreadyActive_DoesNotSpawnAnother()
    {
        var session = MakeSession(level: 10);
        session.HandleEnemyCreation(new Random(1));
        int countAfterFirst = session.State.EnemyHolster.Count;

        session.HandleEnemyCreation(new Random(1));

        Assert.Equal(countAfterFirst, session.State.EnemyHolster.Count);
    }

    [Fact]
    public void HandleDamagingEnemies_KillingBeaudis_MarksDefeatedAndClearsActiveBoss()
    {
        var session = MakeSession(level: 10);
        session.HandleEnemyCreation(new Random(1));
        var boss = Assert.IsType<Beaudis>(session.State.ActiveBoss);
        // Beaudis only ever reaches 0 HP via its own choreographed survival/death
        // countdown (a huge single hit instead pins it to the next survival gate --
        // see BeaudisTests.TakeDamage_CrossingSurvivalThreshold_EntersSurvivalPhaseFive
        // and Update_DeathCountdownElapsed_SetsHpToZero for that sequence). This test
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
        session.HandleEnemyCreation(new Random(1));
        var boss = Assert.IsType<Beaudis>(session.State.ActiveBoss);
        session.State.BossDebugInvincible = true; // spawning the boss resets this; set it after

        session.HandleBossDebugControls(new HashSet<Keys> { Keys.D3 });

        Assert.Equal(3, boss.Phase);
    }

    [Fact]
    public void HandleBossDebugControls_FKey_ForcesBossToTheBrinkOfStagger()
    {
        var session = MakeSession(level: 10);
        session.HandleEnemyCreation(new Random(1));
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
    public void HandleEnemyCreation_NaturalDissonanceTrigger_SpawnsBossAtArenaCenter()
    {
        var session = MakeSession(level: 20); // Progression.FinalBossLevel
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;

        session.HandleEnemyCreation(new Random(1));

        var boss = Assert.IsType<Dissonance>(session.State.ActiveBoss);
        Assert.True(session.State.DissonanceEncounterStarted);
        // Within a pixel: the shared SpawnBoss placement plumbing routes through an
        // int-valued Rectangle, unlike Python's direct float assignment for this case.
        Assert.True(Math.Abs(boss.ArenaCenter.X - boss.Size / 2f - boss.WorldX) < 1f);
        Assert.True(Math.Abs(boss.ArenaCenter.Y - boss.Size / 2f - boss.WorldY) < 1f);
    }

    [Fact]
    public void HandleDamagingEnemies_KillingDissonance_CompletesTheRun()
    {
        var session = MakeSession(level: 20);
        session.State.BeaudisEncounterStarted = true;
        session.State.BeaudisDefeated = true;
        session.HandleEnemyCreation(new Random(1));
        Assert.IsType<Dissonance>(session.State.ActiveBoss);
        ((Enemy)session.State.ActiveBoss!).Hp = 0;

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
        session.HandleEnemyCreation(new Random(1));
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
        session.HandleEnemyCreation(new Random(1));
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
