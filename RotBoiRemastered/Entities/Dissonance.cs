using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// The oldest ancient core and the run's level-20 Sound final boss -- nine phases across three "acts," each
/// with its own attack pattern and locomotion identity, tied to nine Elder
/// Futhark runes. Ported from bossTypes.py's Dissonance (~1780 lines, by far
/// the most complex single class in the codebase -- see Entities/README.md
/// for how this pass was scoped against the rest of bossTypes.py).
///
/// Design notes vs. the Python original:
/// - **Nearly every field is a public settable property**, unlike
///   <see cref="Beaudis"/>'s curated surface. This isn't a style
///   preference: bossTypes.py's own test suite (`tests/test_beaudis_boss.py`,
///   almost entirely about Dissonance -- 40 tests) drives this boss almost
///   entirely through direct attribute mutation (`boss.patternCooldown = 0`,
///   `boss.stagger = boss.maxStagger - 5`, `boss.debug_set_phase(8)`, ...) to
///   reach specific attack windows without waiting out real cooldowns. This
///   port's own test suite needs the exact same access, so hiding this
///   state behind a small curated surface (like Beaudis) would just force
///   every test to wait out multi-second cooldowns via thousands of Update
///   calls -- impractical. `Phase` stays a private-set (via
///   <see cref="SetPhase"/>/<see cref="DebugSetPhase"/> only, matching real
///   game rules); <see cref="CubeGeometry"/> takes age/phase as explicit
///   parameters instead (see its own doc comment) rather than requiring
///   `Age` (privately set on the base `Enemy` class) to be mutable too.
/// - **`_arena_center()` becomes a cached `Vector2 ArenaCenter` field**,
///   computed once from an explicit `Battleground` constructor parameter.
///   Python read a `background.py` global (`bG.currRoomRects`) from *both*
///   Update-side methods (which already receive world context) and Draw
///   (which doesn't) -- since the arena doesn't move mid-fight, computing it
///   once at construction avoids needing to thread a `Battleground` through
///   every draw helper.
/// - **Screen shake is a computed value, not a global write.** Python wrote
///   `vH.screenShakeX/Y` directly inside `_update_visuals`. Here,
///   `UpdateVisuals` only decays `ShakeStrength`; `ComputeScreenShake(shakeScale)`
///   is a pure function GameSession calls once per frame (when this boss is
///   `RunState.ActiveBoss`) and assigns to its own `ScreenShake`, matching
///   this whole port's "explicit parameter over hidden global" convention.
/// - **Dead-in-this-class fields dropped** (confirmed by grep: declared in
///   `__init__`, never read anywhere in Dissonance's body): `portalBreakStagger`,
///   `survivalFormationCycle`, `_arenaMaskCache`/`_arenaMaskCacheKey` (the
///   arena mask is recomputed from scratch every frame regardless -- the
///   cache fields were never actually consulted). `_fire_mines` (defined,
///   never called -- only `_fire_portal_mines` is used) is dropped too.
/// - **Full-screen alpha veils** (`_draw_perfect_break`/`_draw_act_transition`)
///   use `color * (alpha/255f)` against the current viewport rect instead of
///   an offscreen `pygame.Surface` blit -- same technique as `Beaudis`'s
///   death fade, extended to a full-screen rect. Screen size comes from
///   `spriteBatch.GraphicsDevice.Viewport` (no screen-size parameter existed
///   on `Draw` before this).
/// - Italic/bold flavor text is dropped, same documented `UiTheme.Font` gap
///   used everywhere else in this port.
/// - `route_player_bullet`'s player-bullet mutation (`bullet.worldX/worldY/direc/damage/portalCooldown`)
///   is encapsulated as `Bullet.RouteThroughPortal` (see `Bullet.cs`) instead
///   of exposing raw setters for fields nothing else in the port needs to
///   write externally.
/// </summary>
public sealed class Dissonance : Enemy
{
    public const string BossName = "DISSONANCE";
    public const string Subtitle = "KEEPER OF THE FIRST CHORD";
    public const int OrbitingCubeCount = 4;
    public const int MinimumDamagePhaseDeclarations = 2;
    public const int ActiveThreatSoftCap = 132;
    public const int MaximumJeraChordRings = 9;

    public static readonly IReadOnlyDictionary<int, (string Name, Vector2[][] Strokes)> PhaseRunes =
        new Dictionary<int, (string, Vector2[][])>
        {
            [1] = ("OTHALA", new[]
            {
                new[] { new Vector2(-.34f, -.12f), new Vector2(0, -.5f), new Vector2(.34f, -.12f), new Vector2(0, .28f), new Vector2(-.34f, -.12f) },
                new[] { new Vector2(0, .28f), new Vector2(-.38f, .52f) },
                new[] { new Vector2(0, .28f), new Vector2(.38f, .52f) },
            }),
            [2] = ("RAIDHO", new[]
            {
                new[] { new Vector2(-.35f, .52f), new Vector2(-.35f, -.52f), new Vector2(.18f, -.52f), new Vector2(.38f, -.3f), new Vector2(.18f, -.08f), new Vector2(-.35f, -.08f) },
                new[] { new Vector2(-.02f, -.08f), new Vector2(.4f, .52f) },
            }),
            [3] = ("KENAZ", new[]
            {
                new[] { new Vector2(.35f, -.5f), new Vector2(-.35f, 0), new Vector2(.35f, .5f) },
            }),
            [4] = ("HAGALAZ", new[]
            {
                new[] { new Vector2(-.38f, -.52f), new Vector2(-.38f, .52f) },
                new[] { new Vector2(.38f, -.52f), new Vector2(.38f, .52f) },
                new[] { new Vector2(-.38f, .28f), new Vector2(.38f, -.28f) },
            }),
            [5] = ("EIHWAZ", new[]
            {
                new[] { new Vector2(-.28f, -.52f), new Vector2(.28f, -.28f), new Vector2(-.28f, .28f), new Vector2(.28f, .52f) },
            }),
            [6] = ("SOWILO", new[]
            {
                new[] { new Vector2(.3f, -.52f), new Vector2(-.24f, -.18f), new Vector2(.22f, .05f), new Vector2(-.3f, .52f) },
            }),
            [7] = ("TIWAZ", new[]
            {
                new[] { new Vector2(0, .52f), new Vector2(0, -.52f) },
                new[] { new Vector2(-.36f, -.18f), new Vector2(0, -.52f), new Vector2(.36f, -.18f) },
            }),
            [8] = ("DAGAZ", new[]
            {
                new[] { new Vector2(-.4f, -.5f), new Vector2(.4f, .5f), new Vector2(.4f, -.5f), new Vector2(-.4f, .5f), new Vector2(-.4f, -.5f) },
            }),
            [9] = ("JERA", new[]
            {
                new[] { new Vector2(-.42f, -.48f), new Vector2(-.04f, -.48f), new Vector2(.24f, -.22f) },
                new[] { new Vector2(.42f, .48f), new Vector2(.04f, .48f), new Vector2(-.24f, .22f) },
            }),
        };

    private static readonly IReadOnlySet<int> SurvivalPhaseSet = new HashSet<int> { 3, 6, 9 };
    private static readonly IReadOnlySet<int> DamagePhaseSet = new HashSet<int> { 1, 2, 4, 5, 7, 8 };

    public static readonly IReadOnlyDictionary<int, string> PhaseMovement = new Dictionary<int, string>
    {
        [1] = "hearth_tornado", [2] = "road_anchor", [3] = "torch_tornado",
        [4] = "hail_chase", [5] = "yew_anchor", [6] = "sun_revolution",
        [7] = "spear_intercept", [8] = "day_anchor", [9] = "harvest_chase",
    };

    private static readonly IReadOnlyDictionary<int, (string Label, string Flavor, Color Accent)> PhaseMetadata =
        new Dictionary<int, (string, string, Color)>
        {
            [1] = ("ANCESTRAL HEARTH", "Othala recalls the first gathering: a sound with purpose.", UiTheme.Purple),
            [2] = ("PROCESSIONAL ROAD", "Raidho conducts every voice along a deliberate road.", UiTheme.Cream),
            [3] = ("KENAZ REFRAIN", "Keep the ancient torch audible beneath the gathering noise.", UiTheme.Gold),
            [4] = ("HAGALAZ CACOPHONY", "The present fractures meaning into a thousand empty echoes.", UiTheme.Red),
            [5] = ("YEW OVERTONE", "Eihwaz preserves one clear interval between its branches.", UiTheme.Gold),
            [6] = ("SOWILO RESONANCE", "The first chord passes onward; its silence passes with it.", UiTheme.Cream),
            [7] = ("TIWAZ DEFENSE", "A noble spear bars the road to the power humanity squandered.", UiTheme.Blue),
            [8] = ("MEANINGLESS DRONE", "Dagaz repeats the present until imitation consumes intent.", UiTheme.Purple),
            [9] = ("JERA LAST CHORD", "Remember every phrase. Prove that meaning can still survive.", UiTheme.Red),
        };

    public sealed class VisualParticle
    {
        public float X, Y, Vx, Vy, Life;
        public int Size;
        public Color Color;
    }

    public sealed class MotionTrailGhost
    {
        public float X, Y, Life;
        public int Phase;
        public Color Accent;
    }

    private sealed class BossBurst
    {
        public double Timer;
        public float TargetX, TargetY;
        public int Count;
        public float SpeedScale;
    }

    private sealed class RelayPending
    {
        public double Timer;
        public ProjectilePortal Receiver = null!;
        public Vector2 Continuation;
    }

    private readonly Random _rng;
    public Vector2 ArenaCenter { get; }
    public float ArenaRadius { get; } = Simulation.TileSize * (32.0f / 3.0f);
    public float ArenaFormationScale { get; } = 4.0f / 3.0f;

    public int Phase { get; private set; } = 1;
    private readonly List<int> _damagePhaseHistory = new() { 1 };
    public int NextSurvivalPhase { get; set; } = 3;
    public Color PhaseAccent { get; private set; } = UiTheme.Purple;
    public string PhaseLabel { get; private set; } = "ANCESTRAL HEARTH";
    public string PhaseFlavor { get; private set; } = "Othala recalls the first gathering: a sound with purpose.";
    public double PhaseAnnouncementTimer { get; set; } = 3.2;
    public double PhaseProtectionTimer { get; set; }
    public double PhaseElapsed { get; set; }
    public double PhaseTimeLimit { get; } = 36.0;
    public bool PhaseForcedByTimer { get; private set; }
    public int PhaseDeclarations { get; private set; }
    private double _declarationCooldown;

    public double Stagger { get; set; }
    public double MaxStagger { get; } = 360.0;
    public double StaggerPerDamage { get; } = .02;
    public double MinimumStaggerPerHit { get; } = 6.0;
    public double StaggerDecayDelay { get; } = 2.0;
    public double StaggerDecayTimer { get; set; }
    public double StaggerDecayPerSecond { get; } = 16.0;
    public double StaggerDuration { get; } = 5.0;
    public double StaggerRemaining { get; set; }
    public bool IsStaggered { get; set; }
    public double RuneDisruption { get; set; }
    public double RuneDisruptionNeeded { get; } = 18.0;
    public double RuneSilenceRemaining { get; set; }
    public bool PerfectStagger { get; set; }
    public double Fracture { get; set; }
    public double MaxFracture { get; } = 20.0;
    public double StaggerRecoveryRemaining { get; set; }

    public double MineCooldown { get; set; } = 1.6;
    public double PatternCooldown { get; set; } = .7;
    public double RadialCooldown { get; set; } = .4;
    public double AimedCooldown { get; set; } = 1.4;
    public double JumpCooldown { get; set; } = 5.5;
    public double JumpWindup { get; set; }
    public double JumpRecovery { get; set; }
    public double FieldShotCooldown { get; set; } = .8;
    public bool FieldDeployed { get; set; }
    public List<EnemyProjectile> FieldProjectiles { get; } = new();

    public List<ProjectilePortal> ProjectilePortals { get; } = new();
    public List<ProjectilePortal> SurvivalPortals { get; } = new();
    public double RelayCooldown { get; set; } = .6;
    private RelayPending? _relayPending;
    public int RelayIndex { get; set; }
    public double CarouselCooldown { get; set; } = .45;
    public int CarouselIndex { get; set; }
    public double MirrorCooldown { get; set; } = 1.1;
    public double HorizonCooldown { get; set; } = .4;
    public double LastWordCooldown { get; set; } = .3;
    public double CallbackCooldown { get; set; } = 2.0;
    public int CallbackIndex { get; set; }
    public double RuneCannonCooldown { get; set; } = 5.0;
    public double RuneCannonCharge { get; set; }
    public int? RuneCannonReceiver { get; set; }

    public int PortalsBroken { get; private set; }
    public int RunesInterrupted { get; private set; }
    public int PerfectStaggers { get; private set; }
    public bool StaggerEverDecayed { get; private set; }
    private bool _runeWasInterrupted;

    public List<VisualParticle> VisualParticles { get; } = new();
    public List<MotionTrailGhost> MotionTrail { get; } = new();
    private double _ambientParticleCooldown;
    private double _motionTrailCooldown;

    public double ActTransitionTimer { get; set; } = 2.2;
    public string ActTitle { get; set; } = "ACT I // THE FIRST CHORD";
    public double HitFlash { get; set; }
    public double PerfectBreakFlash { get; set; }
    public double ShakeStrength { get; set; }

    public double EntranceRemaining { get; set; } = 3.0;
    public double EntranceDuration { get; } = 3.0;
    public bool Dying { get; private set; }
    public double DeathRemaining { get; set; }
    public double DeathDuration { get; } = 10.0;
    public double DeathBurstDuration { get; } = 10.0;
    public bool DebugPhaseLocked { get; set; }

    public double SurvivalDuration { get; } = 40.0;
    public double SurvivalRemaining { get; set; }
    public bool SurvivalActive { get; private set; }
    public double SurvivalCooldown { get; set; } = .2;
    public int JeraChordRingCount => Phase == 9 && SurvivalActive
        ? Math.Clamp(1 + (int)((1.0 - SurvivalRemaining / SurvivalDuration) * MaximumJeraChordRings),
            1, MaximumJeraChordRings)
        : 0;

    public double TransitionDuration { get; } = 5.0;
    public double TransitionRemaining { get; set; }
    private (float X, float Y)? _transitionStart;
    public (float X, float Y)? TransitionTarget { get; private set; }
    public bool CinematicTransitionsEnabled { get; set; } = true;
    public double SpecialAttackCooldown { get; set; } = 2.4;
    private readonly List<BossBurst> _bossBurstQueue = new();

    public double MirrorJumpDuration { get; } = .48;
    public double MirrorJumpRemaining { get; set; }
    private (float X, float Y)? _mirrorJumpStart;
    private (float X, float Y)? _mirrorJumpTarget;
    private Vector2? _mirrorJumpEchoOrigin;

    public Dissonance(float worldX, float worldY, float awarenessRange, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, .72f, Simulation.TileSize * 1.9f, UiTheme.Purple, 550, 150000, 900, 5, awarenessRange, "dissonance")
    {
        _rng = rng ?? Random.Shared;
        ArenaCenter = new Vector2(battleground.Width * Simulation.TileSize / 2f, battleground.Height * Simulation.TileSize / 2f);
        DeployPhaseOnePortals();
        foreach (var portal in ProjectilePortals)
        {
            portal.ResetForPhase(PhaseRunes[Phase].Strokes);
            portal.HitsToDisable = 15;
        }
    }

    private static double Seconds() => Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);

    private Vector2 Center() => new(WorldX + Size / 2f, WorldY + Size / 2f);

    private void BurstParticles(float worldX, float worldY, Color color, int count, float speed = 2.0f)
    {
        for (int index = 0; index < count; index++)
        {
            float angle = 2f * MathF.PI * index / Math.Max(1, count) + (float)(_rng.NextDouble() * .28 - .14);
            float velocity = speed * (float)(.55 + _rng.NextDouble() * .6);
            int[] sizes = { 3, 4, 6, 8 };
            VisualParticles.Add(new VisualParticle
            {
                X = worldX, Y = worldY, Vx = MathF.Cos(angle) * velocity, Vy = MathF.Sin(angle) * velocity,
                Life = (float)(.35 + _rng.NextDouble() * .5), Size = sizes[_rng.Next(sizes.Length)], Color = color,
            });
        }
    }

    private void UpdateVisuals(double dt)
    {
        HitFlash = Math.Max(0.0, HitFlash - dt);
        PerfectBreakFlash = Math.Max(0.0, PerfectBreakFlash - dt);
        ActTransitionTimer = Math.Max(0.0, ActTransitionTimer - dt);
        _ambientParticleCooldown -= dt;
        _motionTrailCooldown -= dt;
        ShakeStrength = Math.Max(0.0, ShakeStrength - 16 * dt);
        if (_ambientParticleCooldown <= 0)
        {
            var center = Center();
            float angle = Age * .013f + (float)(_rng.NextDouble() - .5);
            int[] sizes = { 3, 4, 5 };
            VisualParticles.Add(new VisualParticle
            {
                X = center.X + MathF.Cos(angle) * Size * .7f, Y = center.Y + MathF.Sin(angle) * Size * .45f,
                Vx = -MathF.Cos(angle) * .25f, Vy = -MathF.Sin(angle) * .25f,
                Life = .8f, Size = sizes[_rng.Next(sizes.Length)], Color = PhaseAccent,
            });
            _ambientParticleCooldown = .08;
        }
        if (_motionTrailCooldown <= 0)
        {
            var center = Center();
            MotionTrail.Add(new MotionTrailGhost { X = center.X, Y = center.Y, Life = .52f, Phase = Phase, Accent = PhaseAccent });
            _motionTrailCooldown = .045;
        }
        float frameScale = (float)Simulation.GetFrameScale();
        foreach (var particle in VisualParticles)
        {
            particle.X += particle.Vx * frameScale;
            particle.Y += particle.Vy * frameScale;
            particle.Vy += .16f * (float)dt;
            particle.Life -= (float)dt;
        }
        VisualParticles.RemoveAll(p => p.Life <= 0);
        foreach (var ghost in MotionTrail)
            ghost.Life -= (float)dt;
        MotionTrail.RemoveAll(g => g.Life <= 0);
    }

    /// <summary>Pure function GameSession calls once per frame (when this boss is RunState.ActiveBoss) instead of this class writing a screen-shake global directly. See class doc comment.</summary>
    public Vector2 ComputeScreenShake(double shakeScale) => new(
        (float)(Math.Sin(Age * .73) * ShakeStrength * shakeScale),
        (float)(Math.Cos(Age * .61) * ShakeStrength * .65 * shakeScale));

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (Dying)
            return new HitResult(false, false, 0, true);
        if (TransitionRemaining > 0 || PhaseProtectionTimer > 0)
            return new HitResult(false, false, 0, true);
        if (SurvivalActive)
            return new HitResult(false, false, 0, true);
        if (partId.StartsWith("portal:"))
        {
            if (!int.TryParse(partId["portal:".Length..], out int index))
                return new HitResult(false, false, 0, true);
            if (index >= 0 && index < ProjectilePortals.Count)
            {
                var portal = ProjectilePortals[index];
                bool broken = portal.TakeDamage((float)amount);
                if (broken)
                {
                    PortalsBroken += 1;
                    Stagger = Math.Min(MaxStagger, Stagger + MaxStagger / 3.0);
                    BurstParticles(portal.WorldX + portal.Size / 2f, portal.WorldY + portal.Size / 2f, portal.Color, 18, 2.4f);
                    if (Stagger >= MaxStagger)
                        TriggerStagger();
                }
                return new HitResult(true, false, amount);
            }
            return new HitResult(false, false, 0, true);
        }
        if (IsStaggered)
        {
            double multiplier = 1.35 + Fracture * .02;
            int applied = (int)Math.Round(amount * multiplier);
            Hp -= applied;
            if (source == DamageSource.Direct)
                Fracture = Math.Min(MaxFracture, Fracture + amount * .0035);
            HitFlash = .12;
            var center = Center();
            BurstParticles(center.X, center.Y, UiTheme.Cream, 4, 1.0f);
            EnforceDamageFloor();
            return new HitResult(true, false, applied);
        }
        int normalApplied = (int)Math.Round(amount * .45);
        Hp -= normalApplied;
        HitFlash = .08;
        double gained = source == DamageSource.Direct ? Math.Max(MinimumStaggerPerHit, amount * StaggerPerDamage) : 0;
        if (source == DamageSource.Direct)
            Stagger = Math.Min(MaxStagger, Stagger + gained);
        if (source == DamageSource.Direct && Phase > 1 && PhaseElapsed <= .75)
        {
            RuneDisruption += gained;
            if (RuneDisruption >= RuneDisruptionNeeded)
            {
                RuneSilenceRemaining = Math.Max(RuneSilenceRemaining, 2.5);
                if (!_runeWasInterrupted)
                {
                    RunesInterrupted += 1;
                    _runeWasInterrupted = true;
                    var center = Center();
                    BurstParticles(center.X, center.Y, PhaseAccent, 28, 3.0f);
                }
            }
        }
        if (source == DamageSource.Direct)
            StaggerDecayTimer = StaggerDecayDelay;
        if (source == DamageSource.Direct && Stagger >= MaxStagger)
            TriggerStagger();
        EnforceDamageFloor();
        return new HitResult(true, false, normalApplied);
    }

    private void EnforceDamageFloor()
    {
        if (DebugPhaseLocked || SurvivalActive || Dying)
        {
            Hp = Math.Max(0, Hp);
            return;
        }
        int floor = NextSurvivalPhase switch
        {
            3 => (int)Math.Round(MaxHp * (2.0 / 3)),
            6 => (int)Math.Round(MaxHp * (1.0 / 3)),
            9 => 1,
            _ => 0,
        };
        if (Hp <= floor && PhaseDeclarations < MinimumDamagePhaseDeclarations)
            Hp = floor;
        else
            Hp = Math.Max(0, Hp);
    }

    public override bool IsDead() => Dying && Hp <= 0;

    private void TriggerStagger()
    {
        if (IsStaggered || SurvivalActive)
            return;
        bool wasPerfect = Phase > 1 && PhaseElapsed <= .75;
        IsStaggered = true;
        TransitionCleanupRequested = true;
        RuneCannonCharge = 0.0;
        RuneCannonReceiver = null;
        PerfectStagger = wasPerfect;
        if (PerfectStagger)
        {
            PerfectStaggers += 1;
            PerfectBreakFlash = 1.0;
            ShakeStrength = Math.Max(ShakeStrength, 9);
            var center = Center();
            BurstParticles(center.X, center.Y, UiTheme.Cream, 44, 3.8f);
        }
        StaggerRemaining = StaggerDuration;
        Fracture = 0.0;
        var center2 = Center();
        BurstParticles(center2.X, center2.Y, UiTheme.Cream, 34, 3.4f);
    }

    public void UpdateStagger(double dt)
    {
        if (SurvivalActive)
        {
            Stagger = 0.0;
            StaggerDecayTimer = 0.0;
            IsStaggered = false;
            StaggerRemaining = 0.0;
            return;
        }
        if (IsStaggered)
        {
            StaggerRemaining = Math.Max(0.0, StaggerRemaining - dt);
            if (StaggerRemaining <= 0)
            {
                int? nextPhase = HealthUnlockedSurvival();
                if (nextPhase.HasValue)
                {
                    SetPhase(nextPhase.Value);
                }
                else if (PhaseDeclarations >= MinimumDamagePhaseDeclarations)
                {
                    SetPhase(ChooseDamagePhase());
                }
                else
                {
                    IsStaggered = false;
                    Stagger = 0.0;
                    StaggerDecayTimer = 0.0;
                }
                PerfectStagger = false;
                Fracture = 0.0;
            }
            return;
        }
        StaggerDecayTimer = Math.Max(0.0, StaggerDecayTimer - dt);
        if (StaggerDecayTimer <= 0 && Stagger > 0)
        {
            Stagger = Math.Max(0.0, Stagger - StaggerDecayPerSecond * dt);
            StaggerEverDecayed = true;
        }
    }

    public void SetPhase(int phase)
    {
        if (phase == Phase)
            return;
        ClearSurvivalPortals();
        Phase = phase;
        if (DamagePhaseSet.Contains(phase))
        {
            _damagePhaseHistory.Add(phase);
            if (_damagePhaseHistory.Count > 3)
                _damagePhaseHistory.RemoveAt(0);
        }
        PhaseElapsed = 0.0;
        PhaseDeclarations = 0;
        _declarationCooldown = 0;
        Stagger = 0.0;
        StaggerDecayTimer = 0.0;
        IsStaggered = false;
        StaggerRemaining = 0.0;
        PhaseForcedByTimer = false;
        RuneDisruption = 0.0;
        _runeWasInterrupted = false;
        RuneCannonCharge = 0.0;
        RuneCannonReceiver = null;
        MirrorJumpRemaining = 0.0;
        _mirrorJumpStart = null;
        _mirrorJumpTarget = null;
        _mirrorJumpEchoOrigin = null;
        PhaseAnnouncementTimer = 5.0;
        PhaseProtectionTimer = CinematicTransitionsEnabled ? 5.0 : 0.0;
        SurvivalActive = SurvivalPhaseSet.Contains(phase);
        SurvivalRemaining = SurvivalActive ? (phase == 9 ? SurvivalDuration : 20.0) : 0.0;
        SurvivalCooldown = .2;
        _bossBurstQueue.Clear();
        if (CinematicTransitionsEnabled)
        {
            TransitionRemaining = TransitionDuration;
            TransitionCleanupRequested = true;
            ClearField();
            ClearPortals();
            _transitionStart = (WorldX, WorldY);
            TransitionTarget = (ArenaCenter.X - Size / 2f, ArenaCenter.Y - Size / 2f);
        }
        var center = Center();
        BurstParticles(center.X, center.Y, PhaseAccent, 24, 2.8f);
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[phase];
        if (phase is 4 or 7)
        {
            ActTransitionTimer = 2.2;
            ActTitle = phase == 4 ? "ACT II // THE EMPTY DRONE" : "ACT III // THE DEFENSE";
        }
        switch (phase)
        {
            case 1:
                ClearPortals();
                DeployPhaseOnePortals();
                break;
            case 2:
                if (ProjectilePortals.Count == 0)
                    DeployPhaseOnePortals();
                CarouselCooldown = .35;
                foreach (var portal in ProjectilePortals)
                {
                    portal.AngularSpeed = .48f;
                    portal.FireInterval = 999f;
                    portal.MovementPath = "wave";
                }
                break;
            case 3:
                if (ProjectilePortals.Count == 0)
                    DeployPhaseOnePortals();
                foreach (var portal in ProjectilePortals)
                {
                    portal.AngularSpeed = .55f;
                    portal.FireInterval = 1.4f;
                    portal.FireCooldown = Math.Min(portal.FireCooldown, .35f);
                    portal.MovementPath = "tornado";
                }
                break;
            case 4:
                JumpCooldown = 4.5;
                DeployPatternPortals(4, 6.4f, -.34f, UiTheme.Red, "dissonance_static");
                break;
            case 5:
                DeployPatternPortals(2, 4.4f, .42f, UiTheme.Gold, "dissonance_mirror_portal");
                MirrorCooldown = .7;
                break;
            case 6:
                DeployRelayPortals();
                foreach (var portal in ProjectilePortals)
                    portal.MovementPath = "wave";
                RelayCooldown = .5;
                _relayPending = null;
                break;
            case 7:
                DeployPatternPortals(4, 5.4f, .23f, UiTheme.Blue, "dissonance_constellation");
                FieldDeployed = false;
                break;
            case 8:
                if (ProjectilePortals.Count == 0 || ProjectilePortals[0].Owner != "dissonance_constellation")
                    DeployPatternPortals(4, 5.4f, -.38f, UiTheme.Blue, "dissonance_constellation");
                HorizonCooldown = .3;
                foreach (var portal in ProjectilePortals)
                {
                    portal.AngularSpeed = -.38f;
                    portal.MovementPath = "tornado";
                }
                foreach (var projectile in FieldProjectiles)
                    projectile.AngularSpeed *= 1.65f;
                break;
            case 9:
                ClearField();
                ClearPortals();
                DeployLastWordPortals();
                foreach (var portal in ProjectilePortals)
                    portal.MovementPath = "tornado";
                LastWordCooldown = .2;
                CallbackCooldown = 1.4;
                CallbackIndex = 0;
                break;
        }
        if (SurvivalActive)
            DeploySurvivalPortals();
        var runeStrokes = PhaseRunes[phase].Strokes;
        foreach (var portal in ProjectilePortals.Concat(SurvivalPortals))
        {
            portal.ResetForPhase(runeStrokes);
            portal.HitsToDisable = 15;
        }
    }

    /// <summary>Cycle attacks within the current act until its HP gate is reached.</summary>
    private int ChooseDamagePhase()
    {
        IReadOnlyList<int> actPhases = NextSurvivalPhase switch
        {
            3 => new[] { 1, 2 },
            6 => new[] { 4, 5 },
            9 => new[] { 7, 8 },
            _ => DamagePhaseSet.ToList(),
        };
        var recent = _damagePhaseHistory.Count > 3
            ? _damagePhaseHistory.Skip(_damagePhaseHistory.Count - 3)
            : _damagePhaseHistory;
        var recentSet = recent.ToHashSet();
        var choices = actPhases.Where(phase => !recentSet.Contains(phase)).ToList();
        if (choices.Count == 0)
            choices = actPhases.Where(phase => phase != Phase).ToList();
        if (choices.Count == 0)
            choices = actPhases.ToList();
        return choices[_rng.Next(choices.Count)];
    }

    /// <summary>Return the next ordered survival rune once its HP gate is reached.</summary>
    private int? HealthUnlockedSurvival()
    {
        if (PhaseDeclarations < MinimumDamagePhaseDeclarations)
            return null;
        if (NextSurvivalPhase == 3 && Hp <= MaxHp * (2.0 / 3))
            return 3;
        if (NextSurvivalPhase == 6 && Hp <= MaxHp * (1.0 / 3))
            return 6;
        if (NextSurvivalPhase == 9 && Hp <= 1)
            return 9;
        return null;
    }

    /// <summary>Dev/testing hotkey support. Ported from debug_set_phase().</summary>
    public void DebugSetPhase(int phase)
    {
        phase = Math.Clamp(phase, 1, 9);
        if (phase == Phase)
            Phase = 0;
        SetPhase(phase);
        EntranceRemaining = 0;
        PhaseElapsed = 0;
        PhaseProtectionTimer = 0;
    }

    private static int ActiveDissonanceThreats(List<EnemyProjectile> sink) =>
        sink.Count(projectile => !projectile.RemFlag &&
            projectile.Owner?.StartsWith("dissonance") == true);

    private bool CommitStagedThreats(List<EnemyProjectile> sink, List<EnemyProjectile> staged)
    {
        if (staged.Count == 0)
            return false;
        if (ActiveDissonanceThreats(sink) + staged.Count > ActiveThreatSoftCap)
        {
            FieldProjectiles.RemoveAll(staged.Contains);
            if (FieldProjectiles.Count == 0 && Phase is 7 or 8)
                FieldDeployed = false;
            return false;
        }
        sink.AddRange(staged);
        return true;
    }

    public void SurvivalBarrage(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        SurvivalCooldown -= dt;
        if (SurvivalCooldown > 0 || ProjectilePortals.Count == 0)
            return;
        var active = ProjectilePortals.Where(p => p.Active).ToList();
        if (active.Count > 0)
        {
            var source = active[CarouselIndex % active.Count];
            var portalCenter = new Vector2(source.WorldX + source.Size / 2f, source.WorldY + source.Size / 2f);
            var target = Phase == 3
                ? ArenaCenter
                : portalCenter + new Vector2(MathF.Cos(source.Angle), MathF.Sin(source.Angle)) * ArenaRadius;
            BurstWave[] waves = Phase < 9
                ? new[] { new BurstWave(3, .42f, .82f, .28f), new BurstWave(5, .68f, 1.08f, .38f), new BurstWave(4, .5f, 1.38f, .32f) }
                : new[] { new BurstWave(5, .72f, .78f, .27f), new BurstWave(7, 1.02f, 1.08f, .36f), new BurstWave(3, .28f, 1.48f, .48f), new BurstWave(8, 1.18f, 1.24f, .25f) };
            source.FirePatternBurst(sink, target, waves, Phase == 9 ? .12f : .16f, .9f, PhaseAccent, "survival_burst");
            int speedBurstStride = Phase == 9 ? 2 : 4;
            if (CarouselIndex % speedBurstStride == 0)
                source.FireSpeedBurst(sink, new Vector2(playerX, playerY), Phase == 9 ? 7 : 3 + Phase / 4, PhaseAccent, "survival_speed_burst");
            CarouselIndex += 1;
        }
        SurvivalCooldown = Phase == 9 ? .5 : .82;

        if (SurvivalPortals.Count > 0)
        {
            int stride = Phase == 9 ? 2 : 3;
            int offset = CarouselIndex % stride;
            for (int index = offset; index < SurvivalPortals.Count; index += stride)
            {
                var portal = SurvivalPortals[index];
                var portalCenter = new Vector2(portal.WorldX + portal.Size / 2f, portal.WorldY + portal.Size / 2f);
                var target = Phase == 3
                    ? ArenaCenter
                    : portalCenter + new Vector2(MathF.Cos(portal.Angle), MathF.Sin(portal.Angle)) * ArenaRadius;
                float speed = new[] { .65f, .9f, 1.15f }[(index / stride + CarouselIndex) % 3];
                portal.FirePatternBurst(sink, target,
                    new[] { new BurstWave(1, 0, speed, .3f), new BurstWave(1, 0, speed * 1.22f, .4f), new BurstWave(1, 0, speed * .78f, .24f) },
                    .11f, .9f, PhaseAccent, Phase == 3 ? "boundary_inward" : "boundary_outward");
                if (Phase == 9)
                {
                    var tangentTarget = portalCenter + new Vector2(MathF.Cos(portal.Angle + MathF.PI / 2f), MathF.Sin(portal.Angle + MathF.PI / 2f)) * ArenaRadius;
                    portal.FireToward(sink, tangentTarget, 2, .24f, speed * 1.15f, .9f, UiTheme.Cream, "boundary_tangent");
                }
            }
        }
    }

    private void DeploySurvivalPortals()
    {
        ClearSurvivalPortals();
        int count = Phase < 9 ? 6 : 8;
        float radius = Phase == 3 ? ArenaRadius * .91f : Simulation.TileSize * 3.4f * ArenaFormationScale;
        float speed = Phase switch { 3 => .2f, 6 => .34f, _ => .48f };
        for (int index = 0; index < count; index++)
        {
            SurvivalPortals.Add(new ProjectilePortal(
                ArenaCenter, radius, index * 2f * MathF.PI / count, angularSpeed: speed, fireInterval: 999f,
                pelletCount: 2, spread: .22f, owner: "dissonance_survival_boundary", color: PhaseAccent, movementPath: "orbit"));
        }
    }

    /// <summary>Choreograph the three survival rings as distinct set pieces.</summary>
    public void UpdateSurvivalFormation(double dt)
    {
        float inner = Simulation.TileSize * 3.4f * ArenaFormationScale;
        float outer = ArenaRadius * .91f;
        int cycle = (int)(PhaseElapsed / 3.2);
        for (int index = 0; index < SurvivalPortals.Count; index++)
        {
            var portal = SurvivalPortals[index];
            portal.OrbitCenter = Phase == 6 ? Center() : ArenaCenter;
            float targetRadius; float speed; string path;
            if (Phase == 3)
            {
                targetRadius = outer; speed = .2f; path = "orbit";
            }
            else if (Phase == 6)
            {
                targetRadius = inner; speed = .34f; path = "orbit";
            }
            else
            {
                bool outside = (index + cycle) % 2 == 0;
                targetRadius = outside ? outer : inner;
                speed = new[] { .22f, .42f, .68f }[cycle % 3];
                path = cycle % 3 == 2 ? "figure8" : "orbit";
            }
            portal.Radius += (targetRadius - portal.Radius) * Math.Min(1f, (float)dt * 2.4f);
            portal.AngularSpeed = speed;
            portal.MovementPath = path;
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
        }
    }

    private void ClearSurvivalPortals()
    {
        foreach (var portal in SurvivalPortals)
            portal.RemFlag = true;
        SurvivalPortals.Clear();
    }

    private void ClearPortals()
    {
        foreach (var portal in ProjectilePortals)
            portal.RemFlag = true;
        ProjectilePortals.Clear();
    }

    private void BeginDeath()
    {
        if (Dying)
            return;
        Hp = 1;
        Dying = true;
        DeathRemaining = DeathDuration;
        ShakeStrength = 6;
        TransitionCleanupRequested = true;
        ClearField();
        ClearPortals();
        ClearSurvivalPortals();
        WorldX = ArenaCenter.X - Size / 2f;
        WorldY = ArenaCenter.Y - Size / 2f;
    }

    private void ClearField()
    {
        foreach (var projectile in FieldProjectiles)
            projectile.RemFlag = true;
        FieldProjectiles.Clear();
        FieldDeployed = false;
    }

    private void DeployPhaseOnePortals()
    {
        float radius = Simulation.TileSize * 7.2f * ArenaFormationScale;
        for (int index = 0; index < 3; index++)
        {
            ProjectilePortals.Add(new ProjectilePortal(
                ArenaCenter, radius, index * MathF.PI / 2f, angularSpeed: .32f, fireInterval: 1.85f, pelletCount: 7, spread: 1.15f));
        }
    }

    private void DeployRelayPortals()
    {
        ClearPortals();
        float radius = Simulation.TileSize * 6.2f * ArenaFormationScale;
        for (int index = 0; index < 3; index++)
        {
            ProjectilePortals.Add(new ProjectilePortal(
                ArenaCenter, radius, index * MathF.PI / 2f + MathF.PI / 4f, angularSpeed: -.2f, fireInterval: 999f,
                pelletCount: 7, spread: 1.0f, owner: "dissonance_relay"));
        }
    }

    private static readonly IReadOnlyDictionary<string, string> PatternMovementPaths = new Dictionary<string, string>
    {
        ["dissonance_static"] = "square", ["dissonance_mirror_portal"] = "figure8", ["dissonance_constellation"] = "figure8",
    };

    private void DeployPatternPortals(int count, float radiusTiles, float angularSpeed, Color color, string owner)
    {
        ClearPortals();
        string path = PatternMovementPaths.GetValueOrDefault(owner, "orbit");
        for (int index = 0; index < count; index++)
        {
            ProjectilePortals.Add(new ProjectilePortal(
                ArenaCenter, Simulation.TileSize * radiusTiles * ArenaFormationScale, index * 2f * MathF.PI / count,
                angularSpeed: angularSpeed, fireInterval: 999f, pelletCount: 5, spread: .72f, owner: owner, color: color,
                polarity: index % 2 == 0 ? 1 : -1, movementPath: path));
        }
    }

    /// <summary>Send a player shot through a paired portal and empower the rerouted hit. Ported from route_player_bullet.</summary>
    public bool RoutePlayerBullet(Bullet bullet, int portalIndex)
    {
        if (bullet.PortalCooldown > 0 || ProjectilePortals.Count == 0)
            return false;
        var source = ProjectilePortals[portalIndex];
        if (!source.BlocksShots || source.Polarity < 0)
            return false;
        int destinationIndex = (portalIndex + ProjectilePortals.Count / 2) % ProjectilePortals.Count;
        var destination = ProjectilePortals[destinationIndex];
        if (!destination.BlocksShots || destination == source)
            return false;
        float exitDistance = destination.Size * .8f;
        float newX = destination.WorldX + destination.Size / 2f + MathF.Cos(bullet.Direction) * exitDistance;
        float newY = destination.WorldY + destination.Size / 2f - MathF.Sin(bullet.Direction) * exitDistance;
        float newDirection = bullet.Direction + (destination.Polarity < 0 ? MathF.PI : 0f);
        float damageMultiplier = destination.Polarity > 0 ? 1.15f : 1.05f;
        bullet.RouteThroughPortal(newX, newY, newDirection, damageMultiplier, .35f);
        return true;
    }

    private void DeployLastWordPortals()
    {
        for (int index = 0; index < 4; index++)
        {
            ProjectilePortals.Add(new ProjectilePortal(
                ArenaCenter, Simulation.TileSize * 5.1f * ArenaFormationScale, index * MathF.PI / 3f,
                angularSpeed: .72f, fireInterval: 1.05f, pelletCount: 5, spread: .7f, owner: "dissonance_last_word"));
        }
    }

    private float MoveToward(float targetX, float targetY, Battleground battleground, float multiplier = 1.0f)
    {
        var center = Center();
        float dx = targetX - center.X, dy = targetY - center.Y;
        float distance = Math.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
        float step = Speed * multiplier * (float)Simulation.GetFrameScale();
        TryAxisMove(dx / distance * step, "x", battleground);
        TryAxisMove(dy / distance * step, "y", battleground);
        return distance;
    }

    /// <summary>Give every rune a readable locomotion identity within its act.</summary>
    private void PhaseMovementStep(float playerX, float playerY, double dt, Battleground battleground)
    {
        float tile = Simulation.TileSize;
        string mode = PhaseMovement[Phase];
        switch (mode)
        {
            case "harvest_chase":
            {
                float side = MathF.Sin((float)PhaseElapsed * 1.35f) * tile * 3.2f;
                float angle = MathF.Atan2(playerY - ArenaCenter.Y, playerX - ArenaCenter.X) + MathF.PI / 2f;
                MoveToward(playerX + MathF.Cos(angle) * side, playerY + MathF.Sin(angle) * side, battleground, .72f);
                break;
            }
            case "road_anchor":
                MoveToward(ArenaCenter.X, ArenaCenter.Y, battleground, .75f);
                break;
            case "day_anchor":
                MoveToward(ArenaCenter.X, ArenaCenter.Y, battleground, 1.05f);
                break;
            case "torch_tornado":
            case "hearth_tornado":
            {
                float speed = mode == "torch_tornado" ? .72f : 1.05f;
                float radius = tile * (7.0f + 2.2f * MathF.Sin((float)PhaseElapsed * .62f));
                float angle = (float)PhaseElapsed * speed;
                MoveToward(ArenaCenter.X + MathF.Cos(angle) * radius, ArenaCenter.Y + MathF.Sin(angle) * radius, battleground, 1.35f);
                break;
            }
            case "hail_chase":
            {
                var center = Center();
                float diagonal = (MathF.PI / 4f) * MathF.Round(MathF.Atan2(playerY - center.Y, playerX - center.X) / (MathF.PI / 4f));
                MoveToward(playerX + MathF.Cos(diagonal) * tile, playerY + MathF.Sin(diagonal) * tile, battleground, .48f);
                break;
            }
            case "yew_anchor":
                MoveToward(ArenaCenter.X, ArenaCenter.Y, battleground, .38f);
                break;
            case "sun_revolution":
                MoveToward(ArenaCenter.X + MathF.Sin((float)PhaseElapsed * .82f) * tile * 9,
                    ArenaCenter.Y + MathF.Sin((float)PhaseElapsed * 1.64f) * tile * 4.5f, battleground, 1.15f);
                break;
            case "spear_intercept":
            {
                float lead = tile * 4.5f;
                var center = Center();
                float angle = MathF.Atan2(playerY - center.Y, playerX - center.X);
                MoveToward(playerX + MathF.Cos(angle) * lead, playerY + MathF.Sin(angle) * lead, battleground, .92f);
                break;
            }
        }
    }

    private void FireProjectile(List<EnemyProjectile> sink, float direction, float speed, float damage, float size, Action<EnemyProjectile>? configure = null)
    {
        var center = Center();
        var projectile = new EnemyProjectile(center.X - size / 2f, center.Y - size / 2f, direction, speed, damage, size,
            owner: "dissonance_body", ignoreWalls: true);
        configure?.Invoke(projectile);
        sink.Add(projectile);
    }

    private void FireLaser(List<EnemyProjectile> sink, float targetX, float targetY, Color? color = null)
    {
        var center = Center();
        float direction = MathF.Atan2(targetY - center.Y, targetX - center.X);
        sink.Add(new EnemyProjectile(center.X, center.Y, direction, 0, 2.0f, Simulation.TileSize * .42f,
            travelRange: ArenaRadius * 2.2f, color: color ?? PhaseAccent, shape: "laser", path: "laser",
            lifetime: 4.0f, owner: "dissonance_rune_laser", ignoreWalls: true));
    }

    public void FireSpeedBurst(List<EnemyProjectile> sink, float targetX, float targetY, int? count = null)
    {
        var center = Center();
        float direction = MathF.Atan2(targetY - center.Y, targetX - center.X);
        float[] speeds = { 1.45f, 1.12f, .82f, .56f, .38f };
        int actualCount = count ?? _rng.Next(3, 6);
        for (int index = 0; index < actualCount; index++)
        {
            sink.Add(new EnemyProjectile(center.X - Simulation.TileSize * (.34f + index * .035f) / 2f, center.Y - Simulation.TileSize * (.34f + index * .035f) / 2f,
                direction, speeds[index], .9f, Simulation.TileSize * (.34f + index * .035f),
                travelRange: float.PositiveInfinity, color: PhaseAccent, shape: "diamond", owner: "dissonance_speed_burst", ignoreWalls: true));
        }
        // Two delayed echoes turn the speed stack into a true three-shot boss burst
        // while retaining the fast-leader/slow-tail dodge timing.
        _bossBurstQueue.Add(new BossBurst { Timer = .13, TargetX = targetX, TargetY = targetY, Count = Math.Max(3, actualCount - 1), SpeedScale = .82f });
        _bossBurstQueue.Add(new BossBurst { Timer = .28, TargetX = targetX, TargetY = targetY, Count = Math.Min(5, actualCount + 1), SpeedScale = 1.12f });
    }

    private void UpdateBossBursts(List<EnemyProjectile> sink, double dt)
    {
        var remaining = new List<BossBurst>();
        float[] speeds = { 1.45f, 1.12f, .82f, .56f, .38f };
        foreach (var burst in _bossBurstQueue)
        {
            burst.Timer -= dt;
            if (burst.Timer > 0)
            {
                remaining.Add(burst);
                continue;
            }
            var center = Center();
            float direction = MathF.Atan2(burst.TargetY - center.Y, burst.TargetX - center.X);
            for (int index = 0; index < burst.Count; index++)
            {
                float speed = speeds[index] * burst.SpeedScale;
                float size = Simulation.TileSize * (.28f + index * .045f);
                sink.Add(new EnemyProjectile(center.X - size / 2f, center.Y - size / 2f, direction, speed, .9f, size,
                    travelRange: float.PositiveInfinity, color: PhaseAccent, shape: "diamond", owner: "dissonance_speed_burst_echo", ignoreWalls: true));
            }
        }
        _bossBurstQueue.Clear();
        _bossBurstQueue.AddRange(remaining);
    }

    private void LobBomb(List<EnemyProjectile> sink, float targetX, float targetY, Color? color = null)
    {
        var center = Center();
        sink.Add(new EnemyProjectile(center.X, center.Y, 0, 0, 1.25f, Simulation.TileSize * .72f,
            color: color ?? PhaseAccent, shape: "bomb", path: "bomb", lifetime: 3.0f,
            target: new Vector2(targetX, targetY), owner: "dissonance_rune_bomb", ignoreWalls: true));
    }

    public void UpdateSpecialAttacks(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        SpecialAttackCooldown -= dt;
        if (SpecialAttackCooldown > 0)
            return;
        int mode = CallbackIndex % 3;
        FireLaser(sink, playerX, playerY);
        if (SurvivalActive && Phase == 9)
        {
            LobBomb(sink, playerX, playerY, UiTheme.Red);
            FireSpeedBurst(sink, playerX, playerY, 5);
            SpecialAttackCooldown = 3.4;
        }
        else if (SurvivalActive)
        {
            SpecialAttackCooldown = 5.8;
        }
        else if (mode == 1)
        {
            LobBomb(sink, playerX, playerY);
            SpecialAttackCooldown = 5.8;
        }
        else if (mode == 2)
        {
            FireSpeedBurst(sink, playerX, playerY);
            SpecialAttackCooldown = 5.2;
        }
        else
        {
            SpecialAttackCooldown = 4.8;
        }
        CallbackIndex += 1;
    }

    private void FirePortalMines(List<EnemyProjectile> sink)
    {
        foreach (var portal in ProjectilePortals)
        {
            float size = Simulation.TileSize * .62f;
            float portalX = portal.WorldX + portal.Size / 2f, portalY = portal.WorldY + portal.Size / 2f;
            float direction = MathF.Atan2(ArenaCenter.Y - portalY, ArenaCenter.X - portalX);
            sink.Add(new EnemyProjectile(portalX - size / 2f, portalY - size / 2f, direction, .9f, 2.5f, size,
                travelRange: Simulation.TileSize * 18f, lifetime: 18f, speedDecay: .16f, color: UiTheme.Red,
                shape: "mine", path: "mine", owner: "dissonance_portal_mine", ignoreWalls: true));
        }
    }

    private void FireSineFromPortal(ProjectilePortal portal, Vector2 target, List<EnemyProjectile> sink, int count = 3, Color? color = null)
    {
        float portalX = portal.WorldX + portal.Size / 2f, portalY = portal.WorldY + portal.Size / 2f;
        float baseDirection = MathF.Atan2(target.Y - portalY, target.X - portalX);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .42f;
            float size = Simulation.TileSize * .3f;
            sink.Add(new EnemyProjectile(portalX - size / 2f, portalY - size / 2f, baseDirection + offset, .72f, 1.05f, size,
                travelRange: Simulation.TileSize * 72f, color: color ?? portal.Color, shape: "diamond", path: "sine",
                amplitude: Simulation.TileSize * .22f, frequency: .04f, owner: $"{portal.Owner}_sine", ignoreWalls: true));
        }
    }

    private void FireSineFan(float playerX, float playerY, List<EnemyProjectile> sink, int count = 6, Color? color = null)
    {
        var center = Center();
        float baseDirection = MathF.Atan2(playerY - center.Y, playerX - center.X);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .64f;
            float size = Simulation.TileSize * .42f;
            sink.Add(new EnemyProjectile(center.X - size / 2f, center.Y - size / 2f, baseDirection + offset, .45f, 1.25f, size,
                travelRange: Simulation.TileSize * 72f, color: color ?? UiTheme.Purple, shape: "diamond", path: "sine",
                amplitude: Simulation.TileSize * (0.32f + Math.Abs(offset)) * .5f, frequency: .035f, owner: "dissonance_sine"));
        }
    }

    private void PhaseOne(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        foreach (var portal in ProjectilePortals)
            portal.Update(sink, (float)dt);
        PatternCooldown -= dt;
        if (PatternCooldown <= 0)
        {
            FireSineFan(playerX, playerY, sink, 6);
            PatternCooldown = 1.05;
        }
        MineCooldown -= dt;
        if (MineCooldown <= 0)
        {
            FirePortalMines(sink);
            MineCooldown = 4.6;
        }
    }

    private void PhaseClosingSpiral(List<EnemyProjectile> sink, double dt)
    {
        float targetRadius = Simulation.TileSize * ArenaFormationScale * MathF.Max(3.8f, 7.2f - (float)PhaseElapsed * .7f);
        foreach (var portal in ProjectilePortals)
        {
            portal.Radius += (targetRadius - portal.Radius) * Math.Min(1f, (float)dt * 2.2f);
            portal.Update(sink, (float)dt);
        }
    }

    private void PhaseCrossfireCarousel(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        foreach (var portal in ProjectilePortals)
        {
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
        }
        CarouselCooldown -= dt;
        if (CarouselCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            var portal = ProjectilePortals[CarouselIndex % ProjectilePortals.Count];
            var portalCenter = new Vector2(portal.WorldX + portal.Size / 2f, portal.WorldY + portal.Size / 2f);
            float tangent = portal.Angle + (CarouselIndex % 2 == 0 ? MathF.PI / 2f : -MathF.PI / 2f);
            var target = portalCenter + new Vector2(MathF.Cos(tangent), MathF.Sin(tangent)) * Simulation.TileSize * 12f;
            portal.FireToward(sink, target, 6, .9f, 1.3f, .9f, UiTheme.Cream, "carousel");
            CarouselIndex += 1;
            CarouselCooldown = .48;
        }
        PatternCooldown -= dt;
        if (PatternCooldown <= 0)
        {
            FireSineFan(playerX, playerY, sink, 5, UiTheme.Purple);
            PatternCooldown = 1.35;
        }
    }

    private void JumpTowardPlayer(float playerX, float playerY, Battleground battleground)
    {
        var center = Center();
        float dx = playerX - center.X, dy = playerY - center.Y;
        float distance = Math.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
        float jumpDistance = Math.Min(Simulation.TileSize * 7f, Math.Max(0f, distance - Simulation.TileSize * 2.5f));
        var target = new Rectangle((int)(WorldX + dx / distance * jumpDistance), (int)(WorldY + dy / distance * jumpDistance), (int)Size, (int)Size);
        var safe = battleground.FindNearestOpenRect(target);
        WorldX = safe.X;
        WorldY = safe.Y;
    }

    private void StartMirrorJump(float playerX, float playerY, Battleground battleground)
    {
        var center = Center();
        float dx = playerX - center.X, dy = playerY - center.Y;
        float distance = Math.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
        float jumpDistance = Math.Min(Simulation.TileSize * 7f, Math.Max(0f, distance - Simulation.TileSize * 2.5f));
        var target = new Rectangle((int)(WorldX + dx / distance * jumpDistance), (int)(WorldY + dy / distance * jumpDistance), (int)Size, (int)Size);
        var safe = battleground.FindNearestOpenRect(target);
        _mirrorJumpStart = (WorldX, WorldY);
        _mirrorJumpTarget = (safe.X, safe.Y);
        _mirrorJumpEchoOrigin = Center();
        MirrorJumpRemaining = MirrorJumpDuration;
    }

    private bool UpdateMirrorJump(List<EnemyProjectile> sink, double dt)
    {
        if (MirrorJumpRemaining <= 0)
            return false;
        MirrorJumpRemaining = Math.Max(0.0, MirrorJumpRemaining - dt);
        double progress = 1 - MirrorJumpRemaining / MirrorJumpDuration;
        double eased = progress * progress * (3 - 2 * progress);
        var start = _mirrorJumpStart!.Value;
        var target = _mirrorJumpTarget!.Value;
        WorldX = (float)(start.X + (target.X - start.X) * eased);
        WorldY = (float)(start.Y + (target.Y - start.Y) * eased - Math.Sin(progress * Math.PI) * Simulation.TileSize * 1.35);
        if (MirrorJumpRemaining > 0)
            return true;
        var echoes = new[] { _mirrorJumpEchoOrigin!.Value, Center() };
        for (int index = 0; index < Math.Min(ProjectilePortals.Count, echoes.Length); index++)
        {
            var portal = ProjectilePortals[index];
            var echo = echoes[index];
            portal.OrbitCenter = echo;
            portal.Radius = 0;
            portal.Place();
            FireRadialFrom(sink, echo, 10, (float)PhaseElapsed * .18f,
                portal == ProjectilePortals[^1] ? UiTheme.Gold : UiTheme.Red,
                index == 0 ? "dissonance_mirror_portal_afterimage" : "dissonance_mirror_portal_landing_echo");
        }
        return false;
    }

    private void PhaseTwo(float playerX, float playerY, List<EnemyProjectile> sink, double dt, Battleground battleground)
    {
        if (PhaseElapsed < 1.6)
            return;
        if (JumpRecovery > 0)
        {
            JumpRecovery -= dt;
            return;
        }
        if (JumpWindup > 0)
        {
            JumpWindup -= dt;
            if (JumpWindup <= 0)
            {
                JumpTowardPlayer(playerX, playerY, battleground);
                JumpRecovery = .85;
            }
            return;
        }
        JumpCooldown -= dt;
        if (JumpCooldown <= 0)
        {
            JumpWindup = 1.15;
            JumpCooldown = 7.2;
            return;
        }
        foreach (var portal in ProjectilePortals)
        {
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
        }
        RadialCooldown -= dt;
        if (RadialCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            int half = Math.Max(1, ProjectilePortals.Count / 2);
            int sourceIndex = CarouselIndex % half;
            foreach (int index in new[] { sourceIndex, sourceIndex + half })
            {
                var source = ProjectilePortals[index];
                var target = ProjectilePortals[(index + half) % ProjectilePortals.Count];
                source.FireToward(sink, new Vector2(target.WorldX + target.Size / 2f, target.WorldY + target.Size / 2f),
                    5, .3f, 1.65f, 1.0f, UiTheme.Red, "chord");
            }
            CarouselIndex += 1;
            RadialCooldown = .72;
        }
        AimedCooldown -= dt;
        if (AimedCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            for (int index = 0; index < ProjectilePortals.Count; index += 2)
                FireSineFromPortal(ProjectilePortals[index], new Vector2(playerX, playerY), sink, 3, UiTheme.Gold);
            AimedCooldown = 1.8;
        }
    }

    private void FireRadialFrom(List<EnemyProjectile> sink, Vector2 origin, int count, float offset, Color color, string owner)
    {
        float size = Simulation.TileSize * .3f;
        for (int index = 0; index < count; index++)
        {
            sink.Add(new EnemyProjectile(origin.X - size / 2f, origin.Y - size / 2f, 2f * MathF.PI * index / count + offset,
                1.35f, 1.0f, size, travelRange: Simulation.TileSize * 15f, color: color, shape: "diamond", owner: owner));
        }
    }

    private void PhaseMirrorStep(float playerX, float playerY, List<EnemyProjectile> sink, double dt, Battleground battleground)
    {
        foreach (var portal in ProjectilePortals)
        {
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
        }
        if (UpdateMirrorJump(sink, dt))
            return;
        MirrorCooldown -= dt;
        if (MirrorCooldown <= 0)
        {
            StartMirrorJump(playerX, playerY, battleground);
            MirrorCooldown = 1.55;
        }
        AimedCooldown -= dt;
        if (AimedCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            foreach (var portal in ProjectilePortals)
                FireSineFromPortal(portal, new Vector2(playerX, playerY), sink, 3, UiTheme.Cream);
            AimedCooldown = 1.1;
        }
    }

    private void DeployRotatingDiamond(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        var center = Center();
        float awayAngle = MathF.Atan2(center.Y - playerY, center.X - playerX);
        float spacing = Simulation.TileSize * .66f;
        for (int diamondIndex = 0; diamondIndex < 4; diamondIndex++)
        {
            float diamondAngle = awayAngle + diamondIndex * MathF.PI / 2f;
            for (int row = 1; row < 7; row++)
            {
                int halfWidth = Math.Min(row - 1, 6 - row);
                for (int column = -halfWidth; column <= halfWidth; column++)
                {
                    float forward = Simulation.TileSize * 1.2f + row * spacing;
                    float lateral = column * spacing;
                    float offsetX = MathF.Cos(diamondAngle) * forward - MathF.Sin(diamondAngle) * lateral;
                    float offsetY = MathF.Sin(diamondAngle) * forward + MathF.Cos(diamondAngle) * lateral;
                    float radius = MathF.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    float angle = MathF.Atan2(offsetY, offsetX);
                    var projectile = new EnemyProjectile(center.X, center.Y, 0, 0, 1.55f, Simulation.TileSize * .34f,
                        travelRange: float.PositiveInfinity, lifetime: 90f, color: UiTheme.Blue, shape: "mine", path: "orbit",
                        orbitCenter: center, orbitRadius: radius, orbitAngle: angle, angularSpeed: .12f + diamondIndex * .025f,
                        owner: "dissonance_field", ignoreWalls: true);
                    sink.Add(projectile);
                    FieldProjectiles.Add(projectile);
                }
            }
        }
        FieldDeployed = true;
    }

    private void PhaseThree(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        if (!FieldDeployed)
            DeployRotatingDiamond(playerX, playerY, sink);
        foreach (var portal in ProjectilePortals)
        {
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Radius = Simulation.TileSize * ArenaFormationScale * (4.8f + .8f * MathF.Sin((float)PhaseElapsed * .7f + portal.Angle * 2));
            portal.Place();
        }
        PatternCooldown -= dt;
        if (PatternCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            var source = ProjectilePortals[CarouselIndex % ProjectilePortals.Count];
            FireSineFromPortal(source, new Vector2(playerX, playerY), sink, 5, UiTheme.Blue);
            CarouselIndex += 2;
            PatternCooldown = .78;
        }
        FieldShotCooldown -= dt;
        if (FieldShotCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            var source = ProjectilePortals[CarouselIndex % ProjectilePortals.Count];
            var target = ProjectilePortals[(CarouselIndex + 2) % ProjectilePortals.Count];
            source.FireToward(sink, new Vector2(target.WorldX + target.Size / 2f, target.WorldY + target.Size / 2f),
                2, .12f, 1.5f, 1.0f, UiTheme.Cream, "constellation_edge");
            FieldShotCooldown = 1.15;
        }
        UpdateRuneCannon(playerX, playerY, sink, dt);
    }

    private void PhaseEventHorizon(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        PhaseThree(playerX, playerY, sink, dt);
        HorizonCooldown -= dt;
        if (HorizonCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            int start = CarouselIndex % ProjectilePortals.Count;
            var sources = new[] { ProjectilePortals[start], ProjectilePortals[(start + ProjectilePortals.Count / 2) % ProjectilePortals.Count] };
            foreach (var source in sources)
                source.FireToward(sink, ArenaCenter, 5, .42f, 1.75f, 1.05f, UiTheme.Purple, "horizon");
            CarouselIndex += 1;
            HorizonCooldown = .62;
        }
    }

    private void PhaseLastWord(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        foreach (var portal in ProjectilePortals)
            portal.Update(sink, (float)dt);
        LastWordCooldown -= dt;
        if (LastWordCooldown <= 0)
        {
            const int count = 14;
            var center = Center();
            int gap = (int)(((MathF.Atan2(playerY - center.Y, playerX - center.X) % (2f * MathF.PI) + 2f * MathF.PI) % (2f * MathF.PI))
                / (2f * MathF.PI) * count);
            for (int index = 0; index < count; index++)
            {
                if (index == gap || index == (gap + 1) % count)
                    continue;
                FireProjectile(sink, 2f * MathF.PI * index / count + (float)PhaseElapsed * .22f, 1.55f, 1.1f, Simulation.TileSize * .3f,
                    projectile => { projectile.RemainingRange = Simulation.TileSize * 16f; });
            }
            LastWordCooldown = .72;
        }
        CallbackCooldown -= dt;
        if (CallbackCooldown <= 0 && ProjectilePortals.Count > 0)
        {
            int mode = CallbackIndex % 3;
            if (mode == 0)
            {
                for (int index = 0; index < ProjectilePortals.Count; index += 2)
                {
                    var portal = ProjectilePortals[index];
                    var center = new Vector2(portal.WorldX + portal.Size / 2f, portal.WorldY + portal.Size / 2f);
                    float tangent = portal.Angle + ((index / 2) % 2 == 0 ? MathF.PI / 2f : -MathF.PI / 2f);
                    var target = center + new Vector2(MathF.Cos(tangent), MathF.Sin(tangent)) * Simulation.TileSize * 11f;
                    portal.FireToward(sink, target, 3, .34f, 1.45f, 1.0f, UiTheme.Cream, "callback_carousel");
                }
            }
            else if (mode == 1)
            {
                int half = ProjectilePortals.Count / 2;
                for (int index = 0; index < half; index++)
                {
                    var source = ProjectilePortals[index];
                    var target = ProjectilePortals[index + half];
                    source.FireToward(sink, new Vector2(target.WorldX + target.Size / 2f, target.WorldY + target.Size / 2f),
                        2, .1f, 1.7f, 1.0f, UiTheme.Red, "callback_chord");
                }
            }
            else
            {
                var source = ProjectilePortals[CallbackIndex % ProjectilePortals.Count];
                var target = ProjectilePortals[(CallbackIndex + 1) % ProjectilePortals.Count];
                source.FireToward(sink, new Vector2(target.WorldX + target.Size / 2f, target.WorldY + target.Size / 2f),
                    1, 0, 1.9f, .9f, UiTheme.Gold, "callback_relay");
            }
            CallbackIndex += 1;
            CallbackCooldown = 2.2;
        }
        UpdateRuneCannon(playerX, playerY, sink, dt);
    }

    public void UpdateRuneCannon(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        if (RuneCannonCharge > 0)
        {
            RuneCannonCharge = Math.Max(0.0, RuneCannonCharge - dt);
            var receiver = RuneCannonReceiver.HasValue && RuneCannonReceiver.Value < ProjectilePortals.Count
                ? ProjectilePortals[RuneCannonReceiver.Value] : null;
            if (receiver is null || !receiver.BlocksShots)
            {
                Stagger = Math.Min(MaxStagger, Stagger + 20);
                RuneSilenceRemaining = Math.Max(RuneSilenceRemaining, 1.2);
                RuneCannonCharge = 0;
                RuneCannonReceiver = null;
            }
            else if (RuneCannonCharge <= 0)
            {
                receiver.FireToward(sink, new Vector2(playerX, playerY), 9, 1.25f, 1.8f, 1.25f, UiTheme.Cream, "rune_cannon");
                RuneCannonReceiver = null;
            }
            return;
        }
        RuneCannonCooldown -= dt;
        var active = ProjectilePortals.Select((portal, index) => (Index: index, Portal: portal)).Where(p => p.Portal.BlocksShots).ToList();
        if (RuneCannonCooldown <= 0 && active.Count >= 2)
        {
            RuneCannonReceiver = active[CarouselIndex % active.Count].Index;
            var receiver = ProjectilePortals[RuneCannonReceiver.Value];
            var target = new Vector2(receiver.WorldX + receiver.Size / 2f, receiver.WorldY + receiver.Size / 2f);
            foreach (var (_, portal) in active)
            {
                portal.TelegraphTimer = 1.4f;
                portal.TelegraphKind = "line";
                portal.TelegraphTarget = target;
            }
            RuneCannonCharge = 1.4;
            RuneCannonCooldown = 7.5;
        }
    }

    public void PhasePortalRelay(List<EnemyProjectile> sink, double dt)
    {
        foreach (var portal in ProjectilePortals)
        {
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
        }
        if (_relayPending is not null)
        {
            _relayPending.Timer -= dt;
            if (_relayPending.Timer <= 0)
            {
                _relayPending.Receiver.FireToward(sink, _relayPending.Continuation, 5, .82f, 1.4f, 1.0f, UiTheme.Cream, "redirect");
                _relayPending = null;
            }
        }
        RelayCooldown -= dt;
        if (RelayCooldown <= 0 && _relayPending is null && ProjectilePortals.Count > 0)
        {
            var source = ProjectilePortals[RelayIndex % ProjectilePortals.Count];
            var receiver = ProjectilePortals[(RelayIndex + 1) % ProjectilePortals.Count];
            var receiverCenter = new Vector2(receiver.WorldX + receiver.Size / 2f, receiver.WorldY + receiver.Size / 2f);
            source.FireToward(sink, receiverCenter, 1, 0, 1.8f, .8f, UiTheme.Gold, "transfer");
            var sourceCenter = new Vector2(source.WorldX + source.Size / 2f, source.WorldY + source.Size / 2f);
            var continuation = receiverCenter + (receiverCenter - sourceCenter);
            _relayPending = new RelayPending { Timer = .42, Receiver = receiver, Continuation = continuation };
            RelayIndex += 1;
            RelayCooldown = 1.35;
        }
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        _declarationCooldown = Math.Max(0.0, _declarationCooldown - dt);
        UpdateStagger(dt);
        RuneSilenceRemaining = Math.Max(0.0, RuneSilenceRemaining - dt);
        PhaseProtectionTimer = Math.Max(0.0, PhaseProtectionTimer - dt);
        StaggerRecoveryRemaining = Math.Max(0.0, StaggerRecoveryRemaining - dt);
        foreach (var portal in ProjectilePortals)
            portal.UpdateStatus((float)dt);
        foreach (var portal in SurvivalPortals)
            portal.UpdateStatus((float)dt);
        AdvanceAge();
        UpdateVisuals(dt);

        if (Dying)
        {
            ShakeStrength = Math.Max(ShakeStrength, 2.5 + 1.5 * Math.Abs(Math.Sin(DeathRemaining * 1.35)));
            int previousTick = (int)(DeathRemaining * 8);
            DeathRemaining = Math.Max(0.0, DeathRemaining - dt);
            if (DeathRemaining <= 0)
            {
                Hp = 0;
            }
            else if (DeathRemaining <= DeathBurstDuration && (int)(DeathRemaining * 8) != previousTick)
            {
                var center = Center();
                BurstParticles(center.X, center.Y, PhaseAccent, 8, 2.5f);
            }
            return;
        }
        if (EntranceRemaining > 0)
        {
            EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
            return;
        }
        if (TransitionRemaining > 0)
        {
            TransitionRemaining = Math.Max(0.0, TransitionRemaining - dt);
            if (_transitionStart.HasValue && TransitionTarget.HasValue)
            {
                double progress = 1 - TransitionRemaining / TransitionDuration;
                double eased = progress * progress * (3 - 2 * progress);
                var start = _transitionStart.Value;
                var target = TransitionTarget.Value;
                WorldX = (float)(start.X + (target.X - start.X) * eased);
                WorldY = (float)(start.Y + (target.Y - start.Y) * eased);
                if (TransitionRemaining <= 0)
                {
                    WorldX = target.X;
                    WorldY = target.Y;
                    _transitionStart = null;
                    TransitionTarget = null;
                }
            }
            PhaseAnnouncementTimer = Math.Max(0.0, PhaseAnnouncementTimer - dt);
            return;
        }
        if (IsStaggered)
            return;
        if (StaggerRecoveryRemaining > 0)
        {
            double progress = 1 - StaggerRecoveryRemaining;
            for (int index = 0; index < ProjectilePortals.Count; index++)
                ProjectilePortals[index].ShowTether = index < (int)(progress * ProjectilePortals.Count) + 1;
            return;
        }
        foreach (var portal in ProjectilePortals)
            portal.ShowTether = true;
        PhaseElapsed += dt;
        var stagedThreats = new List<EnemyProjectile>();
        UpdateBossBursts(stagedThreats, dt);
        PhaseAnnouncementTimer = Math.Max(0.0, PhaseAnnouncementTimer - dt);
        if (!DebugPhaseLocked && !SurvivalActive)
        {
            int? survivalPhase = HealthUnlockedSurvival();
            if (survivalPhase.HasValue)
            {
                SetPhase(survivalPhase.Value);
                stagedThreats.Clear();
            }
            else if (PhaseElapsed >= PhaseTimeLimit && DamagePhaseSet.Contains(Phase) &&
                PhaseDeclarations >= MinimumDamagePhaseDeclarations)
            {
                SetPhase(ChooseDamagePhase());
                PhaseForcedByTimer = true;
                stagedThreats.Clear();
            }
        }
        if (TransitionRemaining > 0)
            return;

        if (SurvivalActive)
        {
            SurvivalRemaining = Math.Max(0.0, SurvivalRemaining - dt);
            if (SurvivalRemaining <= 0)
            {
                SurvivalActive = false;
                if (Phase < 9)
                {
                    NextSurvivalPhase = Phase == 3 ? 6 : 9;
                    SetPhase(ChooseDamagePhase());
                    return;
                }
                BeginDeath();
                return;
            }
        }
        if (RuneSilenceRemaining > 0)
            return;

        if (SurvivalActive)
        {
            PhaseMovementStep(context.PlayerWorldX, context.PlayerWorldY, dt, context.Battleground);
            foreach (var portal in ProjectilePortals)
            {
                portal.Angle += portal.AngularSpeed * (float)dt;
                portal.Place();
                portal.UpdateBursts(stagedThreats, (float)dt);
            }
            UpdateSurvivalFormation(dt);
            foreach (var portal in SurvivalPortals)
                portal.UpdateBursts(stagedThreats, (float)dt);
            SurvivalBarrage(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt);
            UpdateSpecialAttacks(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt);
            CommitStagedThreats(context.ProjectileSink, stagedThreats);
            return;
        }

        PhaseMovementStep(context.PlayerWorldX, context.PlayerWorldY, dt, context.Battleground);
        int queuedThreatCount = stagedThreats.Count;
        switch (Phase)
        {
            case 1: PhaseOne(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt); break;
            case 2: PhaseCrossfireCarousel(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt); break;
            case 3: PhaseClosingSpiral(stagedThreats, dt); break;
            case 4: PhaseTwo(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt, context.Battleground); break;
            case 5: PhaseMirrorStep(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt, context.Battleground); break;
            case 6: PhasePortalRelay(stagedThreats, dt); break;
            case 7: PhaseThree(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt); break;
            case 8: PhaseEventHorizon(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt); break;
            default: PhaseLastWord(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt); break;
        }
        UpdateSpecialAttacks(context.PlayerWorldX, context.PlayerWorldY, stagedThreats, dt);
        bool committed = CommitStagedThreats(context.ProjectileSink, stagedThreats);
        if (committed && stagedThreats.Count > queuedThreatCount && _declarationCooldown <= 0)
        {
            PhaseDeclarations += 1;
            _declarationCooldown = .9;
        }
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var hitboxes = base.GetScreenHitboxes(camera, playerWorldPosition, screenShake).ToList();
        if (SurvivalActive)
            return hitboxes;
        for (int index = 0; index < ProjectilePortals.Count; index++)
        {
            var portal = ProjectilePortals[index];
            if (portal.BlocksShots)
            {
                var screenPosition = camera.WorldToScreen(new Vector2(portal.WorldX, portal.WorldY), playerWorldPosition, screenShake);
                hitboxes.Add(($"portal:{index}", new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)portal.Size, (int)portal.Size)));
            }
        }
        return hitboxes;
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetWorldHitboxes()
    {
        var hitboxes = base.GetWorldHitboxes().ToList();
        if (SurvivalActive)
            return hitboxes;
        for (int index = 0; index < ProjectilePortals.Count; index++)
        {
            var portal = ProjectilePortals[index];
            if (portal.BlocksShots)
                hitboxes.Add(($"portal:{index}", new Rectangle((int)portal.WorldX, (int)portal.WorldY, (int)portal.Size, (int)portal.Size)));
        }
        return hitboxes;
    }

    /// <summary>Stable hooks for future achievements, drops, and hard-mode unlocks.</summary>
    public IReadOnlyDictionary<string, bool> ChallengeResults() => new Dictionary<string, bool>
    {
        ["no_portals_broken"] = PortalsBroken == 0,
        ["unbroken_pressure"] = !StaggerEverDecayed,
        ["rune_interrupter"] = RunesInterrupted >= 3,
        ["perfect_breaker"] = PerfectStaggers >= 3,
    };

    /// <summary>
    /// Takes age/phase explicitly rather than reading this.Age/this.Phase --
    /// Age is privately set on the base Enemy class (every enemy type's
    /// invariant), and this keeps the rotating-cube math a pure function
    /// the Python test suite's `boss.age = 80; boss.phase = 7` pattern can
    /// still be exercised against, just via parameters instead of mutation.
    /// </summary>
    public (Vector3[] Vertices, int[][] Faces) CubeGeometry(Vector2 center, float extent, float age, int phase)
    {
        double transition = phase > 1 ? Math.Max(0.0, 1.0 - PhaseElapsed) : 0.0;
        double staggerWobble = IsStaggered ? Math.Sin(age * .09) * .12 : 0.0;
        double yaw = age * (.0075 + phase * .00055) + transition * transition * Math.PI + staggerWobble;
        double pitch = .42 + Math.Sin(age * (.0055 + phase * .0002)) * .16 + transition * .22;
        float[,] corners =
        {
            { -1, -1, -1 }, { 1, -1, -1 }, { 1, 1, -1 }, { -1, 1, -1 },
            { -1, -1, 1 }, { 1, -1, 1 }, { 1, 1, 1 }, { -1, 1, 1 },
        };
        var vertices = new Vector3[8];
        for (int index = 0; index < 8; index++)
        {
            float x = corners[index, 0], y = corners[index, 1], z = corners[index, 2];
            double rotatedX = x * Math.Cos(yaw) + z * Math.Sin(yaw);
            double rotatedZ = -x * Math.Sin(yaw) + z * Math.Cos(yaw);
            double rotatedY = y * Math.Cos(pitch) - rotatedZ * Math.Sin(pitch);
            rotatedZ = y * Math.Sin(pitch) + rotatedZ * Math.Cos(pitch);
            double perspective = 3.8 / (3.8 - rotatedZ);
            vertices[index] = new Vector3(
                (float)(center.X + rotatedX * extent * perspective),
                (float)(center.Y + rotatedY * extent * perspective),
                (float)rotatedZ);
        }
        int[][] faces =
        {
            new[] { 0, 1, 2, 3 }, new[] { 4, 7, 6, 5 }, new[] { 0, 4, 5, 1 },
            new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 5, 6, 2 },
        };
        var sortedFaces = faces.OrderBy(face => face.Average(i => vertices[i].Z)).ToArray();
        return (vertices, sortedFaces);
    }

    private void DrawCubeAura(SpriteBatch spriteBatch, Vector2 center, Color color)
    {
        double transition = Phase > 1 ? Math.Max(0.0, 1.0 - PhaseElapsed) : 0.0;
        float beat = (float)((1 + Math.Sin(Age * .035) * .055) * (1 + transition * .22));
        for (int index = 0; index < 3; index++)
        {
            float width = Size * (1.18f + index * .18f) * beat;
            float height = Size * (.56f + index * .1f) * beat;
            var arcRect = new Rectangle((int)(center.X - width / 2f), (int)(center.Y - height / 2f), (int)width, (int)height);
            float start = Age * (.012f + index * .004f) * (index % 2 == 1 ? -1f : 1f);
            Primitives2D.Arc(spriteBatch, arcRect, start, start + MathF.PI * 1.18f, UiTheme.Ink, Math.Max(4, (int)(Size * .065f)));
            Primitives2D.Arc(spriteBatch, arcRect, start, start + MathF.PI * 1.18f, color, Math.Max(1, (int)(Size * .022f)));
        }

        int shardCount = 3;
        float orbit = Age * .006f;
        for (int index = 0; index < shardCount; index++)
        {
            float angle = orbit + index * 2f * MathF.PI / shardCount;
            float distance = Size * (.67f + .08f * MathF.Sin(Age * .025f + index));
            float shardX = center.X + MathF.Cos(angle) * distance;
            float shardY = center.Y + MathF.Sin(angle) * distance * .48f;
            float shardSize = Size * (.055f + .012f * MathF.Sin(Age * .04f + index));
            var points = new[]
            {
                new Vector2(shardX, shardY - shardSize * 1.5f), new Vector2(shardX + shardSize, shardY),
                new Vector2(shardX, shardY + shardSize * 1.5f), new Vector2(shardX - shardSize, shardY),
            };
            Primitives2D.FillPolygon(spriteBatch, points, UiTheme.Ink);
            var inner = points.Select(p => new Vector2(center.X + (p.X - center.X) * .82f, center.Y + (p.Y - center.Y) * .82f)).ToArray();
            Primitives2D.FillPolygon(spriteBatch, inner, color);
        }

        if (transition > 0)
        {
            float burstRadius = Size * (1.35f - (float)transition * .65f);
            Primitives2D.CircleOutline(spriteBatch, center, burstRadius, color, Math.Max(1, (int)(Size * .025f)));
        }

        if (IsStaggered)
        {
            for (int index = 0; index < 4; index++)
            {
                float angle = Age * .04f + index * MathF.PI / 2f;
                var start = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Size * .48f;
                var middle = center + new Vector2(MathF.Cos(angle + .18f), MathF.Sin(angle + .18f)) * Size * .64f;
                var end = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Size * .78f;
                Primitives2D.Polyline(spriteBatch, new[] { start, middle, end }, false, UiTheme.Cream, 2);
            }
        }

        if (StaggerRecoveryRemaining > 0)
        {
            float recoveryRadius = Size * (1.55f - (float)StaggerRecoveryRemaining * .55f);
            Primitives2D.CircleOutline(spriteBatch, center, recoveryRadius, UiTheme.Cream, Math.Max(2, (int)(Size * .035f)));
        }
    }

    /// <summary>Layer translucent interpolated echoes behind Dissonance's live cube.</summary>
    private void DrawMotionTrail(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        foreach (var ghost in MotionTrail)
        {
            float alpha = (float)Math.Pow(ghost.Life / .52, 2) * 72f;
            if (alpha <= 2)
                continue;
            var screenPos = camera.WorldToScreen(new Vector2(ghost.X, ghost.Y), playerWorldPosition, screenShake);
            float radius = Size * (.19f + .035f * MotionTrail.IndexOf(ghost) / Math.Max(1, MotionTrail.Count));
            var center = new Vector2(screenPos.X + Size / 2f, screenPos.Y + Size / 2f);
            Primitives2D.CircleOutline(spriteBatch, center, radius, ghost.Accent * (alpha / 255f), Math.Max(2, (int)(radius * .18f)));
            Primitives2D.CircleOutline(spriteBatch, center, Math.Max(2, radius * .38f), UiTheme.Cream * (alpha / 255f / 2f), 2);
        }
    }

    private void DrawArenaInscription(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        int runePhase = Dying || (Phase == 9 && SurvivalActive) ? 9 : Phase;
        var strokes = PhaseRunes[runePhase].Strokes;
        var center = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        float radius = Simulation.TileSize * ArenaFormationScale * (3.2f + .25f * MathF.Sin(Age * .01f));
        for (int strokeIndex = 0; strokeIndex < strokes.Length; strokeIndex++)
        {
            var stroke = strokes[strokeIndex];
            var points = stroke.Select(p => center + p * radius).ToArray();
            if (points.Length <= 1)
                continue;
            int pulse = Math.Max(1, (int)(2 + 2 * (1 + Math.Sin(Age * .025 + strokeIndex))));
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Shadow, 14);
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Ink, 8);
            Primitives2D.Polyline(spriteBatch, points, false, PhaseAccent, pulse);
            int segment = (int)(Age * .018 + strokeIndex) % (points.Length - 1);
            float travel = (Age * .018f + strokeIndex) % 1f;
            var sparkPos = points[segment] + (points[segment + 1] - points[segment]) * travel;
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)sparkPos.X - 4, (int)sparkPos.Y - 4, 8, 8), UiTheme.Cream);
        }
    }

    private void DrawMiniRune(SpriteBatch spriteBatch, Vector2 center, float radius, int runePhase, Color color)
    {
        foreach (var stroke in PhaseRunes[runePhase].Strokes)
        {
            var points = stroke.Select(point => center + point * radius).ToArray();
            if (points.Length <= 1)
                continue;
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Ink * .8f, 6);
            Primitives2D.Polyline(spriteBatch, points, false, color, 2);
        }
    }

    /// <summary>
    /// Jera resolves the preceding eight runes into a visible grand staff:
    /// five steady lines hold the arena while remembered rune-chords illuminate
    /// around its edge and measured wavefronts expand through the final phrase.
    /// The construction is cosmetic and consumes no hostile-projectile budget.
    /// </summary>
    private void DrawJeraGrandStaff(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        int chordCount = JeraChordRingCount;
        if (chordCount == 0)
            return;

        var center = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        float rotation = MathF.Sin(Age * .0035f) * .08f;
        var along = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
        var normal = new Vector2(-along.Y, along.X);
        for (int line = -2; line <= 2; line++)
        {
            float offset = line * Simulation.TileSize * .72f;
            float halfLength = MathF.Sqrt(MathF.Max(0, ArenaRadius * ArenaRadius - offset * offset)) * .94f;
            var midpoint = center + normal * offset;
            Primitives2D.Line(spriteBatch, midpoint - along * halfLength, midpoint + along * halfLength,
                UiTheme.Ink * .72f, 7);
            Primitives2D.Line(spriteBatch, midpoint - along * halfLength, midpoint + along * halfLength,
                (line == 0 ? UiTheme.Cream : PhaseAccent) * .42f, line == 0 ? 2 : 1);
        }

        double progress = Math.Clamp(1.0 - SurvivalRemaining / SurvivalDuration, 0.0, 1.0);
        for (int ring = 0; ring < chordCount; ring++)
        {
            double cycle = (progress * .8 + ring / (double)MaximumJeraChordRings) % 1.0;
            float radius = ArenaRadius * (.18f + .72f * (float)cycle);
            float alpha = .16f + .28f * (1f - (float)cycle);
            Primitives2D.CircleOutline(spriteBatch, center, radius,
                (ring % 2 == 0 ? UiTheme.Red : UiTheme.Cream) * alpha, ring % 3 == 0 ? 3 : 2);
        }

        float runeOrbit = ArenaRadius * .78f;
        for (int index = 0; index < chordCount; index++)
        {
            float angle = -MathF.PI / 2f + index * MathF.Tau / MaximumJeraChordRings + Age * .0015f;
            var runeCenter = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * runeOrbit;
            var nextAngle = -MathF.PI / 2f + ((index + 1) % MaximumJeraChordRings) *
                MathF.Tau / MaximumJeraChordRings + Age * .0015f;
            var next = center + new Vector2(MathF.Cos(nextAngle), MathF.Sin(nextAngle)) * runeOrbit;
            Primitives2D.Line(spriteBatch, runeCenter, next, PhaseAccent * .28f, 2);
            Primitives2D.CircleOutline(spriteBatch, runeCenter, Simulation.TileSize * .43f, UiTheme.Ink, 6);
            DrawMiniRune(spriteBatch, runeCenter, Simulation.TileSize * .62f, index + 1,
                index == chordCount - 1 ? UiTheme.Cream : PhaseAccent * .8f);
        }
    }

    private double PhaseTimerRatio()
    {
        if (TransitionRemaining > 0)
            return 0.0;
        if (SurvivalActive)
        {
            double duration = Phase == 9 ? SurvivalDuration : 20.0;
            return Math.Clamp(SurvivalRemaining / duration, 0.0, 1.0);
        }
        return Math.Clamp(1.0 - PhaseElapsed / PhaseTimeLimit, 0.0, 1.0);
    }

    private void DrawArenaBoundary(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var center = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        float radius = ArenaRadius;
        float timerRadius = radius + 22;
        var timerRect = new Rectangle((int)(center.X - timerRadius), (int)(center.Y - timerRadius), (int)(timerRadius * 2), (int)(timerRadius * 2));
        Primitives2D.CircleOutline(spriteBatch, center, timerRadius, UiTheme.Shadow, 12);
        Primitives2D.CircleOutline(spriteBatch, center, timerRadius, UiTheme.Ink, 7);
        if (TransitionRemaining <= 0)
        {
            double timerRatio = PhaseTimerRatio();
            if (timerRatio > 0)
            {
                float start = -MathF.PI / 2f;
                float end = start + 2f * MathF.PI * (float)timerRatio;
                Primitives2D.Arc(spriteBatch, timerRect, start, end, PhaseAccent, 7);
                var tip = center + new Vector2(MathF.Cos(end), MathF.Sin(end)) * timerRadius;
                Primitives2D.FillCircle(spriteBatch, tip, 5, UiTheme.Cream);
            }
        }
        Primitives2D.CircleOutline(spriteBatch, center, radius + 14, UiTheme.Shadow, 26);
        Primitives2D.CircleOutline(spriteBatch, center, radius + 5, UiTheme.Ink, 14);
        Primitives2D.CircleOutline(spriteBatch, center, radius + 2, PhaseAccent, 6);
        Primitives2D.CircleOutline(spriteBatch, center, radius - 5, UiTheme.Cream, 2);
        for (int index = 0; index < 24; index++)
        {
            float angle = index * 2f * MathF.PI / 24f + Age * .0015f;
            float inner = radius - (index % 3 != 0 ? 8 : 15);
            var start = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * inner;
            var end = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            Primitives2D.Line(spriteBatch, start, end, index % 3 != 0 ? PhaseAccent : UiTheme.Cream, 2);
        }
        for (int index = 0; index < 8; index++)
        {
            float angle = Age * .012f + index * MathF.PI / 4f;
            var packetCenter = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var packet = new Rectangle((int)packetCenter.X - 5, (int)packetCenter.Y - 5, 10, 10);
            var inflated = packet; inflated.Inflate(4, 4);
            Primitives2D.FillRect(spriteBatch, inflated, UiTheme.Ink);
            Primitives2D.FillRect(spriteBatch, packet, PhaseAccent);
        }
        float waveRadius = radius - Age * .8f % 34f;
        Primitives2D.CircleOutline(spriteBatch, center, waveRadius, PhaseAccent, 1);
        for (int ringIndex = 0; ringIndex < 3; ringIndex++)
        {
            var points = new Vector2[64];
            for (int step = 0; step < 64; step++)
            {
                float angle = step * 2f * MathF.PI / 64f + Age * (.004f + ringIndex * .0015f);
                float ripple = MathF.Sin(angle * (3 + ringIndex) + Age * .025f) * (5 + ringIndex * 3);
                float ringRadius = radius + ripple - ringIndex * 7;
                points[step] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ringRadius;
            }
            Primitives2D.PolygonOutline(spriteBatch, points, ringIndex != 1 ? PhaseAccent : UiTheme.Cream, 2);
        }
        for (int index = 0; index < 18; index++)
        {
            float angle = Age * .018f + index * 2f * MathF.PI / 18f;
            float drift = MathF.Sin(Age * .03f + index * 1.7f) * 10;
            var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius + drift);
            Primitives2D.FillCircle(spriteBatch, point, 2 + index % 3, PhaseAccent);
        }
    }

    private void DrawArenaMask(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var center = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        var vertices = new Vector2[64];
        for (int index = 0; index < 64; index++)
        {
            float angle = index * 2f * MathF.PI / 64f;
            vertices[index] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (ArenaRadius + 8);
        }
        Primitives2D.DrawOutsideArena(spriteBatch, center, vertices);
    }

    private void DrawDeathSpectacle(SpriteBatch spriteBatch, Vector2 center)
    {
        if (!Dying)
            return;
        double progress = 1 - DeathRemaining / DeathDuration;
        int beamCount = 3 + (int)(progress * 5);
        float arenaRadius = ArenaRadius * (1.05f + .08f * MathF.Sin(Age * .02f));
        for (int index = 0; index < beamCount; index++)
        {
            float angle = Age * (.011f + index * .0007f) + index * 2f * MathF.PI / beamCount;
            var end = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * arenaRadius;
            int width = 2 + (int)(4 * Math.Abs(MathF.Sin(Age * .035f + index)));
            Primitives2D.Line(spriteBatch, center, end, UiTheme.Ink, width + 6);
            Primitives2D.Line(spriteBatch, center, end, PhaseAccent, width);
            Primitives2D.Line(spriteBatch, center, end, UiTheme.Cream, Math.Max(1, width / 3));
        }
        for (int ringIndex = 0; ringIndex < 5; ringIndex++)
        {
            double cycle = (progress * 4 + ringIndex / 5.0) % 1;
            float radius = Size * (.4f + (float)cycle * 4.2f);
            Color color = ringIndex % 2 != 0 ? UiTheme.Cream : PhaseAccent;
            Primitives2D.CircleOutline(spriteBatch, center, radius, color, Math.Max(1, (int)(5 * (1 - cycle))));
        }
        for (int index = 0; index < 24; index++)
        {
            float angle = index * 2f * MathF.PI / 24f + Age * .019f;
            double distance = Size * (.7 + (progress * 6 + index * .17) % 4);
            var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (float)distance;
            int size = 4 + index % 3;
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)point.X - 2, (int)point.Y - 2, size, size),
                index % 3 == 0 ? UiTheme.Cream : PhaseAccent);
        }
    }

    private string DrawRune(SpriteBatch spriteBatch, Vector2 center, float radius, int? runePhaseOverride = null)
    {
        int runePhase = runePhaseOverride ?? Phase;
        var (runeName, strokes) = PhaseRunes[runePhase];
        double transition = Math.Max(0.0, .75 - PhaseElapsed) / .75;
        float angle = (float)(transition * Math.PI * 1.5) + MathF.Sin(Age * .015f) * .035f;
        float pulse = 1 + MathF.Sin(Age * .04f) * .06f;
        float cosAngle = MathF.Cos(angle), sinAngle = MathF.Sin(angle);
        int glowWidth = Math.Max(6, (int)(radius * .16f));
        int lineWidth = Math.Max(3, (int)(radius * .075f));
        foreach (var stroke in strokes)
        {
            var points = stroke.Select(p =>
            {
                float x = p.X * radius * pulse, y = p.Y * radius * pulse;
                return center + new Vector2(x * cosAngle - y * sinAngle, x * sinAngle + y * cosAngle);
            }).ToArray();
            if (points.Length <= 1)
                continue;
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Ink, glowWidth);
            if (transition > 0)
            {
                float ghostOffset = radius * (float)transition * .12f;
                var ghostOffsetVec = new Vector2(MathF.Cos(Age * .05f), MathF.Sin(Age * .05f)) * ghostOffset;
                var ghost = points.Select(p => p + ghostOffsetVec).ToArray();
                Primitives2D.Polyline(spriteBatch, ghost, false, PhaseAccent, Math.Max(2, lineWidth / 2));
            }
            Primitives2D.Polyline(spriteBatch, points, false, PhaseAccent, lineWidth);
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Cream, Math.Max(1, lineWidth / 3));
        }
        return runeName;
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawArenaMask(spriteBatch, camera, playerWorldPosition, screenShake);
        DrawArenaBoundary(spriteBatch, camera, playerWorldPosition, screenShake);
        DrawJeraGrandStaff(spriteBatch, camera, playerWorldPosition, screenShake);
        DrawArenaInscription(spriteBatch, camera, playerWorldPosition, screenShake);
        DrawMotionTrail(spriteBatch, camera, playerWorldPosition, screenShake);
        foreach (var particle in VisualParticles)
        {
            var screenPos = camera.WorldToScreen(new Vector2(particle.X, particle.Y), playerWorldPosition, screenShake);
            int size = Math.Max(2, (int)(particle.Size * Math.Min(1, particle.Life * 2)));
            var pixel = new Rectangle((int)screenPos.X / 2 * 2, (int)screenPos.Y / 2 * 2, size, size);
            var inflated = pixel; inflated.Inflate(2, 2);
            Primitives2D.FillRect(spriteBatch, inflated, UiTheme.Ink);
            Primitives2D.FillRect(spriteBatch, pixel, particle.Color);
        }
        if (TransitionRemaining <= 0)
        {
            foreach (var portal in ProjectilePortals)
                portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
            foreach (var portal in SurvivalPortals)
                portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        }
        if (RuneCannonCharge > 0 && RuneCannonReceiver.HasValue && RuneCannonReceiver.Value < ProjectilePortals.Count)
        {
            var receiver = ProjectilePortals[RuneCannonReceiver.Value];
            var target = camera.WorldToScreen(new Vector2(receiver.WorldX + receiver.Size / 2f, receiver.WorldY + receiver.Size / 2f), playerWorldPosition, screenShake);
            foreach (var portal in ProjectilePortals)
            {
                if (portal != receiver && portal.Active)
                {
                    var start = camera.WorldToScreen(new Vector2(portal.WorldX + portal.Size / 2f, portal.WorldY + portal.Size / 2f), playerWorldPosition, screenShake);
                    Primitives2D.Line(spriteBatch, start, target, PhaseAccent, 2);
                }
            }
        }

        float bob = MathF.Sin(Age * .055f) * (JumpWindup <= 0 ? 3 : 8);
        var screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)(screenPosition.Y + bob), (int)Size, (int)Size);
        DrawDeathSpectacle(spriteBatch, rect.Center.ToVector2());

        Color color;
        if (HitFlash > 0)
            color = UiTheme.Text;
        else if (IsStaggered)
        {
            color = (int)(StaggerRemaining * 8) % 2 == 0 ? UiTheme.Cream : UiTheme.Muted;
            rect.Inflate(-(int)(Size * .12f), (int)(Size * .1f));
            rect.Offset((int)(MathF.Sin(Age * .31f) * 4), (int)(MathF.Cos(Age * .27f) * 3));
        }
        else if (Phase <= 3)
            color = PhaseAccent;
        else if (Phase == 4)
        {
            bool blink = (int)(PhaseElapsed * 8) % 2 == 0;
            color = blink || PhaseElapsed >= 1.6 ? UiTheme.Red : UiTheme.Cream;
        }
        else if (Phase is 5 or 6 or 7 or 8)
            color = PhaseAccent;
        else
            color = UiTheme.Red;

        if (JumpWindup > 0)
            rect.Inflate((int)(Size * .16f), -(int)(Size * .18f));
        else if (JumpRecovery > 0)
            rect.Inflate(-(int)(Size * .12f), (int)(Size * .16f));

        var rectCenter = rect.Center.ToVector2();
        DrawCubeAura(spriteBatch, rectCenter, PhaseAccent);
        // Four gently depth-scaled satellites make the oldest core feel composed:
        // its power is ordered around it rather than sprayed outward as debris.
        float orbitSpread = SurvivalActive ? 1.35f : 1f;
        BossVisuals.OrbitingCubes(spriteBatch, rectCenter, Age, OrbitingCubeCount, Size * .78f, Size * .16f,
            new Color(105, 75, 196), new Color(64, 142, 214), orbitSpread, .28f, frontLayer: false);
        var (vertices, faces) = CubeGeometry(rectCenter, Size * .43f, Age, Phase);
        double entranceSpread = Math.Max(0.0, EntranceRemaining / EntranceDuration) * 2.8;
        double deathProgress = Dying ? Math.Max(0.0, 1 - DeathRemaining / DeathBurstDuration) : 0.0;
        double deathSpread = deathProgress * 3.4;
        double faceSpread = Math.Max(entranceSpread, deathSpread);
        var projectedFaces = new List<Vector2[]>();
        foreach (var face in faces)
        {
            var points = face.Select(index => new Vector2(vertices[index].X, vertices[index].Y)).ToArray();
            if (faceSpread > 0)
            {
                float faceCenterX = points.Average(p => p.X), faceCenterY = points.Average(p => p.Y);
                float offsetX = (faceCenterX - rectCenter.X) * (float)faceSpread;
                float offsetY = (faceCenterY - rectCenter.Y) * (float)faceSpread;
                points = points.Select(p => new Vector2(p.X + offsetX, p.Y + offsetY)).ToArray();
            }
            projectedFaces.Add(points);
        }
        foreach (var points in projectedFaces)
            Primitives2D.FillPolygon(spriteBatch, points.Select(p => new Vector2(p.X + 7, p.Y + 9)).ToArray(), UiTheme.Shadow);
        for (int faceIndex = 0; faceIndex < projectedFaces.Count; faceIndex++)
        {
            var points = projectedFaces[faceIndex];
            int shimmer = (int)(8 * (1 + Math.Sin(Age * .025 + faceIndex * 1.7)));
            Color ancestralFace = faceIndex % 2 == 0 ? new Color(91, 62, 181) : new Color(51, 120, 198);
            var faceColor = HitFlash > 0 || IsStaggered
                ? UiTheme.Lighten(color, 5 + faceIndex * 6 + shimmer)
                : UiTheme.Lighten(Color.Lerp(ancestralFace, PhaseAccent, .12f), 5 + faceIndex * 5 + shimmer);
            Primitives2D.FillPolygon(spriteBatch, points, faceColor);
            Primitives2D.PolygonOutline(spriteBatch, points, UiTheme.Ink, Math.Max(3, (int)(Size * .045f)));
            Primitives2D.Line(spriteBatch, points[0], points[1], UiTheme.Lighten(PhaseAccent, 35), Math.Max(1, (int)(Size * .012f)));
        }

        double coreVisibility = Math.Max(.15, 1 - entranceSpread * .22);
        float coreRadius = Size * (float)((.31 + Math.Sin(Age * .025) * .015) * coreVisibility);
        var core = new Rectangle((int)(rectCenter.X - coreRadius * 1.55f / 2f), (int)(rectCenter.Y - coreRadius * 1.55f / 2f), (int)(coreRadius * 1.55f), (int)(coreRadius * 1.55f));
        var coreInflated = core; coreInflated.Inflate(8, 8);
        Primitives2D.FillRect(spriteBatch, coreInflated, UiTheme.Ink);
        Primitives2D.FillRect(spriteBatch, core, UiTheme.Void);
        Primitives2D.RectOutline(spriteBatch, core, PhaseAccent, Math.Max(2, (int)(Size * .035f)));
        float pulseScale = .28f + .06f * MathF.Sin(Age * .045f);
        var innerPulse = new Rectangle((int)(rectCenter.X - core.Width * pulseScale / 2f),
            (int)(rectCenter.Y - core.Height * pulseScale / 2f), Math.Max(2, (int)(core.Width * pulseScale)),
            Math.Max(2, (int)(core.Height * pulseScale)));
        Primitives2D.RectOutline(spriteBatch, innerPulse, PhaseAccent, 1);
        int deathRune = Dying || (Phase == 9 && SurvivalActive) ? 9 : Phase;
        string runeName = DrawRune(spriteBatch, rectCenter, coreRadius, deathRune);
        UiTheme.DrawText(spriteBatch, runeName, Math.Max(8, Size * .075), PhaseAccent,
            new Vector2(rect.Center.X, core.Bottom + Size * .045f), "midtop");
        BossVisuals.OrbitingCubes(spriteBatch, rectCenter, Age, OrbitingCubeCount, Size * .78f, Size * .16f,
            new Color(105, 75, 196), new Color(64, 142, 214), orbitSpread, .28f, frontLayer: true);

        if (PhaseAnnouncementTimer > 0)
            DrawPhaseAnnouncement(spriteBatch, rect);
        if (ActTransitionTimer > 0)
            DrawActTransition(spriteBatch);
        if (PerfectBreakFlash > 0)
            DrawPerfectBreak(spriteBatch);
    }

    private void DrawPerfectBreak(SpriteBatch spriteBatch)
    {
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        float alpha = (float)Math.Min(1.0, PerfectBreakFlash * 2) * 150f;
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color(12, 14, 18) * (alpha / 255f));
        var center = new Vector2(viewport.Width / 2f, viewport.Height * .46f);
        UiTheme.DrawText(spriteBatch, "PERFECT BREAK", 36, UiTheme.Ink, center + new Vector2(5, 6), "center");
        UiTheme.DrawText(spriteBatch, "PERFECT BREAK", 36, UiTheme.Cream, center, "center");
        float width = viewport.Width * .34f * (float)PerfectBreakFlash;
        Primitives2D.Line(spriteBatch, center + new Vector2(-width, 34), center + new Vector2(width, 34), PhaseAccent, 4);
    }

    private void DrawActTransition(SpriteBatch spriteBatch)
    {
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        double progress = 1 - ActTransitionTimer / 2.2;
        float alpha = (float)Math.Min(1.0, Math.Min(progress * 5, (1 - progress) * 5)) * 185f;
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, (int)(viewport.Height * .3f), viewport.Width, (int)(viewport.Height * .4f)), UiTheme.Void * (alpha / 255f));
        int jitter = (int)Age % 4 == 0 ? 2 : 0;
        UiTheme.DrawText(spriteBatch, ActTitle, 31, UiTheme.Ink, new Vector2(viewport.Width / 2f + 4, viewport.Height * .43f + 5), "center");
        UiTheme.DrawText(spriteBatch, ActTitle, 31, PhaseAccent, new Vector2(viewport.Width / 2f + jitter, viewport.Height * .43f), "center");
        string runeName = PhaseRunes[Phase].Name;
        UiTheme.DrawText(spriteBatch, $"{runeName} AWAKENS", 13, UiTheme.Cream, new Vector2(viewport.Width / 2f, viewport.Height * .51f), "center");
    }

    private void DrawPhaseAnnouncement(SpriteBatch spriteBatch, Rectangle bossRect)
    {
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        string runeName = PhaseRunes[Phase].Name.ToUpperInvariant();
        var runeFont = UiTheme.Font(13);
        var phaseFont = UiTheme.Font(13);
        float labelWidth = runeFont.MeasureString(runeName).X + phaseFont.MeasureString($"  {PhaseLabel}").X;
        float labelHeight = Math.Max(runeFont.MeasureString(runeName).Y, phaseFont.MeasureString($"  {PhaseLabel}").Y);

        var flavorFont = UiTheme.Font(16);
        float maxTextWidth = Math.Min(viewport.Width * .68f, 680f);
        var words = PhaseFlavor.Split(' ');
        var lines = new List<string>();
        string current = "";
        foreach (var word in words)
        {
            string candidate = (current + " " + word).Trim();
            if (current.Length > 0 && flavorFont.MeasureString(candidate).X > maxTextWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0 || lines.Count == 0)
            lines.Add(current);

        float widestFlavor = lines.Max(line => flavorFont.MeasureString(line).X);
        int lineGap = 2;
        float flavorHeight = lines.Sum(line => flavorFont.MeasureString(line).Y) + lineGap * (lines.Count - 1);
        float width = Math.Max(labelWidth, widestFlavor) + 28;
        float height = labelHeight + flavorHeight + 22;
        var bubble = new Rectangle(0, 0, (int)width, (int)height);
        bubble.X = bossRect.Center.X - bubble.Width / 2;
        bubble.Y = bossRect.Top - 18 - bubble.Height;
        var bounds = viewport.Bounds;
        bounds.Inflate(-12, -12);
        bubble.X = Math.Clamp(bubble.X, bounds.X, Math.Max(bounds.X, bounds.Right - bubble.Width));
        bubble.Y = Math.Clamp(bubble.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - bubble.Height));

        UiTheme.DrawPanel(spriteBatch, bubble, UiTheme.PanelRaised, PhaseAccent, shadow: 4);
        UiTheme.DrawText(spriteBatch, runeName, 13, UiTheme.Cream, new Vector2(bubble.Center.X - phaseFont.MeasureString($"  {PhaseLabel}").X / 2f, bubble.Y + 7), "midtop", bold: true);
        UiTheme.DrawText(spriteBatch, $"  {PhaseLabel}", 13, UiTheme.Cream, new Vector2(bubble.Center.X + runeFont.MeasureString(runeName).X / 2f, bubble.Y + 7), "midtop");
        float flavorY = bubble.Bottom - 7 - flavorHeight;
        foreach (var line in lines)
        {
            UiTheme.DrawText(spriteBatch, line, 16, UiTheme.Text, new Vector2(bubble.Center.X, flavorY), "midtop");
            flavorY += flavorFont.MeasureString(line).Y + lineGap;
        }
        var pointer = new[]
        {
            new Vector2(bossRect.Center.X - 7, bubble.Bottom), new Vector2(bossRect.Center.X + 7, bubble.Bottom),
            new Vector2(bossRect.Center.X, bubble.Bottom + 10),
        };
        Primitives2D.FillPolygon(spriteBatch, pointer, PhaseAccent);
    }
}
