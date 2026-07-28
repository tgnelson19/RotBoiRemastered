using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>
/// Persistent tile discovery plus the player's current line of sight for one
/// generated Path floor. Raised/void tiles block rays but are themselves
/// visible, letting the player read the wall that closes a sightline without
/// revealing the room behind it.
/// </summary>
public sealed class PathFogOfWar
{
    public const int DefaultSightRadiusTiles = 14;

    private readonly Battleground _battleground;
    private readonly bool[,] _explored;
    private readonly bool[,] _visible;

    public int SightRadiusTiles { get; }

    public PathFogOfWar(Battleground battleground, int sightRadiusTiles = DefaultSightRadiusTiles)
    {
        _battleground = battleground;
        SightRadiusTiles = Math.Max(2, sightRadiusTiles);
        _explored = new bool[battleground.Height, battleground.Width];
        _visible = new bool[battleground.Height, battleground.Width];
    }

    public bool IsVisible(int tileX, int tileY) =>
        InBounds(tileX, tileY) && _visible[tileY, tileX];

    public bool IsExplored(int tileX, int tileY) =>
        InBounds(tileX, tileY) && _explored[tileY, tileX];

    public bool IsWorldVisible(Vector2 worldPosition)
    {
        int tileX = (int)MathF.Floor(worldPosition.X / Battleground.TileSize);
        int tileY = (int)MathF.Floor(worldPosition.Y / Battleground.TileSize);
        return IsVisible(tileX, tileY);
    }

    public bool IsWorldAreaVisible(Rectangle worldArea)
    {
        int left = Math.Clamp(worldArea.Left / Battleground.TileSize, 0, _battleground.Width - 1);
        int top = Math.Clamp(worldArea.Top / Battleground.TileSize, 0, _battleground.Height - 1);
        int right = Math.Clamp(Math.Max(worldArea.Left, worldArea.Right - 1) / Battleground.TileSize,
            0, _battleground.Width - 1);
        int bottom = Math.Clamp(Math.Max(worldArea.Top, worldArea.Bottom - 1) / Battleground.TileSize,
            0, _battleground.Height - 1);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                if (_visible[y, x])
                    return true;
        return false;
    }

    public bool AnyExplored(Rectangle tileArea)
    {
        int left = Math.Clamp(tileArea.Left, 0, _battleground.Width - 1);
        int top = Math.Clamp(tileArea.Top, 0, _battleground.Height - 1);
        int right = Math.Clamp(tileArea.Right - 1, 0, _battleground.Width - 1);
        int bottom = Math.Clamp(tileArea.Bottom - 1, 0, _battleground.Height - 1);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                if (_explored[y, x])
                    return true;
        return false;
    }

    public bool AnyVisible(Rectangle tileArea)
    {
        int left = Math.Clamp(tileArea.Left, 0, _battleground.Width - 1);
        int top = Math.Clamp(tileArea.Top, 0, _battleground.Height - 1);
        int right = Math.Clamp(tileArea.Right - 1, 0, _battleground.Width - 1);
        int bottom = Math.Clamp(tileArea.Bottom - 1, 0, _battleground.Height - 1);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                if (_visible[y, x])
                    return true;
        return false;
    }

    public void Update(Vector2 observerWorld)
    {
        Array.Clear(_visible);

        float observerTileX = observerWorld.X / Battleground.TileSize;
        float observerTileY = observerWorld.Y / Battleground.TileSize;
        int observerX = Math.Clamp((int)MathF.Floor(observerTileX), 0, _battleground.Width - 1);
        int observerY = Math.Clamp((int)MathF.Floor(observerTileY), 0, _battleground.Height - 1);
        int radius = SightRadiusTiles;
        int left = Math.Max(0, observerX - radius);
        int right = Math.Min(_battleground.Width - 1, observerX + radius);
        int top = Math.Max(0, observerY - radius);
        int bottom = Math.Min(_battleground.Height - 1, observerY + radius);
        float radiusSquared = (radius + .35f) * (radius + .35f);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                float dx = x + .5f - observerTileX;
                float dy = y + .5f - observerTileY;
                if (dx * dx + dy * dy > radiusSquared
                    || !HasLineOfSight(observerTileX, observerTileY, x, y))
                {
                    continue;
                }
                _visible[y, x] = true;
                _explored[y, x] = true;
            }
        }

        _visible[observerY, observerX] = true;
        _explored[observerY, observerX] = true;
    }

    /// <summary>
    /// Fine-grid ray marching uses the observer's actual sub-tile position,
    /// so edging toward a doorway can reveal around it before the player's
    /// center crosses into the next tile.
    /// </summary>
    public bool HasLineOfSight(float observerTileX, float observerTileY, int targetX, int targetY)
    {
        float targetCenterX = targetX + .5f;
        float targetCenterY = targetY + .5f;
        float dx = targetCenterX - observerTileX;
        float dy = targetCenterY - observerTileY;
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) * 5f));
        int previousX = (int)MathF.Floor(observerTileX);
        int previousY = (int)MathF.Floor(observerTileY);

        for (int step = 1; step <= steps; step++)
        {
            float progress = step / (float)steps;
            int tileX = (int)MathF.Floor(observerTileX + dx * progress);
            int tileY = (int)MathF.Floor(observerTileY + dy * progress);
            if (tileX == previousX && tileY == previousY)
                continue;
            if (!InBounds(tileX, tileY))
                return false;
            if (tileX == targetX && tileY == targetY)
                return true;
            if (_battleground.TileAt(tileX, tileY).IsSolid())
                return false;
            previousX = tileX;
            previousY = tileY;
        }
        return true;
    }

    private bool InBounds(int tileX, int tileY) =>
        tileX >= 0 && tileX < _battleground.Width
        && tileY >= 0 && tileY < _battleground.Height;
}
