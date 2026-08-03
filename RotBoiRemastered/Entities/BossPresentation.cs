namespace RotBoiRemastered.Entities;

/// <summary>The authored visual scale of a recurring sense encounter.</summary>
public enum BossVisualTier
{
    Guardian,
    Midpoint,
    Finale,
}

/// <summary>Stable procedural silhouette grammar used by one sense family.</summary>
public enum BossSilhouetteStyle
{
    Resonator,
    Press,
    Aperture,
    Reactor,
    Prism,
}

/// <summary>Presentation-only lifecycle state. It never changes combat behavior.</summary>
public enum BossPoseState
{
    Idle,
    Entrance,
    Anticipation,
    Commit,
    Recovery,
    Stagger,
    Survival,
    Transition,
    Death,
}

/// <summary>
/// Typed visual direction shared by guardians and their two authored bosses.
/// Values are deliberately cosmetic: no profile field can affect a hitbox,
/// attack timer, health gate, or movement target.
/// </summary>
public readonly record struct BossPresentationProfile(
    BossMotionTheme Theme,
    BossVisualTier Tier,
    BossSilhouetteStyle Silhouette,
    float IdlePeriodSeconds,
    float SecondaryMotion,
    float AttackOvershoot,
    int CosmeticBudget)
{
    public static BossPresentationProfile For(BossMotionTheme theme, BossVisualTier tier)
    {
        BossSilhouetteStyle silhouette = theme switch
        {
            BossMotionTheme.Touch => BossSilhouetteStyle.Press,
            BossMotionTheme.Sight => BossSilhouetteStyle.Aperture,
            BossMotionTheme.Chemesthesis => BossSilhouetteStyle.Reactor,
            BossMotionTheme.Phantasia => BossSilhouetteStyle.Prism,
            _ => BossSilhouetteStyle.Resonator,
        };
        float tierScale = tier switch
        {
            BossVisualTier.Guardian => .78f,
            BossVisualTier.Finale => 1.22f,
            _ => 1f,
        };
        return theme switch
        {
            BossMotionTheme.Touch => new(theme, tier, silhouette, 5.8f,
                .34f * tierScale, .08f, (int)(18 * tierScale)),
            BossMotionTheme.Phantasia => new(theme, tier, silhouette, 3.8f,
                .92f * tierScale, .18f, (int)(32 * tierScale)),
            BossMotionTheme.Sound => new(theme, tier, silhouette, 2f,
                .48f * tierScale, .11f, (int)(22 * tierScale)),
            BossMotionTheme.Chemesthesis => new(theme, tier, silhouette, 2.7f,
                1.08f * tierScale, .21f, (int)(28 * tierScale)),
            BossMotionTheme.Sight => new(theme, tier, silhouette, 1.8f,
                .76f * tierScale, .16f, (int)(20 * tierScale)),
            _ => throw new ArgumentOutOfRangeException(nameof(theme)),
        };
    }
}

internal static class BossPresentation
{
    public static BossPoseState ResolvePose(
        bool dying,
        bool entrance,
        bool transition,
        bool survival,
        bool staggered,
        float anticipation,
        float attackPulse)
    {
        if (dying)
            return BossPoseState.Death;
        if (entrance)
            return BossPoseState.Entrance;
        if (transition)
            return BossPoseState.Transition;
        if (survival)
            return BossPoseState.Survival;
        if (staggered)
            return BossPoseState.Stagger;
        if (anticipation > .01f)
            return BossPoseState.Anticipation;
        if (attackPulse > .5f)
            return BossPoseState.Commit;
        if (attackPulse > .01f)
            return BossPoseState.Recovery;
        return BossPoseState.Idle;
    }
}
