using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Fragile, fast offspring created only by a ParentEnemy threshold. Ported
/// from enemyTypes.py's ChildEnemy. No Update/Fire override -- reuses
/// Enemy's base chase-and-wall-slide behavior as-is.
/// </summary>
public sealed class ChildEnemy : Enemy
{
    public Enemy? Parent { get; set; }

    public ChildEnemy(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, string archetype = "runner",
        string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        AwarenessState = "alerted";
        ThreatCost = .5;
        Family = "parent";
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        var rect = RenderPose(camera, playerWorldPosition, screenShake).Rect;
        if (Parent is not null && !Parent.IsDead())
        {
            Vector2 parent = camera.WorldToScreen(
                new Vector2(Parent.WorldX + Parent.Size / 2f, Parent.WorldY + Parent.Size / 2f),
                playerWorldPosition, screenShake);
            Primitives2D.Line(spriteBatch,
                new Vector2(rect.Center.X, rect.Center.Y), parent,
                UiTheme.Purple * (.25f + .18f * MathF.Sin(Age * .13f)), 2);
        }
        Vector2 core = new(rect.Center.X, rect.Center.Y);
        float coreRadius = Math.Max(2, Size * .12f);
        // Tier 1: shade the disc toward its shadow side and add a small
        // upper-left highlight dot -- the cheapest version of the shared
        // highlight/shadow bevel trick, selling the core as a lit sphere
        // instead of a flat disc.
        Primitives2D.FillCircle(spriteBatch, core, coreRadius, Color.Lerp(UiTheme.Cream, UiTheme.Ink, .3f));
        Primitives2D.FillCircle(spriteBatch, core - new Vector2(coreRadius * .3f, coreRadius * .3f),
            coreRadius * .4f, UiTheme.Cream);
    }
}
