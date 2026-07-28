using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>
/// Small uniform spatial index used by combat collision queries. Ported from
/// spatialHash.py. Genericized over the stored item type (Python's version
/// is duck-typed, so this is the direct translation, not an addition) --
/// constrained to `class` so reference-identity dedup in Query matches
/// Python's id()-keyed `seen` set exactly, regardless of whether T ever
/// grows value-equality semantics (e.g. if it's a record) later.
/// </summary>
public sealed class SpatialHash<T> where T : class
{
    private readonly int _cellSize;
    private readonly Dictionary<(int X, int Y), List<T>> _cells = new();

    public SpatialHash(int cellSize = 128)
    {
        _cellSize = Math.Max(1, cellSize);
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : (value + 1) / divisor - 1;

    public void Insert(T item, Rectangle rect)
    {
        int left = FloorDiv(rect.Left, _cellSize);
        int right = FloorDiv(rect.Right - 1, _cellSize);
        int top = FloorDiv(rect.Top, _cellSize);
        int bottom = FloorDiv(rect.Bottom - 1, _cellSize);
        for (int cellY = top; cellY <= bottom; cellY++)
        {
            for (int cellX = left; cellX <= right; cellX++)
            {
                var cell = (cellX, cellY);
                if (!_cells.TryGetValue(cell, out var list))
                {
                    list = new List<T>();
                    _cells[cell] = list;
                }
                list.Add(item);
            }
        }
    }

    public IEnumerable<T> Query(Rectangle rect)
    {
        var seen = new HashSet<T>(ReferenceEqualityComparer.Instance);
        int left = FloorDiv(rect.Left, _cellSize);
        int right = FloorDiv(rect.Right - 1, _cellSize);
        int top = FloorDiv(rect.Top, _cellSize);
        int bottom = FloorDiv(rect.Bottom - 1, _cellSize);
        for (int cellY = top; cellY <= bottom; cellY++)
        {
            for (int cellX = left; cellX <= right; cellX++)
            {
                if (!_cells.TryGetValue((cellX, cellY), out var list))
                    continue;
                foreach (var item in list)
                {
                    if (seen.Add(item))
                        yield return item;
                }
            }
        }
    }

    /// <summary>Allocation-free query for frame loops that retain their result and deduplication buffers.</summary>
    public void Query(Rectangle rect, List<T> results, HashSet<T> seen)
    {
        results.Clear();
        seen.Clear();
        int left = FloorDiv(rect.Left, _cellSize);
        int right = FloorDiv(rect.Right - 1, _cellSize);
        int top = FloorDiv(rect.Top, _cellSize);
        int bottom = FloorDiv(rect.Bottom - 1, _cellSize);
        for (int cellY = top; cellY <= bottom; cellY++)
        {
            for (int cellX = left; cellX <= right; cellX++)
            {
                if (!_cells.TryGetValue((cellX, cellY), out var list))
                    continue;
                for (int index = 0; index < list.Count; index++)
                {
                    T item = list[index];
                    if (seen.Add(item))
                        results.Add(item);
                }
            }
        }
    }

    /// <summary>
    /// Retains cell/list capacity across frames while removing all items.
    /// Screen-relative combat cells are reused heavily as the camera follows
    /// the player, avoiding a new dictionary and bucket lists every update.
    /// </summary>
    public void Clear()
    {
        foreach (List<T> list in _cells.Values)
            list.Clear();
    }

    /// <summary>
    /// Releases buckets retained for a previous arena. Per-frame clears keep
    /// hot capacity, while run/floor transitions use this to bound memory.
    /// </summary>
    public void Reset() => _cells.Clear();
}
