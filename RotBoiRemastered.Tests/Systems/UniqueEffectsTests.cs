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
        var state = new RunState();
        var rng = new Random(1);
        bool applied = false;
        for (int i = 0; i < 200 && !applied; i++)
        {
            UniqueEffects.OnPlayerHit(boss, TestBullet(isCritical: true), weapon, state, rng);
            applied = boss.StatusEffects.ContainsKey("dread");
        }

        Assert.True(applied, "Dread should proc within 200 crit hits at a 32% per-hit chance.");
        Assert.Equal(1.15, StatusEffects.DamageMultiplier(boss));

        var control = StatusEffects.Update(boss, 0.1);
        Assert.True(control.MovementMultiplier < 1);
    }

    [Fact]
    public void OnPlayerHit_RunsEveryListedEffectIndependently_FromOneWeapon()
    {
        // Bow of Dread lists two EffectIds -- proves both fire off the same
        // weapon, from the same hits, without either one's roll/state
        // touching the other's.
        var boss = new Beaudis(0, 0, 100, new Random(4));
        var weapon = BowOfDread();
        var state = new RunState { HealthPoints = 500 };
        var rng = new Random(2);

        for (int i = 0; i < 200; i++)
            UniqueEffects.OnPlayerHit(boss, TestBullet(isCritical: true), weapon, state, rng);

        Assert.True(boss.StatusEffects.ContainsKey("dread"), "dread_on_hit should have procced at least once in 200 hits.");
        Assert.True(state.HealthPoints > 500, "dread_lifesteal should have healed the player at least once in 200 hits.");
    }

    [Fact]
    public void OnPlayerHit_DoesNothing_ForAWeaponWithNoEffectIds()
    {
        var boss = new Beaudis(0, 0, 100, new Random(4));
        var ironDagger = new ItemDrop(Items.DefinitionsByName["Iron Dagger"], "Epic");
        Assert.Null(ironDagger.Definition.EffectIds);

        var state = new RunState();
        var rng = new Random(1);
        for (int i = 0; i < 200; i++)
            UniqueEffects.OnPlayerHit(boss, TestBullet(isCritical: true), ironDagger, state, rng);

        Assert.False(boss.StatusEffects.ContainsKey("dread"));
    }
}
