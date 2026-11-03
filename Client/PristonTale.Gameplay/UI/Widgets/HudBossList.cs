using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The world's bosses beside the minimap: a skull button left of the map's title opens a window of every scheduled boss,
// the field it spawns in and the time to its next pass, soonest first, counted down on the client from the list's arrival.
// The wheel scrolls it; the button, its close mark or Escape close it. A row's time text is formatted again only when its
// second changes.
public sealed class HudBossList
{
    public const int ButtonSize = 16;
    private const int Width = 300;
    private const int RowHeight = 16;
    private const int VisibleRows = 12;
    private const int Header = 20;
    private const int Padding = 6;
    private static readonly Color TitleColor = new(255, 220, 140);
    private static readonly Color NameColor = new(255, 180, 180);
    private static readonly Color FieldColor = new(210, 210, 210);
    private static readonly Color TimeColor = Color.White;

    private IReadOnlyList<BossListInfo> _list = [];
    private double _receivedMs;
    private long[] _shown = [];
    private string[] _times = [];
    private int _scroll;

    public bool Open { get; private set; }

    public void Receive(IReadOnlyList<BossListInfo> list, double nowMs)
    {
        if (ReferenceEquals(list, _list))
        {
            return;
        }

        _list = list;
        _receivedMs = nowMs;
        _shown = new long[list.Count];
        Array.Fill(_shown, -1);
        _times = new string[list.Count];
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    public void Toggle() => Open = !Open;

    public void Close() => Open = false;

    // The button sits left of the minimap's title strip (minimapArea: the map and its title).
    public static Rectangle Button(Rectangle minimapArea) =>
        new(Math.Max(0, minimapArea.X - ButtonSize - 2), minimapArea.Y, ButtonSize, ButtonSize);

    // The window hangs left of the minimap, its top level with the title, kept on screen.
    public Rectangle Window(HudLayout layout, Rectangle minimapArea)
    {
        var rows = Math.Max(1, Math.Min(VisibleRows, _list.Count));
        var height = Header + rows * RowHeight + Padding;
        return layout.PlaceOverlay(new Point(minimapArea.X - Width - ButtonSize - 6, minimapArea.Y), Width, height);
    }

    public static Rectangle CloseMark(Rectangle window) => new(window.Right - 16, window.Y + 3, 12, 12);

    public bool Captures(HudLayout layout, Rectangle minimapArea, Point pointer) =>
        Button(minimapArea).Contains(pointer) || Open && Window(layout, minimapArea).Contains(pointer);

    // A click on the button or the window; true when the list took it.
    public bool Click(HudLayout layout, Rectangle minimapArea, Point pointer)
    {
        if (Button(minimapArea).Contains(pointer))
        {
            Toggle();
            return true;
        }

        if (!Open || !Window(layout, minimapArea).Contains(pointer))
        {
            return false;
        }

        if (CloseMark(Window(layout, minimapArea)).Contains(pointer))
        {
            Close();
        }

        return true;
    }

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            _scroll = Math.Clamp(_scroll - Math.Sign(wheelDelta), 0, MaxScroll);
        }
    }

    private int MaxScroll => Math.Max(0, _list.Count - VisibleRows);

    public void DrawButton(HudPainter painter, Rectangle minimapArea, Point pointer)
    {
        var button = Button(minimapArea);
        painter.ColorBox(button, 0, 0, 0, Open || button.Contains(pointer) ? 200 : 140);
        painter.Stretch(HudMinimap.BossAliveIcon, button);
    }

    public void Draw(HudPainter painter, HudLayout layout, Rectangle minimapArea, double nowMs)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout, minimapArea);
        painter.ColorBox(window, 8, 6, 6, 215);
        painter.Text("Bosses", window.X + Padding, window.Y + 1, TitleColor);
        painter.TextCentered("x", CloseMark(window).X, CloseMark(window).Right, window.Y + 1, TitleColor);
        if (_list.Count == 0)
        {
            painter.Text("No bosses are scheduled.", window.X + Padding, window.Y + Header, FieldColor);
            return;
        }

        var elapsed = (long)((nowMs - _receivedMs) / 1000d);
        var end = Math.Min(_list.Count, _scroll + VisibleRows);
        for (var index = _scroll; index < end; index++)
        {
            var boss = _list[index];
            var y = window.Y + Header + (index - _scroll) * RowHeight;
            var time = Time(index, boss, elapsed);
            var timeWidth = painter.Measure(time);
            painter.Text(painter.Fit(boss.BossName, 110), window.X + Padding, y, NameColor);
            painter.Text(painter.Fit(boss.FieldName, Width - 110 - timeWidth - Padding * 3), window.X + Padding + 114, y, FieldColor);
            painter.Text(time, window.Right - Padding - timeWidth, y, TimeColor);
        }
    }

    private string Time(int index, BossListInfo boss, long elapsedSeconds)
    {
        var left = boss.Exact ? Math.Max(0L, boss.SecondsLeft - elapsedSeconds) : long.MaxValue;
        if (left != _shown[index] || _times[index] is null)
        {
            _shown[index] = left;
            _times[index] = !boss.Exact ? "tomorrow" : left == 0 ? "now" : HudBossTime.Clock(left);
        }

        return _times[index];
    }
}
