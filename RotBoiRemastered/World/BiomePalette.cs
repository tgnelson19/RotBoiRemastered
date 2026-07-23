using Microsoft.Xna.Framework;

namespace RotBoiRemastered.World;

/// <summary>
/// Ported from background.py's per-biome color dicts. The arena stays
/// deliberately restrained: each ward is mostly charcoal and slate, with one
/// low-saturation identity color. These are renderer palettes rather than
/// gameplay tile types, so biome flavor never changes collision rules.
/// </summary>
public readonly record struct BiomePalette(
    Color Ground, Color GroundAlt, Color Road, Color Interior,
    Color WallTop, Color WallFace, Color Accent, Color Detail);

public static class BiomePalettes
{
    public static readonly IReadOnlyList<BiomePalette> Soul = new[]
    {
        new BiomePalette(
            Ground: new Color(20, 18, 31), GroundAlt: new Color(27, 23, 40),
            Road: new Color(42, 34, 58), Interior: new Color(24, 21, 37),
            WallTop: new Color(63, 54, 82), WallFace: new Color(37, 31, 52),
            Accent: new Color(137, 103, 178), Detail: new Color(202, 181, 226)),
        new BiomePalette(
            Ground: new Color(25, 19, 31), GroundAlt: new Color(34, 24, 39),
            Road: new Color(50, 34, 55), Interior: new Color(29, 21, 35),
            WallTop: new Color(77, 52, 78), WallFace: new Color(45, 31, 49),
            Accent: new Color(177, 92, 149), Detail: new Color(226, 168, 207)),
        new BiomePalette(
            Ground: new Color(17, 23, 32), GroundAlt: new Color(22, 30, 41),
            Road: new Color(31, 43, 58), Interior: new Color(20, 26, 37),
            WallTop: new Color(48, 67, 84), WallFace: new Color(29, 40, 54),
            Accent: new Color(92, 139, 181), Detail: new Color(164, 204, 228)),
    };

    public static readonly IReadOnlyList<BiomePalette> Sound = new[]
    {
        new BiomePalette(
            Ground: new Color(35, 38, 48), GroundAlt: new Color(39, 42, 53),
            Road: new Color(48, 48, 57), Interior: new Color(31, 34, 45),
            WallTop: new Color(67, 67, 82), WallFace: new Color(43, 43, 56),
            Accent: new Color(91, 78, 119), Detail: new Color(120, 111, 137)),
        new BiomePalette(
            Ground: new Color(43, 39, 42), GroundAlt: new Color(48, 42, 45),
            Road: new Color(54, 47, 48), Interior: new Color(38, 32, 37),
            WallTop: new Color(77, 65, 68), WallFace: new Color(49, 39, 43),
            Accent: new Color(124, 62, 67), Detail: new Color(139, 91, 91)),
        new BiomePalette(
            Ground: new Color(34, 41, 48), GroundAlt: new Color(37, 45, 54),
            Road: new Color(43, 50, 58), Interior: new Color(29, 37, 47),
            WallTop: new Color(58, 70, 84), WallFace: new Color(36, 47, 59),
            Accent: new Color(62, 78, 108), Detail: new Color(91, 99, 126)),
    };

    public static readonly IReadOnlyList<BiomePalette> Touch = new[]
    {
        new BiomePalette(
            Ground: new Color(20, 31, 25), GroundAlt: new Color(24, 37, 28),
            Road: new Color(37, 48, 34), Interior: new Color(17, 27, 21),
            WallTop: new Color(48, 67, 48), WallFace: new Color(28, 43, 31),
            Accent: new Color(83, 104, 54), Detail: new Color(119, 104, 61)),
        new BiomePalette(
            Ground: new Color(31, 32, 22), GroundAlt: new Color(37, 38, 24),
            Road: new Color(49, 48, 29), Interior: new Color(27, 27, 18),
            WallTop: new Color(68, 65, 39), WallFace: new Color(45, 43, 25),
            Accent: new Color(104, 91, 48), Detail: new Color(130, 113, 63)),
        new BiomePalette(
            Ground: new Color(18, 29, 27), GroundAlt: new Color(21, 35, 31),
            Road: new Color(31, 45, 38), Interior: new Color(15, 25, 23),
            WallTop: new Color(42, 64, 55), WallFace: new Color(25, 41, 35),
            Accent: new Color(55, 100, 69), Detail: new Color(87, 117, 76)),
    };

    public static readonly IReadOnlyList<BiomePalette> Sight = new[]
    {
        new BiomePalette(
            Ground: new Color(49, 75, 91), GroundAlt: new Color(56, 84, 101),
            Road: new Color(75, 109, 125), Interior: new Color(43, 67, 82),
            WallTop: new Color(104, 174, 204), WallFace: new Color(60, 111, 137),
            Accent: new Color(234, 145, 61), Detail: new Color(247, 188, 96)),
        new BiomePalette(
            Ground: new Color(58, 80, 94), GroundAlt: new Color(64, 89, 104),
            Road: new Color(84, 116, 132), Interior: new Color(48, 70, 85),
            WallTop: new Color(126, 193, 218), WallFace: new Color(69, 123, 147),
            Accent: new Color(211, 111, 48), Detail: new Color(241, 164, 78)),
        new BiomePalette(
            Ground: new Color(43, 70, 87), GroundAlt: new Color(48, 78, 96),
            Road: new Color(69, 103, 121), Interior: new Color(37, 61, 76),
            WallTop: new Color(94, 164, 196), WallFace: new Color(53, 105, 132),
            Accent: new Color(242, 157, 67), Detail: new Color(252, 203, 116)),
    };

    public static readonly IReadOnlyList<BiomePalette> Chemesthesis = new[]
    {
        new BiomePalette(
            Ground: new Color(54, 31, 24), GroundAlt: new Color(64, 36, 26),
            Road: new Color(85, 48, 29), Interior: new Color(45, 25, 21),
            WallTop: new Color(126, 54, 33), WallFace: new Color(77, 35, 27),
            Accent: new Color(202, 77, 35), Detail: new Color(116, 132, 50)),
        new BiomePalette(
            Ground: new Color(50, 38, 22), GroundAlt: new Color(61, 45, 24),
            Road: new Color(82, 57, 29), Interior: new Color(42, 31, 18),
            WallTop: new Color(126, 79, 34), WallFace: new Color(76, 49, 24),
            Accent: new Color(160, 48, 34), Detail: new Color(104, 132, 52)),
        new BiomePalette(
            Ground: new Color(34, 44, 25), GroundAlt: new Color(40, 52, 28),
            Road: new Color(59, 67, 31), Interior: new Color(28, 37, 21),
            WallTop: new Color(79, 100, 43), WallFace: new Color(49, 62, 29),
            Accent: new Color(194, 66, 37), Detail: new Color(137, 142, 55)),
    };

    public static readonly IReadOnlyList<BiomePalette> Phantasia = new[]
    {
        new BiomePalette(
            Ground: new Color(43, 24, 52), GroundAlt: new Color(51, 28, 62),
            Road: new Color(68, 37, 78), Interior: new Color(36, 19, 45),
            WallTop: new Color(113, 55, 126), WallFace: new Color(68, 34, 80),
            Accent: new Color(198, 77, 170), Detail: new Color(226, 125, 199)),
        new BiomePalette(
            Ground: new Color(51, 23, 48), GroundAlt: new Color(61, 27, 57),
            Road: new Color(79, 35, 72), Interior: new Color(43, 18, 40),
            WallTop: new Color(129, 51, 112), WallFace: new Color(79, 31, 70),
            Accent: new Color(218, 82, 158), Detail: new Color(239, 137, 195)),
        new BiomePalette(
            Ground: new Color(36, 25, 55), GroundAlt: new Color(43, 29, 66),
            Road: new Color(58, 39, 83), Interior: new Color(30, 20, 47),
            WallTop: new Color(94, 61, 133), WallFace: new Color(57, 38, 84),
            Accent: new Color(177, 75, 184), Detail: new Color(214, 122, 218)),
    };
}
