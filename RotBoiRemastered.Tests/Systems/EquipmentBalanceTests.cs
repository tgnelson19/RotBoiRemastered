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
