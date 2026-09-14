using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

public enum EgoTerrain { Plains, City, Caverns }

/// <summary>
/// Large-scale terrain assignment for The Ego: a handful of Voronoi seeds,
/// each owning one terrain kind, with the distance field warped by value
/// noise so borders wander and bleed instead of cutting straight lines.
/// Independent of sense holdouts, which are stamped on top afterwards.
/// </summary>
public sealed class EgoRegionField
{
    public sealed record Seed(Point Tile, EgoTerrain Terrain);

    public IReadOnlyList<Seed> Seeds { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Noise warp amplitude in tiles (≈ 0.6 view).</summary>
    public float WarpTiles { get; }
    /// <summary>Half-width of the bleed band in tiles (≈ 0.5 view).</summary>
    public float BorderBandTiles { get; }

    private readonly int _noiseSeed;
    private readonly EgoTerrain[,] _terrainCache;
    private readonly float[,] _blendCache;
    private readonly EgoTerrain[,] _secondCache;

    public EgoRegionField(Random rng, int width, int height, int viewTiles, int seedCount = 12)
    {
        Width = width;
        Height = height;
        WarpTiles = viewTiles * .6f;
        BorderBandTiles = viewTiles * .5f;
        _noiseSeed = rng.Next();
        var seeds = new List<Seed>();
        float minSpacing = viewTiles * 1.5f;
        // Spawn sits on open plains so the first minutes are readable.
        seeds.Add(new Seed(new Point(width / 2, height / 2), EgoTerrain.Plains));
        var kinds = new List<EgoTerrain> { EgoTerrain.City, EgoTerrain.Caverns, EgoTerrain.Plains };
        for (int attempt = 0; attempt < 4000 && seeds.Count < seedCount; attempt++)
        {
            var tile = new Point(rng.Next(12, width - 12), rng.Next(12, height - 12));
            if (seeds.Any(other => Vector2.Distance(other.Tile.ToVector2(), tile.ToVector2()) < minSpacing))
                continue;
            // Cycle the first picks so every terrain is guaranteed, then random.
            EgoTerrain terrain = seeds.Count <= 3
                ? kinds[(seeds.Count - 1) % kinds.Count]
                : (EgoTerrain)rng.Next(3);
            seeds.Add(new Seed(tile, terrain));
        }
        Seeds = seeds;
        _terrainCache = new EgoTerrain[height, width];
        _secondCache = new EgoTerrain[height, width];
        _blendCache = new float[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                Compute(x, y);
    }

    public EgoTerrain TerrainAt(int x, int y) => _terrainCache[y, x];
    /// <summary>Terrain of the runner-up seed at this tile (what the border bleeds into).</summary>
    public EgoTerrain SecondTerrainAt(int x, int y) => _secondCache[y, x];
    /// <summary>0 deep inside a region, rising to 1 exactly on the warped border.</summary>
    public float BorderBlend(int x, int y) => _blendCache[y, x];

    private void Compute(int x, int y)
    {
        // Two-octave value noise displaces the sample point before the
        // nearest-seed test, so region edges meander.
        float wx = x + (Noise(x * .035f, y * .035f, 0) * 2f - 1f) * WarpTiles
            + (Noise(x * .09f, y * .09f, 7) * 2f - 1f) * WarpTiles * .4f;
        float wy = y + (Noise(x * .035f, y * .035f, 3) * 2f - 1f) * WarpTiles
            + (Noise(x * .09f, y * .09f, 11) * 2f - 1f) * WarpTiles * .4f;
        float best = float.MaxValue, second = float.MaxValue;
        Seed? bestSeed = null, secondSeed = null;
        foreach (Seed seed in Seeds)
        {
            float d = Vector2.Distance(new Vector2(wx, wy), seed.Tile.ToVector2());
            if (d < best)
            {
                second = best; secondSeed = bestSeed;
                best = d; bestSeed = seed;
            }
            else if (d < second)
            {
                second = d; secondSeed = seed;
            }
        }
        _terrainCache[y, x] = bestSeed!.Terrain;
        _secondCache[y, x] = (secondSeed ?? bestSeed).Terrain;
        _blendCache[y, x] = Math.Clamp(1f - (second - best) / Math.Max(1f, BorderBandTiles), 0f, 1f);
    }

    /// <summary>Deterministic 0..1 value noise with bilinear interpolation.</summary>
    internal float Noise(float x, float y, int salt)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(x0, y0, salt), b = Hash(x0 + 1, y0, salt);
        float c = Hash(x0, y0 + 1, salt), d = Hash(x0 + 1, y0 + 1, salt);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
    }

    internal float Hash(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1274126177 + _noiseSeed);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
