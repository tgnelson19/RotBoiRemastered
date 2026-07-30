using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

/// <summary>
/// Ported from background.py's _raised_scenery/_wall_screen_geometry/
/// _draw_camera_facing_wall (no dedicated Python test file to mirror).
/// Covers only the pure geometry/selection pieces -- the actual drawing
/// (EnsureBaked/Draw) needs a GraphicsDevice and is covered by visual smoke
/// testing instead, same as the rest of this port's rendering layer.
/// </summary>
public class ArenaRendererTests
{
    [Fact]
    public void ComputeRaisedScenery_SelectsWallTilesAndDeterministicFarDecorations()
    {
        const int size = 41;
        var tiles = new TileType[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tiles[y, x] = TileType.Default;
        tiles[5, 5] = TileType.ArenaWall; // TileAt(x=5, y=5)
        tiles[10, 12] = TileType.BuildingWall; // TileAt(x=12, y=10)
        var battleground = new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);

        var (walls, decorations) = ArenaRenderer.ComputeRaisedScenery(battleground);

        Assert.Equal(2, walls.Count);
        Assert.Contains(walls, w => w.X == 5 && w.Y == 5 && w.Tile == TileType.ArenaWall);
        Assert.Contains(walls, w => w.X == 12 && w.Y == 10 && w.Tile == TileType.BuildingWall);

        int centerX = size / 2, centerY = size / 2;
        Assert.NotEmpty(decorations);
        foreach (var (x, y, _) in decorations)
        {
            int marker = (x * 43 + y * 89 + x * y) % 211;
            Assert.True(marker == 7 || marker == 8);
            double distance = Math.Sqrt((x - centerX) * (double)(x - centerX) + (y - centerY) * (double)(y - centerY));
            Assert.True(distance > 11);
        }
    }

    [Fact]
    public void WallScreenGeometry_CapIsGroundShiftedUpByHeight()
    {
        var camera = new Camera { Lock = new Vector2(400, 300) };
        var (ground, cap) = ArenaRenderer.WallScreenGeometry(camera, Vector2.Zero, Vector2.Zero, tileX: 2, tileY: 3, height: 20);

        Assert.Equal(4, ground.Length);
        Assert.Equal(4, cap.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(ground[i].X, cap[i].X, 3);
            Assert.Equal(ground[i].Y - 20, cap[i].Y, 3);
        }
    }

    [Fact]
    public void ComputeRaisedScenery_PathThemeSuppressesUnrelatedGenericLandmarks()
    {
        var layout = PathFloorGenerator.Generate("sight", 1, new Random(17));

        var (walls, decorations) = ArenaRenderer.ComputeRaisedScenery(layout.Battleground);

        Assert.NotEmpty(walls);
        Assert.Empty(decorations);
        Assert.Contains(layout.Decorations, decoration => decoration.Layer == PathDecorationLayer.Raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    public void VisibleTileBounds_CoversCameraViewWithoutScanningWholeDungeon(
        float cameraAngle)
    {
        var layout = PathFloorGenerator.Generate("sight", 6, new Random(17));
        Battleground battleground = layout.Battleground;
        var camera = new Camera { Lock = new Vector2(640, 360) };
        camera.SetAngle(cameraAngle);
        Vector2 player = battleground.SpawnPosition;
        var viewport = new Rectangle(0, 0, 1280, 720);

        Rectangle bounds = ArenaRenderer.VisibleTileBounds(
            camera, player, Vector2.Zero, viewport, battleground);
        Point playerTile = new(
            (int)(player.X / Battleground.TileSize),
            (int)(player.Y / Battleground.TileSize));

        Assert.True(bounds.Contains(playerTile));
        Assert.True(bounds.Width * bounds.Height
            < battleground.Width * battleground.Height);
    }

    private static Battleground LoneWallBattleground(out TileType[,] tiles)
    {
        tiles = new TileType[5, 5];
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                tiles[y, x] = TileType.Default;
        tiles[2, 2] = TileType.ArenaWall;
        return new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);
    }

    [Fact]
    public void VisibleWallFaces_NoRaisedNeighbors_OnlySouthFaceVisibleAtZeroRotation()
    {
        var battleground = LoneWallBattleground(out _);
        var camera = new Camera { Lock = new Vector2(400, 300) };
        var (ground, cap) = ArenaRenderer.WallScreenGeometry(camera, Vector2.Zero, Vector2.Zero, 2, 2, 14);

        // Camera.WorldVectorToScreen is the identity at AngleDegrees == 0, so only the
        // edge whose outward normal is (0, 1) (south) has normalY > .001; east/west
        // edges land exactly on normalY == 0, which the ".001" threshold excludes too.
        var faces = ArenaRenderer.VisibleWallFaces(camera, battleground, 2, 2, ground, cap);

        Assert.Single(faces);
    }

    [Fact]
    public void VisibleWallFaces_RaisedNeighborOnVisibleSide_HidesThatFace()
    {
        var tiles = new TileType[5, 5];
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                tiles[y, x] = TileType.Default;
        tiles[2, 2] = TileType.ArenaWall;
        tiles[3, 2] = TileType.ArenaWall; // TileAt(x=2, y=3): the south neighbor of (2,2)
        var battleground = new Battleground(tiles, BiomePalettes.Sound, wallHeight: 14);
        var camera = new Camera { Lock = new Vector2(400, 300) };
        var (ground, cap) = ArenaRenderer.WallScreenGeometry(camera, Vector2.Zero, Vector2.Zero, 2, 2, 14);

        var faces = ArenaRenderer.VisibleWallFaces(camera, battleground, 2, 2, ground, cap);

        Assert.Empty(faces);
    }

    [Fact]
    public void VisibleWallFaces_CameraRotatedNinetyDegrees_StillRevealsExactlyOneFace()
    {
        // At a quarter turn, WorldVectorToScreen((dx,dy)) becomes (dy,-dx): the west
        // edge's normal (-1,0) maps to (0,1) (normalY > 0, visible) while south's own
        // normal (0,1) maps to (1,0) (normalY == 0, culled) -- rotation swaps which
        // single face is camera-facing, it doesn't add or remove faces overall for an
        // isolated wall tile.
        var battleground = LoneWallBattleground(out _);
        var camera = new Camera { Lock = new Vector2(400, 300) };
        camera.SetQuarterTurns(1); // 90 degrees
        var (ground, cap) = ArenaRenderer.WallScreenGeometry(camera, Vector2.Zero, Vector2.Zero, 2, 2, 14);

        var faces = ArenaRenderer.VisibleWallFaces(camera, battleground, 2, 2, ground, cap);

        Assert.Single(faces);
    }

    [Fact]
    public void GroundDepth_ZeroRotation_PaintsNorthActorBeforeSouthWall()
    {
        var camera = new Camera();
        var actorNorthOfWall = new Vector2(125, 75);
        var wallCenter = new Vector2(125, 125);

        float actorDepth = ArenaRenderer.GroundDepth(camera, actorNorthOfWall);
        float wallDepth = ArenaRenderer.GroundDepth(camera, wallCenter);

        Assert.True(actorDepth < wallDepth,
            "An actor north/behind the wall must paint first so the wall cap can hide it.");
    }

    [Fact]
    public void GroundDepth_ZeroRotation_PaintsSouthActorAfterNorthWall()
    {
        var camera = new Camera();
        var wallCenter = new Vector2(125, 125);
        var actorSouthOfWall = new Vector2(125, 175);

        float wallDepth = ArenaRenderer.GroundDepth(camera, wallCenter);
        float actorDepth = ArenaRenderer.GroundDepth(camera, actorSouthOfWall);

        Assert.True(wallDepth < actorDepth,
            "An actor south/in front of the wall must paint after it and remain visible.");
    }

    [Fact]
    public void GroundDepth_RotatesOcclusionAxisWithCamera()
    {
        var camera = new Camera();
        camera.SetQuarterTurns(1);
        var actorBehindAtThisAngle = new Vector2(175, 125);
        var wallCenter = new Vector2(125, 125);
        var actorInFrontAtThisAngle = new Vector2(75, 125);

        Assert.True(ArenaRenderer.GroundDepth(camera, actorBehindAtThisAngle)
                    < ArenaRenderer.GroundDepth(camera, wallCenter));
        Assert.True(ArenaRenderer.GroundDepth(camera, wallCenter)
                    < ArenaRenderer.GroundDepth(camera, actorInFrontAtThisAngle));
    }
}
