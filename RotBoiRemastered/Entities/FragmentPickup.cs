using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>A forge-currency shard that scatters briefly, then follows the player's pickup aura.</summary>
public sealed class FragmentPickup
{
    public const float Size = 16f;
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    public float Direction { get; set; }
    public bool NaturalSpawn { get; set; } = true;
    public float VisualAge { get; private set; }

    private float _scatterRemaining = 34f;

    public FragmentPickup(float worldX, float worldY, Random? rng = null)
    {
        WorldX = worldX;
        WorldY = worldY;
        rng ??= Random.Shared;
        Direction = (float)(rng.NextDouble() * Math.PI * 2);
    }

    public Rectangle WorldRect() => new((int)WorldX, (int)WorldY, (int)Size, (int)Size);

    public void Update(float playerAuraSpeed, Battleground battleground)
    {
        float frameScale = (float)Simulation.GetFrameScale();
        VisualAge += (float)(Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate));
        float speed = NaturalSpawn ? (_scatterRemaining > 0 ? 1.2f : 0f) : playerAuraSpeed * 1.1f;
        _scatterRemaining = Math.Max(0, _scatterRemaining - frameScale);
        float dx = speed * MathF.Cos(Direction) * frameScale;
        float dy = speed * MathF.Sin(Direction) * frameScale;
        var current = WorldRect();
        var nextX = new Rectangle(current.X - (int)dx, current.Y, current.Width, current.Height);
        if (!battleground.RectHitsWall(nextX))
        {
            WorldX -= dx;
            current = nextX;
        }
        var nextY = new Rectangle(current.X, current.Y - (int)dy, current.Width, current.Height);
        if (!battleground.RectHitsWall(nextY))
            WorldY -= dy;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var center = screen + new Vector2(Size / 2f);
        float pulse = 1f + .12f * MathF.Sin(VisualAge * 8f);
        Primitives2D.FillCircle(spriteBatch, center, Size * 1.15f * pulse, new Color(UiTheme.Gold, 38));
        float angle = VisualAge * 2.8f;
        Vector2 first = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Size * .62f;
        Vector2 second = center + new Vector2(
            MathF.Cos(angle + MathF.PI * .62f),
            MathF.Sin(angle + MathF.PI * .62f)) * Size * .38f;
        Vector2 third = center + new Vector2(
            MathF.Cos(angle + MathF.PI),
            MathF.Sin(angle + MathF.PI)) * Size * .62f;
        Vector2 fourth = center + new Vector2(
            MathF.Cos(angle - MathF.PI * .62f),
            MathF.Sin(angle - MathF.PI * .62f)) * Size * .38f;
        Vector2 shadow = new(2, 3);
        Primitives2D.FillQuad(
            spriteBatch,
            first + shadow,
            second + shadow,
            third + shadow,
            fourth + shadow,
            UiTheme.Shadow);
        Primitives2D.FillQuad(spriteBatch, first, second, third, fourth, UiTheme.Gold);
        Primitives2D.QuadOutline(
            spriteBatch, first, second, third, fourth, UiTheme.Cream, 2);
    }
}
