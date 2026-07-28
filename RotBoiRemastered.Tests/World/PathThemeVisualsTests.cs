using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

public sealed class PathThemeVisualsTests
{
    public static TheoryData<string, PathDecorationKind> ThemeCases => new()
    {
        { "touch", PathDecorationKind.DripEmitter },
        { "sight", PathDecorationKind.RippleEmitter },
        { "sound", PathDecorationKind.WindEmitter },
        { "phantasia", PathDecorationKind.StarEmitter },
        { "chemesthesis", PathDecorationKind.AshEmitter },
    };

    public static TheoryData<string, PathDecorationKind, PathDecorationKind> LandmarkCases => new()
    {
        { "touch", PathDecorationKind.BrickRunes, PathDecorationKind.PressureTank },
        { "sight", PathDecorationKind.MosaicLens, PathDecorationKind.MirrorArch },
        { "sound", PathDecorationKind.ResonanceTiles, PathDecorationKind.OrganStack },
        { "phantasia", PathDecorationKind.DreamGlyph, PathDecorationKind.LanternSpire },
        { "chemesthesis", PathDecorationKind.CinderPlate, PathDecorationKind.FurnaceIdol },
    };

    [Theory]
    [MemberData(nameof(ThemeCases))]
    public void GenerateDecorations_ProvidesAllVisualLayersAndCorrectAmbience(
        string senseKey, PathDecorationKind ambientKind)
    {
        var layout = PathFloorGenerator.Generate(senseKey, 3, new Random(313));
        var decorations = layout.Decorations;

        Assert.Contains(decorations, decoration =>
            decoration.Layer is PathDecorationLayer.Floor or PathDecorationLayer.Low);
        Assert.Contains(decorations, decoration => decoration.Layer == PathDecorationLayer.Raised);
        Assert.Contains(decorations, decoration =>
            decoration.Layer == PathDecorationLayer.Ambient && decoration.Kind == ambientKind);
        Assert.Contains(decorations, decoration => decoration.RoomId == layout.StartRoom.Id);
        Assert.Contains(decorations, decoration => decoration.RoomId == layout.BossRoom.Id);
        Assert.InRange(decorations.Count, 25, 225);
    }

    [Theory]
    [InlineData("sight")]
    [InlineData("phantasia")]
    public void GenerateDecorations_CenterAxisThemesRetainRaisedProps(string senseKey)
    {
        var layout = PathFloorGenerator.Generate(senseKey, 4, new Random(99));

        Assert.Contains(layout.Decorations, decoration =>
            decoration.Layer == PathDecorationLayer.Raised
            && decoration.Kind != PathThemeVisuals.For(senseKey).AmbientEmitter);
    }

    [Theory]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("sound")]
    [InlineData("phantasia")]
    [InlineData("chemesthesis")]
    public void SecondAct_IncreasesEnvironmentalDeterioration(string senseKey)
    {
        var firstAct = PathFloorGenerator.Generate(senseKey, 5, new Random(515));
        var secondAct = PathFloorGenerator.Generate(senseKey, 6, new Random(515));

        Assert.True(secondAct.Decorations.Count > firstAct.Decorations.Count);
        Assert.True(secondAct.Decorations.Count(decoration =>
            decoration.Layer == PathDecorationLayer.Ambient)
            > firstAct.Decorations.Count(decoration =>
                decoration.Layer == PathDecorationLayer.Ambient));
    }

    [Fact]
    public void Profiles_CoverEveryPlayableSense()
    {
        Assert.Equal(GamePaths.Paths.Select(path => path.Key).Order(),
            PathThemeVisuals.Profiles.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(LandmarkCases))]
    public void Profiles_AddDistinctFloorMaterialsAndArchitecture(
        string senseKey,
        PathDecorationKind floorMaterial,
        PathDecorationKind landmark)
    {
        var profile = PathThemeVisuals.For(senseKey);
        var layout = PathFloorGenerator.Generate(senseKey, 4, new Random(913));

        Assert.Contains(floorMaterial, profile.FloorMotifs);
        Assert.Contains(landmark, profile.RaisedProps);
        Assert.Contains(layout.Decorations, decoration =>
            decoration.Kind == landmark && decoration.Layer == PathDecorationLayer.Raised);
    }

    [Fact]
    public void TreasureRooms_HaveAReadableCentralSeal()
    {
        PathFloorLayout? layout = null;
        for (int seed = 0; seed < 50 && layout is null; seed++)
        {
            var candidate = PathFloorGenerator.Generate("phantasia", 4, new Random(seed));
            if (candidate.TreasureRooms.Count > 0)
                layout = candidate;
        }

        Assert.NotNull(layout);
        Assert.All(layout!.TreasureRooms, room =>
            Assert.Contains(layout.Decorations, decoration =>
                decoration.RoomId == room.Id
                && decoration.Kind == PathDecorationKind.TreasureSeal
                && decoration.WorldPosition == room.WorldCenter));
    }

    [Fact]
    public void CorridorDecorations_ProvideDirectionAndThresholdReadability()
    {
        var layout = PathFloorGenerator.Generate("sound", 7, new Random(404));

        Assert.Contains(layout.Decorations,
            decoration => decoration.Kind == PathDecorationKind.RouteChevron);
        Assert.Equal(layout.Connections.Count,
            layout.Decorations.Count(decoration =>
                decoration.Kind == PathDecorationKind.ThresholdRune));
    }

    [Theory]
    [MemberData(nameof(LandmarkCases))]
    public void ProtectedEntrances_HaveAThemeCrestPairedArchitectureAndExtraAmbience(
        string senseKey,
        PathDecorationKind entranceCrest,
        PathDecorationKind landmark)
    {
        var layout = PathFloorGenerator.Generate(senseKey, 4, new Random(818));
        var entrance = layout.Decorations
            .Where(decoration => decoration.RoomId == layout.StartRoom.Id)
            .ToList();

        Assert.Equal(PathThemeVisuals.EntranceCrestFor(senseKey), entranceCrest);
        Assert.Contains(entrance, decoration =>
            decoration.Kind == entranceCrest
            && decoration.WorldPosition == layout.StartRoom.WorldCenter
            && decoration.Scale >= 4f);
        Assert.Contains(entrance, decoration =>
            decoration.Kind == landmark
            && decoration.Layer == PathDecorationLayer.Raised);
        Assert.True(entrance.Count(decoration =>
            decoration.Layer == PathDecorationLayer.Ambient) >= 3);
    }

    [Theory]
    [InlineData("touch")]
    [InlineData("sight")]
    [InlineData("sound")]
    [InlineData("phantasia")]
    [InlineData("chemesthesis")]
    public void GrandArenas_HaveAuthoredCenterpiecesRingsAndPerimeterMonuments(
        string senseKey)
    {
        var layout = PathFloorGenerator.Generate(senseKey, 4, new Random(919));
        var arenas = layout.Rooms
            .Where(room => room.Shape == PathRoomShape.GrandArena)
            .ToList();

        Assert.NotEmpty(arenas);
        foreach (var arena in arenas)
        {
            var decorations = layout.Decorations
                .Where(decoration => decoration.RoomId == arena.Id)
                .ToList();
            Assert.Contains(decorations, decoration =>
                decoration.Kind == PathThemeVisuals.GrandArenaCenterpieceFor(senseKey)
                && decoration.WorldPosition == arena.WorldCenter
                && decoration.Scale >= 6f);
            Assert.True(decorations.Count(decoration =>
                decoration.Layer is PathDecorationLayer.Floor or PathDecorationLayer.Low) >= 10);
            Assert.True(decorations.Count(decoration =>
                decoration.Layer == PathDecorationLayer.Raised) >= 4);
            Assert.True(decorations.Count(decoration =>
                decoration.Layer == PathDecorationLayer.Ambient) >= 5);
        }
    }
}
