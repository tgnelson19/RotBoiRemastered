using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Ported from bossTypes.py's `BossDefinition`. `Factory` replaces
/// `boss_class: type` + the `spawn_rect.x, spawn_rect.y, rng=rng` call
/// Python built from it -- same `EnemyFactory`-delegate cleanup as
/// `Entities/EnemyCatalog.cs`. The signature is uniform across every boss
/// even though most ignore some parameters (`Battleground`/`awarenessRange`
/// only matter to `Dissonance`/`Beaudis` respectively; the `PathChaseBoss`
/// family needs `Battleground` for its arena center but hardcodes an
/// infinite awareness range internally, matching Python's own
/// `self.awarenessRange = float("inf")`).
/// </summary>
public delegate Enemy BossFactory(float worldX, float worldY, Battleground battleground, float awarenessRange, Random? rng);

public sealed record BossDefinition(string Key, string DisplayName, BossFactory Factory);

/// <summary>
/// Ported from bossTypes.py's `BossCatalog`/`BOSS_CATALOG`. Registers every
/// boss now ported: `beaudis`/`dissonance` (the "sound" content path's
/// mid/final bosses -- the only path natural gameplay can currently reach,
/// since `gamePaths.py`'s per-path boss-key selection isn't wired), the
/// Touch path's `bair`/`sting`, the sight-themed `ishe`/`chronos`, the
/// Chemesthesis path's `kage`/`rot`, and the Phantasia path's
/// `hypno`/`malady`.
/// </summary>
public sealed class BossCatalog
{
    private readonly Dictionary<string, BossDefinition> _definitions = new();

    public static BossCatalog Shared { get; } = CreateDefault();

    public void Register(BossDefinition definition) => _definitions[definition.Key] = definition;

    public bool TryGet(string key, out BossDefinition? definition) => _definitions.TryGetValue(key, out definition);

    /// <summary>Ported from BossCatalog.spawn(): places the boss at the arena center's nearest open footprint (same shape as GameSession.SpawnBoss's default path, kept here for a caller that doesn't already own a GameSession).</summary>
    public Enemy Spawn(string key, Battleground battleground, float awarenessRange, Random? rng = null)
    {
        var definition = _definitions[key];
        float size = Simulation.TileSize * 1.9f;
        float centerX = battleground.Width * Simulation.TileSize / 2f;
        float centerY = battleground.Height * Simulation.TileSize / 2f;
        var requested = new Rectangle((int)(centerX - size / 2f), (int)(centerY - size / 2f), (int)size, (int)size);
        var spawnRect = battleground.FindNearestOpenRect(requested);
        return definition.Factory(spawnRect.X, spawnRect.Y, battleground, awarenessRange, rng);
    }

    public static BossCatalog CreateDefault()
    {
        var catalog = new BossCatalog();
        catalog.Register(new BossDefinition("beaudis", "Beaudis", (x, y, _, awareness, rng) => new Beaudis(x, y, awareness, rng)));
        catalog.Register(new BossDefinition("dissonance", "Dissonance", (x, y, battleground, awareness, rng) => new Dissonance(x, y, awareness, battleground, rng)));
        catalog.Register(new BossDefinition("ishe", "Ishe", (x, y, battleground, _, rng) => new Ishe(x, y, battleground, rng)));
        catalog.Register(new BossDefinition("chronos", "Chronos", (x, y, battleground, _, rng) => new Chronos(x, y, battleground, rng)));
        catalog.Register(new BossDefinition("bair", "Bair", (x, y, battleground, _, rng) => new Bair(x, y, battleground, rng)));
        catalog.Register(new BossDefinition("sting", "Sting", (x, y, battleground, _, rng) => new Sting(x, y, battleground, rng)));
        catalog.Register(new BossDefinition("kage", "Kage", (x, y, battleground, _, rng) => new Kage(x, y, battleground, rng)));
        catalog.Register(new BossDefinition("rot", "Rot", (x, y, battleground, _, rng) => new Rot(x, y, battleground, rng)));
        catalog.Register(new BossDefinition("hypno", "Hypno", (x, y, battleground, _, rng) => new Hypno(x, y, battleground, rng)));
        catalog.Register(new BossDefinition("malady", "Malady", (x, y, battleground, _, rng) => new Malady(x, y, battleground, rng)));
        return catalog;
    }
}
