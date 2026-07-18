using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

/// <summary>
/// Spot-checks against background.py's *_PALETTES tuples -- transcribing 15
/// hand-written color palettes by hand is exactly the kind of place a typo
/// slips in silently, so this isn't testing "logic" so much as guarding the
/// data entry itself.
/// </summary>
public class BiomePaletteTests
{
    [Fact]
    public void EachPaletteTable_HasThreeVariants()
    {
        Assert.Equal(3, BiomePalettes.Sound.Count);
        Assert.Equal(3, BiomePalettes.Touch.Count);
        Assert.Equal(3, BiomePalettes.Sight.Count);
        Assert.Equal(3, BiomePalettes.Chemesthesis.Count);
        Assert.Equal(3, BiomePalettes.Phantasia.Count);
    }

    [Fact]
    public void Sound_FirstVariant_MatchesPythonSource()
    {
        var palette = BiomePalettes.Sound[0];
        Assert.Equal(new Color(35, 38, 48), palette.Ground);
        Assert.Equal(new Color(120, 111, 137), palette.Detail);
    }

    [Fact]
    public void Touch_LastVariant_MatchesPythonSource()
    {
        var palette = BiomePalettes.Touch[2];
        Assert.Equal(new Color(18, 29, 27), palette.Ground);
        Assert.Equal(new Color(55, 100, 69), palette.Accent);
    }

    [Fact]
    public void Sight_MiddleVariant_MatchesPythonSource()
    {
        var palette = BiomePalettes.Sight[1];
        Assert.Equal(new Color(126, 193, 218), palette.WallTop);
    }

    [Fact]
    public void Chemesthesis_FirstVariant_MatchesPythonSource()
    {
        var palette = BiomePalettes.Chemesthesis[0];
        Assert.Equal(new Color(202, 77, 35), palette.Accent);
    }

    [Fact]
    public void Phantasia_LastVariant_MatchesPythonSource()
    {
        var palette = BiomePalettes.Phantasia[2];
        Assert.Equal(new Color(214, 122, 218), palette.Detail);
    }
}
