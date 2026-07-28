using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>
/// Persistent tile discovery plus the player's current line of sight for one
/// generated Path floor. Raised/void tiles block rays but are themselves
/// visible, letting the player read the wall that closes a sightline without
/// revealing the room behind it. Sight has no artificial radius: an
/// unobstructed corridor remains readable to the edge of the floor.
/// </summary>
public sealed class PathFogOfWar
{
    private readonly Battleground _battleground;
    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _solid;
    private readonly bool[] _raised;
    private readonly bool[] _explored;
    private readonly bool[] _visible;
    private readonly Point[] _visibilityTargets;
    private readonly Point[] _convexWallCorners;
    private readonly int[] _supportedCornerIndices;
    private Vector2? _lastObserverWorld;

    public PathFogOfWar(Battleground battleground)
    {
        _battleground = battleground;
        _width = battleground.Width;
        _height = battleground.Height;
        int tileCount = _width * _height;
        _solid = new bool[tileCount];
        _raised = new bool[tileCount];
        _explored = new bool[tileCount];
        _visible = new bool[tileCount];

        var visibilityTargets = new List<Point>();
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                TileType tile = battleground.TileAt(x, y);
                int index = Index(x, y);
                _solid[index] = tile.IsSolid();
                _raised[index] = tile.IsRaised();
                if (tile != TileType.OuterVoid)
                    visibilityTargets.Add(new Point(x, y));
            }
        }
        _visibilityTargets = visibilityTargets.ToArray();

        var convexWallCorners = new List<Point>();
        foreach (Point point in _visibilityTargets)
        {
            if (IsConvexWallCorner(point.X, point.Y))
                convexWallCorners.Add(point);
        }
        _convexWallCorners = convexWallCorners.ToArray();
        _supportedCornerIndices = new int[_convexWallCorners.Length];
    }

    public bool IsVisible(int tileX, int tileY) =>
        InBounds(tileX, tileY) && _visible[Index(tileX, tileY)];

    public bool IsExplored(int tileX, int tileY) =>
        InBounds(tileX, tileY) && _explored[Index(tileX, tileY)];

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
                if (_visible[Index(x, y)])
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
                if (_explored[Index(x, y)])
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
                if (_visible[Index(x, y)])
                    return true;
        return false;
    }

    public void Update(Vector2 observerWorld)
    {
        // MovePlayer and the encounter director can both request a refresh in
        // the same frame. The battleground is immutable, so an identical
        // observer position has an identical result and need not be traced
        // twice.
        if (_lastObserverWorld == observerWorld)
            return;
        _lastObserverWorld = observerWorld;
        Array.Clear(_visible);

        float observerTileX = observerWorld.X / Battleground.TileSize;
        float observerTileY = observerWorld.Y / Battleground.TileSize;
        int observerX = Math.Clamp((int)MathF.Floor(observerTileX), 0, _battleground.Width - 1);
        int observerY = Math.Clamp((int)MathF.Floor(observerTileY), 0, _battleground.Height - 1);

        foreach (Point target in _visibilityTargets)
        {
            if (!HasLineOfSight(observerTileX, observerTileY, target.X, target.Y))
                continue;
            int index = Index(target.X, target.Y);
            _visible[index] = true;
            _explored[index] = true;
        }

        int observerIndex = Index(observerX, observerY);
        _visible[observerIndex] = true;
        _explored[observerIndex] = true;

        RevealSupportedWallCorners();
        RevealWallsBorderingVisibleFloor();
    }

    /// <summary>
    /// Grid-boundary traversal uses the observer's actual sub-tile position,
    /// so edging toward a doorway can reveal around it before the player's
    /// center crosses into the next tile. When a ray passes exactly through a
    /// grid corner, one touching wall does not block it; both side cells must
    /// be solid. This avoids a one-pixel positional wobble changing visibility
    /// while still preventing sight through a sealed diagonal pinch.
    /// </summary>
    public bool HasLineOfSight(float observerTileX, float observerTileY, int targetX, int targetY)
    {
        if (!InBounds(targetX, targetY))
            return false;

        int tileX = Math.Clamp((int)MathF.Floor(observerTileX), 0, _battleground.Width - 1);
        int tileY = Math.Clamp((int)MathF.Floor(observerTileY), 0, _battleground.Height - 1);
        if (tileX == targetX && tileY == targetY)
            return true;

        float targetCenterX = targetX + .5f;
        float targetCenterY = targetY + .5f;
        float dx = targetCenterX - observerTileX;
        float dy = targetCenterY - observerTileY;
        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        float deltaX = stepX == 0 ? float.PositiveInfinity : 1f / MathF.Abs(dx);
        float deltaY = stepY == 0 ? float.PositiveInfinity : 1f / MathF.Abs(dy);
        float maxX = stepX switch
        {
            > 0 => (tileX + 1f - observerTileX) / MathF.Abs(dx),
            < 0 => (observerTileX - tileX) / MathF.Abs(dx),
            _ => float.PositiveInfinity,
        };
        float maxY = stepY switch
        {
            > 0 => (tileY + 1f - observerTileY) / MathF.Abs(dy),
            < 0 => (observerTileY - tileY) / MathF.Abs(dy),
            _ => float.PositiveInfinity,
        };

        const float cornerEpsilon = .00001f;
        while (tileX != targetX || tileY != targetY)
        {
            if (maxX + cornerEpsilon < maxY)
            {
                tileX += stepX;
                maxX += deltaX;
            }
            else if (maxY + cornerEpsilon < maxX)
            {
                tileY += stepY;
                maxY += deltaY;
            }
            else
            {
                // The ray crosses a precise tile corner. Treat it as sealed
                // only when both tiles touching that corner are solid.
                int sideX1 = tileX + stepX;
                int sideY1 = tileY;
                int sideX2 = tileX;
                int sideY2 = tileY + stepY;
                if (IsBlockingSide(sideX1, sideY1, targetX, targetY)
                    && IsBlockingSide(sideX2, sideY2, targetX, targetY))
                {
                    return false;
                }
                tileX += stepX;
                tileY += stepY;
                maxX += deltaX;
                maxY += deltaY;
            }

            if (!InBounds(tileX, tileY))
                return false;
            if (tileX == targetX && tileY == targetY)
                return true;
            if (_solid[Index(tileX, tileY)])
                return false;
        }
        return true;
    }

    /// <summary>
    /// A corner wall can be mathematically hidden by the preceding wall in
    /// the same run even though darkening that single tile makes the wall cap
    /// flash as the player moves. Reveal only true L-turns (exactly two
    /// perpendicular raised neighbors) supported by a directly visible
    /// neighbor. Candidates are collected before any are revealed, preventing
    /// visibility from cascading along an unseen wall.
    /// </summary>
    private void RevealSupportedWallCorners()
    {
        int supportedCount = 0;
        foreach (Point corner in _convexWallCorners)
        {
            int cornerIndex = Index(corner.X, corner.Y);
            if (_visible[cornerIndex])
                continue;
            if (IsVisibleRaised(corner.X, corner.Y - 1)
                || IsVisibleRaised(corner.X + 1, corner.Y)
                || IsVisibleRaised(corner.X, corner.Y + 1)
                || IsVisibleRaised(corner.X - 1, corner.Y))
            {
                _supportedCornerIndices[supportedCount++] = cornerIndex;
            }
        }

        for (int index = 0; index < supportedCount; index++)
        {
            int cornerIndex = _supportedCornerIndices[index];
            _visible[cornerIndex] = true;
            _explored[cornerIndex] = true;
        }
    }

    /// <summary>
    /// In a long corridor, the center ray to a distant side-wall tile
    /// eventually intersects a nearer tile in that same continuous wall.
    /// The walkable tile beside it remains directly visible, so use that
    /// visible floor as non-cascading support for the wall face. This is the
    /// top-down equivalent of lighting a wall surface from the space in front
    /// of it and prevents hallway walls from tapering into a dark wedge.
    /// </summary>
    private void RevealWallsBorderingVisibleFloor()
    {
        foreach (Point target in _visibilityTargets)
        {
            int targetIndex = Index(target.X, target.Y);
            if (_visible[targetIndex] || !_raised[targetIndex])
                continue;

            if (IsVisibleFloor(target.X, target.Y - 1)
                || IsVisibleFloor(target.X + 1, target.Y)
                || IsVisibleFloor(target.X, target.Y + 1)
                || IsVisibleFloor(target.X - 1, target.Y))
            {
                _visible[targetIndex] = true;
                _explored[targetIndex] = true;
            }
        }
    }

    private bool IsConvexWallCorner(int tileX, int tileY)
    {
        if (!IsRaised(tileX, tileY))
            return false;
        bool north = IsRaised(tileX, tileY - 1);
        bool east = IsRaised(tileX + 1, tileY);
        bool south = IsRaised(tileX, tileY + 1);
        bool west = IsRaised(tileX - 1, tileY);
        int neighborCount = (north ? 1 : 0) + (east ? 1 : 0)
            + (south ? 1 : 0) + (west ? 1 : 0);
        return neighborCount == 2 && !(north && south) && !(east && west);
    }

    private bool IsBlockingSide(int tileX, int tileY, int targetX, int targetY) =>
        InBounds(tileX, tileY)
        && (tileX != targetX || tileY != targetY)
        && _solid[Index(tileX, tileY)];

    private bool IsRaised(int tileX, int tileY) =>
        InBounds(tileX, tileY) && _raised[Index(tileX, tileY)];

    private bool IsVisibleRaised(int tileX, int tileY) =>
        InBounds(tileX, tileY)
        && _raised[Index(tileX, tileY)]
        && _visible[Index(tileX, tileY)];

    private bool IsVisibleFloor(int tileX, int tileY) =>
        InBounds(tileX, tileY)
        && !_solid[Index(tileX, tileY)]
        && _visible[Index(tileX, tileY)];

    private int Index(int tileX, int tileY) => tileY * _width + tileX;

    private bool InBounds(int tileX, int tileY) =>
        tileX >= 0 && tileX < _width
        && tileY >= 0 && tileY < _height;
}
