using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Aphantasia's projectile patterns: everything that stages, aims, and
/// commits shots to the enemy projectile sink (ring/curtain/ribbon
/// helpers, the phase-specific Fire*Pattern methods, and the ambient
/// perimeter/arena-half pressure). Update/lifecycle state lives in
/// <see cref="Aphantasia"/>; drawing lives in Aphantasia.Draw.cs.
/// </summary>
public sealed partial class Aphantasia
{

    private void FireCurrentPattern(EnemyUpdateContext context, bool phraseAccent = false)
    {
        List<EnemyProjectile> staged = BeginVolley();
        Vector2 center = BossCenter;
        Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
        if (Phase <= 2)
            FireEssencePattern(context, staged);
        else if (Phase == 3)
            FireTesseractPattern(context, staged);
        else
            FireVoidPattern(context, staged);

        _regularVolleyCount++;
        FireBaselineBossAttack(staged, center, player, _regularVolleyCount);
        AddBossSpecialAttack(staged, center, player, _regularVolleyCount);
        if (phraseAccent)
            FirePhraseAccent(staged, center, player);
        CommitVolley(context.ProjectileSink);
    }

    private void FireBaselineBossAttack(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player, int volley)
    {
        float aim = AngleTo(origin, player);
        switch (volley % 3)
        {
            case 0:
                AddShot(sink, origin, aim, 1.55f, .24f, PhaseAccent,
                    "baseline_straight", "linear", 0f, 8f);
                break;
            case 1:
                for (int side = -1; side <= 1; side += 2)
                    AddShot(sink, origin, aim + side * .08f, 1.28f, .24f,
                        PhaseAccent, "baseline_sine", "sine",
                        side * Simulation.TileSize * .46f, 8f,
                        frequency: .031f);
                break;
            default:
                FireFan(sink, origin, aim, 5, .72f, 1.12f,
                    PhaseAccent, "baseline_shotgun");
                break;
        }
    }

    private void FirePhraseAccent(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player)
    {
        float aim = AngleTo(origin, player);
        switch (Phase)
        {
            case 1:
                for (int side = -1; side <= 1; side += 2)
                {
                    AddShot(sink, origin, aim + side * .16f, 1.72f, .24f,
                        side < 0 ? Light.Accent : Dark.Accent,
                        "order_accent_straight", "linear", 0f, 8f,
                        shape: "needle");
                    AddShot(sink, origin, aim + side * .29f, .82f, .31f,
                        side < 0 ? Light.Accent : Dark.Accent,
                        "order_accent_wave", "sine",
                        side * Simulation.TileSize * .72f, 9f,
                        frequency: .014f, shape: "crescent");
                }
                break;
            case 2:
                for (int index = 0; index < 5; index++)
                {
                    if (index == 2)
                        continue;
                    float offset = (index - 2) * .17f + (index % 2 == 0 ? .05f : -.04f);
                    AddShot(sink, origin, aim + offset,
                        index % 2 == 0 ? 1.68f : .74f,
                        index % 2 == 0 ? .22f : .34f,
                        index % 2 == 0 ? Light.Accent : Dark.Accent,
                        "fracture_accent", index % 2 == 0 ? "linear" : "sine",
                        index % 2 == 0 ? 0f : Simulation.TileSize * .38f,
                        9f, frequency: .052f,
                        shape: index % 2 == 0 ? "needle" : "crescent");
                }
                break;
            case 3:
                for (int axis = 0; axis < 4; axis++)
                {
                    float direction = axis * MathF.PI / 2f
                        + (_regularVolleyCount % 2) * MathF.PI / 4f;
                    AddShot(sink, origin, direction, 1.46f, .25f,
                        axis % 2 == 0 ? Light.Accent : Dark.Accent,
                        "refraction_accent", "linear", 0f, 8f,
                        shape: "needle");
                }
                break;
            default:
                for (int side = -2; side <= 2; side++)
                    AddShot(sink, origin, aim + side * .11f,
                        side == 0 ? .64f : 1.88f,
                        side == 0 ? .42f : .2f,
                        Rainbow(side / 5f + (float)_visualTime * .05f),
                        "void_accent", "linear", 0f, 8f,
                        shape: side == 0 ? "orbit_core" : "needle",
                        speedDecay: side == 0 ? .22f : 0f,
                        preserveAuthoredLifetime: side == 0);
                break;
        }
    }

    private void AddBossSpecialAttack(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player, int volley)
    {
        AphantasiaSpecialAttack specials = ActiveSpecialAttacks();
        if (specials.HasFlag(AphantasiaSpecialAttack.Laser) && volley % 3 == 0)
            FireAphantasiaLaser(sink, origin, player);
        if (specials.HasFlag(AphantasiaSpecialAttack.Bomb) && volley % 4 == 0)
            FireAphantasiaBomb(sink, origin, player);
    }

    private AphantasiaSpecialAttack ActiveSpecialAttacks()
    {
        if (EncounterState is AphantasiaEncounterState.Survival
            or AphantasiaEncounterState.Finale)
        {
            return (SequenceStage % 4) switch
            {
                0 => AphantasiaSpecialAttack.DoubleHelix,
                1 => AphantasiaSpecialAttack.Laser,
                2 => AphantasiaSpecialAttack.Bomb,
                _ => AphantasiaSpecialAttack.None,
            };
        }
        return EncounterState == AphantasiaEncounterState.Combat
            ? CurrentPattern.SpecialAttack
            : AphantasiaSpecialAttack.None;
    }

    private void FireDoubleHelixPair(List<EnemyProjectile> sink, Vector2 origin,
        float direction, string source)
    {
        float amplitude = Simulation.TileSize * .72f;
        float size = Simulation.TileSize * .27f;
        float range = DistanceToArenaEdge(origin, direction) + size;
        const float helixSpeed = 2.05f;
        float lifetime = range
            / (.52f * (float)Simulation.ReferenceFps * helixSpeed) + 1f;
        foreach ((float signedAmplitude, Color color, string strand) in new[]
        {
            (amplitude, Light.Accent, "light"),
            (-amplitude, Dark.Accent, "dark"),
        })
        {
            sink.Add(new EnemyProjectile(
                origin.X - size / 2f, origin.Y - size / 2f,
                direction, helixSpeed, Damage * .56f, size,
                travelRange: range, color: color, shape: "crescent",
                path: "sine", amplitude: signedAmplitude, frequency: .027f,
                lifetime: lifetime, owner: $"aphantasia_double_helix_{source}_{strand}",
                ignoreWalls: true));
        }
    }

    private void UpdateHelixStream(EnemyUpdateContext context, double dt)
    {
        if (CombatFiringPaused)
            return;
        if (!ActiveSpecialAttacks().HasFlag(AphantasiaSpecialAttack.DoubleHelix))
            return;
        _helixFireRemaining -= dt;
        if (_helixFireRemaining > 0)
            return;
        _helixFireRemaining = HelixFireCadence;
        AphantasiaMini? sourceMini = Phase switch
        {
            1 when Light.Alive => Light,
            2 when Dark.Alive => Dark,
            3 when Light.Empowered && Light.Alive => Light,
            3 when Dark.Empowered && Dark.Alive => Dark,
            3 when Light.Alive => Light,
            _ => null,
        };
        if (sourceMini is null && Phase < 4)
            sourceMini = Light.Alive ? Light : Dark.Alive ? Dark : null;
        Vector2 origin = sourceMini?.Position ?? BossCenter;
        string source = sourceMini is null ? "boss"
            : ReferenceEquals(sourceMini, Light) ? "light" : "dark";
        Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
        List<EnemyProjectile> staged = BeginVolley();
        FireDoubleHelixPair(staged, origin, AngleTo(origin, player), source);
        CommitVolley(context.ProjectileSink);
    }

    private void FireAphantasiaLaser(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player)
    {
        float direction = AngleTo(origin, player);
        for (int side = -1; side <= 1; side += 2)
        {
            var laser = new EnemyProjectile(
                origin.X, origin.Y, direction + side * .18f, 0f,
                Damage * .78f, Simulation.TileSize * .28f,
                travelRange: ArenaRadius * 1.95f,
                color: side < 0 ? Light.Accent : Dark.Accent,
                // Lifetime grows by the same amount as the telegraph below,
                // so the extra warning is pure warning -- the beam still
                // burns for its original ~1.6s once it actually fires.
                shape: "diamond", path: "laser", lifetime: 3.1f,
                angularSpeed: side * .09f,
                owner: $"aphantasia_laser_{(side < 0 ? "light" : "dark")}")
            {
                TelegraphDuration = 1.5f,
            };
            sink.Add(laser);
        }
    }

    private void FireAphantasiaBomb(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player)
    {
        float offsetAngle = (float)_visualTime * .83f;
        Vector2 target = player + new Vector2(MathF.Cos(offsetAngle), MathF.Sin(offsetAngle))
            * Simulation.TileSize * 1.25f;
        float size = Simulation.TileSize * .62f;
        sink.Add(new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f,
            AngleTo(origin, target), .7f, Damage * .82f, size,
            travelRange: ArenaRadius * 2f, color: Rainbow((float)_visualTime * .1f),
            shape: "orbit_core", path: "bomb", lifetime: 4f,
            owner: "aphantasia_bomb", ignoreWalls: true, target: target)
        {
            FuseDuration = 1.7f,
            BlastRadius = Simulation.TileSize * 1.8f,
            BurstCount = 10,
            BurstDamage = Damage * .68f,
            BurstRangeTiles = 18f,
            ThreatReservationCost = 10,
            LargeShot3D = Phase >= 3,
        });
    }

    private void FireArenaLaserGrid(List<EnemyProjectile> sink, bool diagonal)
    {
        float[] laneOffsets = [-.54f, -.18f, .18f, .54f];
        float[] directions = diagonal
            ? [MathF.PI / 4f, 3f * MathF.PI / 4f]
            : [0f, MathF.PI / 2f];
        string orientation = diagonal ? "anticardinal" : "cardinal";
        foreach (float direction in directions)
        {
            Vector2 heading = new(MathF.Cos(direction), MathF.Sin(direction));
            Vector2 perpendicular = new(-heading.Y, heading.X);
            foreach (float laneRatio in laneOffsets)
            {
                float offset = laneRatio * ArenaRadius;
                float halfChord = MathF.Sqrt(Math.Max(0f,
                    ArenaRadius * ArenaRadius - offset * offset));
                Vector2 origin = ArenaCenter + perpendicular * offset
                    - heading * halfChord * .96f;
                var laser = new EnemyProjectile(
                    origin.X, origin.Y, direction, 0f, Damage * .7f,
                    Simulation.TileSize * .25f,
                    travelRange: halfChord * 1.92f,
                    color: Rainbow(laneRatio + (float)_visualTime * .05f),
                    // Lifetime grows by the same amount as the telegraph
                    // below, so the extra warning is pure warning -- the beam
                    // still burns for its original ~1.65s once it fires.
                    shape: "diamond", path: "laser", lifetime: 3.15f,
                    owner: $"aphantasia_edge_grid_{orientation}")
                {
                    TelegraphDuration = 1.5f,
                    OriginTelegraphDuration = .45f,
                };
                sink.Add(laser);
            }
        }
    }

    private void FireEssencePattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * (Phase == 1 ? .34f : .21f);
        int pattern = _patternIndex;
        bool darkDensity = TrueDark;
        bool lightTempo = TrueLight;
        if (Phase == 1)
        {
            switch (pattern)
            {
                case 0:
                    FireOrderedRing(sink, center, lightTempo ? 10 : darkDensity ? 18 : 14,
                        spin, lightTempo ? 1.72f : darkDensity ? .82f : 1.16f,
                        .27f, "ordered_bloom_outer", sineEvery: 4);
                    FireOrderedRing(sink, center, lightTempo ? 6 : 8,
                        -spin * .72f, lightTempo ? 1.18f : .72f,
                        .31f, "ordered_bloom_inner", sineEvery: 4);
                    _attackRemaining = lightTempo ? .56 : darkDensity ? .82 : .68;
                    break;
                case 1:
                    FireOrderedCurtain(sink,
                        vertical: (_regularVolleyCount & 1) == 0,
                        reverse: (_regularVolleyCount & 2) != 0,
                        lanes: darkDensity ? 13 : 10,
                        speed: lightTempo ? 1.7f : darkDensity ? .78f : 1.08f,
                        owner: "horizon_ordered");
                    FireOrderedRing(sink, center, 8, -spin, 1.02f, .24f,
                        "horizon_center", sineEvery: 0);
                    _attackRemaining = lightTempo ? .62 : .78;
                    break;
                default:
                    Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
                    float aim = AngleTo(center, player);
                    int pairs = lightTempo ? 2 : darkDensity ? 4 : 3;
                    for (int pair = 0; pair < pairs; pair++)
                    {
                        float offset = (pair - (pairs - 1) / 2f) * .22f;
                        Color color = pair % 2 == 0 ? Light.Accent : Dark.Accent;
                        AddShot(sink, center, aim + offset - .055f, 1.88f, .22f,
                            color, "tidal_straight", "linear", 0f, 8f,
                            shape: "needle");
                        AddShot(sink, center, aim + offset + .055f, .78f, .31f,
                            color, "tidal_wave", "sine",
                            (pair % 2 == 0 ? 1f : -1f) * Simulation.TileSize * .74f,
                            10f, frequency: .014f, shape: "crescent");
                    }
                    _attackRemaining = lightTempo ? .48 : darkDensity ? .72 : .6;
                    break;
            }
            return;
        }

        if (pattern == 0)
        {
            int count = lightTempo ? 12 : darkDensity ? 22 : 18;
            FireBrokenRing(sink, center, count, spin,
                lightTempo ? 1.72f : darkDensity ? .7f : 1.04f,
                darkDensity ? .29f : .25f, "broken_bloom");
            FireBrokenRing(sink, center, Math.Max(8, count / 2), -spin * 1.34f,
                lightTempo ? .84f : 1.38f, .22f, "broken_bloom_echo");
            _attackRemaining = lightTempo ? .52 : darkDensity ? .76 : .62;
        }
        else if (pattern == 1)
        {
            FireFracturedCurtain(sink,
                vertical: (_regularVolleyCount & 1) == 0,
                reverse: (_regularVolleyCount & 2) != 0,
                lanes: darkDensity ? 15 : 11,
                owner: "erratic_eight");
            FireBrokenRing(sink, center, lightTempo ? 8 : 11, -spin,
                lightTempo ? 1.55f : .82f, .24f,
                "erratic_eight_cross");
            _attackRemaining = lightTempo ? .5 : .72;
        }
        else
        {
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            float aim = AngleTo(center, player);
            int pellets = (_regularVolleyCount & 1) == 0 ? 5 : 9;
            float spread = pellets == 5 ? .68f : 1.24f;
            for (int index = 0; index < pellets; index++)
            {
                float fraction = pellets == 1 ? .5f : index / (float)(pellets - 1);
                bool curl = index % 2 != 0;
                AddShot(sink, center, aim - spread / 2f + fraction * spread
                        + (index % 3 - 1) * .025f,
                    curl ? .76f : 1.62f,
                    curl ? .34f : .22f,
                    index % 2 == 0 ? Light.Accent : Dark.Accent,
                    "undertow", curl ? "sine" : "linear",
                    curl ? (index % 4 < 2 ? 1f : -1f) * Simulation.TileSize * .42f : 0f,
                    9f, frequency: curl ? .052f : .035f,
                    shape: "diamond");
            }
            _attackRemaining = pellets == 5 ? .48 : .72;
        }
    }

    private void FireTesseractPattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * .52f;
        switch (_patternIndex)
        {
            case 0:
                FireOrderedRing(sink, center, 18, spin, .94f, .27f,
                    "prism_outer", sineEvery: 3);
                FireOrderedRing(sink, center, 10, -spin * 1.4f, 1.52f, .32f,
                    "prism_inner", sineEvery: 5);
                _attackRemaining = .62;
                break;
            case 1:
                FireEdgeCurtain(sink, true, ((int)_stateElapsed & 1) == 0, 11, .9f, "lattice_v");
                FireEdgeCurtain(sink, false, ((int)_stateElapsed & 2) == 0, 11, .9f, "lattice_h");
                _attackRemaining = .88;
                break;
            case 2:
                FireRing(sink, center, 12, spin, 1.25f, .25f, "eight_spoke", false);
                FireEdgeCurtain(sink, ((int)_stateElapsed & 1) == 0, false, 7, 1.05f, "eight_fold");
                FireRefractorPair(sink, center,
                    new Vector2(context.PlayerWorldX, context.PlayerWorldY));
                _attackRemaining = .66;
                break;
            case 3:
                float foldRotation = (_regularVolleyCount & 1) == 0
                    ? 0f
                    : MathF.PI / 4f;
                for (int side = 0; side < 4; side++)
                {
                    float angle = side * MathF.PI / 2f + foldRotation;
                    Vector2 origin = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * .84f;
                    FireFan(sink, origin, angle + MathF.PI, 7, .68f, .88f,
                        side % 2 == 0 ? Light.Accent : Dark.Accent, "folding_inward");
                }
                _attackRemaining = .82;
                break;
            case 4:
                FireMirroredRibbon(sink, center,
                    new Vector2(context.PlayerWorldX, context.PlayerWorldY));
                _attackRemaining = .48;
                break;
            default:
                FireRing(sink, center, 14, spin * 1.7f, 1.3f, .22f, "satellite_spiral", true);
                FireFan(sink, Light.Position, AngleTo(Light.Position, ArenaCenter), 5, .5f, 1.5f,
                    Light.Accent, "satellite_light");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, ArenaCenter), 7, .9f, .82f,
                    Dark.Accent, "satellite_dark");
                _attackRemaining = .57;
                break;
        }
    }

    private void FireVoidPattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
        => FireVoidStage(context, sink, _patternIndex);

    private void FireVoidStage(EnemyUpdateContext context, List<EnemyProjectile> sink,
        int stage)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * .38f;
        switch (stage)
        {
            case 0:
                // 5 -> 6 seeds: closes the widest gap in the constellation
                // fold, which used to leave a walkable wedge between seeds.
                for (int index = 0; index < 6; index++)
                    FirePortalSeed(sink, center, spin + index * MathF.Tau / 6f, .48f, "constellation");
                _attackRemaining = 1.4;
                break;
            case 1:
                FireOrderedRing(sink, center, 12, spin, 1.76f, .2f,
                    "void_clock_needles", sineEvery: 0);
                // Fired every other visit only -- unconditionally every time
                // used to pile decelerating anchors up faster than their
                // lifetime could clear them, forming a static cluster right
                // on top of the boss.
                if ((_regularVolleyCount & 1) == 0)
                    FireVoidAnchor(sink, center, -spin);
                else
                    FirePortalSeed(sink, center, spin + MathF.PI, .42f, "clock_hand");
                _attackRemaining = .9;
                break;
            case 2:
                FireEdgePortals(sink, vertical: ((int)_stateElapsed & 1) == 0, "pane_procession");
                // A long rest after the flood wall -- lets the room clear
                // out before the next attack, rather than piling straight
                // into another wave.
                _attackRemaining = 4.0;
                break;
            case 3:
                FireEdgePortals(sink, true, "portal_lattice_v");
                FireEdgePortals(sink, false, "portal_lattice_h");
                _attackRemaining = 4.5;
                break;
            case 4:
                FirePortalSeed(sink, center,
                    AngleTo(center, new Vector2(context.PlayerWorldX, context.PlayerWorldY)),
                    .62f, "portal_wake");
                FireAimedRibbon(sink, center, new Vector2(context.PlayerWorldX, context.PlayerWorldY),
                    6, .86f, "void_pursuit");
                _attackRemaining = .62;
                break;
            default:
                // 3 -> 4 seeds and 9 -> 11 ring shots: the collapsing-tesseract
                // finale stage was the easiest one to find a gap in.
                for (int index = 0; index < 4; index++)
                    FirePortalSeed(sink, center, spin + index * MathF.Tau / 4f, .7f, "tesseract_hunt");
                FireRing(sink, center, 11, -spin * 2f, 1.42f, .24f, "collapse_ring", true);
                _attackRemaining = .7;
                break;
        }
    }

    private void FireSurvivalMovement(EnemyUpdateContext context)
    {
        double elapsed = SurvivalDuration - SurvivalRemaining;
        List<EnemyProjectile> sink = BeginVolley();
        Vector2 center = ArenaCenter;
        Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
        switch (SurvivalKind)
        {
            case AphantasiaSurvivalKind.FirstEclipse:
                FireFirstEclipseStage(sink, elapsed);
                break;
            case AphantasiaSurvivalKind.SecondEclipse:
                FireSecondEclipseStage(sink, elapsed, player);
                break;
            case AphantasiaSurvivalKind.GrandChoice:
                FireGrandChoiceStage(sink, elapsed, player);
                break;
            case AphantasiaSurvivalKind.VoidEclipse:
                // Reuses the void finale's own attack pool -- same "typical
                // fun stuff" repertoire, just as a mid-phase-4 checkpoint
                // rather than the closing spectacle.
                FireVoidStage(context, sink, SequenceStage);
                break;
        }
        _regularVolleyCount++;
        AddBossSpecialAttack(sink, BossCenter, player, _regularVolleyCount);
        AddSurvivalLaserGrid(sink);
        CommitVolley(context.ProjectileSink);
    }

    private void FireFinaleMovement(EnemyUpdateContext context)
    {
        double elapsed = SurvivalDuration - SurvivalRemaining;
        List<EnemyProjectile> sink = BeginVolley();
        if (SurvivalKind == AphantasiaSurvivalKind.VoidFinale)
        {
            FireVoidStage(context, sink, SequenceStage);
        }
        else
        {
            FireEssenceFinaleStage(sink, elapsed);
        }
        _regularVolleyCount++;
        AddBossSpecialAttack(sink, BossCenter,
            new Vector2(context.PlayerWorldX, context.PlayerWorldY),
            _regularVolleyCount);
        AddSurvivalLaserGrid(sink);
        CommitVolley(context.ProjectileSink);
    }

    private void FireFirstEclipseStage(List<EnemyProjectile> sink, double elapsed)
    {
        switch (SequenceStage)
        {
            case 0:
                FireOrderedRing(sink, ArenaCenter, 16, (float)elapsed * .23f,
                    .92f, .25f, "first_eclipse_ordered", sineEvery: 4);
                _attackRemaining = .64;
                break;
            case 1:
                FireOrderedCurtain(sink, true, ((int)elapsed & 1) == 0,
                    12, .82f, "first_eclipse_vertical");
                _attackRemaining = .74;
                break;
            case 2:
                FireFan(sink, Light.Position, AngleTo(Light.Position, ArenaCenter),
                    7, .72f, 1.24f, Light.Accent, "first_eclipse_light_fan");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, ArenaCenter),
                    7, .72f, 1.02f, Dark.Accent, "first_eclipse_dark_fan");
                _attackRemaining = .68;
                break;
            default:
                FireOrderedCurtain(sink, true, ((int)elapsed & 1) == 0,
                    10, .9f, "first_eclipse_cross_v");
                FireOrderedCurtain(sink, false, ((int)elapsed & 1) != 0,
                    10, .9f, "first_eclipse_cross_h");
                _attackRemaining = .82;
                break;
        }
    }

    private void FireSecondEclipseStage(List<EnemyProjectile> sink, double elapsed,
        Vector2 player)
    {
        switch (SequenceStage)
        {
            case 0:
                FireBrokenRing(sink, ArenaCenter, 22, (float)elapsed * .31f,
                    .84f, .28f, "second_eclipse_broken");
                _attackRemaining = .58;
                break;
            case 1:
                FireFracturedCurtain(sink, ((int)elapsed & 1) == 0,
                    ((int)(elapsed * 1.5) & 1) == 0, 13,
                    "second_eclipse_staggered");
                FireBrokenRing(sink, ArenaCenter, 9, -(float)elapsed * .43f,
                    1.18f, .22f, "second_eclipse_offset");
                _attackRemaining = .7;
                break;
            case 2:
                FireFan(sink, Light.Position, AngleTo(Light.Position, player),
                    7, .82f, 1.52f, Light.Accent, "second_eclipse_light_swap");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, player),
                    9, 1.12f, .82f, Dark.Accent, "second_eclipse_dark_swap");
                _attackRemaining = .6;
                break;
            default:
                Vector2 left = ArenaCenter - new Vector2(ArenaRadius * .86f, ArenaRadius * .42f);
                Vector2 right = ArenaCenter + new Vector2(ArenaRadius * .86f, ArenaRadius * .42f);
                FireAimedRibbon(sink, left, player, 8, .92f, "second_eclipse_braid_left");
                FireAimedRibbon(sink, right, player, 7, .76f, "second_eclipse_braid_right");
                _attackRemaining = .62;
                break;
        }
    }

    private void FireGrandChoiceStage(List<EnemyProjectile> sink, double elapsed,
        Vector2 player)
    {
        switch (SequenceStage)
        {
            case 0:
                FireFan(sink, Light.Position, AngleTo(Light.Position, player),
                    7, .58f, 1.82f, Light.Accent, "grand_choice_radiant");
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    7, 1.42f, "grand_choice_light_lane");
                _attackRemaining = .52;
                break;
            case 1:
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, player),
                    13, 1.36f, .72f, Dark.Accent, "grand_choice_dark_curl");
                _attackRemaining = .58;
                break;
            case 2:
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    8, 1.48f, "grand_choice_divide_light");
                FireEdgeCurtain(sink, false, ((int)elapsed & 1) != 0,
                    13, .7f, "grand_choice_divide_dark");
                _attackRemaining = .76;
                break;
            default:
                FireRing(sink, ArenaCenter, 18, (float)elapsed * .37f,
                    .9f, .26f, "grand_choice_convergence", true);
                FireFan(sink, Light.Position, AngleTo(Light.Position, ArenaCenter),
                    5, .52f, 1.6f, Light.Accent, "grand_choice_light_close");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, ArenaCenter),
                    9, 1.08f, .78f, Dark.Accent, "grand_choice_dark_close");
                _attackRemaining = .68;
                break;
        }
    }

    private void FireEssenceFinaleStage(List<EnemyProjectile> sink, double elapsed)
    {
        switch (SequenceStage)
        {
            case 0:
                FireRing(sink, ArenaCenter, 18, (float)elapsed * .4f,
                    1f, .26f, "essence_finale_prism_outer", true);
                FireRing(sink, ArenaCenter, 11, -(float)elapsed * .56f,
                    1.48f, .32f, "essence_finale_prism_inner", true);
                _attackRemaining = .62;
                break;
            case 1:
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    13, .94f, "essence_finale_fold_v");
                FireRing(sink, ArenaCenter, 9, (float)elapsed * .31f,
                    1.24f, .23f, "essence_finale_spoke_v", true);
                _attackRemaining = .68;
                break;
            case 2:
                FireEdgeCurtain(sink, false, ((int)elapsed & 1) != 0,
                    13, .94f, "essence_finale_fold_h");
                FireRing(sink, ArenaCenter, 9, -(float)elapsed * .34f,
                    1.24f, .23f, "essence_finale_spoke_h", true);
                _attackRemaining = .68;
                break;
            case 3:
                FireDancingBullets(sink, slow: false);
                FireEdgeCurtain(sink, ((int)elapsed & 1) == 0,
                    ((int)elapsed & 2) == 0, 8, .86f, "essence_finale_lattice");
                _attackRemaining = .58;
                break;
            default:
                float spin = (float)elapsed * .48f;
                for (int side = 0; side < 4; side++)
                {
                    float angle = spin + side * MathF.PI / 2f;
                    Vector2 origin = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                        * ArenaRadius * .84f;
                    FireFan(sink, origin, angle + MathF.PI, 7, .66f, .92f,
                        side % 2 == 0 ? Light.Accent : Dark.Accent,
                        "essence_finale_convergence");
                }
                FireRing(sink, ArenaCenter, 12, -spin, 1.34f, .22f,
                    "essence_finale_core", true);
                _attackRemaining = .82;
                break;
        }
    }

    private void FireDancingBullets(List<EnemyProjectile> sink, bool slow)
    {
        float spin = (float)_visualTime * (slow ? .35f : .72f);
        int count = slow ? 16 : 22;
        for (int index = 0; index < count; index++)
        {
            float angle = spin + index * MathF.Tau / count;
            string path = index % 3 == 0 ? "sine" : "linear";
            AddShot(sink, ArenaCenter, angle, slow ? .78f : 1.18f,
                .2f + index % 4 * .055f, Rainbow(index / (float)count + spin * .05f),
                "dancing_bullets", path, Simulation.TileSize * .46f, 11f);
        }
    }

    private void FireTransformationBurst(List<EnemyProjectile> sink)
    {
        float spin = (float)_visualTime * 1.8f;
        for (int index = 0; index < 8; index++)
            AddShot(sink, ArenaCenter, spin + index * MathF.Tau / 8f,
                .62f + index % 3 * .16f, .18f + index % 2 * .1f,
                Rainbow(index / 8f + spin * .04f), "transformation", "sine",
                Simulation.TileSize * .35f, 3.4f, deliberatelyShortRange: true);
    }

    /// <summary>
    /// How far through this phase's opening ramp the current subphase sits,
    /// 0 at the phase's first subphase and 1 from its third subphase on --
    /// shared by <see cref="PerimeterPressureRampCount"/> and
    /// <see cref="HalfPressureRampCadenceMultiplier"/> so Phase 3 and Phase 4
    /// each ease their always-on ambient pressure in across two subphases
    /// instead of switching straight to full intensity at the phase
    /// transition.
    /// </summary>
    private float PhaseOpeningRampProgress
        => Math.Clamp(_subphasesSincePhaseStart / 2f, 0f, 1f);

    /// <summary>
    /// Perimeter ring projectile count, ramped from 0 at the start of Phase
    /// 3 up to <see cref="PerimeterPressureCount"/> / 2 by Phase 3's third
    /// subphase, then continuing that ramp up to the full
    /// <see cref="PerimeterPressureCount"/> by Phase 4's third subphase.
    /// </summary>
    private int PerimeterPressureRampCount()
    {
        if (Phase <= 2)
            return 0;
        float lower = Phase == 3 ? 0f : PerimeterPressureCount / 2f;
        float upper = Phase == 3 ? PerimeterPressureCount / 2f : PerimeterPressureCount;
        return (int)MathF.Round(MathHelper.Lerp(lower, upper, PhaseOpeningRampProgress));
    }

    /// <summary>
    /// Arena-half ambient volley cadence multiplier, ramped from a gentle
    /// 3x cooldown at the start of Phase 3 down to Phase 3's steady-state 2x,
    /// then continuing that ramp down to Phase 4's full-speed 1x by its
    /// third subphase.
    /// </summary>
    private double HalfPressureRampCadenceMultiplier()
    {
        float lower = Phase == 3 ? 3f : 2f;
        float upper = Phase == 3 ? 2f : 1f;
        return MathHelper.Lerp(lower, upper, PhaseOpeningRampProgress);
    }

    private void UpdatePerimeterPressure(EnemyUpdateContext context, double dt)
    {
        if (Phase <= 2 || CombatFiringPaused)
            return;
        _perimeterPressureRemaining -= dt;
        if (_perimeterPressureRemaining > 0)
            return;
        _perimeterPressureRemaining = PerimeterPressureCadence;
        int projectileCount = PerimeterPressureRampCount();
        if (projectileCount <= 0)
            return;
        List<EnemyProjectile> staged = BeginVolley();
        float rotation = (float)_visualTime * .17f;
        for (int index = 0; index < projectileCount; index++)
        {
            float angle = rotation + index * MathF.Tau / projectileCount;
            Vector2 origin = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * ArenaRadius * .93f;
            float opposite = angle + MathF.PI + MathF.Sin(rotation * 1.7f + index) * .24f;
            Vector2 target = ArenaCenter + new Vector2(MathF.Cos(opposite), MathF.Sin(opposite))
                * ArenaRadius * .92f;
            AddShot(staged, origin, AngleTo(origin, target),
                .72f + index % 3 * .08f, .15f + index % 2 * .035f,
                index % 2 == 0 ? Light.Accent * .82f : Dark.Accent * .9f,
                "perimeter_drift", index % 3 == 0 ? "sine" : "linear",
                Simulation.TileSize * .22f, 12f);
        }
        CommitVolley(context.ProjectileSink);
    }

    /// <summary>
    /// Sprinkles an occasional array of slow, one-directional persistent
    /// lasers into ordinary Phase 3+ subphases -- a calmer, longer-lived
    /// cousin of the Void Finale's five-armed sweep. Arm count (see
    /// <see cref="PersistentLaserArmCounts"/>), starting orientation, and
    /// spin direction are all re-rolled at every spawn, so the shape varies
    /// spawn to spawn (a lone slowly-turning beam, two opposite each other,
    /// three at 120 degrees, and so on) instead of repeating one fixed
    /// pattern. Once fired the array needs no further attention from the
    /// boss -- <see cref="EnemyProjectile"/>'s own per-frame
    /// <c>AngularSpeed</c> turn keeps it spinning, and its authored
    /// <see cref="PersistentLaserLifetime"/> retires it on its own.
    /// </summary>
    private void UpdatePersistentRotatingLaser(EnemyUpdateContext context, double dt)
    {
        if (Phase < 3 || EncounterState != AphantasiaEncounterState.Combat
            || CombatFiringPaused)
        {
            return;
        }
        _persistentLaserRemaining -= dt;
        if (_persistentLaserRemaining > 0)
            return;
        _persistentLaserRemaining = PersistentLaserCadence;

        int armCount = PersistentLaserArmCounts[_rng.Next(PersistentLaserArmCounts.Count)];
        float baseDirection = (float)(_rng.NextDouble() * MathF.Tau);
        float angularSpeed = (_rng.Next(2) == 0 ? 1f : -1f) * PersistentLaserAngularSpeed;

        // Every so often the array's beams travel as a sine wave instead of
        // a straight line -- amplitude, how tightly it curls along the
        // beam, and how fast that shape slides outward (or inward) are all
        // re-rolled per spawn, same as the arm count and spin direction, so
        // the wavy variant itself keeps varying rather than repeating one
        // fixed shape.
        bool wavy = _rng.NextDouble() < .45;
        float waveAmplitude = wavy
            ? Simulation.TileSize * (.6f + (float)_rng.NextDouble() * 1f)
            : 0f;
        float waveFrequency = .008f + (float)_rng.NextDouble() * .016f;
        float waveSpeed = wavy
            ? (_rng.Next(2) == 0 ? 1f : -1f) * (.9f + (float)_rng.NextDouble() * 1.5f)
            : 0f;

        List<EnemyProjectile> staged = BeginVolley();
        Vector2 origin = ArenaCenter;
        for (int index = 0; index < armCount; index++)
        {
            float direction = baseDirection + index * MathF.Tau / armCount;
            var laser = new EnemyProjectile(
                origin.X, origin.Y, direction, 0f,
                Damage * .6f, Simulation.TileSize * PersistentLaserSizeTiles,
                travelRange: ArenaRadius * 2.1f,
                color: Rainbow(index / (float)armCount + (float)_visualTime * .03f),
                shape: "diamond", path: "laser",
                amplitude: waveAmplitude, frequency: waveFrequency,
                lifetime: (float)PersistentLaserLifetime,
                angularSpeed: angularSpeed,
                owner: "aphantasia_persistent_laser",
                longLastingLaser: true)
            {
                TelegraphDuration = 1.6f,
                LaserWaveSpeed = waveSpeed,
            };
            staged.Add(laser);
        }
        CommitVolley(context.ProjectileSink);
    }

    private void AddSurvivalLaserGrid(List<EnemyProjectile> sink)
    {
        if (Phase < 3 || EncounterState is not (AphantasiaEncounterState.Survival
            or AphantasiaEncounterState.Finale))
            return;
        _survivalGridVolleyCount++;
        if (_survivalGridVolleyCount % 6 != 0)
            return;
        FireArenaLaserGrid(sink,
            diagonal: (_survivalGridVolleyCount / 6) % 2 == 0);
    }

    private void UpdateArenaHalfPressure(EnemyUpdateContext context, double dt)
    {
        if (Phase <= 2 || CombatFiringPaused)
            return;
        for (int half = 0; half < 2; half++)
        {
            _halfPressureRemaining[half] -= dt;
            if (_halfPressureRemaining[half] > 0)
                continue;

            // Each side rolls its own cadence and projectile grammar. This
            // deliberately makes adjacent lanes disagree about speed, scale,
            // pellet count, and oscillation instead of mirroring one pattern.
            double cadence = (.42 + _rng.NextDouble() * 1.18) * HalfPressureRampCadenceMultiplier();
            _halfPressureRemaining[half] = cadence;
            int bulletCount = new[] { 1, 1, 3, 5 }[_rng.Next(4)];
            float speed = .46f + (float)_rng.NextDouble() * 1.72f;
            float sizeTiles = .18f + (float)_rng.NextDouble() * .48f;
            bool sinusoidal = _rng.Next(3) != 0;
            float amplitude = sinusoidal
                ? Simulation.TileSize * (.18f + (float)_rng.NextDouble() * .92f)
                : 0f;
            float frequency = sinusoidal
                ? .009f + (float)_rng.NextDouble() * .058f
                : .035f;
            float boundaryAngle = half == 0
                ? MathF.PI / 2f + (float)_rng.NextDouble() * MathF.PI
                : -MathF.PI / 2f + (float)_rng.NextDouble() * MathF.PI;
            Vector2 origin = ArenaCenter
                + new Vector2(MathF.Cos(boundaryAngle), MathF.Sin(boundaryAngle))
                * ArenaRadius * .92f;
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            float aim = AngleTo(origin, Vector2.Lerp(ArenaCenter, player, .38f));
            float spread = bulletCount == 1 ? 0f
                : .22f + (float)_rng.NextDouble() * .62f;
            string owner = $"half_{half}_volley_{_halfVolleySerial++}";
            List<EnemyProjectile> staged = BeginVolley();
            for (int pellet = 0; pellet < bulletCount; pellet++)
            {
                float fraction = bulletCount == 1 ? .5f
                    : pellet / (float)(bulletCount - 1);
                AddShot(staged, origin,
                    aim - spread / 2f + fraction * spread,
                    speed * (.9f + pellet % 2 * .13f),
                    sizeTiles * (.88f + pellet % 3 * .12f),
                    half == 0 ? Light.Accent : Dark.Accent,
                    owner, sinusoidal ? "sine" : "linear",
                    amplitude * (pellet % 2 == 0 ? 1f : -1f), 14f,
                    frequency: frequency);
            }
            CommitVolley(context.ProjectileSink);
        }
    }

    private void FireRing(List<EnemyProjectile> sink, Vector2 origin, int count,
        float rotation, float speed, float size, string owner, bool alternating)
    {
        for (int index = 0; index < count; index++)
        {
            Color color = alternating
                ? index % 2 == 0 ? Light.Accent : Dark.Accent
                : Rainbow(index / (float)Math.Max(1, count) + rotation * .02f);
            AddShot(sink, origin, rotation + index * MathF.Tau / count,
                speed * (.88f + index % 3 * .08f), size * (.82f + index % 4 * .12f),
                color, owner, index % 4 == 0 ? "sine" : "linear",
                Simulation.TileSize * .34f, 10f);
        }
    }

    private void FireOrderedRing(List<EnemyProjectile> sink, Vector2 origin, int count,
        float rotation, float speed, float size, string owner, int sineEvery)
    {
        for (int index = 0; index < count; index++)
        {
            bool sinusoidal = sineEvery > 0 && index % sineEvery == 0;
            AddShot(sink, origin, rotation + index * MathF.Tau / count,
                speed, size,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, sinusoidal ? "sine" : "linear",
                sinusoidal
                    ? (index % (sineEvery * 2) == 0 ? 1f : -1f)
                        * Simulation.TileSize * .62f
                    : 0f,
                10f, frequency: sinusoidal ? .014f : .035f,
                shape: sinusoidal ? "crescent" : "needle");
        }
    }

    private void FireBrokenRing(List<EnemyProjectile> sink, Vector2 origin, int count,
        float rotation, float speed, float size, string owner)
    {
        for (int index = 0; index < count; index++)
        {
            if ((index + (int)(_stateElapsed * 1.7)) % 6 is 2 or 3)
                continue;
            float stagger = index % 2 == 0 ? 0 : .075f;
            AddShot(sink, origin, rotation + index * MathF.Tau / count + stagger,
                speed * (.86f + index % 4 * .08f), size * (.82f + index % 3 * .14f),
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, index % 3 == 0 ? "sine" : "linear",
                Simulation.TileSize * .4f, 10f);
        }
    }

    private void FireFan(List<EnemyProjectile> sink, Vector2 origin, float direction,
        int count, float spread, float speed, Color color, string owner)
    {
        for (int index = 0; index < count; index++)
        {
            float fraction = count == 1 ? .5f : (float)index / (count - 1);
            AddShot(sink, origin, direction - spread / 2f + fraction * spread,
                speed, .25f + index % 3 * .05f, color, owner,
                index % 3 == 0 ? "sine" : "linear", Simulation.TileSize * .38f, 9f,
                shape: "diamond");
        }
    }

    private void FireAimedRibbon(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 target, int count, float speed, string owner)
    {
        float aim = AngleTo(origin, target);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .115f;
            AddShot(sink, origin, aim + offset, speed * (.9f + index % 2 * .16f),
                .22f + index % 4 * .045f,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, "sine", Simulation.TileSize * (.32f + index % 3 * .12f), 10f);
        }
    }

    private void FireEdgeCurtain(List<EnemyProjectile> sink, bool vertical,
        bool reverse, int lanes, float speed, string owner)
    {
        for (int index = 0; index < lanes; index++)
        {
            float across = -ArenaRadius * .82f + ArenaRadius * 1.64f
                * (index + .5f) / lanes;
            Vector2 origin;
            float direction;
            if (vertical)
            {
                origin = ArenaCenter + new Vector2(across, reverse ? ArenaRadius * .92f : -ArenaRadius * .92f);
                direction = reverse ? -MathF.PI / 2f : MathF.PI / 2f;
            }
            else
            {
                origin = ArenaCenter + new Vector2(reverse ? ArenaRadius * .92f : -ArenaRadius * .92f, across);
                direction = reverse ? MathF.PI : 0;
            }
            if (index % 5 == 2)
                continue; // repeated breathing gaps travel with the wave.
            AddShot(sink, origin, direction + MathF.Sin(index * 1.7f) * .045f,
                speed * (.88f + index % 3 * .08f), .25f + index % 4 * .045f,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, index % 3 == 0 ? "sine" : "linear", Simulation.TileSize * .3f, 12f);
        }
    }

    private void FireOrderedCurtain(List<EnemyProjectile> sink, bool vertical,
        bool reverse, int lanes, float speed, string owner)
    {
        int movingGap = (_regularVolleyCount / 2) % Math.Max(1, lanes);
        for (int index = 0; index < lanes; index++)
        {
            if (index == movingGap || index == (movingGap + 1) % lanes)
                continue;
            float across = -ArenaRadius * .82f + ArenaRadius * 1.64f
                * (index + .5f) / lanes;
            Vector2 origin = vertical
                ? ArenaCenter + new Vector2(across,
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f)
                : ArenaCenter + new Vector2(
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f, across);
            float direction = vertical
                ? reverse ? -MathF.PI / 2f : MathF.PI / 2f
                : reverse ? MathF.PI : 0f;
            bool sinusoidal = index % 4 == 0;
            AddShot(sink, origin, direction, speed, .25f,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, sinusoidal ? "sine" : "linear",
                sinusoidal ? Simulation.TileSize * .5f : 0f,
                12f, frequency: sinusoidal ? .014f : .035f,
                shape: sinusoidal ? "crescent" : "needle");
        }
    }

    private void FireMirroredRibbon(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 target)
    {
        float aim = AngleTo(origin, target);
        for (int lane = -2; lane <= 2; lane++)
        {
            float offset = lane * .13f;
            for (int mirror = -1; mirror <= 1; mirror += 2)
            {
                AddShot(sink, origin, aim + offset + mirror * .035f,
                    mirror < 0 ? 1.34f : .96f,
                    mirror < 0 ? .22f : .28f,
                    mirror < 0 ? Light.Accent : Dark.Accent,
                    "ribbon_pursuit_mirror", "sine",
                    mirror * Simulation.TileSize * (.38f + Math.Abs(lane) * .08f),
                    10f, frequency: mirror < 0 ? .026f : .038f,
                    shape: "crescent");
            }
        }
    }

    private void FireRefractorPair(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 target)
    {
        float aim = AngleTo(origin, target);
        for (int side = -1; side <= 1; side += 2)
        {
            AddShot(sink, origin, aim + side * .2f, 1.08f, .36f,
                side < 0 ? Light.Accent : Dark.Accent,
                "refractor", "linear", 0f, 12f,
                shape: "star", splitCount: 3, splitProgress: .5f,
                splitSpeedScale: 1.12f, splitSpread: .84f,
                splitChildLifetime: 9f, splitTelegraphStartRatio: .55f);
        }
    }

    private void FireFracturedCurtain(List<EnemyProjectile> sink, bool vertical,
        bool reverse, int lanes, string owner)
    {
        int firstGap = (_regularVolleyCount * 3) % Math.Max(1, lanes);
        for (int index = 0; index < lanes; index++)
        {
            if (index == firstGap || index == (firstGap + 1) % lanes
                || (index + _regularVolleyCount) % 7 == 3)
                continue;
            float across = -ArenaRadius * .82f + ArenaRadius * 1.64f
                * (index + .5f) / lanes;
            Vector2 origin = vertical
                ? ArenaCenter + new Vector2(across,
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f)
                : ArenaCenter + new Vector2(
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f, across);
            float direction = vertical
                ? reverse ? -MathF.PI / 2f : MathF.PI / 2f
                : reverse ? MathF.PI : 0f;
            bool slowCurl = index % 2 != 0;
            AddShot(sink, origin, direction + (index % 3 - 1) * .035f,
                slowCurl ? .72f : 1.58f,
                slowCurl ? .34f : .22f,
                slowCurl ? Dark.Accent : Light.Accent,
                owner, slowCurl ? "sine" : "linear",
                slowCurl
                    ? (index % 4 < 2 ? 1f : -1f) * Simulation.TileSize * .4f
                    : 0f,
                12f, frequency: slowCurl ? .052f : .035f,
                shape: slowCurl ? "crescent" : "needle");
        }
    }

    /// <summary>
    /// The "flood" wall of giant, slow-moving portal seeds. Used to fire 7
    /// lanes that each cascaded into 8 more splits on arrival -- up to ~56
    /// giant projectiles piling up in the room per call, lingering for their
    /// full 32s child lifetime. Now a flat 8-lane burst with no cascade and
    /// a gentle forward acceleration, so the room fills with far fewer of
    /// them and they sweep out under their own power instead of drifting.
    /// </summary>
    private void FireEdgePortals(List<EnemyProjectile> sink, bool vertical, string owner)
    {
        for (int index = 0; index < 8; index++)
        {
            float lane = index - 3.5f;
            Vector2 origin = vertical
                ? ArenaCenter + new Vector2(lane * ArenaRadius * .18f, -ArenaRadius * .86f)
                : ArenaCenter + new Vector2(-ArenaRadius * .86f, lane * ArenaRadius * .18f);
            float direction = vertical ? MathF.PI / 2f : 0;
            FirePortalSeed(sink, origin, direction, .4f + index * .02f, owner,
                cascade: false, acceleration: .22f);
        }
    }

    private void FirePortalSeed(List<EnemyProjectile> sink, Vector2 origin,
        float direction, float speed, string owner, bool cascade = true, float acceleration = 0f)
    {
        float size = Simulation.TileSize * .92f;
        var portal = new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f, direction, speed,
            Damage * .72f, size,
            travelRange: ArenaRadius * 2.1f,
            color: Rainbow((float)_visualTime * .08f + direction / MathF.Tau),
            shape: "orbit_core", path: "linear", lifetime: 11f,
            owner: $"aphantasia_portal_{owner}", ignoreWalls: true, acceleration: acceleration)
        {
            SplitCount = cascade ? 8 : 0,
            SplitAt = cascade ? Simulation.TileSize * 2.4f : null,
            SplitSpeedScale = .82f,
            SplitSpread = MathF.Tau,
            SplitRadial = true,
            SplitChildLifetime = 32f,
            ThreatReservationCost = cascade ? 8 : 1,
            SplitTelegraphStartRatio = .72f,
            OriginTelegraphDuration = .68f,
            LargeShot3D = Phase >= 3,
        };
        sink.Add(portal);
    }

    /// <summary>
    /// A slow-decaying giant anchor fired from the boss center. Its old 5.5s
    /// lifetime outlasted the ~4.2s it took to decelerate to a full stop, so
    /// anchors would park and linger right on top of the boss -- the
    /// lifetime is now shorter than the time-to-stop so it's always still
    /// drifting away when it expires.
    /// </summary>
    private void FireVoidAnchor(List<EnemyProjectile> sink, Vector2 origin,
        float direction)
    {
        AddShot(sink, origin, direction, 1.35f, .64f,
            Rainbow(direction / MathF.Tau + (float)_visualTime * .04f),
            "void_anchor", "linear", 0f, 3.2f,
            shape: "orbit_core", speedDecay: .32f,
            preserveAuthoredLifetime: true);
    }

    private void AddShot(List<EnemyProjectile> sink, Vector2 origin, float direction,
        float speed, float sizeTiles, Color color, string owner, string path,
        float amplitude, float lifetime, bool deliberatelyShortRange = false,
        float frequency = .035f, string? shape = null, float speedDecay = 0f,
        bool preserveAuthoredLifetime = false, int splitCount = 0,
        float splitProgress = 0f, float splitSpeedScale = 1.08f,
        float? splitSpread = null, float? splitChildLifetime = null,
        float splitTelegraphStartRatio = 1f)
    {
        float size = Simulation.TileSize
            * Math.Max(MinimumProjectileSizeTiles, sizeTiles);
        float edgeRange = DistanceToArenaEdge(origin, direction) + size;
        float travelRange = deliberatelyShortRange
            ? Math.Min(ArenaRadius * .42f, edgeRange)
            : edgeRange;
        float requiredLifetime = travelRange
            / Math.Max(.01f, speed * .52f * (float)Simulation.ReferenceFps * .88f)
            + .75f;
        string projectileShape = shape ?? (path == "sine" ? "crescent" : "needle");
        var projectile = new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f,
            direction, speed, Damage * .62f, size,
            travelRange: travelRange, color: color,
            shape: projectileShape, path: path, amplitude: amplitude, frequency: frequency,
            lifetime: deliberatelyShortRange || preserveAuthoredLifetime
                ? lifetime
                : Math.Max(lifetime, requiredLifetime),
            speedDecay: speedDecay,
            owner: $"aphantasia_{owner}", ignoreWalls: true)
        {
            LargeShot3D = Phase >= 3 && sizeTiles >= LargeShot3DSizeTiles,
        };
        if (splitCount > 1)
        {
            projectile.SplitCount = splitCount;
            projectile.SplitAt = travelRange * Math.Clamp(splitProgress, .05f, .95f);
            projectile.SplitSpeedScale = splitSpeedScale;
            projectile.SplitSpread = splitSpread;
            projectile.SplitChildLifetime = splitChildLifetime;
            projectile.ThreatReservationCost = splitCount;
            projectile.SplitTelegraphStartRatio = splitTelegraphStartRatio;
        }
        sink.Add(projectile);
    }

    private float DistanceToArenaEdge(Vector2 origin, float direction)
    {
        Vector2 offset = origin - ArenaCenter;
        Vector2 heading = new(MathF.Cos(direction), MathF.Sin(direction));
        float projection = Vector2.Dot(offset, heading);
        float discriminant = projection * projection
            - (offset.LengthSquared() - ArenaRadius * ArenaRadius);
        if (discriminant <= 0)
            return ArenaRadius * 2f;
        return Math.Max(Simulation.TileSize,
            -projection + MathF.Sqrt(discriminant));
    }

    private List<EnemyProjectile> BeginVolley()
    {
        _volleyScratch.Clear();
        return _volleyScratch;
    }

    private void CommitVolley(List<EnemyProjectile> sink)
    {
        int activeCost = 0;
        foreach (EnemyProjectile projectile in sink)
        {
            if (!projectile.RemFlag
                && projectile.Owner?.StartsWith("aphantasia_", StringComparison.Ordinal) == true)
            {
                activeCost += Math.Max(1, projectile.ThreatReservationCost);
            }
        }
        bool perimeterVolley = _volleyScratch.Count > 0
            && _volleyScratch.All(projectile =>
                projectile.Owner == "aphantasia_perimeter_drift");
        int volleyCap = perimeterVolley
            ? ActiveThreatSoftCap
            : ActiveThreatSoftCap - PerimeterThreatReserve;
        foreach (EnemyProjectile projectile in _volleyScratch)
        {
            int projectileCost = Math.Max(1, projectile.ThreatReservationCost);
            while (activeCost + projectileCost > volleyCap)
            {
                EnemyProjectile? longestLasting = sink
                    .Where(candidate => !candidate.RemFlag
                        && candidate.Owner?.StartsWith("aphantasia_", StringComparison.Ordinal) == true)
                    .MaxBy(candidate => candidate.Age);
                if (longestLasting is null)
                    break;
                sink.Remove(longestLasting);
                activeCost -= Math.Max(1, longestLasting.ThreatReservationCost);
            }
            if (activeCost + projectileCost <= volleyCap)
            {
                sink.Add(projectile);
                activeCost += projectileCost;
            }
        }
        _volleyScratch.Clear();
    }

    private static float AngleTo(Vector2 from, Vector2 to) =>
        MathF.Atan2(to.Y - from.Y, to.X - from.X);

    private string DispositionText()
    {
        if (TrueLight)
            return "TRUE LIGHT";
        if (TrueDark)
            return "TRUE DARK";
        return $"LIGHT {Light.Disposition.ToString().ToUpperInvariant()} / "
            + $"DARK {Dark.Disposition.ToString().ToUpperInvariant()}";
    }
}
