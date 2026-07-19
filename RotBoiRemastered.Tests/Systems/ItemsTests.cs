using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

/// <summary>Ported from tests/test_items.py.</summary>
public class ItemsTests
{
    [Fact]
    public void RollDropCount_StaysInRange()
    {
        var rng = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            Assert.InRange(Items.RollDropCount(rng), 0, 4);
        }
    }

    [Fact]
    public void RollDropCount_IsReproducible()
    {
        // Mirrors test_roll_drop_count_is_reproducible in tests/test_items.py:
        // a fresh Random(11) each iteration means every draw is the same value.
        // That's a quirk of the original test, kept intentionally for parity.
        var left = Enumerable.Range(0, 20).Select(_ => Items.RollDropCount(new Random(11))).ToList();
        var right = Enumerable.Range(0, 20).Select(_ => Items.RollDropCount(new Random(11))).ToList();
        Assert.Equal(left, right);
    }

    [Fact]
    public void GenerateDrop_HasValidSlotTypeAndRarity()
    {
        var rng = new Random(2);
        for (int i = 0; i < 200; i++)
        {
            var drop = Items.GenerateDrop(rng);
            Assert.Contains(drop.SlotType, Items.SlotTypes);
            Assert.Contains(drop.Rarity, Upgrades.RarityWeights.Keys);
        }
    }

    [Fact]
    public void GenerateDrops_IsReproducible()
    {
        var left = Items.GenerateDrops(4, rng: new Random(42));
        var right = Items.GenerateDrops(4, rng: new Random(42));
        Assert.Equal(left, right);
    }

    [Fact]
    public void GenerateDrops_ReturnsRequestedCount()
    {
        var drops = Items.GenerateDrops(3, rng: new Random(5));
        Assert.Equal(3, drops.Count);
    }

    [Fact]
    public void AttackSpeedDisplay_TreatsShorterDelayAsPositive()
    {
        Assert.Equal("+4%", new ItemEffectView("Attack Speed", 0, .96).DisplayValue);
        Assert.True(new ItemEffectView("Attack Speed", -3, 1).IsBeneficial);
        Assert.Equal("-10%", new ItemEffectView("Attack Speed", 0, 1.10).DisplayValue);
    }

    [Fact]
    public void RollUniqueDrop_NeverDropsForANonMatchingBossKey()
    {
        var rng = new Random(3);
        for (int i = 0; i < 500; i++)
            Assert.Null(Items.RollUniqueDrop("beaudis", rng));
    }

    [Fact]
    public void RollUniqueDrop_CanDropForItsBossKey_AsFixedPowerUniqueRarity()
    {
        var rng = new Random(7);
        var drops = Enumerable.Range(0, 500).Select(_ => Items.RollUniqueDrop("sting", rng)).Where(drop => drop is not null).ToList();

        Assert.NotEmpty(drops);
        Assert.All(drops, drop =>
        {
            Assert.Equal("Unique", drop!.Rarity);
            Assert.Equal("Bow of Dread", drop.Name);
        });
    }

    [Fact]
    public void RarityPower_UniqueIsFixedAtFullPower_NotScaledLikeAnUnrecognizedRarity()
    {
        Assert.Equal(1.0, Items.RarityPower("Unique"));
    }

    [Fact]
    public void Deserialize_RoundTripsAUniqueItem_DespiteItsRarityNotBeingInRarityOrder()
    {
        var original = new ItemDrop(Items.UniquesByName["Bow of Dread"], "Unique");

        var restored = Items.Deserialize(Items.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal("Bow of Dread", restored!.Name);
        Assert.Equal("Unique", restored.Rarity);
        Assert.DoesNotContain("Unique", Upgrades.RarityOrder);
    }

    [Fact]
    public void GenerateDrop_NeverProducesAUniqueItem()
    {
        var rng = new Random(9);
        for (int i = 0; i < 500; i++)
            Assert.DoesNotContain(Items.GenerateDrop(rng).Name, Items.UniquesByName.Keys);
    }
}
