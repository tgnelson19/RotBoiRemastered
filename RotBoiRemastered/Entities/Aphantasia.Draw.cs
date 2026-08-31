using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// All of Aphantasia's rendering: the boss body, Minis, persistent arena,
/// floor/wall dressing, and every sigil/vortex/screen-mood effect.
/// Update/combat state lives in <see cref="Aphantasia"/>; attack
/// patterns live in Aphantasia.Attacks.cs.
/// </summary>
public sealed partial class Aphantasia
{
    public override void Draw(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 center = camera.WorldToScreen(
            new Vector2(WorldX + Size / 2f, WorldY + Size / 2f),
            playerWorldPosition, screenShake);
        if (EncounterState == AphantasiaEncounterState.Finale)
        {
            Vector2 arenaCenterScreen = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
            DrawUltimateGroundSigil(spriteBatch, arenaCenterScreen);
        }
        // All three transition tentacle bursts draw before the body so they
        // read as a shadowy explosion blooming out from behind the boss, not
        // a decal painted on top of it. DrawVoidTransition is independent of
        // the other two (it fires off a flip in VoidedBodyActive rather than
        // EncounterState) and guards its own no-op internally, so it draws
        // unconditionally rather than joining the if/else chain.
        if (PhaseHandoffActive)
            DrawPhaseHandoff(spriteBatch, center);
        else if (CombatDeclarationActive)
            DrawSubphaseDeclaration(spriteBatch, center);
        DrawVoidTransition(spriteBatch, center);
        DrawBossBody(spriteBatch, center);
        if (DamageWindowActive)
        {
            float pulse = .5f + .5f * MathF.Sin((float)_visualTime * 10f);
            Primitives2D.CircleOutline(spriteBatch, center,
                Size * (.66f + pulse * .1f), UiTheme.Ink, 9);
            Primitives2D.CircleOutline(spriteBatch, center,
                Size * (.66f + pulse * .1f), UiTheme.Cream, 4);
        }
        if (Phase < 4)
        {
            DrawMini(spriteBatch, camera, playerWorldPosition, screenShake, Light);
            DrawMini(spriteBatch, camera, playerWorldPosition, screenShake, Dark);
        }
        if (Dying)
            DrawDeath(spriteBatch, center);
    }

    private void DrawBossBody(SpriteBatch spriteBatch, Vector2 center)
    {
        DrawGroundShadow(spriteBatch, center, Size * .5f);
        float pulse = 1f + MathF.Sin((float)_visualTime * 2.1f) * .05f;
        Color glowColor = TrueLight ? new Color(88, 125, 228)
            : TrueDark ? new Color(8, 18, 65)
            : Phase >= 3 ? (VoidedBodyActive
                ? VoidTone((float)_visualTime * .07f)
                : Rainbow((float)_visualTime * .07f))
            : PhaseAccent;
        // Chasing and pathed patterns turn the body to face where it's
        // headed (see UpdateMovement); standing patterns and every
        // non-combat state (survival, handoffs, transformation) keep the
        // plain idle spin. Either way the orbiting decorations always use
        // `orbitSpin`, never `bodyYaw`, so their orbit and direction never
        // change with the body's facing.
        bool facingActive = EncounterState == AphantasiaEncounterState.Combat
            && CurrentPattern.Movement is AphantasiaMovementMode.Chase or AphantasiaMovementMode.Pathed;
        if (Phase <= 2)
        {
            float orbitSpin = (float)_visualTime * (Phase == 1 ? .82f : .38f);
            float bodyYaw = facingActive ? _facingYaw : orbitSpin;
            float bodyPitch = bodyYaw * .63f;
            Vector2[] cube = ProjectCube(center, Size * .42f * pulse, bodyYaw, bodyPitch);
            DrawOrbitingCubes(spriteBatch, center, orbitSpin, foreground: false);
            DrawFilledCube(spriteBatch, cube, new Color(3, 14, 58), PhaseAccent, bodyYaw, bodyPitch);
            DrawOrbitingCubes(spriteBatch, center, orbitSpin, foreground: true);
            if (facingActive)
                DrawFacingMarker(spriteBatch, center, Size * .42f * pulse, bodyYaw, bodyPitch);
        }
        else if (Phase == 3 || EncounterState == AphantasiaEncounterState.Transforming)
        {
            float orbitSpin = (float)_visualTime * .31f;
            float bodyYaw = facingActive ? _facingYaw : orbitSpin;
            float outerPitch = bodyYaw * .71f;
            // Phase 3's own four tentacles (none yet if this is a Phase 2 ->
            // 3 Transforming preview) and the transformation's own blooming
            // burst both draw first, behind every cube layer.
            DrawPersistentTentacles(spriteBatch, center);
            if (EncounterState == AphantasiaEncounterState.Transforming)
                DrawTransformationTentacles(spriteBatch, center, Size * .62f * pulse);
            Vector2[] outer = ProjectCube(center, Size * .62f * pulse, bodyYaw, outerPitch);
            Color outerFill = new(1, 1, 5, 235);
            // The inner cube genuinely nests inside the shell: the shell's
            // far side (facing away from camera, toward the floor) draws
            // first so the solid inner cube covers it, then its near side
            // (facing the camera) draws last, overlapping the inner cube.
            // edgeColor only matters to DrawWireCubeLayer when rainbow is
            // false -- passing it unconditionally here is inert (and simpler)
            // outside the voided window, since rainbow: true ignores it.
            DrawWireCubeLayer(spriteBatch, outer, rainbow: !VoidedBodyActive, outerFill,
                bodyYaw, outerPitch, front: false, edgeColor: VoidTone(orbitSpin * .08f));
            Vector2[] inner = ProjectCube(center, Size * .3f, -bodyYaw * .72f, bodyYaw * .43f);
            Color innerFill = VoidedBodyActive
                ? VoidTone(orbitSpin * .08f)
                : Rainbow(orbitSpin * .08f);
            DrawFilledCube(spriteBatch, inner, innerFill * .82f, UiTheme.Cream,
                -bodyYaw * .72f, bodyYaw * .43f);
            DrawWireCubeLayer(spriteBatch, outer, rainbow: !VoidedBodyActive, outerFill,
                bodyYaw, outerPitch, front: true, edgeColor: VoidTone(orbitSpin * .08f));
            if (EncounterState == AphantasiaEncounterState.Transforming)
                DrawTransformationSweep(spriteBatch, center, Size * .62f * pulse);
            if (facingActive)
                DrawFacingMarker(spriteBatch, center, Size * .62f * pulse, bodyYaw, outerPitch);
        }
        else
        {
            float orbitSpin = (float)_visualTime * .46f;
            float bodyYaw = facingActive ? _facingYaw : orbitSpin;
            float bodyPitch = bodyYaw * .6f;
            // Phase 4's eight tentacles draw first, behind the core and its
            // orbiting panes.
            DrawPersistentTentacles(spriteBatch, center);
            // Phase 4 is the true final form -- its border weight is bumped
            // noticeably past every earlier phase so the core reads heavier
            // and more final, not just another recolor of the same cube.
            // It's also real cube geometry now rather than a flat satellite
            // square, so it can pick up the same chase/pathed facing turn
            // the earlier phases do.
            Vector2[] core = ProjectCube(center, Size * .34f, bodyYaw, bodyPitch);
            Color coreFill = VoidedBodyActive
                ? VoidTone(orbitSpin * .08f)
                : Rainbow(orbitSpin * .08f);
            DrawFilledCube(spriteBatch, core, coreFill, UiTheme.Cream,
                bodyYaw, bodyPitch, inkWidth: 8, accentWidth: 4);
            if (facingActive)
                DrawFacingMarker(spriteBatch, center, Size * .34f, bodyYaw, bodyPitch);
            for (int index = 0; index < 6; index++)
            {
                float angle = orbitSpin * (index % 2 == 0 ? 1f : -.72f) + index * MathF.Tau / 6f;
                float radius = Size * (.55f + .16f * MathF.Sin(orbitSpin * 1.7f + index));
                Vector2 pane = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                float half = Size * (.16f + index % 2 * .04f);
                float tumbleYaw = orbitSpin * 1.4f + index * 1.3f;
                float tumblePitch = orbitSpin * .9f + index * .8f;
                Color edge = VoidedBodyActive
                    ? VoidTone(index / 6f + orbitSpin * .05f)
                    : Rainbow(index / 6f + orbitSpin * .05f);
                Vector2[] paneCube = ProjectCube(pane, half, tumbleYaw, tumblePitch);
                DrawFilledCube(spriteBatch, paneCube, edge, UiTheme.Cream, tumbleYaw, tumblePitch,
                    inkWidth: 4, accentWidth: 2);
            }
        }
        DrawRimGlow(spriteBatch, center, Size * .5f, Size * .84f, glowColor, hot: Phase >= 3);
        if (SurvivalKind is AphantasiaSurvivalKind.GrandChoice
            or AphantasiaSurvivalKind.VoidEclipse or AphantasiaSurvivalKind.VoidFinale)
            DrawSurvivalTentacles(spriteBatch, center);
    }

    /// <summary>
    /// Large flowing tentacles (same technique as the transformation's,
    /// and as the Aphantasia portal in The Mind) circling the core through
    /// the Phase 3 and Phase 4 survival sub-phases -- ambient spectacle,
    /// not a hazard; the actual attacks are the projectiles.
    /// </summary>
    private void DrawSurvivalTentacles(SpriteBatch spriteBatch, Vector2 center)
    {
        const int spikeCount = 7;
        float targetLength = ArenaRadius * .2f;
        float spin = (float)_visualTime * .22f;
        for (int index = 0; index < spikeCount; index++)
        {
            float baseAngle = index * MathF.Tau / spikeCount + spin;
            float length = targetLength * (.82f + .18f * MathF.Sin((float)_visualTime * 1.1f + index));
            float width = targetLength * .1f;
            DrawTentacleSpikeWithTrail(spriteBatch, center, baseAngle, length, width,
                phase: index * 1.9f, colorPhase: index / (float)spikeCount, segments: 40);
        }
    }

    private void DrawOrbitingCubes(SpriteBatch spriteBatch, Vector2 center,
        float spin, bool foreground)
    {
        const int satellites = 6;
        for (int index = 0; index < satellites; index++)
        {
            float angle = spin * (Phase == 1 ? 1f : .72f)
                + index * MathF.Tau / satellites;
            bool isForeground = MathF.Sin(angle) >= 0;
            if (isForeground != foreground)
                continue;
            float erratic = Phase == 2
                ? MathF.Sin((float)_visualTime * 1.9f + index * 2.3f) * Size * .18f
                : 0;
            Vector2 at = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .34f)
                * (Size * .78f + erratic);
            // Satellites swinging behind the core sit smaller, dimmer, and
            // thinner-bordered than the ones swinging in front, so the orbit
            // reads as passing through the body instead of two flat rings.
            // Each one tumbles on its own axis (offset by index) rather than
            // all six spinning in lockstep.
            float depth = foreground ? 1f : .8f;
            float alpha = foreground ? 1f : .72f;
            float tumbleYaw = (float)_visualTime * 1.6f + index * 1.1f;
            float tumblePitch = (float)_visualTime * 1.1f + index * .7f;
            Color tint = Rainbow(index / (float)satellites + spin * .04f) * alpha;
            Vector2[] cube = ProjectCube(at, Size * .1f * depth, tumbleYaw, tumblePitch);
            DrawFilledCube(spriteBatch, cube, tint, UiTheme.Cream * alpha, tumbleYaw, tumblePitch,
                inkWidth: foreground ? 4 : 2, accentWidth: foreground ? 2 : 1);
        }
    }

    private void DrawMini(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake, AphantasiaMini mini)
    {
        if (!mini.Alive)
            return;
        Vector2 center = camera.WorldToScreen(mini.Position, playerWorldPosition, screenShake);
        float radius = MiniSize * .45f * (mini.Empowered ? 1.12f : 1f);
        DrawGroundShadow(spriteBatch, center, radius * 1.35f);
        float handedness = ReferenceEquals(mini, Light) ? 1f : -1f;
        float spin = (float)_visualTime * (mini.Aggressive ? 1.8f : .72f) * handedness;

        // The body hovers above its own ground shadow instead of sitting
        // pinned to it -- every status readout below still anchors to the
        // true ground point at `center`.
        float bob = MathF.Sin((float)_visualTime * 2.4f + handedness) * radius * .16f;
        Vector2 bodyCenter = center + new Vector2(0, -bob - radius * .1f);
        float pitch = spin * .63f;
        Vector2[] cube = ProjectCube(bodyCenter, radius, spin, pitch);

        // Light is a solid, opaque shard; Dark is a hollow void-glass shell
        // -- "solid light vs. hollow shadow" told through construction, not
        // just color, while both still tumble from the same cube geometry
        // the boss body itself is built from.
        if (ReferenceEquals(mini, Light))
        {
            DrawFilledCube(spriteBatch, cube, mini.Accent, UiTheme.Cream, spin, pitch,
                inkWidth: mini.Empowered ? 6 : 4, accentWidth: mini.Empowered ? 3 : 2);
        }
        else
        {
            DrawWireCube(spriteBatch, cube, rainbow: false, fill: mini.Accent, spin, pitch,
                edgeColor: Color.Lerp(mini.Accent, UiTheme.Cream, .3f));
        }

        if (mini.Empowered)
        {
            // The survivor now visibly carries a fragment of the twin it
            // destroyed: a small hollow shell in the absorbed mini's color,
            // tumbling counter to the outer shell.
            AphantasiaMini absorbed = ReferenceEquals(mini, Light) ? Dark : Light;
            float innerYaw = -spin * 1.4f;
            float innerPitch = -pitch * 1.4f;
            Vector2[] innerCube = ProjectCube(bodyCenter, radius * .42f, innerYaw, innerPitch);
            DrawWireCube(spriteBatch, innerCube, rainbow: false, fill: absorbed.Accent,
                innerYaw, innerPitch, edgeColor: Color.Lerp(absorbed.Accent, UiTheme.Cream, .3f));
        }

        float glyphRadius = radius * 1.28f;
        if (!mini.Vulnerable)
        {
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius, UiTheme.Ink, 7);
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius, mini.Accent * .78f, 3);
        }
        else
        {
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius,
                UiTheme.Cream * (.48f + .2f * MathF.Sin((float)_visualTime * 5f)), 2);
        }

        if (mini.Aggressive)
        {
            for (int index = 0; index < 4; index++)
            {
                float angle = spin + index * MathF.PI / 2f;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                Primitives2D.Line(spriteBatch,
                    center + direction * glyphRadius * 1.04f,
                    center + direction * glyphRadius * 1.27f,
                    mini.Empowered ? UiTheme.Cream : mini.Accent,
                    mini.Empowered ? 4 : 2);
            }
        }
        else
        {
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius * 1.14f,
                mini.Accent * .52f, 2);
        }

        if (mini.Empowered)
        {
            float empoweredPulse = .5f + .5f * MathF.Sin((float)_visualTime * 7f);
            // Only reachable during Phase 3's MiniExecution window (Phase 4
            // never draws minis -- see Draw()'s `if (Phase < 4)` guard), so
            // VoidedBodyActive here really is just the Phase 3 voided check.
            Color empoweredGlow = VoidedBodyActive
                ? VoidTone((float)_visualTime * .1f)
                : Rainbow((float)_visualTime * .1f);
            Primitives2D.CircleOutline(spriteBatch, center,
                glyphRadius * (1.28f + empoweredPulse * .12f),
                empoweredGlow, 4);
            DrawRimGlow(spriteBatch, center, glyphRadius * 1.4f, glyphRadius * 2.1f,
                empoweredGlow, hot: true);
        }

        if (mini.FireCooldown <= .18f)
        {
            float warning = 1f - Math.Clamp(mini.FireCooldown / .18f, 0f, 1f);
            Primitives2D.CircleOutline(spriteBatch, center,
                glyphRadius * (1.58f - warning * .3f),
                UiTheme.Cream * (.35f + warning * .65f), 3);
        }
        var bar = new Rectangle((int)(center.X - radius), (int)(center.Y - radius - 12),
            Math.Max(8, (int)(radius * 2)), 5);
        UiTheme.DrawProgress(spriteBatch, bar, mini.HealthRatio, mini.Accent, 8);
    }

    /// <summary>Applies the cube's yaw/pitch rig to one direction, shared by vertex and face-normal transforms.</summary>
    private static Vector3 RotateYawPitch(Vector3 value, float yaw, float pitch)
    {
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        float rx = value.X * cy + value.Z * sy;
        float rz = -value.X * sy + value.Z * cy;
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        float ry = value.Y * cp - rz * sp;
        rz = value.Y * sp + rz * cp;
        return new Vector3(rx, ry, rz);
    }

    private static Vector2[] ProjectCube(Vector2 center, float extent, float yaw, float pitch)
    {
        var result = new Vector2[8];
        for (int index = 0; index < 8; index++)
        {
            float x = (index & 1) == 0 ? -1 : 1;
            float y = (index & 2) == 0 ? -1 : 1;
            float z = (index & 4) == 0 ? -1 : 1;
            Vector3 rotated = RotateYawPitch(new Vector3(x, y, z), yaw, pitch);
            float perspective = 1f + rotated.Z * .12f;
            result[index] = center + new Vector2(rotated.X, rotated.Y) * extent * perspective;
        }
        return result;
    }

    private static Vector3 RotatedFaceNormal(int faceIndex, float yaw, float pitch) =>
        RotateYawPitch(CubeFaceNormals[faceIndex], yaw, pitch);

    /// <summary>Rotated depth of one cube vertex (index encoded the same way as <see cref="ProjectCube"/>). Positive is toward the camera.</summary>
    private static float CubeVertexDepth(int vertexIndex, float yaw, float pitch)
    {
        float x = (vertexIndex & 1) == 0 ? -1 : 1;
        float y = (vertexIndex & 2) == 0 ? -1 : 1;
        float z = (vertexIndex & 4) == 0 ? -1 : 1;
        return RotateYawPitch(new Vector3(x, y, z), yaw, pitch).Z;
    }

    /// <summary>
    /// Brightness for one cube face against a fixed upper-left key light, kept
    /// in a moderate [.5, 1] band on purpose -- unlit faces stay readable
    /// instead of crushing to black, matching the fight's general preference
    /// for depth conveyed through color/intensity rather than heavy shadow.
    /// </summary>
    private static float FaceLight(int faceIndex, float yaw, float pitch)
    {
        float lit = Vector3.Dot(RotatedFaceNormal(faceIndex, yaw, pitch), CubeLightDirection);
        return .5f + .5f * Math.Clamp(lit, 0f, 1f);
    }

    private static void DrawFilledCube(SpriteBatch spriteBatch, Vector2[] points,
        Color fill, Color edge, float yaw, float pitch, int inkWidth = 7, int accentWidth = 3)
    {
        for (int index = 0; index < CubeFaces.Length; index++)
        {
            int[] face = CubeFaces[index];
            float light = FaceLight(index, yaw, pitch);
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], fill * (light * .8f));
        }
        foreach (int[] pair in CubeEdges)
        {
            Primitives2D.Line(spriteBatch, points[pair[0]], points[pair[1]], UiTheme.Ink, inkWidth);
            Primitives2D.Line(spriteBatch, points[pair[0]], points[pair[1]], edge, accentWidth);
        }
    }

    private static void DrawWireCube(SpriteBatch spriteBatch, Vector2[] points,
        bool rainbow, Color fill, float yaw, float pitch, Color? edgeColor = null)
    {
        DrawWireCubeLayer(spriteBatch, points, rainbow, fill, yaw, pitch, front: false, edgeColor);
        DrawWireCubeLayer(spriteBatch, points, rainbow, fill, yaw, pitch, front: true, edgeColor);
    }

    /// <summary>
    /// Half of a wire cube's faces/edges -- whichever half is on the near
    /// (front, toward camera) or far (back, toward the floor) side of the
    /// cube, judged by the same rotated Z depth <see cref="ProjectCube"/>
    /// already uses for its perspective scale. <see cref="DrawWireCube"/>
    /// draws both halves back-then-front for its own correct self-occlusion;
    /// Phase 3's nested cube calls the two halves directly so it can sandwich
    /// the inner solid cube between them -- the shell's far side sits behind
    /// the solid, its near side sits in front of it.
    /// </summary>
    private static void DrawWireCubeLayer(SpriteBatch spriteBatch, Vector2[] points,
        bool rainbow, Color fill, float yaw, float pitch, bool front, Color? edgeColor = null)
    {
        for (int index = 0; index < CubeFaces.Length; index++)
        {
            bool faceFront = RotatedFaceNormal(index, yaw, pitch).Z > 0f;
            if (faceFront != front)
                continue;
            int[] face = CubeFaces[index];
            float light = FaceLight(index, yaw, pitch);
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], fill * (light * .3f));
        }
        for (int index = 0; index < CubeEdges.Length; index++)
        {
            int[] edge = CubeEdges[index];
            float depth = (CubeVertexDepth(edge[0], yaw, pitch)
                + CubeVertexDepth(edge[1], yaw, pitch)) * .5f;
            if ((depth > 0f) != front)
                continue;
            Color color = rainbow ? Rainbow(index / (float)CubeEdges.Length) : edgeColor ?? UiTheme.Purple;
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], UiTheme.Ink, 8);
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], color, 3);
        }
    }

    /// <summary>
    /// A small bright point marking the cube's local "front" (the +Z face's
    /// outward normal), projected the same way <see cref="ProjectCube"/>
    /// projects vertices. Only drawn while <c>facingActive</c>, so it swings
    /// to track the player during Chase and the travel direction during
    /// Pathed -- a concrete tell for the facing turn beyond the subtler
    /// lighting shift <see cref="FaceLight"/> already gives it.
    /// </summary>
    private void DrawFacingMarker(SpriteBatch spriteBatch, Vector2 center,
        float extent, float yaw, float pitch)
    {
        Vector3 rotatedFront = RotateYawPitch(new Vector3(0, 0, 1), yaw, pitch);
        float perspective = 1f + rotatedFront.Z * .12f;
        Vector2 tip = center + new Vector2(rotatedFront.X, rotatedFront.Y)
            * extent * perspective * 1.22f;
        float pulse = .7f + .3f * MathF.Sin((float)_visualTime * 8f);
        float dotRadius = Math.Max(3f, extent * .09f) * pulse;
        Primitives2D.FillCircle(spriteBatch, tip + new Vector2(2, 3), dotRadius, UiTheme.Shadow);
        Primitives2D.FillCircle(spriteBatch, tip, dotRadius, UiTheme.Cream);
        Primitives2D.CircleOutline(spriteBatch, tip, Math.Max(4f, extent * .13f), UiTheme.Ink, 2);
    }

    /// <summary>
    /// One representative sigil from each earlier boss family Aphantasia has
    /// absorbed by the finale, pulled directly from that boss's own stroke
    /// data rather than re-authored -- Rot's Root Ward sits out of this set
    /// and anchors <see cref="DrawUltimateGroundSigil"/>'s center instead,
    /// since none of the other families is itself centered on the arena.
    /// </summary>
    private static readonly (string BossName, Vector2[][] Strokes)[] UltimateSigilSet =
    {
        ("DISSONANCE", Dissonance.PhaseRunes[1].Strokes),
        ("HYPNO & MALADY", PhantasiaBoss.CommandmentSigils[0].Strokes),
        ("KAGE", Kage.KageSinConfig.SinSigils[0].Strokes),
        ("ACHE", Ache.AcheSinConfig.SinSigils[0].Strokes),
        ("BAIR, STING & TOUCH", PlagueSigils.All[0].Strokes),
        ("ISHE & CHRONOS", Ishe.SightSymbols["GLIMPSE"].Strokes),
    };

    /// <summary>
    /// The fight's final spectacle, reserved for the Finale survival window:
    /// every earlier boss's ground sigil, repeated at a far larger scale
    /// than any of the originals and arranged into one combined array
    /// spanning the arena. Colored with the cycling Rainbow palette instead
    /// of any single boss's accent (Aphantasia has absorbed them all by
    /// this point) and kept deliberately dull against the ground -- low
    /// alpha throughout -- so it never competes with incoming shots for
    /// visibility.
    /// </summary>
    private void DrawUltimateGroundSigil(SpriteBatch spriteBatch, Vector2 center)
    {
        float ringRadius = ArenaRadius * .95f;
        float sigilRadius = ArenaRadius * .17f;
        float spin = (float)_visualTime * .025f;

        Primitives2D.DrawGroundSigilRing(spriteBatch, center, ringRadius * 2f, ringRadius * .82f,
            Rainbow(spin * .3f), UiTheme.Shadow, UiTheme.Void, (float)_visualTime, alpha: .3f, tickCount: 24);

        for (int index = 0; index < UltimateSigilSet.Length; index++)
        {
            float angle = spin + index * MathF.Tau / UltimateSigilSet.Length;
            Vector2 placement = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .58f) * ringRadius * .78f;
            Color accent = Rainbow(index / (float)UltimateSigilSet.Length + spin * .4f) * .32f;
            DrawUltimateSigilCopy(spriteBatch, UltimateSigilSet[index].Strokes, placement, sigilRadius, angle,
                accent, UiTheme.Void * .32f, UiTheme.Cream * .3f);
        }

        // The largest copy, dead center, of the one family that sits out of
        // the ring -- what everything else orbits.
        DrawUltimateSigilCopy(spriteBatch, Rot.RootWardSigil.Strokes, center, sigilRadius * 1.3f, -spin * .6f,
            Rainbow(spin * .6f) * .34f, UiTheme.Void * .34f, UiTheme.Cream * .32f);
    }

    private static void DrawUltimateSigilCopy(SpriteBatch spriteBatch, Vector2[][] strokes, Vector2 center,
        float radius, float rotation, Color accent, Color voidTone, Color highlight)
    {
        float cosAngle = MathF.Cos(rotation), sinAngle = MathF.Sin(rotation);
        int lineWidth = Math.Max(2, (int)(radius * .07f));
        foreach (var stroke in strokes)
        {
            var points = stroke.Select(p =>
            {
                float x = p.X * radius, y = p.Y * radius;
                return center + new Vector2(x * cosAngle - y * sinAngle, x * sinAngle + y * cosAngle);
            }).ToArray();
            if (points.Length <= 1)
                continue;
            Primitives2D.DrawGlyphDepthLayers(spriteBatch, points, center, accent, voidTone, lineWidth, 0f);
            Primitives2D.Polyline(spriteBatch, points, false, voidTone, Math.Max(4, (int)(radius * .12f)));
            Primitives2D.Polyline(spriteBatch, points, false, accent, lineWidth);
            Primitives2D.Polyline(spriteBatch, points, false, highlight, 1);
        }
    }

    /// <summary>
    /// Soft ground-contact shadow: a flattened dark ellipse beneath an
    /// entity, offset down like every other shadow in the game (Player,
    /// ProjectileVisuals, the laser origin telegraph). Kept translucent
    /// rather than solid black so it reads as depth, not a hole in the floor.
    /// </summary>
    private static void DrawGroundShadow(SpriteBatch spriteBatch, Vector2 center,
        float radius, float alpha = 1f)
    {
        var rect = new Rectangle(
            (int)(center.X - radius + radius * .05f),
            (int)(center.Y - radius * .38f + radius * .16f),
            (int)(radius * 2f),
            (int)(radius * .76f));
        Primitives2D.FillEllipse(spriteBatch, rect, UiTheme.Shadow * (.55f * alpha));
    }

    /// <summary>
    /// A soft outward glow -- a handful of widening, fading ring outlines --
    /// used to sell the boss core and empowered minis as light sources
    /// against the darkened arena, rather than flat cutouts. Rainbow is
    /// reserved for this fight's highest-stakes moments (Phase 3+, empowered
    /// minis), so <paramref name="hot"/> gives those a wider, brighter bloom
    /// than the plain phase-accent glow -- color intensity standing in for
    /// urgency rather than more geometry or darker shading.
    /// </summary>
    private static void DrawRimGlow(SpriteBatch spriteBatch, Vector2 center,
        float innerRadius, float outerRadius, Color color, bool hot = false)
    {
        int rings = hot ? 6 : 4;
        float reach = hot ? innerRadius + (outerRadius - innerRadius) * 1.2f : outerRadius;
        float alphaScale = hot ? .38f : .3f;
        for (int index = 0; index < rings; index++)
        {
            float t = (index + 1) / (float)rings;
            float radius = MathF.Sin(t * MathF.PI / 2f) * (reach - innerRadius) + innerRadius;
            float alpha = (1f - t) * alphaScale;
            Primitives2D.CircleOutline(spriteBatch, center, radius, color * alpha,
                Math.Max(2, (int)((reach - innerRadius) * .16f)), 32);
        }
    }

    /// <summary>
    /// A single bright point sweeping one and a half laps around the cube
    /// over the Phase 3 -> 4 transformation, trailing a short rainbow arc.
    /// Purely decorative -- it sells "becoming" as the tesseract cube swap
    /// happens, rather than the swap just snapping between two states.
    /// </summary>
    private void DrawTransformationSweep(SpriteBatch spriteBatch, Vector2 center, float extent)
    {
        float progress = 1f - (float)(_transitionRemaining / TesseractTransitionDuration);
        float sweepAngle = progress * MathF.Tau * 1.5f;
        float radius = extent * 1.35f;
        Color sweepColor = Rainbow(progress * .6f);
        Primitives2D.Arc(spriteBatch,
            new Rectangle((int)(center.X - radius), (int)(center.Y - radius),
                (int)(radius * 2), (int)(radius * 2)),
            sweepAngle - .6f, sweepAngle,
            sweepColor, Math.Max(2, (int)(extent * .05f)), 40);
        Vector2 head = center + new Vector2(MathF.Cos(sweepAngle), MathF.Sin(sweepAngle)) * radius;
        Primitives2D.FillCircle(spriteBatch, head, Math.Max(3f, extent * .07f), UiTheme.Cream);
    }

    /// <summary>
    /// The shared shadowy black/white/rainbow tentacle burst
    /// (<see cref="DrawTentacleBurst"/>), blooming out from the cube and
    /// resolving back to nothing over the transformation, rather than
    /// staying at full length throughout -- energy crackling as the
    /// tesseract remakes itself, not a plain hold. Covers both the Phase 2
    /// -> 3 and Phase 3 -> 4 transitions, since both share this same
    /// Transforming encounter state.
    /// </summary>
    private void DrawTransformationTentacles(SpriteBatch spriteBatch, Vector2 center, float extent)
    {
        float progress = Math.Clamp(
            1f - (float)(_transitionRemaining / TesseractTransitionDuration), 0f, 1f);
        float bloom = MathF.Sin(progress * MathF.PI);
        DrawTentacleBurst(spriteBatch, center, 9, ArenaRadius * .2f, bloom, progress * .6f);
    }

    /// <summary>
    /// The dark-themed burst marking the body flipping into or out of its
    /// voided monochrome look (see <see cref="VoidedBodyActive"/> and
    /// <see cref="VoidTone"/>) at the start/end of Phase 3 and Phase 4's
    /// survival windows -- the same shared tentacle-burst technique as
    /// <see cref="DrawTransformationTentacles"/> and
    /// <see cref="DrawPhaseHandoff"/>, driven by
    /// <see cref="_voidTransitionRemaining"/> instead of either of those
    /// timers since this one fires far more often per fight, and forced
    /// strictly black/white (<c>voidOnly: true</c>) rather than the usual
    /// black/white/rainbow cycle so it never reads as "another rainbow
    /// flash" the instant before the body actually goes monochrome.
    /// Mirrors <see cref="DrawTransformationTentacles"/>'s exact
    /// grow-then-fade <c>Sin(progress * PI)</c> envelope -- invisible at
    /// both ends of the transition regardless of direction -- rather than a
    /// one-way linear fade, so it never pops discontinuously the instant
    /// <see cref="_voidTransitionRemaining"/> hits zero.
    /// <see cref="_voidTransitionEntering"/> doesn't reshape that envelope;
    /// it only leans the black/white alternation's phase one way for a flip
    /// into the voided look and the other way for a flip back out, a small
    /// free tell for which direction the flip is going.
    /// </summary>
    private void DrawVoidTransition(SpriteBatch spriteBatch, Vector2 center)
    {
        if (_voidTransitionRemaining <= 0)
            return;
        float progress = Math.Clamp(
            1f - (float)(_voidTransitionRemaining / VoidTransitionDuration), 0f, 1f);
        float bloom = MathF.Sin(progress * MathF.PI);
        float colorSeed = _voidTransitionEntering ? progress * .6f : 1f - progress * .6f;
        DrawTentacleBurst(spriteBatch, center, 9, ArenaRadius * .2f, bloom, colorSeed,
            voidOnly: true);
    }

    /// <summary>
    /// A tentacle spike with trailing after-image echoes -- darkened,
    /// fading copies of itself evaluated at slightly earlier moments in
    /// time, exactly like the Aphantasia portal decoration in The Mind
    /// (same routine, same technique). Every point on a spike is a pure
    /// function of time, so "what it looked like 50ms ago" is just this
    /// same call re-evaluated at time - .05 with some darken and reduced
    /// alpha -- no history buffer needed. The echo alpha is real
    /// transparency, not just a darker hue: without it, a handful of fully
    /// opaque echoes of a fast wiggle interfere into a rigid, ladder-like
    /// pattern instead of blending into a soft trail (this bit the portal
    /// version before the fix landed there).
    /// </summary>
    private void DrawTentacleSpikeWithTrail(SpriteBatch spriteBatch, Vector2 center,
        float baseAngle, float length, float width, float phase, float colorPhase,
        int segments = 22, int echoCount = 6, float echoDelay = .08f, Color? themeColor = null)
    {
        float time = (float)_visualTime;
        for (int echo = echoCount; echo >= 1; echo--)
        {
            float t = echo / (float)(echoCount + 1);
            Primitives2D.DrawTentacleSpike(spriteBatch, center, baseAngle, length, width,
                phase, colorPhase, time - echo * echoDelay, segments,
                darken: t, alpha: 1f - t * .85f, themeColor: themeColor);
        }
        Primitives2D.DrawTentacleSpike(spriteBatch, center, baseAngle, length, width,
            phase, colorPhase, time, segments, themeColor: themeColor);
    }

    /// <summary>
    /// Cycles every third tentacle through black, white, and the usual
    /// rainbow cycle (<c>null</c> tells <see cref="Primitives2D.DrawTentacleSpike"/>
    /// to use its own <see cref="Rainbow"/> stroke) -- the "shadowy explosion
    /// of black, white, and rainbow tentacles" look shared by the transition
    /// bursts (<see cref="DrawPhaseHandoff"/>, <see cref="DrawSubphaseDeclaration"/>,
    /// <see cref="DrawTransformationTentacles"/>) and Phase 3/4's persistent
    /// body tentacles (<see cref="DrawPersistentTentacles"/>).
    /// </summary>
    private static Color? TentacleThemeColor(int index) => (index % 3) switch
    {
        0 => new Color(14, 12, 20),
        1 => new Color(230, 226, 238),
        _ => null,
    };

    /// <summary>
    /// <see cref="TentacleThemeColor"/>'s void-only counterpart for
    /// <see cref="DrawVoidTransition"/>'s burst -- strictly alternates black
    /// and white with no rainbow third (there's no <c>null</c> case to hand
    /// back to <see cref="Primitives2D.DrawTentacleSpike"/>'s own Rainbow
    /// stroke), so the whole burst stays monochrome.
    /// </summary>
    private static Color TentacleVoidThemeColor(int index) => index % 2 == 0
        ? new Color(14, 12, 20)
        : new Color(230, 226, 238);

    /// <summary>
    /// A burst of tentacles rooted at <paramref name="center"/>, each a
    /// different one of <see cref="TentacleThemeColor"/>'s three colors (or,
    /// with <paramref name="voidOnly"/>, <see cref="TentacleVoidThemeColor"/>'s
    /// strict black/white alternation), scaled by <paramref name="bloom"/>
    /// (0 = gone, 1 = full <paramref name="reach"/>) so callers can drive
    /// them through a grow-then-shrink envelope (typically
    /// <c>MathF.Sin(progress * MathF.PI)</c>) over a transition's lifetime.
    /// Shared by every transition tentacle effect in this file so they all
    /// read as the same shadowy-explosion language rather than separate
    /// one-off effects.
    /// </summary>
    private void DrawTentacleBurst(SpriteBatch spriteBatch, Vector2 center,
        int count, float reach, float bloom, float colorSeed = 0f, int segments = 40,
        bool voidOnly = false)
    {
        if (bloom <= .02f)
            return;
        for (int index = 0; index < count; index++)
        {
            float baseAngle = index * MathF.Tau / count + colorSeed + (float)_visualTime * .35f;
            float length = reach * bloom;
            float width = reach * .1f;
            Color? themeColor = voidOnly
                ? TentacleVoidThemeColor(index)
                : TentacleThemeColor(index);
            DrawTentacleSpikeWithTrail(spriteBatch, center, baseAngle, length, width,
                phase: index * 2.1f, colorPhase: index / (float)count + colorSeed,
                segments: segments, themeColor: themeColor);
        }
    }

    /// <summary>
    /// Phase 3's four and Phase 4's eight void tentacles -- a permanent part
    /// of the boss's silhouette from here on, not a one-off transition
    /// effect: the same black/white/rainbow spike technique as the
    /// transition bursts and the Aphantasia portal in The Mind, but held out
    /// near a steady reach (gently wobbling in length) instead of blooming
    /// in and out. <see cref="PersistentTentacleLayout"/> is the single
    /// source of truth for each tentacle's angle/length/width so this draw
    /// call and <see cref="AddPersistentTentacleHitboxes"/>'s contact-damage
    /// boxes can never drift apart.
    /// </summary>
    private void DrawPersistentTentacles(SpriteBatch spriteBatch, Vector2 center)
    {
        var layout = PersistentTentacleLayout();
        for (int index = 0; index < layout.Length; index++)
        {
            (float baseAngle, float length, float width) = layout[index];
            DrawTentacleSpikeWithTrail(spriteBatch, center, baseAngle, length, width,
                phase: index * 1.9f, colorPhase: index / (float)layout.Length,
                segments: 32, themeColor: TentacleThemeColor(index));
        }
    }

    /// <summary>4 in Phase 3, 8 in Phase 4, none before that.</summary>
    private int PersistentTentacleCount => Phase switch
    {
        >= 4 => 8,
        3 => 4,
        _ => 0,
    };

    /// <summary>
    /// Kept close to the boss's own size (extent well under <see cref="Size"/>)
    /// per design intent -- these read as part of the boss, not a separate
    /// oversized hazard -- and orbit slowly so they don't sit dead still.
    /// </summary>
    private (float BaseAngle, float Length, float Width)[] PersistentTentacleLayout()
    {
        int count = PersistentTentacleCount;
        if (count == 0)
            return [];
        var layout = new (float, float, float)[count];
        float extent = Size * .68f;
        float orbitSpin = (float)_visualTime * .18f;
        for (int index = 0; index < count; index++)
        {
            float baseAngle = orbitSpin + index * MathF.Tau / count;
            float length = extent * (.88f + .16f * MathF.Sin((float)_visualTime * 1.3f + index * 1.9f));
            layout[index] = (baseAngle, length, extent * .2f);
        }
        return layout;
    }

    private void DrawDeath(SpriteBatch spriteBatch, Vector2 center)
    {
        float progress = 1f - (float)(_deathRemaining / 4.5);
        const int spikeCount = 10;
        for (int index = 0; index < spikeCount; index++)
        {
            float baseAngle = index * MathF.Tau / spikeCount + progress * 3.2f;
            float length = ArenaRadius * (.18f + progress * .82f);
            float width = Size * (.1f + progress * .06f);
            DrawTentacleSpikeWithTrail(spriteBatch, center, baseAngle, length, width,
                phase: index * 1.7f, colorPhase: index / (float)spikeCount + progress,
                segments: 40);
        }
        for (int ring = 0; ring < 6; ring++)
            Primitives2D.CircleOutline(spriteBatch, center,
                Size * (.5f + ((progress * 4 + ring / 6f) % 1f) * 4f),
                Rainbow(ring / 6f + progress), 3);
    }

    public void DrawPersistentArena(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake, Rectangle logicalViewport)
    {
        Vector2 center = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        for (int index = 0; index < _arenaMask.Length; index++)
        {
            float angle = index * MathF.Tau / _arenaMask.Length;
            _arenaMask[index] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * (ArenaRadius + 8);
        }
        Primitives2D.DrawOutsideArena(spriteBatch, _arenaMask, logicalViewport);
        DrawDistantFragments(spriteBatch, center);
        DrawArenaWall(spriteBatch, center);
        DrawFloorPaneling(spriteBatch, center, PresentationSurvivalActive);

        if (Phase >= 3 && PresentationSurvivalActive)
            DrawSurvivalScreenMood(spriteBatch, logicalViewport);

        if (PresentationSurvivalActive && SurvivalDuration > 0)
        {
            const int timerSegments = 144;
            float timerRadius = ArenaRadius - 13f;
            Primitives2D.CircleOutline(spriteBatch, center, timerRadius,
                UiTheme.Ink * .88f, 18);
            int completedSegments = Math.Clamp(
                (int)MathF.Ceiling(timerSegments * SurvivalTimerProgress),
                0, timerSegments);
            for (int index = 0; index < completedSegments; index++)
            {
                float startAngle = -MathF.PI / 2f + index * MathF.Tau / timerSegments;
                float endAngle = -MathF.PI / 2f + (index + 1.08f) * MathF.Tau / timerSegments;
                Vector2 start = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle))
                    * timerRadius;
                Vector2 end = center + new Vector2(MathF.Cos(endAngle), MathF.Sin(endAngle))
                    * timerRadius;
                Primitives2D.Line(spriteBatch, start, end,
                    Rainbow(index / (float)timerSegments + (float)_visualTime * .025f), 10);
            }
        }
    }

    /// <summary>
    /// The end-of-fight void vortex, drawn from the floor-only occlusion
    /// pass (before the player/boss body/projectiles) instead of the final
    /// world-space pass <see cref="DrawPersistentArena"/> uses -- its radius
    /// grows to cover the whole arena over its ramp, and drawn last-of-all it
    /// used to paint over everything standing in the arena once it got that
    /// big. Floor/background only now, same as every other boss's arena.
    /// </summary>
    public void DrawFloorOcclusion(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (!_voidVortexActive)
            return;
        Vector2 center = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        DrawVoidVortex(spriteBatch, center);
    }

    /// <summary>
    /// Polar graph-paper paneling over the arena floor -- present the whole
    /// fight at a barely-there intensity so the room reads as a built
    /// structure throughout, not something that appears from nothing. A
    /// survival gate simply intensifies the same rings/spokes into a dull
    /// rainbow rather than conjuring a new decoration: kept dull and
    /// low-alpha even then on purpose, since a vibrant rainbow here would
    /// read as a telegraphed hazard and none of this actually damages the
    /// player.
    /// </summary>
    private void DrawFloorPaneling(SpriteBatch spriteBatch, Vector2 center, bool survivalIntensity)
    {
        float ringAlpha = survivalIntensity ? .22f : .07f;
        float spokeAlpha = survivalIntensity ? .16f : .05f;
        Color baseTone = _wallPalette.Detail;

        const int rings = 5;
        for (int ring = 1; ring <= rings; ring++)
        {
            float radius = ArenaRadius * (ring / (float)(rings + 1));
            Color tint = survivalIntensity
                ? DullRainbow(ring / (float)rings + (float)_visualTime * .015f)
                : baseTone;
            Primitives2D.CircleOutline(spriteBatch, center, radius, tint * ringAlpha, 2, 64);
        }
        const int spokes = 12;
        for (int spoke = 0; spoke < spokes; spoke++)
        {
            float angle = spoke * MathF.Tau / spokes + (float)_visualTime * .01f;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Color tint = survivalIntensity
                ? DullRainbow(spoke / (float)spokes + (float)_visualTime * .015f)
                : baseTone;
            Primitives2D.Line(spriteBatch, center, center + direction * ArenaRadius,
                tint * spokeAlpha, 1);
        }
    }

    /// <summary>
    /// Faceted parapet ring: flat panels (not a smooth curve) so the
    /// boundary reads as built from distinct plates, echoing the boss's
    /// cube geometry instead of contrasting with it. Extrudes a cap (the
    /// rim, seen from above) above a ground ring, mirroring the game's
    /// normal room-wall technique (<see cref="ArenaRenderer.VisibleWallFaces"/>)
    /// with the same <see cref="_wallPalette"/> colors, which the arena
    /// previously never touched. Only the near (south-facing) half of the
    /// ring draws its vertical inner face -- the far half's face falls out
    /// of view behind its own cap, same as every other wall in the game.
    /// </summary>
    private void DrawArenaWall(SpriteBatch spriteBatch, Vector2 center)
    {
        Color accent = TrueLight ? new Color(88, 125, 228)
            : TrueDark ? new Color(8, 18, 65)
            : Phase == 4 ? Rainbow((float)_visualTime * .04f)
            : PresentationSurvivalActive ? Color.Lerp(PhaseAccent, DullRainbow((float)_visualTime * .05f), .45f)
            : PhaseAccent;

        for (int index = 0; index <= ArenaWallPanels; index++)
        {
            float angle = index * MathF.Tau / ArenaWallPanels;
            float ocean = MathF.Sin(angle * 7f + (float)_visualTime * .42f) * 8f
                + MathF.Sin(angle * 13f - (float)_visualTime * .21f) * 4f;
            Vector2 ground = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * (ArenaRadius + ocean);
            _arenaWallGround[index] = ground;
            _arenaWallCap[index] = ground - new Vector2(0, ArenaWallHeight);
        }

        for (int index = 0; index < ArenaWallPanels; index++)
        {
            int next = index + 1;
            float midAngle = (index + .5f) * MathF.Tau / ArenaWallPanels;
            if (MathF.Sin(midAngle) > .05f)
            {
                Primitives2D.FillQuad(spriteBatch,
                    _arenaWallCap[index], _arenaWallCap[next],
                    _arenaWallGround[next], _arenaWallGround[index],
                    _wallPalette.WallFace);
                Primitives2D.Line(spriteBatch, _arenaWallCap[index], _arenaWallGround[index],
                    _wallPalette.WallFace * .6f, 2);
            }
        }

        // The cap ribbon is visible the whole way around -- near or far,
        // you're always looking at the rim from inside the room.
        for (int index = 0; index < ArenaWallPanels; index++)
        {
            int next = index + 1;
            Vector2 start = _arenaWallCap[index];
            Vector2 end = _arenaWallCap[next];
            Primitives2D.Line(spriteBatch, start, end, _wallPalette.WallTop, 12);
            Primitives2D.Line(spriteBatch, start, end, UiTheme.Ink, 6);
            Primitives2D.Line(spriteBatch, start, end, accent, Phase == 4 ? 5 : 3);
        }

        Primitives2D.CircleOutline(spriteBatch, center,
            ArenaRadius + 18f + MathF.Sin((float)_visualTime * .35f) * 6f,
            accent * .42f, 3);
    }

    /// <summary>
    /// A handful of small, slow-drifting wireframe cube fragments in the
    /// void beyond the arena wall -- debris from the same tesseract,
    /// adrift in the dark, giving the boundary a sense of scale instead of
    /// opening onto flat black. Drawn after <see cref="Primitives2D.DrawOutsideArena"/>
    /// so they show up against that mask instead of being painted over.
    /// </summary>
    private void DrawDistantFragments(SpriteBatch spriteBatch, Vector2 center)
    {
        foreach ((float angle, float radiusRatio, float size, float spinSeed) in DistantFragments)
        {
            float drift = (float)_visualTime * .015f;
            Vector2 direction = new(MathF.Cos(angle + drift), MathF.Sin(angle + drift));
            Vector2 at = center + direction * (ArenaRadius * radiusRatio);
            float yaw = spinSeed + (float)_visualTime * .12f;
            float pitch = spinSeed * .7f + (float)_visualTime * .08f;
            DrawDistantFragment(spriteBatch, at, size, yaw, pitch, UiTheme.Purple, .3f);
        }
    }

    private static void DrawDistantFragment(SpriteBatch spriteBatch, Vector2 center,
        float extent, float yaw, float pitch, Color tint, float alpha)
    {
        Vector2[] points = ProjectCube(center, extent, yaw, pitch);
        for (int index = 0; index < CubeFaces.Length; index++)
        {
            int[] face = CubeFaces[index];
            float light = FaceLight(index, yaw, pitch);
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], tint * (light * .5f * alpha));
        }
        foreach (int[] edge in CubeEdges)
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], tint * alpha, 1);
    }

    /// <summary>
    /// Whole-screen mood for the Phase 3 and Phase 4 survival sub-phases
    /// (GrandChoice/MiniExecution/EssenceFinale, and VoidFinale). Two
    /// deliberately gentle layers: a flat, low-alpha dim across the entire
    /// screen, and a long, soft-edged vignette that leans on a slow rainbow
    /// wash rather than darkness for its intensity -- there is no hard ring
    /// anywhere in it, just a wide gradient of thin, faint rings so the
    /// falloff reads as gradual rather than a sharp cutoff.
    /// </summary>
    private void DrawSurvivalScreenMood(SpriteBatch spriteBatch, Rectangle viewport)
    {
        Primitives2D.FillRect(spriteBatch, viewport, UiTheme.Scrim * .16f);

        Vector2 center = new(viewport.Center.X, viewport.Center.Y);
        float outerRadius = MathF.Sqrt(
            viewport.Width * viewport.Width + viewport.Height * viewport.Height) * .5f;
        float innerRadius = outerRadius * (Phase >= 4 ? .3f : .42f);
        float cycleSpeed = Phase >= 4 ? .05f : .03f;
        float maxAlpha = Phase >= 4 ? .1f : .07f;

        const int rings = 14;
        for (int ring = 0; ring < rings; ring++)
        {
            float t = ring / (float)(rings - 1);
            float radius = innerRadius + (outerRadius - innerRadius) * t;
            Color hue = Rainbow((float)_visualTime * cycleSpeed + t * .5f);
            Color muted = Color.Lerp(hue, UiTheme.Void, .3f);
            float alpha = t * t * maxAlpha;
            Primitives2D.CircleOutline(spriteBatch, center, radius, muted * alpha,
                Math.Max(3, (int)(outerRadius * .1f)), 80);
        }
    }

    /// <summary>
    /// The Phase 4 finale's floor-to-cosmos reveal. A transparent hole opens
    /// at the arena's center and grows outward over
    /// <see cref="VoidVortexGrowDuration"/>, replacing the floor within it
    /// with a static void backdrop, a scattering of star points, and a
    /// handful of slowly drifting, desaturated nebula blooms. Driven by
    /// <see cref="_voidVortexProgress"/>, which keeps advancing through the
    /// phase handoff and the death collapse, so the reveal survives past the
    /// end of the survival timer rather than snapping shut with it.
    /// </summary>
    private void DrawVoidVortex(SpriteBatch spriteBatch, Vector2 center)
    {
        if (_voidVortexProgress <= 0f)
            return;
        float radius = ArenaRadius * _voidVortexProgress;

        Primitives2D.FillCircle(spriteBatch, center, radius, new Color(6, 5, 14) * .92f);

        foreach (Vector2 offset in VoidStarField)
        {
            float starRadiusFraction = offset.Length();
            if (starRadiusFraction > _voidVortexProgress)
                continue;
            Vector2 point = center + offset * ArenaRadius;
            float twinkle = .5f + .5f * MathF.Sin(
                (float)_visualTime * 3f + offset.X * 37f + offset.Y * 19f);
            Primitives2D.FillCircle(spriteBatch, point, 1.3f, Color.White * (.35f + .55f * twinkle));
        }

        foreach ((Vector2 offset, float blobRadius, Color tint) in VoidNebulae)
        {
            if (offset.Length() > _voidVortexProgress + blobRadius)
                continue;
            Vector2 drift = new(
                MathF.Sin((float)_visualTime * .05f + offset.Y * 5f),
                MathF.Cos((float)_visualTime * .04f + offset.X * 5f));
            Vector2 point = center + (offset + drift * .015f) * ArenaRadius;
            Color dusty = Color.Lerp(tint, new Color(18, 15, 28), .7f);
            Primitives2D.FillCircle(spriteBatch, point, blobRadius * ArenaRadius, dusty * .16f);
            Primitives2D.FillCircle(spriteBatch, point, blobRadius * ArenaRadius * .55f, dusty * .26f);
        }

        const int arms = 3;
        const int armSegments = 26;
        for (int arm = 0; arm < arms; arm++)
        {
            float armOffset = arm * MathF.Tau / arms + (float)_visualTime * .3f;
            Vector2 previous = center;
            for (int segment = 1; segment <= armSegments; segment++)
            {
                float t = segment / (float)armSegments;
                float angle = armOffset + t * MathF.Tau * 1.4f;
                Vector2 point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                    * (radius * t);
                Primitives2D.Line(spriteBatch, previous, point,
                    Rainbow(t + (float)_visualTime * .02f) * (.2f * (1f - t * .4f)), 2);
                previous = point;
            }
        }

        Primitives2D.CircleOutline(spriteBatch, center, radius,
            new Color(120, 90, 200) * .35f, 2);
    }

    /// <summary>
    /// The brief cue that opens each subphase inside a fight: a small void
    /// core (same darkened-disc language as the Aphantasia portal in The
    /// Mind) with a quick shadowy tentacle burst blooming out and back
    /// behind the boss over <see cref="SubphaseDeclarationDuration"/>,
    /// replacing the old plain ring-and-spokes telegraph.
    /// </summary>
    private void DrawSubphaseDeclaration(SpriteBatch spriteBatch, Vector2 center)
    {
        float progress = Math.Clamp(
            (float)(_subphaseCombatElapsed / SubphaseDeclarationDuration), 0f, 1f);
        float bloom = MathF.Sin(progress * MathF.PI);
        DrawTentacleBurst(spriteBatch, center, 6, Size * .95f, bloom, _patternIndex * .17f, 26);
        Primitives2D.FillCircle(spriteBatch, center, Size * .16f,
            new Color(6, 5, 11) * (.5f + .4f * bloom));
    }

    /// <summary>
    /// The larger cue marking an actual phase handoff (boss re-centering,
    /// milestone heal): a shadowy black/white/rainbow tentacle burst
    /// blooming out and back behind the boss over the full
    /// <see cref="PhaseHandoffDuration"/> -- same "the Aphantasia portal in
    /// The Mind" language as every other tentacle effect in this file --
    /// with a small void core standing in for the old flat filled disc and
    /// its cracks.
    /// </summary>
    private void DrawPhaseHandoff(SpriteBatch spriteBatch, Vector2 center)
    {
        float progress = PhaseHandoffProgress;
        float bloom = MathF.Sin(progress * MathF.PI);
        DrawTentacleBurst(spriteBatch, center, 10, Size * 1.6f, bloom);
        Color dullRainbow = Color.Lerp(new Color(58, 55, 68),
            Rainbow((float)_visualTime * .18f), .48f);
        Primitives2D.FillCircle(spriteBatch, center, Size * .24f,
            new Color(6, 5, 11) * (.6f + .3f * bloom));
        Primitives2D.CircleOutline(spriteBatch, center, Size * .26f,
            UiTheme.Ink, 5);
        Primitives2D.CircleOutline(spriteBatch, center, Size * .26f,
            dullRainbow, 3);
    }

    private static Color Rainbow(float phase) => Primitives2D.Rainbow(phase);

    /// <summary>
    /// <see cref="Rainbow"/>'s monochrome counterpart -- a drop-in
    /// replacement with the same signature shape, but a black-to-white
    /// VALUE gradient instead of a cycling hue, for the body's voided look
    /// during Phase 3/4 survival windows (see <see cref="VoidedBodyActive"/>).
    /// A value gradient reads calmer and less busy than a full hue sweep
    /// would, which is the actual "refined, less busy" quality the voided
    /// look is going for -- not simply "the same rainbow, but darker."
    /// </summary>
    private static Color VoidTone(float phase)
    {
        phase -= MathF.Floor(phase);
        float value = .5f + .5f * MathF.Sin(phase * MathF.Tau);
        return Color.Lerp(new Color(6, 5, 11), new Color(230, 226, 238), value);
    }

    /// <summary>
    /// A darkened, low-saturation cousin of <see cref="Rainbow"/> for
    /// decorative environment theming that must never be mistaken for a
    /// telegraphed attack.
    /// </summary>
    private static Color DullRainbow(float phase, float alpha = 1f) =>
        Color.Lerp(new Color(26, 24, 34), Rainbow(phase), .5f) * alpha;

    /// <summary>
    /// Fixed unit-disc star positions for <see cref="DrawVoidVortex"/>,
    /// seeded once so the field doesn't reshuffle every frame.
    /// </summary>
    private static readonly Vector2[] VoidStarField = BuildVoidStarField(150);

    private static Vector2[] BuildVoidStarField(int count)
    {
        var rng = new Random(1337);
        var stars = new Vector2[count];
        for (int index = 0; index < count; index++)
        {
            float angle = (float)(rng.NextDouble() * MathF.Tau);
            float radius = MathF.Sqrt((float)rng.NextDouble());
            stars[index] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        return stars;
    }

    /// <summary>
    /// Fixed dusty rainbow nebula blobs (offset, radius, tint) for
    /// <see cref="DrawVoidVortex"/>, seeded once for the same reason.
    /// </summary>
    private static readonly (Vector2 Offset, float Radius, Color Tint)[] VoidNebulae =
        BuildVoidNebulae();

    private static (Vector2, float, Color)[] BuildVoidNebulae()
    {
        var rng = new Random(7331);
        var nebulae = new (Vector2, float, Color)[6];
        for (int index = 0; index < nebulae.Length; index++)
        {
            float angle = (float)(rng.NextDouble() * MathF.Tau);
            float radius = .25f + (float)rng.NextDouble() * .55f;
            Vector2 offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            float blobRadius = .16f + (float)rng.NextDouble() * .14f;
            nebulae[index] = (offset, blobRadius, Rainbow(index / (float)nebulae.Length));
        }
        return nebulae;
    }

    /// <summary>
    /// Fixed (angle, radius ratio beyond the wall, size, spin seed) tuples
    /// for <see cref="DrawDistantFragments"/>, seeded once for the same
    /// reason as the void star field.
    /// </summary>
    private static readonly (float Angle, float RadiusRatio, float Size, float SpinSeed)[] DistantFragments =
        BuildDistantFragments(9);

    private static (float, float, float, float)[] BuildDistantFragments(int count)
    {
        var rng = new Random(4242);
        var fragments = new (float, float, float, float)[count];
        for (int index = 0; index < count; index++)
        {
            fragments[index] = (
                (float)(rng.NextDouble() * MathF.Tau),
                1.15f + (float)rng.NextDouble() * .55f,
                10f + (float)rng.NextDouble() * 14f,
                (float)(rng.NextDouble() * MathF.Tau));
        }
        return fragments;
    }
}
