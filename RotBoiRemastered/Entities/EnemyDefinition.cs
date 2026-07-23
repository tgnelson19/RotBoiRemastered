using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Everything EnemyCatalog.Create needs to build one concrete enemy at a
/// given world position/level. Ported from the base-stat arguments
/// enemyTypes.py's `create()` computed and forwarded into whichever
/// `enemy_class` a definition named. `Battleground` is nullable/optional --
/// only BannerCaptain's factory uses it (to place its minions without wall
/// overlap); every other factory ignores it.
/// </summary>
public sealed record EnemyConstructionArgs(
    float WorldX, float WorldY, float Speed, float Size, Color Color, double Damage, double Hp,
    double ExpValue, double Difficulty, float AwarenessRange, string DifficultyTier, int TierRank, Random Rng,
    Battleground? Battleground = null);

/// <summary>Builds one concrete Enemy subtype from its construction args.</summary>
public delegate Enemy EnemyFactory(EnemyConstructionArgs args);

/// <summary>
/// One registered enemy type/tier. Ported from enemyTypes.py's
/// EnemyDefinition frozen dataclass.
///
/// Cleanup vs. the Python original: `enemy_class: type` plus an `options:
/// dict` forwarded as `**kwargs` (with a `definition.enemy_class is
/// SnakeEnemy` type-identity special case in `create()` to inject
/// `segment_count`) become one `Factory` delegate instead. Every
/// subtype-specific detail -- which constructor to call, what tier string
/// or phase order to bake in, the segment-count formula -- lives in the
/// closure built at registration time, so `create()` never needs to know
/// what concrete type it's building or branch on it by identity.
/// </summary>
public sealed record EnemyDefinition(
    string Key, EnemyFactory Factory, double Weight, int MinLevel,
    double Speed, double Size, double Damage, double Health, double Experience, Color Color,
    double ThreatCost = 1.0, string Family = "basic", int MaxActive = 99,
    bool GuaranteedOnly = false, int MaxLevel = 20, string ProgressionTier = "easy",
    string? SpawnPath = null);
