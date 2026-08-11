using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.World;

/// <summary>Canonical name for the safe hub layout.</summary>
internal static class MindLayout
{
    public static Point SpawnTile => SoulLayout.SpawnTile;
    public static TileType[,] BuildTiles() =>
        SoulLayout.BuildTiles(SoulLayout.AllGateKeys
            .Where(CampaignProgression.PortalUnlocked)
            .ToHashSet());
}
