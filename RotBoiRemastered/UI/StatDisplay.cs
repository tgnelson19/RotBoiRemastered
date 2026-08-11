using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

public sealed record StatDisplayDefinition(
    string Id,
    string Abbreviation,
    string IconKey,
    Func<RunState, string> Format,
    string Tooltip);

/// <summary>Shared ordering and compact formatting for player-facing stats.</summary>
public static class StatDisplay
{
    public static readonly IReadOnlyList<StatDisplayDefinition> Definitions =
    [
        new("damage", "DMG", "Bullet Damage", s => $"{s.BulletDamage:N0}", "Damage dealt by each normal projectile hit."),
        new("attack_rate", "A.R.", "Attack Speed", s => $"{AttacksPerSecond(s):0.00}/s", "Volleys fired each second."),
        new("shots", "SHOT", "Bullet Count", s => $"{ExpectedProjectiles(s):0.00}", "Expected projectiles per volley; the decimal is the bonus-shot chance."),
        new("crit_chance", "C.%", "Crit Chance", s => $"{s.CritChance * 100:0}%", "Chance for a projectile to deal critical damage."),
        new("crit_damage", "C.D.", "Crit Damage", s => $"+{Math.Max(0, s.CritDamage - 1) * 100:0}%", "Extra damage dealt by a critical hit."),
        new("pierce", "PRC", "Bullet Pierce", s => $"{ExpectedEnemiesHit(s):0.00}", "Expected enemies hit by one projectile, including its first target."),
        new("defense", "DEF", "Defense", s => $"{s.Defense:N0}", "Flat damage removed from each incoming hit."),
        new("vitality", "VIT", "Vitality", s => $"{s.Vitality:N0}/s", "Health recovered each second."),
        new("move_speed", "MOV", "Player Speed", s => $"{s.PlayerSpeed:0.00}", "Player movement speed."),
        new("range", "RNG", "Bullet Range", s => $"{s.BulletRange / Simulation.TileSize:0.0}t", "Projectile travel distance in tiles."),
        new("bullet_speed", "B.S.", "Bullet Speed", s => $"{s.BulletSpeed:0.00}", "Projectile travel speed."),
        new("bullet_size", "SIZE", "Bullet Size", s => $"{s.BulletSize:0.00}x", "Projectile size multiplier."),
    ];

    public static double AttacksPerSecond(RunState state) =>
        Simulation.FrameRate / Math.Max(1, state.AttackCooldownStat);

    public static double ExpectedProjectiles(RunState state) => state.ProjectileCount;

    public static double ExpectedEnemiesHit(RunState state) => state.BulletPierce + 1;

    public static StatDisplayDefinition ById(string id) =>
        Definitions.First(definition => definition.Id == id);
}
