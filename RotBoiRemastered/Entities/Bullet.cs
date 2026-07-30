using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
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
    public float VisualAge { get; private set; }
    public List<Vector2> Trail { get; } = new(6);

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
        VisualAge += seconds;
        WorldX += MathF.Cos(Direction) * distance;
        WorldY -= MathF.Sin(Direction) * distance;
        Range -= distance;

        Trail.Add(new Vector2(WorldX + Size / 2f, WorldY + Size / 2f));
        if (Trail.Count > 6)
            Trail.RemoveAt(0);

        if (battleground.RectHitsWall(WorldRect()) || Range <= 0)
            RemFlag = true;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 centerWorld = new(WorldX + Size / 2f, WorldY + Size / 2f);
        Vector2 center = camera.WorldToScreen(centerWorld, playerWorldPosition, screenShake);
        Vector2 movement = new(MathF.Cos(Direction), -MathF.Sin(Direction));
        Vector2 forward = camera.WorldVectorToScreen(movement);
        float intensity = (float)GameProfile.Profile.VisualEffectsIntensity;
        if (intensity > 0 && Trail.Count > 1)
        {
            int visible = Math.Max(1, (int)MathF.Ceiling((Trail.Count - 1) * intensity));
            int first = Math.Max(0, Trail.Count - 1 - visible);
            for (int index = first; index < Trail.Count - 1; index++)
            {
                Vector2 trail = camera.WorldToScreen(
                    Trail[index], playerWorldPosition, screenShake);
                float progress = (index - first + 1f) / Math.Max(1, visible);
                int trailSize = Math.Max(2, (int)(Size * (.08f + progress * .12f)));
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)trail.X - trailSize / 2,
                        (int)trail.Y - trailSize / 2, trailSize, trailSize),
                    (index % 2 == 0 ? EdgeColor : Color) * (.18f + progress * .32f));
            }
        }
        ProjectileVisuals.Draw(
            spriteBatch,
            center,
            forward,
            Size,
            Color,
            EdgeColor,
            Design,
            IsCritical,
            camera.Zoom,
            VisualAge,
            drawShadow: true,
            intensity: intensity);
    }
}
