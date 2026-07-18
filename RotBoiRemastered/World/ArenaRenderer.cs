using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.World;

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
    private static readonly Color ShadowColor = new(18, 20, 27);
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

    /// <summary>No-op once already baked for this exact Battleground reference. Call once at the top of the frame, before the frame's own SpriteBatch.Begin().</summary>
    public void EnsureBaked(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Battleground battleground)
    {
        if (ReferenceEquals(_bakedFor, battleground))
            return;

        (_walls, _decorations) = ComputeRaisedScenery(battleground);

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
                Primitives2D.FillRect(spriteBatch, rect, color);
                if (!tile.IsSolid())
                {
                    Primitives2D.RectOutline(spriteBatch, rect, GridLineColor, 1);
                    DrawFloorDetail(spriteBatch, rect, tile, x, y, palette);
                }
            }
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
    private static void DrawFloorDetail(SpriteBatch spriteBatch, Rectangle rect, TileType tile, int tileX, int tileY, BiomePalette palette)
    {
        int noise = (tileX * 37 + tileY * 71 + tileX * tileY * 3) % 113;
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
                else if (tile == TileType.Default)
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
    public void Draw(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake, Rectangle viewport)
    {
        if (_bakedGround is null || _bakedFor is null)
            return;

        var previousScissor = graphicsDevice.ScissorRectangle;
        graphicsDevice.ScissorRectangle = viewport;
        spriteBatch.Begin(rasterizerState: ScissorRasterizerState);

        float rotation = -MathHelper.ToRadians(camera.AngleDegrees);
        spriteBatch.Draw(_bakedGround, camera.Lock + screenShake, null, Color.White, rotation, playerWorldPosition, 1f, SpriteEffects.None, 0f);

        var visibility = viewport;
        visibility.Inflate(Battleground.TileSize * 3, Battleground.TileSize * 3);
        float halfTile = Battleground.TileSize / 2f;

        var visibleItems = new List<(float ScreenY, int Kind, int X, int Y, TileType Tile, int Biome)>();
        foreach (var (x, y, tile, biome) in _walls)
        {
            var center = camera.WorldToScreen(new Vector2(x * Battleground.TileSize + halfTile, y * Battleground.TileSize + halfTile), playerWorldPosition, screenShake);
            if (visibility.Contains(center.ToPoint()))
                visibleItems.Add((center.Y, 0, x, y, tile, biome));
        }
        foreach (var (x, y, biome) in _decorations)
        {
            var center = camera.WorldToScreen(new Vector2(x * Battleground.TileSize + halfTile, y * Battleground.TileSize + halfTile), playerWorldPosition, screenShake);
            if (visibility.Contains(center.ToPoint()))
                visibleItems.Add((center.Y, 1, x, y, TileType.Default, biome));
        }
        visibleItems.Sort((a, b) => a.ScreenY.CompareTo(b.ScreenY));

        foreach (var item in visibleItems)
        {
            var palette = _bakedFor.Palettes[item.Biome];
            if (item.Kind == 0)
                DrawCameraFacingWall(spriteBatch, camera, playerWorldPosition, screenShake, item.X, item.Y, item.Tile, palette);
            else
                DrawRaisedDecoration(spriteBatch, camera, playerWorldPosition, screenShake, item.X, item.Y, item.Biome, palette);
        }

        spriteBatch.End();
        graphicsDevice.ScissorRectangle = previousScissor;
    }

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
        var (ground, cap) = WallScreenGeometry(camera, playerWorldPosition, screenShake, tileX, tileY, height);

        var shadow = ground.Select(p => p + new Vector2(6, 7)).ToArray();
        Primitives2D.FillPolygon(spriteBatch, shadow, ShadowColor);

        foreach (var (_, face) in VisibleWallFaces(camera, _bakedFor, tileX, tileY, ground, cap))
        {
            Primitives2D.FillPolygon(spriteBatch, face, palette.WallFace);
            // A closed two-pixel outline put black strokes on both vertical
            // sides of every projected face. At oblique camera angles those
            // independently rounded strokes occasionally doubled into the
            // conspicuous black bars seen between otherwise continuous walls.
            // Keep the depth-defining horizontal seams, but let neighboring
            // face fills meet without a black vertical strip between them.
            var seam = palette.WallFace * .68f;
            Primitives2D.Line(spriteBatch, face[0], face[1], seam, 1);
            Primitives2D.Line(spriteBatch, face[3], face[2], seam, 1);
            var lowerLeft = face[3];
            var lowerRight = face[2];
            var accentLeft = new Vector2(lowerLeft.X * .82f + lowerRight.X * .18f, lowerLeft.Y * .82f + lowerRight.Y * .18f - 5);
            var accentRight = new Vector2(lowerLeft.X * .18f + lowerRight.X * .82f, lowerLeft.Y * .18f + lowerRight.Y * .82f - 5);
            Primitives2D.Line(spriteBatch, accentLeft, accentRight, palette.Accent, 2);
        }

        Primitives2D.FillPolygon(spriteBatch, cap, palette.WallTop);
        Primitives2D.PolygonOutline(spriteBatch, cap, UiTheme.Ink, 2);
        var topEdge = Enumerable.Range(0, 4)
            .Select(i => (Start: cap[i], End: cap[(i + 1) % 4]))
            .OrderBy(e => (e.Start.Y + e.End.Y) / 2f)
            .First();
        Primitives2D.Line(spriteBatch, topEdge.Start, topEdge.End, palette.Detail, 2);

        float centerX = cap.Average(p => p.X), centerY = cap.Average(p => p.Y);
        if (tile == TileType.ArenaWall)
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)centerX - 3, (int)centerY - 3, 6, 6), palette.Accent);
        else if ((tileX + tileY) % 2 == 0)
            Primitives2D.Line(spriteBatch, new Vector2(centerX - 9, centerY), new Vector2(centerX + 9, centerY), palette.Accent, 2);
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
            Primitives2D.FillPolygon(spriteBatch, new[]
            {
                new Vector2(cx - 7, floorY - 16), new Vector2(cx, floorY - 21),
                new Vector2(cx + 7, floorY - 16), new Vector2(cx, floorY - 12),
            }, palette.WallTop);
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)(cx - 3), (int)(floorY - 26), 6, 9), palette.Accent);
        }
        else
        {
            // Archive plinth / drowned circuit relay.
            float height = biome == 0 ? 24 : 20;
            Primitives2D.FillPolygon(spriteBatch, new[]
            {
                new Vector2(cx - 9, floorY - height), new Vector2(cx + 4, floorY - height - 5),
                new Vector2(cx + 10, floorY - height + 1), new Vector2(cx + 10, floorY),
                new Vector2(cx - 9, floorY),
            }, UiTheme.Ink);
            Primitives2D.FillPolygon(spriteBatch, new[]
            {
                new Vector2(cx - 6, floorY - height + 1), new Vector2(cx + 6, floorY - height + 1),
                new Vector2(cx + 6, floorY - 3), new Vector2(cx - 6, floorY - 1),
            }, palette.WallFace);
            Primitives2D.FillPolygon(spriteBatch, new[]
            {
                new Vector2(cx - 7, floorY - height), new Vector2(cx, floorY - height - 5),
                new Vector2(cx + 7, floorY - height), new Vector2(cx, floorY - height + 4),
            }, palette.WallTop);
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)(cx - 2), (int)(floorY - height + 7), 4, 7), palette.Accent);
        }
    }
}
