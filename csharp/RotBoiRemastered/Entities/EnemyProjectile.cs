using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Projectiles fired by enemies, including reusable boss path primitives
/// (linear, sine, pool, laser, bomb, orbit, splitting). Ported from
/// enemyProjectile.py.
///
/// Cleanup vs. the Python original: Update (physics/state/spawning) and Draw
/// (rendering) are split -- Python's updateAndDraw(screen) did both at once,
/// which meant the pool/laser/bomb/split branching logic couldn't be unit
/// tested without a real Surface. `Trail` now stores world-space points
/// instead of screen-space pixels -- Python recomputed posX/posY once per
/// frame and appended straight to the trail, which is fine as long as
/// Update and Draw always run back-to-back in the same frame, but breaks
/// that implicit coupling. World-space points converted through the camera
/// at Draw time have no such coupling and are strictly more correct if the
/// camera ever rotates between a point being recorded and drawn.
/// </summary>
public sealed class EnemyProjectile
{
    private const float HostileSpeedScale = .52f;
    private const float DissonanceDamageScale = 1.3f;

    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float OriginX { get; }
    public float OriginY { get; }
    public float Direction { get; set; }
    public float Speed { get; set; }
    public float Damage { get; }
    public float Size { get; }
    public float RemainingRange { get; set; }
    public Color Color { get; }
    public string Shape { get; }
    public string Path { get; }
    public float Amplitude { get; }
    public float Frequency { get; }
    public float? Lifetime { get; set; }
    public float SpeedDecay { get; }
    public Vector2? OrbitCenter { get; set; }
    public float OrbitRadius { get; set; }
    public float OrbitAngle { get; set; }
    public float AngularSpeed { get; set; }
    public string? Owner { get; }
    public bool Illusory { get; set; }
    public bool TruthMarked { get; set; }
    public double BeliefGain { get; set; }
    public double ClarityGain { get; set; }
    public bool IgnoreWalls { get; }
    public Vector2? Target { get; set; }
    public float TelegraphDuration { get; set; } = 1.0f;
    public float FuseDuration { get; set; } = 3.0f;
    public float BlastRadius { get; set; }
    public int BurstCount { get; set; } = 8;
    public float BurstDamage { get; set; }
    public List<EnemyProjectile> SpawnedProjectiles { get; } = new();
    public int SplitCount { get; set; }
    public float? SplitAt { get; set; }
    public int SplitGeneration { get; set; }
    public bool PersistentHazard { get; }
    public bool Exploded { get; private set; }
    public float Age { get; private set; }
    public float Travelled { get; private set; }
    public bool RemFlag { get; set; }
    public List<Vector2> Trail { get; } = new();

    public EnemyProjectile(
        float worldX, float worldY, float direction, float speed, float damage, float size,
        float travelRange = 900f, Color? color = null, string shape = "square", string path = "linear",
        float amplitude = 0f, float frequency = .035f, float? lifetime = null, float speedDecay = 0f,
        Vector2? orbitCenter = null, float orbitRadius = 0f, float orbitAngle = 0f, float angularSpeed = 0f,
        string? owner = null, bool ignoreWalls = false, Vector2? target = null)
    {
        WorldX = worldX;
        WorldY = worldY;
        OriginX = worldX;
        OriginY = worldY;
        Direction = direction;
        Speed = speed;
        string ownerText = owner ?? "";
        float bossScale = ownerText.StartsWith("beaudis") || ownerText.StartsWith("dissonance") ? 100f : 1f;
        float dissonanceScale = ownerText.StartsWith("dissonance") ? DissonanceDamageScale : 1f;
        Damage = MathF.Round(damage * bossScale * dissonanceScale);
        Size = size;
        RemainingRange = travelRange;
        Color = color ?? UiTheme.Red;
        Shape = shape;
        Path = path;
        Amplitude = amplitude;
        Frequency = frequency;
        Lifetime = lifetime;
        SpeedDecay = speedDecay;
        OrbitCenter = orbitCenter;
        OrbitRadius = orbitRadius;
        OrbitAngle = orbitAngle;
        AngularSpeed = angularSpeed;
        Owner = owner;

        // Dissonance bullets should paint complete lanes across the final arena.
        // Mines retain their deliberately local range and orbit fields retain lifetime rules.
        if (ownerText.StartsWith("dissonance") && path != "mine" && path != "orbit" && lifetime is null)
            RemainingRange = Math.Max(RemainingRange, Simulation.TileSize * 72f);
        if (ownerText.Contains("survival") || ownerText.Contains("boundary_inward"))
            RemainingRange = float.PositiveInfinity;

        IgnoreWalls = ignoreWalls;
        Target = target;
        BlastRadius = Simulation.TileSize * 1.5f;
        BurstDamage = Damage;
        PersistentHazard = path == "laser";
    }

    public Rectangle WorldRect()
    {
        if (Path == "laser" && Age >= TelegraphDuration)
        {
            float endX = WorldX + MathF.Cos(Direction) * RemainingRange;
            float endY = WorldY + MathF.Sin(Direction) * RemainingRange;
            float x = Math.Min(WorldX, endX), y = Math.Min(WorldY, endY);
            float w = Math.Max(Size, Math.Abs(endX - WorldX)), h = Math.Max(Size, Math.Abs(endY - WorldY));
            return new Rectangle((int)x, (int)y, (int)w, (int)h);
        }
        return new Rectangle((int)WorldX, (int)WorldY, (int)Size, (int)Size);
    }

    public bool Collides(Rectangle rect)
    {
        if (Illusory)
            return false;
        if (Path == "pool")
        {
            if (Age < TelegraphDuration)
                return false;
            float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
            float nearestX = Math.Clamp(centerX, rect.Left, rect.Right);
            float nearestY = Math.Clamp(centerY, rect.Top, rect.Bottom);
            float radius = Size * .46f;
            return (nearestX - centerX) * (nearestX - centerX) + (nearestY - centerY) * (nearestY - centerY) <= radius * radius;
        }
        if (Path == "laser")
        {
            if (Age < TelegraphDuration)
                return false;
            var start = new Vector2(WorldX, WorldY);
            var end = new Vector2(WorldX + MathF.Cos(Direction) * RemainingRange, WorldY + MathF.Sin(Direction) * RemainingRange);
            var inflated = rect;
            inflated.Inflate((int)Size, (int)Size);
            return SegmentIntersectsRect(start, end, inflated);
        }
        if (Path == "bomb")
        {
            if (!Exploded)
                return false;
            float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
            float nearestX = Math.Clamp(centerX, rect.Left, rect.Right);
            float nearestY = Math.Clamp(centerY, rect.Top, rect.Bottom);
            return (nearestX - centerX) * (nearestX - centerX) + (nearestY - centerY) * (nearestY - centerY) <= BlastRadius * BlastRadius;
        }
        return rect.Intersects(WorldRect());
    }

    public void Update(Battleground battleground, bool casualMode)
    {
        float seconds = (float)Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        Age += seconds;

        switch (Path)
        {
            case "pool":
                if (Age >= (Lifetime ?? 8.0f))
                    RemFlag = true;
                return;

            case "laser":
                if (Age >= TelegraphDuration && AngularSpeed != 0)
                    Direction += AngularSpeed * seconds;
                if (Lifetime is not null && Age >= Lifetime)
                    RemFlag = true;
                return;

            case "bomb":
                if (Age < 1.0f && Target.HasValue)
                {
                    float progress = Math.Min(1.0f, Age);
                    WorldX = OriginX + (Target.Value.X - OriginX) * progress;
                    WorldY = OriginY + (Target.Value.Y - OriginY) * progress
                        - MathF.Sin(progress * MathF.PI) * Simulation.TileSize * 2.5f;
                }
                else if (Age >= FuseDuration && !Exploded)
                {
                    Exploded = true;
                    for (int index = 0; index < BurstCount; index++)
                    {
                        SpawnedProjectiles.Add(new EnemyProjectile(
                            WorldX, WorldY, index * 2f * MathF.PI / Math.Max(1, BurstCount), .9f,
                            BurstDamage * .28f, Simulation.TileSize * .38f,
                            travelRange: Simulation.TileSize * 24f, color: Color, shape: "diamond",
                            owner: $"{Owner}_burst", ignoreWalls: true));
                    }
                }
                else if (Exploded && Age >= FuseDuration + .18f)
                {
                    RemFlag = true;
                }
                break;

            case "orbit" when OrbitCenter.HasValue:
                OrbitAngle += AngularSpeed * seconds;
                WorldX = OrbitCenter.Value.X + MathF.Cos(OrbitAngle) * OrbitRadius - Size / 2f;
                WorldY = OrbitCenter.Value.Y + MathF.Sin(OrbitAngle) * OrbitRadius - Size / 2f;
                break;

            default:
                float comfortScale = casualMode ? .88f : 1.0f;
                float distance = Speed * HostileSpeedScale * comfortScale * (float)Simulation.GetFrameScale();
                Travelled += distance;
                RemainingRange -= distance;
                if (Path == "sine")
                {
                    float lateral = MathF.Sin(Travelled * Frequency) * Amplitude;
                    WorldX = OriginX + MathF.Cos(Direction) * Travelled - MathF.Sin(Direction) * lateral;
                    WorldY = OriginY + MathF.Sin(Direction) * Travelled + MathF.Cos(Direction) * lateral;
                }
                else
                {
                    WorldX += MathF.Cos(Direction) * distance;
                    WorldY += MathF.Sin(Direction) * distance;
                }
                if (SpeedDecay != 0)
                    Speed = Math.Max(0, Speed - SpeedDecay * seconds);
                if (SplitCount > 1 && SplitAt.HasValue && Travelled >= SplitAt.Value && !Exploded)
                {
                    Exploded = true;
                    float spread = .8f + .12f * SplitGeneration;
                    for (int index = 0; index < SplitCount; index++)
                    {
                        float fraction = SplitCount == 1 ? .5f : (float)index / (SplitCount - 1);
                        var child = new EnemyProjectile(
                            WorldX, WorldY, Direction - spread / 2f + spread * fraction,
                            Speed * 1.08f, Damage * .58f, Size * .72f,
                            travelRange: Math.Max(Simulation.TileSize * 5f, RemainingRange),
                            color: Color, shape: "diamond", owner: Owner, ignoreWalls: IgnoreWalls);
                        if (SplitGeneration > 0)
                        {
                            child.SplitCount = SplitCount;
                            child.SplitAt = Math.Max(Simulation.TileSize * 2.5f, RemainingRange * .42f);
                            child.SplitGeneration = SplitGeneration - 1;
                        }
                        SpawnedProjectiles.Add(child);
                    }
                    RemFlag = true;
                }
                break;
        }

        // Common tail for bomb/orbit/default -- pool/laser returned above.
        Trail.Add(new Vector2(WorldX + Size / 2f, WorldY + Size / 2f));
        if (Trail.Count > 5)
            Trail.RemoveAt(0);

        bool expired = Lifetime is not null && Age >= Lifetime;
        bool rangeSpent = Path != "orbit" && RemainingRange <= 0;
        bool wallHit = !IgnoreWalls && battleground.RectHitsWall(WorldRect());
        if (expired || rangeSpent || wallHit)
            RemFlag = true;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake, bool highContrast)
    {
        if (Path == "pool")
        {
            DrawPool(spriteBatch, camera, playerWorldPosition, screenShake);
            return;
        }
        if (Path == "laser")
        {
            DrawLaser(spriteBatch, camera, playerWorldPosition, screenShake);
            return;
        }

        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);

        if (Trail.Count > 1)
        {
            for (int index = 0; index < Trail.Count - 1; index++)
            {
                Vector2 trailScreen = camera.WorldToScreen(Trail[index], playerWorldPosition, screenShake);
                int trailSize = Math.Max(2, (int)(Size * (index + 1) / (float)Trail.Count * .22f));
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)(trailScreen.X - trailSize / 2f), (int)(trailScreen.Y - trailSize / 2f), trailSize, trailSize),
                    UiTheme.Ink);
                if (index >= Trail.Count - 3)
                {
                    int coreSize = Math.Max(1, trailSize / 2);
                    Primitives2D.FillRect(spriteBatch,
                        new Rectangle((int)(trailScreen.X - coreSize / 2f), (int)(trailScreen.Y - coreSize / 2f), coreSize, coreSize),
                        Color);
                }
            }
        }

        if (Shape is "diamond" or "mine" or "bomb")
            DrawDiamondShape(spriteBatch, rect);
        else
            DrawSquareShape(spriteBatch, rect);

        if (highContrast)
            Primitives2D.RectOutline(spriteBatch, InflateF(rect, 4, 4), UiTheme.Cream, Math.Max(2, (int)(Size * .08f)));

        var center = new Vector2(rect.Center.X, rect.Center.Y);
        if (TruthMarked)
            Primitives2D.FillCircle(spriteBatch, center, Math.Max(2, (int)(Size * .1f)), UiTheme.Cream);
        else if (Illusory)
            Primitives2D.CircleOutline(spriteBatch, center, Math.Max(3, (int)(Size * .22f)), UiTheme.Muted, 2);
    }

    private void DrawSquareShape(SpriteBatch spriteBatch, Rectangle rect)
    {
        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, Color);
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Ink, Math.Max(2, (int)(Size * .1f)));
        Primitives2D.FillRect(spriteBatch, InflateF(rect, -(int)(Size * .5f), -(int)(Size * .5f)), UiTheme.Lighten(Color, 45));
    }

    private void DrawDiamondShape(SpriteBatch spriteBatch, Rectangle rect)
    {
        var points = new[]
        {
            new Vector2(rect.X + rect.Width / 2f, rect.Y),
            new Vector2(rect.Right, rect.Y + rect.Height / 2f),
            new Vector2(rect.X + rect.Width / 2f, rect.Bottom),
            new Vector2(rect.X, rect.Y + rect.Height / 2f),
        };
        var shadowPoints = points.Select(p => p + new Vector2(3, 3)).ToArray();
        Primitives2D.FillPolygon(spriteBatch, shadowPoints, UiTheme.Shadow);
        Primitives2D.FillPolygon(spriteBatch, points, Color);
        Primitives2D.PolygonOutline(spriteBatch, points, UiTheme.Ink, Math.Max(2, (int)(Size * .1f)));

        var center = new Vector2(rect.Center.X, rect.Center.Y);
        if (Shape == "mine")
        {
            int pulse = Math.Max(3, (int)(Size * (.12f + .05f * (1 + MathF.Sin(Age * 5f)))));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)(center.X - pulse / 2f), (int)(center.Y - pulse / 2f), pulse, pulse), UiTheme.Text);
        }
        else if (Shape == "bomb")
        {
            float fuse = Math.Max(0, FuseDuration - Age);
            Primitives2D.FillCircle(spriteBatch, center, Math.Max(3, (int)(Size * (.1f + .04f * MathF.Sin(Age * 14f)))), UiTheme.Cream);
            if (Age >= 1.0f)
            {
                var warning = new Rectangle(0, 0, (int)(BlastRadius * 2), (int)(BlastRadius * 2));
                CenterOn(ref warning, rect.Center);
                float urgency = 1 - fuse / Math.Max(.01f, FuseDuration - 1.0f);
                Primitives2D.EllipseOutline(spriteBatch, warning, UiTheme.Red, Math.Max(2, (int)(2 + urgency * 3)));
                Primitives2D.Arc(spriteBatch, InflateF(rect, 8, 8), -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * Math.Max(0, urgency), UiTheme.Cream, 3);
            }
            if (Exploded)
            {
                var blast = new Rectangle(0, 0, (int)(BlastRadius * 2), (int)(BlastRadius * 2));
                CenterOn(ref blast, rect.Center);
                Primitives2D.EllipseOutline(spriteBatch, blast, UiTheme.Gold, Math.Max(5, (int)(Size * .2f)));
            }
        }
    }

    private void DrawPool(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPos = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPos.X, (int)(screenPos.Y + Size / 2f - Size * .29f), (int)Size, (int)(Size * .58f));
        float lifetime = Lifetime ?? 8.0f;
        float appearing = Math.Min(1.0f, Age / Math.Max(.01f, TelegraphDuration));
        float fading = Math.Min(1.0f, Math.Max(0.0f, lifetime - Age) / .7f);
        float scale = Math.Max(.08f, Math.Min(appearing, fading));
        var visible = InflateF(rect, -rect.Width * (1 - scale), -rect.Height * (1 - scale));

        Primitives2D.FillEllipse(spriteBatch, InflateF(visible, 10, 7), UiTheme.Shadow);
        Primitives2D.FillEllipse(spriteBatch, InflateF(visible, 5, 3), UiTheme.Ink);
        Primitives2D.FillEllipse(spriteBatch, visible, Color);
        var inner = InflateF(visible, -visible.Width * .18f, -visible.Height * .24f);
        Primitives2D.EllipseOutline(spriteBatch, inner, UiTheme.Lighten(Color, 34), 3);

        for (int index = 0; index < 5; index++)
        {
            float angle = Age * (1.8f + index * .13f) + index * 2f * MathF.PI / 5f;
            float radiusX = visible.Width * .34f, radiusY = visible.Height * .27f;
            var point = new Vector2(visible.Center.X + MathF.Cos(angle) * radiusX, visible.Center.Y + MathF.Sin(angle) * radiusY);
            int mote = Math.Max(2, (int)(Size * (.025f + .008f * MathF.Sin(Age * 5f + index))));
            Primitives2D.FillCircle(spriteBatch, point, mote + 2, UiTheme.Ink);
            Primitives2D.FillCircle(spriteBatch, point, mote, UiTheme.Cream);
        }

        if (Age < TelegraphDuration)
        {
            float progress = Age / Math.Max(.01f, TelegraphDuration);
            var warning = InflateF(visible, 12, 8);
            Primitives2D.Arc(spriteBatch, warning, -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * progress, UiTheme.Cream, 3);
        }
    }

    private void DrawLaser(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 start = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var endWorld = new Vector2(WorldX + MathF.Cos(Direction) * RemainingRange, WorldY + MathF.Sin(Direction) * RemainingRange);
        Vector2 end = camera.WorldToScreen(endWorld, playerWorldPosition, screenShake);

        if (Age < TelegraphDuration)
        {
            float progress = Age / Math.Max(.01f, TelegraphDuration);
            int pulse = 2 + (int)((1 - progress) * 3);
            Primitives2D.Line(spriteBatch, start, end, Color, pulse);
            for (int step = 0; step < 5; step++)
            {
                var marker = new Vector2((start.X * (4 - step) + end.X * step) / 4f, (start.Y * (4 - step) + end.Y * step) / 4f);
                Primitives2D.FillCircle(spriteBatch, marker, 3, UiTheme.Cream);
            }
        }
        else
        {
            int width = Math.Max(8, (int)(Size * (1.15f + .18f * MathF.Sin(Age * 18f))));
            Primitives2D.Line(spriteBatch, start, end, UiTheme.Ink, width + 8);
            Primitives2D.Line(spriteBatch, start, end, Color, width);
            Color coreColor = Illusory ? UiTheme.Muted : UiTheme.Cream;
            Primitives2D.Line(spriteBatch, start, end, coreColor, Math.Max(2, width / 3));
            if (TruthMarked)
                Primitives2D.FillCircle(spriteBatch, start, Math.Max(3, width / 3), UiTheme.Cream);
        }
    }

    private static Rectangle InflateF(Rectangle rect, float dx, float dy)
    {
        var result = rect;
        result.Inflate((int)MathF.Round(dx), (int)MathF.Round(dy));
        return result;
    }

    private static void CenterOn(ref Rectangle rect, Point center)
    {
        rect.X = center.X - rect.Width / 2;
        rect.Y = center.Y - rect.Height / 2;
    }

    private static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Rectangle rect)
    {
        if (rect.Contains((int)start.X, (int)start.Y) || rect.Contains((int)end.X, (int)end.Y))
            return true;
        var topLeft = new Vector2(rect.Left, rect.Top);
        var topRight = new Vector2(rect.Right, rect.Top);
        var bottomRight = new Vector2(rect.Right, rect.Bottom);
        var bottomLeft = new Vector2(rect.Left, rect.Bottom);
        return SegmentsIntersect(start, end, topLeft, topRight)
            || SegmentsIntersect(start, end, topRight, bottomRight)
            || SegmentsIntersect(start, end, bottomRight, bottomLeft)
            || SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d1 = Cross(p4 - p3, p1 - p3);
        float d2 = Cross(p4 - p3, p2 - p3);
        float d3 = Cross(p2 - p1, p3 - p1);
        float d4 = Cross(p2 - p1, p4 - p1);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
