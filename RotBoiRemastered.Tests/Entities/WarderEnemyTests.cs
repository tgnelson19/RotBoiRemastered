using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyTypes.py's WarderEnemy shield-hitbox/damage-routing behavior.</summary>
public class WarderEnemyTests
{
    private static WarderEnemy MakeWarder() =>
        new(100, 100, speed: 2, size: 40, Color.Blue, damage: 10, hp: 100, expValue: 10, difficulty: 1, rng: new Random(1));

    [Fact]
    public void GetWorldHitboxes_IncludesShieldFirst_WhileShieldHpPositive()
    {
        var warder = MakeWarder();
        var hitboxes = warder.GetWorldHitboxes();
        Assert.Equal("shield", hitboxes[0].Part);
        Assert.Contains(hitboxes, h => h.Part == "body");
    }

    [Fact]
    public void GetWorldHitboxes_OmitsShield_OnceDepleted()
    {
        var warder = MakeWarder();
        warder.TakeDamage(warder.ShieldHp + 1, "shield");
        var hitboxes = warder.GetWorldHitboxes();
        Assert.DoesNotContain(hitboxes, h => h.Part == "shield");
    }

    [Fact]
    public void TakeDamage_OnShield_AbsorbsWithoutTouchingHp()
    {
        var warder = MakeWarder();
        int startingHp = warder.Hp;
        var result = warder.TakeDamage(5, "shield");
        Assert.True(result.Blocked);
        Assert.Equal(startingHp, warder.Hp);
        Assert.True(warder.ShieldHp < warder.MaxShieldHp);
    }

    [Fact]
    public void TakeDamage_OnBody_BypassesShield()
    {
        var warder = MakeWarder();
        double startingShield = warder.ShieldHp;
        warder.TakeDamage(5, "body");
        Assert.Equal(startingShield, warder.ShieldHp);
        Assert.Equal(95, warder.Hp);
    }
}
