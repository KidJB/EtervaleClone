using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Markets;

// The look the two market windows share, drawn at its size so nothing stretches: an ornate brown frame with its title, close button,
// dark panels, gold buttons and drop-downs. A frame art file, when one is present, replaces the drawn frame.
internal static class HudMarketFrame
{
    public static readonly Color Back = new(62, 41, 26, 255);
    public static readonly Color Panel = new(36, 24, 16, 255);
    public static readonly Color PanelLine = new(112, 80, 44, 255);
    public static readonly Color Slot = new(20, 13, 9, 255);
    public static readonly Color GoldText = new(255, 218, 130);
    public static readonly Color Text = new(232, 224, 208);
    public static readonly Color Dim = new(168, 156, 136);
    public static readonly Color Coin = new(120, 190, 255);
    public static readonly Color Good = new(140, 230, 140);
    public static readonly Color Bad = new(255, 120, 110);
    private static readonly Color Button = new(214, 158, 56, 255);
    private static readonly Color ButtonLit = new(240, 190, 90, 255);
    private static readonly Color ButtonOff = new(96, 80, 60, 255);
    private static readonly Color ButtonLine = new(120, 76, 22, 255);
    private static readonly Color Teal = new(28, 128, 118, 255);
    private static readonly Color TealLit = new(44, 160, 148, 255);
    private static readonly Color TealLine = new(14, 70, 64, 255);
    private static readonly Color DropBack = new(52, 36, 24, 255);
    private static readonly Color DropLit = new(78, 54, 34, 255);
    private const int Border = 3;
    public const int TitleHeight = 40;

    public static Rectangle CloseBox(Rectangle window) => new(window.Right - 30, window.Y + 8, 22, 22);

    public static void Window(HudPainter painter, Rectangle window, string art, string title, Point pointer)
    {
        if (painter.Texture(art) is not null)
        {
            painter.Sprite(art, window.X, window.Y);
            return;
        }

        painter.Fill(window, PanelLine);
        painter.Fill(new Rectangle(window.X + Border, window.Y + Border, window.Width - 2 * Border, window.Height - 2 * Border), Back);
        painter.Fill(new Rectangle(window.X + Border, window.Y + TitleHeight - 2, window.Width - 2 * Border, 2), PanelLine);
        painter.Text(title, window.X + 18, window.Y + 12, GoldText);
        var close = CloseBox(window);
        painter.Fill(close, close.Contains(pointer) ? ButtonLit : PanelLine);
        painter.TextCentered("X", close.X, close.Right, close.Y + 3, Text);
    }

    public static void Box(HudPainter painter, Rectangle box, Color fill)
    {
        painter.Fill(box, PanelLine);
        painter.Fill(new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2), fill);
    }

    public static void GoldButton(HudPainter painter, Rectangle box, string label, Point pointer, bool enabled = true)
    {
        painter.Fill(box, ButtonLine);
        var lit = enabled && box.Contains(pointer);
        painter.Fill(new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2), !enabled ? ButtonOff : lit ? ButtonLit : Button);
        painter.TextCentered(label, box.X, box.Right, box.Y + (box.Height - 14) / 2, Color.White);
    }

    // The details page's buy button: teal, as the market's own action.
    public static void TealButton(HudPainter painter, Rectangle box, string label, Point pointer, bool enabled = true)
    {
        painter.Fill(box, TealLine);
        var lit = enabled && box.Contains(pointer);
        painter.Fill(new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2), !enabled ? ButtonOff : lit ? TealLit : Teal);
        painter.TextCentered(label, box.X, box.Right, box.Y + (box.Height - 14) / 2, Color.White);
    }

    public static void Field(HudPainter painter, Rectangle box, TextInputField field, string hint)
    {
        Box(painter, box, Slot);
        var shown = field.Text.Length == 0 && !field.Focused ? hint : field.DisplayText + (field.CaretVisible ? "|" : "");
        painter.Text(painter.Fit(shown, box.Width - 10), box.X + 5, box.Y + (box.Height - 14) / 2, field.Text.Length == 0 && !field.Focused ? Dim : Text);
    }

    public static void DropBox(HudPainter painter, Rectangle box, string label, bool lit, bool arrow = true)
    {
        Box(painter, box, lit ? DropLit : DropBack);
        painter.TextCentered(painter.Fit(label, box.Width - 26), box.X, box.Right - 14, box.Y + (box.Height - 14) / 2, Text);
        if (arrow)
        {
            painter.Text("v", box.Right - 16, box.Y + (box.Height - 14) / 2 - 1, Dim);
        }
    }
}

// A drop-down over a fixed list of texts; its open list draws over what is below it, so the owner draws it last and clicks it first.
internal sealed class HudMarketCombo(params string[] options)
{
    private readonly string[] _options = options;
    public int Selected { get; set; }
    public bool Opened { get; private set; }
    public string Current => _options[Selected];

    public void Close() => Opened = false;

    // True when the click was this control's; Changed tells whether the choice moved.
    public bool Click(Rectangle box, Point pointer, out bool changed)
    {
        changed = false;
        if (Opened)
        {
            var hit = false;
            for (var index = 0; index < _options.Length; index++)
            {
                if (Row(box, index).Contains(pointer))
                {
                    changed = Selected != index;
                    Selected = index;
                    hit = true;
                }
            }

            Opened = false;
            return hit || box.Contains(pointer);
        }

        if (box.Contains(pointer))
        {
            Opened = true;
            return true;
        }

        return false;
    }

    public void Draw(HudPainter painter, Rectangle box, Point pointer) => HudMarketFrame.DropBox(painter, box, Current, box.Contains(pointer));

    // Text draws in a later pass than fills, so what is under the list is flushed first and the list covers it.
    public void DrawList(HudPainter painter, Rectangle box, Point pointer)
    {
        if (!Opened)
        {
            return;
        }

        painter.Flush();
        for (var index = 0; Opened && index < _options.Length; index++)
        {
            var row = Row(box, index);
            HudMarketFrame.DropBox(painter, row, _options[index], row.Contains(pointer), arrow: false);
        }
    }

    private static Rectangle Row(Rectangle box, int index) => new(box.X, box.Bottom + index * box.Height, box.Width, box.Height);
}
