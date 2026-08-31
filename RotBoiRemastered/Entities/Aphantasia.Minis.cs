using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Update, firing, and damage handling for Aphantasia's two targetable
/// Minis (The Light and The Dark). Everything else lives in
/// <see cref="Aphantasia"/>; drawing lives in Aphantasia.Draw.cs.
/// </summary>
public sealed partial class Aphantasia
{

    private void UpdateMinis(EnemyUpdateContext context, double dt, bool hazardsOnly,
        bool allowFire = true)
    {
        if (Phase == 4)
            return;
        Light.Vulnerable = MiniCanTakeDamage(Light);
        Dark.Vulnerable = MiniCanTakeDamage(Dark);
        UpdateMini(Light, -1, context, dt, hazardsOnly, allowFire);
        UpdateMini(Dark, 1, context, dt, hazardsOnly, allowFire);
    }

    private void UpdateMini(AphantasiaMini mini, int side, EnemyUpdateContext context,
        double dt, bool hazardsOnly, bool allowFire)
    {
        if (!mini.Alive)
            return;
        float t = (float)_visualTime;
        Vector2 anchor;
        if (EncounterState == AphantasiaEncounterState.Survival
            && SurvivalKind == AphantasiaSurvivalKind.SecondEclipse
            && SequenceStage == 2)
        {
            anchor = ArenaCenter + new Vector2(
                side * MathF.Cos(t * 1.1f) * ArenaRadius * .42f,
                MathF.Sin(t * 1.34f + side) * ArenaRadius * .2f);
        }
        else if (EncounterState == AphantasiaEncounterState.Survival
            && SurvivalKind == AphantasiaSurvivalKind.GrandChoice)
        {
            anchor = ArenaCenter + new Vector2(
                side * ArenaRadius * .38f,
                MathF.Sin(t * .72f + side) * ArenaRadius * .2f);
        }
        else if (EncounterState == AphantasiaEncounterState.Combat
            && CurrentPattern.Movement == AphantasiaMovementMode.Pathed)
        {
            // Expand the route from the old .42-radius orbit to most of the
            // arena while preserving its approximate tangential speed.
            float previousOrbitRate = (TrueDark ? .16f : .26f) * .42f;
            float angularRate = previousOrbitRate / MiniPathedRadiusRatio;
            float angle = t * angularRate * side + (side < 0 ? MathF.PI : 0);
            anchor = ArenaCenter + new Vector2(
                MathF.Cos(angle) * ArenaRadius * MiniPathedRadiusRatio,
                MathF.Sin(angle * 2f) * ArenaRadius * .42f);
        }
        else if (mini.Aggressive || mini.Empowered || hazardsOnly)
        {
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            Vector2 desired = Vector2.Lerp(ArenaCenter, player, mini.Empowered ? .62f : .43f);
            desired += new Vector2(
                MathF.Cos(t * (mini.Empowered ? 1.7f : 1.05f) + side) * ArenaRadius * .12f,
                MathF.Sin(t * (mini.Empowered ? 1.3f : .82f) + side) * ArenaRadius * .12f);
            anchor = desired;
        }
        else
        {
            float speed = TrueDark ? .16f : .26f;
            float angle = t * speed * side + (side < 0 ? MathF.PI : 0);
            anchor = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .55f)
                * ArenaRadius * .42f;
        }
        Vector2 delta = anchor - mini.Position;
        float follow = 1f - MathF.Exp(-(mini.Empowered ? 2.1f : mini.Aggressive ? 1.45f : .72f) * (float)dt);
        mini.Velocity = delta * follow / Math.Max(.001f, (float)dt);
        mini.Position += delta * follow;

        mini.FireCooldown -= (float)dt;
        if (allowFire && mini.FireCooldown <= 0)
        {
            FireMini(mini, context.ProjectileSink,
                new Vector2(context.PlayerWorldX, context.PlayerWorldY));
            mini.FireCooldown = mini.Empowered
                ? .42f
                : mini.Aggressive ? (TrueLight ? .48f : .68f) : (TrueDark ? .58f : .94f);
        }
    }

    private void FireMini(AphantasiaMini mini, List<EnemyProjectile> sink, Vector2 player)
    {
        List<EnemyProjectile> staged = BeginVolley();
        float aim = AngleTo(mini.Position, player);
        int count = mini.Empowered ? (ReferenceEquals(mini, Light) ? 5 : 9)
            : mini.Aggressive ? 4 : TrueDark ? 7 : 5;
        float spread = mini.Empowered ? 1.1f : .72f;
        float speed = mini.Empowered && ReferenceEquals(mini, Light) ? 2.15f
            : mini.Empowered ? 1.05f : mini.Aggressive ? 1.65f : .88f;
        for (int index = 0; index < count; index++)
        {
            float fraction = count == 1 ? .5f : (float)index / (count - 1);
            string path = index % 2 == (ReferenceEquals(mini, Dark) ? 0 : 1)
                ? "sine" : "linear";
            AddShot(staged, mini.Position, aim - spread / 2f + fraction * spread,
                speed, mini.Empowered ? .34f : .26f, mini.Accent,
                $"mini_{(ReferenceEquals(mini, Light) ? "light" : "dark")}",
                path, path == "sine" ? Simulation.TileSize * .52f : 0f, 8f,
                shape: "diamond");
        }
        CommitVolley(sink);
    }

    private void ReviveMiniPair()
    {
        Vector2 origin = BossCenter;
        foreach (AphantasiaMini mini in new[] { Light, Dark })
        {
            mini.PermanentlyDestroyed = false;
            mini.Empowered = false;
            mini.MaxHp = BaseMiniHealth;
            mini.Hp = mini.MaxHp;
            mini.Position = origin;
            mini.Velocity = Vector2.Zero;
            mini.FireCooldown = Math.Max(mini.FireCooldown, .65f);
        }
    }

    private bool MiniCanTakeDamage(AphantasiaMini mini)
    {
        if (!mini.Alive || Dying || PhaseHandoffActive || EntranceRemaining > 0)
            return false;
        if (Phase <= 2)
            return EncounterState == AphantasiaEncounterState.Combat;
        if (Phase == 3 && SurvivalKind == AphantasiaSurvivalKind.GrandChoice)
            return Light.Alive && Dark.Alive;
        if (EncounterState == AphantasiaEncounterState.MiniExecution)
            return mini.Empowered;
        return false;
    }

    private HitResult DamageMini(AphantasiaMini mini, double amount)
    {
        if (!MiniCanTakeDamage(mini))
            return new HitResult(false, false, 0, true);
        int applied = Math.Min(mini.Hp, Math.Max(0, (int)Math.Round(amount)));
        mini.Hp -= applied;
        if (mini.Hp <= 0)
        {
            mini.Hp = 0;
            if (Phase == 3 && SurvivalKind == AphantasiaSurvivalKind.GrandChoice)
                mini.PermanentlyDestroyed = true;
            if (Phase <= 2 && !Light.Alive && !Dark.Alive)
            {
                _damageWindowOpened = true;
                _damageWindowRemaining = DamageWindowDuration;
            }
        }
        Light.Vulnerable = MiniCanTakeDamage(Light);
        Dark.Vulnerable = MiniCanTakeDamage(Dark);
        return new HitResult(true, false, applied, false);
    }

    public void DebugSetMiniState(AphantasiaMiniKind kind, int hp,
        AphantasiaMiniDisposition disposition)
    {
        AphantasiaMini mini = kind == AphantasiaMiniKind.Light ? Light : Dark;
        mini.Hp = Math.Clamp(hp, 0, mini.MaxHp);
        mini.PermanentlyDestroyed = disposition == AphantasiaMiniDisposition.Destroyed;
        mini.Empowered = disposition == AphantasiaMiniDisposition.Empowered;
        mini.Aggressive = disposition is AphantasiaMiniDisposition.Aggressive
            or AphantasiaMiniDisposition.Empowered;
        if (mini.PermanentlyDestroyed)
            mini.Hp = 0;
        mini.Vulnerable = MiniCanTakeDamage(mini);
    }
}
