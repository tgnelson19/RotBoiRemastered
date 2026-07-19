using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

[Collection("GameProfileState")]
public class EquipmentBalanceTests : IDisposable
{
    private readonly GameProfileData _originalProfile = GameProfile.Profile;
    private readonly string _originalPath = GameProfile.SavePath;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("rotboi-equipment-tests-").FullName;

    public EquipmentBalanceTests()
    {
        GameProfile.Profile = new GameProfileData();
        GameProfile.SavePath = Path.Combine(_tempDir, "profile.json");
    }

    public void Dispose()
    {
        GameProfile.Profile = _originalProfile;
        GameProfile.SavePath = _originalPath;
        Directory.Delete(_tempDir, recursive: true);
    }

    private static ItemDrop Epic(string name) => new(Items.DefinitionsByName[name], "Epic");

    [Fact]
    public void WeaponArchetypes_FollowDamageAndRangeSpectrum()
    {
        string[] names = { "Iron Dagger", "Iron Sword", "Iron Spear", "Hunting Bow", "Ash Wand" };
        var damage = names.Select(name => Items.AdjustStat("Bullet Damage", 100, new ItemDrop?[] { Epic(name) })).ToArray();
        var range = names.Select(name => Items.AdjustStat("Bullet Range", 250, new ItemDrop?[] { Epic(name) })).ToArray();

        Assert.True(damage.SequenceEqual(damage.OrderDescending()));
        Assert.True(range.SequenceEqual(range.Order()));
        Assert.True(range[^1] >= range[0] * 7);
        Assert.True(damage[0] >= damage[^1] * 2.5);
    }

    [Fact]
    public void EveryWeapon_PreservesPlayableDamageAndRangeFloors()
    {
        foreach (var definition in Items.Definitions.Where(item => item.SlotType == "weapon"))
        {
            var drop = new ItemDrop(definition, "Mythical");
            Assert.InRange(Items.AdjustStat("Bullet Damage", 100, new ItemDrop?[] { drop }), Items.MinBulletDamage, Items.MaxBulletDamage);
            Assert.InRange(Items.AdjustStat("Bullet Range", 250, new ItemDrop?[] { drop }), Items.MinBulletRange, Items.MaxBulletRange);
        }
    }

    [Fact]
    public void RustySword_IsFasterButWeaker_AndBloodySwordCarriesBleed()
    {
        var rusty = Epic("Rusty Sword");
        Assert.True(Items.AdjustStat("Attack Speed", 40, new ItemDrop?[] { rusty }) < 24);
        Assert.True(Items.AdjustStat("Bullet Damage", 100, new ItemDrop?[] { rusty }) < 70);
        Assert.True(Items.StatusChances(new ItemDrop?[] { Epic("Bloody Sword") }).GetValueOrDefault("bleed") > .1);
    }

    [Fact]
    public void Grimsbane_CarriesBleed_AtFullFixedUniquePower()
    {
        var grimsbane = new ItemDrop(Items.UniquesByName["Grimsbane"], "Unique");
        // Unique power is always fixed at 1.0 (see Items.RarityPower), so
        // whatever bleed chance is authored on Grimsbane comes through
        // unscaled -- no Epic/Legendary roll to account for like a regular
        // item's StatusChances would need. Reads the authored chance rather
        // than hardcoding it, so this stays valid as that number gets tuned.
        double authoredChance = grimsbane.Definition.StatusChances!["bleed"];
        Assert.Equal(authoredChance, Items.StatusChances(new ItemDrop?[] { grimsbane }).GetValueOrDefault("bleed"));
    }

    [Fact]
    public void Grimsbane_AttackSpeedModifier_IsFasterNotSlower()
    {
        // Grimsbane is authored as Mult("Attack Speed", 200) -- a number well
        // above the neutral 100, meant to read as "attacks twice as fast".
        // Because Attack Speed is stored internally as a cooldown (smaller =
        // faster), a broken Mult() that forgets to take the reciprocal would
        // silently produce a *slower* weapon instead, exactly the "-50%
        // instead of +100%" bug this guards against. Reads the effect
        // straight off Grimsbane's own definition rather than duplicating
        // Mult()'s formula, so it stays valid as the authored number is
        // retuned -- only the sign/direction is pinned, not the magnitude.
        var grimsbane = new ItemDrop(Items.UniquesByName["Grimsbane"], "Unique");
        var attackSpeed = Items.Effects(grimsbane).Single(e => e.Stat == "Attack Speed");

        Assert.True(attackSpeed.Multiplier < 1, $"A weapon authored with Attack Speed above 100 should have a shorter (sub-1) cooldown ratio, but was {attackSpeed.Multiplier}.");
        Assert.True(attackSpeed.IsBeneficial);
        Assert.StartsWith("+", attackSpeed.DisplayValue);
    }

    [Fact]
    public void Defense_NeverExceedsNinety_EvenWithPlateAndUpgradeStacks()
    {
        var state = new RunState();
        state.Stats["Defense"].Additive.Add(1000);
        state.SetEquipment(new Dictionary<string, ItemDrop?>
        {
            ["weapon"] = null, ["armor"] = new(Items.DefinitionsByName["Plate Armor"], "Mythical"),
            ["ring"] = null, ["accessory_1"] = null, ["accessory_2"] = null,
        });
        Assert.Equal(Items.MaxDefense, state.Defense);
    }

    [Fact]
    public void BleedTicks_DamageBossWithoutBuildingStagger()
    {
        var boss = new Beaudis(0, 0, 100, new Random(4));
        int before = boss.Hp;
        StatusEffects.Apply(boss, "bleed", 3.2, .006);

        StatusEffects.Update(boss, 1.0);

        Assert.True(boss.Hp < before);
        Assert.Equal(0, boss.Stagger);
        boss.TakeDamage(100);
        Assert.True(boss.Stagger > 0);
    }
}
