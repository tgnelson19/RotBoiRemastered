using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public sealed class BossPresentationTests
{
    [Fact]
    public void EveryNaturalBossAndGuardianUsesTheTypedSensePresentationGrammar()
    {
        Simulation.ResetForTests();
        var battleground = Battleground.GenerateSound();
        var families = new[]
        {
            Family(
                new PathGuardianBoss(1000, 1000, "sound", 2, 10000, new Random(1)).PresentationProfile,
                new Beaudis(1000, 1000, 10000, new Random(2)).PresentationProfile,
                new Dissonance(1000, 1000, 10000, battleground, new Random(3)).PresentationProfile),
            Family(
                new PathGuardianBoss(1000, 1000, "touch", 2, 10000, new Random(4)).PresentationProfile,
                new Bair(1000, 1000, battleground, new Random(5)).PresentationProfile,
                new Rot(1000, 1000, battleground, new Random(6)).PresentationProfile),
            Family(
                new PathGuardianBoss(1000, 1000, "sight", 2, 10000, new Random(7)).PresentationProfile,
                new Ishe(1000, 1000, battleground, new Random(8)).PresentationProfile,
                new Chronos(1000, 1000, battleground, new Random(9)).PresentationProfile),
            Family(
                new PathGuardianBoss(1000, 1000, "chemesthesis", 2, 10000, new Random(10)).PresentationProfile,
                new Kage(1000, 1000, battleground, new Random(11)).PresentationProfile,
                new Ache(1000, 1000, battleground, new Random(12)).PresentationProfile),
            Family(
                new PathGuardianBoss(1000, 1000, "phantasia", 2, 10000, new Random(13)).PresentationProfile,
                new Hypno(1000, 1000, battleground, new Random(14)).PresentationProfile,
                new Malady(1000, 1000, battleground, new Random(15)).PresentationProfile),
        };

        Assert.Equal(5, families.Select(family => family.Guardian.Theme).Distinct().Count());
        Assert.Equal(5, families.Select(family => family.Guardian.Silhouette).Distinct().Count());
        foreach (var family in families)
        {
            Assert.Equal(BossVisualTier.Guardian, family.Guardian.Tier);
            Assert.Equal(BossVisualTier.Midpoint, family.Midpoint.Tier);
            Assert.Equal(BossVisualTier.Finale, family.Finale.Tier);
            Assert.Equal(family.Guardian.Theme, family.Midpoint.Theme);
            Assert.Equal(family.Guardian.Theme, family.Finale.Theme);
            Assert.Equal(family.Guardian.Silhouette, family.Midpoint.Silhouette);
            Assert.Equal(family.Guardian.Silhouette, family.Finale.Silhouette);
            Assert.True(family.Guardian.CosmeticBudget < family.Finale.CosmeticBudget);
        }
    }

    [Fact]
    public void PresentationPosePriorityKeepsLifecycleStatesUnambiguous()
    {
        Assert.Equal(BossPoseState.Idle,
            BossPresentation.ResolvePose(false, false, false, false, false, 0, 0));
        Assert.Equal(BossPoseState.Recovery,
            BossPresentation.ResolvePose(false, false, false, false, false, 0, .2f));
        Assert.Equal(BossPoseState.Commit,
            BossPresentation.ResolvePose(false, false, false, false, false, 0, .8f));
        Assert.Equal(BossPoseState.Anticipation,
            BossPresentation.ResolvePose(false, false, false, false, false, .5f, .8f));
        Assert.Equal(BossPoseState.Survival,
            BossPresentation.ResolvePose(false, false, false, true, true, 1, 1));
        Assert.Equal(BossPoseState.Transition,
            BossPresentation.ResolvePose(false, false, true, true, true, 1, 1));
        Assert.Equal(BossPoseState.Entrance,
            BossPresentation.ResolvePose(false, true, true, true, true, 1, 1));
        Assert.Equal(BossPoseState.Death,
            BossPresentation.ResolvePose(true, true, true, true, true, 1, 1));
    }

    [Fact]
    public void NewAnimationCurvesAreContinuousAndBoundedAtLoopSeams()
    {
        const float epsilon = .00001f;
        foreach (float period in new[] { .5f, 1.8f, 5.8f })
        {
            float pulseBefore = BossAnimation.CosinePulse(period - epsilon, period);
            float pulseAfter = BossAnimation.CosinePulse(period + epsilon, period);
            Assert.InRange(MathF.Abs(pulseBefore - pulseAfter), 0, .0001f);
        }
        for (int sample = 0; sample <= 100; sample++)
        {
            float value = sample / 100f;
            Assert.InRange(BossAnimation.EaseInOutSine(value), 0f, 1f);
            Assert.False(float.IsNaN(BossAnimation.EaseOutBack(value)));
        }
        Assert.Equal(0f, BossAnimation.AttackPulse(0, .5f), 5);
    }

    [Fact]
    public void MaladyConstellationChangesBlendInsteadOfSnapping()
    {
        Simulation.ResetForTests();
        var battleground = Battleground.GenerateSound();
        var boss = new Malady(1000, 1000, battleground, new Random(21));
        boss.DebugSetPhase(4);
        boss.EntranceRemaining = 0;
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = 1800,
            PlayerWorldY = 1200,
            Battleground = battleground,
        };

        boss.Update(context);
        Assert.Equal(4, boss.VisualConstellationPhase);
        Assert.Equal(0f, boss.VisualConstellationBlend);
        boss.Update(context);
        Assert.InRange(boss.VisualConstellationBlend, 0.0001f, .99f);
    }

    private static (BossPresentationProfile Guardian,
        BossPresentationProfile Midpoint,
        BossPresentationProfile Finale) Family(
            BossPresentationProfile guardian,
            BossPresentationProfile midpoint,
            BossPresentationProfile finale) => (guardian, midpoint, finale);
}
