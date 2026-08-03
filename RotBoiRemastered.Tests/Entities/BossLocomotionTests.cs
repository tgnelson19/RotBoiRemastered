using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public sealed class BossLocomotionTests
{
    private static EnemyUpdateContext Context(Enemy boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 900,
        PlayerWorldY = boss.WorldY + 350,
        Battleground = battleground,
    };

    [Fact]
    public void EveryNaturalBossAndGuardianHasOneTypedProfilePerPhase()
    {
        var authored = new[]
        {
            (Bair.BairConfig.PhaseLabels.Count, Bair.BairConfig.MovementPhases.Count),
            (Rot.RotConfig.PhaseLabels.Count, Rot.RotConfig.MovementPhases.Count),
            (Ishe.IsheConfig.PhaseLabels.Count, Ishe.IsheConfig.MovementPhases.Count),
            (Chronos.ChronosConfig.PhaseLabels.Count, Chronos.ChronosConfig.MovementPhases.Count),
            (Kage.KageConfig.PhaseLabels.Count, Kage.KageConfig.MovementPhases.Count),
            (Ache.AcheConfig.PhaseLabels.Count, Ache.AcheConfig.MovementPhases.Count),
            (Hypno.HypnoConfig.PhaseLabels.Count, Hypno.HypnoConfig.MovementPhases.Count),
            (Malady.MaladyConfig.PhaseLabels.Count, Malady.MaladyConfig.MovementPhases.Count),
            (5, Beaudis.MovementPhases.Count),
            (9, Dissonance.PhaseMovement.Count),
        };
        Assert.All(authored, counts => Assert.Equal(counts.Item1, counts.Item2));
        Assert.All(PathGuardianBoss.SenseProfiles.Values, profile =>
        {
            Assert.Equal(3, profile.Phases.Count);
            Assert.All(profile.Phases, phase =>
                Assert.NotEqual(BossPathShape.None, phase.Movement.Mode == BossMovementMode.FixedPath
                    ? phase.Movement.Path
                    : BossPathShape.Circle));
        });
    }

    [Fact]
    public void EveryAuthoredStationaryFamilyKeepsBitExactWorldPositionAndRejectsKnockback()
    {
        Simulation.ResetForTests();
        var battleground = Battleground.GenerateSound();
        PathChaseBoss[] bosses =
        [
            new Bair(1000, 1000, battleground, new Random(1)),
            new Rot(1000, 1000, battleground, new Random(2)),
            new Ishe(1000, 1000, battleground, new Random(3)),
            new Chronos(1000, 1000, battleground, new Random(4)),
            new Kage(1000, 1000, battleground, new Random(5)),
            new Ache(1000, 1000, battleground, new Random(6)),
            new Hypno(1000, 1000, battleground, new Random(7)),
            new Malady(1000, 1000, battleground, new Random(8)),
        ];
        int[] stationaryPhases = [3, 1, 3, 2, 3, 4, 3, 3];

        for (int index = 0; index < bosses.Length; index++)
        {
            PathChaseBoss boss = bosses[index];
            boss.DebugSetPhase(stationaryPhases[index]);
            boss.EntranceRemaining = 0;
            float startX = boss.WorldX, startY = boss.WorldY;
            boss.ApplyKnockback(240, -170, battleground);
            var context = Context(boss, battleground);
            for (int tick = 0; tick < Simulation.FrameRate * 3; tick++)
                boss.Update(context);
            Assert.Equal(startX, boss.WorldX);
            Assert.Equal(startY, boss.WorldY);
            Assert.False(boss.Moved);
        }
    }

    [Fact]
    public void SoundStationaryPhasesAndGuardianTrialRemainLocked()
    {
        Simulation.ResetForTests();
        var battleground = Battleground.GenerateSound();

        var beaudis = new Beaudis(1000, 1000, 10000, new Random(11));
        beaudis.DebugSetPhase(3);
        beaudis.EntranceRemaining = 0;
        AssertLocked(beaudis, Context(beaudis, battleground), battleground, 180);

        var dissonance = new Dissonance(1000, 1000, 10000, battleground, new Random(12))
        {
            CinematicTransitionsEnabled = false,
        };
        dissonance.DebugSetPhase(2);
        AssertLocked(dissonance, Context(dissonance, battleground), battleground, 180);

        var guardian = new PathGuardianBoss(1000, 1000, "sound", 4, 10000,
            new Random(13));
        guardian.DebugStartTrial();
        AssertLocked(guardian, Context(guardian, battleground), battleground, 180);
    }

    [Fact]
    public void FixedPathsAreContinuousCoverBothAxesAndRespectThemeSpeedOrder()
    {
        float[] seed = Enumerable.Range(0, 28)
            .Select(index => MathF.Sin(index * 2.7f) * .12f).ToArray();
        Vector2 arena = new(1000, 1000);
        Vector2 boss = arena + new Vector2(250, 0);
        Vector2 player = arena - new Vector2(300, 120);
        var sight = new BossLocomotionController(BossMotionTheme.Sight, seed);
        var touch = new BossLocomotionController(BossMotionTheme.Touch, seed);
        var sightChase = sight.Update(1, BossMovementPhaseProfile.Chase(), boss,
            player, arena, 500, .2f, 1.0 / 120);
        var touchChase = touch.Update(1, BossMovementPhaseProfile.Chase(), boss,
            player, arena, 500, .2f, 1.0 / 120);
        Assert.True(sightChase.SpeedPerReferenceTick > touchChase.SpeedPerReferenceTick);

        var path = new BossLocomotionController(BossMotionTheme.Phantasia, seed);
        var profile = BossMovementPhaseProfile.Fixed(
            BossPathShape.FigureEight, 10f, .65f, .48f);
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        Vector2? previous = null;
        float largestStep = 0;
        for (int tick = 0; tick < 1200; tick++)
        {
            BossLocomotionFrame frame = path.Update(1, profile, boss, player,
                arena, 500, .2f, 1.0 / 120);
            minX = Math.Min(minX, frame.Target.X);
            maxX = Math.Max(maxX, frame.Target.X);
            minY = Math.Min(minY, frame.Target.Y);
            maxY = Math.Max(maxY, frame.Target.Y);
            if (previous.HasValue)
                largestStep = Math.Max(largestStep,
                    Vector2.Distance(previous.Value, frame.Target));
            previous = frame.Target;
        }
        Assert.True(maxX - minX > 500);
        Assert.True(maxY - minY > 350);
        Assert.True(largestStep < 12, $"Path target jumped {largestStep:0.00}px.");
    }

    [Fact]
    public void ChemesthesisJaggedRoutesAreSeededAndDeterministic()
    {
        float[] seed = [.1f, -.08f, .03f, .12f, -.04f];
        var first = new BossLocomotionController(BossMotionTheme.Chemesthesis, seed);
        var second = new BossLocomotionController(BossMotionTheme.Chemesthesis, seed);
        var profile = BossMovementPhaseProfile.Fixed(BossPathShape.Jagged, 9f);
        Vector2 boss = new(1200, 1000), player = new(700, 800), arena = new(1000, 1000);
        for (int tick = 0; tick < 300; tick++)
        {
            BossLocomotionFrame a = first.Update(2, profile, boss, player, arena,
                500, .2f, 1.0 / 120);
            BossLocomotionFrame b = second.Update(2, profile, boss, player, arena,
                500, .2f, 1.0 / 120);
            Assert.Equal(a.Target.X, b.Target.X, 5);
            Assert.Equal(a.Target.Y, b.Target.Y, 5);
        }
    }

    [Fact]
    public void AnimationLoopsHideTheirWrapAndCubeMaterialsStayOnPhysicalFaces()
    {
        const float epsilon = .0001f;
        Assert.InRange(BossAnimation.SeamFade(epsilon), 0, .001f);
        Assert.InRange(BossAnimation.SeamFade(1f - epsilon), 0, .001f);
        float before = BossAnimation.Sine(4f - epsilon, 4f);
        float after = BossAnimation.Sine(4f + epsilon, 4f);
        Assert.InRange(MathF.Abs(before - after), 0, .001f);

        Color primary = new(90, 60, 35), secondary = new(45, 75, 40), accent = Color.Olive;
        Color[] first = Enumerable.Range(0, 6)
            .Select(face => BossVisuals.PhysicalCubeFaceColor(face, primary, secondary, accent))
            .ToArray();
        Color[] second = Enumerable.Range(0, 6)
            .Select(face => BossVisuals.PhysicalCubeFaceColor(face, primary, secondary, accent))
            .ToArray();
        Assert.Equal(first, second);
        Assert.True(first.Distinct().Count() >= 4);
    }

    [Fact]
    public void RotMiasmaBurrowTelegraphsRelocatesAndPausesCombatWhileSubmerged()
    {
        Simulation.ResetForTests();
        var battleground = Battleground.GenerateSound();
        float bodySize = Simulation.TileSize * (float)Rot.RotConfig.FinalBodyScale;
        var rot = new Rot(
            battleground.Width * Simulation.TileSize / 2f - bodySize / 2f,
            battleground.Height * Simulation.TileSize / 2f - bodySize / 2f,
            battleground, new Random(41));
        rot.DebugSetPhase(6);
        rot.EntranceRemaining = 0;
        var context = Context(rot, battleground);

        for (int tick = 0; tick < 1200 && rot.BurrowState == RotBurrowState.Surface; tick++)
            rot.Update(context);

        Assert.Equal(RotBurrowState.Sinking, rot.BurrowState);
        Vector2 original = new(rot.WorldX, rot.WorldY);
        Assert.True(Vector2.Distance(rot.BurrowMudCenter, rot.ArenaCenter)
            <= rot.ArenaRadius * .72f);

        for (int tick = 0; tick < 120 && rot.BurrowState == RotBurrowState.Sinking; tick++)
            rot.Update(context);
        Assert.Equal(RotBurrowState.Submerged, rot.BurrowState);
        Assert.Empty(rot.GetWorldHitboxes());
        Assert.True(rot.TakeDamage(1000).Blocked);
        float? pausedCooldown = rot.AttackCooldown;
        for (int tick = 0; tick < 20; tick++)
            rot.Update(context);
        Assert.Equal(pausedCooldown, rot.AttackCooldown);
        Assert.Equal(original.X, rot.WorldX);
        Assert.Equal(original.Y, rot.WorldY);

        for (int tick = 0; tick < 150 && rot.BurrowState == RotBurrowState.Submerged; tick++)
            rot.Update(context);
        Assert.Equal(RotBurrowState.Rising, rot.BurrowState);
        Assert.NotEmpty(rot.GetWorldHitboxes());
        Assert.NotEqual(original, new Vector2(rot.WorldX, rot.WorldY));

        for (int tick = 0; tick < 120 && rot.BurrowState != RotBurrowState.Surface; tick++)
            rot.Update(context);
        Assert.Equal(RotBurrowState.Surface, rot.BurrowState);
    }

    private static void AssertLocked(Enemy boss, EnemyUpdateContext context,
        Battleground battleground, int ticks)
    {
        float startX = boss.WorldX, startY = boss.WorldY;
        boss.ApplyKnockback(220, -140, battleground);
        for (int tick = 0; tick < ticks; tick++)
            boss.Update(context);
        Assert.Equal(startX, boss.WorldX);
        Assert.Equal(startY, boss.WorldY);
    }
}
