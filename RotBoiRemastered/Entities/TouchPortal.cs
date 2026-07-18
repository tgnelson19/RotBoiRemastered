using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// A heavy square gate that marches along Touch's arena walls. Ported from
/// bossTypes.py's TouchPortal. `ProjectilePortal.Draw` was made `virtual`
/// for this override (its only one so far).
/// </summary>
public sealed class TouchPortal : ProjectilePortal
{
    public TouchPortal(Vector2 center, float radius, float angle, float angularSpeed = .35f, float fireInterval = 1.7f,
        int pelletCount = 5, float spread = .72f, string owner = "dissonance_portal", Color? color = null,
        int polarity = 1, string movementPath = "square")
        : base(center, radius, angle, angularSpeed, fireInterval, pelletCount, spread, owner, color, polarity, movementPath)
    {
        Size = Simulation.TileSize * 1.12f;
        HitsToDisable = 8;
        Place();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (RemFlag)
            return;
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        var screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var outer = rect;
        outer.Inflate(10, 10);
        Primitives2D.RectOutline(spriteBatch, outer, UiTheme.Ink, 5);
        var inner = rect;
        inner.Inflate(4, 4);
        Primitives2D.RectOutline(spriteBatch, inner, Color, 3);
        for (int index = 0; index < 3; index++)
        {
            float y = rect.Y + rect.Height * (.25f + index * .25f);
            Primitives2D.Line(spriteBatch, new Vector2(rect.X + 8, y), new Vector2(rect.Right - 8, y), UiTheme.Cream, 2);
        }
    }
}
