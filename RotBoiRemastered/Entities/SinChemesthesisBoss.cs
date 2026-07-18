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
/// `SinChemesthesisBoss`/`Kage`/`Rot` class attributes (`phaseFlavors`,
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
/// Rot). Ported from bossTypes.py's `SinChemesthesisBoss`.
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
    public double MaxStagger { get; } = 100.0;
    public double MinimumStaggerPerHit { get; } = 4.0;
    public double StaggerDuration { get; } = 2.5;
    public double StaggerRemaining { get; protected set; }
    public bool IsStaggered { get; protected set; }

    protected virtual double ConsumedCrystalPulse => 0.0;

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
        if (DebugPhaseLocked)
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

    protected void Shot(List<EnemyProjectile> sink, float direction, float speed, float damage, float scale = .25f,
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

    protected void Bomb(List<EnemyProjectile> sink, float targetX, float targetY, float damage, string suffix)
    {
        var center = Center();
        float size = Size * .34f;
        var bomb = new EnemyProjectile(center.X, center.Y, 0f, 0f, damage, size, color: PhaseAccent,
            shape: "bomb", path: "bomb", lifetime: 4.0f, target: new Vector2(targetX, targetY),
            owner: $"{Config.OwnerPrefix}_{suffix}", ignoreWalls: true)
        {
            FuseDuration = 2.6f,
            BlastRadius = Simulation.TileSize * 1.8f,
            BurstCount = 10,
        };
        sink.Add(bomb);
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
        if (PhaseProtectionTimer > 0 || ActTransitionTimer > 0)
            return new HitResult(false, false, 0, true);
        if (partId.StartsWith("crystal:"))
            return DamageCrystal(partId, amount);
        int previousHp = Hp;
        double multiplier = IsStaggered ? 1.25 : 1.0;
        var result = base.TakeDamage(amount * multiplier, partId, source);
        if (source == DamageSource.Direct && !IsStaggered)
        {
            Stagger = Math.Min(MaxStagger, Stagger + Math.Max(MinimumStaggerPerHit, amount * .012));
            if (Stagger >= MaxStagger)
            {
                IsStaggered = true;
                StaggerRemaining = StaggerDuration;
                TransitionCleanupRequested = true;
            }
        }
        if (!DebugPhaseLocked && Phase < Config.PhaseLabels.Count)
        {
            double threshold = MaxHp * (double)(Config.PhaseLabels.Count - Phase) / Config.PhaseLabels.Count;
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
        double dt = Seconds();
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
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
            FireSinPattern(effectivePlayerX, effectivePlayerY, context);
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

    private void DrawChemicalBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        float pulse = .5f + .5f * MathF.Sin(Age * .04f);
        int pipCount = Config.FinalBoss ? 7 : 4;
        float pipRadius = Size * .62f;
        for (int index = 0; index < pipCount; index++)
        {
            float angle = -MathF.PI / 2f + index * 2f * MathF.PI / pipCount;
            var point = new Vector2(rect.Center.X + MathF.Cos(angle) * pipRadius, rect.Center.Y + MathF.Sin(angle) * pipRadius);
            bool active = index < Phase;
            Color color = active ? PhaseAccent : UiTheme.Border;
            Primitives2D.FillCircle(spriteBatch, point, Math.Max(4, (int)(Size * .065f)), UiTheme.Ink);
            Primitives2D.FillCircle(spriteBatch, point, Math.Max(2, (int)(Size * (.034f + .008f * pulse))), color);
        }

        var vessel = rect;
        vessel.Inflate(-(int)(Size * .58f), -(int)(Size * .24f));
        var vesselOuter = vessel;
        vesselOuter.Inflate(6, 6);
        Primitives2D.FillRect(spriteBatch, vesselOuter, UiTheme.Ink);
        Primitives2D.FillRect(spriteBatch, vessel, UiTheme.Void);
        float fillHeight = vessel.Height * (float)Math.Min(1.0, (double)Phase / Math.Max(1, Config.PhaseLabels.Count));
        var fluid = new Rectangle(vessel.X, (int)(vessel.Bottom - fillHeight), vessel.Width, (int)fillHeight);
        Primitives2D.FillRect(spriteBatch, fluid, PhaseAccent);
        Primitives2D.Line(spriteBatch, new Vector2(vessel.X + vessel.Width * .28f, vessel.Y + 5),
            new Vector2(vessel.X + vessel.Width * .28f, vessel.Bottom - 5), UiTheme.Cream, 2);

        double transition = SigilTransitionTimer / SigilTransitionDuration;
        var rectCenter = new Vector2(rect.Center.X, rect.Center.Y);
        if (transition > 0 && PreviousSigilPhase != Phase)
        {
            DrawSigil(spriteBatch, rectCenter, Size * .34f, 1.0, -transition * Math.PI * .35, (int)(120 * transition), PreviousSigilPhase);
        }
        DrawSigil(spriteBatch, rectCenter, Size * .36f, Math.Max(0.05, 1 - transition), transition * Math.PI * .55, 255, Phase);

        if (ConsumedCrystalPulse > 0)
        {
            float radius = Size * (1.0f + (float)(1 - ConsumedCrystalPulse) * .65f);
            var pulseRect = new Rectangle((int)(rect.Center.X - radius), (int)(rect.Center.Y - radius), (int)(radius * 2), (int)(radius * 2));
            Primitives2D.EllipseOutline(spriteBatch, pulseRect, UiTheme.Cream, Math.Max(2, (int)(5 * ConsumedCrystalPulse)));
        }
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
        DrawFieldDiagram(spriteBatch, camera, playerWorldPosition, screenShake);
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        DrawChemicalBody(spriteBatch, camera, playerWorldPosition, screenShake);
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
