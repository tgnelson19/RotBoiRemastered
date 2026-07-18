using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Per-family commandment-sigil content: flavor/color text, which shared
/// commandment sigil each phase displays, and act-transition metadata.
/// Ported from bossTypes.py's `PhantasiaBoss`/`Hypno`/`Malady` class
/// attributes (`phaseFlavors`, `phaseColors`, `phaseSigils`, `ACT_METADATA`).
/// Unlike `SinSigilConfig` (where each boss owns its own full sigil stroke
/// data), `phaseSigils` here is a list of indices into the single shared
/// `PhantasiaBoss.CommandmentSigils` array -- matching Python's module-level
/// `COMMANDMENT_SIGILS` tuple every subclass indexes into.
/// </summary>
public sealed record PhantasiaSigilConfig(
    IReadOnlyList<string> PhaseFlavors,
    IReadOnlyList<Color> PhaseColors,
    IReadOnlyList<int> PhaseSigils,
    IReadOnlyDictionary<int, string> ActMetadata);

/// <summary>An unclaimed final-phase reward. Ported from bossTypes.py's per-instance `offeringPositions` dict literals.</summary>
public sealed class Offering
{
    public float X;
    public float Y;
    public string Name = "";
    public bool Taken;
}

/// <summary>
/// Commandment-driven dream court shared by Hypno and Malady. Ported from
/// bossTypes.py's `PhantasiaBoss`.
///
/// Integrates with `RunState.DreamState`'s belief/truth mechanics via
/// illusion-vs-truth-marked shots (see `ShotFrom`'s `illusion` parameter)
/// and direct `DreamState.AlterBelief` calls for rule violations (the
/// Sabbath/REST phase) and offering pickups (the final phase). Both need a
/// live `DreamState`, so `EnemyUpdateContext` gained a nullable
/// `DreamState` field (and a nullable `PlayerBullets` list, for the REST
/// phase's "did the player fire" check) -- see that type's doc comment.
///
/// `_draw_dream_court`'s belief-driven intensity read
/// (`cS.dreamState["belief"]`) is an implicit global read even inside
/// Python's *draw* method. Rather than give `Draw` an implicit dependency
/// on `RunState` (breaking this port's established Update-mutates/
/// Draw-only-reads split), <see cref="UpdateSpecialRules"/> caches the
/// current belief into <see cref="CurrentBelief"/> each Update tick, and
/// every Draw-side belief read uses that cached value instead.
/// </summary>
public abstract class PhantasiaBoss : PathChaseBoss
{
    /// <summary>Ported from bossTypes.py's module-level `COMMANDMENT_SIGILS` -- shared by every `PhantasiaBoss` subclass, indexed via `PhantasiaSigilConfig.PhaseSigils`.</summary>
    public static readonly IReadOnlyList<(string Name, Vector2[][] Strokes)> CommandmentSigils = new (string, Vector2[][])[]
    {
        ("AUTHORITY", new[]
        {
            new[] { new Vector2(0, -.78f), new Vector2(0, .72f) },
            new[] { new Vector2(-.62f, -.26f), new Vector2(0, -.78f), new Vector2(.62f, -.26f) },
            new[] { new Vector2(-.48f, .5f), new Vector2(0, .72f), new Vector2(.48f, .5f) },
        }),
        ("IMAGE", new[]
        {
            new[] { new Vector2(-.7f, 0), new Vector2(0, -.62f), new Vector2(.7f, 0), new Vector2(0, .62f), new Vector2(-.7f, 0) },
            new[] { new Vector2(-.34f, 0), new Vector2(0, -.28f), new Vector2(.34f, 0), new Vector2(0, .28f), new Vector2(-.34f, 0) },
        }),
        ("REVERENCE", new[]
        {
            new[]
            {
                new Vector2(-.55f, .66f), new Vector2(-.55f, -.5f), new Vector2(0, -.76f),
                new Vector2(.55f, -.5f), new Vector2(.55f, .66f),
            },
            new[] { new Vector2(-.28f, -.1f), new Vector2(0, -.38f), new Vector2(.28f, -.1f) },
            new[] { new Vector2(0, -.38f), new Vector2(0, .48f) },
        }),
        ("REST", new[]
        {
            new[] { new Vector2(-.7f, -.45f), new Vector2(.7f, -.45f) },
            new[] { new Vector2(-.7f, .45f), new Vector2(.7f, .45f) },
            new[] { new Vector2(-.52f, -.45f), new Vector2(-.52f, .45f) },
            new[] { new Vector2(.52f, -.45f), new Vector2(.52f, .45f) },
        }),
        ("LINEAGE", new[]
        {
            new[] { new Vector2(0, .75f), new Vector2(0, -.18f), new Vector2(-.58f, -.7f) },
            new[] { new Vector2(0, -.18f), new Vector2(.58f, -.7f) },
            new[] { new Vector2(-.58f, -.7f), new Vector2(-.22f, -.7f) },
            new[] { new Vector2(.58f, -.7f), new Vector2(.22f, -.7f) },
        }),
        ("MERCY", new[]
        {
            new[] { new Vector2(-.65f, -.58f), new Vector2(0, .68f), new Vector2(.65f, -.58f) },
            new[] { new Vector2(-.42f, -.22f), new Vector2(0, .15f), new Vector2(.42f, -.22f) },
            new[] { new Vector2(0, -.72f), new Vector2(0, .15f) },
        }),
        ("FIDELITY", new[]
        {
            new[] { new Vector2(-.5f, -.68f), new Vector2(-.5f, .68f), new Vector2(.5f, .68f), new Vector2(.5f, -.68f) },
            new[] { new Vector2(-.5f, 0), new Vector2(.5f, 0) },
        }),
        ("OWNERSHIP", new[]
        {
            new[] { new Vector2(-.7f, -.5f), new Vector2(0, -.12f), new Vector2(.7f, -.5f) },
            new[] { new Vector2(-.7f, .5f), new Vector2(0, .12f), new Vector2(.7f, .5f) },
            new[] { new Vector2(0, -.72f), new Vector2(0, .72f) },
        }),
        ("TRUTH", new[]
        {
            new[]
            {
                new Vector2(-.72f, 0), new Vector2(0, -.5f), new Vector2(.72f, 0),
                new Vector2(0, .5f), new Vector2(-.72f, 0),
            },
            new[] { new Vector2(0, -.68f), new Vector2(0, .68f) },
            new[] { new Vector2(-.18f, 0), new Vector2(.18f, 0) },
        }),
        ("CONTENTMENT", new[]
        {
            new[]
            {
                new Vector2(-.68f, -.28f), new Vector2(-.3f, .58f), new Vector2(0, .72f),
                new Vector2(.3f, .58f), new Vector2(.68f, -.28f),
            },
            new[] { new Vector2(-.68f, -.28f), new Vector2(-.3f, -.62f) },
            new[] { new Vector2(.68f, -.28f), new Vector2(.3f, -.62f) },
            new[] { new Vector2(-.3f, .1f), new Vector2(.3f, .1f) },
        }),
    };

    public static readonly PathChaseBossConfig BaseConfig = PathChaseBossConfig.Default with
    {
        ArenaShape = "atomic", ArenaScale = 10.8, MovementModes = new[] { "chase", "path", "static" },
    };

    protected PhantasiaSigilConfig SigilConfig { get; }

    public int PatternRotation { get; protected set; }
    public double ActTransitionTimer { get; protected set; }
    public double ActTransitionDuration { get; } = 2.4;
    public string ActTitle { get; protected set; } = "";
    public double PhaseProtectionTimer { get; protected set; }
    public int PreviousSigilPhase { get; protected set; } = 1;
    public double SigilTransitionTimer { get; protected set; } = 1.35;
    public double SigilTransitionDuration { get; } = 1.35;
    public bool RestActive { get; protected set; }
    public bool RestViolationLatched { get; protected set; }
    public HashSet<string> AcceptedOfferings { get; } = new();
    public List<Offering> OfferingPositions { get; } = new();
    public string RuleText { get; protected set; } = "THE SIGIL SPEAKS TRUE";
    public bool RuleTruth { get; protected set; } = true;
    public int TruthIndex { get; protected set; }
    public double PeakBelief { get; protected set; }
    public double PhaseAnnouncementTimer { get; protected set; } = 3.0;

    /// <summary>Cached each Update tick from `context.DreamState.Belief` -- see this class's doc comment.</summary>
    protected double CurrentBelief { get; private set; }

    protected PhantasiaBoss(float worldX, float worldY, Battleground battleground,
        PathChaseBossConfig config, PhantasiaSigilConfig sigilConfig, Random? rng = null)
        : base(worldX, worldY, battleground, config, rng)
    {
        SigilConfig = sigilConfig;
        PhaseFlavor = sigilConfig.PhaseFlavors[0];
        PhaseAccent = sigilConfig.PhaseColors[0];
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked)
            return;
        int count = Config.PhaseLabels.Count;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int newPhase = Math.Min(count, (int)((1.0 - ratio) * count + 1e-9) + 1);
        if (newPhase != Phase)
            SetDreamPhase(newPhase);
    }

    protected virtual void SetDreamPhase(int phase)
    {
        PreviousSigilPhase = Phase;
        Phase = Math.Clamp(phase, 1, Config.PhaseLabels.Count);
        PhaseLabel = Config.PhaseLabels[Phase - 1];
        PhaseFlavor = SigilConfig.PhaseFlavors[Phase - 1];
        PhaseAccent = SigilConfig.PhaseColors[Phase - 1];
        PhaseElapsed = 0.0;
        SigilTransitionTimer = SigilTransitionDuration;
        PhaseAnnouncementTimer = 3.2;
        AttackCooldown = Math.Min(AttackCooldown!.Value, Simulation.FrameRate * .4f);
        TransitionCleanupRequested = true;
        RestActive = false;
        RestViolationLatched = false;
        RuleTruth = true;
        if (SigilConfig.ActMetadata.TryGetValue(Phase, out var title))
        {
            ActTitle = title;
            ActTransitionTimer = ActTransitionDuration;
            PhaseProtectionTimer = ActTransitionDuration;
        }
        if (Phase == Config.PhaseLabels.Count)
            PlaceOfferings();
    }

    public override void DebugSetPhase(int phase)
    {
        SetDreamPhase(phase);
        DebugPhaseLocked = true;
        AttackCooldown = 0f;
    }

    protected void PlaceOfferings()
    {
        var center = Center();
        string[] names = { "POWER", "HASTE", "LIFE", "MULTITUDE" };
        OfferingPositions.Clear();
        for (int index = 0; index < 4; index++)
        {
            float angle = index * MathF.PI / 2f;
            OfferingPositions.Add(new Offering
            {
                X = center.X + MathF.Cos(angle) * Simulation.TileSize * 4.8f,
                Y = center.Y + MathF.Sin(angle) * Simulation.TileSize * 4.8f,
                Name = names[index], Taken = false,
            });
        }
    }

    /// <summary>
    /// `sizeScale` exists for Malady's flowing-chain shots (bossTypes.py's
    /// `_update_sequences` mutates `shot.size *= .82` right after
    /// construction) -- baked into construction here instead of exposing a
    /// settable `EnemyProjectile.Size` for one caller.
    /// </summary>
    protected EnemyProjectile ShotFrom(List<EnemyProjectile> sink, Vector2 origin, float direction, float speed, float damage, string suffix,
        bool illusion = false, string shape = "diamond", string path = "linear", Color? color = null, double belief = .45, double clarity = 0.0,
        float sizeScale = 1.0f)
    {
        float size = Size * (Config.FinalBoss ? .19f : .17f) * sizeScale;
        var shot = new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f, direction, speed, damage, size,
            travelRange: Simulation.TileSize * 34f, color: color ?? PhaseAccent, shape: shape, path: path,
            amplitude: path == "sine" ? Simulation.TileSize * .58f : 0f,
            owner: $"{Config.OwnerPrefix}_{suffix}", ignoreWalls: true)
        {
            Illusory = illusion, TruthMarked = !illusion, BeliefGain = illusion ? 0.0 : belief, ClarityGain = clarity,
        };
        sink.Add(shot);
        return shot;
    }

    protected void FanFrom(List<EnemyProjectile> sink, Vector2 origin, Vector2 target, int count, float spread, float speed, float damage,
        string suffix, bool illusion = false, string path = "linear", double belief = .45)
    {
        float baseDirection = MathF.Atan2(target.Y - origin.Y, target.X - origin.X);
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            ShotFrom(sink, origin, baseDirection + offset, speed, damage, suffix, illusion: illusion, path: path, belief: belief);
        }
    }

    protected void RadialFrom(List<EnemyProjectile> sink, Vector2 origin, int count, float speed, float damage, string suffix, bool illusion = false)
    {
        for (int index = 0; index < count; index++)
            ShotFrom(sink, origin, index * 2f * MathF.PI / count + PatternRotation * .13f, speed, damage, suffix, illusion: illusion);
    }

    protected void LaserFrom(List<EnemyProjectile> sink, Vector2 origin, float direction, float damage, string suffix, bool illusion = false)
    {
        var laser = new EnemyProjectile(origin.X, origin.Y, direction, 0f, damage, Size * .13f,
            travelRange: Simulation.TileSize * 35f, color: illusion ? UiTheme.Muted : PhaseAccent, shape: "laser", path: "laser",
            lifetime: 2.5f, owner: $"{Config.OwnerPrefix}_{suffix}", ignoreWalls: true)
        {
            TelegraphDuration = 1.05f, Illusory = illusion, TruthMarked = !illusion, BeliefGain = illusion ? 0.0 : .7,
        };
        sink.Add(laser);
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (ActTransitionTimer > 0 || PhaseProtectionTimer > 0)
            return new HitResult(false, false, 0, true);
        int previousHp = Hp;
        var result = base.TakeDamage(amount, partId, source);
        if (!DebugPhaseLocked && Phase < Config.PhaseLabels.Count)
        {
            double gate = MaxHp * (double)(Config.PhaseLabels.Count - Phase) / Config.PhaseLabels.Count;
            Hp = Math.Max(Hp, (int)gate);
        }
        return new HitResult(result.Applied, Hp <= 0, Math.Max(0, previousHp - Hp), result.Blocked);
    }

    /// <summary>
    /// Belief/rest-phase/offering-pickup bookkeeping. Ported from
    /// _update_special_rules. A null `context.DreamState` (a caller that
    /// doesn't care about this subsystem) is a documented no-op, same
    /// reasoning as `Rot.UpdateTerrain`'s null `BossAfflictions` guard.
    /// </summary>
    protected virtual void UpdateSpecialRules(float playerX, float playerY, double dt, EnemyUpdateContext context)
    {
        if (context.DreamState is null)
            return;
        CurrentBelief = context.DreamState.Belief;
        PeakBelief = Math.Max(PeakBelief, CurrentBelief);
        if (Config.FinalBoss && Phase == 4)
        {
            RestActive = PhaseElapsed % 7.0 >= 5.4;
            if (RestActive && context.PlayerBullets.Count > 0 && !RestViolationLatched)
            {
                context.DreamState.AlterBelief(1.25, falseRule: true);
                RestViolationLatched = true;
            }
            else if (!RestActive)
            {
                RestViolationLatched = false;
            }
        }
        if (Config.FinalBoss && Phase == Config.PhaseLabels.Count)
        {
            float tile = Simulation.TileSize;
            foreach (var offering in OfferingPositions)
            {
                if (offering.Taken)
                    continue;
                float dx = playerX - offering.X, dy = playerY - offering.Y;
                if (MathF.Sqrt(dx * dx + dy * dy) <= tile * .9f)
                {
                    offering.Taken = true;
                    AcceptedOfferings.Add(offering.Name);
                    context.DreamState.AlterBelief(1.2, falseRule: true);
                }
            }
        }
    }

    protected abstract void FirePhantasiaPattern(float playerX, float playerY, EnemyUpdateContext context);

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        ActTransitionTimer = Math.Max(0.0, ActTransitionTimer - dt);
        PhaseProtectionTimer = Math.Max(0.0, PhaseProtectionTimer - dt);
        SigilTransitionTimer = Math.Max(0.0, SigilTransitionTimer - dt);
        PhaseAnnouncementTimer = Math.Max(0.0, PhaseAnnouncementTimer - dt);
        PhaseElapsed += dt;
        UpdatePhase();
        UpdateSpecialRules(context.PlayerWorldX, context.PlayerWorldY, dt, context);
        if (EntranceRemaining > 0 || ActTransitionTimer > 0)
        {
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
            effectivePlayerX = ArenaCenter.X + MathF.Sin((float)PhaseElapsed * .62f) * ArenaRadius * .58f;
            effectivePlayerY = ArenaCenter.Y + MathF.Sin((float)PhaseElapsed * 1.24f) * ArenaRadius * .32f;
        }
        var effectiveContext = mode == "chase" ? context : new EnemyUpdateContext
        {
            PlayerWorldX = effectivePlayerX, PlayerWorldY = effectivePlayerY, Battleground = context.Battleground,
            ProjectileSink = context.ProjectileSink, AllEnemies = context.AllEnemies, ExperienceBubbles = context.ExperienceBubbles,
            Camera = context.Camera, BossAfflictions = context.BossAfflictions, PlayerBuildSnapshot = context.PlayerBuildSnapshot,
            PlayerBullets = context.PlayerBullets, DreamState = context.DreamState,
        };
        ChaseUpdate(effectiveContext);
        Speed = originalSpeed;
        AttackCooldown -= (float)Simulation.GetTimerStep();
        if (RestActive)
            return;
        if (AttackCooldown <= 0)
        {
            FirePhantasiaPattern(effectivePlayerX, effectivePlayerY, context);
            double rate = Math.Max(.34, 1.0 - .055 * (Phase - 1));
            AttackCooldownMax ??= Simulation.FrameRate * (float)(Config.FinalBoss ? Config.FinalCooldownSeconds : Config.CooldownSeconds);
            AttackCooldown = AttackCooldownMax.Value * (float)(rate * (.88 + Rng.NextDouble() * .2));
        }
    }

    /// <summary>
    /// Ported from _draw_commandment_sigil. Unlike SinChemesthesisBoss's
    /// DrawSigil (progressive per-stroke *segment* reveal), Python reveals
    /// this family's sigils by a coarser whole-vertex budget
    /// (`points[:count]`) -- ported faithfully as the simpler reveal, not
    /// upgraded to match the other family's smoother one.
    /// </summary>
    protected string DrawCommandmentSigil(SpriteBatch spriteBatch, Vector2 center, float radius, double progress = 1.0,
        int? phase = null, int alpha = 255, double rotation = 0.0)
    {
        if (SigilConfig.PhaseSigils.Count == 0)
            return "";
        int phaseValue = phase ?? Phase;
        int sigilIndex = SigilConfig.PhaseSigils[Math.Clamp(phaseValue - 1, 0, SigilConfig.PhaseSigils.Count - 1)];
        var (name, strokes) = CommandmentSigils[sigilIndex];
        double budget = Math.Clamp(progress, 0.0, 1.0) * strokes.Length;
        float cosAngle = MathF.Cos((float)rotation), sinAngle = MathF.Sin((float)rotation);
        int width = Math.Max(2, (int)(radius * .08f));
        byte a = (byte)Math.Clamp(alpha, 0, 255);
        var inkAlpha = new Color(UiTheme.Ink.R, UiTheme.Ink.G, UiTheme.Ink.B, a);
        var accentAlpha = new Color(PhaseAccent.R, PhaseAccent.G, PhaseAccent.B, a);
        var creamAlpha = new Color(UiTheme.Cream.R, UiTheme.Cream.G, UiTheme.Cream.B, a);

        for (int strokeIndex = 0; strokeIndex < strokes.Length; strokeIndex++)
        {
            var stroke = strokes[strokeIndex];
            double amount = Math.Clamp(budget - strokeIndex, 0.0, 1.0);
            if (amount <= 0)
                continue;
            var points = new Vector2[stroke.Length];
            for (int index = 0; index < stroke.Length; index++)
            {
                float x = stroke[index].X * radius, y = stroke[index].Y * radius;
                points[index] = center + new Vector2(x * cosAngle - y * sinAngle, x * sinAngle + y * cosAngle);
            }
            int count = Math.Max(2, Math.Min(points.Length, (int)(amount * (points.Length - 1)) + 2));
            var visible = points.Take(count).ToArray();
            Primitives2D.Polyline(spriteBatch, visible, false, inkAlpha, width + 7);
            Primitives2D.Polyline(spriteBatch, visible, false, accentAlpha, width);
            Primitives2D.Polyline(spriteBatch, visible, false, creamAlpha, Math.Max(1, width / 3));
        }
        return name;
    }

    protected virtual void DrawDreamCourt(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var center = camera.WorldToScreen(Center(), playerWorldPosition, screenShake);
        float tile = Simulation.TileSize;
        float chaos = Config.FinalBoss ? (float)(1.0 + CurrentBelief * .055) : 1.0f;
        int ringCount = Config.FinalBoss ? 5 : 3;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float radius = tile * (2.1f + ring * 1.22f + MathF.Sin(Age * .018f + ring) * .18f) * chaos;
            var rect = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2), (int)(radius * 2));
            float start = Age * (.0018f + ring * .0007f) * (ring % 2 == 1 ? -1f : 1f);
            Color ringColor = Color.Lerp(PhaseAccent, UiTheme.Void, .82f - ring * .025f);
            int segments = 8 + ring * 2;
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = start + segment * 2f * MathF.PI / segments;
                Primitives2D.Arc(spriteBatch, rect, angle, angle + MathF.PI / (9 + ring), ringColor, 2 + ring % 2);
            }
        }
        Color moteColor = Color.Lerp(PhaseAccent, UiTheme.Void, .67f);
        int moteCount = (Config.FinalBoss ? 8 : 4) + (int)(CurrentBelief * .5);
        for (int index = 0; index < moteCount; index++)
        {
            float angle = index * 2.399f + Age * .0025f;
            float radius = tile * (1.7f + index % 5);
            var point = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            float size = 5 + index % 4;
            Primitives2D.FillPolygon(spriteBatch, new[]
            {
                new Vector2(point.X, point.Y - size), new Vector2(point.X + size, point.Y),
                new Vector2(point.X, point.Y + size), new Vector2(point.X - size, point.Y),
            }, moteColor);
        }
        double progress = 1 - SigilTransitionTimer / SigilTransitionDuration;
        DrawCommandmentSigil(spriteBatch, center, tile * (Config.FinalBoss ? 2.0f : 1.45f), progress, Phase, 62, Age * .0007);
    }

    protected virtual void DrawMaskAndHalos(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        int haloCount = Config.FinalBoss ? 10 : 5;
        for (int index = 0; index < haloCount; index++)
        {
            float angle = Age * (.005f + index * .0003f) + index * 2f * MathF.PI / haloCount;
            float radius = Size * (.72f + .08f * MathF.Sin(Age * .02f + index));
            var point = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * .62f);
            Primitives2D.FillCircle(spriteBatch, point, Math.Max(3, (int)(Size * .045f)), UiTheme.Ink);
            Primitives2D.FillCircle(spriteBatch, point, Math.Max(2, (int)(Size * .025f)), PhaseAccent);
        }
        var mask = rect;
        mask.Inflate(-(int)(Size * .26f), -(int)(Size * .12f));
        var maskOuter = mask;
        maskOuter.Inflate(8, 8);
        Primitives2D.FillEllipse(spriteBatch, maskOuter, UiTheme.Ink);
        Primitives2D.FillEllipse(spriteBatch, mask, UiTheme.Cream);
        Primitives2D.Arc(spriteBatch, mask, MathF.PI, 2f * MathF.PI, PhaseAccent, Math.Max(3, (int)(Size * .06f)));
        float eyeY = mask.Y + mask.Height * .42f;
        foreach (int side in new[] { -1, 1 })
        {
            var eye = new Vector2(mask.Center.X + side * mask.Width * .2f, eyeY);
            Primitives2D.Line(spriteBatch, eye - new Vector2(7, 0), eye + new Vector2(7, 0), UiTheme.Ink, 4);
            Primitives2D.FillCircle(spriteBatch, eye, 3, PhaseAccent);
        }
        double transition = SigilTransitionTimer / SigilTransitionDuration;
        DrawCommandmentSigil(spriteBatch, center, Size * .3f, Math.Max(.05, 1 - transition), null, 255, transition * Math.PI);
    }

    protected virtual void DrawActTransition(SpriteBatch spriteBatch)
    {
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        double progress = 1 - ActTransitionTimer / ActTransitionDuration;
        int alpha = (int)Math.Clamp(210 * Math.Min(1.0, Math.Min(progress * 6, (1 - progress) * 6)), 0, 255);
        var curtainColor = new Color(42, 13, 52, alpha);
        int curtain = (int)(viewport.Width * Math.Min(.5, progress * .7));
        Primitives2D.FillRect(spriteBatch, new Rectangle(0, 0, curtain, viewport.Height), curtainColor);
        Primitives2D.FillRect(spriteBatch, new Rectangle(viewport.Width - curtain, 0, curtain, viewport.Height), curtainColor);
        UiTheme.DrawText(spriteBatch, ActTitle, 34, PhaseAccent, new Vector2(viewport.Width / 2f, viewport.Height * .4f), "center");
        string name = DrawCommandmentSigil(spriteBatch, new Vector2(viewport.Width / 2f, viewport.Height * .55f), 44, Math.Min(1.0, progress * 2.5), null, 255, 0.0);
        UiTheme.DrawText(spriteBatch, name, 11, UiTheme.Cream, new Vector2(viewport.Width / 2f, viewport.Height * .65f), "center");
    }

    /// <summary>Ported from `getattr(self, "_draw_dream_body", None)`: only Malady defines a fully custom body, replacing the generic arena+ellipse+mask rendering entirely.</summary>
    protected virtual bool HasCustomDreamBody => false;

    protected virtual void DrawDreamBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawDreamCourt(spriteBatch, camera, playerWorldPosition, screenShake);
        if (!HasCustomDreamBody)
        {
            base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
            DrawMaskAndHalos(spriteBatch, camera, playerWorldPosition, screenShake);
        }
        else
        {
            DrawPathArena(spriteBatch, camera, playerWorldPosition, screenShake);
            DrawDreamBody(spriteBatch, camera, playerWorldPosition, screenShake);
        }

        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        int ghostCount = (int)(CurrentBelief / 2);
        for (int index = 0; index < ghostCount; index++)
        {
            float angle = Age * (.006f + index * .001f) + index * 2.1f;
            float offset = Size * (1.0f + index * .22f);
            var ghostCenter = new Vector2(rect.Center.X + MathF.Cos(angle) * offset, rect.Center.Y + MathF.Sin(angle) * offset * .55f);
            var ghost = new Rectangle((int)(ghostCenter.X - Size * .21f), (int)(ghostCenter.Y - Size * .29f), (int)(Size * .42f), (int)(Size * .58f));
            var ghostOuter = ghost;
            ghostOuter.Inflate(5, 5);
            Primitives2D.EllipseOutline(spriteBatch, ghostOuter, UiTheme.Ink, 3);
            Primitives2D.EllipseOutline(spriteBatch, ghost, UiTheme.Muted, 2);
        }

        var viewport = spriteBatch.GraphicsDevice.Viewport;
        if ((!Config.FinalBoss && Phase == 2) || (Config.FinalBoss && (Phase == 3 || Phase == 9)))
        {
            int width = Math.Min((int)(viewport.Width * .48f), 620);
            var banner = new Rectangle(viewport.Width / 2 - width / 2, (int)(viewport.Height * .13f), width, 42);
            Color ruleColor = RuleTruth ? UiTheme.Cream : PhaseAccent;
            UiTheme.DrawPanel(spriteBatch, banner, UiTheme.PanelRaised, ruleColor, shadow: 5);
            UiTheme.DrawText(spriteBatch, RuleText, 11, ruleColor, new Vector2(banner.Center.X, banner.Center.Y), "center");
        }

        if (PhaseAnnouncementTimer > 0 && ActTransitionTimer <= 0)
        {
            int width = Math.Min((int)(viewport.Width * .56f), 680);
            var banner = new Rectangle(viewport.Width / 2 - width / 2, viewport.Height - 32 - 66, width, 66);
            UiTheme.DrawPanel(spriteBatch, banner, UiTheme.Panel, PhaseAccent, shadow: 6);
            string sigilName = CommandmentSigils[SigilConfig.PhaseSigils[Phase - 1]].Name;
            UiTheme.DrawText(spriteBatch, $"{sigilName} // {PhaseLabel}", 13, PhaseAccent, new Vector2(banner.Center.X, banner.Y + 12), "midtop");
            UiTheme.DrawText(spriteBatch, PhaseFlavor, 9, UiTheme.Cream, new Vector2(banner.Center.X, banner.Bottom - 12), "midbottom");
        }

        if (Config.FinalBoss && Phase == Config.PhaseLabels.Count)
        {
            float tile = Simulation.TileSize;
            foreach (var offering in OfferingPositions)
            {
                if (offering.Taken)
                    continue;
                var point = camera.WorldToScreen(new Vector2(offering.X, offering.Y), playerWorldPosition, screenShake);
                Primitives2D.FillCircle(spriteBatch, point, tile * .38f, UiTheme.Ink);
                Primitives2D.CircleOutline(spriteBatch, point, tile * .3f, PhaseAccent, 4);
                UiTheme.DrawText(spriteBatch, offering.Name, 8, UiTheme.Cream, new Vector2(point.X, point.Y + tile * .48f), "center");
            }
        }

        if (RestActive)
            UiTheme.DrawText(spriteBatch, "REST // DO NOT FIRE", 16, UiTheme.Cream, new Vector2(viewport.Width / 2f, viewport.Height * .18f), "center");

        if (ActTransitionTimer > 0)
            DrawActTransition(spriteBatch);
    }

    /// <summary>Ported from challenge_results(). Takes DreamState explicitly (unlike Rot.ChallengeResults()) since false-rule count lives there, not cached on the boss.</summary>
    public IReadOnlyDictionary<string, bool> ChallengeResults(DreamState dreamState) => new Dictionary<string, bool>
    {
        ["unbelieving"] = PeakBelief <= 3.0,
        ["true_witness"] = dreamState.FalseRules == 0,
        ["content"] = AcceptedOfferings.Count == 0,
        ["measured_desire"] = AcceptedOfferings.Count == 1,
    };
}
