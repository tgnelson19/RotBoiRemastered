using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Presentation;

/// <summary>
/// Four bounded cosmetic sockets driven by the carried build. No socket
/// changes the player footprint or introduces a gameplay hit target.
/// </summary>
public static class PlayerRegaliaRenderer
{
    public static void DrawRear(
        SpriteBatch spriteBatch,
        RunState state,
        Vector2 center,
        Vector2 axisX,
        Vector2 axisY,
        float size,
        float time,
        float intensity)
    {
        int forged = CoreForgeCount(state);
        int satellites = Math.Min(3, forged);
        for (int index = 0; index < satellites; index++)
        {
            float angle = MathF.Floor(
                (time * (.8f + index * .16f) + index * MathF.Tau / 3f) * 12f)
                / 12f;
            Vector2 point = center
                + axisX * MathF.Cos(angle) * size * (.68f + index * .08f)
                + axisY * MathF.Sin(angle) * size * (.42f + index * .05f);
            int mote = Math.Max(3, (int)(size * .09f));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)point.X - mote / 2,
                    (int)point.Y - mote / 2, mote, mote),
                CoreForgeColor(state, index) * (.5f + .5f * intensity));
        }
    }

    public static void DrawFront(
        SpriteBatch spriteBatch,
        RunState state,
        Vector2 center,
        Vector2 axisX,
        Vector2 axisY,
        float size,
        float time)
    {
        int equipped = state.Equipment.Values.Count(item => item is not null);
        Color accent = DominantBuildColor(state);
        int width = Math.Max(2, (int)(size * .055f));

        // Shoulder socket: the number of carried equipment pieces decides
        // whether the single brace becomes a balanced pair.
        if (equipped >= 2)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 shoulder = center
                    + axisX * side * size * .56f
                    - axisY * size * .18f;
                Primitives2D.Line(spriteBatch,
                    shoulder - axisY * size * .2f,
                    shoulder + axisX * side * size * .18f,
                    accent, width);
            }
        }

        // Crown socket: high-rarity equipment earns an architectural crest,
        // never a larger body.
        int highestRarity = state.Equipment.Values
            .Where(item => item is not null)
            .Select(item => RarityRank(item!.Rarity))
            .DefaultIfEmpty(0)
            .Max();
        if (highestRarity >= 3)
        {
            Vector2 crown = center - axisY * size * .62f;
            Primitives2D.Line(spriteBatch,
                crown - axisX * size * .28f,
                crown - axisX * size * .1f - axisY * size * .18f,
                accent, width);
            Primitives2D.Line(spriteBatch,
                crown - axisX * size * .1f - axisY * size * .18f,
                crown + axisY * size * .02f,
                accent, width);
            Primitives2D.Line(spriteBatch,
                crown + axisY * size * .02f,
                crown + axisX * size * .12f - axisY * size * .18f,
                accent, width);
            Primitives2D.Line(spriteBatch,
                crown + axisX * size * .12f - axisY * size * .18f,
                crown + axisX * size * .28f,
                accent, width);
        }
    }

    public static Color DominantBuildColor(RunState state)
    {
        if (state.UpgradeTypeCounts.Count == 0)
            return state.PlayerEdgeColor;
        string family = state.UpgradeTypeCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First().Key;
        return family.ToLowerInvariant() switch
        {
            var value when value.Contains("critical") => UiTheme.Gold,
            var value when value.Contains("volley") => UiTheme.Blue,
            var value when value.Contains("harvest") => UiTheme.Green,
            var value when value.Contains("survival") => UiTheme.Cream,
            var value when value.Contains("tempo") => UiTheme.Purple,
            _ => state.PlayerEdgeColor,
        };
    }

    private static int CoreForgeCount(RunState state) =>
        state.Equipment.Values.Count(item => item?.CoreForge is not null);

    private static Color CoreForgeColor(RunState state, int requestedIndex)
    {
        int index = 0;
        foreach (var item in state.Equipment.Values)
        {
            if (item?.CoreForge is null)
                continue;
            CoreForgeDefinition? forge = Items.CoreForgesByPathKey.Values
                .FirstOrDefault(value => value.Key == item.CoreForge);
            if (forge is not null && index++ == requestedIndex)
                return GamePaths.PathsByKey[forge.PathKey].Accent;
        }
        return state.PlayerEdgeColor;
    }

    private static int RarityRank(string rarity) => rarity switch
    {
        "Unique" => 6,
        "Mythical" => 5,
        "Legendary" => 4,
        "Epic" => 3,
        "Rare" => 2,
        _ => 1,
    };
}
