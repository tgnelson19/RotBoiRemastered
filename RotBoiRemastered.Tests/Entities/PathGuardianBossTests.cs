using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public sealed class PathGuardianBossTests
{
    private sealed record GuardianPressure(
        int Peak,
        int Emitted,
        int Hits,
        string HitDetails);

    private static EnemyUpdateContext Context(Battleground battleground, List<EnemyProjectile> sink) => new()
    {
        PlayerWorldX = battleground.Width * Battleground.TileSize / 2f + 150,
        PlayerWorldY = battleground.Height * Battleground.TileSize / 2f,
        Battleground = battleground,
        ProjectileSink = sink,
    };

    private static void ReachPhaseAttackRequirement(
        PathGuardianBoss boss,
        EnemyUpdateContext context)
    {
        for (int frame = 0; frame < 1200
            && boss.AttacksCompletedInPhase < PathGuardianBoss.MinimumAttacksPerPhase;
            frame++)
        {
            boss.Update(context);
        }
        Assert.True(boss.AttacksCompletedInPhase
            >= PathGuardianBoss.MinimumAttacksPerPhase);
    }

    private static GuardianPressure SimulatePressure(
        string senseKey,
        bool trial)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 8, new Random(120));
        var center = layout.BossRoom.WorldCenter;
        var boss = new PathGuardianBoss(
            center.X, center.Y, senseKey, 8, float.PositiveInfinity,
            new Random(121), Simulation.TileSize * 9f);
        if (trial)
            boss.DebugStartTrial();
        else
            boss.DebugSetPhase(3);
        var player = boss.ArenaCenter + Vector2.UnitX * boss.ArenaRadius * .52f;
        int playerSize = (int)(Simulation.TileSize * .72f);
        var playerRect = new Rectangle(
            (int)(player.X - playerSize / 2f),
            (int)(player.Y - playerSize / 2f),
            playerSize, playerSize);
        var sink = new List<EnemyProjectile>();
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = layout.Battleground,
            ProjectileSink = sink,
        };
        var emitted = new HashSet<EnemyProjectile>();
        var hits = new HashSet<EnemyProjectile>();
        var hitDetails = new List<string>();
        int peak = 0;

        for (int frame = 0; frame < Simulation.FrameRate * 14; frame++)
        {
            boss.Update(context);
            if (trial && !boss.TrialActive)
                break; // GameSession clears the trial field at this boundary.
            var children = new List<EnemyProjectile>();
            foreach (var projectile in sink.ToList())
            {
                emitted.Add(projectile);
                if (projectile.Damage > 0 && projectile.Collides(playerRect)
                    && hits.Add(projectile))
                {
                    hitDetails.Add(
                        $"frame={frame}, dir={projectile.Direction:0.000}, "
                        + $"real={projectile.TruthMarked}, path={projectile.Path}");
                }
                projectile.Update(layout.Battleground, casualMode: false);
                if (projectile.Damage > 0 && projectile.Collides(playerRect)
                    && hits.Add(projectile))
                {
                    hitDetails.Add(
                        $"frame={frame}, dir={projectile.Direction:0.000}, "
                        + $"real={projectile.TruthMarked}, path={projectile.Path}");
                }
                children.AddRange(projectile.SpawnedProjectiles);
                projectile.SpawnedProjectiles.Clear();
                var projectileCenter = new Vector2(
                    projectile.WorldX + projectile.Size / 2f,
                    projectile.WorldY + projectile.Size / 2f);
                if (Vector2.Distance(projectileCenter, boss.ArenaCenter)
                    > boss.ArenaRadius * 1.05f)
                {
                    projectile.RemFlag = true;
                }
            }
            sink.RemoveAll(projectile => projectile.RemFlag);
            sink.AddRange(children);
            peak = Math.Max(peak, sink.Count);
        }

        return new GuardianPressure(
            peak,
            emitted.Count,
            hits.Count,
            string.Join("; ", hitDetails.Take(4)));
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void Update_FiresSenseSpecificPattern(string senseKey)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 2, new Random(3));
        var center = layout.BossRoom.WorldCenter;
        var boss = new PathGuardianBoss(center.X, center.Y, senseKey, 2, float.PositiveInfinity, new Random(4));
        var sink = new List<EnemyProjectile>();
        var context = Context(layout.Battleground, sink);

        for (int frame = 0; frame < 600 && sink.Count == 0; frame++)
            boss.Update(context);

        Assert.NotEmpty(sink);
        Assert.True(senseKey switch
        {
            "sound" => sink.Any(projectile => projectile.Path == "sine"),
            "touch" => sink.Any(projectile => projectile.Path == "bank"),
            "sight" => sink.Any(projectile => projectile.Speed > 1.5f),
            "chemesthesis" => sink.Any(projectile => projectile.Path is "mine" or "sine"),
            "phantasia" => sink.Any(projectile => projectile.Illusory),
            _ => false,
        });
    }

    [Fact]
    public void LessonsAdvanceOnTheirClockThroughInvulnerableTransitions()
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate("sound", 1, new Random(5));
        var boss = new PathGuardianBoss(layout.BossRoom.WorldCenter.X, layout.BossRoom.WorldCenter.Y,
            "sound", 1, float.PositiveInfinity, new Random(6));
        var context = Context(layout.Battleground, new List<EnemyProjectile>());

        ReachPhaseAttackRequirement(boss, context);
        Assert.Equal(PathGuardianBoss.MinimumAttacksPerPhase, boss.AttacksCompletedInPhase);

        // A lesson surrenders at most its budget; health no longer advances
        // the encounter on its own.
        var hit = boss.TakeDamage(boss.MaxHp * .4);
        Assert.True(hit.Applied);
        Assert.Equal(1, boss.Phase);

        AdvanceUntilLessonTurnsOver(boss, context, 1);
        Assert.Equal(2, boss.Phase);
        // Every handover is protected while the arena clears.
        Assert.True(boss.Invulnerable);
        DrainTransition(boss, context);

        ReachPhaseAttackRequirement(boss, context);
        AdvanceToNextLesson(boss, context, 2);
        Assert.Equal(3, boss.Phase);
        // Guardians are the simplest encounters and hold no survival trial.
        Assert.False(boss.TrialActive);
    }

    /// <summary>
    /// Runs the guardian until its phase clock hands over to the next lesson.
    /// </summary>
    private static void AdvanceUntilLessonTurnsOver(
        PathGuardianBoss boss, EnemyUpdateContext context, int fromPhase)
    {
        for (int frame = 0; frame < 6000 && boss.Phase == fromPhase; frame++)
        {
            boss.DebugCompletePhaseClock();
            boss.Update(context);
        }
    }

    private static void DrainTransition(PathGuardianBoss boss, EnemyUpdateContext context)
    {
        for (int frame = 0; frame < 600 && boss.TransitionRemaining > 0; frame++)
            boss.Update(context);
    }

    private static void AdvanceToNextLesson(
        PathGuardianBoss boss, EnemyUpdateContext context, int fromPhase)
    {
        AdvanceUntilLessonTurnsOver(boss, context, fromPhase);
        DrainTransition(boss, context);
    }

    [Theory]
    [InlineData("sound", "FOOTFALL", "COUNTERBEAT", "RESONANT PURSUIT")]
    [InlineData("touch", "NEAR / FAR", "COMPRESSION", "PULSE LOCK")]
    [InlineData("sight", "REFRACTION", "LENS MAZE", "WHITE GEOMETRY")]
    [InlineData("chemesthesis", "CARRIER", "PROPAGATION", "CHAIN BLOOM")]
    [InlineData("phantasia", "TRUTH PETAL", "LUCID PASSAGE", "FALSE AWAKENING")]
    public void PhaseGates_RequireSenseSpecificLessons(
        string senseKey, string first, string second, string third)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 3, new Random(40));
        var center = layout.BossRoom.WorldCenter;
        var boss = new PathGuardianBoss(
            center.X, center.Y, senseKey, 3, float.PositiveInfinity, new Random(41));
        var context = Context(layout.Battleground, new List<EnemyProjectile>());

        while (boss.EntranceRemaining > 0)
            boss.Update(context);
        Assert.Equal(first, boss.PhaseLabel);
        boss.TakeDamage(boss.MaxHp);
        Assert.Equal(1, boss.Phase);
        // One lesson surrenders at most fifteen percent of maximum health.
        Assert.Equal(
            boss.MaxHp - (int)Math.Round(boss.MaxHp * BossPhaseGovernor.DefaultThresholdFraction),
            boss.Hp);
        Assert.True(boss.TakeDamage(100).Blocked);

        ReachPhaseAttackRequirement(boss, context);
        AdvanceToNextLesson(boss, context, 1);
        Assert.Equal(2, boss.Phase);
        Assert.Equal(second, boss.PhaseLabel);

        ReachPhaseAttackRequirement(boss, context);
        AdvanceToNextLesson(boss, context, 2);
        Assert.Equal(3, boss.Phase);
        Assert.False(boss.TrialActive);
        Assert.Equal(third, boss.PhaseLabel);
        Assert.True(boss.Hp > 0);
    }

    [Fact]
    public void SenseProfiles_ProvideDistinctBossIdentityAndPhaseLanguage()
    {
        Assert.Equal(GamePaths.Paths.Select(path => path.Key).Order(),
            PathGuardianBoss.SenseProfiles.Keys.Order());
        Assert.Equal(5, PathGuardianBoss.SenseProfiles.Values
            .Select(profile => profile.BossName).Distinct().Count());
        Assert.All(PathGuardianBoss.SenseProfiles.Values, profile =>
        {
            Assert.Equal(3, profile.Phases.Count);
            Assert.Equal(3, profile.Phases.Select(phase => phase.Label)
                .Distinct().Count());
            Assert.All(profile.Phases, phase =>
            {
                Assert.False(string.IsNullOrWhiteSpace(phase.Flavor));
                Assert.InRange(phase.CadenceSeconds, 1.2f, 2.8f);
            });
            Assert.InRange(profile.TrialDuration, 5.5, 7.5);
        });
    }

    [Theory]
    [InlineData("sound", "THE HELD NOTE")]
    [InlineData("touch", "LOCKED VALVE")]
    [InlineData("sight", "BLIND ANGLE")]
    [InlineData("chemesthesis", "INCUBATION")]
    [InlineData("phantasia", "FALSE AWAKENING")]
    public void SecondThreshold_StartsProtectedSenseTrialBeforeFinalDamagePhase(
        string senseKey,
        string trialLabel)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 3, new Random(50));
        var boss = new PathGuardianBoss(
            layout.BossRoom.WorldCenter.X,
            layout.BossRoom.WorldCenter.Y,
            senseKey, 3, float.PositiveInfinity, new Random(51));
        var sink = new List<EnemyProjectile>();
        var context = Context(layout.Battleground, sink);

        while (boss.EntranceRemaining > 0)
            boss.Update(context);
        ReachPhaseAttackRequirement(boss, context);
        AdvanceToNextLesson(boss, context, 1);
        ReachPhaseAttackRequirement(boss, context);
        AdvanceToNextLesson(boss, context, 2);

        // The trial is no longer part of the natural flow -- guardians are
        // the one tier with no survival phase -- but the debug hook that
        // exercises its presentation still works.
        Assert.Equal(3, boss.Phase);
        Assert.False(boss.TrialActive);
        boss.DebugStartTrial();

        Assert.True(boss.TrialActive);
        Assert.Equal(trialLabel, boss.PhaseLabel);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int frame = 0; frame < 1200 && boss.TrialActive; frame++)
            boss.Update(context);

        Assert.False(boss.TrialActive);
        Assert.Equal(PathGuardianBoss.SenseProfiles[senseKey].Phases[2].Label,
            boss.PhaseLabel);
        Assert.NotEmpty(sink);
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void ThreatAdmission_NeverExceedsGuardianSoftCap(string senseKey)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 8, new Random(61));
        var boss = new PathGuardianBoss(
            layout.BossRoom.WorldCenter.X,
            layout.BossRoom.WorldCenter.Y,
            senseKey, 8, float.PositiveInfinity, new Random(62));
        var sink = new List<EnemyProjectile>();
        var context = Context(layout.Battleground, sink);

        for (int frame = 0; frame < 2400; frame++)
            boss.Update(context);

        Assert.InRange(sink.Count, 1, PathGuardianBoss.ActiveThreatSoftCap);
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void SecondAct_AddsPatternComplexityWithoutRaisingThreatCap(
        string senseKey)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 8, new Random(70));
        var center = layout.BossRoom.WorldCenter;
        var firstAct = new PathGuardianBoss(
            center.X, center.Y, senseKey, 4, float.PositiveInfinity,
            new Random(71));
        var secondAct = new PathGuardianBoss(
            center.X, center.Y, senseKey, 8, float.PositiveInfinity,
            new Random(71));
        firstAct.DebugSetPhase(3);
        secondAct.DebugSetPhase(3);
        var firstSink = new List<EnemyProjectile>();
        var secondSink = new List<EnemyProjectile>();

        firstAct.Update(Context(layout.Battleground, firstSink));
        secondAct.Update(Context(layout.Battleground, secondSink));

        var variant = RotBoiRemastered.Systems.BossEncounterCatalog.GuardianVariantFor(
            senseKey,
            RotBoiRemastered.Systems.GuardianActVariant.SecondAct);
        Assert.Equal(1, variant.AdditionalAttackFamiliesPerPhase);
        Assert.NotEmpty(secondSink);
        Assert.True(secondSink.Count <= PathGuardianBoss.ActiveThreatSoftCap);
    }

    [Theory]
    [InlineData("sound", .24f)]
    [InlineData("touch", .78f)]
    [InlineData("sight", .32f)]
    [InlineData("chemesthesis", .82f)]
    [InlineData("phantasia", .38f)]
    public void HighConsequencePatterns_HaveSenseAppropriateTelegraphs(
        string senseKey,
        float minimumTelegraph)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 8, new Random(80));
        var center = layout.BossRoom.WorldCenter;
        var boss = new PathGuardianBoss(
            center.X, center.Y, senseKey, 8, float.PositiveInfinity,
            new Random(81));
        boss.DebugSetPhase(3);
        var sink = new List<EnemyProjectile>();

        boss.Update(Context(layout.Battleground, sink));

        Assert.NotEmpty(sink);
        var consequential = senseKey switch
        {
            "chemesthesis" => sink.Where(projectile =>
                projectile.Path == "mine").ToList(),
            "phantasia" => sink.Where(projectile =>
                projectile.TruthMarked).ToList(),
            _ => sink,
        };
        Assert.NotEmpty(consequential);
        Assert.All(consequential, projectile =>
            Assert.True(projectile.TelegraphDuration >= minimumTelegraph,
                $"{senseKey} emitted {projectile.Path}/{projectile.Shape} "
                + $"with {projectile.TelegraphDuration:0.00}s warning."));
    }

    [Fact]
    public void AttackAnticipation_BuildsBeforeACommittedPattern()
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate("sight", 4, new Random(90));
        var boss = new PathGuardianBoss(
            layout.BossRoom.WorldCenter.X,
            layout.BossRoom.WorldCenter.Y,
            "sight", 4, float.PositiveInfinity, new Random(91));
        boss.DebugSetPhase(2);
        boss.AttackCooldown = Simulation.FrameRate * .2f;
        var sink = new List<EnemyProjectile>();

        boss.Update(Context(layout.Battleground, sink));

        Assert.True(boss.AttackAnticipation > 0);
        Assert.Empty(sink);
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void TrialPatterns_ExposeAReadableSenseSpecificSurvivalRule(
        string senseKey)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(senseKey, 8, new Random(92));
        var boss = new PathGuardianBoss(
            layout.BossRoom.WorldCenter.X,
            layout.BossRoom.WorldCenter.Y,
            senseKey, 8, float.PositiveInfinity, new Random(93));
        boss.DebugStartTrial();
        boss.AttackCooldown = 0;
        var sink = new List<EnemyProjectile>();

        boss.Update(Context(layout.Battleground, sink));

        Assert.NotEmpty(sink);
        switch (senseKey)
        {
            case "sound":
                Assert.True(sink.Count < 22); // omitted player-facing wedge
                Assert.All(sink, projectile =>
                    Assert.Equal("sine", projectile.Path));
                break;
            case "touch":
                Assert.Equal(3, sink.Count);
                Assert.All(sink, projectile =>
                    Assert.True(projectile.TelegraphDuration >= 1.08f));
                break;
            case "sight":
                Assert.Equal(4, sink.Count);
                Assert.All(sink, projectile =>
                    Assert.Equal("laser", projectile.Path));
                break;
            case "chemesthesis":
                Assert.True(sink.Count <= 7); // three-mine safe sector
                Assert.All(sink, projectile =>
                    Assert.Equal("mine", projectile.Path));
                break;
            case "phantasia":
                Assert.Contains(sink, projectile => projectile.TruthMarked);
                Assert.True(sink.Count(projectile => projectile.Illusory)
                    > sink.Count(projectile => projectile.TruthMarked));
                break;
        }
    }

    [Fact]
    public void LethalDamage_UsesProtectedDeathChoreography()
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate("sound", 4, new Random(94));
        var boss = new PathGuardianBoss(
            layout.BossRoom.WorldCenter.X,
            layout.BossRoom.WorldCenter.Y,
            "sound", 4, float.PositiveInfinity, new Random(95));
        boss.DebugSetPhase(3);
        boss.Hp = 10;
        boss.DebugRebasePhaseHealth();
        var context = Context(layout.Battleground,
            new List<EnemyProjectile>());

        var lethal = boss.TakeDamage(1000);

        Assert.True(lethal.Applied);
        Assert.False(lethal.Killed);
        Assert.True(boss.Dying);
        Assert.False(boss.IsDead());
        Assert.True(boss.TakeDamage(1).Blocked);

        for (int frame = 0; frame < 240 && !boss.IsDead(); frame++)
            boss.Update(context);

        Assert.True(boss.IsDead());
        Assert.Equal(0, boss.Hp);
    }

    [Fact]
    public void DemonstratedLesson_AutoResolvesAnAlreadyReachedHealthGate()
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate("touch", 4, new Random(96));
        var boss = new PathGuardianBoss(
            layout.BossRoom.WorldCenter.X,
            layout.BossRoom.WorldCenter.Y,
            "touch", 4, float.PositiveInfinity, new Random(97));
        var context = Context(layout.Battleground,
            new List<EnemyProjectile>());
        while (boss.EntranceRemaining > 0)
            boss.Update(context);

        boss.TakeDamage(boss.MaxHp);
        Assert.Equal(1, boss.Phase);
        Assert.Equal(
            boss.MaxHp - (int)Math.Round(boss.MaxHp * BossPhaseGovernor.DefaultThresholdFraction),
            boss.Hp);

        ReachPhaseAttackRequirement(boss, context);
        Assert.True(boss.PhaseGatePending);
        for (int frame = 0; frame < 120 && boss.Phase == 1; frame++)
            boss.Update(context);

        Assert.Equal(2, boss.Phase);
        Assert.False(boss.PhaseGatePending);
        Assert.True(boss.TransitionRemaining > 0);
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void FinaleAndTrialPressure_RemainBoundedAndReachPlayerSpace(
        string senseKey)
    {
        var finale = SimulatePressure(senseKey, trial: false);
        var trial = SimulatePressure(senseKey, trial: true);

        Assert.InRange(finale.Peak, 1, PathGuardianBoss.ActiveThreatSoftCap);
        Assert.InRange(trial.Peak, 1, PathGuardianBoss.ActiveThreatSoftCap);
        Assert.True(finale.Emitted >= 3);
        Assert.True(trial.Emitted >= 3);
        Assert.True(finale.Hits > 0,
            $"{senseKey} final phase never reached player space.");
        if (senseKey is "sound" or "chemesthesis" or "phantasia")
        {
            Assert.True(trial.Hits == 0,
                $"{senseKey} safe sector failed: {trial.HitDetails}");
        }
        else
        {
            Assert.True(trial.Hits > 0,
                $"{senseKey} trial never reached player space.");
        }
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("chemesthesis")]
    [InlineData("phantasia")]
    public void RareAlternatePattern_ExistsInEverySenseAndPhase(
        string senseKey)
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate(
            senseKey, 8, new Random(140));
        foreach (int phase in new[] { 1, 2, 3 })
        {
            var boss = new PathGuardianBoss(
                layout.BossRoom.WorldCenter.X,
                layout.BossRoom.WorldCenter.Y,
                senseKey,
                8,
                float.PositiveInfinity,
                new Random(141 + phase));
            boss.DebugSetPhase(phase);
            boss.DebugQueueRarePattern();
            boss.AttackCooldown = 0;
            var sink = new List<EnemyProjectile>();

            boss.Update(Context(layout.Battleground, sink));

            Assert.True(boss.LastPatternWasRare);
            Assert.Equal(1, boss.RarePatternsCommitted);
            Assert.InRange(
                sink.Count,
                1,
                PathGuardianBoss.ActiveThreatSoftCap);
            Assert.True(senseKey switch
            {
                "sound" => sink.All(projectile =>
                    projectile.Path == "sine"
                    && projectile.TelegraphDuration >= .44f),
                "touch" => sink.All(projectile =>
                    projectile.Path == "bank"
                    && projectile.TelegraphDuration >= 1.04f),
                "sight" => sink.All(projectile =>
                    projectile.Path == "laser"
                    && projectile.TelegraphDuration >= .96f),
                "chemesthesis" => sink.All(projectile =>
                    projectile.Path == "mine"
                    && projectile.TelegraphDuration >= 1.02f),
                "phantasia" => sink.Any(projectile =>
                    projectile.TruthMarked)
                    && sink.Any(projectile => projectile.Illusory),
                _ => false,
            });
        }
    }
}
