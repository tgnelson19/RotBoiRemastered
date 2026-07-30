using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.World;

/// <summary>
/// One grounded object that participates in the arena's painter-order pass.
/// <paramref name="WorldAnchor"/> is the point where the object meets the
/// floor, not the top of its artwork. PaintPriority only breaks exact depth
/// ties; screen-space ground depth remains the primary ordering rule.
/// </summary>
public enum WorldDepthDrawKind
{
    Bullet,
    Encounter,
    Enemy,
    Player,
    EnemyProjectile,
}

public readonly record struct WorldDepthDrawItem(
    Vector2 WorldAnchor,
    int PaintPriority,
    int StableOrder,
    WorldDepthDrawKind Kind,
    object Drawable);

/// <summary>
/// Bakes each Battleground's floor plane into a RenderTarget2D once, then
/// draws it every frame as a single rotated sprite -- plus a small per-frame
/// set of camera-facing wall/decoration polygons. Ported from background.py's
/// drawRepasteableBackground/moveAndDisplayBackground/_raised_scenery/
/// _wall_screen_geometry/_draw_camera_facing_wall/_decoration_screen_rect/
/// _draw_raised_decoration/_draw_floor_detail.
///
/// Cleanup vs. the Python original: Python bakes the ground plane into a CPU
/// pygame.Surface for the same reason walls/decorations stay per-frame here
/// (Primitives2D.FillPolygon costs one SpriteBatch.Draw call per scanline
/// row -- see its own doc comment -- so redrawing thousands of floor tiles
/// every frame would be far too many draw calls), but then layers an
/// elaborate downsample/cache/rotate/rescale pipeline on top
/// (moveAndDisplayBackground) purely to make pygame.transform.rotate
/// affordable on a multi-thousand-pixel CPU surface every frame. That entire
/// pipeline is dropped here: MonoGame's SpriteBatch.Draw rotation is a single
/// hardware-accelerated GPU call regardless of source texture size, so the
/// baked RenderTarget2D is drawn directly with a rotation/origin every frame
/// -- no caching needed.
///
/// The rotation angle handed to SpriteBatch.Draw is derived, not guessed:
/// Camera.WorldVectorToScreen((dx,dy)) = (dx*cos(t)+dy*sin(t), -dx*sin(t)+dy*cos(t))
/// for camera angle t. SpriteBatch's own rotation transform for an offset
/// (x,y) from `origin` is (x*cos(r)-y*sin(r), x*sin(r)+y*cos(r)). Matching
/// coefficients for all (dx,dy) requires cos(r)=cos(t) and sin(r)=-sin(t),
/// i.e. r = -t. Verified visually (see the visual smoke test run for this
/// pass): the baked ground rotates the same direction as entities under
/// camera rotation.
/// </summary>
public sealed class ArenaRenderer
{
    private static readonly Color VoidColor = new(15, 18, 25);
    private static readonly Color GridLineColor = new(48, 51, 60);
    private static readonly Color RoadEdgeDark = new(28, 30, 37);
    private static readonly Color RoadEdgeLight = new(67, 65, 72);
    private static readonly Color BuildingInset = new(24, 27, 35);
    private static readonly Color CableDoodle = new(26, 29, 36);
    private static readonly Color DecorationShadow = new(20, 22, 29);
    private static readonly RasterizerState ScissorRasterizerState = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    private Battleground? _bakedFor;
    private RenderTarget2D? _bakedGround;
    private List<(int X, int Y, TileType Tile, int Biome)> _walls = new();
    private List<(int X, int Y, int Biome)> _decorations = new();
    private List<PathDecoration> _pathRaisedDecorations = new();
    private readonly List<(float ScreenY, int Kind, int X, int Y, TileType Tile, int Biome, PathDecoration? PathDecoration)>
        _visibleItemScratch = new();
    private readonly List<DepthSceneItem> _depthSceneScratch = new();

    /// <summary>No-op once already baked for this exact Battleground reference. Call once at the top of the frame, before the frame's own SpriteBatch.Begin().</summary>
    public void EnsureBaked(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Battleground battleground)
    {
        if (ReferenceEquals(_bakedFor, battleground))
            return;

        (_walls, _decorations) = ComputeRaisedScenery(battleground);
        _pathRaisedDecorations = battleground.PathDecorations
            .Where(decoration => decoration.Layer == PathDecorationLayer.Raised)
            .ToList();

        var previousTargets = graphicsDevice.GetRenderTargets();
        var target = new RenderTarget2D(graphicsDevice, battleground.Width * Battleground.TileSize, battleground.Height * Battleground.TileSize);
        graphicsDevice.SetRenderTarget(target);
        graphicsDevice.Clear(VoidColor);
        spriteBatch.Begin();
        for (int y = 0; y < battleground.Height; y++)
        {
            for (int x = 0; x < battleground.Width; x++)
            {
                var tile = battleground.TileAt(x, y);
                var rect = battleground.TileRect(x, y);
                int biome = battleground.BiomeForTile(x, y);
                var palette = battleground.Palettes[biome];
                Color color;
                if (tile == TileType.OuterVoid)
                    color = VoidColor;
                else if (tile.IsSolid())
                    color = palette.Ground;
                else if (tile == TileType.Road)
                    color = palette.Road;
                else if (tile == TileType.BuildingFloor)
                    color = palette.Interior;
                else
                    color = (x + y) % 7 == 0 ? palette.GroundAlt : palette.Ground;
                if (battleground.PathFloorNumber > 5 && tile != TileType.OuterVoid)
                    color = Color.Lerp(color, SecondActTint(battleground.VisualThemeKey), .13f);
                Primitives2D.FillRect(spriteBatch, rect, color);
                if (!tile.IsSolid())
                {
                    Primitives2D.RectOutline(spriteBatch, rect, GridLineColor, 1);
                    DrawFloorDetail(spriteBatch, rect, tile, x, y, palette, battleground.VisualThemeKey);
                }
            }
        }
        foreach (var decoration in battleground.PathDecorations)
        {
            if (decoration.Layer is PathDecorationLayer.Floor or PathDecorationLayer.Low)
                DrawPathFloorDecoration(spriteBatch, battleground, decoration);
        }
        spriteBatch.End();
        if (previousTargets.Length == 0)
            graphicsDevice.SetRenderTarget(null);
        else
            graphicsDevice.SetRenderTargets(previousTargets);

        _bakedGround?.Dispose();
        _bakedGround = target;
        _bakedFor = battleground;
    }

    /// <summary>Ported from _draw_floor_detail: cheap per-tile cosmetic doodles for non-solid floor tiles.</summary>
    private static void DrawFloorDetail(
        SpriteBatch spriteBatch, Rectangle rect, TileType tile, int tileX, int tileY,
        BiomePalette palette, string? visualThemeKey)
    {
        int noise = (tileX * 37 + tileY * 71 + tileX * tileY * 3) % 113;
        if (visualThemeKey is not null)
        {
            DrawThemedFloorDetail(spriteBatch, rect, tile, noise, palette, visualThemeKey);
            return;
        }

        if (tile == TileType.Road)
        {
            Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Top), RoadEdgeDark, 2);
            Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Bottom), new Vector2(rect.Right, rect.Bottom), RoadEdgeLight, 1);
            if (noise % 4 == 0)
                Primitives2D.FillRect(spriteBatch, new Rectangle(rect.Center.X - 6, rect.Center.Y - 2, 12, 4), palette.Accent);
        }
        else if (tile == TileType.BuildingFloor)
        {
            var inset = rect;
            inset.Inflate(-12, -12);
            Primitives2D.RectOutline(spriteBatch, inset, BuildingInset, 2);
            if (noise % 3 == 0)
                Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 7, rect.Y + 7, 5, 5), palette.Detail);
        }
        else if (tile == TileType.Default)
        {
            if (noise < 9)
            {
                var p1 = new Vector2(rect.X + 9, rect.Y + 14);
                var p2 = new Vector2(rect.X + 20 + noise, rect.Y + 18 + noise / 2);
                var p3 = new Vector2(rect.X + 24 + noise, rect.Y + 29);
                Primitives2D.Line(spriteBatch, p1, p2, CableDoodle, 2);
                Primitives2D.Line(spriteBatch, p2, p3, CableDoodle, 1);
            }
            else if (noise is 31 or 63 or 91)
            {
                Primitives2D.FillRect(spriteBatch, new Rectangle(rect.Center.X - 2, rect.Center.Y - 2, 4, 4), palette.Accent);
            }
        }
    }

    private static Color SecondActTint(string? themeKey) => themeKey switch
    {
        "touch" => new Color(34, 45, 20),
        "sight" => new Color(24, 55, 72),
        "sound" => new Color(36, 31, 49),
        "phantasia" => new Color(25, 8, 40),
        "chemesthesis" => new Color(59, 22, 12),
        _ => VoidColor,
    };

    private static void DrawThemedFloorDetail(
        SpriteBatch spriteBatch, Rectangle rect, TileType tile, int noise,
        BiomePalette palette, string themeKey)
    {
        Vector2 center = rect.Center.ToVector2();
        switch (themeKey)
        {
            case "touch":
                if (tile == TileType.Road)
                {
                    Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X, rect.Center.Y - 8, rect.Width, 16), new Color(24, 38, 25));
                    Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Center.Y - 8), new Vector2(rect.Right, rect.Center.Y - 8), palette.Detail * .55f, 2);
                    if (noise % 3 == 0)
                        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.Center.X - 8, rect.Center.Y - 2, 16, 4), palette.Accent * .75f);
                }
                else
                {
                    int seam = rect.Y + (noise % 2 == 0 ? 15 : 34);
                    Primitives2D.Line(spriteBatch, new Vector2(rect.Left, seam), new Vector2(rect.Right, seam), new Color(12, 22, 17), 2);
                    if (noise % 4 == 0)
                        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 7 + noise % 19, rect.Y + 8, 5, 4), palette.Detail * .55f);
                }
                break;

            case "sight":
                if (noise % 2 == 0 || tile == TileType.Road)
                {
                    int offset = 8 + noise % 25;
                    Primitives2D.Arc(spriteBatch,
                        new Rectangle(rect.X + 4, rect.Y + offset, rect.Width - 8, 13),
                        MathF.PI, MathF.Tau, palette.Detail * .48f, tile == TileType.Road ? 2 : 1, 16);
                }
                if (noise % 5 == 0)
                    Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 9, rect.Y + 11, 8, 2), palette.Accent * .65f);
                break;

            case "sound":
                if (tile == TileType.Road || noise % 4 == 0)
                {
                    int y = rect.Y + 13 + noise % 19;
                    Primitives2D.Line(spriteBatch, new Vector2(rect.X + 5, y), new Vector2(rect.Right - 7, y - 5), palette.Detail * .42f, 2);
                    Primitives2D.FillRect(spriteBatch, new Rectangle(rect.Right - 11, y - 8, 5, 3), palette.Accent * .55f);
                }
                break;

            case "phantasia":
                if (noise % 3 == 0 || tile == TileType.Road)
                {
                    int size = noise % 11 == 0 ? 4 : 2;
                    Primitives2D.FillRect(spriteBatch,
                        new Rectangle(rect.X + 5 + noise % 31, rect.Y + 6 + noise % 27, size, size),
                        noise % 5 == 0 ? palette.Accent : palette.Detail);
                }
                if (tile == TileType.Road && noise % 2 == 0)
                    Primitives2D.Line(spriteBatch, center - new Vector2(14, 8), center + new Vector2(13, 7), palette.Accent * .5f, 1);
                break;

            case "chemesthesis":
                if (noise % 3 == 0 || tile == TileType.Road)
                {
                    var points = new[]
                    {
                        new Vector2(rect.X + 4, rect.Y + 9 + noise % 17),
                        center + new Vector2(-4, noise % 7 - 3),
                        new Vector2(rect.Right - 7, rect.Bottom - 10),
                    };
                    Primitives2D.Polyline(spriteBatch, points, false, new Color(27, 20, 14), tile == TileType.Road ? 2 : 1);
                }
                if (noise % 7 == 0)
                    Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 10, rect.Bottom - 12, 6, 4), palette.Detail * .55f);
                break;
        }
    }

    private static void DrawPathFloorDecoration(
        SpriteBatch spriteBatch, Battleground battleground, PathDecoration decoration)
    {
        int tileX = Math.Clamp((int)(decoration.WorldPosition.X / Battleground.TileSize), 0, battleground.Width - 1);
        int tileY = Math.Clamp((int)(decoration.WorldPosition.Y / Battleground.TileSize), 0, battleground.Height - 1);
        var palette = battleground.Palettes[battleground.BiomeForTile(tileX, tileY)];
        Vector2 center = decoration.WorldPosition;
        float scale = decoration.Scale;
        int width = Math.Max(10, (int)(38 * scale));
        int height = Math.Max(6, (int)(18 * scale));
        Rectangle area = new((int)center.X - width / 2, (int)center.Y - height / 2, width, height);

        switch (decoration.Kind)
        {
            case PathDecorationKind.SewerChannel:
                if (decoration.Variant % 2 != 0)
                    area = new Rectangle((int)center.X - height / 2, (int)center.Y - width / 2, height, width);
                Primitives2D.FillRect(spriteBatch, area, new Color(15, 25, 18));
                var channel = area;
                channel.Inflate(decoration.Variant % 2 == 0 ? -2 : -area.Width / 3,
                    decoration.Variant % 2 == 0 ? -area.Height / 3 : -2);
                Primitives2D.FillRect(spriteBatch, channel, new Color(57, 69, 35));
                Primitives2D.RectOutline(spriteBatch, area, palette.Detail * .62f, 2);
                break;

            case PathDecorationKind.SewerGrate:
                Primitives2D.FillRect(spriteBatch, area, new Color(16, 23, 20));
                Primitives2D.RectOutline(spriteBatch, area, palette.Detail, 2);
                for (int x = area.Left + 5; x < area.Right - 2; x += 7)
                    Primitives2D.Line(spriteBatch, new Vector2(x, area.Top + 3), new Vector2(x, area.Bottom - 3), palette.WallTop, 3);
                break;

            case PathDecorationKind.BrickRunes:
                Primitives2D.FillRect(spriteBatch, area, new Color(28, 36, 29));
                for (int row = 0; row < 3; row++)
                {
                    int y = area.Top + row * Math.Max(3, area.Height / 3);
                    Primitives2D.Line(spriteBatch, new Vector2(area.Left, y),
                        new Vector2(area.Right, y), palette.WallTop * .55f, 1);
                    int seam = area.Left + area.Width * (row % 2 == 0 ? 1 : 2) / 3;
                    Primitives2D.Line(spriteBatch, new Vector2(seam, y),
                        new Vector2(seam, Math.Min(area.Bottom, y + area.Height / 3)),
                        palette.WallTop * .55f, 1);
                }
                Primitives2D.Polyline(spriteBatch, new[]
                {
                    new Vector2(center.X - 8 * scale, center.Y),
                    new Vector2(center.X, center.Y - 6 * scale),
                    new Vector2(center.X + 8 * scale, center.Y),
                    new Vector2(center.X, center.Y + 6 * scale),
                }, true, palette.Accent * .82f, 2);
                break;

            case PathDecorationKind.SludgePool:
                Primitives2D.FillEllipse(spriteBatch, area, new Color(49, 61, 27));
                Primitives2D.EllipseOutline(spriteBatch, area, palette.Accent * .72f, 2, 28);
                Primitives2D.FillRect(spriteBatch, new Rectangle(area.X + width / 4, area.Y + height / 3, Math.Max(3, width / 7), 3), palette.Detail * .7f);
                break;

            case PathDecorationKind.WaterPool:
                Primitives2D.FillEllipse(spriteBatch, area, new Color(35, 88, 112));
                Primitives2D.EllipseOutline(spriteBatch, area, palette.WallTop, 2, 28);
                Primitives2D.Arc(spriteBatch, new Rectangle(area.X + width / 5, area.Y + height / 4, width / 2, height / 3),
                    0, MathF.PI, palette.Detail, 2, 14);
                break;

            case PathDecorationKind.CausticCurrent:
                for (int line = -1; line <= 1; line++)
                {
                    float y = center.Y + line * 7 * scale;
                    var current = new[]
                    {
                        new Vector2(area.Left, y), new Vector2(center.X - width * .18f, y - 4),
                        new Vector2(center.X + width * .18f, y + 4), new Vector2(area.Right, y - 1),
                    };
                    Primitives2D.Polyline(spriteBatch, current, false, line == 0 ? palette.Detail : palette.WallTop * .72f, 2);
                }
                break;

            case PathDecorationKind.MosaicLens:
                Primitives2D.FillPolygon(spriteBatch, new[]
                {
                    new Vector2(center.X, area.Top),
                    new Vector2(area.Right, center.Y),
                    new Vector2(center.X, area.Bottom),
                    new Vector2(area.Left, center.Y),
                }, new Color(31, 72, 91));
                Primitives2D.EllipseOutline(spriteBatch,
                    new Rectangle(area.X + width / 5, area.Y + 2,
                        Math.Max(5, width * 3 / 5), Math.Max(4, height - 4)),
                    palette.Detail, 2, 24);
                Primitives2D.FillCircle(spriteBatch, center, Math.Max(2, (int)(3 * scale)),
                    palette.Accent);
                break;

            case PathDecorationKind.CloudBank:
                Primitives2D.FillEllipse(spriteBatch, area, new Color(43, 48, 63));
                Primitives2D.FillEllipse(spriteBatch,
                    new Rectangle(area.X + width / 5, area.Y - height / 4, width / 2, height),
                    new Color(62, 66, 83));
                Primitives2D.Line(spriteBatch, new Vector2(area.X + 5, area.Bottom - 2), new Vector2(area.Right - 5, area.Bottom - 2), palette.Accent * .56f, 2);
                break;

            case PathDecorationKind.WindLane:
                for (int line = -1; line <= 1; line++)
                {
                    float y = center.Y + line * 6 * scale;
                    Primitives2D.Line(spriteBatch, new Vector2(area.Left, y + 4), new Vector2(area.Right - width / 5, y - 3), palette.Detail * .67f, 2);
                    Primitives2D.Line(spriteBatch, new Vector2(area.Right - width / 5, y - 3), new Vector2(area.Right, y), palette.Accent * .6f, 2);
                }
                break;

            case PathDecorationKind.StormCrack:
                Primitives2D.Polyline(spriteBatch, new[]
                {
                    new Vector2(center.X - width / 2f, center.Y - height / 2f),
                    new Vector2(center.X - width / 8f, center.Y - 2),
                    new Vector2(center.X - width / 5f, center.Y + 4),
                    new Vector2(center.X + width / 3f, center.Y + height / 2f),
                }, false, palette.Detail, Math.Max(2, (int)(2 * scale)));
                break;

            case PathDecorationKind.ResonanceTiles:
                Primitives2D.FillRect(spriteBatch, area, new Color(39, 39, 55));
                for (int band = 0; band < 3; band++)
                {
                    var bandRect = new Rectangle(
                        area.X + band * Math.Max(2, width / 9),
                        area.Y + band * Math.Max(1, height / 8),
                        Math.Max(5, width - band * Math.Max(4, width / 5)),
                        Math.Max(4, height - band * Math.Max(2, height / 4)));
                    Primitives2D.Arc(spriteBatch, bandRect, MathF.PI, MathF.Tau,
                        band == 0 ? palette.WallTop : palette.Accent * .72f, 2, 18);
                }
                Primitives2D.Line(spriteBatch,
                    new Vector2(area.Left + 4, area.Bottom - 3),
                    new Vector2(area.Right - 4, area.Bottom - 3),
                    palette.Detail * .7f, 2);
                break;

            case PathDecorationKind.StarField:
                Primitives2D.FillEllipse(spriteBatch, area, new Color(17, 10, 31));
                for (int star = 0; star < Math.Min(12, 4 + (int)(scale * 2)); star++)
                {
                    int px = area.X + 4 + Math.Abs((star * 37 + decoration.Variant * 11) % Math.Max(1, area.Width - 8));
                    int py = area.Y + 3 + Math.Abs((star * 23 + decoration.RoomId * 7) % Math.Max(1, area.Height - 6));
                    int size = star % 4 == 0 ? 3 : 2;
                    Primitives2D.FillRect(spriteBatch, new Rectangle(px, py, size, size), star % 3 == 0 ? palette.Accent : palette.Detail);
                }
                break;

            case PathDecorationKind.Nebula:
                Primitives2D.FillEllipse(spriteBatch, area, new Color(57, 23, 75));
                Primitives2D.FillEllipse(spriteBatch,
                    new Rectangle(area.X + width / 4, area.Y + height / 5, width / 2, height / 2),
                    palette.Accent * .48f);
                Primitives2D.FillRect(spriteBatch, new Rectangle(area.Center.X, area.Y + 4, 3, 3), palette.Detail);
                break;

            case PathDecorationKind.Constellation:
                var stars = new[]
                {
                    new Vector2(area.Left + 3, area.Bottom - 3), new Vector2(center.X - width * .12f, area.Top + 4),
                    new Vector2(center.X + width * .16f, center.Y + 3), new Vector2(area.Right - 4, area.Top + height * .3f),
                };
                Primitives2D.Polyline(spriteBatch, stars, false, palette.Accent * .65f, 1);
                foreach (var star in stars)
                    Primitives2D.FillRect(spriteBatch, new Rectangle((int)star.X - 2, (int)star.Y - 2, 4, 4), palette.Detail);
                break;

            case PathDecorationKind.VoidRift:
                var diamond = new[]
                {
                    new Vector2(center.X, area.Top), new Vector2(area.Right, center.Y),
                    new Vector2(center.X, area.Bottom), new Vector2(area.Left, center.Y),
                };
                Primitives2D.FillPolygon(spriteBatch, diamond, new Color(10, 5, 19));
                Primitives2D.PolygonOutline(spriteBatch, diamond, palette.Accent, Math.Max(2, (int)scale));
                break;

            case PathDecorationKind.DreamGlyph:
                Primitives2D.CircleOutline(spriteBatch, center,
                    Math.Max(7, (int)(11 * scale)), palette.Accent * .82f, 2, 24);
                for (int point = 0; point < 5; point++)
                {
                    float angle = -MathF.PI / 2f + point * MathF.Tau / 5f;
                    float nextAngle = -MathF.PI / 2f + ((point + 2) % 5) * MathF.Tau / 5f;
                    Primitives2D.Line(spriteBatch,
                        center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 10 * scale,
                        center + new Vector2(MathF.Cos(nextAngle), MathF.Sin(nextAngle)) * 10 * scale,
                        palette.Detail * .72f, 1);
                }
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)center.X - 2, (int)center.Y - 2, 5, 5),
                    palette.Detail);
                break;

            case PathDecorationKind.RouteChevron:
                Vector2 direction = decoration.Variant switch
                {
                    1 => Vector2.UnitY,
                    2 => -Vector2.UnitX,
                    3 => -Vector2.UnitY,
                    _ => Vector2.UnitX,
                };
                Vector2 perpendicular = new(-direction.Y, direction.X);
                for (int chevron = -1; chevron <= 1; chevron += 2)
                {
                    Vector2 tip = center + direction * (10 + chevron * 5) * scale;
                    Vector2 back = tip - direction * 10 * scale;
                    Primitives2D.Line(spriteBatch,
                        back + perpendicular * 7 * scale, tip, palette.Accent * .8f, 2);
                    Primitives2D.Line(spriteBatch,
                        back - perpendicular * 7 * scale, tip, palette.Accent * .8f, 2);
                }
                break;

            case PathDecorationKind.ThresholdRune:
                Vector2 axis = decoration.Variant % 2 == 0 ? Vector2.UnitX : Vector2.UnitY;
                Vector2 side = new(-axis.Y, axis.X);
                var thresholdDiamond = new[]
                {
                    center + axis * 15 * scale,
                    center + side * 10 * scale,
                    center - axis * 15 * scale,
                    center - side * 10 * scale,
                };
                Primitives2D.PolygonOutline(spriteBatch, thresholdDiamond, palette.Detail * .85f, 2);
                Primitives2D.Line(spriteBatch,
                    center - side * 7 * scale, center + side * 7 * scale,
                    palette.Accent, 3);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)center.X - 2, (int)center.Y - 2, 5, 5),
                    palette.Detail);
                break;

            case PathDecorationKind.CrackedEarth:
                Color crack = new(31, 19, 13);
                for (int branch = -1; branch <= 1; branch++)
                {
                    Primitives2D.Polyline(spriteBatch, new[]
                    {
                        center, center + new Vector2(branch * 9 - 5, 6 * scale),
                        center + new Vector2(branch * width / 3f, height / 2f),
                    }, false, crack, Math.Max(1, (int)scale));
                }
                break;

            case PathDecorationKind.RotPatch:
                Primitives2D.FillPolygon(spriteBatch, new[]
                {
                    new Vector2(area.Left + width * .1f, center.Y), new Vector2(area.Left + width * .3f, area.Top),
                    new Vector2(area.Right - width * .14f, area.Top + height * .2f), new Vector2(area.Right, center.Y),
                    new Vector2(area.Right - width * .3f, area.Bottom), new Vector2(area.Left + width * .2f, area.Bottom - 2),
                }, new Color(59, 65, 25));
                Primitives2D.PolygonOutline(spriteBatch, new[]
                {
                    new Vector2(area.Left + width * .1f, center.Y), new Vector2(area.Left + width * .3f, area.Top),
                    new Vector2(area.Right - width * .14f, area.Top + height * .2f), new Vector2(area.Right, center.Y),
                    new Vector2(area.Right - width * .3f, area.Bottom), new Vector2(area.Left + width * .2f, area.Bottom - 2),
                }, palette.Detail * .65f, 2);
                break;

            case PathDecorationKind.ScorchedCrater:
                Primitives2D.FillEllipse(spriteBatch, area, new Color(35, 20, 15));
                Primitives2D.EllipseOutline(spriteBatch, area, palette.Accent * .7f, Math.Max(2, (int)scale), 30);
                var inner = area;
                inner.Inflate(-Math.Max(3, width / 5), -Math.Max(2, height / 5));
                Primitives2D.EllipseOutline(spriteBatch, inner, new Color(12, 12, 11), 2, 24);
                break;

            case PathDecorationKind.CinderPlate:
                Primitives2D.FillRect(spriteBatch, area, new Color(48, 34, 27));
                Primitives2D.RectOutline(spriteBatch, area, palette.WallTop * .7f, 2);
                Primitives2D.Line(spriteBatch,
                    new Vector2(area.Left + 4, area.Top + 4),
                    new Vector2(area.Right - 4, area.Bottom - 4),
                    palette.Accent * .55f, 2);
                foreach (Point corner in new[]
                {
                    new Point(area.Left + 4, area.Top + 4),
                    new Point(area.Right - 5, area.Top + 4),
                    new Point(area.Left + 4, area.Bottom - 5),
                    new Point(area.Right - 5, area.Bottom - 5),
                })
                {
                    Primitives2D.FillCircle(spriteBatch, corner.ToVector2(), 2, palette.Detail);
                }
                break;

            case PathDecorationKind.TreasureSeal:
                float radius = Math.Max(18f, 15f * scale);
                var seal = new[]
                {
                    center + new Vector2(0, -radius),
                    center + new Vector2(radius, 0),
                    center + new Vector2(0, radius),
                    center + new Vector2(-radius, 0),
                };
                Primitives2D.FillPolygon(spriteBatch, seal, new Color(64, 45, 19) * .82f);
                Primitives2D.PolygonOutline(spriteBatch, seal, palette.Accent, Math.Max(3, (int)(1.3f * scale)));
                Primitives2D.CircleOutline(spriteBatch, center, Math.Max(8, (int)(radius * .48f)),
                    palette.Detail, Math.Max(2, (int)scale), 28);
                Primitives2D.Line(spriteBatch, center - Vector2.UnitX * radius * .72f,
                    center + Vector2.UnitX * radius * .72f, UiTheme.Ink * .8f, Math.Max(2, (int)scale));
                Primitives2D.Line(spriteBatch, center - Vector2.UnitY * radius * .72f,
                    center + Vector2.UnitY * radius * .72f, UiTheme.Ink * .8f, Math.Max(2, (int)scale));
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)center.X - Math.Max(3, (int)(3 * scale)),
                        (int)center.Y - Math.Max(4, (int)(5 * scale)),
                        Math.Max(7, (int)(7 * scale)), Math.Max(9, (int)(10 * scale))),
                    palette.Accent);
                Primitives2D.FillCircle(spriteBatch, center - new Vector2(0, 2 * scale),
                    Math.Max(2, (int)(2 * scale)), UiTheme.Ink);
                break;
        }
    }

    /// <summary>
    /// Ported from _raised_scenery: the small subset of tiles that need
    /// full-resolution per-frame drawing. Public/static (pure function of a
    /// Battleground, no GraphicsDevice involved) so the deterministic
    /// decoration-marker selection and wall enumeration are directly unit
    /// testable, matching this port's established pattern of promoting pure
    /// geometry/selection helpers to public rather than reaching for
    /// `internal`+`InternalsVisibleTo`.
    /// </summary>
    public static (List<(int X, int Y, TileType Tile, int Biome)> Walls, List<(int X, int Y, int Biome)> Decorations) ComputeRaisedScenery(Battleground battleground)
    {
        var walls = new List<(int X, int Y, TileType Tile, int Biome)>();
        var decorations = new List<(int X, int Y, int Biome)>();
        int centerX = battleground.Width / 2, centerY = battleground.Height / 2;
        for (int y = 0; y < battleground.Height; y++)
        {
            for (int x = 0; x < battleground.Width; x++)
            {
                var tile = battleground.TileAt(x, y);
                int biome = battleground.BiomeForTile(x, y);
                if (tile.IsRaised())
                {
                    walls.Add((x, y, tile, biome));
                }
                else if (tile == TileType.Default && battleground.VisualThemeKey is null)
                {
                    int marker = (x * 43 + y * 89 + x * y) % 211;
                    double distanceFromCenter = Math.Sqrt((x - centerX) * (double)(x - centerX) + (y - centerY) * (double)(y - centerY));
                    if ((marker == 7 || marker == 8) && distanceFromCenter > 11)
                        decorations.Add((x, y, biome));
                }
            }
        }
        return (walls, decorations);
    }

    /// <summary>Draws the baked ground plane rotated, then camera-facing walls/decorations sorted by screen Y, clipped to viewport.</summary>
    public void Draw(
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        Rectangle viewport,
        bool drawRaisedScenery = true)
    {
        if (_bakedGround is null || _bakedFor is null)
            return;

        var previousScissor = graphicsDevice.ScissorRectangle;
        graphicsDevice.ScissorRectangle = viewport;
        spriteBatch.Begin(rasterizerState: ScissorRasterizerState, transformMatrix: camera.WorldTransform);

        float rotation = -MathHelper.ToRadians(camera.AngleDegrees);
        spriteBatch.Draw(_bakedGround, camera.Lock + screenShake, null, Color.White, rotation, playerWorldPosition, 1f, SpriteEffects.None, 0f);

        if (!drawRaisedScenery)
        {
            spriteBatch.End();
            graphicsDevice.ScissorRectangle = previousScissor;
            return;
        }

        var visibility = camera.LogicalViewport(viewport);
        visibility.Inflate(Battleground.TileSize * 3, Battleground.TileSize * 3);
        float halfTile = Battleground.TileSize / 2f;

        _visibleItemScratch.Clear();
        foreach (var (x, y, tile, biome) in _walls)
        {
            var center = camera.WorldToScreen(new Vector2(x * Battleground.TileSize + halfTile, y * Battleground.TileSize + halfTile), playerWorldPosition, screenShake);
            if (visibility.Contains(center.ToPoint()))
                _visibleItemScratch.Add((center.Y, 0, x, y, tile, biome, null));
        }
        foreach (var (x, y, biome) in _decorations)
        {
            var center = camera.WorldToScreen(new Vector2(x * Battleground.TileSize + halfTile, y * Battleground.TileSize + halfTile), playerWorldPosition, screenShake);
            if (visibility.Contains(center.ToPoint()))
                _visibleItemScratch.Add((center.Y, 1, x, y, TileType.Default, biome, null));
        }
        foreach (var decoration in _pathRaisedDecorations)
        {
            var center = camera.WorldToScreen(decoration.WorldPosition, playerWorldPosition, screenShake);
            if (visibility.Contains(center.ToPoint()))
            {
                int tileX = Math.Clamp((int)(decoration.WorldPosition.X / Battleground.TileSize), 0, _bakedFor.Width - 1);
                int tileY = Math.Clamp((int)(decoration.WorldPosition.Y / Battleground.TileSize), 0, _bakedFor.Height - 1);
                int biome = _bakedFor.BiomeForTile(tileX, tileY);
                _visibleItemScratch.Add((center.Y, 2, tileX, tileY, TileType.Default, biome, decoration));
            }
        }
        _visibleItemScratch.Sort(static (a, b) => a.ScreenY.CompareTo(b.ScreenY));

        foreach (var item in _visibleItemScratch)
        {
            var palette = _bakedFor.Palettes[item.Biome];
            if (item.Kind == 0)
                DrawCameraFacingWall(spriteBatch, camera, playerWorldPosition, screenShake, item.X, item.Y, item.Tile, palette);
            else if (item.Kind == 1)
                DrawRaisedDecoration(spriteBatch, camera, playerWorldPosition, screenShake, item.X, item.Y, item.Biome, palette);
            else if (item.PathDecoration is not null)
                DrawPathRaisedDecoration(spriteBatch, camera, playerWorldPosition, screenShake, item.PathDecoration, palette);
        }

        spriteBatch.End();
        graphicsDevice.ScissorRectangle = previousScissor;
    }

    /// <summary>
    /// Returns painter depth for a point resting on the ground plane. Smaller
    /// values are farther toward screen-north and therefore paint first.
    /// Keeping this derived from Camera.WorldVectorToScreen makes occlusion
    /// rotate with the camera instead of being hard-coded to world Y.
    /// </summary>
    public static float GroundDepth(Camera camera, Vector2 worldAnchor) =>
        camera.WorldVectorToScreen(worldAnchor).Y;

    /// <summary>
    /// Draws raised scenery and grounded combat objects in one camera-depth
    /// order. The regular gameplay background pass stops after the baked
    /// ground plane so this is the one authoritative raised-scenery pass:
    ///
    /// - an object north/behind a wall paints first, then the wall cap/face
    ///   covers the overlapping pixels;
    /// - an object south/in front paints after that wall and stays readable.
    ///
    /// The caller owns SpriteBatch.Begin/End so this can share the same
    /// world-zoom transform as actors and projectiles.
    /// </summary>
    public void DrawDepthSortedWorld(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        Rectangle viewport,
        IReadOnlyList<WorldDepthDrawItem> dynamicItems,
        Action<SpriteBatch, WorldDepthDrawItem> drawDynamic)
    {
        _depthSceneScratch.Clear();
        _depthSceneScratch.EnsureCapacity(
            dynamicItems.Count + _walls.Count + _decorations.Count + _pathRaisedDecorations.Count);
        if (_bakedFor is null)
        {
            for (int index = 0; index < dynamicItems.Count; index++)
            {
                WorldDepthDrawItem item = dynamicItems[index];
                _depthSceneScratch.Add(new DepthSceneItem(
                    GroundDepth(camera, item.WorldAnchor),
                    item.PaintPriority,
                    item.StableOrder,
                    DynamicIndex: index,
                    Kind: -1,
                    X: 0,
                    Y: 0,
                    Tile: TileType.Default,
                    Biome: 0,
                    PathDecoration: null));
            }
            SortDepthScene();
            foreach (DepthSceneItem sceneItem in _depthSceneScratch)
                drawDynamic(spriteBatch, dynamicItems[sceneItem.DynamicIndex]);
            return;
        }

        var visibility = camera.LogicalViewport(viewport);
        visibility.Inflate(Battleground.TileSize * 3, Battleground.TileSize * 3);
        float halfTile = Battleground.TileSize / 2f;
        for (int index = 0; index < dynamicItems.Count; index++)
        {
            var item = dynamicItems[index];
            _depthSceneScratch.Add(new DepthSceneItem(
                GroundDepth(camera, item.WorldAnchor),
                item.PaintPriority,
                item.StableOrder,
                DynamicIndex: index,
                Kind: -1,
                X: 0,
                Y: 0,
                Tile: TileType.Default,
                Biome: 0,
                PathDecoration: null));
        }

        int sceneryOrder = dynamicItems.Count;
        foreach (var (x, y, tile, biome) in _walls)
        {
            var anchor = new Vector2(x * Battleground.TileSize + halfTile, y * Battleground.TileSize + halfTile);
            var center = camera.WorldToScreen(anchor, playerWorldPosition, screenShake);
            if (!visibility.Contains(center.ToPoint()))
                continue;
            _depthSceneScratch.Add(new DepthSceneItem(
                GroundDepth(camera, anchor),
                SceneryPaintPriority,
                sceneryOrder++,
                DynamicIndex: -1,
                Kind: 0,
                X: x,
                Y: y,
                Tile: tile,
                Biome: biome,
                PathDecoration: null));
        }
        foreach (var (x, y, biome) in _decorations)
        {
            var anchor = new Vector2(x * Battleground.TileSize + halfTile, y * Battleground.TileSize + halfTile);
            var center = camera.WorldToScreen(anchor, playerWorldPosition, screenShake);
            if (!visibility.Contains(center.ToPoint()))
                continue;
            _depthSceneScratch.Add(new DepthSceneItem(
                GroundDepth(camera, anchor),
                SceneryPaintPriority,
                sceneryOrder++,
                DynamicIndex: -1,
                Kind: 1,
                X: x,
                Y: y,
                Tile: TileType.Default,
                Biome: biome,
                PathDecoration: null));
        }
        foreach (var decoration in _pathRaisedDecorations)
        {
            var center = camera.WorldToScreen(decoration.WorldPosition, playerWorldPosition, screenShake);
            if (!visibility.Contains(center.ToPoint()))
                continue;
            int tileX = Math.Clamp((int)(decoration.WorldPosition.X / Battleground.TileSize), 0, _bakedFor.Width - 1);
            int tileY = Math.Clamp((int)(decoration.WorldPosition.Y / Battleground.TileSize), 0, _bakedFor.Height - 1);
            int biome = _bakedFor.BiomeForTile(tileX, tileY);
            _depthSceneScratch.Add(new DepthSceneItem(
                GroundDepth(camera, decoration.WorldPosition),
                SceneryPaintPriority,
                sceneryOrder++,
                DynamicIndex: -1,
                Kind: 2,
                X: tileX,
                Y: tileY,
                Tile: TileType.Default,
                Biome: biome,
                PathDecoration: decoration));
        }

        SortDepthScene();

        foreach (var item in _depthSceneScratch)
        {
            if (item.DynamicIndex >= 0)
            {
                drawDynamic(spriteBatch, dynamicItems[item.DynamicIndex]);
                continue;
            }

            var palette = _bakedFor.Palettes[item.Biome];
            if (item.Kind == 0)
                DrawCameraFacingWall(spriteBatch, camera, playerWorldPosition, screenShake, item.X, item.Y, item.Tile, palette);
            else if (item.Kind == 1)
                DrawRaisedDecoration(spriteBatch, camera, playerWorldPosition, screenShake, item.X, item.Y, item.Biome, palette);
            else if (item.PathDecoration is not null)
                DrawPathRaisedDecoration(spriteBatch, camera, playerWorldPosition, screenShake, item.PathDecoration, palette);
        }
    }

    private void SortDepthScene() =>
        _depthSceneScratch.Sort(static (left, right) =>
        {
            int comparison = left.Depth.CompareTo(right.Depth);
            if (comparison != 0)
                return comparison;
            comparison = left.PaintPriority.CompareTo(right.PaintPriority);
            return comparison != 0 ? comparison : left.StableOrder.CompareTo(right.StableOrder);
        });

    private const int SceneryPaintPriority = 100;

    private readonly record struct DepthSceneItem(
        float Depth,
        int PaintPriority,
        int StableOrder,
        int DynamicIndex,
        int Kind,
        int X,
        int Y,
        TileType Tile,
        int Biome,
        PathDecoration? PathDecoration);

    /// <summary>Ported from _wall_screen_geometry. Public/static for the same testability reasoning as <see cref="ComputeRaisedScenery"/>.</summary>
    public static (Vector2[] Ground, Vector2[] Cap) WallScreenGeometry(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake, int tileX, int tileY, int height)
    {
        int size = Battleground.TileSize;
        var groundWorld = new[]
        {
            new Vector2(tileX * size, tileY * size),
            new Vector2((tileX + 1) * size, tileY * size),
            new Vector2((tileX + 1) * size, (tileY + 1) * size),
            new Vector2(tileX * size, (tileY + 1) * size),
        };
        var ground = groundWorld.Select(p => camera.WorldToScreen(p, playerWorldPosition, screenShake)).ToArray();
        var cap = ground.Select(p => new Vector2(p.X, p.Y - height)).ToArray();
        return (ground, cap);
    }

    /// <summary>
    /// Ported from _draw_camera_facing_wall's per-edge visibility test: culls
    /// faces whose outward normal doesn't currently point toward
    /// screen-bottom (that face is hidden from the camera), and faces whose
    /// neighboring tile is itself raised (a hidden interior face). Returns
    /// the survivors sorted by that normal for painter's-algorithm z-order.
    /// Public/static for the same testability reasoning as
    /// <see cref="ComputeRaisedScenery"/> -- this is the one piece of wall
    /// rendering with real conditional logic worth testing directly.
    /// </summary>
    public static List<(float NormalY, Vector2[] Face)> VisibleWallFaces(Camera camera, Battleground battleground, int tileX, int tileY, Vector2[] ground, Vector2[] cap)
    {
        var edges = new (int Start, int End, Vector2 Normal, int NeighborX, int NeighborY)[]
        {
            (0, 1, new Vector2(0, -1), tileX, tileY - 1),
            (1, 2, new Vector2(1, 0), tileX + 1, tileY),
            (2, 3, new Vector2(0, 1), tileX, tileY + 1),
            (3, 0, new Vector2(-1, 0), tileX - 1, tileY),
        };
        var visibleFaces = new List<(float NormalY, Vector2[] Face)>();
        foreach (var edge in edges)
        {
            if (battleground.IsRaisedAt(edge.NeighborX, edge.NeighborY))
                continue;
            float normalY = camera.WorldVectorToScreen(edge.Normal).Y;
            if (normalY <= .001f)
                continue;
            var face = new[] { cap[edge.Start], cap[edge.End], ground[edge.End], ground[edge.Start] };
            visibleFaces.Add((normalY, face));
        }
        visibleFaces.Sort((a, b) => a.NormalY.CompareTo(b.NormalY));
        return visibleFaces;
    }

    /// <summary>Ported from _draw_camera_facing_wall's drawing half (see VisibleWallFaces for the culling/sort logic).</summary>
    private void DrawCameraFacingWall(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake, int tileX, int tileY, TileType tile, BiomePalette palette)
    {
        int height = _bakedFor!.WallHeight + (tile == TileType.ArenaWall ? 2 : 0);
        int size = Battleground.TileSize;
        Span<Vector2> ground = stackalloc Vector2[4]
        {
            camera.WorldToScreen(new Vector2(tileX * size, tileY * size), playerWorldPosition, screenShake),
            camera.WorldToScreen(new Vector2((tileX + 1) * size, tileY * size), playerWorldPosition, screenShake),
            camera.WorldToScreen(new Vector2((tileX + 1) * size, (tileY + 1) * size), playerWorldPosition, screenShake),
            camera.WorldToScreen(new Vector2(tileX * size, (tileY + 1) * size), playerWorldPosition, screenShake),
        };
        Span<Vector2> cap = stackalloc Vector2[4];
        for (int index = 0; index < 4; index++)
            cap[index] = new Vector2(ground[index].X, ground[index].Y - height);

        Span<int> visibleEdges = stackalloc int[2];
        Span<float> visibleNormals = stackalloc float[2];
        int visibleCount = 0;
        for (int edge = 0; edge < 4; edge++)
        {
            int neighborX = tileX;
            int neighborY = tileY;
            Vector2 normal;
            switch (edge)
            {
                case 0: neighborY--; normal = new Vector2(0, -1); break;
                case 1: neighborX++; normal = new Vector2(1, 0); break;
                case 2: neighborY++; normal = new Vector2(0, 1); break;
                default: neighborX--; normal = new Vector2(-1, 0); break;
            }
            if (_bakedFor.IsRaisedAt(neighborX, neighborY))
                continue;
            float normalY = camera.WorldVectorToScreen(normal).Y;
            if (normalY <= .001f)
                continue;
            int insertion = visibleCount;
            while (insertion > 0 && normalY < visibleNormals[insertion - 1])
            {
                visibleNormals[insertion] = visibleNormals[insertion - 1];
                visibleEdges[insertion] = visibleEdges[insertion - 1];
                insertion--;
            }
            visibleNormals[insertion] = normalY;
            visibleEdges[insertion] = edge;
            visibleCount++;
        }

        for (int index = 0; index < visibleCount; index++)
        {
            int start = visibleEdges[index];
            int end = (start + 1) % 4;
            DrawWallFace(spriteBatch, cap[start], cap[end], ground[end], ground[start],
                tileX, tileY, palette);
        }

        Primitives2D.FillQuad(spriteBatch, cap[0], cap[1], cap[2], cap[3], palette.WallTop);
        for (int edge = 0; edge < 4; edge++)
            Primitives2D.Line(spriteBatch, cap[edge], cap[(edge + 1) % 4], UiTheme.Ink, 2);

        int topEdgeIndex = 0;
        float topEdgeY = (cap[0].Y + cap[1].Y) * .5f;
        for (int edge = 1; edge < 4; edge++)
        {
            float edgeY = (cap[edge].Y + cap[(edge + 1) % 4].Y) * .5f;
            if (edgeY < topEdgeY)
            {
                topEdgeY = edgeY;
                topEdgeIndex = edge;
            }
        }
        Primitives2D.Line(spriteBatch, cap[topEdgeIndex], cap[(topEdgeIndex + 1) % 4],
            palette.Detail, 2);

        float centerX = (cap[0].X + cap[1].X + cap[2].X + cap[3].X) * .25f;
        float centerY = (cap[0].Y + cap[1].Y + cap[2].Y + cap[3].Y) * .25f;
        if (tile == TileType.ArenaWall)
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)centerX - 3, (int)centerY - 3, 6, 6), palette.Accent);
        else if ((tileX + tileY) % 2 == 0)
            Primitives2D.Line(spriteBatch, new Vector2(centerX - 9, centerY), new Vector2(centerX + 9, centerY), palette.Accent, 2);

        if (_bakedFor.VisualThemeKey is not null && (tileX * 31 + tileY * 17) % 3 == 0)
        {
            Vector2 capCenter = new(centerX, centerY);
            if (_bakedFor.VisualThemeKey == "phantasia")
                Primitives2D.FillCircle(spriteBatch, capCenter, 3, palette.Detail);
            else
                Primitives2D.Line(spriteBatch, capCenter - new Vector2(7, 3),
                    capCenter + new Vector2(7, 3), palette.Detail * .72f, 1);
        }
    }

    private void DrawWallFace(
        SpriteBatch spriteBatch,
        Vector2 topLeft,
        Vector2 topRight,
        Vector2 bottomRight,
        Vector2 bottomLeft,
        int tileX,
        int tileY,
        BiomePalette palette)
    {
        Primitives2D.FillQuad(
            spriteBatch, topLeft, topRight, bottomRight, bottomLeft, palette.WallFace);
        Color seam = palette.WallFace * .68f;
        Primitives2D.Line(spriteBatch, topLeft, topRight, seam, 1);
        Primitives2D.Line(spriteBatch, bottomLeft, bottomRight, seam, 1);
        Vector2 accentLeft = Vector2.Lerp(bottomLeft, bottomRight, .18f) - new Vector2(0, 5);
        Vector2 accentRight = Vector2.Lerp(bottomLeft, bottomRight, .82f) - new Vector2(0, 5);
        Primitives2D.Line(spriteBatch, accentLeft, accentRight, palette.Accent, 2);
        DrawPathWallMaterial(
            spriteBatch, topLeft, topRight, bottomRight, bottomLeft,
            tileX, tileY, _bakedFor!.VisualThemeKey, palette);
    }

    private static void DrawPathWallMaterial(
        SpriteBatch spriteBatch,
        Vector2 topLeft,
        Vector2 topRight,
        Vector2 bottomRight,
        Vector2 bottomLeft,
        int tileX,
        int tileY,
        string? themeKey,
        BiomePalette palette)
    {
        if (themeKey is null)
            return;

        Vector2 Top(float amount) => Vector2.Lerp(topLeft, topRight, amount);
        Vector2 Bottom(float amount) => Vector2.Lerp(bottomLeft, bottomRight, amount);
        Vector2 Across(float amount, float depth) =>
            Vector2.Lerp(Vector2.Lerp(topLeft, bottomLeft, depth),
                Vector2.Lerp(topRight, bottomRight, depth), amount);
        int hash = Math.Abs(tileX * 47 + tileY * 83);
        Color line = palette.Detail * .38f;

        switch (themeKey)
        {
            case "touch":
                Primitives2D.Line(spriteBatch, Across(0, .48f), Across(1, .48f), line, 1);
                Primitives2D.Line(spriteBatch, Top(hash % 2 == 0 ? .34f : .66f),
                    Across(hash % 2 == 0 ? .34f : .66f, .48f), line, 1);
                break;
            case "sight":
                Primitives2D.Line(spriteBatch, Top(.5f), Across(.2f, .55f), line, 1);
                Primitives2D.Line(spriteBatch, Top(.5f), Across(.8f, .55f), line, 1);
                Primitives2D.Line(spriteBatch, Across(.2f, .55f), Bottom(.5f), line, 1);
                Primitives2D.Line(spriteBatch, Across(.8f, .55f), Bottom(.5f), line, 1);
                break;
            case "sound":
                for (int band = 1; band <= 3; band++)
                {
                    float depth = band * .2f;
                    float inset = band == 2 ? .16f : .08f;
                    Primitives2D.Line(spriteBatch, Across(inset, depth),
                        Across(1f - inset, depth), line, 1);
                }
                break;
            case "phantasia":
                Vector2 star = Across(.3f + (hash % 4) * .13f, .42f);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)star.X - 1, (int)star.Y - 1, 3, 3),
                    palette.Detail * .65f);
                break;
            case "chemesthesis":
                Vector2 crackStart = Top(.32f);
                Vector2 crackMidA = Across(.55f, .36f);
                Vector2 crackMidB = Across(.42f, .65f);
                Vector2 crackEnd = Bottom(.7f);
                Primitives2D.Line(spriteBatch, crackStart, crackMidA, line, 1);
                Primitives2D.Line(spriteBatch, crackMidA, crackMidB, line, 1);
                Primitives2D.Line(spriteBatch, crackMidB, crackEnd, line, 1);
                break;
        }
    }

    /// <summary>Draws a complete dark wall volume without temporary geometry arrays.</summary>
    public static void DrawWallOcclusionMask(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        int tileX,
        int tileY,
        int height,
        Color color)
    {
        int size = Battleground.TileSize;
        Span<Vector2> ground = stackalloc Vector2[4]
        {
            camera.WorldToScreen(new Vector2(tileX * size, tileY * size), playerWorldPosition, screenShake),
            camera.WorldToScreen(new Vector2((tileX + 1) * size, tileY * size), playerWorldPosition, screenShake),
            camera.WorldToScreen(new Vector2((tileX + 1) * size, (tileY + 1) * size), playerWorldPosition, screenShake),
            camera.WorldToScreen(new Vector2(tileX * size, (tileY + 1) * size), playerWorldPosition, screenShake),
        };
        Span<Vector2> cap = stackalloc Vector2[4];
        for (int index = 0; index < 4; index++)
            cap[index] = new Vector2(ground[index].X, ground[index].Y - height);
        Primitives2D.FillQuad(spriteBatch, cap[0], cap[1], cap[2], cap[3], color);
        for (int edge = 0; edge < 4; edge++)
        {
            int next = (edge + 1) % 4;
            Primitives2D.FillQuad(
                spriteBatch, cap[edge], cap[next], ground[next], ground[edge], color);
        }
    }

    /// <summary>Ported from _draw_raised_decoration: a small biome-specific "2.5D landmark" prop with a top, face, and grounded shadow.</summary>
    private static void DrawRaisedDecoration(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake, int tileX, int tileY, int biome, BiomePalette palette)
    {
        int size = Battleground.TileSize;
        var center = camera.WorldToScreen(new Vector2((tileX + .5f) * size, (tileY + .5f) * size), playerWorldPosition, screenShake);
        float cx = center.X;
        float floorY = center.Y + size / 2f - 8;
        Primitives2D.FillEllipse(spriteBatch, new Rectangle((int)(cx - 13), (int)(floorY - 3), 30, 13), DecorationShadow);

        if (biome == 1)
        {
            // Ember ward brazier.
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)(cx - 8), (int)(floorY - 16), 16, 18), UiTheme.Ink);
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)(cx - 6), (int)(floorY - 15), 12, 15), palette.WallFace);
            Primitives2D.FillQuad(
                spriteBatch,
                new Vector2(cx - 7, floorY - 16),
                new Vector2(cx, floorY - 21),
                new Vector2(cx + 7, floorY - 16),
                new Vector2(cx, floorY - 12),
                palette.WallTop);
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)(cx - 3), (int)(floorY - 26), 6, 9), palette.Accent);
        }
        else
        {
            // Archive plinth / drowned circuit relay.
            float height = biome == 0 ? 24 : 20;
            Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
            {
                new Vector2(cx - 9, floorY - height), new Vector2(cx + 4, floorY - height - 5),
                new Vector2(cx + 10, floorY - height + 1), new Vector2(cx + 10, floorY),
                new Vector2(cx - 9, floorY),
            }, UiTheme.Ink);
            Primitives2D.FillQuad(
                spriteBatch,
                new Vector2(cx - 6, floorY - height + 1),
                new Vector2(cx + 6, floorY - height + 1),
                new Vector2(cx + 6, floorY - 3),
                new Vector2(cx - 6, floorY - 1),
                palette.WallFace);
            Primitives2D.FillQuad(
                spriteBatch,
                new Vector2(cx - 7, floorY - height),
                new Vector2(cx, floorY - height - 5),
                new Vector2(cx + 7, floorY - height),
                new Vector2(cx, floorY - height + 4),
                palette.WallTop);
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)(cx - 2), (int)(floorY - height + 7), 4, 7), palette.Accent);
        }
    }

    private static void DrawPathRaisedDecoration(
        SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake,
        PathDecoration decoration, BiomePalette palette)
    {
        Vector2 center = camera.WorldToScreen(decoration.WorldPosition, playerWorldPosition, screenShake);
        float scale = decoration.Scale;
        float cx = center.X;
        float floorY = center.Y + Battleground.TileSize * .32f;
        int S(float value) => Math.Max(1, (int)MathF.Round(value * scale));
        Rectangle Rect(float x, float y, float width, float height) =>
            new((int)(cx + x * scale), (int)(floorY + y * scale), S(width), S(height));
        Vector2 P(float x, float y) => new(cx + x * scale, floorY + y * scale);

        Primitives2D.FillEllipse(spriteBatch, Rect(-15, -3, 32, 11), DecorationShadow * .9f);
        switch (decoration.Kind)
        {
            case PathDecorationKind.PipeStack:
                for (int pipe = 0; pipe < 3; pipe++)
                {
                    int offset = (pipe - 1) * S(7);
                    int height = S(18 + pipe * 5);
                    Primitives2D.FillRect(spriteBatch, new Rectangle((int)cx + offset - S(3), (int)floorY - height, S(7), height), UiTheme.Ink);
                    Primitives2D.FillRect(spriteBatch, new Rectangle((int)cx + offset - S(2), (int)floorY - height + S(2), S(4), height - S(3)), palette.WallFace);
                    Primitives2D.FillEllipse(spriteBatch, new Rectangle((int)cx + offset - S(4), (int)floorY - height - S(2), S(9), S(5)), palette.Detail);
                }
                break;

            case PathDecorationKind.Valve:
                Primitives2D.FillRect(spriteBatch, Rect(-3, -22, 6, 23), palette.WallFace);
                Primitives2D.CircleOutline(spriteBatch, P(0, -23), S(10), palette.Detail, S(3), 20);
                for (int spoke = 0; spoke < 4; spoke++)
                {
                    float angle = spoke * MathF.PI / 2f;
                    Primitives2D.Line(spriteBatch, P(0, -23), P(MathF.Cos(angle) * 9, -23 + MathF.Sin(angle) * 9), palette.Accent, S(2));
                }
                Primitives2D.FillCircle(spriteBatch, P(0, -23), S(3), UiTheme.Ink);
                break;

            case PathDecorationKind.Pump:
                Primitives2D.FillRect(spriteBatch, Rect(-11, -20, 22, 21), UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch, Rect(-8, -18, 16, 17), palette.WallFace);
                Primitives2D.FillQuad(
                    spriteBatch, P(-10, -20), P(0, -27),
                    P(10, -20), P(0, -14), palette.WallTop);
                Primitives2D.FillCircle(spriteBatch, P(0, -8), S(4), palette.Accent);
                Primitives2D.Line(spriteBatch, P(8, -18), P(17, -18), palette.Detail, S(4));
                break;

            case PathDecorationKind.PressureTank:
                Primitives2D.FillRect(spriteBatch, Rect(-11, -34, 22, 35), UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch, Rect(-8, -32, 16, 31), palette.WallFace);
                Primitives2D.FillEllipse(spriteBatch, Rect(-8, -38, 16, 11), palette.WallTop);
                Primitives2D.EllipseOutline(spriteBatch, Rect(-9, -39, 18, 12),
                    palette.Detail, S(2), 20);
                Primitives2D.Line(spriteBatch, P(-8, -18), P(8, -18), palette.Accent, S(3));
                Primitives2D.FillCircle(spriteBatch, P(0, -25), S(4), UiTheme.Ink);
                Primitives2D.FillCircle(spriteBatch, P(0, -25), S(2), palette.Accent);
                Primitives2D.Line(spriteBatch, P(8, -8), P(17, -8), palette.Detail, S(4));
                break;

            case PathDecorationKind.LensBuoy:
                Primitives2D.FillRect(spriteBatch, Rect(-3, -22, 6, 23), palette.WallFace);
                Primitives2D.FillQuad(
                    spriteBatch, P(0, -35), P(13, -24),
                    P(0, -14), P(-13, -24), UiTheme.Ink);
                Primitives2D.FillEllipse(spriteBatch, Rect(-10, -30, 20, 13), palette.WallTop);
                Primitives2D.FillCircle(spriteBatch, P(0, -24), S(5), palette.Accent);
                Primitives2D.FillRect(spriteBatch, Rect(-2, -27, 3, 3), palette.Detail);
                break;

            case PathDecorationKind.SteppingStone:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-13, -6), P(-5, -12), P(11, -10),
                    P(15, -4), P(8, 0), P(-10, 0),
                }, palette.WallFace);
                Span<Vector2> steppingStoneTop = stackalloc Vector2[]
                {
                    P(-13, -7), P(-5, -13), P(11, -11),
                    P(15, -5), P(2, -2),
                };
                Primitives2D.FillPolygonSpan(
                    spriteBatch, steppingStoneTop, palette.WallTop);
                Primitives2D.PolygonOutlineSpan(
                    spriteBatch, steppingStoneTop, palette.Detail, S(2));
                break;

            case PathDecorationKind.BrokenColumn:
                Primitives2D.FillRect(spriteBatch, Rect(-9, -25, 18, 26), UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch, Rect(-6, -23, 12, 22), palette.WallFace);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-8, -25), P(-3, -30), P(2, -25),
                    P(7, -29), P(9, -24),
                }, palette.WallTop);
                Primitives2D.Line(spriteBatch, P(-4, -19), P(4, -12), palette.Accent, S(2));
                break;

            case PathDecorationKind.MirrorArch:
                Primitives2D.FillRect(spriteBatch, Rect(-17, -32, 7, 33), UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch, Rect(10, -32, 7, 33), UiTheme.Ink);
                Primitives2D.Arc(spriteBatch, Rect(-17, -48, 34, 32),
                    MathF.PI, MathF.Tau, UiTheme.Ink, S(7), 28);
                Primitives2D.Arc(spriteBatch, Rect(-12, -43, 24, 25),
                    MathF.PI, MathF.Tau, palette.Detail, S(3), 28);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -39), P(10, -28), P(7, -4), P(-7, -4), P(-10, -28),
                }, palette.WallFace);
                Primitives2D.Line(spriteBatch, P(-4, -31), P(5, -10),
                    palette.Accent * .85f, S(2));
                break;

            case PathDecorationKind.EchoPylon:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -39), P(10, -27), P(7, -1),
                    P(-7, -1), P(-10, -27),
                }, UiTheme.Ink);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -35), P(7, -25), P(4, -3),
                    P(-4, -3), P(-7, -25),
                }, palette.WallFace);
                for (int ring = 0; ring < 3; ring++)
                    Primitives2D.Arc(spriteBatch, Rect(-15 - ring * 3, -34 - ring * 2, 30 + ring * 6, 20 + ring * 4), MathF.PI, MathF.Tau, palette.Accent * (.9f - ring * .18f), S(2), 18);
                break;

            case PathDecorationKind.Chime:
                Primitives2D.Line(spriteBatch, P(0, -36), P(0, -25), palette.Detail, S(2));
                Primitives2D.Line(spriteBatch, P(-13, -25), P(13, -25), palette.WallTop, S(3));
                for (int chime = -1; chime <= 1; chime++)
                {
                    int length = 13 + (1 - Math.Abs(chime)) * 7;
                    Primitives2D.Line(spriteBatch, P(chime * 9, -25), P(chime * 9, -25 + length), palette.Detail, S(3));
                    Primitives2D.FillRect(spriteBatch, Rect(chime * 9 - 2, -25 + length, 5, 4), palette.Accent);
                }
                break;

            case PathDecorationKind.LightningRod:
                Primitives2D.FillRect(spriteBatch, Rect(-3, -34, 6, 35), palette.WallFace);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -46), P(7, -34), P(1, -35),
                    P(6, -24), P(-7, -37), P(-1, -36),
                }, palette.Detail);
                Primitives2D.Line(spriteBatch, P(-11, -3), P(0, -10), palette.WallTop, S(3));
                Primitives2D.Line(spriteBatch, P(11, -3), P(0, -10), palette.WallTop, S(3));
                break;

            case PathDecorationKind.OrganStack:
                for (int pipe = -2; pipe <= 2; pipe++)
                {
                    int pipeHeight = 22 + (2 - Math.Abs(pipe)) * 8;
                    Primitives2D.FillRect(spriteBatch,
                        Rect(pipe * 7 - 3, -pipeHeight, 7, pipeHeight + 1),
                        UiTheme.Ink);
                    Primitives2D.FillRect(spriteBatch,
                        Rect(pipe * 7 - 1, -pipeHeight + 2, 3, pipeHeight - 3),
                        pipe == 0 ? palette.Accent : palette.WallFace);
                    Primitives2D.FillEllipse(spriteBatch,
                        Rect(pipe * 7 - 3, -pipeHeight - 3, 7, 6),
                        palette.Detail);
                }
                Primitives2D.FillRect(spriteBatch, Rect(-19, -7, 39, 8), palette.WallTop);
                break;

            case PathDecorationKind.Asteroid:
                Span<Vector2> asteroid = stackalloc Vector2[]
                {
                    P(-14, -15), P(-5, -27), P(10, -24),
                    P(16, -13), P(8, -2), P(-9, -3),
                };
                Primitives2D.FillPolygonSpan(
                    spriteBatch, asteroid, palette.WallFace);
                Primitives2D.PolygonOutlineSpan(
                    spriteBatch, asteroid, UiTheme.Ink, S(2));
                Primitives2D.FillCircle(spriteBatch, P(4, -16), S(4), palette.Accent * .72f);
                Primitives2D.FillRect(spriteBatch, Rect(-8, -11, 4, 4), palette.Detail);
                break;

            case PathDecorationKind.PrismObelisk:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -43), P(12, -25), P(8, -1),
                    P(-8, -1), P(-12, -25),
                }, UiTheme.Ink);
                Primitives2D.FillQuad(
                    spriteBatch, P(0, -39), P(8, -24),
                    P(5, -4), P(0, -8), palette.Accent);
                Primitives2D.FillQuad(
                    spriteBatch, P(0, -39), P(0, -8),
                    P(-5, -4), P(-8, -24), palette.WallTop);
                Primitives2D.Line(spriteBatch, P(0, -35), P(0, -10), palette.Detail, S(2));
                break;

            case PathDecorationKind.OrbitShrine:
                Primitives2D.FillRect(spriteBatch, Rect(-7, -18, 14, 19), palette.WallFace);
                Primitives2D.FillCircle(spriteBatch, P(0, -22), S(8), palette.Accent);
                Primitives2D.EllipseOutline(spriteBatch, Rect(-18, -30, 36, 15), palette.Detail, S(2), 24);
                Primitives2D.FillCircle(spriteBatch, P(15, -22), S(3), palette.WallTop);
                break;

            case PathDecorationKind.LanternSpire:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -51), P(8, -39), P(6, -2), P(-6, -2), P(-8, -39),
                }, UiTheme.Ink);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(0, -46), P(5, -37), P(4, -5), P(-4, -5), P(-5, -37),
                }, palette.WallFace);
                Primitives2D.FillQuad(
                    spriteBatch, P(0, -38), P(7, -29),
                    P(0, -20), P(-7, -29), palette.Accent);
                Primitives2D.CircleOutline(spriteBatch, P(0, -29), S(12),
                    palette.Detail * .8f, S(2), 24);
                Primitives2D.FillCircle(spriteBatch, P(0, -29), S(4), palette.Detail);
                break;

            case PathDecorationKind.RustBarricade:
                Primitives2D.FillRect(spriteBatch, Rect(-16, -17, 32, 7), UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch, Rect(-14, -15, 28, 4), palette.Accent);
                Primitives2D.Line(spriteBatch, P(-11, 0), P(-5, -24), palette.Detail, S(4));
                Primitives2D.Line(spriteBatch, P(11, 0), P(5, -24), palette.Detail, S(4));
                Span<Vector2> barricadeSpike = stackalloc Vector2[3];
                for (int spike = -1; spike <= 1; spike++)
                {
                    barricadeSpike[0] = P(spike * 10 - 3, -16);
                    barricadeSpike[1] = P(spike * 10, -28);
                    barricadeSpike[2] = P(spike * 10 + 3, -16);
                    Primitives2D.FillPolygonSpan(
                        spriteBatch, barricadeSpike, palette.WallTop);
                }
                break;

            case PathDecorationKind.DeadTree:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-6, 0), P(-4, -27), P(2, -39),
                    P(6, -28), P(5, 0),
                }, palette.WallFace);
                Primitives2D.Line(spriteBatch, P(0, -27), P(-16, -38), palette.WallFace, S(5));
                Primitives2D.Line(spriteBatch, P(-13, -36), P(-18, -46), palette.Detail, S(3));
                Primitives2D.Line(spriteBatch, P(3, -31), P(17, -42), palette.WallFace, S(5));
                Primitives2D.Line(spriteBatch, P(14, -39), P(19, -50), palette.Detail, S(3));
                break;

            case PathDecorationKind.RuinSlab:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-13, 0), P(-10, -32), P(-3, -39),
                    P(11, -33), P(13, 0),
                }, UiTheme.Ink);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-9, -2), P(-7, -30), P(-2, -35),
                    P(8, -30), P(9, -2),
                }, palette.WallFace);
                Primitives2D.PolylineSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-2, -29), P(4, -21), P(-1, -13), P(5, -6),
                }, false, palette.Accent, S(2));
                break;

            case PathDecorationKind.FurnaceIdol:
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-13, 0), P(-15, -31), P(-8, -42), P(8, -42),
                    P(15, -31), P(13, 0),
                }, UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch, Rect(-10, -35, 20, 33), palette.WallFace);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    P(-7, -27), P(0, -34), P(7, -27), P(5, -19), P(-5, -19),
                }, palette.WallTop);
                Primitives2D.FillCircle(spriteBatch, P(-4, -25), S(2), palette.Accent);
                Primitives2D.FillCircle(spriteBatch, P(4, -25), S(2), palette.Accent);
                Primitives2D.FillRect(spriteBatch, Rect(-6, -14, 12, 9), palette.Accent);
                for (int grate = -1; grate <= 1; grate++)
                    Primitives2D.Line(spriteBatch, P(grate * 4, -13), P(grate * 4, -6),
                        UiTheme.Ink, S(1));
                break;
        }
    }
}
