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

    private IEnumerable<(int X, int Y)> CellRange(Rectangle rect)
    {
        int left = FloorDiv(rect.Left, _cellSize);
        int right = FloorDiv(rect.Right - 1, _cellSize);
        int top = FloorDiv(rect.Top, _cellSize);
        int bottom = FloorDiv(rect.Bottom - 1, _cellSize);
        for (int cellY = top; cellY <= bottom; cellY++)
            for (int cellX = left; cellX <= right; cellX++)
                yield return (cellX, cellY);
    }

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor((double)value / divisor);

    public void Insert(T item, Rectangle rect)
    {
        foreach (var cell in CellRange(rect))
        {
            if (!_cells.TryGetValue(cell, out var list))
            {
                list = new List<T>();
                _cells[cell] = list;
            }
            list.Add(item);
        }
    }

    public IEnumerable<T> Query(Rectangle rect)
    {
        var seen = new HashSet<T>(ReferenceEqualityComparer.Instance);
        foreach (var cell in CellRange(rect))
        {
            if (!_cells.TryGetValue(cell, out var list))
                continue;
            foreach (var item in list)
            {
                if (seen.Add(item))
                    yield return item;
            }
        }
    }
}
