using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from projectilePortal.py's orbit placement, damage/disable, and firing behavior.</summary>
public class ProjectilePortalTests
{
    [Fact]
    public void DefaultOrbitPath_PlacesOnTheCircleAroundCenter()
    {
        var center = new Vector2(100, 100);
        var portal = new ProjectilePortal(center, radius: 50, angle: 0f);
        // worldX/Y are the top-left corner of a Size-square centered on the orbit point.
        Assert.Equal(center.X + 50 - portal.Size / 2f, portal.WorldX, 3);
        Assert.Equal(center.Y - portal.Size / 2f, portal.WorldY, 3);
    }

    [Fact]
    public void TakeDamage_DisablesPortal_AfterThreeHits()
    {
        var portal = new ProjectilePortal(Vector2.Zero, radius: 10, angle: 0f);
        Assert.True(portal.BlocksShots);
        portal.TakeDamage(1);
        portal.TakeDamage(1);
        bool disabledNow = portal.TakeDamage(1);
        Assert.True(disabledNow);
        Assert.True(portal.PhaseDisabled);
        Assert.False(portal.BlocksShots);
        Assert.True(portal.Active); // disabled != removed
    }

    [Fact]
    public void TakeDamage_DoesNothing_WhenAlreadyDisabled()
    {
        var portal = new ProjectilePortal(Vector2.Zero, radius: 10, angle: 0f);
        portal.TakeDamage(1);
        portal.TakeDamage(1);
        portal.TakeDamage(1);
        Assert.False(portal.TakeDamage(1));
    }

    [Fact]
    public void Update_FiresIntoSink_OnceCooldownExpires()
    {
        var portal = new ProjectilePortal(Vector2.Zero, radius: 10, angle: 0f, fireInterval: 1.7f);
        var sink = new List<EnemyProjectile>();
        // Drain the cooldown in large steps rather than depending on Simulation's clock.
        for (int i = 0; i < 50 && sink.Count == 0; i++)
            portal.Update(sink, dt: 0.1f);
        Assert.NotEmpty(sink);
    }

    [Fact]
    public void ResetForPhase_RestoresFullHealthAndClearsDisable()
    {
        var portal = new ProjectilePortal(Vector2.Zero, radius: 10, angle: 0f);
        portal.TakeDamage(1);
        portal.TakeDamage(1);
        portal.TakeDamage(1);
        portal.ResetForPhase();
        Assert.False(portal.PhaseDisabled);
        Assert.Equal(portal.MaxHp, portal.Hp);
        Assert.True(portal.BlocksShots);
    }
}
