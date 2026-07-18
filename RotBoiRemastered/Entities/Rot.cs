using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// A brittle or reinforced terrain obstacle Rot grows across the arena.
/// Ported from bossTypes.py's per-instance `crystalWalls` dict literals --
/// a small mutable class instead, since `Remaining`/`Warning`/`Rect` mutate
/// every frame (see <see cref="RunState.BossAfflictions"/> for the same
/// mutable-class-over-dict reasoning).
/// </summary>
public sealed class CrystalWall
{
    public Rectangle Rect;
    public double Remaining;
    public double Duration;
    public float Angle;
    public string Kind = "brittle";
    public double? Hp;
    public double Warning;
    public bool Compression;
}

/// <summary>Ported from bossTypes.py's per-instance `cleansingVents` dict literals.</summary>
public sealed class CleansingVent
{
    public float X;
    public float Y;
    public float Angle;
    public double Cooldown;
    public double Flash;
}

/// <summary>
/// "THE FIELD THAT REMAINS" -- the final boss of the Chemesthesis content
/// path. Ported from bossTypes.py's Rot. Adds a persistent-terrain
/// subsystem (crystal walls that block player movement and can be shot
/// down, cleansing vents that reset the player's exposure/pull afflictions
/// at the cost of sealing off a route) on top of Kage's shared sin-pattern
/// plumbing.
/// </summary>
public sealed class Rot : Kage
{
    public static readonly PathChaseBossConfig RotConfig = KageConfig with
    {
        BossName = "ROT", Subtitle = "THE FIELD THAT REMAINS", FinalBoss = true,
        OwnerPrefix = "rot_chemesthesis",
        FinalBodyColor = new Color(122, 47, 36), FinalAccentColor = new Color(210, 85, 36),
        FinalBodyScale = 2.5, FinalCooldownSeconds = 1.35, FinalShotSpeed = .38, FinalShotScale = .29,
        MovementSpeed = .07,
        MovementModes = new[] { "static", "path", "chase", "static", "path", "chase", "static" },
        PhaseLabels = new[] { "CROWN", "HOARD", "PULL", "BORROWED SHAPE", "CONSUMPTION", "RETORT", "THE ROT" },
    };

    public static readonly SinSigilConfig RotSinConfig = new(
        PhaseFlavors: new[]
        {
            "There is room for only one above.", "Nothing is enough.",
            "Every nerve bends toward desire.", "Your strength looks better on me.",
            "The field must feed.", "Every wound demands an answer.",
            "Rest. Become part of the garden.",
        },
        PhaseColors: new[]
        {
            new Color(232, 196, 84), new Color(211, 145, 45), new Color(216, 80, 112), new Color(111, 155, 88),
            new Color(153, 77, 42), new Color(224, 55, 35), new Color(91, 117, 52),
        },
        SinSigils: new (string, Vector2[][])[]
        {
            ("PRIDE", new[]
            {
                new[]
                {
                    new Vector2(-.72f, .52f), new Vector2(-.58f, -.22f), new Vector2(-.25f, .08f), new Vector2(0, -.72f),
                    new Vector2(.25f, .08f), new Vector2(.58f, -.22f), new Vector2(.72f, .52f),
                },
                new[] { new Vector2(-.58f, .28f), new Vector2(.58f, .28f) },
                new[] { new Vector2(0, -.72f), new Vector2(0, .68f) },
            }),
            ("GREED", new[]
            {
                new[]
                {
                    new Vector2(0, -.74f), new Vector2(.62f, -.18f), new Vector2(.42f, .58f), new Vector2(0, .74f),
                    new Vector2(-.42f, .58f), new Vector2(-.62f, -.18f), new Vector2(0, -.74f),
                },
                new[] { new Vector2(-.42f, -.06f), new Vector2(0, .28f), new Vector2(.42f, -.06f) },
                new[] { new Vector2(0, -.42f), new Vector2(0, .74f) },
            }),
            ("LUST", new[]
            {
                new[]
                {
                    new Vector2(0, .72f), new Vector2(-.68f, -.04f), new Vector2(-.42f, -.6f), new Vector2(0, -.22f),
                    new Vector2(.42f, -.6f), new Vector2(.68f, -.04f), new Vector2(0, .72f),
                },
                new[] { new Vector2(-.72f, 0), new Vector2(.72f, 0) },
            }),
            ("ENVY", new[]
            {
                new[]
                {
                    new Vector2(-.74f, 0), new Vector2(-.36f, -.42f), new Vector2(0, 0),
                    new Vector2(-.36f, .42f), new Vector2(-.74f, 0),
                },
                new[]
                {
                    new Vector2(.74f, 0), new Vector2(.36f, -.42f), new Vector2(0, 0),
                    new Vector2(.36f, .42f), new Vector2(.74f, 0),
                },
                new[] { new Vector2(-.36f, 0), new Vector2(.36f, 0) },
            }),
            ("GLUTTONY", new[]
            {
                new[] { new Vector2(-.7f, -.34f), new Vector2(-.34f, -.68f), new Vector2(.34f, -.68f), new Vector2(.7f, -.34f) },
                new[] { new Vector2(-.7f, .34f), new Vector2(-.34f, .68f), new Vector2(.34f, .68f), new Vector2(.7f, .34f) },
                new[] { new Vector2(-.7f, -.34f), new Vector2(-.28f, 0), new Vector2(-.7f, .34f) },
                new[] { new Vector2(.7f, -.34f), new Vector2(.28f, 0), new Vector2(.7f, .34f) },
            }),
            ("WRATH", new[]
            {
                new[] { new Vector2(-.58f, -.7f), new Vector2(.1f, -.08f), new Vector2(-.18f, .08f), new Vector2(.58f, .7f) },
                new[] { new Vector2(.58f, -.7f), new Vector2(-.1f, -.08f), new Vector2(.18f, .08f), new Vector2(-.58f, .7f) },
                new[] { new Vector2(-.72f, 0), new Vector2(.72f, 0) },
            }),
            ("SLOTH", new[]
            {
                new[]
                {
                    new Vector2(-.62f, -.56f), new Vector2(.48f, -.56f), new Vector2(.48f, .34f), new Vector2(-.28f, .34f),
                    new Vector2(-.28f, -.1f), new Vector2(.14f, -.1f), new Vector2(.14f, .06f),
                },
                new[] { new Vector2(0, -.76f), new Vector2(0, -.56f) },
                new[] { new Vector2(-.48f, .62f), new Vector2(0, .76f), new Vector2(.48f, .62f) },
            }),
        },
        ActMetadata: new Dictionary<int, string> { [3] = "ACT II // TEMPTATION", [5] = "ACT III // SATURATION" });

    private readonly List<CrystalWall> _crystalWalls = new();
    private readonly List<CleansingVent> _cleansingVents = new();
    private double _compressionCooldown = 5.0;
    private double _consumedCrystalPulse;

    public IReadOnlyList<CrystalWall> CrystalWalls => _crystalWalls;
    public IReadOnlyList<CleansingVent> CleansingVents => _cleansingVents;
    public int VentsUsed { get; private set; }
    public double PeakExposure { get; private set; }
    protected override double ConsumedCrystalPulse => _consumedCrystalPulse;

    public Rot(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, RotConfig, RotSinConfig, rng)
    {
        ActTitle = "ACT I // APPETITE";
        ActTransitionTimer = ActTransitionDuration;
        PhaseProtectionTimer = ActTransitionDuration;
        var center = Center();
        for (int index = 0; index < 4; index++)
        {
            float angle = index * MathF.PI / 2f + MathF.PI / 4f;
            _cleansingVents.Add(new CleansingVent
            {
                X = center.X + MathF.Cos(angle) * Simulation.TileSize * 5.7f,
                Y = center.Y + MathF.Sin(angle) * Simulation.TileSize * 5.7f,
                Angle = angle, Cooldown = 0.0, Flash = 0.0,
            });
        }
    }

    protected override void SetSinPhase(int phase)
    {
        base.SetSinPhase(phase);
        _crystalWalls.Clear();
        if (Phase == 7)
            _compressionCooldown = 5.0;
    }

    /// <summary>Ported from _camera_cardinal_angle: the on-screen "right" direction, rotated by quarter turns, expressed in world space.</summary>
    private float CameraCardinalAngle(Camera? camera, int quarterTurn = 0)
    {
        var worldVector = camera?.ScreenVectorToWorld(new Vector2(1, 0)) ?? new Vector2(1, 0);
        float baseAngle = MathF.Atan2(worldVector.Y, worldVector.X);
        return baseAngle + quarterTurn * MathF.PI / 2f;
    }

    private void GrowCrystalWall(float angle, double duration = 8.0, string? kind = null, float distanceTiles = 3.9f, bool compression = false)
    {
        var center = Center();
        float distance = Simulation.TileSize * distanceTiles;
        float wallCenterX = center.X + MathF.Cos(angle) * distance;
        float wallCenterY = center.Y + MathF.Sin(angle) * distance;
        bool horizontal = Math.Abs(MathF.Cos(angle)) < Math.Abs(MathF.Sin(angle));
        float width = Simulation.TileSize * (horizontal ? 3.5f : .72f);
        float height = Simulation.TileSize * (horizontal ? .72f : 3.5f);
        var rect = new Rectangle((int)(wallCenterX - width / 2f), (int)(wallCenterY - height / 2f), (int)width, (int)height);
        string wallKind = kind ?? (PatternRotation % 2 == 0 ? "brittle" : "reinforced");
        _crystalWalls.Add(new CrystalWall
        {
            Rect = rect, Remaining = duration, Duration = duration, Angle = angle, Kind = wallKind,
            Hp = wallKind == "brittle" ? 420 : null, Warning = compression ? 2.5 : 0.0, Compression = compression,
        });
        if (_crystalWalls.Count > 6)
            _crystalWalls.RemoveRange(0, _crystalWalls.Count - 6);
    }

    protected override void UpdateTerrain(float playerX, float playerY, double dt, EnemyUpdateContext context)
    {
        var afflictions = context.BossAfflictions;
        if (afflictions is null)
            return;
        PeakExposure = Math.Max(PeakExposure, afflictions.Exposure);
        _consumedCrystalPulse = Math.Max(0.0, _consumedCrystalPulse - dt);
        var center = Center();
        foreach (var wall in _crystalWalls)
        {
            wall.Remaining = Math.Max(0.0, wall.Remaining - dt);
            wall.Warning = Math.Max(0.0, wall.Warning - dt);
            if (wall.Compression && wall.Warning <= 0)
            {
                float deltaX = center.X - wall.Rect.Center.X, deltaY = center.Y - wall.Rect.Center.Y;
                float distance = Math.Max(1.0f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
                if (distance > Simulation.TileSize * 2.25f)
                {
                    float step = Simulation.TileSize * .34f * (float)dt;
                    int newCenterX = wall.Rect.Center.X + (int)(deltaX / distance * step);
                    int newCenterY = wall.Rect.Center.Y + (int)(deltaY / distance * step);
                    wall.Rect = new Rectangle(newCenterX - wall.Rect.Width / 2, newCenterY - wall.Rect.Height / 2, wall.Rect.Width, wall.Rect.Height);
                }
            }
        }
        _crystalWalls.RemoveAll(wall => wall.Remaining <= 0);

        foreach (var vent in _cleansingVents)
        {
            vent.Cooldown = Math.Max(0.0, vent.Cooldown - dt);
            vent.Flash = Math.Max(0.0, vent.Flash - dt);
            float distanceToVent = MathF.Sqrt((playerX - vent.X) * (playerX - vent.X) + (playerY - vent.Y) * (playerY - vent.Y));
            if (vent.Cooldown <= 0 && afflictions.Exposure > .25 && distanceToVent <= Simulation.TileSize * 1.05f)
            {
                afflictions.Reset();
                vent.Cooldown = 12.0;
                vent.Flash = 1.0;
                VentsUsed++;
                // Cleansing opens the player's immediate position but seals the
                // corresponding inner route, turning relief into a terrain choice.
                GrowCrystalWall(vent.Angle, 7.0);
            }
        }

        if (Phase == 7 && ActTransitionTimer <= 0)
        {
            _compressionCooldown -= dt;
            if (_compressionCooldown <= 0)
            {
                float angle = CameraCardinalAngle(context.Camera, PatternRotation % 2);
                GrowCrystalWall(angle, 11.0, "reinforced", 6.2f, true);
                GrowCrystalWall(angle + MathF.PI, 11.0, "reinforced", 6.2f, true);
                _compressionCooldown = 12.0;
            }
        }
    }

    public override IReadOnlyList<Rectangle> MovementObstacles() =>
        _crystalWalls.Where(wall => wall.Warning <= 0).Select(wall => wall.Rect).ToList();

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var hitboxes = base.GetScreenHitboxes(camera, playerWorldPosition, screenShake).ToList();
        for (int index = 0; index < _crystalWalls.Count; index++)
        {
            var wall = _crystalWalls[index];
            if (wall.Kind != "brittle" || wall.Warning > 0)
                continue;
            var rect = wall.Rect;
            var corners = new[]
            {
                camera.WorldToScreen(new Vector2(rect.Left, rect.Top), playerWorldPosition, screenShake),
                camera.WorldToScreen(new Vector2(rect.Right, rect.Top), playerWorldPosition, screenShake),
                camera.WorldToScreen(new Vector2(rect.Right, rect.Bottom), playerWorldPosition, screenShake),
                camera.WorldToScreen(new Vector2(rect.Left, rect.Bottom), playerWorldPosition, screenShake),
            };
            float left = corners.Min(c => c.X), top = corners.Min(c => c.Y);
            float right = corners.Max(c => c.X), bottom = corners.Max(c => c.Y);
            hitboxes.Add(($"crystal:{index}", new Rectangle((int)left, (int)top, Math.Max(1, (int)(right - left)), Math.Max(1, (int)(bottom - top)))));
        }
        return hitboxes;
    }

    protected override HitResult DamageCrystal(string partId, double amount)
    {
        int index = int.Parse(partId.Split(':', 2)[1]);
        if (index < 0 || index >= _crystalWalls.Count)
            return new HitResult(false, false, 0, true);
        var wall = _crystalWalls[index];
        if (wall.Kind != "brittle")
            return new HitResult(false, false, 0, true);
        double applied = Math.Min(wall.Hp!.Value, Math.Round(amount));
        wall.Hp -= applied;
        if (wall.Hp <= 0)
            _crystalWalls.RemoveAt(index);
        return new HitResult(true, false, applied);
    }

    protected override void DrawPersistentTerrain(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        foreach (var vent in _cleansingVents)
        {
            var point = camera.WorldToScreen(new Vector2(vent.X, vent.Y), playerWorldPosition, screenShake);
            bool ready = vent.Cooldown <= 0;
            Color color = vent.Flash > 0 ? UiTheme.Cream : ready ? new Color(96, 185, 151) : UiTheme.Border;
            float radius = Simulation.TileSize * (ready ? .42f : .32f);
            Primitives2D.FillCircle(spriteBatch, point, radius + 6, UiTheme.Ink);
            Primitives2D.CircleOutline(spriteBatch, point, radius, color, 4);
            Primitives2D.Line(spriteBatch, new Vector2(point.X - radius, point.Y), new Vector2(point.X + radius, point.Y), color, 2);
            Primitives2D.Line(spriteBatch, new Vector2(point.X, point.Y - radius), new Vector2(point.X, point.Y + radius), color, 2);
        }

        foreach (var wall in _crystalWalls)
        {
            var rect = wall.Rect;
            var topLeft = camera.WorldToScreen(new Vector2(rect.Left, rect.Top), playerWorldPosition, screenShake);
            var bottomRight = camera.WorldToScreen(new Vector2(rect.Right, rect.Bottom), playerWorldPosition, screenShake);
            var screenRect = new Rectangle(
                (int)Math.Min(topLeft.X, bottomRight.X), (int)Math.Min(topLeft.Y, bottomRight.Y),
                Math.Max(8, (int)Math.Abs(bottomRight.X - topLeft.X)), Math.Max(8, (int)Math.Abs(bottomRight.Y - topLeft.Y)));
            double fade = Math.Min(1.0, wall.Remaining * 2);
            bool warning = wall.Warning > 0;
            Color color = warning ? UiTheme.Cream : UiTheme.Lighten(PhaseAccent, wall.Kind == "brittle" ? 48 : (int)(20 * fade));
            var outer = screenRect;
            outer.Inflate(8, 8);
            Primitives2D.FillRect(spriteBatch, outer, UiTheme.Ink);
            if (warning)
                Primitives2D.RectOutline(spriteBatch, screenRect, color, 3);
            else
                Primitives2D.FillRect(spriteBatch, screenRect, color);
            int stripeStep = Math.Max(8, (int)(Simulation.TileSize * .4f));
            int span = Math.Max(screenRect.Width, screenRect.Height);
            for (int offset = 0; offset < span; offset += stripeStep)
            {
                if (screenRect.Width >= screenRect.Height)
                {
                    Primitives2D.Line(spriteBatch, new Vector2(screenRect.X + offset, screenRect.Bottom),
                        new Vector2(screenRect.X + offset + 9, screenRect.Y), UiTheme.Cream, 2);
                }
                else
                {
                    Primitives2D.Line(spriteBatch, new Vector2(screenRect.X, screenRect.Y + offset),
                        new Vector2(screenRect.Right, screenRect.Y + offset + 9), UiTheme.Cream, 2);
                }
            }
        }

        if (ActTransitionTimer > 0)
            DrawRoutePreview(spriteBatch, camera, playerWorldPosition, screenShake);
    }

    private void DrawRoutePreview(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var ready = _cleansingVents.Where(vent => vent.Cooldown <= 0).ToList();
        if (ready.Count < 2)
            return;
        var color = new Color(96, 185, 151);
        var start = camera.WorldToScreen(new Vector2(ready[0].X, ready[0].Y), playerWorldPosition, screenShake);
        var target = ready[ready.Count > 2 ? 2 : 1];
        var end = camera.WorldToScreen(new Vector2(target.X, target.Y), playerWorldPosition, screenShake);
        Primitives2D.Line(spriteBatch, start, end, UiTheme.Ink, 9);
        Primitives2D.Line(spriteBatch, start, end, color, 3);
        var midpoint = (start + end) / 2f;
        UiTheme.DrawText(spriteBatch, "PREVIEW // CLEAN ROUTE", 9, color, midpoint, "center");
    }

    public IReadOnlyDictionary<string, bool> ChallengeResults() => new Dictionary<string, bool>
    {
        ["clean_traversal"] = PeakExposure <= 3.0,
        ["vent_discipline"] = VentsUsed <= 1,
        ["uncontaminated"] = PeakExposure <= .25,
    };

    protected override void FireSinPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        var sink = context.ProjectileSink;
        switch (Phase)
        {
            case 1: // Pride
            {
                float laneAngle = CameraCardinalAngle(context.Camera, PatternRotation % 2);
                ParallelLanes(sink, laneAngle, 3, Simulation.TileSize * 2.4f, 330, "pride_crown");
                PatternRotation++;
                break;
            }
            case 2: // Greed
            {
                Radial(sink, 9, .25f, 315, "greed_hoard", mine: true);
                for (int index = 0; index < 3; index++)
                {
                    Shot(sink, index * 2f * MathF.PI / 3f, 0f, 290, scale: .20f, shape: "mine", path: "orbit",
                        lifetime: 12f, orbitRadius: Simulation.TileSize * (3.2f + index), angularSpeed: .26f + index * .05f,
                        ownerSuffix: "greed_coin");
                }
                GrowCrystalWall(CameraCardinalAngle(context.Camera, PatternRotation % 4));
                break;
            }
            case 3: // Lust
            {
                for (int index = 0; index < 9; index++)
                {
                    float offset = -1.3f + 2.6f * index / 8f;
                    Shot(sink, aimed + offset, .62f, 325, ownerSuffix: "lust_pull", affliction: "pull",
                        afflictionDuration: 1.4, afflictionStrength: .32, exposure: .8, afflictionSource: center);
                }
                Bomb(sink, playerX, playerY, 340, "lust_lure");
                break;
            }
            case 4: // Envy
            {
                var build = context.PlayerBuildSnapshot;
                string identity = build?.DominantOffense ?? "power";
                double projectileCountStat = build is not null ? build.Stats.GetValueOrDefault("projectile_count") : 1.0;
                int count = Math.Clamp((int)Math.Round(projectileCountStat), 3, 9);
                if (identity == "critical")
                    Laser(sink, aimed, 375, "envy_critical");
                else if (identity == "tempo")
                    Fan(sink, aimed, count, .55f, 1.38f, 300, "envy_tempo");
                else if (identity == "precision")
                    Fan(sink, aimed, 3, .22f, 1.65f, 350, "envy_precision");
                else
                    Fan(sink, aimed, count, .9f, 1.05f, 335, $"envy_{identity}");
                Fan(sink, aimed + MathF.PI, count, .9f, .62f, 310, "envy_reflection");
                break;
            }
            case 5: // Gluttony
            {
                if (_crystalWalls.Count > 0)
                {
                    _crystalWalls.RemoveAt(0);
                    Stagger = Math.Max(0.0, Stagger - MaxStagger * .25);
                    _consumedCrystalPulse = 1.0;
                }
                Bomb(sink, playerX, playerY, 390, "gluttony_feast");
                Radial(sink, 7, .32f, 325, "gluttony_morsel", mine: true);
                break;
            }
            case 6: // Wrath
            {
                Fan(sink, aimed, 7, .65f, 1.2f, 370, "wrath_retort");
                float laneAngle = CameraCardinalAngle(context.Camera, PatternRotation % 2);
                ParallelLanes(sink, laneAngle, 2, Simulation.TileSize * 3.2f, 360, "wrath_answer");
                ParallelLanes(sink, laneAngle + MathF.PI / 2f, 2, Simulation.TileSize * 3.2f, 360, "wrath_cross");
                PatternRotation++;
                break;
            }
            default: // Sloth: persistent rot plus callbacks from the other sins.
            {
                Radial(sink, 12, .16f, 335, "sloth_rot", mine: true);
                int start = Math.Max(0, sink.Count - 12);
                for (int index = start; index < sink.Count; index++)
                {
                    sink[index].Affliction = "slow";
                    sink[index].AfflictionDuration = 2.1;
                    sink[index].AfflictionStrength = .16;
                    sink[index].Exposure = 1.15;
                }
                int callback = PatternRotation % 3;
                if (callback == 0)
                    Laser(sink, aimed, 345, "rot_crown", .08f);
                else if (callback == 1)
                    Bomb(sink, playerX, playerY, 355, "rot_feast");
                else
                    Fan(sink, aimed, 7, 1.5f, .72f, 345, "rot_desire");
                break;
            }
        }
        MarkAttack(.58f);
    }
}
