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
    public void EveryUniqueWithEffectIds_HasAnEffectFlavorTextCallout()
    {
        // EffectFlavorText is what InformationSheet.DrawItemTooltip shows in
        // place of a StatusChances "X% ON HIT" row for a unique's signature
        // effect (guaranteed procs like Grimsbane's Bane stacking never
        // generate one of those rows on their own, since they're not chance-
        // based) -- without it, the tooltip would silently give no on-hit
        // effect other than whatever an item shares with regular items.
        foreach (var unique in Items.Uniques.Where(item => item.EffectIds is { Count: > 0 }))
            Assert.False(string.IsNullOrWhiteSpace(unique.EffectFlavorText), $"{unique.Name} has EffectIds but no EffectFlavorText.");
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
            Assert.Contains(drop.Grade, Items.GradeOrder);
            Assert.Contains(Items.AffixesFor(drop.SlotType), affix => affix.Name == drop.Modifier);
        }
    }

    [Fact]
    public void GradeCatalog_HasRequestedPowerCurveAndOneInFiftySWeight()
    {
        Assert.Equal(new[] { "F", "D", "C", "B", "A", "S" }, Items.GradeOrder);
        Assert.Equal(.60, Items.GradePowers["F"]);
        Assert.Equal(.70, Items.GradePowers["D"]);
        Assert.Equal(.80, Items.GradePowers["C"]);
        Assert.Equal(.90, Items.GradePowers["B"]);
        Assert.Equal(.95, Items.GradePowers["A"]);
        Assert.Equal(1.00, Items.GradePowers["S"]);
        Assert.Equal(100, Items.GradeWeights.Values.Sum());
        Assert.Equal(2, Items.GradeWeights["S"]);
    }

    [Fact]
    public void RollGrade_IsWeightedTowardLowGrades_WithApproximatelyTwoPercentS()
    {
        var rng = new Random(2026);
        var counts = Items.GradeOrder.ToDictionary(grade => grade, _ => 0);
        for (int index = 0; index < 100_000; index++)
            counts[Items.RollGrade(rng)]++;

        Assert.True(counts["F"] > counts["D"]);
        Assert.True(counts["D"] > counts["C"]);
        Assert.True(counts["C"] > counts["B"]);
        Assert.True(counts["B"] > counts["A"]);
        Assert.True(counts["A"] > counts["S"]);
        Assert.InRange(counts["S"], 1_800, 2_200);
    }

    [Fact]
    public void GradeScalesItemDeltaSeparatelyFromRarity()
    {
        var definition = Items.DefinitionsByName["Iron Sword"];
        var f = new ItemDrop(definition, "Epic", "F", "Balanced");
        var s = new ItemDrop(definition, "Epic", "S", "Balanced");

        double fDamage = Items.AdjustStat("Bullet Damage", 100, new ItemDrop?[] { f });
        double sDamage = Items.AdjustStat("Bullet Damage", 100, new ItemDrop?[] { s });

        Assert.Equal(.60, (fDamage - 100) / (sDamage - 100), precision: 10);
        Assert.Equal(f.Rarity, s.Rarity);
    }

    [Fact]
    public void LazyAndFastWeaponModifiers_PullProjectileStatsInOppositeDirections()
    {
        var definition = Items.DefinitionsByName["Iron Sword"];
        var lazy = new ItemDrop(definition, "Epic", "S", "Lazy");
        var fast = new ItemDrop(definition, "Epic", "S", "Fast");
        var equipmentLazy = new ItemDrop?[] { lazy };
        var equipmentFast = new ItemDrop?[] { fast };

        Assert.True(Items.AdjustStat("Bullet Speed", 4, equipmentLazy) < Items.AdjustStat("Bullet Speed", 4, equipmentFast));
        Assert.True(Items.AdjustStat("Bullet Range", 250, equipmentLazy) > Items.AdjustStat("Bullet Range", 250, equipmentFast));
        Assert.True(Items.AdjustStat("Bullet Damage", 100, equipmentLazy) > Items.AdjustStat("Bullet Damage", 100, equipmentFast));
    }

    [Fact]
    public void ArmorRingAndAccessory_HaveExclusiveMultiStatModifierPools()
    {
        foreach (string slot in new[] { "armor", "ring", "accessory" })
        {
            var exclusive = Items.Affixes.Where(affix => affix.SlotType == slot).ToList();
            Assert.True(exclusive.Count >= 2, $"{slot} needs at least two exclusive affixes.");
            Assert.All(exclusive, affix => Assert.True(affix.Modifiers.Count >= 2,
                $"{affix.Name} should demonstrate at least two stat changes."));
        }
    }

    [Fact]
    public void Serialize_RoundTripsGradeAndModifier()
    {
        var original = new ItemDrop(Items.DefinitionsByName["Ash Wand"], "Legendary", "B", "Lazy");

        var restored = Items.Deserialize(Items.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Deserialize_OldTwoFieldSaveMigratesWithoutNerfingExistingItem()
    {
        var stored = System.Text.Json.JsonSerializer.Deserialize<StoredItemData>(
            "{\"Name\":\"Iron Sword\",\"Rarity\":\"Epic\"}");

        var restored = Items.Deserialize(stored);

        Assert.NotNull(restored);
        Assert.Equal("S", restored!.Grade);
        Assert.Equal("Balanced", restored.Modifier);
    }

    [Fact]
    public void CoreForgeCatalog_CoversEveryPathWithTheRequestedIdentity()
    {
        Assert.Equal(5, Items.CoreForges.Count);
        Assert.Equal("rot", Items.CoreForgesByPathKey["touch"].Key);
        Assert.Equal("malady", Items.CoreForgesByPathKey["phantasia"].Key);
        Assert.Equal("dissonance", Items.CoreForgesByPathKey["sound"].Key);
        Assert.Equal("ache", Items.CoreForgesByPathKey["chemesthesis"].Key);
        Assert.Equal("chronos", Items.CoreForgesByPathKey["sight"].Key);
    }

    [Theory]
    [InlineData("Common", 0)]
    [InlineData("Rare", 0)]
    [InlineData("Epic", .10)]
    [InlineData("Legendary", .20)]
    [InlineData("Mythical", .35)]
    [InlineData("Unique", 0)]
    public void CoreForgeChance_RequiresEpicOrHigherRegularRarity(string rarity, double expected) =>
        Assert.Equal(expected, Items.CoreForgeChance(rarity));

    [Fact]
    public void RollCoreForge_RequiresHardModeAndUsesTheActivePathsCore()
    {
        var epic = new ItemDrop(Items.DefinitionsByName["Iron Sword"], "Epic", "S", "Balanced");
        Assert.Null(Items.RollCoreForge(epic, hardMode: false, "touch", new Random(1)).CoreForge);

        foreach (var core in Items.CoreForges)
        {
            var rolls = Enumerable.Range(0, 1_000)
                .Select(_ => Items.RollCoreForge(epic, hardMode: true, core.PathKey, new Random(_ + 10)))
                .Where(drop => drop.CoreForge is not null)
                .ToList();
            Assert.NotEmpty(rolls);
            Assert.All(rolls, drop => Assert.Equal(core.Key, drop.CoreForge));
        }
    }

    [Fact]
    public void RollCoreForge_RarityRatesTrackTenTwentyAndThirtyFivePercent()
    {
        var definition = Items.DefinitionsByName["Iron Sword"];
        var rng = new Random(904);
        int RollCount(string rarity) => Enumerable.Range(0, 20_000)
            .Count(_ => Items.RollCoreForge(new ItemDrop(definition, rarity), true, "sound", rng).CoreForge is not null);

        Assert.InRange(RollCount("Epic"), 1_850, 2_150);
        Assert.InRange(RollCount("Legendary"), 3_750, 4_250);
        Assert.InRange(RollCount("Mythical"), 6_700, 7_300);
    }

    [Fact]
    public void GenerateDrops_CoreForgesOnlyEligibleItemsInHardModeForThatPath()
    {
        var normal = Items.GenerateDrops(5_000, new Random(12), hardMode: false, pathKey: "touch");
        var hard = Items.GenerateDrops(5_000, new Random(12), hardMode: true, pathKey: "touch");

        Assert.All(normal, drop => Assert.Null(drop.CoreForge));
        var coreDrops = hard.Where(drop => drop.CoreForge is not null).ToList();
        Assert.NotEmpty(coreDrops);
        Assert.All(coreDrops, drop =>
        {
            Assert.Equal("rot", drop.CoreForge);
            Assert.Contains(drop.Rarity, new[] { "Epic", "Legendary", "Mythical" });
        });
    }

    [Fact]
    public void CoreForgeBonuses_AreExactAndDoNotShrinkWithGrade()
    {
        var definition = Items.DefinitionsByName["Iron Sword"];
        var fAche = new ItemDrop(definition, "Epic", "F", "Balanced", "ache");
        var sAche = new ItemDrop(definition, "Mythical", "S", "Balanced", "ache");

        var fBonus = Items.Effects(fAche).Single(effect => effect.Stat == "Bullet Count");
        var sBonus = Items.Effects(sAche).Single(effect => effect.Stat == "Bullet Count");

        Assert.Equal(2, fBonus.Additive);
        Assert.Equal(2, sBonus.Additive);
    }

    [Fact]
    public void EquippedCoreForges_ReturnsOneRingIdentityPerDistinctCore()
    {
        var definition = Items.DefinitionsByName["Iron Sword"];
        var equipment = new ItemDrop?[]
        {
            new(definition, "Epic", CoreForge: "rot"),
            new(definition, "Legendary", CoreForge: "rot"),
            new(definition, "Mythical", CoreForge: "chronos"),
            null,
        };

        var cores = Items.EquippedCoreForges(equipment);

        Assert.Equal(2, cores.Count);
        Assert.Contains(cores, core => core.Key == "rot");
        Assert.Contains(cores, core => core.Key == "chronos");
    }

    [Fact]
    public void Serialize_RoundTripsCoreForgeIdentity()
    {
        var original = new ItemDrop(Items.DefinitionsByName["Iron Spear"], "Legendary", "A", "Fast", "chronos");

        var restored = Items.Deserialize(Items.Serialize(original));

        Assert.Equal(original, restored);
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
        // Multiplier is a cooldown ratio for Attack Speed (smaller = faster);
        // DisplayValue inverts it to the actual speed ratio before turning
        // that into a percent, so a .96 cooldown ratio (attacks 1/.96 =
        // 1.041666...x as often) reads as "+4.17%", not the raw "+4%" you'd
        // get by applying (1 - Multiplier) * 100 directly to the ratio.
        Assert.Equal("+4.17%", new ItemEffectView("Attack Speed", 0, .96).DisplayValue);
        Assert.True(new ItemEffectView("Attack Speed", -3, 1).IsBeneficial);
        Assert.Equal("-9.09%", new ItemEffectView("Attack Speed", 0, 1.10).DisplayValue);
    }

    [Fact]
    public void AttackSpeedMult_ReciprocalAndDisplayValue_StayInLockstep()
    {
        // Regression test: Items.Mult("Attack Speed", percent) is documented to
        // store 100/percent as the cooldown ratio (so "200" means attacking
        // twice as fast), and ItemEffectView.DisplayValue is documented to
        // un-invert that same ratio before turning it into a percent. These
        // two have already drifted out of sync once in practice -- Mult()
        // reverted to the plain percent/100 form while DisplayValue still
        // expected the inverted ratio, which silently turned a "200" (meant
        // to double attack speed) into a displayed "-50%" that actually
        // halved it. Mult() itself is private, so this pins its documented
        // output (100/200 = .5) directly and checks DisplayValue interprets
        // that ratio as "attacks twice as fast", not "half as fast".
        var doubledAttackSpeed = new ItemEffectView("Attack Speed", 0, 100.0 / 200.0);
        Assert.Equal("+100%", doubledAttackSpeed.DisplayValue);
        Assert.True(doubledAttackSpeed.IsBeneficial);
    }

    [Fact]
    public void RollUniqueDrop_NeverDropsForANonMatchingBossKey()
    {
        var rng = new Random(3);
        for (int i = 0; i < 500; i++)
            Assert.Null(Items.RollUniqueDrop("beaudis", rng));
    }

    [Fact]
    public void RollUniqueDrop_CanDropForItsBossKey_WithUniqueRarityAndIndependentGrade()
    {
        var rng = new Random(7);
        var drops = Enumerable.Range(0, 500).Select(_ => Items.RollUniqueDrop("rot", rng)).Where(drop => drop is not null).ToList();

        Assert.NotEmpty(drops);
        Assert.All(drops, drop =>
        {
            Assert.Equal("Unique", drop!.Rarity);
            Assert.Equal("Bow of Dread", drop.Name);
            Assert.Contains(drop.Grade, Items.GradeOrder);
            Assert.Contains(Items.AffixesFor("weapon"), affix => affix.Name == drop.Modifier);
        });
    }

    [Fact]
    public void RarityPower_UniqueContributesFullPowerBeforeIndependentGradeScaling()
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
