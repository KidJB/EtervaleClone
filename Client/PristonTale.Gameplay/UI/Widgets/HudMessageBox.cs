using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The message box of every yes-or-no question, with its Yes and No buttons and its text lines, in the RPT art
// (assets/Hud/MessageBox). It is centred on the HUD's screen (Place, every frame), at (300,200) until the first placement.
public static class HudMessageBox
{
    public const int VisibleWidth = 230;
    public const int Height = 161;
    private const string Folder = "Hud/MessageBox/";
    private const string Background = Folder + "background_normal.png";
    private const string Yes = Folder + "btnyes.png";
    private const string YesLit = Folder + "btnyes_.png";
    private const string No = Folder + "btnno.png";
    private const string NoLit = Folder + "btnno_.png";
    public const string OkArt = Folder + "btnok.png";
    public const string OkLit = Folder + "btnok_.png";
    public const string CancelArt = Folder + "btncancel.png";
    public const string CancelLit = Folder + "btncancel_.png";
    public const int TitleY = 18;
    public static readonly string[] Art = [Background, Yes, YesLit, No, NoLit, OkArt, OkLit, CancelArt, CancelLit];

    private static Point _origin = new(300, 200);

    public static Rectangle Bounds => new(_origin.X, _origin.Y, VisibleWidth, Height);
    public static Rectangle Accept => new(_origin.X + 26, _origin.Y + 116, 88, 35);
    public static Rectangle Reject => new(_origin.X + 115, _origin.Y + 116, 88, 35);

    public static void Place(HudLayout layout) => _origin = new Point((layout.Width - VisibleWidth) / 2, (layout.Height - Height) / 2);

    public static void DrawFrame(HudPainter painter) => painter.Sprite(Background, _origin.X, _origin.Y);

    // A question's third line sits under the second.
    public static void Draw(HudPainter painter, string first, string second, Point pointer, string third = null)
    {
        var bounds = Bounds;
        DrawFrame(painter);
        painter.Sprite(Accept.Contains(pointer) ? YesLit : Yes, Accept.X, Accept.Y);
        painter.Sprite(Reject.Contains(pointer) ? NoLit : No, Reject.X, Reject.Y);
        painter.TextCentered(painter.Fit(first, VisibleWidth - 24), bounds.Left, bounds.Right, bounds.Y + TitleY, HudTextColors.BoxTitle);
        painter.TextCentered(painter.Fit(second, VisibleWidth - 24), bounds.Left, bounds.Right, bounds.Y + 50);
        if (third is not null)
        {
            painter.TextCentered(third, bounds.Left, bounds.Right, bounds.Y + 72);
        }
    }
}
