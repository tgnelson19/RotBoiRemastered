using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Per-family sin-motif content: seven-sins flavor/color text, sigil stroke
/// data, and act-transition metadata. Ported from bossTypes.py's
/// `SinChemesthesisBoss`/`Kage`/`Ache` class attributes (`phaseFlavors`,
/// `phaseColors`, `SIN_SIGILS`, `ACT_METADATA`) -- kept as a small composed
/// record (same reasoning as `PlagueSigilConfig`) rather than folded into
/// `PathChaseBossConfig`, since only this one family needs it.
/// </summary>
public sealed record SinSigilConfig(
    IReadOnlyList<string> PhaseFlavors,
    IReadOnlyList<Color> PhaseColors,
    IReadOnlyList<(string Name, Vector2[][] Strokes)> SinSigils,
    IReadOnlyDictionary<int, string> ActMetadata);

/// <summary>
/// Shared seven-sins pattern language for the Chemesthesis bosses (Kage,
/// Ache). Ported from bossTypes.py's `SinChemesthesisBoss`.
///
/// Unlike the rest of the `PathChaseBoss` family (Ishe/Chronos, the Touch
/// bosses), this family has a real stagger/fracture system -- a hit that
/// pushes `Stagger` to `MaxStagger` disables the boss for `StaggerDuration`
/// seconds -- and calls `Enemy.Update` directly during its own `Update`
/// (bypassing `PathChaseBoss.Update`'s movement-mode dispatch is still
/// needed, so this class re-implements that dispatch itself and calls the
/// new <see cref="PathChaseBoss.ChaseUpdate"/> wrapper instead of
/// `base.Update`).
/// </summary>
public abstract class SinChemesthesisBoss : PathChaseBoss
{
    protected override bool VisualSurvivalActive => ActTransitionTimer > 0 || PhaseProtectionTimer > 0 || IsStaggered || base.VisualSurvivalActive;
    protected SinSigilConfig SinConfig { get; }

    public static readonly PathChaseBossConfig BaseConfig = PathChaseBossConfig.Default with
    {
        ArenaShape = "jagged", ArenaScale = 10.1, MovementModes = new[] { "chase", "static", "path" },
    };

    public int PatternRotation { get; protected set; }
    public double ActTransitionTimer { get; protected set; }
    public double ActTransitionDuration { get; } = 2.2;
    public string ActTitle { get; protected set; } = "";
    public double PhaseProtectionTimer { get; protected set; }
    public int PreviousSigilPhase { get; protected set; } = 1;
    public double SigilTransitionTimer { get; protected set; } = 1.25;
    public double SigilTransitionDuration { get; } = 1.25;

    public double Stagger { get; protected set; }
    public virtual double MaxStagger => 100.0;
    public double MinimumStaggerPerHit { get; } = 4.0;
    public double StaggerDuration { get; } = 2.5;
    public double StaggerRemaining { get; protected set; }
    public bool IsStaggered { get; protected set; }
    public double StaggerDecayTimer { get; protected set; }
    protected virtual double StaggerDecayDelay => double.PositiveInfinity;
    protected virtual double StaggerDecayPerSecond => 0.0;

    protected virtual double ConsumedCrystalPulse => 0.0;
    protected virtual double DamageFloorRatio() =>
        Phase < Config.PhaseLabels.Count
            ? (double)(Config.PhaseLabels.Count - Phase) / Config.PhaseLabels.Count
            : 0.0;

    protected SinChemesthesisBoss(float worldX, float worldY, Battleground battleground,
        PathChaseBossConfig config, SinSigilConfig sinConfig, Random? rng = null)
        : base(worldX, worldY, battleground, config, rng)
    {
        SinConfig = sinConfig;
        PhaseFlavor = sinConfig.PhaseFlavors[0];
        PhaseAccent = sinConfig.PhaseColors[0];
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked || FinaleActive)
            return;
        int count = Config.PhaseLabels.Count;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int newPhase = Math.Min(count, (int)((1.0 - ratio) * count + 1e-9) + 1);
        if (newPhase != Phase)
            SetSinPhase(newPhase);
    }

    protected virtual void SetSinPhase(int phase)
    {
        PreviousSigilPhase = Phase;
        Phase = Math.Clamp(phase, 1, Config.PhaseLabels.Count);
        PhaseLabel = Config.PhaseLabels[Phase - 1];
        PhaseFlavor = SinConfig.PhaseFlavors[Phase - 1];
        PhaseAccent = SinConfig.PhaseColors[Phase - 1];
        AttackCooldown = Math.Min(AttackCooldown!.Value, Simulation.FrameRate * .45f);
        TransitionCleanupRequested = true;
        SigilTransitionTimer = SigilTransitionDuration;
        if (SinConfig.ActMetadata.TryGetValue(Phase, out var title))
        {
            ActTitle = title;
            ActTransitionTimer = ActTransitionDuration;
            PhaseProtectionTimer = ActTransitionDuration;
        }
    }

    public override void DebugSetPhase(int phase)
    {
        SetSinPhase(phase);
        DebugPhaseLocked = true;
        AttackCooldown = 0f;
    }

    protected EnemyProjectile Shot(List<EnemyProjectile> sink, float direction, float speed, float damage, float scale = .25f,
        string shape = "diamond", string path = "linear", float? lifetime = null, float speedDecay = 0f,
        float orbitRadius = 0f, float angularSpeed = 0f, string ownerSuffix = "sin", string? affliction = null,
        double afflictionDuration = 0.0, double afflictionStrength = 0.0, double exposure = 0.0,
        Vector2? afflictionSource = null)
    {
        var center = Center();
        float size = Size * scale;
        var shot = new EnemyProjectile(
            center.X - size / 2f, center.Y - size / 2f, direction, speed, damage, size,
            travelRange: Simulation.TileSize * (float)Config.ShotRangeTiles, color: PhaseAccent,
            shape: shape, path: path, lifetime: lifetime, speedDecay: speedDecay,
            orbitCenter: orbitRadius != 0f ? center : null, orbitRadius: orbitRadius, orbitAngle: direction,
            angularSpeed: angularSpeed, owner: $"{Config.OwnerPrefix}_{ownerSuffix}", ignoreWalls: true)
        {
            Affliction = affliction,
            AfflictionDuration = afflictionDuration,
            AfflictionStrength = afflictionStrength,
            Exposure = exposure,
            AfflictionSource = afflictionSource,
        };
        sink.Add(shot);
        return shot;
    }

    protected void Fan(List<EnemyProjectile> sink, float baseDirection, int count, float spread, float speed, float damage, string suffix)
    {
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            Shot(sink, baseDirection + offset, speed, damage, ownerSuffix: suffix);
        }
    }

    protected void Radial(List<EnemyProjectile> sink, int count, float speed, float damage, string suffix, bool mine = false)
    {
        float offset = PatternRotation * .19f;
        for (int index = 0; index < count; index++)
        {
            Shot(sink, 2f * MathF.PI * index / count + offset, speed, damage,
                scale: mine ? .27f : .22f, shape: mine ? "mine" : "diamond", path: mine ? "mine" : "linear",
                lifetime: mine ? 18f : null, speedDecay: mine ? .12f : 0f, ownerSuffix: suffix,
                exposure: mine ? .45 : 0.0);
        }
        PatternRotation++;
    }

    protected EnemyProjectile Bomb(List<EnemyProjectile> sink, float targetX, float targetY, float damage, string suffix,
        int burstCount = 10, float fuseDuration = 2.6f, float? burstShotDamage = null)
    {
        var center = Center();
        float size = Size * .34f;
        var bomb = new EnemyProjectile(center.X, center.Y, 0f, 0f, damage, size, color: PhaseAccent,
            shape: "bomb", path: "bomb", lifetime: 4.0f, target: new Vector2(targetX, targetY),
            owner: $"{Config.OwnerPrefix}_{suffix}", ignoreWalls: true)
        {
            FuseDuration = fuseDuration,
            BlastRadius = Simulation.TileSize * 1.8f,
            BurstCount = burstCount,
            // EnemyProjectile's bomb primitive applies a .28 multiplier when
            // constructing children. Callers may specify the desired final
            // child damage without knowing that internal representation.
            BurstDamage = burstShotDamage.HasValue ? burstShotDamage.Value / .28f : damage,
        };
        sink.Add(bomb);
        return bomb;
    }

    protected void Laser(List<EnemyProjectile> sink, float direction, float damage, string suffix, float angularSpeed = 0f)
    {
        var center = Center();
        var laser = new EnemyProjectile(center.X, center.Y, direction, 0f, damage, Size * .16f,
            travelRange: Simulation.TileSize * 30f, color: PhaseAccent, shape: "laser", path: "laser",
            lifetime: 2.35f, angularSpeed: angularSpeed, owner: $"{Config.OwnerPrefix}_{suffix}", ignoreWalls: true)
        {
            TelegraphDuration = .85f,
        };
        sink.Add(laser);
    }

    /// <summary>Ported from _parallel_lanes: an immediately legible wall of parallel, telegraphed lanes.</summary>
    protected void ParallelLanes(List<EnemyProjectile> sink, float direction, int count, float spacing, float damage, string suffix)
    {
        var center = Center();
        float perpendicular = direction + MathF.PI / 2f;
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * spacing;
            float startX = center.X + MathF.Cos(perpendicular) * offset;
            float startY = center.Y + MathF.Sin(perpendicular) * offset;
            var laser = new EnemyProjectile(startX, startY, direction, 0f, damage, Size * .14f,
                travelRange: Simulation.TileSize * 34f, color: PhaseAccent, shape: "laser", path: "laser",
                lifetime: 2.3f, owner: $"{Config.OwnerPrefix}_{suffix}", ignoreWalls: true)
            {
                TelegraphDuration = .95f,
            };
            sink.Add(laser);
        }
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (Dying || FinaleActive)
            return new HitResult(false, false, 0, true);
        if (PhaseProtectionTimer > 0 || ActTransitionTimer > 0)
            return new HitResult(false, false, 0, true);
        if (partId.StartsWith("crystal:"))
            return DamageCrystal(partId, amount);
        int previousHp = Hp;
        double multiplier = IsStaggered ? 1.25 : 1.0;
        var result = base.TakeDamage(amount * multiplier, partId, source);
        if (source == DamageSource.Direct && amount > 0 && !IsStaggered)
        {
            Stagger = Math.Min(MaxStagger, Stagger + Math.Max(MinimumStaggerPerHit, amount * .012));
            StaggerDecayTimer = StaggerDecayDelay;
            if (Stagger >= MaxStagger)
            {
                IsStaggered = true;
                StaggerRemaining = StaggerDuration;
                TransitionCleanupRequested = true;
            }
        }
        if (!DebugPhaseLocked)
        {
            double threshold = MaxHp * DamageFloorRatio();
            Hp = Math.Max(Hp, (int)threshold);
        }
        int applied = Math.Max(0, previousHp - Hp);
        return new HitResult(result.Applied, Hp <= 0, applied, result.Blocked);
    }

    /// <summary>Only Rot's crystal walls resolve "crystal:N" part IDs -- every other hit through this path is blocked.</summary>
    protected virtual HitResult DamageCrystal(string partId, double amount) => new(false, false, 0, true);

    /// <summary>Optional persistent terrain hook used by Rot.</summary>
    protected virtual void UpdateTerrain(float playerX, float playerY, double dt, EnemyUpdateContext context)
    {
    }

    /// <summary>Ported from movement_obstacles(). World-space rects GameSession.MovePlayer treats as extra walls -- only Rot returns any.</summary>
    public virtual IReadOnlyList<Rectangle> MovementObstacles() => Array.Empty<Rectangle>();

    /// <summary>Optional world-terrain rendering hook used by Rot.</summary>
    protected virtual void DrawPersistentTerrain(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
    }

    protected abstract void FireSinPattern(float playerX, float playerY, EnemyUpdateContext context);

    public override void Update(EnemyUpdateContext context)
    {
        if (UpdateDeathSpectacle())
            return;
        double dt = Seconds();
        if (UpdateFinaleSequence(dt))
            return;
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        ActTransitionTimer = Math.Max(0.0, ActTransitionTimer - dt);
        PhaseProtectionTimer = Math.Max(0.0, PhaseProtectionTimer - dt);
        PhaseElapsed += dt;
        SigilTransitionTimer = Math.Max(0.0, SigilTransitionTimer - dt);
        UpdateTerrain(context.PlayerWorldX, context.PlayerWorldY, dt, context);
        UpdatePhase();
        if (EntranceRemaining > 0 || ActTransitionTimer > 0)
        {
            AdvanceAge();
            return;
        }
        if (IsStaggered)
        {
            StaggerRemaining = Math.Max(0.0, StaggerRemaining - dt);
            if (StaggerRemaining <= 0)
            {
                IsStaggered = false;
                Stagger = 0.0;
            }
            AdvanceAge();
            return;
        }
        if (Stagger > 0 && StaggerDecayPerSecond > 0)
        {
            StaggerDecayTimer = Math.Max(0.0, StaggerDecayTimer - dt);
            if (StaggerDecayTimer <= 0)
                Stagger = Math.Max(0.0, Stagger - StaggerDecayPerSecond * dt);
        }

        string mode = Config.MovementModes[(Phase - 1) % Config.MovementModes.Count];
        float originalSpeed = Speed;
        float effectivePlayerX = context.PlayerWorldX, effectivePlayerY = context.PlayerWorldY;
        if (mode == "static")
        {
            Speed = 0;
        }
        else if (mode == "path")
        {
            float jitter = ArenaSeed[(Phase + PatternRotation) % ArenaSeed.Length];
            effectivePlayerX = ArenaCenter.X + MathF.Cos((float)PhaseElapsed * (.45f + jitter)) * ArenaRadius * .5f;
            effectivePlayerY = ArenaCenter.Y + MathF.Sin((float)PhaseElapsed * (.7f - jitter)) * ArenaRadius * .42f;
        }
        var effectiveContext = mode == "chase" ? context : new EnemyUpdateContext
        {
            PlayerWorldX = effectivePlayerX, PlayerWorldY = effectivePlayerY, Battleground = context.Battleground,
            ProjectileSink = context.ProjectileSink, AllEnemies = context.AllEnemies, ExperienceBubbles = context.ExperienceBubbles,
            Camera = context.Camera, BossAfflictions = context.BossAfflictions, PlayerBuildSnapshot = context.PlayerBuildSnapshot,
        };
        ChaseUpdate(effectiveContext);
        Speed = originalSpeed;
        AttackCooldown -= (float)Simulation.GetTimerStep();
        if (AttackCooldown <= 0)
        {
            // Scripted movement waypoints steer the boss only. Patterns must
            // target the actual player or path-mode phases attack empty space.
            FireSinPattern(context.PlayerWorldX, context.PlayerWorldY, context);
            double rate = Math.Max(.36, 1.0 - .08 * (Phase - 1));
            AttackCooldownMax ??= Simulation.FrameRate * (float)(Config.FinalBoss ? Config.FinalCooldownSeconds : Config.CooldownSeconds);
            AttackCooldown = AttackCooldownMax.Value * (float)(rate * (.92 + Rng.NextDouble() * .16));
        }
    }

    protected virtual void DrawFieldDiagram(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var center = camera.WorldToScreen(Center(), playerWorldPosition, screenShake);
        float tile = Simulation.TileSize;
        float extent = tile * (Config.FinalBoss ? 7.2f : 4.8f);
        float pulse = .5f + .5f * MathF.Sin(Age * .025f);
        var faint = new Color(PhaseAccent.R, PhaseAccent.G, PhaseAccent.B, (byte)Math.Clamp(24 + pulse * 18, 0, 255));
        var brightBase = UiTheme.Lighten(PhaseAccent, 35);
        var bright = new Color(brightBase.R, brightBase.G, brightBase.B, (byte)Math.Clamp(58 + pulse * 30, 0, 255));

        for (int ring = 1; ring < 4; ring++)
        {
            float radius = tile * (1.15f + ring * 1.18f + .08f * pulse);
            Primitives2D.CircleOutline(spriteBatch, center, radius, faint, Math.Max(1, ring % 3 + 1));
        }

        int motif = (Phase - 1) % 7;
        switch (motif)
        {
            case 0: // Pride: a rigid crown and cardinal axes.
                foreach (float angle in new[] { 0f, MathF.PI / 2f, MathF.PI, 3f * MathF.PI / 2f })
                {
                    var end = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * extent * .82f;
                    Primitives2D.Line(spriteBatch, center, end, bright, 2);
                }
                var crown = new[]
                {
                    center + new Vector2(-tile * 1.5f, -tile * .6f), center + new Vector2(-tile * .75f, -tile * 1.45f),
                    center + new Vector2(0, -tile * .72f), center + new Vector2(tile * .75f, -tile * 1.45f),
                    center + new Vector2(tile * 1.5f, -tile * .6f),
                };
                Primitives2D.Polyline(spriteBatch, crown, false, bright, 3);
                break;
            case 1: // Greed: nested containment cells.
                foreach (float radius in new[] { tile * 1.7f, tile * 2.7f, tile * 3.7f })
                {
                    var rect = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2), (int)(radius * 2));
                    Primitives2D.RectOutline(spriteBatch, rect, bright, 2);
                }
                break;
            case 2: // Lust: converging attraction spokes.
                for (int index = 0; index < 8; index++)
                {
                    float angle = index * MathF.PI / 4f + Age * .002f;
                    var outer = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * extent * .82f;
                    var inner = center + new Vector2(MathF.Cos(angle + .22f), MathF.Sin(angle + .22f)) * tile * 1.1f;
                    Primitives2D.Line(spriteBatch, outer, inner, faint, 3);
                }
                break;
            case 3: // Envy: two offset copies of one diagram.
                foreach (int side in new[] { -1, 1 })
                {
                    float size = tile * 2.5f;
                    var copyCenter = center + new Vector2(side * tile * 1.35f, 0);
                    var rect = new Rectangle((int)(copyCenter.X - size / 2f), (int)(copyCenter.Y - size / 2f), (int)size, (int)size);
                    Primitives2D.RectOutline(spriteBatch, rect, side > 0 ? bright : faint, 3);
                }
                break;
            case 4: // Gluttony: an open maw of broken rings.
                foreach (float radius in new[] { tile * 1.5f, tile * 2.5f, tile * 3.5f })
                {
                    var rect = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2), (int)(radius * 2));
                    Primitives2D.Arc(spriteBatch, rect, .35f, 2f * MathF.PI - .35f, bright, 3);
                }
                break;
            case 5: // Wrath: an uncompromising cross-lane reticle.
                Primitives2D.Line(spriteBatch, center - new Vector2(extent, 0), center + new Vector2(extent, 0), bright, 4);
                Primitives2D.Line(spriteBatch, center - new Vector2(0, extent), center + new Vector2(0, extent), bright, 4);
                break;
            default: // Sloth: a sagging, incomplete containment spiral.
                var points = new Vector2[48];
                for (int index = 0; index < 48; index++)
                {
                    float angle = index * .34f;
                    float radius = tile * (.35f + index * .075f);
                    points[index] = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * .72f);
                }
                Primitives2D.Polyline(spriteBatch, points, false, faint, 4);
                break;
        }

        // Slow rising spores make the field feel alive without hiding projectile lanes.
        int sporeCount = Config.FinalBoss ? 9 : 5;
        for (int index = 0; index < sporeCount; index++)
        {
            float angle = index * 2.399f + Age * .003f;
            float radius = tile * (1.2f + (index % 4) * 1.1f);
            float yOffset = (Age * .04f + index * 13) % tile;
            var point = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius - yOffset);
            Primitives2D.FillCircle(spriteBatch, point, 2 + index % 3, bright);
        }

        double sigilProgress = 1 - SigilTransitionTimer / SigilTransitionDuration;
        DrawSigil(spriteBatch, center, tile * (Config.FinalBoss ? 2.25f : 1.55f), Math.Max(0.0, sigilProgress), Age * .0008, 55, Phase);
        DrawPersistentTerrain(spriteBatch, camera, playerWorldPosition, screenShake);
    }

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var center = rect.Center.ToVector2();
        if (Dying)
        {
            BossVisuals.Disassemble(spriteBatch, center, Age, DeathProgress, Simulation.TileSize * 1.4f,
                new Color(232, 116, 34), new Color(154, 62, 210), 16);
            return;
        }

        float pulse = .5f + .5f * MathF.Sin(Age * .08f);
        float attack = VisualAttackTimer > 0 ? MathF.Sin(Math.Clamp(VisualAttackTimer / (Simulation.FrameRate * .58f), 0f, 1f) * MathF.PI) : 0f;
        float cubeSize = Simulation.TileSize * (1f + attack * .16f);
        float jitterX = MathF.Sin(Age * .17f) * 3.5f + MathF.Sin(Age * .071f) * 2f;
        float jitterY = MathF.Sin(Age * .133f + 1.2f) * 3f;
        var jittered = center + new Vector2(jitterX, jitterY);
        Color purple = new(157, 69, 214);
        Color orange = new(232, 116, 34);
        for (int ring = 3; ring >= 1; ring--)
        {
            float radius = cubeSize * (.72f + ring * .17f + pulse * .05f);
            Primitives2D.CircleOutline(spriteBatch, jittered, radius, purple * (.12f + ring * .07f), 3 + ring, 32);
        }
        int sparks = Config.FinalBoss ? 18 : 13;
        float spread = VisualSurvivalActive ? 1.65f : 1f;
        for (int index = 0; index < sparks; index++)
        {
            float angle = index * 2.399f + Age * (.018f + index % 3 * .004f);
            float radius = cubeSize * (.62f + (index % 5) * .15f) * spread;
            var point = jittered + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle * 1.17f) * radius * .68f);
            int sparkSize = 2 + index % 3;
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)point.X - sparkSize, (int)point.Y - sparkSize, sparkSize * 2, sparkSize * 2),
                index % 4 == 0 ? UiTheme.Cream : purple);
        }
        BossVisuals.Cube(spriteBatch, jittered, cubeSize, orange, purple, Age * .035f);
        var inner = new Rectangle((int)(jittered.X - cubeSize * .13f), (int)(jittered.Y - cubeSize * .13f), (int)(cubeSize * .26f), (int)(cubeSize * .26f));
        Primitives2D.FillRect(spriteBatch, inner, purple);
        Primitives2D.RectOutline(spriteBatch, inner, UiTheme.Cream, 2);

        if (ConsumedCrystalPulse > 0)
        {
            float radius = Size * (1.0f + (float)(1 - ConsumedCrystalPulse) * .65f);
            var pulseRect = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2), (int)(radius * 2));
            Primitives2D.EllipseOutline(spriteBatch, pulseRect, UiTheme.Cream, Math.Max(2, (int)(5 * ConsumedCrystalPulse)));
        }
        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .66f), (int)(Size * .92f), 6));
    }

    /// <summary>
    /// Ported from _draw_sigil, with a progressive per-stroke reveal
    /// (`stroke_budget`/`segment_budget`) preserved as a real gameplay-relevant
    /// visual. Python's supersample+`smoothscale` anti-aliasing is dropped, in
    /// line with this port's "no AA anywhere in Primitives2D" simplification --
    /// draws directly at final screen coordinates instead of an offscreen
    /// surface.
    /// </summary>
    protected string DrawSigil(SpriteBatch spriteBatch, Vector2 center, float radius, double progress = 1.0,
        double rotation = 0.0, int alpha = 255, int? phase = null)
    {
        if (SinConfig.SinSigils.Count == 0)
            return "";
        int phaseValue = phase ?? Phase;
        int index = Math.Clamp(phaseValue - 1, 0, SinConfig.SinSigils.Count - 1);
        var (name, strokes) = SinConfig.SinSigils[index];
        progress = Math.Clamp(progress, 0.0, 1.0);
        float cosAngle = MathF.Cos((float)rotation), sinAngle = MathF.Sin((float)rotation);
        int lineWidth = Math.Max(2, (int)(radius * .09f));
        int glowWidth = lineWidth + Math.Max(3, (int)(radius * .09f));
        double strokeBudget = progress * strokes.Length;
        byte a = (byte)Math.Clamp(alpha, 0, 255);
        var inkAlpha = new Color(UiTheme.Ink.R, UiTheme.Ink.G, UiTheme.Ink.B, a);
        var accentAlpha = new Color(PhaseAccent.R, PhaseAccent.G, PhaseAccent.B, a);
        var creamAlpha = new Color(UiTheme.Cream.R, UiTheme.Cream.G, UiTheme.Cream.B, a);

        for (int strokeIndex = 0; strokeIndex < strokes.Length; strokeIndex++)
        {
            var stroke = strokes[strokeIndex];
            double localProgress = Math.Clamp(strokeBudget - strokeIndex, 0.0, 1.0);
            if (localProgress <= 0 || stroke.Length < 2)
                continue;
            var transformed = new Vector2[stroke.Length];
            for (int point = 0; point < stroke.Length; point++)
            {
                float x = stroke[point].X * radius, y = stroke[point].Y * radius;
                transformed[point] = center + new Vector2(x * cosAngle - y * sinAngle, x * sinAngle + y * cosAngle);
            }
            var visible = new List<Vector2> { transformed[0] };
            double segmentBudget = localProgress * (transformed.Length - 1);
            for (int segment = 0; segment < transformed.Length - 1; segment++)
            {
                if (segmentBudget <= segment)
                    break;
                float fraction = (float)Math.Min(1.0, segmentBudget - segment);
                var start = transformed[segment];
                var end = transformed[segment + 1];
                visible.Add(start + (end - start) * fraction);
                if (fraction < 1)
                    break;
            }
            if (visible.Count > 1)
            {
                Primitives2D.Polyline(spriteBatch, visible, false, inkAlpha, glowWidth);
                Primitives2D.Polyline(spriteBatch, visible, false, accentAlpha, lineWidth);
                Primitives2D.Polyline(spriteBatch, visible, false, creamAlpha, Math.Max(1, lineWidth / 3));
                Primitives2D.FillCircle(spriteBatch, visible[^1], Math.Max(1, lineWidth / 2), creamAlpha);
            }
        }
        return name;
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (!Dying)
            DrawFieldDiagram(spriteBatch, camera, playerWorldPosition, screenShake);
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        if (Dying)
            return;
        if (ActTransitionTimer <= 0)
            return;

        var viewport = spriteBatch.GraphicsDevice.Viewport;
        double progress = 1 - ActTransitionTimer / ActTransitionDuration;
        float alpha = (float)Math.Min(1.0, Math.Min(progress * 5, (1 - progress) * 5)) * 185f;
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(0, (int)(viewport.Height * .3f), viewport.Width, (int)(viewport.Height * .4f)),
            UiTheme.Void * (alpha / 255f));
        UiTheme.DrawText(spriteBatch, ActTitle, 31, PhaseAccent, new Vector2(viewport.Width / 2f, viewport.Height * .43f), "center");
        UiTheme.DrawText(spriteBatch, $"{PhaseLabel} SPREADS", 13, UiTheme.Cream, new Vector2(viewport.Width / 2f, viewport.Height * .51f), "center");
        UiTheme.DrawText(spriteBatch, PhaseFlavor, 11, UiTheme.Lighten(PhaseAccent, 45), new Vector2(viewport.Width / 2f, viewport.Height * .56f), "center");
        string sigilName = DrawSigil(spriteBatch, new Vector2(viewport.Width / 2f, viewport.Height * .64f), 34,
            Math.Min(1.0, progress * 2.4), 0, 255, Phase);
        UiTheme.DrawText(spriteBatch, sigilName, 9, PhaseAccent, new Vector2(viewport.Width / 2f, viewport.Height * .70f), "center");
    }
}
