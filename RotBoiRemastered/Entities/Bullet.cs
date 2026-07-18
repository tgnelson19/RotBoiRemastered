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
/// `PortalCooldown` exists for `Dissonance.RoutePlayerBullet` (bossTypes.py's
/// `route_player_bullet`), which reads/writes it directly.
/// </summary>
public sealed class Bullet
{
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    public float Speed { get; }
    public float Direction { get; private set; }
    public float Size { get; }
    public Color Color { get; }
    public Color EdgeColor { get; }
    public string Design { get; }
    public float Range { get; private set; }
    public int Pierce { get; set; }
    public float Damage { get; private set; }
    public bool IsCritical { get; }
    public bool RemFlag { get; set; }
    public float PortalCooldown { get; set; }

    public Bullet(float worldX, float worldY, float speed, float direction, float bulletRange,
        float size, Color color, int pierce, float damage, bool isCritical, Color? edgeColor = null, string design = "bulb")
    {
        WorldX = worldX;
        WorldY = worldY;
        Speed = speed;
        Direction = direction;
        Size = size;
        Color = color;
        EdgeColor = edgeColor ?? UiTheme.Ink;
        Design = design;
        Range = bulletRange;
        Pierce = pierce;
        Damage = damage;
        IsCritical = isCritical;
    }

    public Rectangle WorldRect() => new((int)WorldX, (int)WorldY, (int)Size, (int)Size);

    /// <summary>Ported from Dissonance.route_player_bullet's direct worldX/worldY/direc/damage/portalCooldown reassignment -- the sole external mutator of this bullet's position/direction/damage, so it's encapsulated as one method instead of exposing raw setters.</summary>
    public void RouteThroughPortal(float worldX, float worldY, float direction, float damageMultiplier, float cooldownSeconds)
    {
        WorldX = worldX;
        WorldY = worldY;
        Direction = direction;
        Damage *= damageMultiplier;
        PortalCooldown = cooldownSeconds;
    }

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
        Vector2 centerWorld = new(WorldX + Size / 2f, WorldY + Size / 2f);
        Vector2 center = camera.WorldToScreen(centerWorld, playerWorldPosition, screenShake);
        Vector2 movement = new(MathF.Cos(Direction), -MathF.Sin(Direction));
        Vector2 forward = camera.WorldVectorToScreen(movement);
        ProjectileVisuals.Draw(spriteBatch, center, forward, Size, Color, EdgeColor, Design, IsCritical);
    }
}
