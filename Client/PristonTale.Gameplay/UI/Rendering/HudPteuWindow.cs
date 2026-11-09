using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Rendering;

// The window style shared by the Teleport Core and The Cabinet (PTEU's window art in Hud/Teleport): a dark frame with a
// thin bronze edge, the title plate over its top edge, darker panels inside, list rows split by the thin separator, the
// small scroller and the CLOSE button.
public static class HudPteuWindow
{
    private const string Folder = "Hud/Teleport/";
    private const string PlateLeft = Folder + "plate-left.png";
    private const string PlateCenter = Folder + "plate-center.png";
    private const string PlateRight = Folder + "plate-right.png";
    private const string CloseArt = Folder + "close.png";
    private const string CloseHoverArt = Folder + "close-hover.png";
    private const string SeparatorV = Folder + "separator-v.png";
    private const string SeparatorH = Folder + "separator-h.png";
    private const string ScrollerArt = Folder + "scroller.png";
    public const string TeleportHeader = Folder + "header-teleport.png";
    public const int PlateHeight = 43;
    private const int PlateEnd = 32;
    private const int ScrollerWidth = 8;
    private const int ScrollerHeight = 11;
    public static readonly string[] Art = [PlateLeft, PlateCenter, PlateRight, TeleportHeader, CloseArt, CloseHoverArt, SeparatorV, SeparatorH, ScrollerArt];

    public static readonly Color FrameColor = new(22, 19, 17, 245);
    public static readonly Color PanelColor = new(14, 12, 11, 235);
    public static readonly Color BorderColor = new(96, 84, 66);
    public static readonly Color SectionColor = new(240, 175, 70);
    public static readonly Color TextColor = new(235, 230, 220);
    public static readonly Color SelectedColor = new(70, 58, 42, 220);
    public static readonly Color HoverColor = new(50, 42, 32, 200);

    // The title plate's place, centred on the window's top edge.
    public static Rectangle Plate(Rectangle window, int width) => new(window.X + (window.Width - width) / 2, window.Y - 22, width, PlateHeight);

    // The CLOSE button centred at a point's x, its top at y.
    public static Rectangle CloseButton(int centerX, int y) => new(centerX - 93 / 2, y, 93, 23);

    public static void Frame(HudPainter painter, Rectangle window)
    {
        painter.Fill(window, FrameColor);
        Border(painter, window, BorderColor);
    }

    public static void Panel(HudPainter painter, Rectangle panel)
    {
        painter.Fill(panel, PanelColor);
        Border(painter, panel, BorderColor);
    }

    // The plate's ends and stretched middle; a title drawn in the middle when one is given.
    public static void DrawPlate(HudPainter painter, Rectangle plate, string title = null, Color? color = null)
    {
        painter.Sprite(PlateLeft, plate.X, plate.Y);
        painter.Stretch(PlateCenter, new Rectangle(plate.X + PlateEnd, plate.Y, plate.Width - PlateEnd * 2, plate.Height));
        painter.Sprite(PlateRight, plate.Right - PlateEnd, plate.Y);
        if (title is not null)
        {
            painter.TextCentered(title, plate.X, plate.Right, plate.Y + 13, color ?? SectionColor);
        }
    }

    public static void DrawClose(HudPainter painter, Rectangle button, Point pointer) =>
        painter.Sprite(button.Contains(pointer) ? CloseHoverArt : CloseArt, button.X, button.Y);

    public static void SeparatorBelow(HudPainter painter, Rectangle row) => painter.Stretch(SeparatorH, new Rectangle(row.X, row.Bottom, row.Width, 1));

    public static void SeparatorAt(HudPainter painter, int x, int y, int height) => painter.Stretch(SeparatorV, new Rectangle(x, y, 1, height));

    // The scroller down an area's right edge, placed by how far the list is scrolled; none when everything fits.
    public static void Scroller(HudPainter painter, Rectangle area, int scroll, int count, int visible)
    {
        if (count <= visible)
        {
            return;
        }

        var travel = area.Height - ScrollerHeight;
        var y = area.Y + (int)((long)travel * scroll / Math.Max(1, count - visible));
        painter.Sprite(ScrollerArt, area.Right - ScrollerWidth - 1, y);
    }

    public static void Border(HudPainter painter, Rectangle bounds, Color color)
    {
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
    }
}
