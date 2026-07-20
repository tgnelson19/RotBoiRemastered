using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.World;

/// <summary>
/// Shared per-enemy stat multipliers for one content path. Ported from
/// gamePaths.py's EnemyStyle dataclass. Colors/Tags are nullable rather than
/// defaulting to an empty array (C# doesn't allow non-constant default
/// parameter values) -- null means "no override," matching how the Python
/// original's `if style.colors:` truthiness check treated an empty tuple.
/// </summary>
public sealed record EnemyStyle(
    double Speed = 1.0,
    double Size = 1.0,
    double Health = 1.0,
    double Damage = 1.0,
    double AttackCooldown = 1.0,
    double AttackRange = 1.0,
    double Awareness = 1.0,
    double Experience = 1.0,
    double ProjectileSpeed = 1.0,
    double ProjectileSize = 1.0,
    double ProjectileDamage = 1.0,
    double ProjectileRange = 1.0,
    double? ProjectileLifetime = null,
    double AimedChance = 1.0,
    IReadOnlyList<Color>? Colors = null,
    IReadOnlyList<string>? Tags = null);

/// <summary>Ported from gamePaths.py's GamePath dataclass.</summary>
public sealed record GamePath(
    string Key, string Title, string Subtitle, string Description,
    string MidBoss, string FinalBoss, Color Accent, EnemyStyle Style);

/// <summary>
/// Data-driven content paths layered over the shared run systems. Selection,
/// battleground generation, boss rosters, enemy identity, and hostile
/// projectile tuning are wired into GameSession. The optional Python-only
/// exclusive-encounter extension registry has no registered consumers.
/// </summary>
public static class GamePaths
{
    private static readonly Random ProjectileRng = new(9071);
    // Order matters here (matches Python 3.7+ dict insertion order, which
    // gamePaths.py relies on for e.g. title-screen path cycling) -- Paths is
    // the ordered source of truth; PathsByKey is only for O(1) lookup.
    public static readonly IReadOnlyList<GamePath> Paths = new[]
    {
        new GamePath("sound", "PATH OF SOUND", "THE DISSONANCE",
            "The original arena: fast patterns, echoes, and open ground.",
            "beaudis", "dissonance", new Color(207, 191, 151), new EnemyStyle()),
        new GamePath("touch", "PATH OF TOUCH", "THE WEIGHT BELOW",
            "A cramped prison sewer of heavy bodies and slow, punishing shots.",
            "bair", "rot", new Color(91, 132, 74), new EnemyStyle(
                Speed: .70, Size: 1.22, Health: 1.65, Damage: 1.28,
                AttackCooldown: 1.48, Awareness: .92, Experience: 1.22,
                ProjectileSpeed: .68, ProjectileSize: 1.24, ProjectileDamage: 1.28,
                Colors: new[] { new Color(79, 101, 55), new Color(103, 91, 55), new Color(54, 83, 55), new Color(116, 105, 63) },
                Tags: new[] { "rotton", "heavy" })),
        new GamePath("sight", "PATH OF SIGHT", "THE QUICKENED HORIZON",
            "An exposed field of small, fragile hunters and close, rapid attacks.",
            "ishe", "chronos", new Color(104, 190, 222), new EnemyStyle(
                Speed: 1.45, Size: .76, Health: .56, Damage: .78,
                AttackCooldown: .58, AttackRange: .58, Awareness: 1.08,
                Experience: .82, ProjectileSpeed: 1.55, ProjectileSize: .72,
                ProjectileDamage: .76, ProjectileRange: .48,
                Colors: new[] { new Color(105, 190, 220), new Color(135, 210, 230), new Color(228, 142, 63), new Color(244, 174, 82) },
                Tags: new[] { "sighted", "quick", "close_range" })),
        new GamePath("chemesthesis", "PATH OF CHEMESTHESIS", "THE BURNING FIELD",
            "Durable carriers seed long-lived, mostly unaimed hazards everywhere.",
            "kage", "ache", new Color(207, 83, 45), new EnemyStyle(
                Speed: .92, Size: 1.06, Health: 2.15, Damage: 1.02,
                AttackCooldown: .94, Awareness: 1.05, Experience: 1.34,
                ProjectileSpeed: .62, ProjectileSize: 1.08, ProjectileDamage: .88,
                ProjectileRange: 4.0, ProjectileLifetime: 18.0, AimedChance: .18,
                Colors: new[] { new Color(171, 62, 36), new Color(211, 91, 38), new Color(92, 120, 50), new Color(126, 48, 39) },
                Tags: new[] { "chemesthetic", "minefield", "durable" })),
        new GamePath("phantasia", "PATH OF PHANTASIA", "THE ORNATE DREAM",
            "Broad dream courts surround a few extravagant, feature-heavy ruins.",
            "hypno", "malady", new Color(190, 83, 175), new EnemyStyle(
                Speed: 1.02, Size: 1.03, Health: 1.12, Damage: 1.02,
                AttackCooldown: .96, Experience: 1.08,
                ProjectileSize: 1.08, ProjectileRange: 1.15,
                Colors: new[] { new Color(117, 48, 121), new Color(161, 57, 147), new Color(202, 85, 174), new Color(91, 48, 119) },
                Tags: new[] { "phantasian", "ornate" })),
    };

    public static readonly IReadOnlyDictionary<string, GamePath> PathsByKey =
        Paths.ToDictionary(path => path.Key);

    // Module-level mutable state, same as Python's selected_key/active_key.
    // No test class other than GamePathsTests touches this yet -- if one
    // ever does, it'll need the same [Collection("...")] treatment as
    // GameProfileTests/KeybindsTests (see GameProfileStateCollection.cs).
    private static string _selectedKey = "sound";
    private static string _activeKey = "sound";

    public static GamePath Selected() => PathsByKey[_selectedKey];

    public static GamePath Active() => PathsByKey[_activeKey];

    public static void Select(string key)
    {
        if (!PathsByKey.ContainsKey(key))
            throw new KeyNotFoundException($"Unknown game path: {key}");
        _selectedKey = key;
    }

    public static void Cycle(int direction)
    {
        var keys = Paths.Select(path => path.Key).ToList();
        int index = keys.IndexOf(_selectedKey);
        int nextIndex = ((index + direction) % keys.Count + keys.Count) % keys.Count;
        Select(keys[nextIndex]);
    }

    /// <summary>
    /// Activates the selected path and generates its battleground. Unlike
    /// Python's activate_selected() (which reassigned background.py's
    /// module-level currRoomRects etc. in place), this returns the new
    /// Battleground -- there's no hidden global "current battleground" to
    /// mutate here, so the caller is responsible for holding onto it. That
    /// owner will most likely be whatever session/world container gets
    /// built when Entities/ and Core/RotBoiGame.cs's state machine are wired
    /// together.
    /// </summary>
    public static Battleground ActivateSelected()
    {
        _activeKey = _selectedKey;
        return Battleground.CreateForPath(_activeKey);
    }

    public static string BossKey(bool midpoint) => midpoint ? Active().MidBoss : Active().FinalBoss;

    public static bool IsTouch() => _activeKey == "touch";

    /// <summary>Applies the active path's shared enemy identity exactly once.</summary>
    public static Enemy? ApplyEnemyIdentity(Enemy? enemy)
    {
        var path = Active();
        var style = path.Style;
        if (enemy is null || path.Key == "sound" || enemy.ContentPath == path.Key)
            return enemy;

        enemy.ContentPath = path.Key;
        enemy.Speed *= (float)style.Speed;
        enemy.Size *= (float)style.Size;
        enemy.MaxHp = (int)Math.Round(enemy.MaxHp * style.Health);
        enemy.Hp = enemy.MaxHp;
        enemy.Damage = (int)Math.Round(enemy.Damage * style.Damage);
        enemy.ExpValue *= style.Experience;
        if (style.Colors is { Count: > 0 })
            enemy.Color = style.Colors[enemy.Family.Sum(character => character) % style.Colors.Count];
        if (enemy.AttackCooldownMax.HasValue)
            enemy.AttackCooldownMax *= (float)style.AttackCooldown;
        if (enemy.AttackCooldown.HasValue)
            enemy.AttackCooldown *= (float)style.AttackCooldown;
        enemy.ScaleAttackRange(style.AttackRange);
        enemy.AwarenessRange *= (float)style.Awareness;
        enemy.DisengageRange *= (float)style.Awareness;
        enemy.InteractionTags = enemy.InteractionTags.Concat(style.Tags ?? Array.Empty<string>()).ToHashSet();
        foreach (var child in enemy.SpawnedEnemies)
            ApplyEnemyIdentity(child);
        return enemy;
    }

    /// <summary>Applies active-path projectile rules to newly emitted hostile shots.</summary>
    public static void TuneNewProjectiles(IEnumerable<EnemyProjectile> projectiles)
    {
        var path = Active();
        var style = path.Style;
        if (path.Key == "sound")
            return;

        foreach (var projectile in projectiles)
        {
            if (projectile.ContentPath == path.Key)
                continue;
            projectile.ContentPath = path.Key;
            projectile.Speed *= (float)style.ProjectileSpeed;
            projectile.Size *= (float)style.ProjectileSize;
            projectile.Damage = MathF.Round(projectile.Damage * (float)style.ProjectileDamage);
            projectile.RemainingRange *= (float)style.ProjectileRange;
            if (style.ProjectileLifetime.HasValue)
                projectile.Lifetime = Math.Max(projectile.Lifetime ?? 0, (float)style.ProjectileLifetime.Value);
            if (style.AimedChance < 1 && ProjectileRng.NextDouble() > style.AimedChance)
                projectile.Direction += (float)(ProjectileRng.NextDouble() * Math.PI * 2 - Math.PI);
            if (style.Colors is { Count: > 0 })
                projectile.Color = style.Colors[^1];
        }
    }
}
