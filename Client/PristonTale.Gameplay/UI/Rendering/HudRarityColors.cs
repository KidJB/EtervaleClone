using Microsoft.Xna.Framework;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Rendering;

// The colours of the rarity grades (ItemRarities.Colors) for names and tooltips; under the cursor a name is drawn a
// little brighter.
public static class HudRarityColors
{
    private static readonly Color[] Normal = Build(0f);
    private static readonly Color[] Bright = Build(0.35f);

    public static Color Of(ItemRarity rarity, bool hovered = false)
    {
        var index = (int)rarity <= (int)ItemRarities.Highest ? (int)rarity : 0;
        return hovered ? Bright[index] : Normal[index];
    }

    private static Color[] Build(float lighten)
    {
        var colors = new Color[ItemRarities.Colors.Length];
        for (var index = 0; index < colors.Length; index++)
        {
            var (r, g, b) = ItemRarities.Colors[index];
            colors[index] = Color.Lerp(new Color(r, g, b), Color.White, lighten);
        }

        return colors;
    }
}
