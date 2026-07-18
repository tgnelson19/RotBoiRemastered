using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Floating combat text that rises above the target and fades out. Ported
/// from damageText.py.
///
/// Cleanup vs. the Python original: drawAndUpdateDamageText(pDX, pDY) never
/// referenced pDX/pDY in its body (every call site passed the player's
/// dX/dY for no effect) -- dropped. lifetimeMax/frameRate fields were set
/// from the constructor's `framerate` argument (really a lifetime-in-frames
/// duration, confusingly named after the global frame rate) but never read
/// again anywhere in the codebase -- dropped, keeping only the one field
/// that's actually used (Lifetime). Update and Draw are split (Python
/// combined them in one method) so the countdown/expiry logic is unit
/// testable without a GraphicsDevice.
/// </summary>
public sealed class DamageText
{
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    public Color Color { get; }
    public object Value { get; }
    public float ObjSize { get; }
    public float Lifetime { get; private set; }
    public bool DeleteMe { get; private set; }
    private float _deltaVal = 10f;

    public DamageText(float worldX, float worldY, Color color, object value, float objSize, float lifetimeFrames)
    {
        WorldX = worldX;
        WorldY = worldY;
        Color = color;
        Value = value;
        ObjSize = objSize;
        Lifetime = lifetimeFrames / 2f;
    }

    public void Update()
    {
        if (Lifetime > 0)
        {
            Lifetime -= (float)Simulation.GetFrameScale() * 2f;
            _deltaVal += (float)Simulation.GetFrameScale();
        }
        if (Lifetime <= 0)
            DeleteMe = true;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake, double fontSize)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        string label = Value is double or float or int
            ? Math.Round(Convert.ToDouble(Value)).ToString("0")
            : Value.ToString() ?? "";
        var center = new Vector2(screenPosition.X + ObjSize / 2f, screenPosition.Y - _deltaVal);
        UiTheme.DrawText(spriteBatch, label, fontSize, UiTheme.Ink, center + new Vector2(3, 3), "center");
        UiTheme.DrawText(spriteBatch, label, fontSize, Color, center, "center");
    }
}
