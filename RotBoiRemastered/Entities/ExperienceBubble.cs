using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Experience pickup that wanders naturally, then homes toward the player once
/// inside the pickup aura. Ported from experienceBubble.py.
///
/// Cleanup vs. the Python original: `color` and the constructor's `frameRate`
/// argument were both assigned to fields but never read anywhere again --
/// dropped; the constructor takes an injectable `Random? rng` in frameRate's
/// old position instead, matching this port's usual testability convention
/// (Upgrades.cs/Items.cs). updateBubble(pAuraSpeed, pDX, pDY) never used
/// pDX/pDY -- dropped. The single-entry `_GLOW_CACHE` dict (keyed by radius,
/// holding a pre-rendered alpha surface) is skipped -- MonoGame's SpriteBatch
/// already alpha-blends by default, so the glow is just two translucent filled
/// circles drawn directly each frame; redoing that math per frame is cheap
/// enough not to need a cache. Update (physics/particles) and Draw (rendering)
/// are split, unlike Python's combined updateBubble, so movement/collision is
/// unit testable without a GraphicsDevice.
/// </summary>
public sealed class ExperienceBubble
{
    private struct CelebrationParticle
    {
        public float X, Y, Vx, Vy, Life;
        public int Size;
        public Color Color;
    }

    public float Size { get; }
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    public double Value { get; }
    public float Direction { get; set; }
    public float SpeedSpan { get; private set; } = 40f;
    public float Speed { get; private set; } = 1f;
    public bool NaturalSpawn { get; set; } = true;
    public float VisualAge { get; private set; }
    public string PickupKind => "experience";

    private readonly List<CelebrationParticle> _celebrationParticles = new();
    public int CelebrationParticleCount => _celebrationParticles.Count;

    public ExperienceBubble(float worldX, float worldY, double value, double difficultyDead, Random? rng = null, bool celebration = false)
    {
        Size = 20f * (float)difficultyDead;
        WorldX = worldX;
        WorldY = worldY;
        Value = value;
        rng ??= Random.Shared;
        Direction = rng.Next(0, 361) * 0.0174533f; // matches Python's randint(0, 360) degrees-to-radians

        if (celebration)
        {
            for (int index = 0; index < 56; index++)
            {
                float angle = index * 6.283185f / 56f + (float)(rng.NextDouble() * 0.16 - 0.08);
                float speed = (float)(1.5 + rng.NextDouble() * (5.2 - 1.5));
                _celebrationParticles.Add(new CelebrationParticle
                {
                    X = Size / 2f,
                    Y = Size / 2f,
                    Vx = MathF.Cos(angle) * speed,
                    Vy = MathF.Sin(angle) * speed,
                    Life = (float)(0.7 + rng.NextDouble() * (1.8 - 0.7)),
                    Size = rng.Next(2, 11),
                    Color = index % 4 == 0 ? UiTheme.Cream : UiTheme.Purple,
                });
            }
        }
    }

    public Rectangle WorldRect() => new((int)WorldX, (int)WorldY, (int)Size, (int)Size);

    public void Update(float playerAuraSpeed, Battleground battleground)
    {
        float frameScale = (float)Simulation.GetFrameScale();
        float seconds = (float)(Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate));
        VisualAge += seconds;

        for (int i = _celebrationParticles.Count - 1; i >= 0; i--)
        {
            var particle = _celebrationParticles[i];
            particle.X += particle.Vx * frameScale;
            particle.Y += particle.Vy * frameScale;
            particle.Vy += 0.08f * frameScale;
            particle.Life -= seconds;
            if (particle.Life <= 0)
                _celebrationParticles.RemoveAt(i);
            else
                _celebrationParticles[i] = particle;
        }

        if (NaturalSpawn)
        {
            if (SpeedSpan > 0)
                SpeedSpan -= frameScale;

            if (SpeedSpan <= 0)
                Speed = 0f;
            else if (SpeedSpan < 20)
                Speed = 1.25f;

            Rectangle currentWorld = WorldRect();
            float dX = Speed * MathF.Cos(Direction) * frameScale;
            float dY = Speed * MathF.Sin(Direction) * frameScale;

            var nextX = new Rectangle(currentWorld.X - (int)dX, currentWorld.Y, currentWorld.Width, currentWorld.Height);
            if (!battleground.RectHitsWall(nextX))
            {
                WorldX -= dX;
                currentWorld = nextX;
            }

            var nextY = new Rectangle(currentWorld.X, currentWorld.Y - (int)dY, currentWorld.Width, currentWorld.Height);
            if (!battleground.RectHitsWall(nextY))
                WorldY -= dY;
        }
        else
        {
            WorldX -= playerAuraSpeed * MathF.Cos(Direction) * frameScale;
            WorldY -= playerAuraSpeed * MathF.Sin(Direction) * frameScale;
        }
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);

        foreach (var particle in _celebrationParticles)
        {
            int size = Math.Max(1, (int)(particle.Size * Math.Min(1f, particle.Life * 1.5f)));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)(screenPosition.X + particle.X), (int)(screenPosition.Y + particle.Y), size, size),
                particle.Color);
        }

        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        float pulse = 0.88f + 0.12f * MathF.Sin(VisualAge * 7f);
        float radius = Math.Max(5f, Size * 0.62f * pulse);

        Primitives2D.FillCircle(spriteBatch, center, radius * 2f, new Color(UiTheme.Green, 35));
        Primitives2D.CircleOutline(spriteBatch, center, radius * 1.35f, new Color(UiTheme.Cream, 65), 2);

        float angle = VisualAge * 2.4f;
        var diamond = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            float a = angle + i * MathF.PI / 2f;
            diamond[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        var shadowDiamond = diamond.Select(p => p + new Vector2(3, 3)).ToArray();
        Primitives2D.FillPolygon(spriteBatch, shadowDiamond, UiTheme.Shadow);
        Primitives2D.FillPolygon(spriteBatch, diamond, UiTheme.Green);
        Primitives2D.PolygonOutline(spriteBatch, diamond, UiTheme.Cream, Math.Max(2, (int)(radius * 0.18f)));
        Primitives2D.FillCircle(spriteBatch, center, Math.Max(2f, radius * 0.28f), UiTheme.Text);

        for (int i = 0; i < 4; i++)
        {
            float orbitAngle = -angle * 1.35f + i * MathF.PI / 2f;
            var pip = center + new Vector2(MathF.Cos(orbitAngle), MathF.Sin(orbitAngle)) * (radius * 1.55f);
            Primitives2D.FillCircle(spriteBatch, pip, Math.Max(2f, radius * 0.13f), UiTheme.Gold);
        }
    }
}
