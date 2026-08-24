using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

[Collection("GameProfileState")]
public sealed class AphantasiaTests
{
    private static Battleground MakeArena() =>
        BossArenaFactory.Create("aphantasia", Progression.FinalBossLevel);

    private static GameSession MakeSession()
    {
        GamePaths.Select("phantasia");
        return new GameSession(
            Battleground.GeneratePhantasia(), 1280, 720, new Random(3));
    }

    private static Aphantasia MakeBoss(
        bool noHealing = false, bool noExtract = false) =>
        new(1000, 1000, MakeArena(), new Random(5), noHealing, noExtract);

    private static EnemyUpdateContext Context(
        Aphantasia boss, Battleground arena) => new()
    {
        PlayerWorldX = boss.ArenaCenter.X + boss.ArenaRadius * .55f,
        PlayerWorldY = boss.ArenaCenter.Y,
        Battleground = arena,
    };

    private static void AdvanceUntil(
        Aphantasia boss,
        EnemyUpdateContext context,
        Func<bool> completed,
        int maximumTicks = Simulation.FrameRate * 90)
    {
        for (int tick = 0; tick < maximumTicks; tick++)
        {
            boss.Update(context);
            context.ProjectileSink.Clear();
            if (completed())
                return;
        }
        Assert.Fail("Aphantasia did not reach the expected state within the bounded simulation.");
    }

    /// <summary>
    /// Crosses whatever HP gate is currently active using hits that each
    /// stay under the damage cap (a single hit at or above 1/4-or-1/2 of
    /// the active bar now shields the boss for
    /// <see cref="Aphantasia.DamageCapInvincibilityDuration"/> seconds
    /// instead of firing the gate immediately) -- so tests that just want
    /// "cross the floor right now" can keep asserting on the immediate
    /// post-hit state.
    /// </summary>
    private static HitResult DealDamageBelowTheCap(
        Aphantasia boss, string partId = "body")
    {
        double increment = boss.MaxHp * .1;
        HitResult result = new(false, false, 0, true);
        for (int attempt = 0; attempt < 20
            && boss.EncounterState == AphantasiaEncounterState.Combat; attempt++)
        {
            result = boss.TakeDamage(increment, partId);
        }
        return result;
    }

    private static void OpenDamageWindow(
        Aphantasia boss, EnemyUpdateContext context)
    {
        if (boss.PhaseHandoffActive)
            AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
        Assert.True(boss.TakeDamage(boss.Light.MaxHp, "light").Applied);
        AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
        Assert.True(boss.TakeDamage(1, "body").Blocked);
        Assert.True(boss.TakeDamage(boss.Dark.MaxHp, "dark").Applied);
        AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
        Assert.True(boss.DamageWindowActive);
    }

    private static IReadOnlyList<AphantasiaPattern> PatternsForPhase(int phase) =>
        phase switch
        {
            1 => Aphantasia.PhaseOnePatterns,
            2 => Aphantasia.PhaseTwoPatterns,
            3 => Aphantasia.PhaseThreePatterns,
            _ => Aphantasia.PhaseFourPatterns,
        };

    private static void SelectPattern(Aphantasia boss, string key)
    {
        for (int attempt = 0; attempt < Aphantasia.PatternSelectionCycleCount(boss.Phase); attempt++)
        {
            if (boss.CurrentPattern.Key == key)
                return;
            boss.DebugAdvanceSubPhase();
        }
        Assert.Fail($"Pattern '{key}' was absent from phase {boss.Phase}'s shuffled bag.");
    }

    private static void StepEncounter(Aphantasia boss, EnemyUpdateContext context)
    {
        boss.Update(context);
        if (boss.TransitionCleanupRequested)
        {
            context.ProjectileSink.Clear();
            boss.TransitionCleanupRequested = false;
        }

        var children = new List<EnemyProjectile>();
        foreach (EnemyProjectile projectile in context.ProjectileSink.ToList())
        {
            projectile.Update(context.Battleground, casualMode: false);
            children.AddRange(projectile.SpawnedProjectiles);
            projectile.SpawnedProjectiles.Clear();
        }
        context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
        context.ProjectileSink.AddRange(children);
    }

    private sealed record SequencePressure(int Peak, int ReservedPeak, int Hits);

    private static SequencePressure SimulateSequencePressure(
        int phase, bool finale, float playerRadiusRatio)
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(700 + phase),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(phase);
        if (finale)
            boss.DebugStartFinale();
        else
            boss.DebugStartSurvival();
        Vector2 player = boss.ArenaCenter + Vector2.UnitX * boss.ArenaRadius * playerRadiusRatio;
        EnemyUpdateContext context = Context(boss, arena);
        context.PlayerWorldX = player.X;
        context.PlayerWorldY = player.Y;
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle(
            (int)(player.X - playerSize / 2f),
            (int)(player.Y - playerSize / 2f), playerSize, playerSize);
        var hitThreats = new HashSet<EnemyProjectile>();
        int peak = 0;
        int reservedPeak = 0;
        int maximumTicks = (int)Math.Ceiling(boss.SurvivalDuration * Simulation.FrameRate) + 12;

        for (int tick = 0; tick < maximumTicks; tick++)
        {
            StepEncounter(boss, context);
            foreach (EnemyProjectile projectile in context.ProjectileSink)
            {
                if (projectile.Collides(playerRect))
                    hitThreats.Add(projectile);
                Assert.True(!projectile.PersistentHazard || projectile.Path == "laser");
                Assert.DoesNotContain(projectile.Path, new[] { "pool", "mine" });
                Assert.True(projectile.Lifetime is > 0 and <= 240f
                    || projectile.Lifetime is null
                    && float.IsFinite(projectile.RemainingRange)
                    && projectile.Speed > 0);
                Assert.True(projectile.Size >= Simulation.TileSize
                    * Aphantasia.MinimumProjectileSizeTiles);
            }
            peak = Math.Max(peak, context.ProjectileSink.Count);
            reservedPeak = Math.Max(reservedPeak,
                context.ProjectileSink.Sum(projectile =>
                    Math.Max(1, projectile.ThreatReservationCost)));
            if (boss.SurvivalRemaining <= 0)
                break;
        }

        return new SequencePressure(peak, reservedPeak, hitThreats.Count);
    }

    [Fact]
    public void CatalogAndEncounterMetadata_RegisterTheFinalBoss()
    {
        BossCatalog catalog = BossCatalog.CreateDefault();

        Assert.True(catalog.TryGet("aphantasia", out BossDefinition? definition));
        Assert.NotNull(definition);
        Assert.Equal("Aphantasia, Essence of Darkness", definition.DisplayName);
        Assert.IsType<Aphantasia>(catalog.Spawn(
            "aphantasia", MakeArena(), float.PositiveInfinity, new Random(7)));

        BossEncounterDefinition encounter =
            BossEncounterCatalog.DefinitionFor("aphantasia");
        Assert.Equal(20, encounter.Tier);
        Assert.Equal("aphantasia", encounter.Arena.BossKey);
        Assert.Contains(encounter.Phases, phase => phase.Survival);
    }

    [Fact]
    public void Arena_IsLargerThanMaladysMaximumScaledCourt()
    {
        BossArenaDefinition definition =
            BossArenaFactory.DefinitionFor("aphantasia");
        BossArenaDefinition malady = BossArenaFactory.DefinitionFor("malady");
        Battleground arena = MakeArena();

        Assert.True(definition.SizeTiles > malady.SizeTiles * 1.5f);
        Assert.True(definition.PlayableRadiusTiles >
            malady.PlayableRadiusTiles * 1.5f);
        Assert.Equal(definition.SizeTiles, arena.Width);
        Assert.Equal(definition.SizeTiles, arena.Height);
        Assert.Equal("aphantasia", arena.VisualThemeKey);
        Assert.Equal(TileType.OuterVoid, arena.TileAt(0, 0));
        Assert.False(arena.TileAt(arena.Width / 2, arena.Height / 2).IsSolid());
        Assert.Empty(arena.PathDecorations);
        Assert.All(arena.Palettes, palette =>
        {
            Assert.InRange(palette.Ground.R, (byte)9, (byte)15);
            Assert.InRange(palette.Ground.G, (byte)15, (byte)21);
            Assert.InRange(palette.Ground.B, (byte)30, (byte)40);
        });
        LightingTheme lighting = WorldLighting.ThemeFor("aphantasia");
        Assert.Equal((byte)132, lighting.DarknessAlpha);
        Assert.Equal(4.8f, lighting.PlayerRadiusTiles);
    }

    [Theory]
    [InlineData(1, 20.0)]
    [InlineData(2, 20.0)]
    [InlineData(3, 30.0)]
    [InlineData(4, 30.0)]
    public void MidpointSurvivals_UseTheirAuthoredDurations(
        int phase, double expectedSeconds)
    {
        Aphantasia boss = MakeBoss(noHealing: true, noExtract: true);
        boss.DebugSetPhase(phase);

        boss.DebugStartSurvival();

        Assert.Equal(expectedSeconds, boss.SurvivalRemaining);
        Assert.Equal(AphantasiaEncounterState.Survival, boss.EncounterState);
        Assert.True(boss.TakeDamage(1, "body").Blocked);
    }

    [Theory]
    [InlineData(1, .75, 20.0)]
    [InlineData(2, .25, 20.0)]
    [InlineData(3, .50, 30.0)]
    public void HealthGates_StartTheCorrectMidpointSurvival(
        int phase, double floorRatio, double expectedSeconds)
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(13));
        boss.DebugSetPhase(phase);
        if (phase <= 2)
            OpenDamageWindow(boss, Context(boss, arena));

        HitResult result = DealDamageBelowTheCap(boss);

        Assert.True(result.Applied);
        Assert.Equal((int)Math.Round(boss.MaxHp * floorRatio), boss.Hp);
        Assert.Equal(AphantasiaEncounterState.Survival, boss.EncounterState);
        Assert.Equal(expectedSeconds, boss.SurvivalRemaining);
    }

    [Fact]
    public void HealthBars_AreSharedForOneAndTwoThenResetForThreeAndFour()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(19),
            noHealing: true, noExtract: true);
        EnemyUpdateContext context = Context(boss, arena);
        boss.DebugSetPhase(1);

        OpenDamageWindow(boss, context);
        DealDamageBelowTheCap(boss);
        AdvanceUntil(boss, context, () =>
            boss.EncounterState == AphantasiaEncounterState.Combat
            && boss.SurvivalKind == AphantasiaSurvivalKind.None);
        OpenDamageWindow(boss, context);
        DealDamageBelowTheCap(boss);

        Assert.Equal(2, boss.Phase);
        Assert.Equal((int)Math.Round(boss.MaxHp * .5), boss.Hp);

        OpenDamageWindow(boss, context);
        DealDamageBelowTheCap(boss);
        AdvanceUntil(boss, context, () =>
            boss.EncounterState == AphantasiaEncounterState.Combat
            && boss.SurvivalKind == AphantasiaSurvivalKind.None);
        OpenDamageWindow(boss, context);
        DealDamageBelowTheCap(boss);
        Assert.Equal(AphantasiaEncounterState.Transforming, boss.EncounterState);
        AdvanceUntil(boss, context, () =>
            boss.Phase == 3
            && boss.EncounterState == AphantasiaEncounterState.Combat);

        Assert.Equal(3, boss.Phase);
        Assert.Equal(boss.DisplayedMaxHp, boss.DisplayedHp);

        boss.DebugSetPhase(4);
        Assert.Equal(boss.DisplayedMaxHp, boss.DisplayedHp);
        Assert.Equal("Aphantasia, Core of The Void", boss.DisplayName);
    }

    [Theory]
    [InlineData(3, 30.0)]
    [InlineData(4, 45.0)]
    public void Finales_UsePhaseSpecificDurations(int phase, double expectedSeconds)
    {
        Aphantasia boss = MakeBoss(noHealing: true, noExtract: true);
        boss.DebugSetPhase(phase);

        boss.DebugStartFinale();

        Assert.Equal(expectedSeconds, boss.SurvivalRemaining);
        Assert.Equal(AphantasiaEncounterState.Finale, boss.EncounterState);
        Assert.True(boss.TakeDamage(1, "body").Blocked);
    }

    [Fact]
    public void VoidFinale_SweepLasersSpawnEquallySpacedOnlyInClosingWindowAndDriftClockwise()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(661),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(4);
        boss.DebugStartFinale();
        EnemyUpdateContext context = Context(boss, arena);

        static List<EnemyProjectile> SweepLasers(EnemyUpdateContext context) =>
            context.ProjectileSink.Where(projectile =>
                projectile.Owner == "aphantasia_finale_sweep_laser").ToList();

        for (int tick = 0; tick < Simulation.FrameRate * 4; tick++)
            boss.Update(context);
        Assert.True(boss.SurvivalRemaining > Aphantasia.FinaleSweepLaserWindowDuration);
        Assert.Empty(SweepLasers(context));

        while (boss.SurvivalRemaining > Aphantasia.FinaleSweepLaserWindowDuration)
            boss.Update(context);

        List<EnemyProjectile> lasers = SweepLasers(context);
        Assert.Equal(Aphantasia.FinaleSweepLaserCount, lasers.Count);
        Assert.All(lasers, laser =>
        {
            Assert.True(laser.LongLastingLaser);
            Assert.Equal("laser", laser.Path);
            Assert.Equal("diamond", laser.Shape);
            Assert.True(laser.Size > Simulation.TileSize * .4f,
                "Expected the sweep lasers to render noticeably larger than a standard laser.");
        });
        float[] initialDirections = lasers.Select(laser => laser.Direction)
            .OrderBy(direction => direction).ToArray();
        for (int index = 1; index < initialDirections.Length; index++)
        {
            float spacing = initialDirections[index] - initialDirections[index - 1];
            Assert.InRange(spacing, MathF.Tau / 5f - .02f, MathF.Tau / 5f + .02f);
        }

        EnemyProjectile arm = lasers[0];
        float windowStartDirection = arm.Direction;
        double windowStartRemaining = boss.SurvivalRemaining;
        double ElapsedInWindow() => windowStartRemaining - boss.SurvivalRemaining;
        void TickUntilElapsed(double targetElapsed)
        {
            while (ElapsedInWindow() < targetElapsed)
                boss.Update(context);
        }

        // Clockwise phase: direction should climb toward the authored 120
        // degrees over its first ~5s (sampled a little short of the 5s/2.5s
        // boundary to avoid a boundary-tick flake).
        TickUntilElapsed(Aphantasia.FinaleSweepLaserClockwiseDuration - .1);
        float nearPeak = arm.Direction;
        Assert.True(nearPeak - windowStartDirection
            > MathHelper.ToRadians(Aphantasia.FinaleSweepLaserClockwiseDegrees * .9f));

        // Crossing into the counterclockwise phase: direction should now
        // fall back down, but the cycle's net drift stays positive -- it
        // never returns to (let alone past) where the clockwise phase
        // started.
        TickUntilElapsed(Aphantasia.FinaleSweepLaserClockwiseDuration
            + Aphantasia.FinaleSweepLaserCounterclockwiseDuration - .3);
        Assert.True(arm.Direction < nearPeak);
        Assert.True(arm.Direction > windowStartDirection);
    }

    [Fact]
    public void VoidFinale_SweepLasersAreRemovedWhenTheFinaleEnds()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(661),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(4);
        boss.DebugStartFinale();
        EnemyUpdateContext context = Context(boss, arena);

        while (boss.SurvivalRemaining > 0)
            boss.Update(context);

        List<EnemyProjectile> lasers = context.ProjectileSink.Where(projectile =>
            projectile.Owner == "aphantasia_finale_sweep_laser").ToList();
        Assert.Equal(Aphantasia.FinaleSweepLaserCount, lasers.Count);
        Assert.All(lasers, laser => Assert.True(laser.RemFlag));
        Assert.Equal(AphantasiaEncounterState.Dying, boss.EncounterState);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PersistentRotatingLaser_NeverFiresBeforePhaseThree(int phase)
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(701));
        boss.DebugSetPhase(phase);
        EnemyUpdateContext context = Context(boss, arena);

        for (int tick = 0; tick < Simulation.FrameRate * 30; tick++)
            boss.Update(context);

        Assert.DoesNotContain(context.ProjectileSink, projectile =>
            projectile.Owner == "aphantasia_persistent_laser");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void PersistentRotatingLaser_SpawnsAVariedEquallySpacedOneDirectionArray(int phase)
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(701),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(phase);
        EnemyUpdateContext context = Context(boss, arena);

        for (int tick = 0; tick < Simulation.FrameRate * 15; tick++)
            boss.Update(context);

        List<EnemyProjectile> lasers = context.ProjectileSink.Where(projectile =>
            projectile.Owner == "aphantasia_persistent_laser").ToList();
        Assert.NotEmpty(lasers);
        Assert.Contains(lasers.Count, Aphantasia.PersistentLaserArmCounts);
        Assert.All(lasers, laser =>
        {
            Assert.True(laser.LongLastingLaser);
            Assert.Equal("laser", laser.Path);
            Assert.Equal(Aphantasia.PersistentLaserLifetime, (double)laser.Lifetime!.Value, 3);
            Assert.NotEqual(0f, laser.AngularSpeed);
            Assert.True(laser.Size > Simulation.TileSize * .3f
                && laser.Size < Simulation.TileSize * .5f,
                "Expected a persistent laser larger than a standard laser but smaller than the finale sweep.");
        });

        // Every arm in one array shares the same spin direction (the
        // attack is "always moving in one direction," never back-and-forth
        // like the finale sweep) and starts evenly spaced around the ring.
        float firstAngularSpeed = lasers[0].AngularSpeed;
        Assert.All(lasers, laser => Assert.Equal(firstAngularSpeed, laser.AngularSpeed));
        float[] directions = lasers.Select(laser => laser.Direction)
            .OrderBy(direction => direction).ToArray();
        for (int index = 1; index < directions.Length; index++)
        {
            float spacing = directions[index] - directions[index - 1];
            Assert.InRange(spacing, MathF.Tau / lasers.Count - .02f,
                MathF.Tau / lasers.Count + .02f);
        }

        // Driving the real per-frame Update lets the array actually turn --
        // confirming the built-in AngularSpeed rotation (not just the field
        // value) carries it consistently in that one direction.
        EnemyProjectile arm = lasers[0];
        float beforeSpin = arm.Direction;
        for (int tick = 0; tick < Simulation.FrameRate * 3; tick++)
            arm.Update(arena, casualMode: false);
        float delta = arm.Direction - beforeSpin;
        Assert.True(Math.Sign(delta) == Math.Sign(firstAngularSpeed) && delta != 0,
            $"Expected the laser to keep turning the same way its AngularSpeed ({firstAngularSpeed}) implies, got delta {delta}.");
    }

    [Fact]
    public void PersistentRotatingLaser_SometimesTravelsAsASineWaveAndSometimesStaysStraight()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(701),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(3);
        EnemyUpdateContext context = Context(boss, arena);

        // Enough spawns (cadence is 14s) that, at a 45% roll per spawn, the
        // chance every single one landed on the same straight/wavy outcome
        // is negligible -- proving the wave is a genuine per-spawn variation
        // rather than an always-on or never-on setting.
        for (int tick = 0; tick < Simulation.FrameRate * 150; tick++)
            boss.Update(context);

        List<EnemyProjectile> lasers = context.ProjectileSink.Where(projectile =>
            projectile.Owner == "aphantasia_persistent_laser").ToList();
        Assert.Contains(lasers, laser => laser.Amplitude == 0);
        Assert.Contains(lasers, laser => laser.Amplitude > 0);
        Assert.All(lasers.Where(laser => laser.Amplitude > 0), laser =>
        {
            Assert.True(laser.Frequency > 0);
            Assert.NotEqual(0f, laser.LaserWaveSpeed);
        });
    }

    [Fact]
    public void Minis_ShieldTheBossAndExposeNamedHitboxes()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(23));
        EnemyUpdateContext context = Context(boss, arena);
        boss.DebugSetPhase(1);

        IReadOnlyList<(string Part, Rectangle Rect)> hitboxes =
            boss.GetWorldHitboxes();
        Assert.Contains(hitboxes, hitbox => hitbox.Part == "light");
        Assert.Contains(hitboxes, hitbox => hitbox.Part == "dark");
        Assert.True(boss.TakeDamage(1000, "body").Blocked);

        Assert.True(boss.TakeDamage(boss.Light.MaxHp, "light").Applied);
        AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
        Assert.True(boss.TakeDamage(1000, "body").Blocked);
        Assert.True(boss.TakeDamage(boss.Dark.MaxHp, "dark").Applied);
        AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
        Assert.True(boss.DamageWindowActive);
        Assert.True(boss.TakeDamage(1000, "body").Applied);

        AdvanceUntil(boss, context, () =>
            !boss.DamageWindowActive && boss.Light.Alive && boss.Dark.Alive);
        Assert.Equal(boss.Light.MaxHp, boss.Light.Hp);
        Assert.Equal(boss.Dark.MaxHp, boss.Dark.Hp);
        Assert.False(boss.DamageWindowActive);
        Assert.True(boss.TakeDamage(1000, "body").Blocked);
    }

    [Fact]
    public void BossAndMinis_UseTheRetunedHealthAndLargerTargets()
    {
        Aphantasia boss = MakeBoss();
        boss.DebugSetPhase(1);

        Assert.Equal(1_038_000, Aphantasia.BaseBarHealth);
        Assert.Equal(Aphantasia.BaseBarHealth, boss.MaxHp);
        Assert.Equal(54_690, Aphantasia.BaseMiniHealth);
        Assert.Equal(Aphantasia.BaseMiniHealth, boss.Light.MaxHp);
        Assert.Equal(Aphantasia.BaseMiniHealth, boss.Dark.MaxHp);
        int expectedEarlySize = (int)(Simulation.TileSize * 1.62f);
        IReadOnlyList<(string Part, Rectangle Rect)> earlyWorld = boss.GetWorldHitboxes();
        IReadOnlyList<(string Part, Rectangle Rect)> earlyScreen = boss.GetScreenHitboxes(
            new Camera(), boss.ArenaCenter, Vector2.Zero);
        Assert.Equal(expectedEarlySize, earlyWorld.Single(hitbox => hitbox.Part == "light").Rect.Width);
        Assert.Equal(expectedEarlySize, earlyScreen.Single(hitbox => hitbox.Part == "dark").Rect.Width);

        boss.DebugSetPhase(3);

        int expectedTesseractSize = (int)(Simulation.TileSize * 1.92f);
        IReadOnlyList<(string Part, Rectangle Rect)> tesseractWorld = boss.GetWorldHitboxes();
        IReadOnlyList<(string Part, Rectangle Rect)> tesseractScreen = boss.GetScreenHitboxes(
            new Camera(), boss.ArenaCenter, Vector2.Zero);
        Assert.Equal(expectedTesseractSize,
            tesseractWorld.Single(hitbox => hitbox.Part == "light").Rect.Width);
        Assert.Equal(expectedTesseractSize,
            tesseractScreen.Single(hitbox => hitbox.Part == "dark").Rect.Width);
    }

    [Fact]
    public void Minis_SpawnAndReviveFromTheBossOrigin()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(427));
        boss.DebugSetPhase(1);
        Vector2 bossOrigin = new(
            boss.WorldX + boss.Size / 2f,
            boss.WorldY + boss.Size / 2f);

        Assert.Equal(bossOrigin, boss.Light.Position);
        Assert.Equal(bossOrigin, boss.Dark.Position);

        EnemyUpdateContext context = Context(boss, arena);
        OpenDamageWindow(boss, context);
        AdvanceUntil(boss, context, () =>
            !boss.DamageWindowActive && boss.Light.Alive && boss.Dark.Alive);
        bossOrigin = new Vector2(
            boss.WorldX + boss.Size / 2f,
            boss.WorldY + boss.Size / 2f);

        Assert.Equal(bossOrigin, boss.Light.Position);
        Assert.Equal(bossOrigin, boss.Dark.Position);
        Assert.Equal(Vector2.Zero, boss.Light.Velocity);
        Assert.Equal(Vector2.Zero, boss.Dark.Velocity);
    }

    [Fact]
    public void MiniHealth_PersistsAcrossTimedSubPhaseChanges()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(29));
        EnemyUpdateContext context = Context(boss, arena);
        boss.DebugSetPhase(1);
        Assert.True(boss.TakeDamage(boss.Light.MaxHp * 2 / 3, "light").Applied);
        Assert.True(boss.TakeDamage(boss.Dark.MaxHp / 2, "dark").Applied);
        float lightHp = boss.Light.Hp;
        float darkHp = boss.Dark.Hp;
        int pattern = boss.PatternIndex;

        AdvanceUntil(boss, context, () => boss.PatternIndex != pattern);

        Assert.Equal(lightHp, boss.Light.Hp);
        Assert.Equal(darkHp, boss.Dark.Hp);
        Assert.NotEqual(pattern, boss.PatternIndex);
        Assert.True(boss.SubphaseRemaining > Aphantasia.SubphaseDuration - 1);
    }

    [Fact]
    public void EarlySurvivals_MakeBothMinisInvulnerable()
    {
        Aphantasia boss = MakeBoss();
        boss.DebugSetPhase(2);
        boss.DebugStartSurvival();
        float lightHp = boss.Light.Hp;
        float darkHp = boss.Dark.Hp;

        HitResult lightHit = boss.TakeDamage(1000, "light");
        HitResult darkHit = boss.TakeDamage(1000, "dark");

        Assert.True(lightHit.Blocked);
        Assert.True(darkHit.Blocked);
        Assert.Equal(lightHp, boss.Light.Hp);
        Assert.Equal(darkHp, boss.Dark.Hp);
    }

    [Fact]
    public void PhaseThreeChoice_EmpowersAndProtectsTheSurvivingMini()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(31));
        EnemyUpdateContext context = Context(boss, arena);
        boss.DebugSetPhase(3);
        boss.DebugStartSurvival();
        float originalDarkMax = boss.Dark.MaxHp;

        Assert.True(boss.TakeDamage(boss.Light.MaxHp, "light").Applied);
        Assert.False(boss.Light.Alive);
        Assert.True(boss.Dark.Alive);

        AdvanceUntil(boss, context, () =>
            boss.EncounterState == AphantasiaEncounterState.Combat
            && boss.Dark.Empowered);

        Assert.True(boss.Dark.Empowered);
        Assert.True(boss.Dark.Aggressive);
        Assert.Equal(Aphantasia.EmpoweredMiniHealth, boss.Dark.MaxHp);
        Assert.Equal(175_515, boss.Dark.MaxHp);
        Assert.True(boss.Dark.MaxHp > originalDarkMax * 3);
        Assert.True(boss.TakeDamage(1, "dark").Blocked);
    }

    [Fact]
    public void MiniDispositions_DeriveTrueLightAndTrueDarkMoods()
    {
        var moods = new HashSet<string>();
        for (int seed = 0; seed < 96; seed++)
        {
            var boss = new Aphantasia(1000, 1000, MakeArena(), new Random(seed));
            boss.DebugSetPhase(1);
            moods.Add(boss.TrueLight ? "light" : boss.TrueDark ? "dark" : "mixed");
        }

        Assert.Contains("light", moods);
        Assert.Contains("dark", moods);
        Assert.Contains("mixed", moods);
    }

    [Fact]
    public void PatternCatalog_ContainsAllEighteenAuthoredSubPhases()
    {
        Assert.Equal(18, Aphantasia.AllPatterns.Count);
        Assert.Equal(18, Aphantasia.AllPatterns.Select(pattern => pattern.Key).Distinct().Count());

        foreach (int phase in Enumerable.Range(1, 4))
        {
            IReadOnlyList<AphantasiaPattern> patterns = PatternsForPhase(phase);
            Assert.Equal(phase <= 2 ? 3 : 6, patterns.Count);
            int expectedPerMovement = phase <= 2 ? 1 : 2;
            Assert.Equal(expectedPerMovement,
                patterns.Count(pattern => pattern.Movement == AphantasiaMovementMode.Standing));
            Assert.Equal(expectedPerMovement,
                patterns.Count(pattern => pattern.Movement == AphantasiaMovementMode.Pathed));
            Assert.Equal(expectedPerMovement,
                patterns.Count(pattern => pattern.Movement == AphantasiaMovementMode.Chase));
        }
    }

    [Fact]
    public void PatternBags_CoverEachSubphaseExactlyOncePerCycleAndAvoidImmediateRepeats()
    {
        foreach (int phase in Enumerable.Range(1, 4))
        {
            Aphantasia boss = MakeBoss(noHealing: true, noExtract: true);
            boss.DebugSetPhase(phase);
            int count = Aphantasia.PatternSelectionCycleCount(phase);
            Assert.Equal(PatternsForPhase(phase).Count, count);
            var cycle = new List<string> { boss.CurrentPattern.Key };
            for (int index = 1; index < count; index++)
            {
                boss.DebugAdvanceSubPhase();
                cycle.Add(boss.CurrentPattern.Key);
            }

            // Every subphase in the phase appears exactly once per cycle --
            // a pure shuffle of the pool, no pattern favored or repeated.
            Assert.Equal(PatternsForPhase(phase).Select(pattern => pattern.Key).OrderBy(key => key),
                cycle.OrderBy(key => key));
            Assert.DoesNotContain(Enumerable.Range(1, cycle.Count - 1),
                index => cycle[index] == cycle[index - 1]);
            string finalPattern = cycle[^1];
            boss.DebugAdvanceSubPhase();
            Assert.NotEqual(finalPattern, boss.CurrentPattern.Key);
        }
    }

    [Fact]
    public void PathedSubphases_SendMinisAcrossMostOfTheArenaAtExistingFollowRates()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(433));
        boss.DebugSetPhase(1);
        SelectPattern(boss, "horizon_ellipse");
        boss.DebugSetMiniState(AphantasiaMiniKind.Light, boss.Light.MaxHp,
            AphantasiaMiniDisposition.Passive);
        boss.DebugSetMiniState(AphantasiaMiniKind.Dark, boss.Dark.MaxHp,
            AphantasiaMiniDisposition.Passive);
        EnemyUpdateContext context = Context(boss, arena);
        float farthest = 0;

        for (int tick = 0; tick < Aphantasia.SubphaseDuration * Simulation.FrameRate; tick++)
        {
            boss.Update(context);
            context.ProjectileSink.Clear();
            farthest = Math.Max(farthest,
                Math.Max(Vector2.Distance(boss.Light.Position, boss.ArenaCenter),
                    Vector2.Distance(boss.Dark.Position, boss.ArenaCenter)));
        }

        Assert.Equal(.76f, Aphantasia.MiniPathedRadiusRatio);
        Assert.True(farthest >= boss.ArenaRadius * .68f,
            $"Pathed Minis reached only {farthest / boss.ArenaRadius:P0} of the arena radius.");
    }

    [Fact]
    public void DarkArenaStates_RemainSubstantiallyIlluminated()
    {
        Aphantasia boss = MakeBoss(noHealing: true, noExtract: true);
        boss.DebugSetPhase(1);
        boss.DebugSetMiniState(AphantasiaMiniKind.Light, boss.Light.MaxHp,
            AphantasiaMiniDisposition.Passive);
        boss.DebugSetMiniState(AphantasiaMiniKind.Dark, boss.Dark.MaxHp,
            AphantasiaMiniDisposition.Passive);

        Assert.True(boss.TrueDark);
        Assert.InRange(boss.ArenaDarknessScale, .7f, .8f);
        Assert.True(boss.ArenaPlayerLightScale >= 1.25f);

        boss.DebugSetPhase(4);
        Assert.True(boss.ArenaDarknessScale <= .82f);
        Assert.True(boss.ArenaPlayerLightScale >= 1.2f);
    }

    [Fact]
    public void GenericPerimeterPressure_ScalesFromNoneToHalfToFullByPhase()
    {
        // Phase 3 and Phase 4 each fade their perimeter ring in across the
        // phase's first two subphases rather than switching straight to a
        // fixed count, so a phase's *opening* subphase (subphase 0) starts
        // wherever the previous phase left off (0 for Phase 3, half for
        // Phase 4) and only reaches the old flat values (4, then 8) once the
        // phase has been running a couple of subphases.
        foreach ((int phase, int subphasesIn, int expectedCount) in new[]
        {
            (1, 0, 0), (2, 0, 0),
            (3, 0, 0), (3, 2, 4),
            (4, 0, 4), (4, 2, 8),
        })
        {
            Battleground arena = MakeArena();
            var boss = new Aphantasia(1000, 1000, arena, new Random(439));
            boss.DebugSetPhase(phase);
            for (int advance = 0; advance < subphasesIn; advance++)
                boss.DebugAdvanceSubPhase();
            EnemyUpdateContext context = Context(boss, arena);

            for (int tick = 0; tick < Simulation.FrameRate * 2; tick++)
                boss.Update(context);

            List<EnemyProjectile> perimeter = context.ProjectileSink.Where(projectile =>
                projectile.Owner == "aphantasia_perimeter_drift").ToList();
            Assert.Equal(expectedCount, perimeter.Count);
            Assert.All(perimeter, projectile =>
            {
                Vector2 origin = projectile.OriginPoint - boss.ArenaCenter;
                Assert.InRange(origin.Length(), boss.ArenaRadius * .9f, boss.ArenaRadius);
                Assert.True(projectile.Lifetime > 12f);
            });
        }
    }

    [Fact]
    public void OrdinaryBossAndMiniShots_LiveLongEnoughToReachTheArenaEdge()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(443));
        boss.DebugSetPhase(1);
        SelectPattern(boss, "ordered_bloom");
        EnemyUpdateContext context = Context(boss, arena);

        for (int tick = 0; tick < Simulation.FrameRate * 2; tick++)
            boss.Update(context);

        EnemyProjectile bossShot = Assert.Single(context.ProjectileSink.Where(projectile =>
            projectile.Owner == "aphantasia_ordered_bloom_outer"
            && projectile.Path == "linear").Take(1));
        EnemyProjectile miniShot = Assert.Single(context.ProjectileSink.Where(projectile =>
            projectile.Owner?.StartsWith("aphantasia_mini_") == true
            && projectile.Path == "linear").Take(1));

        AssertShotReachesBoundary(bossShot, arena, boss.ArenaRadius);
        AssertShotReachesBoundary(miniShot, arena, boss.ArenaRadius);
    }

    private static void AssertShotReachesBoundary(EnemyProjectile projectile,
        Battleground arena, float arenaRadius)
    {
        float authoredRange = projectile.RemainingRange;
        Assert.True(authoredRange >= Simulation.TileSize);
        for (int tick = 0; tick < Simulation.FrameRate * 45 && !projectile.RemFlag; tick++)
            projectile.Update(arena, casualMode: true);

        Assert.True(projectile.RemFlag);
        Assert.True(projectile.Travelled >= authoredRange * .98f,
            $"Shot expired at {projectile.Travelled:0} of {authoredRange:0} authored pixels.");
        Assert.True(projectile.Lifetime >= authoredRange
            / Math.Max(.01f, projectile.Speed * .52f * (float)Simulation.ReferenceFps * .88f));
        Assert.True(projectile.Travelled >= arenaRadius * .25f);
    }

    [Fact]
    public void ObjectiveText_ExplainsDispositionDamageAndSurvivalGoals()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(401));
        EnemyUpdateContext context = Context(boss, arena);
        boss.DebugSetPhase(1);
        boss.DebugSetMiniState(AphantasiaMiniKind.Light, boss.Light.MaxHp,
            AphantasiaMiniDisposition.Aggressive);
        boss.DebugSetMiniState(AphantasiaMiniKind.Dark, boss.Dark.MaxHp,
            AphantasiaMiniDisposition.Aggressive);

        Assert.Contains("TRUE LIGHT", boss.ObjectiveText);
        Assert.Contains("BREAK BOTH", boss.ObjectiveText);

        OpenDamageWindow(boss, context);
        Assert.StartsWith("VULNERABLE", boss.ObjectiveText);

        boss.DebugSetPhase(2);
        boss.DebugStartSurvival();
        Assert.StartsWith("SURVIVE", boss.ObjectiveText);

        boss.DebugSetPhase(3);
        boss.DebugStartSurvival();
        Assert.StartsWith("DESTROY ONE", boss.ObjectiveText);
        AdvanceUntil(boss, context, () => boss.ObjectiveText == "CHOOSE NOW");
        Assert.Equal("CHOOSE NOW", boss.ObjectiveText);
    }

    [Fact]
    public void SurvivalAndFinaleSequences_UseTheirAuthoredStageOrder()
    {
        var cases = new[]
        {
            (Phase: 1, Finale: false, Labels: new[]
                { "ORDERED RINGS", "VERTICAL TIDES", "OPPOSING FANS", "CLOSING CROSS-WAVES" }),
            (Phase: 2, Finale: false, Labels: new[]
                { "BROKEN RINGS", "STAGGERED CURTAINS", "ERRATIC EXCHANGE", "ASYMMETRIC BRAID" }),
            (Phase: 3, Finale: false, Labels: new[]
                { "RADIANT LANES", "DARK CURLS", "DIVIDED HORIZON", "CHOOSE THE SURVIVOR" }),
            (Phase: 3, Finale: true, Labels: new[]
                { "PRISM BLOOM", "FOLDING VERTICAL", "FOLDING HORIZONTAL", "DANCING LATTICE", "TESSERACT CONVERGENCE" }),
            (Phase: 4, Finale: false, Labels: new[]
                { "PORTAL EQUINOX", "DRIFTING LATTICE", "FRACTURED CONSTELLATION", "VOID CONVERGENCE" }),
            (Phase: 4, Finale: true, Labels: new[]
                { "PORTAL CONSTELLATION", "NESTED VOID CLOCK", "PANE PROCESSION", "FOLDING PORTAL LATTICE", "PORTAL WAKE", "COLLAPSING TESSERACT" }),
        };

        foreach (var sequence in cases)
        {
            Battleground arena = MakeArena();
            var boss = new Aphantasia(1000, 1000, arena,
                new Random(500 + sequence.Phase), noHealing: true, noExtract: true);
            boss.DebugSetPhase(sequence.Phase);
            if (sequence.Finale)
                boss.DebugStartFinale();
            else
                boss.DebugStartSurvival();
            EnemyUpdateContext context = Context(boss, arena);
            var observed = new List<string>();
            int previousStage = -1;
            int maximumTicks = (int)Math.Ceiling(boss.SurvivalDuration * Simulation.FrameRate) + 12;

            for (int tick = 0; tick < maximumTicks; tick++)
            {
                boss.Update(context);
                context.ProjectileSink.Clear();
                if (boss.TransitionCleanupRequested)
                    boss.TransitionCleanupRequested = false;
                if (boss.SequenceStage != previousStage)
                {
                    previousStage = boss.SequenceStage;
                    observed.Add(boss.SequenceStageLabel);
                }
                if (boss.SurvivalRemaining <= 0)
                    break;
            }

            Assert.Equal(sequence.Labels, observed);
        }
    }

    [Fact]
    public void ProjectileBudget_IsFiveTimesLargerAndEvictsTheLongestLastingShot()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(601),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(4);
        SelectPattern(boss, "void_clock");
        EnemyUpdateContext context = Context(boss, arena);
        var longestLasting = new EnemyProjectile(
            boss.ArenaCenter.X, boss.ArenaCenter.Y, 0, 0, 1, 1,
            lifetime: 60, owner: "aphantasia_budget_oldest", ignoreWalls: true);
        for (int tick = 0; tick < Simulation.FrameRate; tick++)
            longestLasting.Update(arena, casualMode: false);
        context.ProjectileSink.Add(longestLasting);
        for (int index = 1; index < Aphantasia.ActiveThreatSoftCap; index++)
        {
            context.ProjectileSink.Add(new EnemyProjectile(
                boss.ArenaCenter.X, boss.ArenaCenter.Y, 0, 0, 1, 1, lifetime: 20,
                owner: "aphantasia_budget_filler", ignoreWalls: true));
        }

        for (int tick = 0; tick < Simulation.FrameRate * 2
            && context.ProjectileSink.Contains(longestLasting); tick++)
            boss.Update(context);

        Assert.Equal(5, Aphantasia.ProjectileCapacityMultiplier);
        Assert.Equal(1_600, Aphantasia.ActiveThreatSoftCap);
        Assert.DoesNotContain(longestLasting, context.ProjectileSink);
        int reserved = context.ProjectileSink.Sum(projectile =>
            Math.Max(1, projectile.ThreatReservationCost));
        Assert.InRange(reserved,
            Aphantasia.ActiveThreatSoftCap - Aphantasia.PerimeterThreatReserve,
            Aphantasia.ActiveThreatSoftCap);
    }

    [Fact]
    public void EveryPhaseKeepsBaselineShotsButSpecialAttacksAreSubphaseExclusive()
    {
        foreach (int phase in Enumerable.Range(1, 4))
        {
            IReadOnlyList<AphantasiaPattern> patterns = PatternsForPhase(phase);
            Assert.Contains(patterns, pattern =>
                pattern.SpecialAttack == AphantasiaSpecialAttack.DoubleHelix);
            Assert.Contains(patterns, pattern =>
                pattern.SpecialAttack == AphantasiaSpecialAttack.Laser);
            Assert.Contains(patterns, pattern =>
                pattern.SpecialAttack == AphantasiaSpecialAttack.Bomb);

            foreach (AphantasiaPattern pattern in patterns)
            {
                Battleground arena = MakeArena();
                var boss = new Aphantasia(1000, 1000, arena,
                    new Random(613 + phase * 100 + pattern.Key.Length),
                    noHealing: true, noExtract: true);
                boss.DebugSetPhase(phase);
                SelectPattern(boss, pattern.Key);
                EnemyUpdateContext context = Context(boss, arena);

                for (int tick = 0; tick < Simulation.FrameRate * 8; tick++)
                    boss.Update(context);

                Assert.Contains(context.ProjectileSink, projectile =>
                    projectile.Owner == "aphantasia_baseline_straight");
                Assert.Contains(context.ProjectileSink, projectile =>
                    projectile.Owner == "aphantasia_baseline_sine");
                Assert.Contains(context.ProjectileSink, projectile =>
                    projectile.Owner == "aphantasia_baseline_shotgun");
                foreach (string mini in phase < 4
                    ? new[] { "light", "dark" }
                    : Array.Empty<string>())
                {
                    List<EnemyProjectile> miniShots = context.ProjectileSink.Where(projectile =>
                        projectile.Owner == $"aphantasia_mini_{mini}").ToList();
                    Assert.Contains(miniShots, projectile => projectile.Path == "linear");
                    Assert.Contains(miniShots, projectile => projectile.Path == "sine");
                }
                Assert.DoesNotContain(context.ProjectileSink, projectile =>
                    projectile.Owner?.StartsWith("aphantasia_mini_") == true
                    && (projectile.Path == "laser" || projectile.Path == "bomb"));

                bool hasHelix = context.ProjectileSink.Any(projectile =>
                    projectile.Owner?.StartsWith("aphantasia_double_helix_") == true);
                bool hasLaser = context.ProjectileSink.Any(projectile =>
                    projectile.Owner?.StartsWith("aphantasia_laser_") == true);
                bool hasBomb = context.ProjectileSink.Any(projectile =>
                    projectile.Owner?.EndsWith("_bomb") == true);
                Assert.Equal(pattern.SpecialAttack == AphantasiaSpecialAttack.DoubleHelix,
                    hasHelix);
                Assert.Equal(pattern.SpecialAttack == AphantasiaSpecialAttack.Laser,
                    hasLaser);
                Assert.Equal(pattern.SpecialAttack == AphantasiaSpecialAttack.Bomb,
                    hasBomb);
            }
        }
    }

    [Fact]
    public void TesseractEight_FiresTelegraphedCapacityReservedRefractors()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(617));
        boss.DebugSetPhase(3);
        SelectPattern(boss, "tesseract_eight");
        EnemyUpdateContext context = Context(boss, arena);

        for (int tick = 0; tick < Simulation.FrameRate * 3; tick++)
            boss.Update(context);

        List<EnemyProjectile> refractors = context.ProjectileSink.Where(projectile =>
            projectile.Owner == "aphantasia_refractor").ToList();
        Assert.NotEmpty(refractors);
        Assert.All(refractors, projectile =>
        {
            Assert.Equal("star", projectile.Shape);
            Assert.Equal(3, projectile.SplitCount);
            Assert.Equal(3, projectile.ThreatReservationCost);
            Assert.Equal(.55f, projectile.SplitTelegraphStartRatio);
            Assert.NotNull(projectile.SplitAt);
            Assert.InRange(projectile.SplitAt!.Value / projectile.RemainingRange, .49f, .51f);
        });
    }

    [Fact]
    public void NestedVoidClock_FiresFiniteDeceleratingVoidAnchors()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(619));
        boss.DebugSetPhase(4);
        SelectPattern(boss, "void_clock");
        EnemyUpdateContext context = Context(boss, arena);

        for (int tick = 0; tick < Simulation.FrameRate * 3; tick++)
            boss.Update(context);

        EnemyProjectile anchor = Assert.Single(context.ProjectileSink.Where(projectile =>
            projectile.Owner == "aphantasia_void_anchor").Take(1));
        Assert.Equal("orbit_core", anchor.Shape);
        // 3.2s -- shorter than the ~4.2s it takes to decelerate to a stop,
        // by design (see FireVoidAnchor): the anchor is always still
        // drifting away when it expires instead of parking on the boss.
        Assert.Equal(3.2f, anchor.Lifetime);
        Assert.True(anchor.SpeedDecay > 0);
        float initialSpeed = anchor.Speed;
        for (int tick = 0; tick < Simulation.FrameRate * 6 && !anchor.RemFlag; tick++)
            anchor.Update(arena, casualMode: false);
        Assert.True(anchor.Speed < initialSpeed);
        Assert.True(anchor.RemFlag);
    }

    [Fact]
    public void CombatPhrases_PauseNewFireThenResumeWithAnAccentVolley()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(623));
        boss.DebugSetPhase(1);
        SelectPattern(boss, "ordered_bloom");
        EnemyUpdateContext context = Context(boss, arena);
        bool Breathing() => boss.SubPhaseElapsed >= Aphantasia.SubphaseDeclarationDuration
            + Aphantasia.CombatPhraseDuration - Aphantasia.CombatPhraseBreathDuration
            && boss.SubPhaseElapsed < Aphantasia.SubphaseDeclarationDuration
                + Aphantasia.CombatPhraseDuration;
        AdvanceUntil(boss, context, Breathing,
            Simulation.FrameRate * 8);
        context.ProjectileSink.Clear();

        for (int tick = 0; tick < Simulation.FrameRate * .4; tick++)
            boss.Update(context);

        Assert.True(Breathing());
        Assert.Empty(context.ProjectileSink);
        while (Breathing())
            boss.Update(context);

        Assert.Contains(context.ProjectileSink, projectile =>
            projectile.Owner?.Contains("order_accent_") == true);
    }

    [Fact]
    public void BossLasersProvideAOneAndAHalfSecondCollisionFreeIndicator()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(619));
        boss.DebugSetPhase(1);
        SelectPattern(boss, "horizon_ellipse");
        EnemyUpdateContext context = Context(boss, arena);

        for (int tick = 0; tick < Simulation.FrameRate * 5; tick++)
            boss.Update(context);

        List<EnemyProjectile> lasers = context.ProjectileSink.Where(projectile =>
            projectile.Owner?.StartsWith("aphantasia_laser_") == true).ToList();
        Assert.NotEmpty(lasers);
        Assert.All(lasers, laser => Assert.Equal(1.5f, laser.TelegraphDuration));
        EnemyProjectile warning = lasers[0];
        Assert.False(warning.Collides(new Rectangle(
            (int)warning.WorldX, (int)warning.WorldY, 20, 20)));
    }

    [Fact]
    public void LaserGridsOnlyAppearDuringPhaseThreeAndFourSurvivalSequences()
    {
        foreach (int phase in Enumerable.Range(1, 4))
        {
            Battleground arena = MakeArena();
            var boss = new Aphantasia(1000, 1000, arena, new Random(623 + phase),
                noHealing: true, noExtract: true);
            boss.DebugSetPhase(phase);
            EnemyUpdateContext context = Context(boss, arena);

            for (int tick = 0; tick < Simulation.FrameRate * 12; tick++)
                boss.Update(context);
            Assert.DoesNotContain(context.ProjectileSink, projectile =>
                projectile.Owner?.StartsWith("aphantasia_edge_grid_") == true);

            context.ProjectileSink.Clear();
            if (phase == 3)
                boss.DebugStartSurvival();
            else if (phase == 4)
                boss.DebugStartFinale();
            else
                continue;
            if (boss.PhaseHandoffActive)
                AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
            context.ProjectileSink.Clear();
            var emittedGrids = new List<EnemyProjectile>();
            for (int tick = 0; tick < Simulation.FrameRate * 20; tick++)
            {
                boss.Update(context);
                emittedGrids.AddRange(context.ProjectileSink.Where(projectile =>
                    projectile.Owner?.StartsWith("aphantasia_edge_grid_") == true));
                context.ProjectileSink.Clear();
            }

            foreach (string orientation in new[] { "cardinal", "anticardinal" })
            {
                List<EnemyProjectile> grid = emittedGrids.Where(projectile =>
                    projectile.Owner == $"aphantasia_edge_grid_{orientation}").ToList();
                Assert.True(grid.Count >= 8,
                    $"Phase {phase} produced {grid.Count} {orientation} grid lasers.");
                Assert.All(grid, projectile =>
                {
                    Assert.Equal("laser", projectile.Path);
                    Assert.True(Vector2.Distance(projectile.OriginPoint, boss.ArenaCenter)
                        >= boss.ArenaRadius * .75f);
                });
            }
        }
    }

    [Fact]
    public void PlayerRoll_ReachesAphantasiaArenaBoundary()
    {
        GameSession session = MakeSession();
        session.StartAphantasia(new Random(629));
        var boss = Assert.IsType<Aphantasia>(session.State.ActiveBoss);
        float playerSize = (float)session.State.PlayerSize;
        float limit = boss.ArenaRadius - playerSize * .72f;
        Vector2 startCenter = boss.ArenaCenter + Vector2.UnitX * (limit - 4f);
        session.Player.SetPosition(
            startCenter.X - playerSize / 2f,
            startCenter.Y - playerSize / 2f);
        float before = session.Player.WorldX;
        session.State.CurrDashCooldown = 0;

        session.MovePlayer(false, true, false, false, true);

        Vector2 playerCenter = new(
            session.Player.WorldX + playerSize / 2f,
            session.Player.WorldY + playerSize / 2f);
        Assert.True(session.State.Dashing);
        Assert.True(session.Player.WorldX > before);
        Assert.InRange(Vector2.Distance(playerCenter, boss.ArenaCenter),
            limit - .01f, limit + .01f);
    }

    [Fact]
    public void ArenaHalfPressure_IsDisabledEarlyAndRampsInAcrossEachPhasesOpening()
    {
        static List<EnemyProjectile> Simulate(int phase, int subphasesIn = 0)
        {
            Battleground arena = MakeArena();
            var boss = new Aphantasia(1000, 1000, arena, new Random(631));
            boss.DebugSetPhase(phase);
            for (int advance = 0; advance < subphasesIn; advance++)
                boss.DebugAdvanceSubPhase();
            EnemyUpdateContext context = Context(boss, arena);
            for (int tick = 0; tick < Simulation.FrameRate * 12; tick++)
                boss.Update(context);
            return context.ProjectileSink.Where(projectile =>
                projectile.Owner?.StartsWith("aphantasia_half_") == true).ToList();
        }

        Assert.Empty(Simulate(1));
        Assert.Empty(Simulate(2));

        // Each phase's opening subphase (subphase 0) eases the ambient
        // cadence in rather than jumping straight to its steady-state
        // multiplier -- Phase 3 opens slower than its own steady state, and
        // Phase 4 opens at Phase 3's steady state rather than its own full
        // speed, so the phase-3-vs-phase-4 gap is wider on subphase 0 than
        // it becomes once each phase has run a couple of subphases.
        List<EnemyProjectile> phaseThreeOpening = Simulate(3, subphasesIn: 0);
        List<EnemyProjectile> phaseFourOpening = Simulate(4, subphasesIn: 0);
        int phaseThreeOpeningVolleys = phaseThreeOpening.Select(p => p.Owner).Distinct().Count();
        int phaseFourOpeningVolleys = phaseFourOpening.Select(p => p.Owner).Distinct().Count();
        Assert.InRange(phaseThreeOpeningVolleys, 1, phaseFourOpeningVolleys);
        Assert.True(phaseThreeOpeningVolleys <= Math.Ceiling(phaseFourOpeningVolleys * .8),
            $"Expected phase-three's opening subphase to stay well below phase-four's opening cadence, got {phaseThreeOpeningVolleys} versus {phaseFourOpeningVolleys}.");

        List<EnemyProjectile> phaseThreeRamped = Simulate(3, subphasesIn: 2);
        List<EnemyProjectile> halfShots = Simulate(4, subphasesIn: 2);
        int phaseThreeRampedVolleys = phaseThreeRamped.Select(p => p.Owner).Distinct().Count();
        int phaseFourVolleys = halfShots.Select(projectile => projectile.Owner).Distinct().Count();
        Assert.InRange(phaseThreeRampedVolleys, 1, phaseFourVolleys);
        Assert.True(phaseThreeRampedVolleys <= Math.Ceiling(phaseFourVolleys * .65),
            $"Expected roughly half as many phase-three half-pressure volleys once ramped in, got {phaseThreeRampedVolleys} versus {phaseFourVolleys}.");
        Assert.Contains(halfShots, projectile => projectile.Owner?.StartsWith("aphantasia_half_0_") == true);
        Assert.Contains(halfShots, projectile => projectile.Owner?.StartsWith("aphantasia_half_1_") == true);
        Assert.True(halfShots.Select(projectile => projectile.Speed).Distinct().Count() >= 6);
        Assert.True(halfShots.Select(projectile => projectile.Size).Distinct().Count() >= 6);
        Assert.Contains(halfShots, projectile => projectile.Path == "linear");
        Assert.Contains(halfShots, projectile => projectile.Path == "sine"
            && projectile.Amplitude != 0 && projectile.Frequency != .035f);
        Assert.Contains(halfShots.GroupBy(projectile => projectile.Owner), volley => volley.Count() >= 3);
    }

    [Fact]
    public void SurvivalTimerCompletesAcrossTheThirtySecondPhase()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(641));
        boss.DebugSetPhase(3);
        boss.DebugStartSurvival();
        EnemyUpdateContext context = Context(boss, arena);

        Assert.Equal(30, boss.SurvivalDuration);
        Assert.Equal(0f, boss.SurvivalTimerProgress);
        for (int tick = 0; tick < Simulation.FrameRate * 15; tick++)
            boss.Update(context);

        Assert.InRange(boss.SurvivalTimerProgress, .49f, .51f);
    }

    [Fact]
    public void SurvivalClockAndPresentationWaitForSevenSecondHandoff()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(643));
        boss.DebugSetPhase(1);
        EnemyUpdateContext context = Context(boss, arena);
        Assert.True(boss.TakeDamage(boss.Light.MaxHp, "light").Applied);
        Assert.True(boss.TakeDamage(boss.Dark.MaxHp, "dark").Applied);
        Assert.True(DealDamageBelowTheCap(boss).Applied);
        context.ProjectileSink.Clear();

        Assert.Equal(AphantasiaEncounterState.Survival, boss.EncounterState);
        Assert.True(boss.PhaseHandoffActive);
        Assert.False(boss.PresentationSurvivalActive);
        Assert.Equal(Aphantasia.EarlySurvivalDuration, boss.SurvivalRemaining);
        for (int tick = 0; tick < (Aphantasia.PhaseHandoffDuration - .5)
            * Simulation.FrameRate; tick++)
            boss.Update(context);

        Assert.True(boss.PhaseHandoffActive);
        Assert.Equal(Aphantasia.EarlySurvivalDuration, boss.SurvivalRemaining);
        Assert.Empty(context.ProjectileSink);
        AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
        Assert.True(boss.PresentationSurvivalActive);
        Assert.Equal(Aphantasia.EarlySurvivalDuration, boss.SurvivalRemaining);

        boss.Update(context);

        Assert.True(boss.SurvivalRemaining < Aphantasia.EarlySurvivalDuration);
    }

    [Fact]
    public void SurvivalStageChanges_PreserveExistingProjectiles()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(617));
        boss.DebugSetPhase(1);
        boss.DebugStartSurvival();
        boss.TransitionCleanupRequested = false;
        EnemyUpdateContext context = Context(boss, arena);
        var persistent = new EnemyProjectile(
            boss.ArenaCenter.X, boss.ArenaCenter.Y, 0, 0, 1, 1,
            lifetime: 60, owner: "aphantasia_stage_persistence", ignoreWalls: true);
        context.ProjectileSink.Add(persistent);

        double firstStageDuration = Aphantasia.EarlySurvivalDuration / 4;
        for (int tick = 0; tick < (firstStageDuration + 1) * Simulation.FrameRate; tick++)
            boss.Update(context);

        Assert.True(boss.SequenceStage >= 1);
        Assert.False(boss.TransitionCleanupRequested);
        Assert.Contains(persistent, context.ProjectileSink);
    }

    [Fact]
    public void IndividualMiniDeath_DoesNotBeginPhaseHandoff()
    {
        Battleground arena = MakeArena();
        Aphantasia boss = Enumerable.Range(643, 200)
            .Select(seed =>
            {
                var candidate = new Aphantasia(1000, 1000, arena, new Random(seed));
                candidate.DebugSetPhase(1);
                return candidate;
            })
            .First(candidate => candidate.CurrentPattern.Key == "tidal_pursuit");
        EnemyUpdateContext context = Context(boss, arena);
        context.PlayerWorldX = boss.ArenaCenter.X + boss.ArenaRadius * .55f;
        for (int tick = 0; tick < Simulation.FrameRate * 4; tick++)
        {
            boss.Update(context);
            context.ProjectileSink.Clear();
        }
        Vector2 before = new(boss.WorldX + boss.Size / 2f,
            boss.WorldY + boss.Size / 2f);
        Assert.True(Vector2.Distance(before, boss.ArenaCenter)
            > Simulation.TileSize);
        var lingering = new EnemyProjectile(
            boss.ArenaCenter.X, boss.ArenaCenter.Y, 0, 0, 1, 1,
            lifetime: 60, owner: "aphantasia_handoff_lingering", ignoreWalls: true);
        context.ProjectileSink.Add(lingering);

        HitResult result = boss.TakeDamage(boss.Light.MaxHp, "light");

        Assert.True(result.Applied);
        Assert.False(boss.PhaseHandoffActive);
        Assert.False(boss.MilestoneHealRequested);
        Assert.False(boss.TransitionCleanupRequested);
        Assert.Contains(lingering, context.ProjectileSink);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PhaseCheckpoint_OnlyFullHealsInHardMode(
        bool hardMode, bool expectFullHealth)
    {
        GameSession session = MakeSession();
        session.State.SetHardMode(hardMode);
        session.StartAphantasia(new Random(647));
        session.State.PendingLevelUps = 0;
        var boss = Assert.IsType<Aphantasia>(session.State.ActiveBoss);
        boss.DebugSetPhase(1);
        session.State.HealthPoints = 1;
        var lingering = new EnemyProjectile(
            boss.ArenaCenter.X, boss.ArenaCenter.Y, 0, 0, 1, 1,
            lifetime: 60, owner: "aphantasia_session_lingering", ignoreWalls: true);
        session.State.EnemyProjectileHolster.Add(lingering);

        Assert.True(boss.TakeDamage(boss.Light.MaxHp, "light").Applied);
        Assert.True(boss.TakeDamage(boss.Dark.MaxHp, "dark").Applied);
        Assert.True(DealDamageBelowTheCap(boss).Applied);
        session.UpdateEnemies();

        Assert.Equal(expectFullHealth
            ? session.State.MaxHealthPoints : 1,
            session.State.HealthPoints);
        Assert.Contains(lingering, session.State.EnemyProjectileHolster);
        Assert.False(boss.MilestoneHealRequested);
    }

    [Fact]
    public void PhaseAndSubphaseHandoffs_RequestAProjectileSweep()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(643));
        boss.DebugSetPhase(1);
        EnemyUpdateContext context = Context(boss, arena);
        Assert.False(boss.TransitionSweepRequested);

        Assert.True(boss.TakeDamage(boss.Light.MaxHp, "light").Applied);
        Assert.True(boss.TakeDamage(boss.Dark.MaxHp, "dark").Applied);
        Assert.True(DealDamageBelowTheCap(boss).Applied);

        // The same handoff beat fires the sweep whether it closes out a
        // full phase or just an ordinary subphase -- BeginPhaseHandoff is
        // the single funnel for both, so a subphase's own gate proves the
        // request lands without needing a full phase transition.
        Assert.True(boss.PhaseHandoffActive);
        Assert.True(boss.TransitionSweepRequested);
    }

    [Fact]
    public void TransitionSweep_AcceleratesLingeringProjectilesUntilTheArenaBoundaryRemovesThem()
    {
        GameSession session = MakeSession();
        session.StartAphantasia(new Random(647));
        session.State.PendingLevelUps = 0;
        var boss = Assert.IsType<Aphantasia>(session.State.ActiveBoss);
        boss.DebugSetPhase(1);
        var lingering = new EnemyProjectile(
            boss.ArenaCenter.X, boss.ArenaCenter.Y, 0f, .3f, 1, 1,
            lifetime: 30, owner: "aphantasia_sweep_lingering", ignoreWalls: true);
        session.State.EnemyProjectileHolster.Add(lingering);
        Assert.Equal(0, lingering.Acceleration);

        Assert.True(boss.TakeDamage(boss.Light.MaxHp, "light").Applied);
        Assert.True(boss.TakeDamage(boss.Dark.MaxHp, "dark").Applied);
        Assert.True(DealDamageBelowTheCap(boss).Applied);
        session.UpdateEnemies();

        Assert.Contains(lingering, session.State.EnemyProjectileHolster);
        Assert.True(lingering.Acceleration > 0,
            "Expected the sweep to accelerate the lingering projectile.");

        for (int tick = 0; tick < Simulation.FrameRate * 8
            && session.State.EnemyProjectileHolster.Contains(lingering); tick++)
        {
            session.UpdateEnemies();
            session.UpdateEnemyProjectiles();
        }

        Assert.DoesNotContain(lingering, session.State.EnemyProjectileHolster);
    }

    [Fact]
    public void VoidClockVolley_ReservesItsCompletePortalWave()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(601),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(4);
        SelectPattern(boss, "void_clock");
        EnemyUpdateContext context = Context(boss, arena);

        // "void_clock" alternates every other volley between this portal
        // seed and a plain void anchor (see FireVoidStage case 1), and the
        // very first volley of a fresh subphase always lands on the anchor
        // branch (_regularVolleyCount starts at 0). This test is
        // specifically about the portal-seed branch's reserved threat cost,
        // so clear out any anchor volley that fires first and keep waiting
        // for the portal seed to show up.
        context.ProjectileSink.Clear();
        for (int tick = 0; tick < Simulation.FrameRate * 6
            && !context.ProjectileSink.Any(projectile =>
                projectile.Owner?.Contains("portal_clock_hand") == true); tick++)
        {
            boss.Update(context);
            if (!context.ProjectileSink.Any(projectile =>
                projectile.Owner?.Contains("portal_clock_hand") == true))
            {
                context.ProjectileSink.Clear();
            }
        }

        List<EnemyProjectile> clockVolley = context.ProjectileSink.Where(projectile =>
            projectile.Owner?.Contains("void_clock") == true
            || projectile.Owner?.Contains("portal_clock_hand") == true).ToList();
        Assert.Equal(13, clockVolley.Count);
        Assert.Equal(20, clockVolley.Sum(projectile => projectile.ThreatReservationCost));
        Assert.Single(clockVolley,
            projectile => projectile.Owner?.Contains("portal_clock_hand") == true);
    }

    [Fact]
    public void PortalSeeds_ReserveTheirRadialWaveAndEmitMovingFiniteChildren()
    {
        Battleground arena = MakeArena();
        var boss = new Aphantasia(1000, 1000, arena, new Random(607),
            noHealing: true, noExtract: true);
        boss.DebugSetPhase(4);
        SelectPattern(boss, "portal_constellation");
        EnemyUpdateContext context = Context(boss, arena);
        for (int tick = 0; tick < Simulation.FrameRate * 3
            && !context.ProjectileSink.Any(projectile =>
                projectile.Owner?.StartsWith("aphantasia_portal_") == true); tick++)
            boss.Update(context);
        EnemyProjectile portal = Assert.Single(context.ProjectileSink.Where(projectile =>
            projectile.Owner?.StartsWith("aphantasia_portal_") == true).Take(1));

        Assert.Equal(8, portal.ThreatReservationCost);
        Assert.Equal(.72f, portal.SplitTelegraphStartRatio);
        Assert.Equal(.82f, portal.SplitSpeedScale);
        Assert.Equal(32f, portal.SplitChildLifetime);
        float parentSpeed = portal.Speed;
        for (int tick = 0; tick < Simulation.FrameRate * 12 && !portal.Exploded; tick++)
            portal.Update(arena, casualMode: false);

        Assert.True(portal.Exploded);
        Assert.Equal(8, portal.SpawnedProjectiles.Count);
        Assert.All(portal.SpawnedProjectiles, child =>
        {
            Assert.Equal(parentSpeed * .82f, child.Speed, 3);
            Assert.True(child.Speed < parentSpeed);
            Assert.Equal(32f, child.Lifetime);
            Assert.Equal(1, child.ThreatReservationCost);
            Assert.False(child.PersistentHazard);
        });
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void AuthoredSequences_ThreatenCenterAndOuterPositionsWithoutOverflow(
        int phase, bool finale)
    {
        SequencePressure center = SimulateSequencePressure(phase, finale, 0f);
        SequencePressure outer = SimulateSequencePressure(phase, finale, .62f);

        Assert.InRange(center.Peak, 1, Aphantasia.ActiveThreatSoftCap);
        Assert.InRange(outer.Peak, 1, Aphantasia.ActiveThreatSoftCap);
        Assert.InRange(center.ReservedPeak, 1, Aphantasia.ActiveThreatSoftCap);
        Assert.InRange(outer.ReservedPeak, 1, Aphantasia.ActiveThreatSoftCap);
        Assert.True(center.Hits > 0,
            $"Phase {phase} sequence never threatened the stationary center player.");
        Assert.True(outer.Hits > 0,
            $"Phase {phase} sequence never threatened the stationary outer player.");
    }

    [Fact]
    public void EveryPattern_EmitsOnlyFiniteMovingProjectiles()
    {
        foreach (int phase in Enumerable.Range(1, 4))
        {
            Battleground arena = MakeArena();
            var boss = new Aphantasia(1000, 1000, arena, new Random(50 + phase),
                noHealing: true, noExtract: true);
            boss.DebugSetPhase(phase);
            var context = new EnemyUpdateContext
            {
                PlayerWorldX = boss.ArenaCenter.X + boss.ArenaRadius * .55f,
                PlayerWorldY = boss.ArenaCenter.Y,
                Battleground = arena,
            };
            var remaining = PatternsForPhase(phase)
                .Select(pattern => pattern.Key)
                .ToHashSet();

            for (int attempt = 0; attempt < 40 && remaining.Count > 0; attempt++)
            {
                string pattern = boss.CurrentPattern.Key;
                if (remaining.Contains(pattern))
                {
                    if (boss.PhaseHandoffActive)
                        AdvanceUntil(boss, context, () => !boss.PhaseHandoffActive);
                    context.ProjectileSink.Clear();
                    for (int tick = 0; tick < Simulation.FrameRate * 3
                        && !context.ProjectileSink.Any(projectile =>
                            projectile.Owner != "aphantasia_perimeter_drift"
                            && projectile.Owner?.StartsWith("aphantasia_half_") != true
                            && projectile.Owner?.StartsWith("aphantasia_double_helix_") != true); tick++)
                        boss.Update(context);

                    List<EnemyProjectile> patternProjectiles = context.ProjectileSink
                        .Where(projectile => projectile.Owner != "aphantasia_perimeter_drift"
                            && projectile.Owner?.StartsWith("aphantasia_half_") != true
                            && projectile.Owner?.StartsWith("aphantasia_double_helix_") != true)
                        .ToList();
                    Assert.NotEmpty(patternProjectiles);
                    Assert.DoesNotContain(patternProjectiles, projectile =>
                        projectile.Path is "pool" or "mine"
                        || projectile.Shape == "mine"
                        || projectile.PersistentHazard && projectile.Path != "laser");
                    Assert.All(patternProjectiles, projectile =>
                    {
                        Assert.True(float.IsFinite(projectile.RemainingRange),
                            $"{projectile.Owner} has range {projectile.RemainingRange}.");
                        Assert.True(projectile.Lifetime is > 0 and <= 40f,
                            $"{projectile.Owner} has lifetime {projectile.Lifetime}.");
                        Assert.True(projectile.Size >= Simulation.TileSize
                            * Aphantasia.MinimumProjectileSizeTiles,
                            $"{projectile.Owner} has size {projectile.Size}.");
                    });
                    if (phase == 4)
                        // "void_clock" alternates every other volley between
                        // a portal-seed clock hand and a plain void anchor
                        // (see FireVoidStage case 1) -- either one is valid
                        // evidence of the pattern's big hazard, but which
                        // one shows up here depends on _regularVolleyCount's
                        // parity when this pattern's first volley fires.
                        Assert.Contains(patternProjectiles, projectile =>
                            projectile.Owner?.StartsWith("aphantasia_portal_") == true
                            || projectile.Owner == "aphantasia_void_anchor");
                    remaining.Remove(pattern);
                }
                int previous = boss.PatternIndex;
                AdvanceUntil(boss, context, () => boss.PatternIndex != previous);
                Assert.NotEqual(previous, boss.PatternIndex);
            }

            Assert.Empty(remaining);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Entry_CapturesBothBraziersAndQueuesTheCompleteBuildDraft(
        bool noHealing, bool noExtract, bool phase4Eligible)
    {
        GameSession session = MakeSession();
        session.State.SetHardMode(noHealing);
        session.State.SetNoExtract(noExtract);

        session.StartAphantasia(new Random(11));

        Aphantasia boss = Assert.IsType<Aphantasia>(session.State.ActiveBoss);
        Assert.Equal(phase4Eligible, boss.PhaseFourEligible);
        Assert.Equal(noHealing, boss.CapturedNoHealing);
        Assert.Equal(noExtract, boss.CapturedNoExtract);
        Assert.Equal(noHealing, session.State.NoHealing);
        Assert.Equal(noExtract, session.State.NoExtract);
        Assert.Equal(Progression.MaxLevel, session.State.CurrentLevel);
        Assert.Equal(20, session.State.PendingLevelUps);
        Assert.True(session.AphantasiaPrecombatDraftsPending);
        Assert.False(session.State.EnemySpawningEnabled);
        Assert.Equal(CampaignActivity.Aphantasia, session.CampaignActivity);
        Assert.Equal("phantasia", session.CampaignActivitySense);
        Assert.Contains(boss, session.State.EnemyHolster);
    }

    [Fact]
    public void Entry_PreservesTheCurrentlyEquippedMindLoadout()
    {
        GameSession session = MakeSession();
        var weapon = new ItemDrop(
            Items.DefinitionsByName["Iron Dagger"],
            "Epic");
        session.State.Equipment["weapon"] = weapon;
        session.State.CombinePlayerStats();
        int damageWithWeapon = session.State.BulletDamage;

        session.StartAphantasia(new Random(13));

        Assert.Same(weapon, session.State.Equipment["weapon"]);
        Assert.Equal(damageWithWeapon, session.State.BulletDamage);
        Assert.Equal(Progression.MaxLevel, session.State.CurrentLevel);
        Assert.Equal(Progression.MaxLevel, session.State.PendingLevelUps);
    }

    [Fact]
    public void PrecombatDrafts_FreezeTheBossAndDoNotPurchaseExtraLevels()
    {
        GameSession session = MakeSession();
        session.StartAphantasia(new Random(17));
        Aphantasia boss = Assert.IsType<Aphantasia>(session.State.ActiveBoss);
        float initialAge = boss.VisualAgeSeconds;

        Assert.True(session.TryPurchaseLevelUp());
        Assert.Equal(Progression.MaxLevel, session.State.CurrentLevel);
        Assert.Equal(20, session.State.PendingLevelUps);

        session.UpdateEnemies();
        session.UpdateEnemyProjectiles();
        Assert.Equal(initialAge, boss.VisualAgeSeconds);
        Assert.Empty(session.State.EnemyProjectileHolster);

        session.State.PendingLevelUps = 0;
        Assert.False(session.AphantasiaPrecombatDraftsPending);
        session.UpdateEnemies();
        Assert.True(boss.VisualAgeSeconds > initialAge);
    }
}
