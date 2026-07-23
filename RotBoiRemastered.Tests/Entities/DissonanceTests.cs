using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// Ported from tests/test_beaudis_boss.py's DissonanceBossTests (40 tests,
/// almost the entire file -- despite its name, it's overwhelmingly about
/// Dissonance, not Beaudis). Mirrors that oracle's setUp: entrance skipped
/// and cinematic transitions disabled by default so most tests can drive
/// phase/attack logic directly without waiting out timers; specific tests
/// re-enable cinematic transitions exactly where the Python oracle does.
/// </summary>
public class DissonanceTests
{
    private static Dissonance MakeBoss(Random? rng = null)
    {
        var boss = new Dissonance(1000, 1000, 400f, Battleground.GenerateSound(), rng ?? new Random(5));
        boss.EntranceRemaining = 0;
        boss.CinematicTransitionsEnabled = false;
        return boss;
    }

    private static EnemyUpdateContext MakeContext(float playerX, float playerY) => new()
    {
        PlayerWorldX = playerX, PlayerWorldY = playerY, Battleground = Battleground.GenerateSound(),
    };

    private static void Step(Dissonance boss, EnemyUpdateContext context)
    {
        boss.Update(context);
        var children = new List<EnemyProjectile>();
        foreach (var projectile in context.ProjectileSink.ToList())
        {
            projectile.Update(context.Battleground, casualMode: false);
            children.AddRange(projectile.SpawnedProjectiles);
            projectile.SpawnedProjectiles.Clear();
        }
        context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
        context.ProjectileSink.AddRange(children);
    }

    private static void ReachDeclarations(Dissonance boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 2400 && boss.PhaseDeclarations < count; tick++)
            Step(boss, context);
        Assert.True(boss.PhaseDeclarations >= count,
            $"Phase {boss.Phase} produced only {boss.PhaseDeclarations} declarations.");
    }

    private sealed record Pressure(int Peak, int Overflow, int Hits);

    private static Pressure SimulatePressure(int phase, double duration = 18.0)
    {
        Simulation.ResetForTests();
        var boss = MakeBoss(new Random(300 + phase));
        boss.DebugSetPhase(phase);
        boss.DebugPhaseLocked = true;
        boss.TransitionRemaining = 0;
        var player = boss.ArenaCenter + new Vector2(boss.ArenaRadius * .72f, 0);
        var context = MakeContext(player.X, player.Y);
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle(
            (int)(player.X - playerSize / 2f), (int)(player.Y - playerSize / 2f),
            playerSize, playerSize);
        var hitThreats = new HashSet<EnemyProjectile>();
        int peak = 0, overflow = 0;

        for (int tick = 0; tick < duration * Simulation.FrameRate; tick++)
        {
            Step(boss, context);
            foreach (var projectile in context.ProjectileSink)
                if (projectile.Collides(playerRect))
                    hitThreats.Add(projectile);
            peak = Math.Max(peak, context.ProjectileSink.Count);
            if (context.ProjectileSink.Count > GameSession.MaxBossProjectiles)
                overflow += context.ProjectileSink.Count - GameSession.MaxBossProjectiles;
        }
        return new Pressure(peak, overflow, hitThreats.Count);
    }

    [Fact]
    public void Constructor_SetsBossIdentityAndDeploysPhaseOnePortals()
    {
        var boss = MakeBoss();
        Assert.Equal(150000, boss.Hp);
        Assert.Equal(150000, boss.MaxHp);
        Assert.Equal(1, boss.Phase);
        Assert.Equal("ANCESTRAL HEARTH", boss.PhaseLabel);
        Assert.Equal("KEEPER OF THE FIRST CHORD", Dissonance.Subtitle);
        Assert.Equal(4, Dissonance.OrbitingCubeCount);
        Assert.Equal(3, boss.ProjectilePortals.Count);
    }

    [Fact]
    public void PhaseRunes_EachPhaseHasADistinctName()
    {
        var names = Enumerable.Range(1, 9).Select(phase => Dissonance.PhaseRunes[phase].Name).ToList();
        Assert.Equal("OTHALA", names[0]);
        Assert.Equal("JERA", names[^1]);
        Assert.Equal(9, names.Distinct().Count());
    }

    [Fact]
    public void PhaseMovement_CoversEveryPhaseWithADistinctIdentity()
    {
        var expected = new[]
        {
            "hearth_tornado", "road_anchor", "torch_tornado", "hail_chase", "yew_anchor",
            "sun_revolution", "spear_intercept", "day_anchor", "harvest_chase",
        };
        for (int phase = 1; phase <= 9; phase++)
            Assert.Equal(expected[phase - 1], Dissonance.PhaseMovement[phase]);
    }

    [Fact]
    public void TakeDamage_NormalHit_AppliesPartialDamageAndBuildsStagger()
    {
        var boss = MakeBoss();
        var result = boss.TakeDamage(2);
        Assert.True(result.Applied);
        Assert.True(boss.Hp < boss.MaxHp);
        Assert.Equal(6.0, boss.Stagger);
    }

    [Fact]
    public void TakeDamage_PortalPart_DamagesOnlyThatPortal()
    {
        var boss = MakeBoss();
        var portal = boss.ProjectilePortals[0];
        double hpBefore = boss.Hp;

        var result = boss.TakeDamage(1, "portal:0");

        Assert.True(result.Applied);
        Assert.Equal(hpBefore, boss.Hp); // portal hits never touch the boss's own HP
        Assert.True(portal.HitsTaken > 0);
    }

    [Fact]
    public void TakeDamage_UnknownPortalIndex_IsBlocked()
    {
        var boss = MakeBoss();
        var result = boss.TakeDamage(1, "portal:99");
        Assert.True(result.Blocked);
        Assert.False(result.Applied);
    }

    [Fact]
    public void TakeDamage_SixtyHits_TriggersStaggerExactlyOnTheSixtieth()
    {
        var boss = MakeBoss();
        for (int i = 0; i < 59; i++)
            boss.TakeDamage(1);
        Assert.False(boss.IsStaggered);

        boss.TakeDamage(1);

        Assert.True(boss.IsStaggered);
        Assert.True(boss.TransitionCleanupRequested);
        Assert.Equal(5.0, boss.StaggerRemaining);
    }

    [Fact]
    public void TakeDamage_WhileStaggered_AppliesBonusMultiplierAndBuildsFracture()
    {
        var boss = MakeBoss();
        for (int i = 0; i < 60; i++)
            boss.TakeDamage(1);
        Assert.True(boss.IsStaggered);
        double hpBefore = boss.Hp;

        var result = boss.TakeDamage(100);

        Assert.Equal(hpBefore - result.Amount, boss.Hp);
        Assert.Equal(135, result.Amount); // round(100 * (1.35 + 0*.02))
    }

    [Fact]
    public void TakeDamage_DuringCinematicPhaseProtection_IsBlocked()
    {
        var boss = MakeBoss();
        boss.CinematicTransitionsEnabled = true;
        boss.SetPhase(2); // sets phaseProtectionTimer = 5.0
        double hpBefore = boss.Hp;

        var result = boss.TakeDamage(1000);

        Assert.True(result.Blocked);
        Assert.Equal(hpBefore, boss.Hp);
    }

    [Fact]
    public void TakeDamage_PerfectWindow_SetsPerfectStaggerAndFlash()
    {
        var boss = MakeBoss();
        boss.SetPhase(2); // phaseElapsed resets to 0, well within the .75s perfect window
        boss.Stagger = boss.MaxStagger - 5;

        boss.TakeDamage(1);

        Assert.True(boss.PerfectStagger);
        Assert.True(boss.PerfectBreakFlash > 0);
    }

    [Fact]
    public void UpdateStagger_DoesNotDecayBeforeDelayElapses_ThenDecaysAfter()
    {
        var boss = MakeBoss();
        boss.TakeDamage(2);
        double staggerBefore = boss.Stagger;

        boss.UpdateStagger(1.9); // staggerDecayDelay == 2.0
        Assert.Equal(staggerBefore, boss.Stagger);

        boss.StaggerDecayTimer = 0;
        boss.UpdateStagger(.25);
        Assert.Equal(staggerBefore - boss.StaggerDecayPerSecond * .25, boss.Stagger, 3);
        Assert.True(boss.StaggerEverDecayed);
    }

    [Fact]
    public void UpdateStagger_ExpiringWindow_AdvancesToAChosenPhase()
    {
        var boss = MakeBoss();
        for (int i = 0; i < 60; i++)
            boss.TakeDamage(1);
        Assert.True(boss.IsStaggered);

        boss.StaggerRemaining = 0;
        boss.UpdateStagger(.1);

        Assert.False(boss.IsStaggered);
        Assert.Contains(boss.Phase, new[] { 1, 2 }); // act one's damage-phase pool (nextSurvivalPhase defaults to 3)
    }

    [Fact]
    public void DebugSetPhase_JumpsPhaseAndSkipsEntranceProtectionImmediately()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(8);
        Assert.Equal(8, boss.Phase);
        Assert.Equal(0, boss.PhaseElapsed);
        Assert.Equal(0, boss.EntranceRemaining);
        Assert.Equal(0, boss.PhaseProtectionTimer);
    }

    [Fact]
    public void DebugSetPhase_ToSamePhase_StillReappliesIt()
    {
        var boss = MakeBoss();
        Assert.Equal(1, boss.Phase);
        boss.DebugSetPhase(1);
        Assert.Equal(1, boss.Phase);
        Assert.Equal(5.0, boss.PhaseAnnouncementTimer);
    }

    [Theory]
    [InlineData(1, 2.0 / 3, 3)]
    [InlineData(4, 1.0 / 3, 6)]
    [InlineData(7, 0.0, 9)]
    public void HealthGate_UnlocksTheCorrectActsSurvivalPhase(int startPhase, double hpRatio, int expectedPhase)
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(startPhase);
        boss.TransitionRemaining = 0; // debug jump still primes a transition; the health gate check needs it clear
        boss.NextSurvivalPhase = expectedPhase;
        var context = MakeContext(boss.WorldX, boss.WorldY);
        ReachDeclarations(boss, context, Dissonance.MinimumDamagePhaseDeclarations);
        boss.Hp = (int)(boss.MaxHp * hpRatio);

        boss.Update(context);

        Assert.Equal(expectedPhase, boss.Phase);
        Assert.True(boss.SurvivalActive);
    }

    [Fact]
    public void Update_PhaseTimeLimitElapsed_ForcesADamagePhaseChange()
    {
        var boss = MakeBoss();
        Assert.Equal(36.0, boss.PhaseTimeLimit);
        var context = MakeContext(boss.WorldX, boss.WorldY);
        ReachDeclarations(boss, context, Dissonance.MinimumDamagePhaseDeclarations);
        boss.PhaseElapsed = boss.PhaseTimeLimit;

        boss.Update(context);

        Assert.NotEqual(1, boss.Phase);
        Assert.True(boss.PhaseForcedByTimer);
        Assert.Equal(150000, boss.Hp); // no damage taken, so health gate never fires -- only the timer did
    }

    [Fact]
    public void Update_SurvivalCompletionMidRun_AdvancesToNextAct()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(3);
        boss.TransitionRemaining = 0;
        boss.SurvivalRemaining = 0;

        boss.Update(MakeContext(boss.WorldX, boss.WorldY));

        Assert.Equal(6, boss.NextSurvivalPhase);
        Assert.Contains(boss.Phase, new[] { 4, 5 });
        Assert.False(boss.SurvivalActive);
    }

    [Fact]
    public void Update_FinalSurvivalCompletion_BeginsDeath()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(9);
        boss.TransitionRemaining = 0;
        boss.SurvivalRemaining = 0;

        boss.Update(MakeContext(boss.WorldX, boss.WorldY));

        Assert.True(boss.Dying);
        Assert.Equal(boss.DeathDuration, boss.DeathRemaining);
        Assert.Equal(1, boss.Hp);
    }

    [Fact]
    public void JeraGrandFinaleLastsFortySecondsAndDeathLastsTen()
    {
        var boss = MakeBoss();

        boss.DebugSetPhase(9);

        Assert.Equal(40.0, boss.SurvivalDuration);
        Assert.Equal(40.0, boss.SurvivalRemaining);
        Assert.Equal(10.0, boss.DeathDuration);
        Assert.True(boss.TakeDamage(1000).Blocked);
    }

    [Fact]
    public void Update_DyingCountdownElapsed_SetsHpToZero()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(9);
        boss.TransitionRemaining = 0;
        boss.SurvivalRemaining = 0;
        var context = MakeContext(boss.WorldX, boss.WorldY);
        boss.Update(context);
        Assert.True(boss.Dying);

        for (int i = 0; i < 1300; i++) // deathDuration (10s) * 120 ticks/s == 1200
            boss.Update(context);

        Assert.Equal(0, boss.Hp);
        Assert.True(boss.IsDead());
    }

    [Fact]
    public void Update_PhaseOne_FiresPortalMinesAndSineFan()
    {
        var boss = MakeBoss();
        boss.MineCooldown = 0;
        boss.PatternCooldown = 0;
        var context = MakeContext(boss.WorldX + 100, boss.WorldY);

        boss.Update(context);

        Assert.Contains(context.ProjectileSink, p => p.Shape == "mine");
        Assert.Contains(context.ProjectileSink, p => p.Path == "sine");
    }

    [Fact]
    public void Update_PhaseOnePortal_OrbitsAndFiresAShotgun()
    {
        var boss = MakeBoss();
        var portal = boss.ProjectilePortals[0];
        portal.FireCooldown = 0;
        float oldAngle = portal.Angle;
        var context = MakeContext(boss.WorldX, boss.WorldY);

        boss.Update(context);

        Assert.NotEqual(oldAngle, portal.Angle);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void CrossfireCarousel_FiresATangentialVolley()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(2);
        boss.TransitionRemaining = 0;
        boss.CarouselCooldown = 0;
        var context = MakeContext(boss.WorldX, boss.WorldY);

        boss.Update(context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "dissonance_portal_carousel");
    }

    [Fact]
    public void MirrorStep_JumpsThenFiresLandingEchoes()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(5);
        boss.TransitionRemaining = 0;
        boss.MirrorCooldown = 0;
        var context = MakeContext(boss.WorldX + 300, boss.WorldY);

        boss.Update(context); // starts the jump
        Assert.True(boss.MirrorJumpRemaining > 0);
        Assert.Empty(context.ProjectileSink);

        for (int i = 0; i < 120 && boss.MirrorJumpRemaining > 0; i++)
            boss.Update(context);

        Assert.Equal(0, boss.MirrorJumpRemaining);
        Assert.Contains(context.ProjectileSink, p => p.Owner == "dissonance_mirror_portal_landing_echo");
    }

    [Fact]
    public void PortalRelay_TransfersThenRedirects()
    {
        // Mirrors the Python oracle's own testing technique (test_portal_relay_transfers_then_redirects_a_shotgun):
        // call the phase-attack method directly with a controlled dt large enough to force each
        // cooldown/pending-timer past zero in one call, rather than looping real per-frame ticks.
        var boss = MakeBoss();
        boss.DebugSetPhase(6);
        boss.TransitionRemaining = 0;
        Assert.Equal(6, boss.Phase);

        boss.RelayCooldown = 0;
        var transfer = new List<EnemyProjectile>();
        boss.PhasePortalRelay(transfer, .1);
        Assert.Contains(transfer, p => p.Owner == "dissonance_relay_transfer");

        var redirected = new List<EnemyProjectile>();
        boss.PhasePortalRelay(redirected, .5); // relayPending.Timer (.42) - .5 < 0 -> fires the redirect now
        Assert.Equal(5, redirected.Count(p => p.Owner == "dissonance_relay_redirect"));
    }

    [Fact]
    public void RuneCannon_ChargesThenFiresAtReceiver()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(7);
        boss.TransitionRemaining = 0;

        boss.RuneCannonCooldown = 0;
        var armSink = new List<EnemyProjectile>();
        boss.UpdateRuneCannon(boss.WorldX, boss.WorldY, armSink, .1);
        Assert.NotNull(boss.RuneCannonReceiver);
        Assert.True(boss.RuneCannonCharge > 0);

        var fireSink = new List<EnemyProjectile>();
        boss.UpdateRuneCannon(boss.WorldX, boss.WorldY, fireSink, 1.5); // charge (1.4) - 1.5 < 0 -> fires now
        Assert.Equal(9, fireSink.Count); // pelletCount passed to fire_toward
        Assert.Contains(fireSink, p => p.Owner!.EndsWith("_rune_cannon"));
        Assert.Null(boss.RuneCannonReceiver);
    }

    [Fact]
    public void RoutePlayerBullet_ValidPolarityPair_RelocatesAndEmpowersBullet()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(4); // deploys 4 polarity-alternating "dissonance_static" portals
        boss.TransitionRemaining = 0;
        var source = boss.ProjectilePortals[0];
        var bullet = new Bullet(source.WorldX, source.WorldY, 4, 0, 300, 10, Color.White, 1, 2, false);

        bool routed = boss.RoutePlayerBullet(bullet, 0);

        Assert.True(routed);
        Assert.True(bullet.PortalCooldown > 0);
        Assert.True(bullet.Damage > 2);
    }

    [Fact]
    public void RoutePlayerBullet_StillOnCooldown_DoesNothing()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(4);
        boss.TransitionRemaining = 0;
        var bullet = new Bullet(boss.ProjectilePortals[0].WorldX, boss.ProjectilePortals[0].WorldY, 4, 0, 300, 10, Color.White, 1, 2, false);
        bullet.RouteThroughPortal(bullet.WorldX, bullet.WorldY, 0, 1.0f, .5f);

        bool routed = boss.RoutePlayerBullet(bullet, 0);

        Assert.False(routed);
    }

    [Fact]
    public void CinematicTransition_ClearsFormationAndHoldsFiveSeconds()
    {
        var boss = MakeBoss();
        boss.CinematicTransitionsEnabled = true;
        boss.SetPhase(4);
        Assert.Equal(5.0, boss.TransitionRemaining);
        Assert.Equal(5.0, boss.PhaseAnnouncementTimer);
        Assert.True(boss.TransitionCleanupRequested);
        Assert.NotNull(boss.TransitionTarget);
    }

    [Fact]
    public void ChallengeResults_TracksPortalsBrokenAndPerfectStaggers()
    {
        var boss = MakeBoss();
        var initial = boss.ChallengeResults();
        Assert.True(initial["no_portals_broken"]);
        Assert.True(initial["unbroken_pressure"]);

        for (int i = 0; i < 15; i++)
            boss.TakeDamage(1, "portal:0");

        Assert.False(boss.ChallengeResults()["no_portals_broken"]);
    }

    [Fact]
    public void CubeGeometry_ChangesOverTimeAndByPhase()
    {
        var boss = MakeBoss();
        var (first, _) = boss.CubeGeometry(new Vector2(400, 300), 40, age: 0, phase: 1);
        var (later, _) = boss.CubeGeometry(new Vector2(400, 300), 40, age: 80, phase: 7);
        Assert.NotEqual(first[0], later[0]);
    }

    [Fact]
    public void GetWorldHitboxes_IncludesActivePortals_ButNotDuringSurvival()
    {
        var boss = MakeBoss();
        var hitboxes = boss.GetWorldHitboxes();
        Assert.Contains(hitboxes, h => h.Part == "portal:0");

        boss.DebugSetPhase(3); // a survival phase
        boss.TransitionRemaining = 0;
        Assert.DoesNotContain(boss.GetWorldHitboxes(), h => h.Part.StartsWith("portal:"));
    }

    [Fact]
    public void FifteenPortalHits_DisablesInterceptionAndHalvesFirepower()
    {
        var boss = MakeBoss();
        var portal = boss.ProjectilePortals[0];
        for (int i = 0; i < 14; i++)
            Assert.True(boss.TakeDamage(1, "portal:0").Applied);
        Assert.True(portal.BlocksShots);

        Assert.True(boss.TakeDamage(1, "portal:0").Applied);

        Assert.True(portal.Active);
        Assert.True(portal.PhaseDisabled);
        Assert.False(portal.BlocksShots);
        var disabledShots = new List<EnemyProjectile>();
        portal.FireToward(disabledShots, boss.ArenaCenter, pelletCount: 7);
        Assert.Equal(4, disabledShots.Count); // (7+1)//2 when phase-disabled
    }

    [Theory]
    [InlineData(1, 3, 2.0 / 3)]
    [InlineData(4, 6, 1.0 / 3)]
    [InlineData(7, 9, 0.0)]
    public void ActGatesCannotSkipTheCurrentDamagePhrase(int phase, int survivalPhase, double ratio)
    {
        Simulation.ResetForTests();
        var boss = MakeBoss();
        boss.DebugSetPhase(phase);
        boss.NextSurvivalPhase = survivalPhase;
        var context = MakeContext(boss.WorldX + 300, boss.WorldY);

        boss.TakeDamage(boss.MaxHp * 4.0);

        int floor = survivalPhase == 9 ? 1 : (int)Math.Round(boss.MaxHp * ratio);
        Assert.Equal(floor, boss.Hp);
        Assert.Equal(phase, boss.Phase);
        Assert.False(boss.IsDead());

        ReachDeclarations(boss, context, Dissonance.MinimumDamagePhaseDeclarations);
        Step(boss, context);

        Assert.Equal(survivalPhase, boss.Phase);
        Assert.True(boss.SurvivalActive);
    }

    [Fact]
    public void DisablingThreePortalsEarnsAResonantStagger()
    {
        var boss = MakeBoss();

        for (int portalIndex = 0; portalIndex < 3; portalIndex++)
            for (int hit = 0; hit < 15; hit++)
                boss.TakeDamage(1, $"portal:{portalIndex}");

        Assert.Equal(3, boss.PortalsBroken);
        Assert.True(boss.IsStaggered);
        Assert.Equal(boss.StaggerDuration, boss.StaggerRemaining);
    }

    [Fact]
    public void JeraBuildsAllNineRememberedRuneChords()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(9);

        Assert.Equal(1, boss.JeraChordRingCount);
        boss.SurvivalRemaining = boss.SurvivalDuration / 2;
        Assert.InRange(boss.JeraChordRingCount, 5, 6);
        boss.SurvivalRemaining = 0;
        Assert.Equal(Dissonance.MaximumJeraChordRings, boss.JeraChordRingCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void EveryRuneStaysWithinTheEncounterOwnedThreatBudget(int phase)
    {
        var pressure = SimulatePressure(phase);

        Assert.InRange(pressure.Peak, 1, Dissonance.ActiveThreatSoftCap + 8);
        Assert.Equal(0, pressure.Overflow);
        Assert.True(pressure.Hits > 0,
            $"Dissonance phase {phase} never threatened the stationary outer player. Peak={pressure.Peak}.");
    }
}
