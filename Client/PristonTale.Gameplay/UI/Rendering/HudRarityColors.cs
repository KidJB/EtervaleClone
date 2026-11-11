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

    private const float WashAlpha = 0.18f;

    // Gear of Uncommon grade and above: a faint wash of its grade's colour, drawn under its icon.
    public static void Wash(HudPainter painter, Rectangle bounds, ItemRarity rarity)
    {
        if (rarity >= ItemRarity.Uncommon)
        {
            painter.Fill(bounds, Of(rarity) * WashAlpha);
        }
    }

    // And a one-pixel border of that colour, drawn over its icon.
    public static void Border(HudPainter painter, Rectangle bounds, ItemRarity rarity)
    {
        if (rarity < ItemRarity.Uncommon)
        {
            return;
        }

        var color = Of(rarity);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
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
