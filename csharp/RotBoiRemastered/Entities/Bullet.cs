using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// World-space player projectile whose path is independent of player and
/// camera movement. Ported from bullet.py. Update (movement/removal-flag)
/// and Draw are split, unlike Python's combined updateAndDrawBullet, so
/// removal logic is unit testable without a GraphicsDevice.
///
/// `PortalCooldown` is unused by anything ported so far -- it exists for
/// bossTypes.py's projectile-portal interception (not yet ported), which
/// reads/writes it directly. Kept as a public settable field for that
/// future caller rather than re-deriving it later.
/// </summary>
public sealed class Bullet
{
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    public float Speed { get; }
    public float Direction { get; }
    public float Size { get; }
    public Color Color { get; }
    public float Range { get; private set; }
    public int Pierce { get; set; }
    public float Damage { get; }
    public bool IsCritical { get; }
    public bool RemFlag { get; private set; }
    public float PortalCooldown { get; set; }

    public Bullet(float worldX, float worldY, float speed, float direction, float bulletRange,
        float size, Color color, int pierce, float damage, bool isCritical)
    {
        WorldX = worldX;
        WorldY = worldY;
        Speed = speed;
        Direction = direction;
        Size = size;
        Color = color;
        Range = bulletRange;
        Pierce = pierce;
        Damage = damage;
        IsCritical = isCritical;
    }

    public Rectangle WorldRect() => new((int)WorldX, (int)WorldY, (int)Size, (int)Size);

    public void Update(Battleground battleground)
    {
        float distance = Speed * (float)Simulation.GetFrameScale();
        float seconds = (float)Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        PortalCooldown = Math.Max(0f, PortalCooldown - seconds);
        WorldX += MathF.Cos(Direction) * distance;
        WorldY -= MathF.Sin(Direction) * distance;
        Range -= distance;

        if (battleground.RectHitsWall(WorldRect()) || Range <= 0)
            RemFlag = true;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        rect.Inflate(4, 4);
        Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
        rect.Inflate(-4, -4);
        Primitives2D.FillRect(spriteBatch, rect, IsCritical ? UiTheme.Purple : UiTheme.Cream);
        var inner = rect;
        inner.Inflate(-(int)(Size * 0.5f), -(int)(Size * 0.5f));
        Primitives2D.FillRect(spriteBatch, inner, UiTheme.Text);
    }
}
