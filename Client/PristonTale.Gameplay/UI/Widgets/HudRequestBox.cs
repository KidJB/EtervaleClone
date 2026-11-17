using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// A request another player sent (a party, a raid, a trade, a clan invitation) as the game's message box: its title, the
// question in up to two lines, "Automatically closes in N sec..." and Yes and No. It sits centred just above where a
// confirmation box shows, answers once and closes with its answer or after its time.
public sealed class HudRequestBox(long lifeMs)
{
    private const int Width = HudMessageBox.VisibleWidth;
    private const int Height = HudMessageBox.Height;
    private const int Gap = 10;
    private const int TextMargin = 22;

    private double _openedMs;
    private string _title = "";
    private string _question = "";
    private string[] _lines;
    private string _closes = "";
    private int _shownSeconds = -1;

    public bool IsOpen => FromName is not null;
    public ulong FromId { get; private set; }
    public string FromName { get; private set; }

    // Centred, one box and a gap above the confirmation box's place.
    public static Rectangle Box(HudLayout layout) =>
        new((layout.Width - Width) / 2, Math.Max(0, (layout.Height - Height) / 2 - Height - Gap), Width, Height);

    public static Rectangle Accept(HudLayout layout)
    {
        var box = Box(layout);
        return new Rectangle(box.X + 26, box.Y + 116, 88, 35);
    }

    public static Rectangle Reject(HudLayout layout)
    {
        var box = Box(layout);
        return new Rectangle(box.X + 115, box.Y + 116, 88, 35);
    }

    public void Show(ulong fromId, string fromName, string title, string question, double nowMs)
    {
        (FromId, FromName, _title, _question, _openedMs) = (fromId, fromName, title.ToUpperInvariant(), question, nowMs);
        _lines = null;
        _shownSeconds = -1;
    }

    public void Close() => FromName = null;

    public void Update(double nowMs)
    {
        if (IsOpen && nowMs - _openedMs >= lifeMs)
        {
            Close();
        }
    }

    public bool Captures(HudLayout layout, Point pointer) => IsOpen && Box(layout).Contains(pointer);

    // The answer a click gives (true accept, false refuse), or null when it missed both buttons.
    public bool? Click(HudLayout layout, Point pointer)
    {
        if (!IsOpen)
        {
            return null;
        }

        var answer = Accept(layout).Contains(pointer) ? true : Reject(layout).Contains(pointer) ? false : (bool?)null;
        if (answer is not null)
        {
            Close();
        }

        return answer;
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer, double nowMs)
    {
        if (!IsOpen)
        {
            return;
        }

        _lines ??= [.. HudTextLayout.Wrap(_question, Width - 2 * TextMargin, text => painter.Measure(text)).Take(2)];
        var seconds = (int)Math.Ceiling(Math.Max(0, lifeMs - (nowMs - _openedMs)) / 1000);
        if (seconds != _shownSeconds)
        {
            _shownSeconds = seconds;
            _closes = ClientText.Format("Request.Closes", seconds);
        }

        var box = Box(layout);
        painter.Flush();
        painter.Sprite(HudMessageBox.Background, box.X, box.Y);
        painter.TextCentered(_title, box.Left, box.Right, box.Y + HudMessageBox.TitleY, HudTextColors.BoxTitle);
        for (var index = 0; index < _lines.Length; index++)
        {
            painter.TextCentered(_lines[index], box.Left, box.Right, box.Y + 48 + index * 18, Color.White);
        }

        painter.TextCentered(_closes, box.Left, box.Right, box.Y + 92, HudTextColors.Hint);
        var accept = Accept(layout);
        var reject = Reject(layout);
        painter.Sprite(accept.Contains(pointer) ? HudMessageBox.YesLit : HudMessageBox.Yes, accept.X, accept.Y);
        painter.Sprite(reject.Contains(pointer) ? HudMessageBox.NoLit : HudMessageBox.No, reject.X, reject.Y);
    }
}
