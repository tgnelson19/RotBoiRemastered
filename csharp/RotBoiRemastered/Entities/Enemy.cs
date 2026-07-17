using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>Ported from enemy.py's HitResult frozen dataclass.</summary>
public sealed record HitResult(bool Applied, bool Killed, double Amount = 0, bool Blocked = false);

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
/// </summary>
public sealed class EnemyUpdateContext
{
    public required float PlayerWorldX { get; init; }
    public required float PlayerWorldY { get; init; }
    public required Battleground Battleground { get; init; }
    public List<EnemyProjectile> ProjectileSink { get; init; } = new();
    public IReadOnlyList<Enemy> AllEnemies { get; init; } = Array.Empty<Enemy>();
    public List<ExperienceBubble> ExperienceBubbles { get; init; } = new();
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
    public string AwarenessState { get; set; } = "wandering";
    public float AwarenessRange { get; }
    public float DisengageRange { get; }
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
    public RuntimeEncounter? Encounter { get; set; }
    public int EncounterSlot { get; set; }
    public Vector2? EncounterPatrolTarget { get; set; }
    public Vector2? EncounterCombatTarget { get; set; }
    public int CombatSide { get; set; }
    public string Family { get; set; } = "basic";
    public string? SpawnDefinitionKey { get; set; }
    public string? EncounterKey { get; set; }
    public bool AtomicSpawnGroup { get; set; }
    public float? AttackCooldownMax { get; set; }
    public float? AttackCooldown { get; set; }

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

    private Vector2 _lastVisualWorld;
    private readonly Random _rng;

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

    public void MarkAttack(float duration = .22f) => VisualAttackTimer = Math.Max(VisualAttackTimer, Simulation.FrameRate * duration);

    public Rectangle WorldRect() => WorldRectAt(WorldX, WorldY);

    private Rectangle WorldRectAt(float x, float y) => new((int)x, (int)y, (int)Size, (int)Size);

    protected bool TryAxisMove(float amount, string axis, Battleground battleground)
    {
        if (amount == 0)
            return false;
        float nextX = axis == "x" ? WorldX + amount : WorldX;
        float nextY = axis == "y" ? WorldY + amount : WorldY;
        var candidate = WorldRectAt(nextX, nextY);
        if (battleground.RectHitsWall(candidate))
            return false;
        WorldX = nextX;
        WorldY = nextY;
        return true;
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
        Moved = Vector2.Distance(current, _lastVisualWorld) > .02f;
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
        float step = Speed * lunge * (float)Simulation.GetFrameScale();

        // Axis separation is the important behavior change: a blocked perpendicular
        // component is discarded while the wall-parallel component proceeds in full.
        // There are no partial retries to flip between on consecutive frames.
        TryAxisMove(directionX * step, "x", battleground);
        TryAxisMove(directionY * step, "y", battleground);

        if (battleground.RectHitsWall(WorldRect()))
        {
            var safeRect = battleground.FindNearestOpenRect(WorldRect());
            WorldX = safeRect.X;
            WorldY = safeRect.Y;
        }

        FinishMovementTracking();
    }

    public virtual IReadOnlyList<(string Part, Rectangle Rect)> GetWorldHitboxes() => new[] { ("body", WorldRect()) };

    public virtual IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(
        Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        return new[] { ("body", new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size)) };
    }

    public virtual HitResult TakeDamage(double amount, string partId = "body")
    {
        int rounded = (int)Math.Round(amount);
        Hp -= rounded;
        return new HitResult(true, Hp <= 0, rounded);
    }

    public virtual bool IsDead() => Hp <= 0;

    public void ApplyKnockback(float deltaX, float deltaY, Battleground battleground)
    {
        TryAxisMove(deltaX, "x", battleground);
        TryAxisMove(deltaY, "y", battleground);
    }

    public virtual void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        float walk = Moved ? MathF.Sin(Age * (.16f + TierRank * .018f)) : 0f;
        int bob = (int)(Math.Abs(walk) * Math.Min(4f, Size * .055f));
        float squash = Moved ? Math.Abs(walk) * .045f : 0f;
        if (VisualAttackTimer > 0)
        {
            float attackProgress = VisualAttackTimer / Math.Max(1f, Simulation.FrameRate * .22f);
            squash -= MathF.Sin(Math.Min(1f, attackProgress) * MathF.PI) * .12f;
        }

        int width = (int)(Size * (1 + squash)), height = (int)(Size * (1 - squash));
        float midBottomX = screenPosition.X + Size / 2f, midBottomY = screenPosition.Y + Size - bob;
        var rect = new Rectangle((int)(midBottomX - width / 2f), (int)(midBottomY - height), width, height);

        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 4, rect.Y + 4, rect.Width, rect.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, Color);
        int borderWidth = Math.Max(2, (int)(Size * (Archetype == "bulwark" ? .1f : .07f)));
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Ink, borderWidth);

        if (BehaviorModifier is not null)
        {
            int pip = Math.Max(4, (int)(Size * .11f));
            Primitives2D.FillRect(spriteBatch, new Rectangle(rect.Right - pip - 3, rect.Y + 3, pip + 2, pip + 2), UiTheme.Ink);
            Primitives2D.FillRect(spriteBatch, new Rectangle(rect.Right - pip - 2, rect.Y + 4, pip, pip), ModifierColor ?? UiTheme.Ink);
        }

        switch (Archetype)
        {
            case "runner":
                var points = new[]
                {
                    new Vector2(rect.Center.X, rect.Y + rect.Height * .2f),
                    new Vector2(rect.Right - rect.Width * .2f, rect.Center.Y),
                    new Vector2(rect.Center.X, rect.Bottom - rect.Height * .2f),
                    new Vector2(rect.X + rect.Width * .2f, rect.Center.Y),
                };
                Primitives2D.FillPolygon(spriteBatch, points, UiTheme.Lighten(Color, 55));
                break;
            case "bulwark":
                var inset = rect;
                inset.Inflate(-(int)(Size * .34f), -(int)(Size * .34f));
                Primitives2D.RectOutline(spriteBatch, inset, UiTheme.Lighten(Color, 38), 3);
                break;
            case "skirmisher":
                int crossWidth = Math.Max(2, (int)(Size * .08f));
                Primitives2D.Line(spriteBatch, new Vector2(rect.Center.X, rect.Top), new Vector2(rect.Center.X, rect.Bottom), UiTheme.Lighten(Color, 60), crossWidth);
                Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Center.Y), new Vector2(rect.Right, rect.Center.Y), UiTheme.Lighten(Color, 60), crossWidth);
                break;
            default:
                var highlight = rect;
                highlight.Inflate(-(int)(Size * .48f), -(int)(Size * .48f));
                Primitives2D.FillRect(spriteBatch, highlight, UiTheme.Lighten(Color, 42));
                break;
        }

        if (Hp < MaxHp)
        {
            var bar = new Rectangle(rect.X, rect.Y - 9, rect.Width, 5);
            Primitives2D.FillRect(spriteBatch, bar, UiTheme.Ink);
            var fill = bar;
            fill.Width = (int)(bar.Width * Math.Max(0f, (float)Hp / MaxHp));
            Primitives2D.FillRect(spriteBatch, fill, UiTheme.Red);
        }
    }
}
