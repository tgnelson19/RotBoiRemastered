using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Presentation;

namespace RotBoiRemastered.Tests.Entities;

public sealed class BitVfxSystemTests
{
    [Fact]
    public void ZeroIntensity_EmitsNoOptionalParticles()
    {
        var effects = new BitVfxSystem();

        effects.EmitBurst(
            Vector2.Zero, Color.Red, Color.White,
            20, 2, 1, BitVfxLayer.World, 1, intensity: 0);

        Assert.Equal(0, effects.Count);
    }

    [Fact]
    public void Intensity_ScalesDensityAndCapacityIsBounded()
    {
        var half = new BitVfxSystem();
        half.EmitBurst(
            Vector2.Zero, Color.Red, Color.White,
            20, 2, 1, BitVfxLayer.World, 1, intensity: .5);
        Assert.Equal(10, half.Count);

        var full = new BitVfxSystem();
        for (int index = 0; index < 100; index++)
        {
            full.EmitBurst(
                Vector2.Zero, Color.Red, Color.White,
                20, 2, 1, BitVfxLayer.World, index, intensity: 1);
        }
        Assert.Equal(BitVfxSystem.Capacity, full.Count);
    }

    [Fact]
    public void UpdateExpiresParticlesWithoutAllocatingNewOnes()
    {
        var effects = new BitVfxSystem();
        effects.EmitBurst(
            Vector2.Zero, Color.Red, Color.White,
            4, 2, .01f, BitVfxLayer.World, 1, intensity: 1);

        effects.Update(.05);

        Assert.Equal(0, effects.Count);
    }

    [Fact]
    public void RecipeEmissionPreservesEssentialCuesAtZeroIntensity()
    {
        var effects = new BitVfxSystem();

        effects.Emit(
            "impact", Vector2.Zero, Color.Red, Color.White,
            seed: 7, intensity: 0);
        Assert.Equal(
            SoulVisualLanguage.VfxRecipes["impact"].Count,
            effects.Count);

        effects.Clear();
        effects.Emit(
            "death", Vector2.Zero, Color.Red, Color.White,
            seed: 7, intensity: 0);
        Assert.Equal(0, effects.Count);
    }

    [Fact]
    public void EveryRecipeEmitsWithinTheSharedCapacity()
    {
        var effects = new BitVfxSystem();
        int seed = 0;
        foreach (string recipe in SoulVisualLanguage.VfxRecipes.Keys)
        {
            effects.Emit(
                recipe, Vector2.Zero, Color.Red, Color.White,
                seed++, intensity: 1);
        }

        Assert.InRange(effects.Count, 1, BitVfxSystem.Capacity);
    }
}
