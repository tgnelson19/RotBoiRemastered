using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

public enum BitVfxLayer
{
    Ground,
    World,
    Overlay,
}

/// <summary>
/// Bounded, allocation-free-after-construction pixel debris used by combat
/// feedback. Ambient scenery remains deterministic geometry and does not
/// consume this budget.
/// </summary>
public sealed class BitVfxSystem
{
    public const int Capacity = 768;

    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Color Color;
        public float Life;
        public float MaxLife;
        public float Gravity;
        public int Size;
        public BitVfxLayer Layer;
        public VfxPrimitive Primitive;
        public float Rotation;
    }

    private readonly Particle[] _particles = new Particle[Capacity];
    private int _count;

    public int Count => _count;

    public void Clear() => _count = 0;

    public void EmitBurst(
        Vector2 worldPosition,
        Color primary,
        Color secondary,
        int requestedCount,
        float speed,
        float lifetime,
        BitVfxLayer layer,
        int seed,
        double intensity,
        Vector2 bias = default,
        float gravity = 0f,
        VfxPrimitive primitive = VfxPrimitive.Square)
    {
        int count = Math.Clamp(
            (int)MathF.Ceiling(requestedCount * (float)Math.Clamp(intensity, 0, 1)),
            0,
            requestedCount);
        if (count == 0)
            return;

        uint randomState = unchecked((uint)seed) ^ 0xA511E9B3u;
        for (int index = 0; index < count && _count < Capacity; index++)
        {
            float angle = index * MathF.Tau / count
                + (NextFloat(ref randomState) * .42f - .21f);
            float localSpeed = speed * (.55f + NextFloat(ref randomState) * .65f);
            _particles[_count++] = new Particle
            {
                Position = worldPosition,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * localSpeed + bias,
                Color = index % 4 == 0 ? secondary : primary,
                Life = lifetime * (.68f + NextFloat(ref randomState) * .45f),
                MaxLife = lifetime,
                Gravity = gravity,
                Size = 2 + (int)(NextFloat(ref randomState) * 4),
                Layer = layer,
                Primitive = primitive,
                Rotation = NextFloat(ref randomState) * MathF.Tau,
            };
        }
    }

    public void Emit(
        string recipeKey,
        Vector2 worldPosition,
        Color primary,
        Color secondary,
        int seed,
        double intensity,
        Vector2 bias = default)
    {
        if (!SoulVisualLanguage.VfxRecipes.TryGetValue(recipeKey, out VfxRecipe? recipe))
            throw new KeyNotFoundException($"Unknown VFX recipe: {recipeKey}");
        double recipeIntensity = recipe.Essential ? 1.0 : intensity;
        EmitBurst(
            worldPosition,
            primary,
            secondary,
            recipe.Count,
            recipe.Speed,
            recipe.Lifetime,
            recipe.Layer,
            seed,
            recipeIntensity,
            bias,
            recipe.Gravity,
            recipe.Primitive);
    }

    private static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00FFFFFF) / 16777216f;
    }

    public void Update(double seconds)
    {
        float dt = (float)Math.Clamp(seconds, 0, .05);
        float frameScale = dt * (float)Simulation.ReferenceFps;
        for (int index = _count - 1; index >= 0; index--)
        {
            Particle particle = _particles[index];
            particle.Position += particle.Velocity * frameScale;
            particle.Velocity.Y += particle.Gravity * frameScale;
            particle.Velocity *= MathF.Pow(.965f, frameScale);
            particle.Rotation += (.035f + particle.Velocity.Length() * .008f) * frameScale;
            particle.Life -= dt;
            if (particle.Life <= 0)
            {
                _particles[index] = _particles[--_count];
                continue;
            }
            _particles[index] = particle;
        }
    }

    public void Draw(
        SpriteBatch spriteBatch,
        BitVfxLayer layer,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        Rectangle viewport)
    {
        var expanded = viewport;
        expanded.Inflate(24, 24);
        for (int index = 0; index < _count; index++)
        {
            Particle particle = _particles[index];
            if (particle.Layer != layer)
                continue;
            Vector2 screen = camera.WorldToScreen(
                particle.Position, playerWorldPosition, screenShake);
            Point point = screen.ToPoint();
            if (!expanded.Contains(point))
                continue;
            float fade = Math.Clamp(particle.Life / Math.Max(.01f, particle.MaxLife), 0, 1);
            int size = Math.Max(1, (int)MathF.Ceiling(particle.Size * Math.Min(1f, fade * 1.8f)));
            DrawParticle(spriteBatch, particle, point, size, fade);
        }
    }

    private static void DrawParticle(
        SpriteBatch spriteBatch,
        Particle particle,
        Point point,
        int size,
        float fade)
    {
        Color color = particle.Color * fade;
        Vector2 center = point.ToVector2();
        Vector2 direction = particle.Velocity.LengthSquared() > .0001f
            ? Vector2.Normalize(particle.Velocity)
            : new Vector2(MathF.Cos(particle.Rotation), MathF.Sin(particle.Rotation));
        Vector2 side = new(-direction.Y, direction.X);
        switch (particle.Primitive)
        {
            case VfxPrimitive.Chip:
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle(point.X - size, point.Y - size / 2,
                        size * 2, Math.Max(1, size / 2)), color);
                break;
            case VfxPrimitive.Streak:
                Primitives2D.Line(spriteBatch,
                    center - direction * size * 1.8f,
                    center + direction * size * .7f,
                    color, Math.Max(1, size / 2));
                break;
            case VfxPrimitive.Shard:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    center + direction * size * 1.5f,
                    center + side * size * .65f,
                    center - direction * size,
                    center - side * size * .65f,
                }, color);
                break;
            case VfxPrimitive.ArcSegment:
                float radius = size * 1.7f;
                var arcRect = new Rectangle(
                    (int)(center.X - radius), (int)(center.Y - radius),
                    Math.Max(2, (int)(radius * 2)), Math.Max(2, (int)(radius * 2)));
                Primitives2D.Arc(spriteBatch, arcRect, particle.Rotation,
                    particle.Rotation + MathF.PI * .45f, color, Math.Max(1, size / 2));
                break;
            case VfxPrimitive.Afterimage:
                Primitives2D.FillQuad(spriteBatch,
                    center + direction * size,
                    center + side * size,
                    center - direction * size,
                    center - side * size,
                    color);
                break;
            default:
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle(point.X - size / 2, point.Y - size / 2, size, size),
                    color);
                break;
        }
    }
}
