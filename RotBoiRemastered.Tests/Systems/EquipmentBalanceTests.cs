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
    private static ItemDrop EpicCore(string core) =>
        new(Items.DefinitionsByName["Iron Sword"], "Epic", "S", "Balanced", core);

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
    public void Grimsbane_CarriesBleed_AtUniqueRarityAndDefaultSGrade()
    {
        var grimsbane = new ItemDrop(Items.UniquesByName["Grimsbane"], "Unique");
        // The explicit constructor defaults to S grade, while Unique rarity
        // contributes power 1.0, so the authored chance comes through
        // unscaled. Naturally dropped uniques still roll F-S like every item.
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
    public void CoreForgePackages_ApplyTheirRequestedCombatIdentities()
    {
        var plain = Epic("Iron Sword");
        double Stat(string stat, double baseline, ItemDrop item) =>
            Items.AdjustStat(stat, baseline, new ItemDrop?[] { item });

        var rot = EpicCore("rot");
        Assert.True(Stat("Defense", 0, rot) > Stat("Defense", 0, plain));
        Assert.True(Stat("Health", 1000, rot) > Stat("Health", 1000, plain));
        Assert.True(Stat("Bullet Damage", 100, rot) < Stat("Bullet Damage", 100, plain));
        Assert.True(Stat("Player Speed", 2.1, rot) < Stat("Player Speed", 2.1, plain));

        var malady = EpicCore("malady");
        Assert.True(Stat("Bullet Speed", 4, malady) < Stat("Bullet Speed", 4, plain));
        Assert.True(Stat("Attack Speed", 40, malady) > Stat("Attack Speed", 40, plain));
        Assert.True(Stat("Bullet Damage", 100, malady) >= Stat("Bullet Damage", 100, plain) * 1.5);

        var dissonance = EpicCore("dissonance");
        Assert.True(Stat("Attack Speed", 40, dissonance) < Stat("Attack Speed", 40, plain));
        Assert.True(Stat("Bullet Damage", 100, dissonance) > Stat("Bullet Damage", 100, plain));
        Assert.True(Stat("Player Speed", 2.1, dissonance) < Stat("Player Speed", 2.1, plain));

        var ache = EpicCore("ache");
        Assert.Equal(3, Stat("Bullet Count", 1, ache));
        Assert.True(Stat("Spread Angle", Math.PI / 8, ache) > 1.0);
        Assert.True(Stat("Attack Speed", 40, ache) < Stat("Attack Speed", 40, plain));

        var chronos = EpicCore("chronos");
        Assert.Equal(2, Stat("Bullet Count", 1, chronos));
        Assert.True(Stat("Defense", 0, chronos) > 0);
        Assert.True(Stat("Attack Speed", 40, chronos) < Stat("Attack Speed", 40, plain));
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
