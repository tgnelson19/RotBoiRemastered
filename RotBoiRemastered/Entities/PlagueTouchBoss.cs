using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Ten plague-themed sigils shared by <see cref="PlagueTouchBoss"/>'s phase
/// display and <see cref="Bair"/>/<see cref="Sting"/>'s `phaseSigils` index
/// lists. Ported from bossTypes.py's module-level `PLAGUE_SIGILS`.
/// </summary>
public static class PlagueSigils
{
    public static readonly IReadOnlyList<(string Name, Vector2[][] Strokes)> All = new (string, Vector2[][])[]
    {
        ("CORRUPTION", new[]
        {
            new[] { new Vector2(-.68f, -.48f), new Vector2(0, -.72f), new Vector2(.68f, -.48f), new Vector2(0, .72f), new Vector2(-.68f, -.48f) },
            new[] { new Vector2(-.5f, .05f), new Vector2(.5f, .05f) },
        }),
        ("OVERRUN", new[]
        {
            new[] { new Vector2(-.7f, .45f), new Vector2(-.35f, -.35f), new Vector2(0, .15f), new Vector2(.35f, -.35f), new Vector2(.7f, .45f) },
            new[] { new Vector2(-.48f, .45f), new Vector2(0, .68f), new Vector2(.48f, .45f) },
        }),
        ("INFESTATION", new[]
        {
            new[] { new Vector2(0, -.72f), new Vector2(0, .72f) },
            new[] { new Vector2(-.65f, -.35f), new Vector2(.65f, .35f) },
            new[] { new Vector2(-.65f, .35f), new Vector2(.65f, -.35f) },
        }),
        ("INVASION", new[]
        {
            new[] { new Vector2(-.72f, .55f), new Vector2(-.35f, -.55f), new Vector2(0, .05f), new Vector2(.35f, -.55f), new Vector2(.72f, .55f) },
            new[] { new Vector2(-.72f, .1f), new Vector2(.72f, .1f) },
        }),
        ("PESTILENCE", new[]
        {
            new[] { new Vector2(-.7f, -.5f), new Vector2(.7f, .5f) },
            new[] { new Vector2(.7f, -.5f), new Vector2(-.7f, .5f) },
            new[] { new Vector2(0, -.76f), new Vector2(0, .76f) },
        }),
        ("AFFLICTION", new[]
        {
            new[]
            {
                new Vector2(-.65f, 0), new Vector2(-.3f, -.5f), new Vector2(0, 0), new Vector2(.3f, -.5f), new Vector2(.65f, 0),
                new Vector2(.3f, .5f), new Vector2(0, 0), new Vector2(-.3f, .5f), new Vector2(-.65f, 0),
            },
        }),
        ("IMPACT", new[]
        {
            new[] { new Vector2(0, -.78f), new Vector2(-.55f, .1f), new Vector2(-.12f, .1f), new Vector2(-.48f, .72f) },
            new[] { new Vector2(.18f, -.35f), new Vector2(.65f, .05f), new Vector2(.28f, .05f), new Vector2(.55f, .68f) },
        }),
        ("DEVOUR", new[]
        {
            new[] { new Vector2(-.72f, -.42f), new Vector2(0, 0), new Vector2(-.72f, .42f) },
            new[] { new Vector2(.72f, -.42f), new Vector2(0, 0), new Vector2(.72f, .42f) },
            new[] { new Vector2(0, -.72f), new Vector2(0, .72f) },
        }),
        ("DARKNESS", new[]
        {
            new[]
            {
                new Vector2(-.72f, 0), new Vector2(-.35f, -.48f), new Vector2(.35f, -.48f), new Vector2(.72f, 0),
                new Vector2(.35f, .48f), new Vector2(-.35f, .48f), new Vector2(-.72f, 0),
            },
            new[] { new Vector2(-.28f, 0), new Vector2(.28f, 0) },
        }),
        ("SEVERANCE", new[]
        {
            new[] { new Vector2(-.72f, -.58f), new Vector2(.72f, .58f) },
            new[] { new Vector2(.72f, -.58f), new Vector2(-.72f, .58f) },
            new[] { new Vector2(-.72f, 0), new Vector2(-.18f, 0) },
            new[] { new Vector2(.18f, 0), new Vector2(.72f, 0) },
        }),
    };
}

/// <summary>Per-phase flavor/color/sigil data a <see cref="PlagueTouchBoss"/> subclass supplies. Ported from Bair/Sting's `phaseFlavors`/`phaseColors`/`phaseSigils` class attributes.</summary>
public sealed record PlagueSigilConfig(IReadOnlyList<string> PhaseFlavors, IReadOnlyList<Color> PhaseColors, IReadOnlyList<int> PhaseSigils);

/// <summary>
/// Shared base for the Touch content path's mid/final bosses (<see cref="Bair"/>/<see cref="Sting"/>).
/// Ported from bossTypes.py's PlagueTouchBoss. Fully overrides
/// <see cref="PathChaseBoss"/>'s Update/Draw (its own portal-driven combat
/// and movement, not the chase-the-player base behavior) but still calls
/// `base.Draw` for the shared arena rendering + generic body + eye overlay,
/// matching Python's `super().drawEnemy(screen)` call.
/// </summary>
public class PlagueTouchBoss : PathChaseBoss
{
    public static readonly PathChaseBossConfig BaseConfig = PathChaseBossConfig.Default with
    {
        ArenaShape = "square", ArenaScale = 9.4,
        MotionTheme = BossMotionTheme.Touch,
        MovementPhases = Array.Empty<BossMovementPhaseProfile>(),
    };

    protected readonly PlagueSigilConfig SigilConfig;
    protected readonly List<TouchPortal> TouchPortals = new();
    public double PortalCooldown { get; set; } = .4;
    public int PortalIndex { get; set; }
    public int PatternRotation { get; set; }
    public double PhaseAnnouncementTimer { get; set; } = 3.0;
    private readonly List<PendingPortalVolley> _pendingPortalVolleys = new();
    private sealed record PendingPortalVolley(
        TouchPortal Portal, Vector2 Target, double Remaining);
    protected override bool VisualSurvivalActive => PhaseAnnouncementTimer > 1.65 || base.VisualSurvivalActive;
    protected virtual double PortalFireCadence => 1.15;
    protected virtual double PortalWarningDuration => 0.0;
    protected virtual float? PortalProjectileLifetime => null;
    protected virtual float? PortalProjectileRange => null;
    protected virtual int PortalPelletCount => 2;
    protected virtual float PortalSpread => .12f;
    protected virtual float PortalShotSpeed => .42f;
    protected virtual float PortalShotDamage => Config.FinalBoss ? 300f : 240f;

    public PlagueTouchBoss(float worldX, float worldY, Battleground battleground, PathChaseBossConfig config,
        PlagueSigilConfig sigilConfig, Random? rng = null)
        : base(worldX, worldY, battleground, config, rng)
    {
        SigilConfig = sigilConfig;
        Phase = 1;
        PhaseLabel = config.PhaseLabels[0];
        PhaseFlavor = sigilConfig.PhaseFlavors[0];
        PhaseAccent = sigilConfig.PhaseColors[0];
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked || FinaleActive)
            return;
        int count = Config.PhaseLabels.Count;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int phase = Math.Min(count, (int)((1 - ratio) * count + 1e-9) + 1);
        if (phase != Phase)
            SetPlaguePhase(phase);
    }

    protected virtual void SetPlaguePhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, Config.PhaseLabels.Count);
        PhaseLabel = Config.PhaseLabels[Phase - 1];
        PhaseFlavor = SigilConfig.PhaseFlavors[Phase - 1];
        PhaseAccent = SigilConfig.PhaseColors[Phase - 1];
        PhaseElapsed = 0.0;
        PhaseAnnouncementTimer = 3.0;
        TransitionCleanupRequested = true;
        ClearTouchPortals();
        int portalCount = PortalCountForPhase(Phase);
        if (portalCount > 0)
            DeployTouchPortals(portalCount);
    }

    public override void DebugSetPhase(int phase)
    {
        SetPlaguePhase(phase);
        DebugPhaseLocked = true;
        AttackCooldown = 0f;
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (Dying || FinaleActive)
            return new HitResult(false, false, 0, true);
        if (partId.StartsWith("portal:"))
        {
            int index = int.Parse(partId["portal:".Length..]);
            if (index >= 0 && index < TouchPortals.Count)
            {
                TouchPortals[index].TakeDamage((float)amount);
                // Matches Python's `blocked=not broken and False` -- always False
                // regardless of whether the hit disabled the portal (looks like a
                // leftover Python expression bug; preserved for observable parity).
                return new HitResult(true, false, amount, false);
            }
        }
        int previousHp = Hp;
        var result = base.TakeDamage(amount, partId, source);
        if (!DebugPhaseLocked && Phase < Config.PhaseLabels.Count)
        {
            double gate = MaxHp * (double)(Config.PhaseLabels.Count - Phase) / Config.PhaseLabels.Count;
            Hp = Math.Max(Hp, (int)Math.Round(gate));
        }
        return new HitResult(result.Applied, Hp <= 0, previousHp - Hp, result.Blocked);
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var hitboxes = base.GetScreenHitboxes(camera, playerWorldPosition, screenShake).ToList();
        for (int index = 0; index < TouchPortals.Count; index++)
        {
            var portal = TouchPortals[index];
            if (portal.BlocksShots)
            {
                var screenPosition = camera.WorldToScreen(new Vector2(portal.WorldX, portal.WorldY), playerWorldPosition, screenShake);
                hitboxes.Add(($"portal:{index}", new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)portal.Size, (int)portal.Size)));
            }
        }
        return hitboxes;
    }

    protected virtual int PortalCountForPhase(int phase)
    {
        if (Config.FinalBoss)
            return new[] { 2, 4, 7, 9, 10 }.Contains(phase) ? 4 : 0;
        return phase switch { 2 => 2, 4 => 4, _ => 0 };
    }

    protected void ClearTouchPortals()
    {
        _pendingPortalVolleys.Clear();
        foreach (var portal in TouchPortals)
            portal.RemFlag = true;
        TouchPortals.Clear();
    }

    protected void DeployTouchPortals(int count)
    {
        for (int index = 0; index < count; index++)
        {
            var portal = new TouchPortal(ArenaCenter, ArenaRadius * .78f, index * 2f * MathF.PI / count,
                angularSpeed: index % 2 == 0 ? .09f : -.09f, fireInterval: 999f, pelletCount: 2, spread: .2f,
                owner: $"{Config.OwnerPrefix}_plague_gate", color: PhaseAccent);
            portal.ResetForPhase(PlagueSigils.All[SigilConfig.PhaseSigils[Phase - 1]].Strokes);
            TouchPortals.Add(portal);
        }
    }

    protected EnemyProjectile PlagueProjectile(List<EnemyProjectile> sink, float direction, float speed, float damage, string suffix,
        float sizeScale = .25f, string path = "linear", Vector2? target = null)
    {
        var center = Center();
        float size = Size * sizeScale;
        var shot = new EnemyProjectile(center.X - size / 2f, center.Y - size / 2f, direction, speed, damage, size,
            travelRange: Simulation.TileSize * 35f, color: PhaseAccent, shape: path == "bomb" ? "bomb" : "diamond",
            path: path, target: target, owner: $"{Config.OwnerPrefix}_{suffix}", ignoreWalls: true);
        if (path == "bomb")
        {
            shot.FuseDuration = 2.8f;
            shot.BlastRadius = Simulation.TileSize * 1.7f;
            shot.BurstCount = 8;
        }
        sink.Add(shot);
        return shot;
    }

    protected void Radial(List<EnemyProjectile> sink, int count, float speed, float damage, string suffix)
    {
        for (int index = 0; index < count; index++)
            PlagueProjectile(sink, index * 2f * MathF.PI / count + PatternRotation * .11f, speed, damage, suffix);
    }

    protected void Fan(List<EnemyProjectile> sink, float playerX, float playerY, int count, float spread, float speed, float damage, string suffix)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            PlagueProjectile(sink, aimed + offset, speed, damage, suffix);
        }
    }

    protected EnemyProjectile Projectile(List<EnemyProjectile> sink, float direction, float speed, float damage, string suffix,
        float sizeScale = .25f, string path = "linear", Vector2? target = null)
        => PlagueProjectile(sink, direction, speed, damage, suffix, sizeScale, path, target);

    /// <summary>
    /// Lets a concrete plague encounter apply its own authored health gates
    /// without duplicating portal-part routing or trying to bypass this base
    /// class's generic five/ten-way phase floors.
    /// </summary>
    protected HitResult ApplyPlagueBodyDamage(double amount, string partId, DamageSource source) =>
        base.TakeDamage(amount, partId, source);

    protected virtual void FirePlaguePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        // Base PlagueTouchBoss has no attack pattern of its own -- Bair/Sting override this.
    }

    private void UpdateTouchPortals(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        foreach (var portal in TouchPortals)
        {
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
            portal.UpdateBursts(sink, (float)dt);
            portal.TelegraphTimer = Math.Max(0f, portal.TelegraphTimer - (float)dt);
        }
        for (int index = _pendingPortalVolleys.Count - 1; index >= 0; index--)
        {
            var pending = _pendingPortalVolleys[index];
            double remaining = pending.Remaining - dt;
            if (remaining > 0)
            {
                _pendingPortalVolleys[index] = pending with { Remaining = remaining };
                continue;
            }
            FirePortalVolley(pending.Portal, pending.Target, sink);
            _pendingPortalVolleys.RemoveAt(index);
        }
        PortalCooldown -= dt;
        if (PortalCooldown <= 0 && TouchPortals.Count > 0)
        {
            var portal = TouchPortals[PortalIndex % TouchPortals.Count];
            var target = new Vector2(playerX, playerY);
            if (PortalWarningDuration > 0)
            {
                portal.TelegraphTimer = (float)PortalWarningDuration;
                portal.TelegraphKind = "line";
                portal.TelegraphTarget = target;
                _pendingPortalVolleys.Add(new PendingPortalVolley(
                    portal, target, PortalWarningDuration));
            }
            else
            {
                FirePortalVolley(portal, target, sink);
            }
            PortalIndex += 1;
            PortalCooldown = PortalFireCadence;
        }
    }

    private void FirePortalVolley(TouchPortal portal, Vector2 target,
        List<EnemyProjectile> sink)
    {
        if (portal.RemFlag || !portal.Active)
            return;
        int start = sink.Count;
        portal.FireToward(sink, target, PortalPelletCount, PortalSpread,
            PortalShotSpeed, PortalShotDamage, PhaseAccent, "heavy");
        for (int index = start; index < sink.Count; index++)
        {
            if (PortalProjectileLifetime.HasValue)
                sink[index].Lifetime = PortalProjectileLifetime.Value;
            if (PortalProjectileRange.HasValue)
                sink[index].RemainingRange = Math.Min(
                    sink[index].RemainingRange, PortalProjectileRange.Value);
        }
    }

    public sealed override void Update(EnemyUpdateContext context)
    {
        if (UpdateDeathSpectacle())
            return;
        double dt = Seconds();
        if (UpdateFinaleSequence(dt))
            return;
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        PhaseElapsed += dt;
        PhaseAnnouncementTimer = Math.Max(0.0, PhaseAnnouncementTimer - dt);
        UpdatePhase();
        UpdateLocomotion(context);
        UpdateTouchPortals(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink, dt);
        AttackCooldown -= (float)Simulation.GetTimerStep();
        if (EntranceRemaining <= 0 && AttackCooldown <= 0)
        {
            FirePlaguePattern(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            MarkAttack(.58f);
            AttackCooldown = AttackCooldownMax!.Value * Math.Max(.4f, 1f - .055f * (Phase - 1));
        }
    }

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        float walkRoll = Moved ? BossAnimation.Sine(VisualAgeSeconds, 3.4f) * Size * .012f : 0f;
        var center = screenPosition + new Vector2(Size / 2f, Size / 2f + walkRoll);
        if (Dying)
        {
            BossVisuals.Disassemble(spriteBatch, center, Age, DeathProgress, Size, new Color(111, 69, 43), new Color(211, 84, 42), 15);
            return;
        }

        float attack = VisualAttackPulse;
        float detach = VisualSurvivalActive ? 1.5f : 1f;
        Color mud = new(105, 70, 43);
        Color orange = new(211, 116, 46);
        Color red = new(157, 55, 39);
        float settle = BossAnimation.CosinePulse(VisualAgeSeconds,
            PresentationProfile.IdlePeriodSeconds) * .035f;
        float compression = Math.Clamp(attack * .12f + settle, 0f, .18f);
        Vector2 coreCenter = center + new Vector2(0, attack * Size * .035f);
        BossVisuals.Cuboid(spriteBatch, coreCenter, Size * .62f,
            Size * (.72f - compression), mud, orange, 0f);
        float plateOffset = Size * (.43f * detach - attack * .025f);
        for (int side = -1; side <= 1; side += 2)
        {
            BossVisuals.HingedPlate(spriteBatch,
                coreCenter + new Vector2(side * plateOffset, 0),
                Size * .54f, Size * .17f,
                side < 0 ? new Color(91, 63, 43) : new Color(121, 75, 42),
                PhaseAccent, MathF.PI / 2f);
        }
        for (int groove = -1; groove <= 1; groove++)
        {
            float y = coreCenter.Y + groove * Size * .12f;
            Primitives2D.Line(spriteBatch,
                new Vector2(coreCenter.X - Size * .24f, y),
                new Vector2(coreCenter.X + Size * .24f, y),
                groove == 0 ? PhaseAccent : Color.Lerp(mud, UiTheme.Cream, .2f),
                groove == 0 ? 3 : 2);
        }

        int blobCount = Config.FinalBoss ? 13 : 9;
        for (int index = 0; index < blobCount; index++)
        {
            float cycle = BossAnimation.LoopPhase(VisualAgeSeconds,
                Math.Max(.9f, 1.38f - attack * .55f), index * .173f);
            float seamFade = BossAnimation.SeamFade(cycle, .13f);
            float angle = index * 2.399f + Age * .0016f;
            float radius = Size * (.24f + cycle * .48f) * detach;
            float roll = MathF.Sin(cycle * MathF.PI) * Size * .32f;
            var point = center + new Vector2(MathF.Cos(angle) * radius,
                -Size * .44f + cycle * Size * .9f - roll * .24f);
            float blob = Size * (.055f + .038f * (1f - cycle)) * seamFade;
            Color color = (index % 3 == 0 ? red : index % 2 == 0 ? orange : new Color(132, 82, 43)) * seamFade;
            Primitives2D.FillCircle(spriteBatch, point + new Vector2(3, 4), blob + 3 * seamFade, UiTheme.Shadow * seamFade);
            Primitives2D.FillCircle(spriteBatch, point, blob, color);
            if (blob >= 2)
                Primitives2D.CircleOutline(spriteBatch, point, blob, UiTheme.Ink * seamFade, Math.Max(1, (int)(blob * .18f)), 18);
        }
        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .72f), (int)(Size * .92f), 6));
    }

    private string DrawPlagueSigil(SpriteBatch spriteBatch, Vector2 center, float radius)
    {
        var (name, strokes) = PlagueSigils.All[SigilConfig.PhaseSigils[Phase - 1]];
        foreach (var stroke in strokes)
        {
            var points = stroke.Select(p => center + p * radius).ToArray();
            if (points.Length <= 1)
                continue;
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Ink, Math.Max(5, (int)(radius * .14f)));
            Primitives2D.Polyline(spriteBatch, points, false, PhaseAccent, Math.Max(2, (int)(radius * .07f)));
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Cream, Math.Max(1, (int)(radius * .025f)));
        }
        return name;
    }

    public sealed override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (!Dying)
            foreach (var portal in TouchPortals)
                portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        if (Dying)
            return;
        var screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        string sigil = DrawPlagueSigil(spriteBatch, rect.Center.ToVector2(), Size * .32f);
        if (PhaseAnnouncementTimer > 0)
        {
            UiTheme.DrawText(spriteBatch, $"{sigil} // {PhaseLabel}", 11, PhaseAccent,
                new Vector2(rect.Center.X, rect.Y - 18), "midbottom");
        }
    }
}
