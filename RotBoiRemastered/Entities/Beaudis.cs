using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// The restrained midpoint echo fought in the ordinary world -- the run's
/// level-10 gate boss. Ported from bossTypes.py's Beaudis (the first of
/// fifteen boss classes in that ~4750-line file).
///
/// Cleanup vs. the Python original:
/// - `damagePhaseHistory`, `SURVIVAL_PHASES`, `finalFlavorItalic`,
///   `perfectStagger`, `staggerRecoveryRemaining`, `runeSilenceRemaining`,
///   `survivalPortals`, and `transitionRemaining` are all set in Python's
///   `__init__` but never read anywhere in Beaudis's own body (confirmed by
///   reading the full class) -- they're either vestigial (never used by
///   any boss), or fields Beaudis shares in *name* with Dissonance (which
///   does use them meaningfully) purely so Python's boss-agnostic HUD could
///   read them without an AttributeError. The C# HUD now consumes the smaller
///   common contract and encounter-specific state remains on Dissonance.
/// - `self.posX, self.posY = bG.world_to_screen(...)` assignments at the end
///   of every `updateEnemy` branch are dropped -- a same-frame cache for
///   Python's combined update-and-draw call, made unnecessary by this
///   port's Update/Draw split (Draw recomputes screen position itself).
/// - The death/stagger fade (`sprite.set_alpha(...)` on an offscreen
///   pygame.Surface) becomes each draw color pre-multiplied by the fade
///   factor (`color * fade`) instead -- MonoGame's `Color * float` already
///   scales RGBA uniformly under the default alpha blend state, so no
///   intermediate render target is needed for a single shared fade value.
/// - Italic/bold flavor-text styling is dropped, same documented gap as
///   `UiTheme.Font`'s italic/bold parameters (regular weight only).
/// </summary>
public sealed class Beaudis : Enemy
{
    public const string BossName = "BEAUDIS";
    public const string Subtitle = "THE ECHO THAT FOLLOWS";
    public const int MinimumDamagePhaseDeclarations = 2;
    public const int ActiveThreatSoftCap = 36;
    private const string FinalFlavor = "You can't escape me...";
    private const int PhaseCount = 5;
    private const double PhaseTimeLimit = 28.0;
    private const double EntranceDuration = 1.25;
    private const double StaggerDuration = 3.0;
    private const int SurvivalPhase = 3;

    private static readonly IReadOnlyDictionary<int, (string Label, string Flavor, Color Accent)> PhaseMetadata =
        new Dictionary<int, (string, string, Color)>
        {
            [1] = ("AWAKEN", "You hear it too.", UiTheme.Purple),
            [2] = ("ANSWER", "Stay a while.", UiTheme.Blue),
            [3] = ("ENDURE", "Run, while you still can.", UiTheme.Cream),
            [4] = ("PRESS", "The pattern remembers.", UiTheme.Gold),
            [5] = ("PERSIST", "This is not the end.", UiTheme.Red),
        };

    private readonly List<ProjectilePortal> _projectilePortals = new();

    private int _portalIndex;
    private double _attackCooldown = 1.25;
    private int _attackPattern;
    private int _phaseDeclarations;
    private double _phaseElapsed;
    private double _phaseProtectionTimer;
    private double _staggerRemaining;

    public int Phase { get; private set; } = 1;
    public string PhaseLabel { get; private set; }
    public string PhaseFlavor { get; private set; }
    public Color PhaseAccent { get; private set; }
    public double PhaseAnnouncementTimer { get; private set; } = 2.4;
    public bool PhaseForcedByTimer { get; private set; }
    public bool DebugPhaseLocked { get; set; }
    public int PhaseDeclarations => _phaseDeclarations;
    public int PatternRotation => _attackPattern;

    /// <summary>Settable so debug controls/tests can skip the entrance cinematic, matching Python tests setting `entranceRemaining` directly.</summary>
    public double EntranceRemaining { get; set; } = EntranceDuration;

    public bool Dying { get; private set; }
    public double DeathDuration { get; } = 3.0;
    public double DeathRemaining { get; private set; }

    public bool IsStaggered { get; private set; }
    /// <summary>Settable: the boss-debug "F" hotkey (HandleBossDebugControls) sets this directly, same as Python.</summary>
    public double Stagger { get; set; }
    public double MaxStagger { get; } = 90.0;
    public double MinimumStaggerPerHit { get; } = 4.0;

    public bool SurvivalActive { get; private set; }
    public double SurvivalDuration { get; } = 14.0;
    public double SurvivalRemaining { get; private set; }
    public double SurvivalCooldown { get; private set; } = .7;

    public IReadOnlyList<ProjectilePortal> ProjectilePortals => _projectilePortals;

    /// <summary>Ported from challenge_results()'s single-key dict -- no known caller yet (defined on several boss types but never invoked by production game code, only by that boss's own tests), kept for fidelity.</summary>
    public bool MidpointSurvived => Dying || Hp <= 0;

    public Beaudis(float worldX, float worldY, float awarenessRange, Random? rng = null)
        : base(worldX, worldY, .38f, Simulation.TileSize * 1.55f, UiTheme.Purple, 220, 50000, 240, 3.2, awarenessRange, "beaudis")
    {
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[1];
    }

    private static double Seconds() => Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);

    private Vector2 Center() => new(WorldX + Size / 2f, WorldY + Size / 2f);

    private void SetPhase(int phase)
    {
        phase = Math.Clamp(phase, 1, PhaseCount);
        if (phase == Phase)
            return;
        Phase = phase;
        _phaseElapsed = 0.0;
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[phase];
        PhaseAnnouncementTimer = 2.4;
        _phaseProtectionTimer = .55;
        TransitionCleanupRequested = true;
        _attackCooldown = 1.0;
        _phaseDeclarations = 0;
        Stagger = 0.0;
        IsStaggered = false;
        _staggerRemaining = 0.0;
        PhaseForcedByTimer = false;
        SurvivalActive = phase == SurvivalPhase;
        if (SurvivalActive)
        {
            Hp = Math.Max(1, Hp);
            SurvivalRemaining = SurvivalDuration;
            SurvivalCooldown = .75;
            DeployFinalePortals();
        }
        else
        {
            ClearPortals();
        }
    }

    /// <summary>Dev/testing hotkey support. Ported from debug_set_phase().</summary>
    public void DebugSetPhase(int phase)
    {
        phase = Math.Clamp(phase, 1, PhaseCount);
        if (phase == Phase)
            Phase = 0;
        SetPhase(phase);
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (Dying || SurvivalActive || _phaseProtectionTimer > 0)
            return new HitResult(false, false, 0, true);
        double multiplier = IsStaggered ? 1.25 : 1.0;
        int applied = (int)Math.Round(amount * multiplier);
        Hp -= applied;
        if (source == DamageSource.Direct)
            Stagger = Math.Min(MaxStagger, Stagger + Math.Max(MinimumStaggerPerHit, amount * .014));
        if (Stagger >= MaxStagger && !IsStaggered)
        {
            IsStaggered = true;
            _staggerRemaining = StaggerDuration;
            TransitionCleanupRequested = true;
        }
        if (!DebugPhaseLocked)
        {
            int floor = Phase switch
            {
                1 => (int)Math.Round(MaxHp * .75),
                2 => (int)Math.Round(MaxHp * .50),
                4 => (int)Math.Round(MaxHp * .25),
                _ => 1,
            };
            if (Hp <= floor)
            {
                Hp = floor;
                if (_phaseDeclarations >= MinimumDamagePhaseDeclarations)
                {
                    if (Phase == 5)
                        BeginFade();
                    else
                        SetPhase(Phase + 1);
                }
            }
        }
        Hp = Dying ? Math.Max(1, Hp) : Math.Max(0, Hp);
        // Dying is a protected three-second spectacle, not an immediate kill.
        // GameSession removes HitResult.Killed enemies in the damage pass.
        return new HitResult(true, false, applied);
    }

    private void ClearPortals()
    {
        foreach (var portal in _projectilePortals)
            portal.RemFlag = true;
        _projectilePortals.Clear();
    }

    private void DeployFinalePortals()
    {
        ClearPortals();
        var center = Center();
        for (int index = 0; index < 4; index++)
        {
            _projectilePortals.Add(new ProjectilePortal(
                center, Simulation.TileSize * 3.8f, index * MathF.PI / 2f,
                angularSpeed: .18f, fireInterval: 999f, pelletCount: 2, spread: .22f,
                owner: "beaudis_finale", color: index % 2 == 0 ? UiTheme.Purple : UiTheme.Blue));
        }
    }

    private void FireProjectile(List<EnemyProjectile> sink, float direction, float speed = .68f, float damage = 1.0f,
        Color? color = null, string owner = "beaudis_shot", Vector2? origin = null, string path = "linear")
    {
        var center = origin ?? Center();
        float size = Simulation.TileSize * .34f;
        sink.Add(new EnemyProjectile(
            center.X - size / 2f, center.Y - size / 2f, direction, speed, damage, size,
            travelRange: Simulation.TileSize * 30f, color: color ?? PhaseAccent, shape: "diamond",
            path: path, amplitude: path == "sine" ? Simulation.TileSize * .22f : 0,
            frequency: .04f, owner: owner, ignoreWalls: true));
    }

    private void FireFan(float playerX, float playerY, List<EnemyProjectile> sink, int count, float spread, float speed = .68f)
    {
        var center = Center();
        float baseDirection = MathF.Atan2(playerY - center.Y, playerX - center.X);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * spread / Math.Max(1, count - 1);
            FireProjectile(sink, baseDirection + offset, speed);
        }
    }

    private void FireRadial(List<EnemyProjectile> sink, int count = 6, float speed = .62f)
    {
        float offset = _attackPattern * MathF.PI / Math.Max(1, count);
        for (int index = 0; index < count; index++)
            FireProjectile(sink, offset + index * 2f * MathF.PI / count, speed, .9f, UiTheme.Gold, "beaudis_pulse");
    }

    private void FireRadialWithAimedGap(float playerX, float playerY, List<EnemyProjectile> sink, int count, float speed)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        int gap = (int)MathF.Round(((aimed % MathF.Tau + MathF.Tau) % MathF.Tau) / MathF.Tau * count) % count;
        float offset = _attackPattern * .17f;
        for (int index = 0; index < count; index++)
        {
            if (index == gap || index == (gap + 1) % count)
                continue;
            FireProjectile(sink, index * MathF.Tau / count + offset, speed, 1.0f, UiTheme.Gold, "beaudis_press");
        }
    }

    private int ActiveThreats(List<EnemyProjectile> sink) =>
        sink.Count(projectile => !projectile.RemFlag && projectile.Owner?.StartsWith("beaudis") == true);

    private void Move(float playerX, float playerY, Battleground battleground)
    {
        var center = Center();
        float dx = playerX - center.X, dy = playerY - center.Y;
        float distance = Math.Max(1.0f, MathF.Sqrt(dx * dx + dy * dy));
        float moveX, moveY;
        if (distance > Simulation.TileSize * 6.5f)
        {
            moveX = dx / distance;
            moveY = dy / distance;
        }
        else if (distance < Simulation.TileSize * 3.5f)
        {
            moveX = -dx / distance * .7f;
            moveY = -dy / distance * .7f;
        }
        else
        {
            float direction = Phase % 2 != 0 ? 1f : -1f;
            moveX = -dy / distance * direction * .45f;
            moveY = dx / distance * direction * .45f;
        }
        float step = Speed * (float)Simulation.GetFrameScale();
        TryAxisMove(moveX * step, "x", battleground);
        TryAxisMove(moveY * step, "y", battleground);
    }

    private void UpdateDamagePhase(float playerX, float playerY, List<EnemyProjectile> sink, double dt, Battleground battleground)
    {
        Move(playerX, playerY, battleground);
        _attackCooldown -= dt;
        if (_attackCooldown > 0)
            return;
        if (ActiveThreats(sink) >= ActiveThreatSoftCap)
        {
            _attackCooldown = .3;
            return;
        }
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        switch (Phase)
        {
            case 1:
                FireProjectile(sink, aimed, .72f, 1.0f, UiTheme.Purple, "beaudis_call");
                FireProjectile(sink, aimed - .18f, .58f, .9f, UiTheme.Blue, "beaudis_call_echo", path: "sine");
                FireProjectile(sink, aimed + .18f, .58f, .9f, UiTheme.Blue, "beaudis_call_echo", path: "sine");
                _attackCooldown = 1.65;
                break;
            case 2:
            {
                float side = Simulation.TileSize * 3.2f;
                var left = center + new Vector2(-MathF.Sin(aimed), MathF.Cos(aimed)) * side;
                var right = center - new Vector2(-MathF.Sin(aimed), MathF.Cos(aimed)) * side;
                float leftAim = MathF.Atan2(playerY - left.Y, playerX - left.X);
                float rightAim = MathF.Atan2(playerY - right.Y, playerX - right.X);
                for (int index = -1; index <= 1; index++)
                {
                    FireProjectile(sink, leftAim + index * .18f, .68f, .95f, UiTheme.Blue,
                        "beaudis_answer_left", left);
                    FireProjectile(sink, rightAim + index * .18f, .68f, .95f, UiTheme.Purple,
                        "beaudis_answer_right", right);
                }
                _attackCooldown = 1.9;
                break;
            }
            case 4:
                FireRadialWithAimedGap(playerX, playerY, sink, 10, .67f);
                FireFan(playerX, playerY, sink, 3, .34f, .76f);
                _attackCooldown = 2.15;
                break;
            default:
                FireFan(playerX, playerY, sink, 5, .62f, .78f);
                FireRadial(sink, 6, .70f);
                _attackCooldown = 1.85;
                break;
        }
        _attackPattern += 1;
        _phaseDeclarations += 1;
    }

    private void UpdateSurvival(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        SurvivalRemaining = Math.Max(0.0, SurvivalRemaining - dt);
        var center = Center();
        foreach (var portal in _projectilePortals)
        {
            portal.OrbitCenter = center;
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
            portal.UpdateBursts(sink, (float)dt);
        }
        SurvivalCooldown -= dt;
        if (SurvivalCooldown <= 0 && _projectilePortals.Count > 0)
        {
            var portal = _projectilePortals[_portalIndex % _projectilePortals.Count];
            portal.FireToward(sink, new Vector2(playerX, playerY), 2, .22f, .72f, 1.0f, PhaseAccent, "survival");
            _portalIndex += 1;
            SurvivalCooldown = .85;
        }
        if (SurvivalRemaining <= 0)
        {
            SetPhase(4);
        }
    }

    private void BeginFade()
    {
        if (Dying)
            return;
        SurvivalActive = false;
        Dying = true;
        DeathRemaining = DeathDuration;
        PhaseFlavor = FinalFlavor;
        PhaseAnnouncementTimer = DeathDuration;
        TransitionCleanupRequested = true;
        ClearPortals();
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        AdvanceAge();
        _phaseElapsed += dt;
        PhaseAnnouncementTimer = Math.Max(0.0, PhaseAnnouncementTimer - dt);
        _phaseProtectionTimer = Math.Max(0.0, _phaseProtectionTimer - dt);
        if (Dying)
        {
            DeathRemaining = Math.Max(0.0, DeathRemaining - dt);
            if (DeathRemaining <= 0)
                Hp = 0;
            return;
        }
        if (EntranceRemaining > 0)
        {
            EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
            return;
        }
        if (IsStaggered)
        {
            _staggerRemaining = Math.Max(0.0, _staggerRemaining - dt);
            if (_staggerRemaining <= 0)
            {
                IsStaggered = false;
                Stagger = 0.0;
            }
            return;
        }
        if (SurvivalActive)
        {
            UpdateSurvival(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink, dt);
        }
        else
        {
            if (!DebugPhaseLocked && _phaseElapsed >= PhaseTimeLimit &&
                Phase is 1 or 2 or 4 or 5 &&
                _phaseDeclarations >= MinimumDamagePhaseDeclarations)
            {
                _phaseElapsed = 0;
                _attackCooldown = 0;
                PhaseForcedByTimer = true;
            }
            UpdateDamagePhase(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink, dt, context.Battleground);
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        foreach (var portal in _projectilePortals)
            portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);

        float fade = Dying ? (float)(DeathRemaining / DeathDuration) : 1.0f;
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        Color color = (IsStaggered ? UiTheme.Cream : PhaseAccent) * fade;

        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 5, rect.Y + 6, rect.Width, rect.Height), UiTheme.Shadow * fade);
        Primitives2D.FillRect(spriteBatch, rect, color);
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Ink * fade, Math.Max(3, (int)(Size * .06f)));

        var inner = rect;
        inner.Inflate(-(int)(Size * .42f), -(int)(Size * .42f));
        Primitives2D.FillRect(spriteBatch, inner, UiTheme.Void * fade);
        Primitives2D.RectOutline(spriteBatch, inner, color, Math.Max(4, (int)(Size * .045f)));

        int pipSize = Math.Max(4, (int)(Size * .07f));
        for (int index = 0; index < Math.Min(Phase, 4); index++)
        {
            var pipRect = new Rectangle(rect.X + 8 + index * (pipSize + 3), rect.Bottom - pipSize - 8, pipSize, pipSize);
            Primitives2D.FillRect(spriteBatch, pipRect, UiTheme.Cream * fade);
        }

        if (Dying)
        {
            UiTheme.DrawText(spriteBatch, FinalFlavor, Math.Max(12, Size * .17), UiTheme.Cream * fade,
                new Vector2(rect.Center.X, screenPosition.Y - 12), "midbottom");
        }
        else if (PhaseAnnouncementTimer > 0)
        {
            UiTheme.DrawText(spriteBatch, $"PHASE {Phase} // {PhaseLabel}", Math.Max(11, Size * .13), PhaseAccent,
                new Vector2(rect.Center.X, screenPosition.Y - 10), "midbottom", bold: true);
        }
    }
}
