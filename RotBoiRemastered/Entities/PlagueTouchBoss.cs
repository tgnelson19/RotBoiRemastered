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
        ArenaShape = "square", ArenaScale = 9.4, MovementModes = Array.Empty<string>(),
    };

    protected readonly PlagueSigilConfig SigilConfig;
    protected readonly List<TouchPortal> TouchPortals = new();
    public double PortalCooldown { get; set; } = .4;
    public int PortalIndex { get; set; }
    public int PatternRotation { get; set; }
    public double PhaseAnnouncementTimer { get; set; } = 3.0;
    private float _pathAngle;

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
        if (DebugPhaseLocked)
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
        var portalPhases = Config.FinalBoss ? new[] { 2, 4, 7, 9, 10 } : new[] { 2, 4 };
        if (portalPhases.Contains(Phase))
            DeployTouchPortals(Config.FinalBoss ? 4 : 2);
    }

    public override void DebugSetPhase(int phase)
    {
        SetPlaguePhase(phase);
        DebugPhaseLocked = true;
        AttackCooldown = 0f;
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
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

    private void ClearTouchPortals()
    {
        foreach (var portal in TouchPortals)
            portal.RemFlag = true;
        TouchPortals.Clear();
    }

    private void DeployTouchPortals(int count)
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

    private void PlagueProjectile(List<EnemyProjectile> sink, float direction, float speed, float damage, string suffix,
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

    protected void Projectile(List<EnemyProjectile> sink, float direction, float speed, float damage, string suffix,
        float sizeScale = .25f, string path = "linear", Vector2? target = null)
        => PlagueProjectile(sink, direction, speed, damage, suffix, sizeScale, path, target);

    protected virtual void FirePlaguePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        // Base PlagueTouchBoss has no attack pattern of its own -- Bair/Sting override this.
    }

    private void MovementStep(float playerX, float playerY, Battleground battleground)
    {
        string mode = Config.MovementModes[Phase - 1];
        if (mode == "static")
            return;
        Vector2 target;
        if (mode == "path")
        {
            _pathAngle += .005f * (float)Simulation.GetFrameScale();
            target = ArenaCenter + new Vector2(MathF.Cos(_pathAngle), MathF.Sin(_pathAngle)) * ArenaRadius * .48f;
        }
        else
        {
            target = new Vector2(playerX, playerY);
        }
        var center = Center();
        float distance = Math.Max(1f, Vector2.Distance(target, center));
        float step = Speed * (float)Simulation.GetFrameScale();
        TryAxisMove((target.X - center.X) / distance * step, "x", battleground);
        TryAxisMove((target.Y - center.Y) / distance * step, "y", battleground);
    }

    private void UpdateTouchPortals(float playerX, float playerY, List<EnemyProjectile> sink, double dt)
    {
        foreach (var portal in TouchPortals)
        {
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
            portal.UpdateBursts(sink, (float)dt);
        }
        PortalCooldown -= dt;
        if (PortalCooldown <= 0 && TouchPortals.Count > 0)
        {
            var portal = TouchPortals[PortalIndex % TouchPortals.Count];
            portal.FireToward(sink, new Vector2(playerX, playerY), 2, .12f, .42f, Config.FinalBoss ? 300f : 240f, PhaseAccent, "heavy");
            PortalIndex += 1;
            PortalCooldown = 1.15;
        }
    }

    public sealed override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        PhaseElapsed += dt;
        PhaseAnnouncementTimer = Math.Max(0.0, PhaseAnnouncementTimer - dt);
        UpdatePhase();
        AdvanceAge();
        MovementStep(context.PlayerWorldX, context.PlayerWorldY, context.Battleground);
        UpdateTouchPortals(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink, dt);
        AttackCooldown -= (float)Simulation.GetTimerStep();
        if (EntranceRemaining <= 0 && AttackCooldown <= 0)
        {
            FirePlaguePattern(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            AttackCooldown = AttackCooldownMax!.Value * Math.Max(.4f, 1f - .055f * (Phase - 1));
        }
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
        foreach (var portal in TouchPortals)
            portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        var screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        // Layered stone plates make Touch feel massive rather than fluid.
        foreach (float inset in new[] { 0f, Size * .14f, Size * .28f })
        {
            var plate = rect;
            plate.Inflate(-(int)inset, -(int)inset);
            Primitives2D.RectOutline(spriteBatch, plate, UiTheme.Ink, Math.Max(3, (int)(Size * .055f)));
        }
        string sigil = DrawPlagueSigil(spriteBatch, rect.Center.ToVector2(), Size * .32f);
        if (PhaseAnnouncementTimer > 0)
        {
            UiTheme.DrawText(spriteBatch, $"{sigil} // {PhaseLabel}", 11, PhaseAccent,
                new Vector2(rect.Center.X, rect.Y - 18), "midbottom");
        }
    }
}
