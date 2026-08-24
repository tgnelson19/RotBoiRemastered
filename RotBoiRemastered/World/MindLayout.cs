using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.World;

/// <summary>Canonical name for the safe hub layout.</summary>
internal static class MindLayout
{
    public static Point SpawnTile => SoulLayout.SpawnTile;
    public static TileType[,] BuildTiles()
    {
        var unlocked = SoulLayout.AllGateKeys
            .Where(CampaignProgression.PortalUnlocked)
            .ToHashSet();
        // The Void isn't a campaign gate -- it's a secret wall shot open from
        // inside The Mind, tracked on the profile instead.
        if (GameProfile.Profile.VoidPassageDiscovered)
            unlocked.Add("the_void");
        return SoulLayout.BuildTiles(unlocked);
    }
}
