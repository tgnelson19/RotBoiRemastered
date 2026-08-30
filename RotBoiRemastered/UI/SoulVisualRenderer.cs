using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

internal enum SoulPortalPresentationState
{
    Idle,
    Approached,
    Confirming,
    Committing,
}

internal enum SoulStationPresentationState
{
    Idle,
    Nearby,
    Active,
    Complete,
}

/// <summary>
/// Procedural presentation for The Soul. It owns no interaction or effect
/// state: all motion is derived from time, layout anchors, progression, and
/// the current interaction keys supplied by SoulHub.
/// </summary>
internal static class SoulVisualRenderer
{
    private static readonly Color ChapelStone = new(54, 45, 70);
    private static readonly Color ChapelStoneLight = new(91, 73, 111);
    private static readonly Color ChapelWarm = new(222, 177, 104);
    private static readonly Color ChapelGlass = new(173, 120, 188);

    public static SoulPortalPresentationState ResolvePortalState(
        string key,
        string? nearby,
        string? confirming,
        string? entering) =>
        entering == key ? SoulPortalPresentationState.Committing
        : confirming == key ? SoulPortalPresentationState.Confirming
        : nearby == key ? SoulPortalPresentationState.Approached
        : SoulPortalPresentationState.Idle;

    public static int MasteryTier(int mastery) => Math.Clamp(mastery, 0, 5);

    public static int OptionalEffectCount(int authoredCount, float intensity) =>
        Math.Clamp((int)MathF.Round(authoredCount * Math.Clamp(intensity, 0, 1)), 0, authoredCount);

    internal static float VeinProximity(
        Vector2 playerWorld,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        float radiusTiles = 2.65f)
    {
        Vector2 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.LengthSquared();
        float amount = lengthSquared <= .001f
            ? 0f
            : Math.Clamp(
                Vector2.Dot(playerWorld - segmentStart, segment)
                    / lengthSquared,
                0f, 1f);
        Vector2 closest = segmentStart + segment * amount;
        float radius = Math.Max(.1f, radiusTiles) * Battleground.TileSize;
        return 1f - Math.Clamp(
            Vector2.Distance(playerWorld, closest) / radius,
            0f, 1f);
    }

    public static void DrawEnvironment(
        SpriteBatch spriteBatch,
        GameSession session,
        float time,
        float intensity,
        IReadOnlyDictionary<string, Vector2> portalWorld)
    {
        DrawChapel(spriteBatch, session, time, intensity);
        DrawTransition(spriteBatch, session, time, intensity);
        DrawBranches(spriteBatch, session, time, intensity, portalWorld);
    }

    public static void DrawStations(
        SpriteBatch spriteBatch,
        GameSession session,
        float time,
        IReadOnlyDictionary<string, Vector2> stations,
        string? nearbyStation,
        string? activeStation)
    {
        foreach (var (key, world) in stations)
        {
            bool complete = key switch
            {
                "storage" => GameProfile.Profile.Storage.Count >= MetaProgression.StorageCapacity,
                "quests" => GameProfile.Profile.CompletedQuests.Count >= MetaProgression.Quests.Count,
                "skills" => MetaProgression.SkillNodes.All(node =>
                    GameProfile.Profile.SkillLevels.GetValueOrDefault(node.Key) >= node.MaxLevel),
                "hard_mode" => GameProfile.Profile.NoHealingEnabled,
                "no_extract" => GameProfile.Profile.NoExtractEnabled,
                _ => false,
            };
            SoulStationPresentationState state = activeStation == key
                ? SoulStationPresentationState.Active
                : nearbyStation == key ? SoulStationPresentationState.Nearby
                : complete ? SoulStationPresentationState.Complete
                : SoulStationPresentationState.Idle;
            DrawStation(spriteBatch, session, key, world, time, state);
        }
    }

    public static void DrawPortals(
        SpriteBatch spriteBatch,
        GameSession session,
        float time,
        IReadOnlyDictionary<string, Vector2> portals,
        string? nearbyPortal,
        string? confirmingPortal,
        string? enteringPortal,
        double portalAnimationStart)
    {
        if (CampaignProgression.PortalUnlocked("body")
            && portals.TryGetValue(SoulHub.BodyPortalKey, out Vector2 composite))
        {
            SoulPortalPresentationState state = ResolvePortalState(
                SoulHub.BodyPortalKey, nearbyPortal, confirmingPortal, enteringPortal);
            float pull = state == SoulPortalPresentationState.Committing
                ? (float)Math.Clamp((time - portalAnimationStart) / .9, 0, 1)
                : 0;
            DrawSoulRose(spriteBatch, session, composite, time, state, pull);
        }

        foreach (GamePath path in GamePaths.Paths)
        {
            if (!portals.TryGetValue(path.Key, out Vector2 world))
                continue;
            SoulPortalPresentationState state = ResolvePortalState(
                path.Key, nearbyPortal, confirmingPortal, enteringPortal);
            float pull = state == SoulPortalPresentationState.Committing
                ? (float)Math.Clamp((time - portalAnimationStart) / .9, 0, 1)
                : 0;
            DrawPathPortal(spriteBatch, session, path, world, time, state, pull,
                path.Key == nearbyPortal && confirmingPortal != path.Key && enteringPortal is null);
        }
    }

    public static void DrawOverlayFrame(
        SpriteBatch spriteBatch,
        Rectangle panel,
        string stationKey,
        Color accent)
    {
        UiTheme.DrawPanel(spriteBatch, panel, UiTheme.PanelRaised, accent, shadow: 10);
        var inner = panel;
        inner.Inflate(-9, -9);
        Primitives2D.RectOutline(spriteBatch, inner, accent * .28f, 2);

        // A restrained chapel arch and station watermark frame the existing
        // information architecture without changing any content hit boxes.
        int archWidth = Math.Min(180, panel.Width / 4);
        var arch = new Rectangle(panel.Center.X - archWidth / 2, panel.Y + 8, archWidth, 42);
        Primitives2D.Arc(spriteBatch, arch, MathF.PI, MathF.Tau, accent * .28f, 3);
        for (int corner = 0; corner < 4; corner++)
        {
            int x = corner % 2 == 0 ? panel.X + 16 : panel.Right - 22;
            int y = corner < 2 ? panel.Y + 16 : panel.Bottom - 22;
            Primitives2D.FillRect(spriteBatch, new Rectangle(x, y, 6, 6), accent * .58f);
            Primitives2D.FillRect(spriteBatch, new Rectangle(x + (corner % 2 == 0 ? 8 : -8), y, 4, 4), ChapelWarm * .4f);
        }

        Vector2 mark = new(panel.Right - 58, panel.Y + 54);
        switch (stationKey)
        {
            case "storage":
                Primitives2D.RectOutline(spriteBatch,
                    new Rectangle((int)mark.X - 22, (int)mark.Y - 13, 44, 30), accent * .2f, 4);
                Primitives2D.Line(spriteBatch, mark + new Vector2(-22, -2), mark + new Vector2(22, -2), accent * .2f, 3);
                break;
            case "quests":
                Primitives2D.Polyline(spriteBatch, new[]
                {
                    mark + new Vector2(-24, -15), mark, mark + new Vector2(24, -15),
                    mark + new Vector2(24, 17), mark, mark + new Vector2(-24, 17),
                }, false, accent * .2f, 3);
                break;
            case "skills":
                Primitives2D.CircleOutline(spriteBatch, mark, 23, accent * .2f, 3);
                for (int i = 0; i < 6; i++)
                    Primitives2D.Line(spriteBatch, mark,
                        mark + Direction(i * MathF.Tau / 6f) * 23, accent * .18f, 2);
                break;
            case "wardrobe":
                Primitives2D.Arc(spriteBatch,
                    new Rectangle((int)mark.X - 23, (int)mark.Y - 25, 46, 50),
                    MathF.PI, MathF.Tau, accent * .22f, 4);
                Primitives2D.Line(spriteBatch, mark + new Vector2(-23, 0),
                    mark + new Vector2(-23, 22), accent * .22f, 4);
                Primitives2D.Line(spriteBatch, mark + new Vector2(23, 0),
                    mark + new Vector2(23, 22), accent * .22f, 4);
                break;
        }
    }

    private static void DrawChapel(SpriteBatch spriteBatch, GameSession session, float time, float intensity)
    {
        Vector2 aisleSouth = Screen(session, SoulLayout.TileWorldCenter(SoulLayout.AuthoredTile(39, 76)));
        Vector2 aisleNorth = Screen(session, SoulLayout.TileWorldCenter(SoulLayout.AuthoredTile(39, 55)));
        Primitives2D.Line(spriteBatch, aisleSouth + new Vector2(5, 7), aisleNorth + new Vector2(5, 7),
            UiTheme.Shadow * .65f, 80);
        Primitives2D.Line(spriteBatch, aisleSouth, aisleNorth, new Color(72, 42, 76) * .72f, 58);
        Primitives2D.Line(spriteBatch, aisleSouth, aisleNorth, ChapelGlass * .22f, 4);

        // Low pews and shelves remain purely decorative and never affect
        // collision. Their gaps preserve a clean sight line to every shrine.
        for (int row = 0; row < 3; row++)
        {
            float y = 62 + row * 4;
            foreach (float x in new[] { 33f, 45f })
            {
                Vector2 a = Screen(session, SoulLayout.TileWorldCenter(SoulLayout.AuthoredTile((int)x - 3, (int)y)));
                Vector2 b = Screen(session, SoulLayout.TileWorldCenter(SoulLayout.AuthoredTile((int)x, (int)y)));
                Primitives2D.Line(spriteBatch, a + new Vector2(5, 8), b + new Vector2(5, 8), UiTheme.Shadow * .7f, 16);
                Primitives2D.Line(spriteBatch, a, b, ChapelStone, 10);
                Primitives2D.Line(spriteBatch, a - new Vector2(0, 3), b - new Vector2(0, 3), ChapelStoneLight * .65f, 3);
            }
        }

        // Stained-light panes tick through three hard-edged brightness steps.
        for (int pane = 0; pane < 6; pane++)
        {
            float side = pane % 2 == 0 ? -1 : 1;
            float y = 60 + pane / 2 * 6;
            Vector2 at = Screen(session, SoulLayout.TileWorldCenter(SoulLayout.AuthoredTile(
                39 + (int)(side * 12), (int)y)));
            float step = MathF.Floor((.5f + .5f * MathF.Sin(time * 1.2f + pane)) * 3f) / 3f;
            Color color = (pane % 3) switch
            {
                0 => ChapelGlass,
                1 => ChapelWarm,
                _ => UiTheme.Blue,
            };
            Primitives2D.FillPolygon(spriteBatch, new[]
            {
                at + new Vector2(-14, -28), at + new Vector2(14, -28),
                at + new Vector2(23, 18), at + new Vector2(-23, 18),
            }, color * (.06f + step * .08f));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 5, (int)at.Y - 24, 10, 28), color * (.35f + step * .28f));
        }

        // Practice transept target and two hit-reactive-looking candles.
        Vector2 dummy = Screen(session, SoulLayout.TileWorldCenter(SoulLayout.DummyTile));
        Primitives2D.CircleOutline(spriteBatch, dummy + new Vector2(0, 18), 45, UiTheme.Red * .22f, 3);
        Primitives2D.CircleOutline(spriteBatch, dummy + new Vector2(0, 18), 25, UiTheme.Red * .28f, 2);
        Primitives2D.Line(spriteBatch, dummy + new Vector2(-42, 18), dummy + new Vector2(42, 18), UiTheme.Red * .18f, 2);
        DrawCandle(spriteBatch, dummy + new Vector2(-55, 34), time, 0);
        DrawCandle(spriteBatch, dummy + new Vector2(55, 34), time, 1);

        int dustCount = OptionalEffectCount(22, intensity);
        for (int dust = 0; dust < dustCount; dust++)
        {
            float x = 27f + (dust * 7 % 25);
            float y = 58f + ((dust * 11 + (int)(time * (1 + dust % 2))) % 18);
            Vector2 at = Screen(session, SoulLayout.TileWorldCenter(SoulLayout.AuthoredTile((int)x, (int)y)));
            int rise = (int)MathF.Floor((time * (5 + dust % 3) + dust * 13) % 24);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 1, (int)at.Y - rise, 2 + dust % 2, 2 + dust % 2),
                ChapelWarm * (.16f + dust % 3 * .05f));
        }
    }

    private static void DrawTransition(SpriteBatch spriteBatch, GameSession session, float time, float intensity)
    {
        Vector2 southWorld = SoulLayout.TileWorldCenter(SoulLayout.TunnelSouthTile);
        Vector2 northWorld = SoulLayout.TileWorldCenter(SoulLayout.NexusTile);
        Color[] colors = GamePaths.Paths.Select(path => path.Accent).ToArray();

        // The old repeated arch canopy has been removed. Five exposed soul
        // veins now carry the chapel into the convergence, waking locally as
        // the player crosses them instead of lighting the whole tunnel at
        // once.
        const int segments = 28;
        for (int lane = 0; lane < colors.Length; lane++)
        {
            Vector2 previous = TunnelVeinPoint(
                southWorld, northWorld, lane, 0f, time);
            for (int segment = 1; segment <= segments; segment++)
            {
                float amount = segment / (float)segments;
                Vector2 current = TunnelVeinPoint(
                    southWorld, northWorld, lane, amount, time);
                DrawLivingVeinSegment(
                    spriteBatch, session,
                    previous, current, colors[lane],
                    time, lane, amount, intensity,
                    baseEnergy: .12f);
                previous = current;
            }
        }

        int motes = OptionalEffectCount(20, intensity);
        for (int mote = 0; mote < motes; mote++)
        {
            float amount = ((mote * .173f + time * (.025f + mote % 3 * .006f)) % 1f + 1f) % 1f;
            Vector2 world = Vector2.Lerp(southWorld, northWorld, amount);
            float wake = VeinProximity(
                session.PlayerWorldCenter,
                world - Vector2.UnitY * Battleground.TileSize,
                world + Vector2.UnitY * Battleground.TileSize);
            if (wake <= .02f)
                continue;
            Vector2 at = Screen(session, world) + new Vector2(
                MathF.Round(MathF.Sin(mote * 2.4f + time) * 82f),
                -12 - MathF.Round(MathF.Sin(time * 1.5f + mote) * 11f));
            Color color = colors[mote % colors.Length];
            int size = 3 + mote % 3;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - size / 2, (int)at.Y - size / 2, size, size),
                color * (.22f + wake * .58f));
        }
    }

    private static Vector2 TunnelVeinPoint(
        Vector2 south,
        Vector2 north,
        int lane,
        float amount,
        float time)
    {
        float spread = (lane - 2) * Battleground.TileSize
            * MathHelper.Lerp(.28f, .075f, amount);
        float steppedWave = MathF.Round(
            MathF.Sin(time * (1.05f + lane * .045f)
                - amount * 8f + lane * 1.3f) * 3f);
        return Vector2.Lerp(south, north, amount)
            + new Vector2(spread + steppedWave, 0);
    }

    private static void DrawLivingVeinSegment(
        SpriteBatch spriteBatch,
        GameSession session,
        Vector2 startWorld,
        Vector2 endWorld,
        Color color,
        float time,
        int veinIndex,
        float routeAmount,
        float intensity,
        float baseEnergy)
    {
        float wake = VeinProximity(
            session.PlayerWorldCenter, startWorld, endWorld);
        float steppedPulse = MathF.Floor(
            (.5f + .5f * MathF.Sin(
                time * 3.2f - routeAmount * 9f + veinIndex)) * 4f) / 4f;
        float energy = Math.Clamp(
            baseEnergy + wake * (.64f + steppedPulse * .2f),
            0f, 1f);
        Vector2 start = Screen(session, startWorld);
        Vector2 end = Screen(session, endWorld);

        Primitives2D.Line(
            spriteBatch, start + new Vector2(0, 4),
            end + new Vector2(0, 4),
            UiTheme.Shadow * (.36f + energy * .28f), 8);
        if (intensity > 0 && wake > .02f)
        {
            Primitives2D.Line(
                spriteBatch, start, end,
                color * (wake * intensity * .2f), 11);
        }
        Primitives2D.Line(
            spriteBatch, start, end,
            color * (.16f + energy * .68f),
            wake > .15f ? 5 : 3);
        Primitives2D.Line(
            spriteBatch,
            start - new Vector2(0, 1),
            end - new Vector2(0, 1),
            Color.Lerp(color, UiTheme.Cream, .45f)
                * (.08f + energy * .36f),
            wake > .15f ? 2 : 1);

        float packet = ((time * .34f + veinIndex * .137f) % 1f + 1f) % 1f;
        if (wake > .08f
            && Math.Abs(routeAmount - packet) < .025f)
        {
            Vector2 at = end;
            int size = 4 + (int)MathF.Round(wake * 4f);
            Primitives2D.FillRect(
                spriteBatch,
                new Rectangle(
                    (int)at.X - size / 2,
                    (int)at.Y - size / 2,
                    size, size),
                Color.Lerp(color, UiTheme.Cream, .55f)
                    * (.45f + wake * .5f));
        }
    }

    private static void DrawBranches(SpriteBatch spriteBatch, GameSession session, float time,
        float intensity, IReadOnlyDictionary<string, Vector2> portalWorld)
    {
        Vector2 nexus = SoulLayout.TileWorldCenter(SoulLayout.NexusTile);
        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            GamePath path = GamePaths.Paths[index];
            if (!portalWorld.TryGetValue(path.Key, out Vector2 portal))
                continue;
            int mastery = MasteryTier(GameProfile.Profile.PathMastery.GetValueOrDefault(path.Key));
            int ng = NewGamePlus.SelectedLevel(path.Key);
            float completion = .3f + mastery * .12f;
            const int segments = 28;
            Vector2 previous = nexus;
            for (int segment = 1; segment <= segments; segment++)
            {
                float amount = segment / (float)segments;
                Vector2 at = Vector2.Lerp(nexus, portal, amount);
                DrawLivingVeinSegment(
                    spriteBatch, session,
                    previous, at, path.Accent,
                    time, index, amount, intensity,
                    baseEnergy: .1f + completion * .16f);
                previous = at;
            }
            DrawBranchVocabulary(spriteBatch, session, path.Key, portal, path.Accent, time, intensity, mastery, ng);
        }
    }

    private static void DrawBranchVocabulary(SpriteBatch spriteBatch, GameSession session,
        string key, Vector2 world, Color color, float time, float intensity, int mastery, int ng)
    {
        Vector2 center = Screen(session, world);
        float light = .42f + mastery * .08f;
        switch (key)
        {
            case "sound":
                for (int i = 0; i < 4; i++)
                {
                    float height = 28 + i * 12 + MathF.Round(MathF.Sin(time * 2f + i) * 5);
                    Primitives2D.FillRect(spriteBatch,
                        new Rectangle((int)center.X - 92 + i * 18, (int)(center.Y - 54 - height), 8, (int)height),
                        color * light);
                }
                for (int ring = 0; ring < 2 + OptionalEffectCount(1, intensity); ring++)
                    Primitives2D.CircleOutline(spriteBatch, center,
                        88 + ((time * 18 + ring * 29) % 32), color * (.18f + mastery * .025f), 2);
                break;
            case "touch":
                for (int block = 0; block < 5; block++)
                {
                    var rect = new Rectangle((int)center.X - 98 + block * 42, (int)center.Y - 94 - block % 2 * 8, 25, 21);
                    Primitives2D.FillRect(spriteBatch, rect, ChapelStone);
                    Primitives2D.RectOutline(spriteBatch, rect, color * light, 3);
                }
                float valve = MathF.Floor(time * 4f) * MathF.PI / 2f;
                for (int spoke = 0; spoke < 4; spoke++)
                    Primitives2D.Line(spriteBatch, center + new Vector2(72, 52),
                        center + new Vector2(72, 52) + Direction(valve + spoke * MathF.PI / 2f) * 20,
                        color * .72f, 4);
                break;
            case "sight":
                Primitives2D.EllipseOutline(spriteBatch,
                    new Rectangle((int)center.X - 92, (int)center.Y - 112, 184, 54), color * light, 3);
                float scan = MathF.Floor(time * 8f) / 8f;
                Vector2 eye = center + new Vector2(0, -85);
                Primitives2D.Line(spriteBatch, eye,
                    eye + Direction(scan) * 108, color * .42f, 2);
                Primitives2D.FillCircle(spriteBatch, eye,
                    8 + MathF.Round(MathF.Sin(time * 2f) * 3), color * .62f);
                break;
            case "chemesthesis":
                for (int vent = 0; vent < 5; vent++)
                {
                    Vector2 at = center + new Vector2(-90 + vent * 45, -72);
                    Primitives2D.Line(spriteBatch, at, at + new Vector2((vent % 2 == 0 ? -1 : 1) * 15, -24),
                        color * light, 4);
                    if (vent < OptionalEffectCount(5, intensity))
                    {
                        int rise = (int)((time * (13 + vent) + vent * 17) % 34);
                        Primitives2D.FillRect(spriteBatch,
                            new Rectangle((int)at.X - 3, (int)at.Y - rise, 6, 6),
                            Color.Lerp(color, UiTheme.Gold, .45f) * .72f);
                    }
                }
                break;
            case "phantasia":
                int stars = 5 + mastery;
                for (int star = 0; star < stars; star++)
                {
                    float angle = star * MathF.Tau / stars + MathF.Floor(time * 4f) * .025f;
                    Vector2 at = center + Direction(angle) * (72 + star % 3 * 13);
                    int size = 4 + star % 2 * 3;
                    Primitives2D.FillRect(spriteBatch,
                        new Rectangle((int)at.X - size / 2, (int)at.Y - size / 2, size, size),
                        Color.Lerp(color, Color.White, .5f) * light);
                }
                break;
        }

        for (int pip = 0; pip < ng; pip++)
        {
            float angle = pip * 2.17f + MathF.Floor(time * 5f) * .04f;
            Vector2 at = center + Direction(angle) * (105 + pip % 3 * 8);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 3, (int)at.Y - 3, 6, 6), UiTheme.Red * .7f);
        }
    }

    private static void DrawStation(SpriteBatch spriteBatch, GameSession session, string key,
        Vector2 world, float time, SoulStationPresentationState state)
    {
        Vector2 at = Screen(session, world);
        (string label, Color accent) = StationIdentity(key);
        float wake = state switch
        {
            SoulStationPresentationState.Active => 1.35f,
            SoulStationPresentationState.Nearby => 1.1f,
            SoulStationPresentationState.Complete => .9f,
            _ => .62f,
        };
        float breathe = MathF.Floor((.5f + .5f * MathF.Sin(time * 2f + StableSeed(key))) * 4f) / 4f;

        var plinth = new Rectangle((int)at.X - 41, (int)at.Y + 17, 82, 24);
        Primitives2D.FillRect(spriteBatch, new Rectangle(plinth.X + 5, plinth.Y + 7, plinth.Width, plinth.Height),
            UiTheme.Shadow * .78f);
        Primitives2D.FillRect(spriteBatch, plinth, new Color(37, 31, 48));
        Primitives2D.RectOutline(spriteBatch, plinth, accent * wake, 3);
        Primitives2D.Arc(spriteBatch,
            new Rectangle((int)at.X - 46, (int)at.Y - 66, 92, 100),
            MathF.PI, MathF.Tau, ChapelStoneLight * .72f, 5);
        Primitives2D.Line(spriteBatch, at + new Vector2(-46, -16), at + new Vector2(-46, 25), ChapelStone, 7);
        Primitives2D.Line(spriteBatch, at + new Vector2(46, -16), at + new Vector2(46, 25), ChapelStone, 7);

        switch (key)
        {
            case "storage":
                DrawReliquary(spriteBatch, at, accent, time, wake);
                break;
            case "quests":
                DrawLectern(spriteBatch, at, accent, time, wake);
                break;
            case "skills":
                DrawRoseWindow(spriteBatch, at, accent, time, wake);
                break;
            case "wardrobe":
                DrawVestmentMirror(spriteBatch, at, accent, time, wake);
                break;
            case "hard_mode":
                DrawTrialBrazier(spriteBatch, at, accent, time, wake, GameProfile.Profile.NoHealingEnabled);
                break;
            case "no_extract":
                DrawTrialBrazier(spriteBatch, at, UiTheme.Purple, time, wake, GameProfile.Profile.NoExtractEnabled);
                break;
            case "golden_flame":
                DrawTrialBrazier(spriteBatch, at, UiTheme.Gold, time, wake, GameProfile.Profile.GoldenFlameEnabled);
                break;
            case "the_void":
                DrawTrialBrazier(spriteBatch, at, Color.Lerp(UiTheme.Void, UiTheme.Purple, .4f), time, wake, GameProfile.Profile.VoidEnabled);
                break;
            case "developer_armory":
                DrawReliquary(spriteBatch, at, UiTheme.Gold, time, wake);
                for (int slot = 0; slot < 4; slot++)
                    Primitives2D.RectOutline(spriteBatch,
                        new Rectangle((int)at.X - 27 + slot * 14, (int)at.Y - 8, 10, 16),
                        UiTheme.Cream * .75f, 2);
                break;
        }

        if (state is SoulStationPresentationState.Nearby or SoulStationPresentationState.Active)
        {
            int extent = 50 + (int)(breathe * 8);
            Primitives2D.RectOutline(spriteBatch,
                new Rectangle((int)at.X - extent, (int)at.Y - extent, extent * 2, extent * 2),
                accent * (.22f + breathe * .18f), 2);
        }
        UiTheme.DrawText(spriteBatch, label, 8, accent,
            new Vector2(at.X, plinth.Bottom + 7), "midtop");
    }

    /// <summary>
    /// Turns a flat polygon -- exactly the kind already authored for each
    /// totem's old flat silhouette -- into a simple extruded "block" by
    /// filling one shaded quad per edge along <paramref name="extrude"/>
    /// (the totem's implied depth axis) for the sides, then redrawing the
    /// original polygon on top as the lit face. No mesh/3D pipeline
    /// involved, just per-face directional shading -- a cheap, general way
    /// to give every totem real volume as a first pass to workshop further.
    /// </summary>
    private static void DrawExtrudedPolygon(SpriteBatch spriteBatch, IReadOnlyList<Vector2> topFace,
        Vector2 extrude, Color color, float wake)
    {
        int count = topFace.Count;
        for (int index = 0; index < count; index++)
        {
            Vector2 a = topFace[index];
            Vector2 b = topFace[(index + 1) % count];
            // Crude "light from upper-left" split: edges running rightward
            // catch more light than edges running leftward. Not physically
            // accurate, but it's enough per-face contrast to read as a solid
            // block instead of a flat cutout.
            float shade = b.X >= a.X ? .32f : .5f;
            Primitives2D.FillQuad(spriteBatch, a, b, b + extrude, a + extrude,
                Color.Lerp(color, Color.Black, shade) * wake);
        }
        Primitives2D.FillPolygon(spriteBatch, topFace, Color.Lerp(color, Color.White, .1f) * wake);
        Primitives2D.PolygonOutline(spriteBatch, topFace, Color.Lerp(color, Color.White, .3f) * wake, 2);
    }

    /// <summary>
    /// A disc tilted away from the viewer with real rim thickness -- top
    /// ellipse (lit), a crude front-facing band standing in for the curved
    /// side wall, and a darker bottom ellipse peeking out beneath it. Used
    /// for the Mind Rose's wheel; the same "flat circle -> tilted drum"
    /// trick generalizes to any totem that used to be a bare CircleOutline.
    /// </summary>
    private static void DrawTiltedDisc(SpriteBatch spriteBatch, Vector2 center, float radiusX, float radiusY,
        float thickness, Color color, float wake)
    {
        var top = new Rectangle((int)(center.X - radiusX), (int)(center.Y - radiusY),
            (int)(radiusX * 2), (int)(radiusY * 2));
        var bottom = new Rectangle(top.X, top.Y + (int)thickness, top.Width, top.Height);
        Primitives2D.FillEllipse(spriteBatch, bottom, Color.Lerp(color, Color.Black, .55f) * wake);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(top.X, top.Y + (int)radiusY, top.Width, (int)thickness),
            Color.Lerp(color, Color.Black, .4f) * wake);
        Primitives2D.FillEllipse(spriteBatch, top, Color.Lerp(color, Color.White, .08f) * wake);
        Primitives2D.EllipseOutline(spriteBatch, top, Color.Lerp(color, Color.White, .32f) * wake, 3);
    }

    private static void DrawReliquary(SpriteBatch spriteBatch, Vector2 at, Color color, float time, float wake)
    {
        // Chest body, extruded straight down for a real box...
        Vector2[] body =
        {
            at + new Vector2(-29, -3), at + new Vector2(29, -3),
            at + new Vector2(29, 22), at + new Vector2(-29, 22),
        };
        DrawExtrudedPolygon(spriteBatch, body, new Vector2(0, 9), new Color(58, 46, 52), wake);
        // ...topped with a narrower lid wedge, extruded shallower, so the
        // silhouette still reads as a hinged chest rather than a plain box.
        Vector2[] lid =
        {
            at + new Vector2(-29, -14), at + new Vector2(29, -14),
            at + new Vector2(25, -3), at + new Vector2(-25, -3),
        };
        DrawExtrudedPolygon(spriteBatch, lid, new Vector2(0, 5), color, wake * .92f);

        var keyhole = new Rectangle((int)at.X - 5, (int)at.Y - 1, 10, 13);
        Primitives2D.FillRect(spriteBatch, keyhole, Color.Lerp(color, Color.Black, .2f) * wake);
        Primitives2D.RectOutline(spriteBatch, keyhole, Color.Lerp(color, Color.White, .3f) * wake, 2);
        for (int key = 0; key < 3; key++)
        {
            float angle = MathF.Floor(time * 6f) * .12f + key * MathF.Tau / 3f;
            Vector2 mote = at + Direction(angle) * 39;
            Primitives2D.FillCircle(spriteBatch, mote, 3.5f, color * .7f);
            Primitives2D.FillCircle(spriteBatch, mote - new Vector2(1, 1), 1.2f, Color.White * .5f);
        }
    }

    private static void DrawLectern(SpriteBatch spriteBatch, Vector2 at, Color color, float time, float wake)
    {
        // Each page gets a shallow extrusion of its own -- opposite
        // directions, so the book keeps its open-V silhouette while both
        // halves read as having real paper thickness rather than two flat
        // cutouts glued together.
        Vector2[] leftPage =
        {
            at + new Vector2(-31, -24), at + new Vector2(-2, -14), at + new Vector2(0, 13),
            at + new Vector2(-33, 2),
        };
        Vector2[] rightPage =
        {
            at + new Vector2(31, -24), at + new Vector2(2, -14), at + new Vector2(0, 13),
            at + new Vector2(33, 2),
        };
        DrawExtrudedPolygon(spriteBatch, leftPage, new Vector2(-4, 5), new Color(44, 48, 43), wake);
        DrawExtrudedPolygon(spriteBatch, rightPage, new Vector2(4, 5), new Color(44, 48, 43), wake);
        Primitives2D.Polyline(spriteBatch, new[]
        {
            at + new Vector2(-31, -24), at + new Vector2(-2, -14), at,
            at + new Vector2(2, -14), at + new Vector2(31, -24),
        }, false, color * wake, 3);
        float page = MathF.Floor(time * 2f) % 4;
        Primitives2D.Line(spriteBatch, at + new Vector2(-22 + page * 3, -16),
            at + new Vector2(-4, -9), ChapelWarm * .72f, 2);

        // A short extruded plinth stand carries the book up off the base
        // plinth instead of it floating flush against the stone.
        Vector2[] stand =
        {
            at + new Vector2(-10, 13), at + new Vector2(10, 13),
            at + new Vector2(6, 24), at + new Vector2(-6, 24),
        };
        DrawExtrudedPolygon(spriteBatch, stand, new Vector2(0, 6), new Color(38, 33, 40), wake * .8f);

        DrawCandle(spriteBatch, at + new Vector2(-42, 9), time, 3);
        DrawCandle(spriteBatch, at + new Vector2(42, 9), time, 4);
    }

    private static void DrawRoseWindow(SpriteBatch spriteBatch, Vector2 at, Color color, float time, float wake)
    {
        Vector2 center = at - new Vector2(0, 17);
        // Squashed vertically and given rim thickness -- the window now
        // reads as a wheel tilted back into the wall rather than a flat
        // circle painted on it.
        DrawTiltedDisc(spriteBatch, center, 31, 20, 9, color, wake);
        float phase = MathF.Floor(time * 4f) * MathF.PI / 12f;
        for (int spoke = 0; spoke < 8; spoke++)
        {
            float angle = phase + spoke * MathF.Tau / 8f;
            // Project each spoke through the same vertical squash as the
            // rim so they read as lying flat against the tilted wheel face
            // instead of floating in an untilted circle over it.
            Vector2 outer = center + new Vector2(Direction(angle).X * 29, Direction(angle).Y * 18);
            Primitives2D.Line(spriteBatch, center, outer, color * .62f, 2);
            Primitives2D.FillCircle(spriteBatch, outer, 3.5f, Color.Lerp(color, Color.White, .42f));
            Primitives2D.FillCircle(spriteBatch, outer - new Vector2(.8f, .8f), 1.2f, Color.White * .55f);
        }
        Primitives2D.FillCircle(spriteBatch, center - new Vector2(0, 2), 7, ChapelWarm * .8f);
        Primitives2D.FillCircle(spriteBatch, center - new Vector2(1.5f, 4f), 2.5f, Color.White * .4f);
    }

    private static void DrawVestmentMirror(SpriteBatch spriteBatch, Vector2 at, Color color, float time, float wake)
    {
        var mirror = new Rectangle((int)at.X - 28, (int)at.Y - 47, 56, 68);
        var frameDepth = mirror;
        frameDepth.Offset(5, 7);
        Primitives2D.FillRoundedRect(spriteBatch, frameDepth,
            Color.Lerp(new Color(28, 37, 54), Color.Black, .45f) * wake, UiTheme.LargeCornerRadiusPx);
        Primitives2D.FillRoundedRect(spriteBatch, mirror, new Color(28, 37, 54), UiTheme.LargeCornerRadiusPx);
        Primitives2D.RoundedRectOutline(spriteBatch, mirror, color * wake, 4, UiTheme.LargeCornerRadiusPx);
        // A soft diagonal gloss streak sells "glass" over a flat panel.
        var glossInner = mirror;
        glossInner.Inflate(-6, -6);
        Primitives2D.FillQuad(spriteBatch,
            new Vector2(glossInner.X, glossInner.Bottom), new Vector2(glossInner.X + glossInner.Width * .35f, glossInner.Top),
            new Vector2(glossInner.X + glossInner.Width * .55f, glossInner.Top), new Vector2(glossInner.X + glossInner.Width * .2f, glossInner.Bottom),
            Color.White * .07f);

        float phase = MathF.Floor(time * 4f) * MathF.PI / 4f;
        Vector2 projectile = at + new Vector2(MathF.Cos(phase) * 9, -14 + MathF.Sin(phase) * 4);
        Primitives2D.FillPolygon(spriteBatch, new[]
        {
            projectile + new Vector2(16, 0), projectile + new Vector2(-4, -8),
            projectile + new Vector2(-13, 0), projectile + new Vector2(-4, 8),
        }, Cosmetics.SelectedProjectile.Core);
        Primitives2D.PolygonOutline(spriteBatch, new[]
        {
            projectile + new Vector2(16, 0), projectile + new Vector2(-4, -8),
            projectile + new Vector2(-13, 0), projectile + new Vector2(-4, 8),
        }, Cosmetics.SelectedProjectile.Edge, 2);

        // A short extruded base plants the standing mirror on the plinth.
        Vector2[] stand =
        {
            at + new Vector2(-14, 21), at + new Vector2(14, 21),
            at + new Vector2(10, 29), at + new Vector2(-10, 29),
        };
        DrawExtrudedPolygon(spriteBatch, stand, new Vector2(0, 5), new Color(24, 30, 44), wake * .85f);
    }

    private static void DrawTrialBrazier(SpriteBatch spriteBatch, Vector2 at, Color color,
        float time, float wake, bool enabled)
    {
        // Extruded urn body -- was a single flat trapezoid, now a real
        // pedestal with a lit top rim and shaded sides.
        Vector2[] bowl =
        {
            new Vector2(at.X - 29, at.Y - 1), new Vector2(at.X + 29, at.Y - 1),
            new Vector2(at.X + 19, at.Y + 17), new Vector2(at.X - 19, at.Y + 17),
        };
        DrawExtrudedPolygon(spriteBatch, bowl, new Vector2(0, 8), new Color(51, 36, 40), wake);
        // An elliptical rim opening (rather than the old dead-straight top
        // edge) so the urn reads as being looked slightly down into.
        var rim = new Rectangle((int)at.X - 29, (int)at.Y - 8, 58, 14);
        Primitives2D.FillEllipse(spriteBatch, rim, Color.Lerp(new Color(30, 20, 22), Color.Black, .35f) * wake);
        Primitives2D.EllipseOutline(spriteBatch, rim, color * wake, 3);

        int flameHeight = 24 + (int)MathF.Round(MathF.Sin(time * (enabled ? 5f : 2f)) * 5);
        Color flame = enabled ? UiTheme.Red : UiTheme.Muted;
        // A wider, dimmer outer glow layer behind the core flame gives the
        // fire itself a little depth instead of one flat silhouette.
        Primitives2D.FillPolygon(spriteBatch, new[]
        {
            at + new Vector2(-21, 0), at + new Vector2(-9, -flameHeight - 4),
            at + new Vector2(0, -flameHeight + 5), at + new Vector2(11, -flameHeight - 12),
            at + new Vector2(22, 0),
        }, flame * (enabled ? .35f : .18f));
        Primitives2D.FillPolygon(spriteBatch, new[]
        {
            at + new Vector2(-17, -2), at + new Vector2(-7, -flameHeight),
            at + new Vector2(0, -flameHeight + 9), at + new Vector2(9, -flameHeight - 8),
            at + new Vector2(18, -2),
        }, flame * (enabled ? .92f : .48f));
        if (enabled)
            Primitives2D.RectOutline(spriteBatch,
                new Rectangle((int)at.X - 37, (int)at.Y - 52, 74, 68),
                Color.Lerp(UiTheme.Red, UiTheme.Gold, .35f) * .52f, 3);
    }

    private static void DrawPathPortal(SpriteBatch spriteBatch, GameSession session, GamePath path,
        Vector2 world, float time, SoulPortalPresentationState state, float pull, bool showPrompt)
    {
        Vector2 center = Screen(session, world);
        int mastery = MasteryTier(GameProfile.Profile.PathMastery.GetValueOrDefault(path.Key));
        int selectedNg = NewGamePlus.SelectedLevel(path.Key);
        float wake = PortalWake(state, pull) + mastery * .055f;
        float radius = Simulation.TileSize * (.82f - pull * .18f);
        DrawPortalShadow(spriteBatch, center, radius);
        DrawPortalAmbience(spriteBatch, center, radius, path.Accent, time, wake, StableSeed(path.Key));
        Primitives2D.FillCircle(spriteBatch, center, radius * .72f, new Color(12, 10, 18));

        switch (path.Key)
        {
            case "sound":
                DrawSoundPortal(spriteBatch, center, radius, path.Accent, time, wake);
                break;
            case "touch":
                DrawTouchPortal(spriteBatch, center, radius, path.Accent, time, wake);
                break;
            case "sight":
                DrawSightPortal(spriteBatch, center, radius, path.Accent, time, wake);
                break;
            case "chemesthesis":
                DrawChemPortal(spriteBatch, center, radius, path.Accent, time, wake);
                break;
            case "phantasia":
                DrawPhantasiaPortal(spriteBatch, center, radius, path.Accent, time, wake);
                break;
        }

        for (int tier = 0; tier < mastery; tier++)
        {
            float angle = tier * MathF.Tau / Math.Max(1, mastery) + MathF.Floor(time * 3f) * .035f;
            Vector2 at = center + Direction(angle) * radius * 1.22f;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 3, (int)at.Y - 3, 6, 6), UiTheme.Gold * .8f);
        }
        for (int tier = 0; tier < selectedNg; tier++)
        {
            float angle = tier * 2.19f - MathF.Floor(time * 4f) * .03f;
            Vector2 at = center + Direction(angle) * radius * 1.42f;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 4, (int)at.Y - 4, 8, 8), UiTheme.Red * .72f);
            if (tier < 5)
            {
                Vector2 inner = center + Direction(angle + .12f) * radius * .72f;
                Vector2 bend = center + Direction(angle - .08f) * radius * 1.02f;
                Primitives2D.Line(spriteBatch, inner, bend, UiTheme.Red * .48f, 2);
                Primitives2D.Line(spriteBatch, bend, at, UiTheme.Red * .32f, 2);
            }
        }

        UiTheme.DrawText(spriteBatch, path.Title, 10, path.Accent,
            new Vector2(center.X, center.Y + radius + 12), "midtop");
        int unlockedNg = NewGamePlus.UnlockedLevel(path.Key);
        string ngLabel = unlockedNg == 0
            ? "NORMAL  //  COMPLETE TO UNLOCK NG+"
            : selectedNg == 0 ? $"NORMAL  //  NG+{unlockedNg} UNLOCKED" : $"NG+{selectedNg}  //  MAX {unlockedNg}";
        UiTheme.DrawText(spriteBatch, ngLabel, 8,
            selectedNg == 0 ? UiTheme.Muted : UiTheme.Gold,
            new Vector2(center.X, center.Y + radius + 29), "midtop");
        if (showPrompt)
            UiTheme.DrawText(spriteBatch, "F  //  ENTER", 9, UiTheme.Cream,
                new Vector2(center.X, center.Y + radius + 46), "midtop");
    }

    private static void DrawSoundPortal(SpriteBatch spriteBatch, Vector2 center, float radius,
        Color color, float time, float wake)
    {
        for (int ring = 0; ring < 3; ring++)
        {
            float wave = ((time * (13 + ring * 3)) + ring * 17) % 18;
            Primitives2D.CircleOutline(spriteBatch, center, radius * (.55f + ring * .18f) + wave,
                color * (.68f - ring * .12f) * wake, 2 + ring % 2);
        }
        for (int bar = 0; bar < 5; bar++)
        {
            int height = 16 + (int)MathF.Round((.5f + .5f * MathF.Sin(time * 3f + bar)) * 25);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)center.X - 24 + bar * 10, (int)center.Y - height / 2, 5, height),
                Color.Lerp(color, Color.White, .28f) * wake);
        }
    }

    private static void DrawTouchPortal(SpriteBatch spriteBatch, Vector2 center, float radius,
        Color color, float time, float wake)
    {
        int extent = (int)radius;
        var outer = new Rectangle((int)center.X - extent, (int)center.Y - extent, extent * 2, extent * 2);
        // A square ripple, echoing the frame outward and fading -- the
        // "something is pressing outward" read this portal was missing.
        float ripple = (time * 22f) % (radius * .6f);
        var pulse = outer;
        pulse.Inflate((int)ripple, (int)ripple);
        Primitives2D.RectOutline(spriteBatch, pulse, color * wake * Math.Max(0f, .32f - ripple / (radius * 2f)), 2);
        Primitives2D.RectOutline(spriteBatch, outer, color * wake, 5);
        var inner = outer;
        inner.Inflate(-13, -13);
        Primitives2D.RectOutline(spriteBatch, inner, color * .55f * wake, 3);
        float valve = MathF.Floor(time * 4f) * MathF.PI / 4f;
        for (int spoke = 0; spoke < 6; spoke++)
            Primitives2D.Line(spriteBatch, center,
                center + Direction(valve + spoke * MathF.Tau / 6f) * radius * .58f,
                color * .76f * wake, 4);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)center.X - 8, (int)center.Y - 8, 16, 16), ChapelWarm * .65f);
    }

    private static void DrawSightPortal(SpriteBatch spriteBatch, Vector2 center, float radius,
        Color color, float time, float wake)
    {
        Primitives2D.EllipseOutline(spriteBatch,
            new Rectangle((int)(center.X - radius), (int)(center.Y - radius * .58f),
                (int)(radius * 2), (int)(radius * 1.16f)), color * wake, 4);
        float iris = radius * (.26f + .08f * MathF.Sin(time * 2.4f));
        Primitives2D.FillCircle(spriteBatch, center, iris, color * .72f * wake);
        Primitives2D.FillCircle(spriteBatch, center, iris * .38f, UiTheme.Ink);
        float scan = MathF.Floor(time * 12f) * MathF.PI / 24f;
        // Fading echoes of the last couple of scan positions, trailing
        // behind the live beam -- the same "motion leaves a trace" idea
        // Aphantasia's tentacle echoes use, applied to this portal's own
        // sweeping scanline instead of a new shape.
        for (int echo = 2; echo >= 1; echo--)
        {
            float echoScan = scan - echo * MathF.PI / 24f;
            float echoAlpha = .3f / echo;
            Primitives2D.Line(spriteBatch, center + Direction(echoScan) * iris,
                center + Direction(echoScan) * radius * 1.12f,
                Color.Lerp(color, Color.White, .45f) * (echoAlpha * wake), 1);
        }
        Primitives2D.Line(spriteBatch, center + Direction(scan) * iris,
            center + Direction(scan) * radius * 1.18f, Color.Lerp(color, Color.White, .45f) * .66f, 2);
    }

    private static void DrawChemPortal(SpriteBatch spriteBatch, Vector2 center, float radius,
        Color color, float time, float wake)
    {
        Primitives2D.CircleOutline(spriteBatch, center, radius, color * wake, 5);
        for (int crack = 0; crack < 8; crack++)
        {
            float angle = crack * MathF.Tau / 8f;
            Vector2 inner = center + Direction(angle + .13f) * radius * .3f;
            Vector2 outer = center + Direction(angle) * radius;
            Primitives2D.Line(spriteBatch, inner, outer, color * .68f * wake, 3);
        }
        for (int ember = 0; ember < 6; ember++)
        {
            int rise = (int)((time * (17 + ember) + ember * 19) % (radius * 1.25f));
            Vector2 at = center + new Vector2(-radius * .55f + ember * radius * .22f, radius * .45f - rise);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 3, (int)at.Y - 3, 6, 6),
                Color.Lerp(color, UiTheme.Gold, .5f) * wake);
        }
    }

    private static void DrawPhantasiaPortal(SpriteBatch spriteBatch, Vector2 center, float radius,
        Color color, float time, float wake)
    {
        float phase = MathF.Floor(time * 6f) * MathF.PI / 12f;
        // A fading echo of the star's last position, one step behind the
        // live rotation -- reuses the polygon build below rather than a
        // whole new shape, same trailing-motion idea as the sight portal's
        // scanline echo.
        var echoStar = new Vector2[16];
        for (int point = 0; point < echoStar.Length; point++)
        {
            float angle = phase - MathF.PI / 12f + point * MathF.Tau / echoStar.Length;
            float r = point % 2 == 0 ? radius : radius * .54f;
            echoStar[point] = center + Direction(angle) * r;
        }
        Primitives2D.PolygonOutline(spriteBatch, echoStar, color * wake * .22f, 2);

        var star = new Vector2[16];
        for (int point = 0; point < star.Length; point++)
        {
            float angle = phase + point * MathF.Tau / star.Length;
            float r = point % 2 == 0 ? radius : radius * .54f;
            star[point] = center + Direction(angle) * r;
        }
        Primitives2D.PolygonOutline(spriteBatch, star, color * wake, 4);
        for (int satellite = 0; satellite < 4; satellite++)
        {
            float angle = -phase * .6f + satellite * MathF.Tau / 4f;
            Vector2 at = center + Direction(angle) * radius * .72f;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 4, (int)at.Y - 4, 8, 8),
                Color.Lerp(color, Color.White, .45f) * wake);
        }
    }

    private static void DrawSoulRose(SpriteBatch spriteBatch, GameSession session, Vector2 world,
        float time, SoulPortalPresentationState state, float pull)
    {
        Vector2 center = Screen(session, world);
        float wake = PortalWake(state, pull);
        float radius = Simulation.TileSize * (1.12f - pull * .25f);
        DrawPortalShadow(spriteBatch, center, radius * 1.18f);
        Primitives2D.FillCircle(spriteBatch, center, radius * .86f, new Color(10, 8, 16));
        Primitives2D.CircleOutline(spriteBatch, center, radius * 1.08f, UiTheme.Gold * wake, 4);

        int completed = 0;
        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            GamePath path = GamePaths.Paths[index];
            int mastery = MasteryTier(GameProfile.Profile.PathMastery.GetValueOrDefault(path.Key));
            if (mastery > 0)
                completed++;
            float angle = -MathF.PI / 2f + index * MathF.Tau / GamePaths.Paths.Count;
            float orbit = radius * (.62f - pull * .16f);
            Vector2 lobe = center + Direction(angle) * orbit;
            float lobeRadius = radius * (.31f + mastery * .012f);
            Color color = path.Accent * (mastery > 0 ? wake : wake * .55f);
            Primitives2D.FillCircle(spriteBatch, lobe, lobeRadius, new Color(19, 15, 27));

            switch (path.Key)
            {
                case "sound":
                    Primitives2D.CircleOutline(spriteBatch, lobe, lobeRadius, color, 3);
                    Primitives2D.CircleOutline(spriteBatch, lobe, lobeRadius * .58f, color * .7f, 2);
                    break;
                case "touch":
                    Primitives2D.RectOutline(spriteBatch,
                        new Rectangle((int)(lobe.X - lobeRadius), (int)(lobe.Y - lobeRadius),
                            (int)(lobeRadius * 2), (int)(lobeRadius * 2)), color, 3);
                    break;
                case "sight":
                    Primitives2D.EllipseOutline(spriteBatch,
                        new Rectangle((int)(lobe.X - lobeRadius), (int)(lobe.Y - lobeRadius * .55f),
                            (int)(lobeRadius * 2), (int)(lobeRadius * 1.1f)), color, 3);
                    Primitives2D.FillCircle(spriteBatch, lobe, lobeRadius * .28f, color);
                    break;
                case "chemesthesis":
                    Primitives2D.CircleOutline(spriteBatch, lobe, lobeRadius, color, 3);
                    for (int crack = 0; crack < 4; crack++)
                        Primitives2D.Line(spriteBatch, lobe,
                            lobe + Direction(crack * MathF.PI / 2f) * lobeRadius, color * .7f, 2);
                    break;
                case "phantasia":
                    var diamond = new[]
                    {
                        lobe + new Vector2(0, -lobeRadius), lobe + new Vector2(lobeRadius, 0),
                        lobe + new Vector2(0, lobeRadius), lobe + new Vector2(-lobeRadius, 0),
                    };
                    Primitives2D.PolygonOutline(spriteBatch, diamond, color, 3);
                    break;
            }

            float packetPhase = MathF.Floor((time * (8f + index) + index * 9f) % 24f) / 24f;
            Vector2 packet = Vector2.Lerp(lobe, center, packetPhase);
            int packetSize = 4 + mastery;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)packet.X - packetSize / 2, (int)packet.Y - packetSize / 2,
                    packetSize, packetSize),
                Color.Lerp(path.Accent, Color.White, .35f) * wake);
        }

        float corePulse = 10 + completed * 1.5f
            + MathF.Floor((.5f + .5f * MathF.Sin(time * 2.3f)) * 4f);
        Primitives2D.FillCircle(spriteBatch, center, corePulse,
            Color.Lerp(UiTheme.Purple, UiTheme.Gold, .45f + completed * .06f));
        Primitives2D.CircleOutline(spriteBatch, center, radius * .32f, UiTheme.Cream * .72f * wake, 3);
        UiTheme.DrawText(spriteBatch, "THE BODY / THE SOUL", 12, UiTheme.Gold,
            new Vector2(center.X, center.Y + radius + 15), "midtop");
        UiTheme.DrawText(spriteBatch, "CAMPAIGN DESCENT", 8, UiTheme.Cream,
            new Vector2(center.X, center.Y + radius + 34), "midtop");
        if (state == SoulPortalPresentationState.Approached)
            UiTheme.DrawText(spriteBatch, "F  //  TRAVERSE", 10, UiTheme.Gold,
                new Vector2(center.X, center.Y + radius + 52), "midtop");
    }

    private static void DrawPortalShadow(SpriteBatch spriteBatch, Vector2 center, float radius)
    {
        Primitives2D.FillEllipse(spriteBatch,
            new Rectangle((int)(center.X - radius * 1.05f), (int)(center.Y + radius * .48f),
                (int)(radius * 2.1f), (int)(radius * .55f)),
            UiTheme.Shadow * .8f);
    }

    /// <summary>
    /// Shared ambient dressing for every sense's path portal (the older,
    /// plainer 2D portals compared to the Aphantasia/Composite portals'
    /// twinkling stars and echo trails): a soft breathing double halo plus a
    /// handful of drifting motes orbiting the rim, in the portal's own
    /// accent color. Deterministic/time-driven rather than pooled-particle
    /// (BitVfxSystem.EmitBurst is tuned for one-shot combat events, not a
    /// continuous ambient loop like this), matching how the rest of this
    /// file already animates portals purely off `time`. Drawn once, shared
    /// by every DrawPathPortal call, so each sense's own method only needs
    /// to add what makes it distinct (see the echo touches in
    /// DrawSightPortal/DrawPhantasiaPortal).
    /// </summary>
    private static void DrawPortalAmbience(SpriteBatch spriteBatch, Vector2 center, float radius,
        Color color, float time, float wake, int seed)
    {
        float breathe = 1f + .05f * MathF.Sin(time * 1.3f + seed);
        Primitives2D.CircleOutline(spriteBatch, center, radius * 1.32f * breathe, color * .22f * wake, 2);
        Primitives2D.CircleOutline(spriteBatch, center, radius * 1.5f * breathe, color * .12f * wake, 1);

        const int moteCount = 7;
        for (int index = 0; index < moteCount; index++)
        {
            float speed = .35f + index % 3 * .12f;
            float orbitRadius = radius * (1.05f + index % 4 * .09f);
            float direction = index % 2 == 0 ? 1f : -1f;
            float angle = seed * .37f + index * (MathF.Tau / moteCount) + time * speed * direction;
            float bob = MathF.Sin(time * (1.4f + index * .13f) + index) * radius * .08f;
            Vector2 at = center + Direction(angle) * orbitRadius + new Vector2(0, bob);
            float twinkle = .35f + .45f * (.5f + .5f * MathF.Sin(time * (2.2f + index * .21f) + index * 3f));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)at.X - 2, (int)at.Y - 2, 4, 4),
                Color.Lerp(color, Color.White, .3f) * (twinkle * wake));
        }
    }

    private static float PortalWake(SoulPortalPresentationState state, float pull) => state switch
    {
        SoulPortalPresentationState.Approached => 1f,
        SoulPortalPresentationState.Confirming => 1.18f,
        SoulPortalPresentationState.Committing => 1.25f + pull * .85f,
        _ => .68f,
    };

    private static (string Label, Color Accent) StationIdentity(string key) => key switch
    {
        "storage" => ("VAULT RELIQUARY", UiTheme.Gold),
        "quests" => ("VOW LECTERN", UiTheme.Green),
        "skills" => ("MIND ROSE", UiTheme.Purple),
        "wardrobe" => ("VESTMENT MIRROR", UiTheme.Blue),
        "hard_mode" => (GameProfile.Profile.NoHealingEnabled ? "NO HEALING LIT" : "NO HEALING BRAZIER",
            GameProfile.Profile.NoHealingEnabled ? UiTheme.Red : UiTheme.Muted),
        "no_extract" => (GameProfile.Profile.NoExtractEnabled ? "NO EXTRACT LIT" : "NO EXTRACT BRAZIER",
            GameProfile.Profile.NoExtractEnabled ? UiTheme.Purple : UiTheme.Muted),
        "golden_flame" => (GameProfile.Profile.GoldenFlameEnabled ? "GOLDEN FLAME LIT" : "GOLDEN FLAME BRAZIER",
            GameProfile.Profile.GoldenFlameEnabled ? UiTheme.Gold : UiTheme.Muted),
        "the_void" => (GameProfile.Profile.VoidEnabled ? "THE VOID LIT" : "THE VOID BRAZIER",
            GameProfile.Profile.VoidEnabled ? Color.Lerp(UiTheme.Void, UiTheme.Purple, .4f) : UiTheme.Muted),
        "developer_armory" => ("DEVELOPER ARMORY", UiTheme.Gold),
        _ => (key.ToUpperInvariant(), UiTheme.Cream),
    };

    private static void DrawCandle(SpriteBatch spriteBatch, Vector2 at, float time, int seed)
    {
        Primitives2D.FillRect(spriteBatch, new Rectangle((int)at.X - 3, (int)at.Y - 10, 6, 12), UiTheme.Cream * .72f);
        int flame = 4 + (int)MathF.Floor((.5f + .5f * MathF.Sin(time * 5f + seed)) * 3f);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)at.X - 2, (int)at.Y - 12 - flame, 4, flame),
            ChapelWarm * .88f);
    }

    private static int StableSeed(string value)
    {
        int hash = 17;
        foreach (char character in value)
            hash = unchecked(hash * 31 + character);
        return hash;
    }

    private static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    private static Vector2 Screen(GameSession session, Vector2 world) =>
        session.Camera.WorldToScreen(world, session.PlayerWorldCenter, Vector2.Zero);
}
