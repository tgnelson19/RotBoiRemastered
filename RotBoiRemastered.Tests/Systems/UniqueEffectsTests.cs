using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

public class UniqueEffectsTests
{
    private static ItemDrop BowOfDread() => new(Items.UniquesByName["Bow of Dread"], "Unique");

    private static Bullet TestBullet(bool isCritical) =>
        new(0, 0, speed: 1, direction: 0, bulletRange: 100, size: 10, Color.White, pierce: 1, damage: 10, isCritical);

    [Fact]
    public void OnPlayerHit_CanApplyDread_WhichSlowsAndAddsDamageTaken()
    {
        var boss = new Beaudis(0, 0, 100, new Random(4));
        var weapon = BowOfDread();
        var rng = new Random(1);
        bool applied = false;
        for (int i = 0; i < 200 && !applied; i++)
        {
            UniqueEffects.OnPlayerHit(boss, TestBullet(isCritical: true), weapon, rng);
            applied = boss.StatusEffects.ContainsKey("dread");
        }

        Assert.True(applied, "Dread should proc within 200 crit hits at a 32% per-hit chance.");
        Assert.Equal(1.15, StatusEffects.DamageMultiplier(boss));

        var control = StatusEffects.Update(boss, 0.1);
        Assert.True(control.MovementMultiplier < 1);
    }

    [Fact]
    public void OnPlayerHit_DoesNothing_ForAWeaponWithNoEffectId()
    {
        var boss = new Beaudis(0, 0, 100, new Random(4));
        var ironDagger = new ItemDrop(Items.DefinitionsByName["Iron Dagger"], "Epic");
        Assert.Null(ironDagger.Definition.EffectId);

        var rng = new Random(1);
        for (int i = 0; i < 200; i++)
            UniqueEffects.OnPlayerHit(boss, TestBullet(isCritical: true), ironDagger, rng);

        Assert.False(boss.StatusEffects.ContainsKey("dread"));
    }
}
