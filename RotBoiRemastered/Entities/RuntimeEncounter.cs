using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Persistent coordination shared by every ordinary encounter group. Ported
/// from enemyTypes.py's RuntimeEncounter.
///
/// Cleanup vs. the Python original: `activationRange` was computed from a
/// screen-height global (`vH.sH * (.48 + ...)`) inside the constructor --
/// takes `screenHeight` as an explicit parameter instead, same cleanup as
/// Enemy.AwarenessRange.
/// </summary>
public sealed class RuntimeEncounter
{
    private static int _nextId = 1;

    public int Id { get; }
    public string Key { get; }
    public List<Enemy> Members { get; }
    public Vector2 Anchor { get; private set; }
    public int Level { get; }
    public string State { get; private set; } = "patrolling";
    public float PatrolAngle { get; private set; }
    public float PatrolTimer { get; private set; }
    public float ActivationRange { get; }
    public float DisengageRange { get; }
    public bool EngagementAllowed { get; private set; }
    public float AlertTimer { get; private set; }

    private readonly Random _rng;

    public RuntimeEncounter(string key, IEnumerable<Enemy> members, Vector2 anchor, int level,
        float screenHeight, Random? rng = null)
    {
        Id = _nextId++;
        _rng = rng ?? Random.Shared;
        Key = key;
        Members = new List<Enemy>(members);
        Anchor = anchor;
        Level = level;
        PatrolAngle = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
        PatrolTimer = _rng.Next(100, 221);
        ActivationRange = screenHeight * (.48f + Math.Min(20, level) * .006f);
        DisengageRange = ActivationRange * 1.45f;

        for (int index = 0; index < Members.Count; index++)
        {
            var enemy = Members[index];
            enemy.Encounter = this;
            enemy.EncounterSlot = index;
            enemy.CombatSide = index % 2 != 0 ? -1 : 1;
            if (enemy.AttackCooldown.HasValue)
                enemy.AttackCooldown += index * Simulation.FrameRate * .18f;
        }
    }

    /// <summary>Test-only: resets the shared id counter to mirror a fresh Python module import.</summary>
    public static void ResetIdCounterForTests() => _nextId = 1;

    /// <summary>
    /// The id the next constructed RuntimeEncounter will receive, without
    /// consuming it. EnemyCatalog.SpawnPatrol needs this to build a patrol's
    /// key string before the RuntimeEncounter that will carry that same id
    /// actually exists yet (mirrors Python's `f"patrol_{RuntimeEncounter._next_id}"`).
    /// </summary>
    public static int NextId => _nextId;

    public double ThreatCost => Members.Where(enemy => !enemy.IsDead()).Sum(enemy => enemy.ThreatCost);

    public Vector2 Center()
    {
        var living = Members.Where(enemy => !enemy.IsDead()).ToList();
        if (living.Count == 0)
            return Anchor;
        return new Vector2(
            living.Average(enemy => enemy.WorldX + enemy.Size / 2f),
            living.Average(enemy => enemy.WorldY + enemy.Size / 2f));
    }

    public float DistanceTo(float playerX, float playerY) => Vector2.Distance(new Vector2(playerX, playerY), Center());

    public void Update(float playerX, float playerY, Battleground battleground, bool allowed = true)
    {
        Members.RemoveAll(enemy => enemy.IsDead());
        if (Members.Count == 0)
            return;

        float distance = DistanceTo(playerX, playerY);
        if (State == "patrolling" && allowed && distance <= ActivationRange)
        {
            State = "engaged";
            AlertTimer = Simulation.FrameRate * .8f;
        }
        else if (State == "engaged" && (!allowed || distance > DisengageRange))
        {
            State = "patrolling";
        }

        EngagementAllowed = allowed && State == "engaged";
        AlertTimer = Math.Max(0f, AlertTimer - (float)Simulation.GetTimerStep());
        if (State == "patrolling")
        {
            PatrolTimer -= (float)Simulation.GetTimerStep();
            if (PatrolTimer <= 0)
            {
                PatrolAngle += (float)(_rng.NextDouble() * 2.4 - 1.2);
                var nextAnchor = Anchor + new Vector2(MathF.Cos(PatrolAngle), MathF.Sin(PatrolAngle)) * Simulation.TileSize * 2.5f;
                var candidate = new Rectangle((int)nextAnchor.X, (int)nextAnchor.Y, Simulation.TileSize, Simulation.TileSize);
                var safe = battleground.FindNearestOpenRect(candidate);
                Anchor = new Vector2(safe.X, safe.Y);
                PatrolTimer = _rng.Next(120, 261);
            }
        }

        int count = Math.Max(1, Members.Count);
        var center = Center();
        float playerDx = playerX - center.X, playerDy = playerY - center.Y;
        float playerDistance = Math.Max(1.0f, MathF.Sqrt(playerDx * playerDx + playerDy * playerDy));
        float towardX = playerDx / playerDistance, towardY = playerDy / playerDistance;

        for (int index = 0; index < Members.Count; index++)
        {
            var enemy = Members[index];
            enemy.EngagementAllowed = EngagementAllowed;
            if (EngagementAllowed)
            {
                enemy.EncounterPatrolTarget = null;
                enemy.AwarenessState = "alerted";
                if (enemy.CombatRole is "tank" or "support")
                {
                    float roleDistance = Simulation.TileSize * (enemy.CombatRole == "tank" ? 1.65f : 1.15f);
                    enemy.EncounterCombatTarget = new Vector2(center.X + towardX * roleDistance, center.Y + towardY * roleDistance);
                }
                else if (enemy.CombatRole == "artillery")
                {
                    enemy.EncounterCombatTarget = new Vector2(center.X - towardX * Simulation.TileSize, center.Y - towardY * Simulation.TileSize);
                }
                else
                {
                    enemy.EncounterCombatTarget = null;
                }
            }
            else
            {
                enemy.EncounterCombatTarget = null;
                float angle = PatrolAngle + index * 2f * MathF.PI / count;
                float radius = Simulation.TileSize * (1.0f + .25f * (index % 3));
                enemy.EncounterPatrolTarget = Anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                enemy.AwarenessState = "wandering";
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (AlertTimer <= 0 || Members.Count == 0)
            return;
        Vector2 center = camera.WorldToScreen(Center(), playerWorldPosition, screenShake);
        float progress = AlertTimer / Math.Max(1f, Simulation.FrameRate * .8f);
        float radius = Simulation.TileSize * (1.2f + progress * 1.1f);
        Primitives2D.CircleOutline(spriteBatch, center, radius, UiTheme.Ink, 6);
        Primitives2D.CircleOutline(spriteBatch, center, radius, UiTheme.Gold, 3);
        foreach (var enemy in Members)
        {
            Vector2 target = camera.WorldToScreen(
                new Vector2(enemy.WorldX + enemy.Size / 2f, enemy.WorldY + enemy.Size / 2f),
                playerWorldPosition, screenShake);
            Primitives2D.Line(spriteBatch, center, target, UiTheme.Gold, 1);
        }
    }
}
