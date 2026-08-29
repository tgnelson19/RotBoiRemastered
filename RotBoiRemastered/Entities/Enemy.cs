using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

public readonly record struct EnemyRenderPose(
    Rectangle Rect,
    Vector2 Center,
    Vector2 Facing,
    Vector2 WorldRight,
    Vector2 WorldDown,
    float WalkPhase,
    float AttackPulse,
    bool HitFlash,
    bool HasFacing);

/// <summary>Ported from enemy.py's HitResult frozen dataclass.</summary>
public sealed record HitResult(bool Applied, bool Killed, double Amount = 0, bool Blocked = false);

/// <summary>Separates deliberate projectile impacts from periodic status damage.</summary>
public enum DamageSource { Direct, DamageOverTime }

/// <summary>
/// Everything an Enemy subclass's Update might need beyond its own state and
/// the player's position. Ported from enemyTypes.py's reliance on
/// characterStats.py's module-level `enemyHolster`/`experienceList` globals
/// (BannerCaptain.updateEnemy reaches into `cS.enemyHolster` to command
/// sibling minions; CollectorEnemy.updateEnemy reaches into
/// `cS.experienceList` to steal nearby XP bubbles). Bundled into one object,
/// rather than adding those two fields as ignored parameters to every other
/// enemy's override, so every Update signature stays identical and every
/// enemy type -- not just the two that need it -- is unit testable without
/// constructing a whole run's worth of global state.
///
/// `Camera`/`BossAfflictions`/`PlayerBuildSnapshot` are all nullable and
/// added for `Rot` (bossTypes.py's `SinChemesthesisBoss` family): Camera for
/// `_camera_cardinal_angle`'s `bG.screen_vector_to_world` read, BossAfflictions
/// for `characterStats.py`'s `bossAfflictions`/`reset_boss_afflictions`, and
/// PlayerBuildSnapshot for `player_build_snapshot()`.
///
/// `PlayerBullets`/`DreamState` are likewise nullable and added for
/// `PhantasiaBoss` (bossTypes.py's `Hypno`/`Malady` family): PlayerBullets
/// for the "did the player fire during REST" check
/// (`cS.bulletHolster` truthiness), DreamState for direct
/// `cS.alter_belief(...)` calls a boss makes on itself (Sabbath-phase
/// violations, offering pickups) as well as the `cS.dreamState["belief"]`
/// reads that drive the dream-court field diagram's intensity. Nothing else
/// in this port needs them yet, but every Enemy still gets one uniform
/// Update signature, same reasoning as the rest of this context object.
/// </summary>
public sealed class EnemyUpdateContext
{
    public required float PlayerWorldX { get; set; }
    public required float PlayerWorldY { get; set; }
    public required Battleground Battleground { get; set; }
    public List<EnemyProjectile> ProjectileSink { get; set; } = new();
    public IReadOnlyList<Enemy> AllEnemies { get; set; } = Array.Empty<Enemy>();
    public List<ExperienceBubble> ExperienceBubbles { get; set; } = new();
    public Camera? Camera { get; set; }
    public BossAfflictions? BossAfflictions { get; set; }
    public PlayerBuildSnapshot? PlayerBuildSnapshot { get; set; }
    public IReadOnlyList<Bullet> PlayerBullets { get; set; } = Array.Empty<Bullet>();
    public RotBoiRemastered.Systems.DreamState? DreamState { get; set; }
    public float PlayerMovementSpeed { get; set; } = 2.1f;
    public float MovementSpeedCap { get; set; } = float.PositiveInfinity;
}

/// <summary>
/// Base world-space enemy entity and shared combat contract. Ported from
/// enemy.py. Marked virtual where enemyTypes.py's subclasses override.
///
/// Cleanup vs. the Python original:
/// - `awarenessRange` was computed from a screen-height global (vH.sH * .5)
///   inside the constructor -- Enemy now takes it as an explicit parameter,
///   so gameplay logic has no implicit dependency on display resolution.
///   The caller (whatever spawns enemies) computes it once from the real
///   screen height, same as Camera.cs's cleanup for player position/shake.
/// - drawEnemy() both mutated state (decrementing visualAttackTimer,
///   updating the "did I move this frame" bookkeeping) and rendered.
///   Update/Draw are split here -- all state mutation (including that
///   bookkeeping) now happens in Update, so Draw never mutates anything and
///   the movement/awareness/collision logic is unit testable without a
///   GraphicsDevice.
/// - Update takes one `EnemyUpdateContext` rather than loose parameters --
///   see that type's doc comment.
/// - `family`/`spawnDefinitionKey`/`encounterKey`/`atomicSpawnGroup` were
///   attributes EnemyCatalog attached to instances after construction
///   (`enemy.family = ...`, duck-typed `getattr(enemy, "atomicSpawnGroup",
///   False)` reads elsewhere) -- promoted to real properties on the base
///   class with sensible defaults, since every enemy has them.
/// - `attackCooldownMax`/`attackCooldown` were duck-typed
///   (`hasattr(enemy, "attackCooldownMax")` in EnemyCatalog.apply_modifier's
///   "hasty" branch; `hasattr(enemy, "attackCooldown")` in
///   RuntimeEncounter's constructor) rather than declared on the base
///   Enemy class in Python -- each subclass that needed one just assigned
///   `self.attackCooldown` itself. Both are nullable properties on the base
///   class here instead (null unless a subclass sets them), which reads the
///   same as the Python hasattr checks without runtime type-name checks and
///   lets every ranged/timed subclass share one property instead of
///   re-declaring its own field.
/// </summary>
public class Enemy
{
    public float WorldX { get; protected set; }
    public float WorldY { get; protected set; }
    public float Speed { get; set; }
    public float Size { get; set; }
    public Color Color { get; set; }
    public int Damage { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public List<object> CantTouchMeList { get; } = new();
    public double ExpValue { get; set; }
    public double Difficulty { get; }
    public string Archetype { get; }
    public string DifficultyTier { get; }
    public int TierRank { get; }
    public float Age { get; private set; }
    public float VisualAgeSeconds => Age / Math.Max(1, Simulation.FrameRate);
    public string AwarenessState { get; set; } = "wandering";
    public float AwarenessRange { get; set; }
    public float DisengageRange { get; set; }
    public float WanderAngle { get; protected set; }
    public float WanderTimer { get; protected set; }
    public double ThreatCost { get; set; } = 1.0;
    public List<Enemy> SpawnedEnemies { get; } = new();
    public bool EngagementAllowed { get; set; } = true;
    public string CombatRole { get; set; } = "pressure";
    public IReadOnlySet<string> InteractionTags { get; set; } = new HashSet<string>();
    public string? BehaviorModifier { get; set; }
    public Color? ModifierColor { get; set; }
    public double RegenerationRate { get; set; }
    public double RegenerationBuffer { get; set; }
    public int VolatileBurst { get; set; }
    public float VisualAttackTimer { get; private set; }
    public float VisualAttackCooldown { get; private set; }
    public bool Moved { get; private set; }
    public float VisualHitTimer { get; private set; }
    public RuntimeEncounter? Encounter { get; set; }
    public int EncounterSlot { get; set; }
    public Vector2? EncounterPatrolTarget { get; set; }
    public Vector2? EncounterCombatTarget { get; set; }
    public int CombatSide { get; set; }
    public string Family { get; set; } = "basic";
    public string? ContentPath { get; set; }
    public string? SpawnDefinitionKey { get; set; }
    public string? EncounterKey { get; set; }
    public bool AtomicSpawnGroup { get; set; }
    public float? AttackCooldownMax { get; set; }
    public float? AttackCooldown { get; set; }
    public virtual bool ReceivesKnockback => true;
    public Dictionary<string, StatusEffectState> StatusEffects { get; } = new();
    public double StatusDotBuffer { get; set; }
    public double StatusControlResistance { get; set; }
    /// <summary>Guards the run-level NG+ health transform against recursive/double application.</summary>
    public int NewGamePlusLevelApplied { get; set; }

    /// <summary>
    /// Set by a boss/miniboss to request that GameSession purge live enemy
    /// projectiles on its next Update pass (a phase transition invalidating
    /// its own projectile field). Promoted to the base class from
    /// ArsenalMiniBoss (its only prior setter) now that Beaudis needs it
    /// too. When <see cref="TransitionCleanupOwner"/> is null the request
    /// means "clear every live enemy projectile" (Beaudis's isolated
    /// encounter has nothing else to preserve); when set, only that
    /// owner's projectiles are removed (ArsenalMiniBoss's per-instance tag,
    /// so multiple simultaneous minibosses don't clear each other's shots).
    /// </summary>
    public bool TransitionCleanupRequested { get; set; }
    public string? TransitionCleanupOwner { get; set; }
    /// <summary>
    /// Set by a boss to request that GameSession accelerate every live enemy
    /// projectile it owns out toward its arena's edge on the next Update
    /// pass -- a softer alternative to <see cref="TransitionCleanupRequested"/>
    /// that sweeps the last phase's or subphase's shots off the screen over
    /// a second or so (the existing radial-arena boundary check then removes
    /// each one once it crosses the edge) instead of purging them instantly.
    /// Self-clears once GameSession applies it. When
    /// <see cref="TransitionSweepOwner"/> is null the request applies to
    /// every live enemy projectile; when set, only that owner's.
    /// </summary>
    public bool TransitionSweepRequested { get; set; }
    public string? TransitionSweepOwner { get; set; }
    /// <summary>
    /// Requests the Hard Mode full-heal checkpoint associated with a boss
    /// phase or subphase handoff. Ordinary recovery remains disabled.
    /// </summary>
    public bool MilestoneHealRequested { get; set; }
    /// <summary>
    /// Seconds of player invulnerability requested by a boss phase interlude.
    /// The between-phase transition sweeps the outgoing phase's shots off the
    /// arena at speed (see <see cref="TransitionSweepRequested"/>); those
    /// accelerating bullets are close to undodgeable, so the interlude buys
    /// the player grace for its own duration rather than asking them to
    /// survive a pattern that is deliberately being destroyed. GameSession
    /// folds this into RunState.GracePeriod and clears the request, so a boss
    /// sets it once per interlude instead of every frame.
    /// </summary>
    public double PhaseInterludeInvulnerabilitySeconds { get; set; }

    private Vector2 _lastVisualWorld;
    private Vector2 _visualFacing = Vector2.UnitX;
    /// <summary>
    /// True once this enemy has actually moved and so has a real facing.
    /// Before that, `_visualFacing`'s arbitrary UnitX default would give
    /// spawns-that-never-move (turrets, stationary blockers) a shadow
    /// pointing in an arbitrary direction instead of the shared south rest
    /// pose every other idle shadow uses.
    /// </summary>
    private bool _hasFacing;
    private float _visualAttackDuration = Simulation.FrameRate * .22f;
    private readonly Random _rng;
    private Vector2 _lastCollisionSafePosition;
    private float _lastCollisionSafeCameraAngle;
    private bool _hasCollisionSafePosition;
    private readonly (string Part, Rectangle Rect)[] _singleWorldHitbox = new (string, Rectangle)[1];
    private readonly (string Part, Rectangle Rect)[] _singleScreenHitbox = new (string, Rectangle)[1];
    private Camera? _collisionCamera;

    private static readonly IReadOnlyDictionary<string, int> TierRanks =
        new Dictionary<string, int> { ["easy"] = 1, ["medium"] = 2, ["hard"] = 3 };

    public Enemy(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, string archetype = "drifter",
        string difficultyTier = "easy", Random? rng = null)
    {
        WorldX = worldX;
        WorldY = worldY;
        Speed = speed;
        Size = size;
        Color = color;
        Damage = (int)Math.Round(damage);
        Hp = (int)Math.Round(hp);
        MaxHp = (int)Math.Round(hp);
        ExpValue = expValue;
        Difficulty = difficulty;
        Archetype = archetype;
        DifficultyTier = difficultyTier;
        TierRank = TierRanks.GetValueOrDefault(difficultyTier, 1);
        AwarenessRange = awarenessRange;
        DisengageRange = awarenessRange * 1.25f;
        _rng = rng ?? Random.Shared;
        WanderAngle = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
        WanderTimer = _rng.Next(55, 136); // Python randint(55, 135) is inclusive on both ends
        VisualAttackCooldown = (float)(0.7 + _rng.NextDouble() * (1.4 - 0.7)) * Simulation.FrameRate;
        _lastVisualWorld = new Vector2(worldX, worldY);
    }

    public void MarkAttack(float duration = .22f)
    {
        float durationFrames = Math.Max(1f, Simulation.FrameRate * duration);
        if (durationFrames >= VisualAttackTimer)
        {
            VisualAttackTimer = durationFrames;
            _visualAttackDuration = durationFrames;
        }
    }

    /// <summary>
    /// A complete zero-to-one-to-zero attack curve keyed to the duration of
    /// the gameplay event that most recently called <see cref="MarkAttack"/>.
    /// Renderers use this instead of guessing a family-specific duration.
    /// </summary>
    public float VisualAttackPulse
    {
        get
        {
            if (VisualAttackTimer <= 0)
                return 0f;
            float progress = 1f - Math.Clamp(
                VisualAttackTimer / Math.Max(1f, _visualAttackDuration),
                0f,
                1f);
            return MathF.Sin(progress * MathF.PI);
        }
    }
    public void MarkVisualHit(float duration = .1f) =>
        VisualHitTimer = Math.Max(VisualHitTimer, Simulation.FrameRate * duration);

    /// <summary>Hook for the path layer's ranged-distance multiplier.</summary>
    public virtual void ScaleAttackRange(double multiplier) { }

    public Rectangle WorldRect() => WorldRectAt(WorldX, WorldY);

    private Rectangle WorldRectAt(float x, float y) => new((int)x, (int)y, (int)Size, (int)Size);

    /// <summary>Supplies the live camera used to match wall collision to the screen-aligned body.</summary>
    public void SetCollisionCamera(Camera? camera) => _collisionCamera = camera;

    /// <summary>Exact world footprint of the square drawn from this enemy's screen anchor.</summary>
    public Vector2[] WorldCollisionPolygon(Camera camera, float? worldX = null, float? worldY = null)
    {
        float x = worldX ?? WorldX, y = worldY ?? WorldY;
        var anchor = new Vector2(x, y);
        var offsets = new[]
        {
            Vector2.Zero, new Vector2(Size, 0),
            new Vector2(Size, Size), new Vector2(0, Size),
        };
        return offsets.Select(offset => anchor + camera.ScreenVectorToWorld(offset)).ToArray();
    }

    private bool PositionHitsWall(float x, float y, Battleground battleground) =>
        _collisionCamera is null
            ? battleground.RectHitsWall(WorldRectAt(x, y))
            : battleground.ScreenAlignedRectangleHitsWall(
                new Vector2(x, y), Size, Size, _collisionCamera);

    /// <summary>Moves a newly spawned or camera-rotated body out of any wall overlap.</summary>
    public void EnsureCollisionSafePosition(Battleground battleground)
    {
        float cameraAngle = _collisionCamera?.AngleDegrees ?? 0f;
        var position = new Vector2(WorldX, WorldY);
        if (_hasCollisionSafePosition
            && position == _lastCollisionSafePosition
            && cameraAngle == _lastCollisionSafeCameraAngle)
        {
            return;
        }
        if (PositionHitsWall(WorldX, WorldY, battleground))
            FindNearestCollisionSafePosition(battleground);
        MarkCollisionSafe();
    }

    protected bool TryAxisMove(float amount, string axis, Battleground battleground)
    {
        if (amount == 0)
            return false;
        float nextX = axis == "x" ? WorldX + amount : WorldX;
        float nextY = axis == "y" ? WorldY + amount : WorldY;
        if (PositionHitsWall(nextX, nextY, battleground))
            return false;
        WorldX = nextX;
        WorldY = nextY;
        MarkCollisionSafe();
        return true;
    }

    private void MarkCollisionSafe()
    {
        _lastCollisionSafePosition = new Vector2(WorldX, WorldY);
        _lastCollisionSafeCameraAngle = _collisionCamera?.AngleDegrees ?? 0f;
        _hasCollisionSafePosition = true;
    }

    /// <summary>
    /// Advances Age and decays VisualAttackTimer. Python's base drawEnemy()
    /// did this unconditionally on every render, which worked because it ran
    /// regardless of which subclass's updateEnemy executed that frame. Since
    /// Draw here never mutates state, every subclass's Update override calls
    /// this itself (in place of its own `self.age += vH.get_timer_step()`)
    /// so the walk-bob/attack-flinch animation Draw reads stays correct.
    /// </summary>
    protected void AdvanceAge()
    {
        float timerStep = (float)Simulation.GetTimerStep();
        Age += timerStep;
        VisualAttackTimer = Math.Max(0f, VisualAttackTimer - timerStep);
        VisualHitTimer = Math.Max(0f, VisualHitTimer - timerStep);
    }

    /// <summary>
    /// Updates the Moved flag Draw uses for the walk-bob animation. Python
    /// computed this inline in drawEnemy() by comparing worldX/Y (already
    /// updated by that frame's updateEnemy) against the previous frame's
    /// cached position. Every subclass's Update override calls this once,
    /// after movement is finalized for the frame.
    /// </summary>
    protected void FinishMovementTracking()
    {
        var current = new Vector2(WorldX, WorldY);
        Vector2 delta = current - _lastVisualWorld;
        Moved = delta.LengthSquared() > .0004f;
        if (Moved)
        {
            _visualFacing = Vector2.Normalize(delta);
            _hasFacing = true;
        }
        _lastVisualWorld = current;
    }

    /// <summary>Update the shared wander/alert/disengage state with hysteresis.</summary>
    protected bool UpdateAwareness(float distance)
    {
        if (!EngagementAllowed)
        {
            AwarenessState = "wandering";
            return false;
        }
        if (AwarenessState == "wandering")
        {
            if (distance <= AwarenessRange)
                AwarenessState = "alerted";
        }
        else if (distance > DisengageRange)
        {
            AwarenessState = "wandering";
        }
        else if (distance > AwarenessRange)
        {
            AwarenessState = "disengaging";
        }
        else
        {
            AwarenessState = "alerted";
        }
        return AwarenessState != "wandering";
    }

    /// <summary>Low-cost MMO-style roaming shared by otherwise simple enemies.</summary>
    protected void Wander(Battleground battleground, float speedMultiplier = .2f)
    {
        if (EncounterPatrolTarget.HasValue)
        {
            float targetX = EncounterPatrolTarget.Value.X, targetY = EncounterPatrolTarget.Value.Y;
            float dx = targetX - (WorldX + Size / 2f);
            float dy = targetY - (WorldY + Size / 2f);
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > Size * .35f)
            {
                float patrolStep = Speed * speedMultiplier * (float)Simulation.GetFrameScale();
                TryAxisMove(dx / distance * patrolStep, "x", battleground);
                TryAxisMove(dy / distance * patrolStep, "y", battleground);
                return;
            }
        }
        WanderTimer -= (float)Simulation.GetTimerStep();
        if (WanderTimer <= 0)
        {
            WanderAngle += (float)(_rng.NextDouble() * 2.7 - 1.35);
            WanderTimer = _rng.Next(55, 136);
        }
        float step = Speed * speedMultiplier * (float)Simulation.GetFrameScale();
        bool movedX = TryAxisMove(MathF.Cos(WanderAngle) * step, "x", battleground);
        bool movedY = TryAxisMove(MathF.Sin(WanderAngle) * step, "y", battleground);
        if (!movedX || !movedY)
            WanderAngle += (float)(_rng.NextDouble() * 1.45 + .75);
    }

    /// <summary>Move toward the player while retaining motion parallel to solid walls.</summary>
    public virtual void Update(EnemyUpdateContext context)
    {
        float playerWorldX = context.PlayerWorldX, playerWorldY = context.PlayerWorldY;
        var battleground = context.Battleground;
        AdvanceAge();
        float timerStep = (float)Simulation.GetTimerStep();
        VisualAttackCooldown -= timerStep;

        float centerX = WorldX + Size / 2f;
        float centerY = WorldY + Size / 2f;
        float deltaX = playerWorldX - centerX;
        float deltaY = playerWorldY - centerY;
        float distance = Math.Max(1.0f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
        float directionX = deltaX / distance;
        float directionY = deltaY / distance;

        if (!UpdateAwareness(distance))
        {
            if (RegenerationRate > 0 && Hp < MaxHp)
            {
                RegenerationBuffer += RegenerationRate * timerStep;
                int recovered = (int)RegenerationBuffer;
                Hp = Math.Min(MaxHp, Hp + recovered);
                RegenerationBuffer -= recovered;
            }
            Wander(battleground);
            FinishMovementTracking();
            return;
        }

        // Skirmishers weave in open ground, producing a distinct approach without
        // changing collision behavior at walls.
        if (Archetype == "skirmisher")
        {
            float weave = MathF.Sin(Age * .055f) * .42f;
            (directionX, directionY) = (directionX - directionY * weave, directionY + directionX * weave);
            float length = Math.Max(1.0f, MathF.Sqrt(directionX * directionX + directionY * directionY));
            directionX /= length;
            directionY /= length;
        }
        else if (Encounter is not null && CombatRole == "pressure")
        {
            float flank = CombatSide * (.12f + .04f * TierRank);
            (directionX, directionY) = (directionX - directionY * flank, directionY + directionX * flank);
            float length = Math.Max(1.0f, MathF.Sqrt(directionX * directionX + directionY * directionY));
            directionX /= length;
            directionY /= length;
        }
        else if (EncounterCombatTarget.HasValue && (CombatRole == "tank" || CombatRole == "support"))
        {
            float targetDx = EncounterCombatTarget.Value.X - centerX;
            float targetDy = EncounterCombatTarget.Value.Y - centerY;
            float targetDistance = MathF.Sqrt(targetDx * targetDx + targetDy * targetDy);
            if (targetDistance > Simulation.TileSize * .65f)
            {
                directionX = targetDx / targetDistance;
                directionY = targetDy / targetDistance;
            }
        }

        float lunge = 1.0f;
        if (TierRank > 1 && distance <= Simulation.TileSize * 4f)
        {
            if (VisualAttackCooldown <= 0)
            {
                MarkAttack(.28f);
                VisualAttackCooldown = Simulation.FrameRate * (TierRank == 2 ? 2.8f : 2.0f);
            }
            if (VisualAttackTimer > 0)
                lunge += .22f * (TierRank - 1);
        }
        float movementSpeed = Math.Min(Speed * lunge, context.MovementSpeedCap);
        float step = movementSpeed * (float)Simulation.GetFrameScale();

        // Axis separation is the important behavior change: a blocked perpendicular
        // component is discarded while the wall-parallel component proceeds in full.
        // There are no partial retries to flip between on consecutive frames.
        TryAxisMove(directionX * step, "x", battleground);
        TryAxisMove(directionY * step, "y", battleground);

        EnsureCollisionSafePosition(battleground);

        FinishMovementTracking();
    }

    private void FindNearestCollisionSafePosition(Battleground battleground)
    {
        int step = Math.Max(1, Battleground.TileSize / 8);
        for (int distance = step; distance <= Battleground.TileSize; distance += step)
        {
            for (int offsetX = -distance; offsetX <= distance; offsetX += step)
            {
                foreach (int offsetY in new[] { -distance, distance })
                {
                    if (!PositionHitsWall(WorldX + offsetX, WorldY + offsetY, battleground))
                    {
                        WorldX += offsetX;
                        WorldY += offsetY;
                        return;
                    }
                }
            }
            for (int offsetY = -distance + step; offsetY < distance; offsetY += step)
            {
                foreach (int offsetX in new[] { -distance, distance })
                {
                    if (!PositionHitsWall(WorldX + offsetX, WorldY + offsetY, battleground))
                    {
                        WorldX += offsetX;
                        WorldY += offsetY;
                        return;
                    }
                }
            }
        }
    }

    public virtual IReadOnlyList<(string Part, Rectangle Rect)> GetWorldHitboxes()
    {
        _singleWorldHitbox[0] = ("body", WorldRect());
        return _singleWorldHitbox;
    }

    public virtual IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(
        Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        _singleScreenHitbox[0] = (
            "body",
            new Rectangle(
                (int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size));
        return _singleScreenHitbox;
    }

    public virtual HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        int rounded = (int)Math.Round(amount);
        Hp -= rounded;
        return new HitResult(true, Hp <= 0, rounded);
    }

    public virtual bool IsDead() => Hp <= 0;

    public void ApplyKnockback(float deltaX, float deltaY, Battleground battleground)
    {
        if (!ReceivesKnockback)
            return;
        TryAxisMove(deltaX, "x", battleground);
        TryAxisMove(deltaY, "y", battleground);
    }

    public virtual void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        EnemyRenderPose pose = RenderPose(camera, playerWorldPosition, screenShake);
        Rectangle rect = pose.Rect;
        Color bodyColor = pose.HitFlash ? UiTheme.Cream : Color;

        EnemyVisualProfile visual = SoulVisualLanguage.Enemy(
            ContentPath ?? GamePaths.Active().Key,
            Family,
            DifficultyTier);
        EnemyVisualRenderer.DrawBody(
            spriteBatch, pose, visual, bodyColor, Size, Archetype,
            BehaviorModifier, ModifierColor,
            NewGamePlusLevelApplied);

        DrawStatusAccents(spriteBatch, pose);

        if (Hp < MaxHp)
        {
            var bar = new Rectangle(rect.X, rect.Y - 9, rect.Width, 5);
            Primitives2D.FillRect(spriteBatch, bar, UiTheme.Ink);
            var fill = bar;
            fill.Width = (int)(bar.Width * Math.Max(0f, (float)Hp / MaxHp));
            Primitives2D.FillRect(spriteBatch, fill, UiTheme.Red);
        }
    }

    public EnemyRenderPose RenderPose(
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(
            new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        float walk = Moved ? MathF.Sin(Age * (.16f + TierRank * .018f)) : 0f;
        int bob = (int)(Math.Abs(walk) * Math.Min(4f, Size * .055f));
        float squash = Moved ? Math.Abs(walk) * .045f : 0f;
        float attackPulse = VisualAttackPulse;
        squash -= attackPulse * .12f;

        int width = (int)(Size * (1 + squash)), height = (int)(Size * (1 - squash));
        float midBottomX = screenPosition.X + Size / 2f, midBottomY = screenPosition.Y + Size - bob;
        var rect = new Rectangle((int)(midBottomX - width / 2f), (int)(midBottomY - height), width, height);
        Vector2 facing = camera.WorldVectorToScreen(_visualFacing);
        if (facing.LengthSquared() > .0001f)
            facing.Normalize();
        else
            facing = Vector2.UnitX;
        return new EnemyRenderPose(
            rect,
            new Vector2(rect.Center.X, rect.Center.Y),
            facing,
            camera.WorldVectorToScreen(Vector2.UnitX),
            camera.WorldVectorToScreen(Vector2.UnitY),
            walk,
            attackPulse,
            VisualHitTimer > 0,
            _hasFacing);
    }

    private void DrawStatusAccents(SpriteBatch spriteBatch, EnemyRenderPose pose)
    {
        if (StatusEffects.Count == 0)
            return;
        int index = 0;
        foreach (string key in StatusEffects.Keys)
        {
            float phase = Age * .08f + index * 1.7f;
            Vector2 point = pose.Center + new Vector2(
                (index - (StatusEffects.Count - 1) / 2f) * Math.Max(5, Size * .13f),
                -Size * .58f + MathF.Sin(phase) * 2f);
            Color statusColor = key switch
            {
                "bleed" => UiTheme.Red,
                "dread" => UiTheme.Purple,
                "bane" => UiTheme.Gold,
                _ => UiTheme.Green,
            };
            int size = Math.Max(2, (int)(Size * .055f));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)point.X - size / 2, (int)point.Y - size / 2, size, size),
                statusColor);
            index++;
            if (index >= 5)
                break;
        }
    }
}
