using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Inventory;

// The item info box framed by its rarity grade (Hud/ItemBox/<grade>/): a dark fill, 24-pixel corners and edges stretched
// between them, the grade's flourishes at the top-left and bottom-right corners (none and common have none), a glow
// behind the name and a divider under it. Without the art the classic tiled info box stands in.
public static class HudItemFrame
{
    private const string Folder = "Hud/ItemBox/";
    private const int Edge = 24;
    private const int Flourish = 95;
    // The flourishes sit on the box's corners, this far out so their corner points meet the frame's.
    private const int FlourishOut = 4;

    // Room the frame's edges take on each side of the text, and the gap under the name that holds the divider.
    public const int Padding = 14;
    public const int NameGap = 10;

    // The fill behind the text lets the scene show through.
    private const int FillAlpha = 165;

    // The frame's line runs 4 to 8 pixels in from the tiles' outer edge; the fill starts under it so none shows outside.
    private const int LineInset = 6;
    private const int GlowWidth = 180;
    private const int GlowHeight = 50;
    private const int DividerWidth = 150;
    private const int DividerHeight = 14;
    private static readonly string[] Grades = ["none", "common", "uncommon", "rare", "epic", "legendary"];
    private static readonly string[] Parts = ["topleft", "top", "topright", "left", "right", "bottomleft", "bottom", "bottomright", "corner", "bottomcorner",
        "nameframebackgroud", "divider"];

    // [grade][part] asset paths, built once.
    private static readonly string[][] Paths = Build();

    public static readonly string[] Art = Paths.SelectMany(static paths => paths).ToArray();

    private static string[][] Build()
    {
        var paths = new string[Grades.Length][];
        for (var grade = 0; grade < Grades.Length; grade++)
        {
            paths[grade] = new string[Parts.Length];
            for (var part = 0; part < Parts.Length; part++)
            {
                paths[grade][part] = Folder + Grades[grade] + "/" + Parts[part] + ".png";
            }
        }

        return paths;
    }

    // The grade's soft glow (the art behind the item box's name), which also pools under a graded drop's beam.
    public static string GlowOf(ItemRarity rarity) => Paths[(int)rarity <= (int)ItemRarities.Highest ? (int)rarity : 0][10];

    public static bool HasArt(HudPainter painter) => painter.Texture(Paths[0][0]) is not null;

    // Draws the frame on bounds; false when the grade's art is not loaded (the caller then draws the classic box).
    public static bool Draw(HudPainter painter, Rectangle bounds, ItemRarity rarity, int nameY)
    {
        var art = Paths[(int)rarity <= (int)ItemRarities.Highest ? (int)rarity : 0];
        if (painter.Texture(art[0]) is null)
        {
            return false;
        }

        var inside = new Rectangle(bounds.X + LineInset, bounds.Y + LineInset, bounds.Width - LineInset * 2, bounds.Height - LineInset * 2);
        painter.ColorBox(inside, 8, 8, 12, FillAlpha);
        var center = bounds.X + bounds.Width / 2;
        painter.Stretch(art[10], new Rectangle(center - GlowWidth / 2, nameY - GlowHeight / 2 + 7, GlowWidth, GlowHeight));
        painter.Stretch(art[1], new Rectangle(bounds.X + Edge, bounds.Y, bounds.Width - Edge * 2, Edge));
        painter.Stretch(art[6], new Rectangle(bounds.X + Edge, bounds.Bottom - Edge, bounds.Width - Edge * 2, Edge));
        painter.Stretch(art[3], new Rectangle(bounds.X, bounds.Y + Edge, Edge, bounds.Height - Edge * 2));
        painter.Stretch(art[4], new Rectangle(bounds.Right - Edge, bounds.Y + Edge, Edge, bounds.Height - Edge * 2));
        painter.Sprite(art[0], bounds.X, bounds.Y);
        painter.Sprite(art[2], bounds.Right - Edge, bounds.Y);
        painter.Sprite(art[5], bounds.X, bounds.Bottom - Edge);
        painter.Sprite(art[7], bounds.Right - Edge, bounds.Bottom - Edge);
        painter.Sprite(art[8], bounds.X - FlourishOut, bounds.Y - FlourishOut);
        painter.Sprite(art[9], bounds.Right - Flourish + FlourishOut, bounds.Bottom - Flourish + FlourishOut);
        var dividerWidth = Math.Min(DividerWidth, bounds.Width - Edge);
        painter.Stretch(art[11], new Rectangle(center - dividerWidth / 2, nameY + 14 + NameGap / 2 - DividerHeight / 2, dividerWidth, DividerHeight));
        return true;
    }
}
