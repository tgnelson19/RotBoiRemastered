using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.World;

/// <summary>Large neutral cave-and-chamber maps shared by Body and hostile Soul expeditions.</summary>
public static class ExpeditionWorldGenerator
{
    public const int Width = 120;
    public const int Height = 120;
    private const int RoomCount = 18;

    public static Battleground Generate(CampaignWorld world, Random rng)
    {
        var tiles = new TileType[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                tiles[y, x] = TileType.OuterVoid;

        var centers = new List<Point> { new(Width / 2, Height - 12) };
        CarveRoom(tiles, centers[0], 8, 6);
        for (int index = 1; index < RoomCount; index++)
        {
            Point center = new(rng.Next(10, Width - 10), rng.Next(10, Height - 10));
            Point nearest = centers.OrderBy(other => DistanceSquared(center, other)).First();
            CarveTunnel(tiles, nearest, center, rng.Next(1, 3), rng.Next(2) == 0);
            CarveRoom(tiles, center, rng.Next(5, 11), rng.Next(4, 9));
            centers.Add(center);
            if (index > 3 && rng.NextDouble() < .36)
            {
                Point loop = centers[rng.Next(index)];
                CarveTunnel(tiles, center, loop, 1, rng.Next(2) == 0);
            }
        }
        AddWallShell(tiles);
        Point spawn = centers[0];
        return new Battleground(tiles, BiomePalettes.Soul, 20,
            new Vector2((spawn.X + .125f) * Battleground.TileSize,
                (spawn.Y + .125f) * Battleground.TileSize),
            world == CampaignWorld.Body ? "body" : "soul_expedition");
    }

    public static IReadOnlyList<Vector2> SecretPositions(Battleground battleground, Random rng, int count)
    {
        var candidates = new List<Point>();
        for (int y = 7; y < battleground.Height - 7; y++)
            for (int x = 7; x < battleground.Width - 7; x++)
                if (!battleground.TileAt(x, y).IsSolid()
                    && Math.Abs(x - battleground.Width / 2) + Math.Abs(y - (battleground.Height - 12)) > 28)
                    candidates.Add(new Point(x, y));
        var chosen = new List<Vector2>();
        while (chosen.Count < count && candidates.Count > 0)
        {
            int index = rng.Next(candidates.Count);
            Point point = candidates[index];
            candidates.RemoveAt(index);
            Vector2 world = new((point.X + .5f) * Battleground.TileSize, (point.Y + .5f) * Battleground.TileSize);
            if (chosen.All(other => Vector2.DistanceSquared(other, world) >= MathF.Pow(Battleground.TileSize * 14, 2)))
                chosen.Add(world);
        }
        if (chosen.Count != count)
            throw new InvalidOperationException("Expedition generator could not place five separated secrets.");
        return chosen;
    }

    private static int DistanceSquared(Point a, Point b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static void CarveRoom(TileType[,] tiles, Point center, int rx, int ry)
    {
        for (int y = Math.Max(2, center.Y - ry); y <= Math.Min(Height - 3, center.Y + ry); y++)
            for (int x = Math.Max(2, center.X - rx); x <= Math.Min(Width - 3, center.X + rx); x++)
                if (MathF.Pow((x - center.X) / (float)rx, 2) + MathF.Pow((y - center.Y) / (float)ry, 2) <= 1.12f)
                    tiles[y, x] = TileType.BuildingFloor;
    }

    private static void CarveTunnel(TileType[,] tiles, Point from, Point to, int radius, bool horizontalFirst)
    {
        Point bend = horizontalFirst ? new Point(to.X, from.Y) : new Point(from.X, to.Y);
        CarveLine(tiles, from, bend, radius);
        CarveLine(tiles, bend, to, radius);
    }

    private static void CarveLine(TileType[,] tiles, Point from, Point to, int radius)
    {
        int steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        for (int i = 0; i <= steps; i++)
        {
            int x = (int)MathF.Round(MathHelper.Lerp(from.X, to.X, i / (float)Math.Max(1, steps)));
            int y = (int)MathF.Round(MathHelper.Lerp(from.Y, to.Y, i / (float)Math.Max(1, steps)));
            for (int oy = -radius; oy <= radius; oy++)
                for (int ox = -radius; ox <= radius; ox++)
                    tiles[y + oy, x + ox] = TileType.Road;
        }
    }

    private static void AddWallShell(TileType[,] tiles)
    {
        var shell = new bool[Height, Width];
        for (int y = 1; y < Height - 1; y++)
            for (int x = 1; x < Width - 1; x++)
                if (tiles[y, x] == TileType.OuterVoid)
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                            shell[y, x] |= !tiles[y + oy, x + ox].IsSolid();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (shell[y, x]) tiles[y, x] = TileType.BuildingWall;
    }
}
