using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bossTypes.py's BossCatalog/BOSS_CATALOG (no dedicated Python test file to mirror).</summary>
public class BossCatalogTests
{
    [Theory]
    [InlineData("beaudis", typeof(Beaudis))]
    [InlineData("dissonance", typeof(Dissonance))]
    [InlineData("ishe", typeof(Ishe))]
    [InlineData("chronos", typeof(Chronos))]
    [InlineData("bair", typeof(Bair))]
    [InlineData("sting", typeof(Sting))]
    [InlineData("kage", typeof(Kage))]
    [InlineData("rot", typeof(Rot))]
    public void CreateDefault_RegistersEveryPortedBoss(string key, Type expectedType)
    {
        var catalog = BossCatalog.CreateDefault();
        Assert.True(catalog.TryGet(key, out var definition));
        Assert.NotNull(definition);

        var boss = catalog.Spawn(key, Battleground.GenerateSound(), 400f, new Random(1));

        Assert.IsType(expectedType, boss);
    }

    [Fact]
    public void CreateDefault_DoesNotRegisterUnportedBosses()
    {
        var catalog = BossCatalog.CreateDefault();
        Assert.False(catalog.TryGet("hypno", out _));
        Assert.False(catalog.TryGet("malady", out _));
    }

    [Fact]
    public void Spawn_PlacesBossNearArenaCenter()
    {
        var catalog = BossCatalog.CreateDefault();
        var battleground = Battleground.GenerateSound();
        var boss = catalog.Spawn("ishe", battleground, 400f, new Random(1));

        float arenaX = battleground.Width * Battleground.TileSize / 2f;
        float arenaY = battleground.Height * Battleground.TileSize / 2f;
        Assert.True(Math.Abs(boss.WorldX - arenaX) < Battleground.TileSize * 4);
        Assert.True(Math.Abs(boss.WorldY - arenaY) < Battleground.TileSize * 4);
    }

    [Fact]
    public void Register_EmptyCatalog_OnlyKnowsExplicitlyRegisteredBosses()
    {
        var catalog = new BossCatalog();
        Assert.False(catalog.TryGet("beaudis", out _));
        catalog.Register(new BossDefinition("beaudis", "Beaudis", (x, y, _, awareness, rng) => new Beaudis(x, y, awareness, rng)));
        Assert.True(catalog.TryGet("beaudis", out _));
    }
}
