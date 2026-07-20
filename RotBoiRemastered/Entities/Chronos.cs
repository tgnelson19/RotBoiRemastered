using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Sight's usurper and king of attrition. Chronos is deliberately slower than Ishe: every attack first
/// draws its complete segmented route, then the whole laser-tentacle strikes.
/// The encounter has three opening lessons, an invulnerable half-health exam,
/// two heavy damage movements, and a forty-second King's Attrition finale.
/// </summary>
public sealed class Chronos : Ishe
{
    public const int AmbientMoteCount = 15;
    public const int FinaleMoteCount = 24;
    private static readonly IReadOnlyDictionary<int, (string Label, string Flavor, Color Accent)> PhaseMetadata =
        new Dictionary<int, (string, string, Color)>
        {
            [1] = ("DIRECTIVE", "The pale cube declares one route. Obey the line.", new Color(102, 198, 230)),
            [2] = ("CROSSCUT", "Two futures close; the unmarked road remains.", new Color(238, 170, 75)),
            [3] = ("WEARYING LASH", "The warning bends slowly enough to exhaust the eye.", new Color(117, 164, 232)),
            [4] = ("STILL SECOND", "Time stops. Reaction dulls. The declared routes remain.", UiTheme.Cream),
            [5] = ("PARALLAX", "Stand between the futures the usurper has already shown.", new Color(91, 191, 218)),
            [6] = ("THORN OF TIME", "Valia fell to one impossible line. It is declared long before it strikes.", new Color(235, 125, 72)),
            [7] = ("KING'S ATTRITION", "Forty seconds of prediction against a foe who only needs one hit.", new Color(244, 186, 82)),
        };

    public static readonly PathChaseBossConfig ChronosConfig = IsheConfig with
    {
        BossName = "CHRONOS", Subtitle = "THE KING OF ATTRITION", FinalBoss = true,
        OwnerPrefix = "chronos_sight",
        PhaseLabels = PhaseMetadata.OrderBy(pair => pair.Key).Select(pair => pair.Value.Label).ToArray(),
        FinalBodyColor = new Color(101, 190, 228), FinalAccentColor = new Color(203, 239, 250),
        FinalBodyScale = 1.75, FinalCooldownSeconds = 2.15,
        FinalShotSpeed = .42, FinalShotDamage = 760, FinalShotScale = .22,
        MovementSpeed = .12, ArenaScale = 11.8,
        MovementModes = new[] { "path", "static", "path", "static", "path", "static", "static" },
        FinalHealth = 240000, FinalContactDamage = 780, FinalRewardExperience = 820,
    };

    public bool MidpointSurvivalActive { get; private set; }
    public bool MidpointSurvivalCleared { get; private set; }
    public double MidpointSurvivalDuration { get; } = 20.0;
    public double MidpointSurvivalRemaining { get; private set; }
    public int PatternRotation { get; private set; }
    private double _survivalCooldown;

    public Chronos(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, ChronosConfig, rng)
    {
        ApplyPhase(1);
    }

    private void ApplyPhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, PhaseMetadata.Count);
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[Phase];
        PhaseElapsed = 0.0;
        VisualTransitionRemaining = 1.4;
        AttackCooldown = Math.Min(AttackCooldown ?? 0f, Simulation.FrameRate * .6f);
        TransitionCleanupRequested = true;
    }

    private void BeginMidpointSurvival()
    {
        if (MidpointSurvivalActive || MidpointSurvivalCleared)
            return;
        Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
        ApplyPhase(4);
        MidpointSurvivalActive = true;
        MidpointSurvivalRemaining = MidpointSurvivalDuration;
        _survivalCooldown = .35;
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked || FinaleActive || MidpointSurvivalActive)
            return;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int desired;
        if (!MidpointSurvivalCleared)
        {
            if (ratio <= .5)
            {
                BeginMidpointSurvival();
                return;
            }
            desired = ratio > .84 ? 1 : ratio > .67 ? 2 : 3;
        }
        else
        {
            desired = ratio > .25 ? 5 : 6;
        }
        if (desired != Phase)
            ApplyPhase(desired);
    }

    public override void DebugSetPhase(int phase)
    {
        phase = Math.Clamp(phase, 1, 7);
        DebugPhaseLocked = true;
        MidpointSurvivalActive = false;
        if (phase >= 5)
            MidpointSurvivalCleared = true;
        ApplyPhase(phase);
        AttackCooldown = 0f;
        if (phase == 4)
        {
            MidpointSurvivalActive = true;
            MidpointSurvivalRemaining = MidpointSurvivalDuration;
            _survivalCooldown = 0;
        }
        else if (phase == 7 && !FinaleActive)
        {
            BeginFinaleSequence();
        }
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (MidpointSurvivalActive || FinaleActive || Dying)
            return new HitResult(false, false, 0, true);

        if (!MidpointSurvivalCleared)
        {
            double floorRatio = Phase switch { 1 => .84, 2 => .67, _ => .50 };
            int floor = Math.Max(1, (int)Math.Round(MaxHp * floorRatio));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                BeginMidpointSurvival();
                return new HitResult(false, false, 0, true);
            }
            var result = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            if (Hp <= MaxHp * .5)
                BeginMidpointSurvival();
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }

        if (Phase == 5)
        {
            int floor = Math.Max(1, (int)Math.Round(MaxHp * .25));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                UpdatePhase();
                return new HitResult(false, false, 0, true);
            }
            var gated = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }

        var finalResult = base.TakeDamage(amount, partId, source);
        if (FinaleActive)
            ApplyPhase(7);
        return finalResult;
    }

    private void Tentacle(List<EnemyProjectile> sink, float baseDirection, float bend, float damage,
        string suffix, float telegraph = 1.45f, int segments = 6, float segmentTiles = 2.15f)
    {
        Vector2 origin = Center();
        float segmentLength = Simulation.TileSize * segmentTiles;
        for (int segment = 0; segment < segments; segment++)
        {
            float fraction = segment / (float)Math.Max(1, segments - 1);
            float direction = baseDirection
                + MathF.Sin(fraction * MathF.PI * 1.35f + PatternRotation * .53f) * bend
                + (fraction - .5f) * bend * .35f;
            float width = Size * (.075f + segment * .006f);
            var laser = new EnemyProjectile(origin.X, origin.Y, direction, 0f, damage, width,
                travelRange: segmentLength, color: PhaseAccent, shape: "laser", path: "laser",
                lifetime: telegraph + .72f, owner: $"chronos_{suffix}_segment_{segment}", ignoreWalls: true)
            {
                TelegraphDuration = telegraph,
            };
            sink.Add(laser);
            origin += new Vector2(MathF.Cos(direction), MathF.Sin(direction)) * segmentLength;
        }
    }

    private void DirectivePair(float playerX, float playerY, List<EnemyProjectile> sink, bool crossed)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        float opening = crossed ? .52f : .34f;
        Tentacle(sink, aimed - opening, crossed ? .42f : .22f, crossed ? 820 : 730,
            crossed ? "crosscut_left" : "directive_left", telegraph: crossed ? 1.65f : 1.5f);
        Tentacle(sink, aimed + opening, crossed ? -.42f : -.22f, crossed ? 820 : 730,
            crossed ? "crosscut_right" : "directive_right", telegraph: crossed ? 1.65f : 1.5f);
    }

    private void RadialTentacles(float playerX, float playerY, List<EnemyProjectile> sink, int count, int gaps,
        float bend, float damage, string suffix, float telegraph = 1.55f)
    {
        var center = Center();
        float playerAngle = MathF.Atan2(playerY - center.Y, playerX - center.X);
        float step = MathF.Tau / count;
        int gapIndex = (int)MathF.Round(((playerAngle % MathF.Tau + MathF.Tau) % MathF.Tau) / step) % count;
        for (int index = 0; index < count; index++)
        {
            int distance = Math.Min((index - gapIndex + count) % count, (gapIndex - index + count) % count);
            if (distance < gaps)
                continue;
            float direction = index * step + PatternRotation * .17f;
            Tentacle(sink, direction, index % 2 == 0 ? bend : -bend, damage, $"{suffix}_{index}", telegraph, segments: 5);
        }
    }

    private void FireSurvivalPattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        RadialTentacles(playerX, playerY, sink, 10, 2, .34f, 760, "still_second", 1.7f);
        PatternRotation++;
    }

    private void ThornOfTime(float playerX, float playerY, List<EnemyProjectile> sink, string suffix = "thorn_of_time")
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        // The fabled strike is the encounter's most lethal attack, but it is
        // also its fairest: one complete eight-segment route is visible for
        // over two seconds and never retargets after being declared.
        Tentacle(sink, aimed, PatternRotation % 2 == 0 ? .075f : -.075f, 1260, suffix,
            telegraph: 2.35f, segments: 8, segmentTiles: 2.45f);
    }

    protected override void FirePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        switch (Phase)
        {
            case 1:
                DirectivePair(playerX, playerY, sink, crossed: false);
                break;
            case 2:
                DirectivePair(playerX, playerY, sink, crossed: true);
                break;
            case 3:
            {
                var center = Center();
                float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
                Tentacle(sink, aimed - .72f, .68f, 850, "oracle_outer_left", 1.7f, 7);
                Tentacle(sink, aimed + .72f, -.68f, 850, "oracle_outer_right", 1.7f, 7);
                Tentacle(sink, aimed + MathF.PI, PatternRotation % 2 == 0 ? .55f : -.55f, 810, "oracle_rear", 1.7f, 6);
                break;
            }
            case 5:
                DirectivePair(playerX, playerY, sink, crossed: PatternRotation % 2 == 0);
                Tentacle(sink, PatternRotation * .71f, .76f, 870, "parallax_flail", 1.8f, 7);
                break;
            case 6:
                if (PatternRotation % 2 == 0)
                    RadialTentacles(playerX, playerY, sink, 12, 2, .48f, 910, "thorn_crown", 1.8f);
                else
                    ThornOfTime(playerX, playerY, sink);
                break;
            default:
            {
                int movement = PatternRotation % 4;
                if (movement == 0)
                    DirectivePair(playerX, playerY, sink, crossed: true);
                else if (movement == 1)
                    RadialTentacles(playerX, playerY, sink, 12, 2, .55f, 930, "attrition_crown", 1.6f);
                else if (movement == 2)
                {
                    var center = Center();
                    float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
                    for (int index = -1; index <= 1; index++)
                        Tentacle(sink, aimed + MathF.PI + index * .7f, (index == 0 ? 1 : index) * .62f,
                            920, $"attrition_lash_{index + 1}", 1.55f, 7);
                }
                else
                    ThornOfTime(playerX, playerY, sink, "attrition_thorn");
                break;
            }
        }
        PatternRotation++;
        MarkAttack(.82f);
    }

    public override void Update(EnemyUpdateContext context)
    {
        if (!MidpointSurvivalActive)
        {
            base.Update(context);
            return;
        }

        double dt = Seconds();
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        PhaseElapsed += dt;
        AdvanceAge();
        MidpointSurvivalRemaining = Math.Max(0.0, MidpointSurvivalRemaining - dt);
        _survivalCooldown -= dt;
        if (EntranceRemaining <= 0 && _survivalCooldown <= 0)
        {
            FireSurvivalPattern(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            _survivalCooldown = 2.45;
        }
        if (MidpointSurvivalRemaining <= 0 && !DebugPhaseLocked)
        {
            MidpointSurvivalActive = false;
            MidpointSurvivalCleared = true;
            Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
            ApplyPhase(5);
        }
    }

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        Vector2 center = screen + new Vector2(Size / 2f, Size / 2f);
        if (Dying)
        {
            BossVisuals.Disassemble(spriteBatch, center, Age, DeathProgress, Size * 1.15f,
                new Color(102, 198, 230), new Color(207, 241, 250), 16);
            return;
        }

        bool survival = MidpointSurvivalActive || FinaleActive;
        float auraScale = survival ? 1.42f : 1f;
        Color sky = new(103, 197, 231);
        Color ice = new(194, 235, 248);
        BossVisuals.OscillatingAura(spriteBatch, center, Age, Size * .55f * auraScale, sky, survival ? 7 : 5, .48f);
        for (int index = 0; index < (FinaleActive ? FinaleMoteCount : AmbientMoteCount); index++)
        {
            float angle = index * 2.399963f + Age * (index % 2 == 0 ? .006f : -.0045f);
            float radius = Size * (.54f + (index % 5) * .14f) * auraScale;
            float pulse = .55f + .45f * MathF.Sin(Age * .023f + index * 1.3f);
            var mote = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * .58f);
            int moteSize = 2 + index % 3;
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)mote.X - moteSize, (int)mote.Y - moteSize,
                moteSize * 2, moteSize * 2), Color.Lerp(sky, UiTheme.Cream, pulse));
        }

        float yaw = Age * .012f;
        float pitch = .58f + MathF.Sin(Age * .009f) * .42f;
        float roll = MathF.Sin(Age * .0065f) * .24f;
        BossVisuals.RotatingCube3D(spriteBatch, center, Size * .34f, sky, ice, PhaseAccent, yaw, pitch, roll);
        float inset = Size * (.065f + .01f * MathF.Sin(Age * .037f));
        var playerLikeCore = new Rectangle((int)(center.X - inset), (int)(center.Y - inset),
            Math.Max(3, (int)(inset * 2)), Math.Max(3, (int)(inset * 2)));
        Primitives2D.FillRect(spriteBatch, playerLikeCore, UiTheme.Cream);
        Primitives2D.RectOutline(spriteBatch, playerLikeCore, new Color(48, 124, 167), 2);
        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .72f), (int)(Size * .92f), 6));
    }
}
