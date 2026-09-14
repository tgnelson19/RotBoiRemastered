using RotBoiRemastered.Core;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Leader of a veteran holdout in The Ego: a much tougher sense guardian that
/// always drops that sense's Veteran Dungeon portal on death (see
/// <see cref="Systems.EgoRun.RollDungeonDrop"/>). Stats are scaled from the
/// floor-10 guardian; unique per-sense veteran attacks are still a TODO in
/// <see cref="PathGuardianBoss.FireVeteranSignature"/>.
/// </summary>
public sealed class VeteranGuardianBoss : PathGuardianBoss
{
    public const double HealthScale = 2.2;
    public const double DamageScale = 1.4;

    public VeteranGuardianBoss(float worldX, float worldY, string senseKey, float awarenessRange, Random? rng = null)
        : base(worldX, worldY, senseKey, floorNumber: 10, awarenessRange, rng,
            arenaRadius: Simulation.TileSize * 7f)
    {
        IsVeteran = true;
        ContentPath = senseKey;
        CombatRole = "elite";
        ThreatCost = 45;
        MaxHp = (int)Math.Round(MaxHp * HealthScale);
        Hp = MaxHp;
        Damage = (int)Math.Round(Damage * DamageScale);
        ExpValue *= 2.5;
        AttackCooldown = Simulation.FrameRate * 1.15f;
        AttackCooldownMax = AttackCooldown;
    }
}
