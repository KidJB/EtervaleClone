using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI.Widgets;

// The Active Buffs box, under the target portrait until moved: the personal timers, then the world buffs, counted from what
// the server last sent. The heading strip drags it and its arrow folds it; Offset, Collapsed and Revision let the HUD save both.
public sealed class HudWorldBuffs
{
    private const int Width = 250;
    private const int Padding = 8;
    private const int InnerWidth = Width - Padding * 2;
    private const int StripHeight = 20;
    private const int PersonalRow = 22;
    private const int DragThreshold = 3;
    private const string ExpandedArt = Quests.HudQuestBook.Folder + "icon_collapse_white.png";
    private const string CollapsedArt = Quests.HudQuestBook.Folder + "icon_expand_white.png";
    private const int Top = 216;
    private const int Step = 16;
    private const int Tile = 21;
    private const int TilesPerRow = 10;
    private const string Folder = "Hud/WorldBuffs/";
    public static readonly string[] Art =
    [
        Folder + "HPUp.png", Folder + "SPUp.png", Folder + "MPUp.png", Folder + "MPDown.png", Folder + "SPDown.png", Folder + "TopPvp.png",
        Folder + "IceUp.png", Folder + "SixthSense.png", Folder + "EXP.png", Folder + "EventDrop.png"
    ];
    private static readonly Color Section = new(150, 200, 120);
    private static readonly Color BoxColor = new(10, 8, 6, 205);
    private static readonly Color BorderColor = new(120, 92, 52);
    private static readonly Color StripColor = new(52, 38, 22, 235);
    private static readonly Color StripLitColor = new(70, 52, 30, 245);
    private static readonly Color RuleColor = new(90, 70, 44, 160);
    private static readonly Color Heading = new(240, 205, 120);
    private static readonly Color Name = new(235, 225, 205);
    private static readonly Color Time = new(120, 220, 120);

    private readonly List<(string Name, string Effect, string Icon, double EndMs, string Time)> _rows = [];
    private IReadOnlyList<(int Kind, int SecondsLeft)> _from;
    private long _second = -1;
    private int _bodyHeight;
    private bool _shown;
    private bool _pressed;
    private bool _dragging;
    private Point _pressedAt;
    private Point _pressedOffset;

    // Where the player moved the box from its place at the right of the screen, and whether it is folded.
    public Point Offset { get; set; }
    public bool Collapsed { get; set; }
    public int Revision { get; private set; }
    public bool Dragging => _dragging;

    public Rectangle Bounds(HudLayout layout)
    {
        var height = Collapsed ? StripHeight : StripHeight + Padding * 2 + _bodyHeight;
        var x = Math.Clamp(layout.Width - Width - 8 + Offset.X, 0, Math.Max(0, layout.Width - Width));
        var y = Math.Clamp(Top + Offset.Y, 0, Math.Max(0, layout.Height - StripHeight));
        return new Rectangle(x, y, Width, height);
    }

    private static Rectangle Strip(Rectangle bounds) => new(bounds.X, bounds.Y, bounds.Width, StripHeight);

    private static Rectangle Fold(Rectangle bounds) => new(bounds.Right - 19, bounds.Y + 2, 16, 16);

    public bool Captures(HudLayout layout, Point pointer) => _shown && (Bounds(layout).Contains(pointer) || _dragging);

    // A click on the arrow folds or unfolds the box; a press on the strip moved past DragThreshold carries it.
    public void Update(HudLayout layout, Point pointer, MouseState mouse, MouseState previous, bool allowed)
    {
        if (mouse.LeftButton == ButtonState.Released)
        {
            if (_pressed && !_dragging && previous.LeftButton == ButtonState.Pressed && Fold(Bounds(layout)).Contains(pointer))
            {
                Collapsed = !Collapsed;
                Revision++;
            }

            if (_dragging)
            {
                Revision++;
            }

            _pressed = false;
            _dragging = false;
            return;
        }

        if (previous.LeftButton == ButtonState.Released)
        {
            _pressed = allowed && _shown && Strip(Bounds(layout)).Contains(pointer);
            _pressedAt = pointer;
            _pressedOffset = Offset;
            return;
        }

        if (!_pressed)
        {
            return;
        }

        var moved = new Point(pointer.X - _pressedAt.X, pointer.Y - _pressedAt.Y);
        _dragging |= Math.Abs(moved.X) > DragThreshold || Math.Abs(moved.Y) > DragThreshold;
        if (_dragging)
        {
            Offset = new Point(_pressedOffset.X + moved.X, _pressedOffset.Y + moved.Y);
        }
    }

    public void Receive(IReadOnlyList<(int Kind, int SecondsLeft)> buffs, double nowMs)
    {
        if (ReferenceEquals(buffs, _from) || buffs is null)
        {
            return;
        }

        _from = buffs;
        _rows.Clear();
        _second = -1;
        foreach (var (kind, secondsLeft) in buffs)
        {
            if (WorldBuffs.Get(kind) is { } buff)
            {
                _rows.Add((buff.Name, buff.Effect, Art[kind - 1], nowMs + secondsLeft * 1000d, ""));
            }
        }
    }

    // The panel: the premium timers under Personal and the world buffs as a row of tiles under World; hovering a tile names it.
    public void Draw(HudPainter painter, HudLayout layout, double nowMs, Point pointer, HudPremiumTimers personal)
    {
        var second = (long)(nowMs / 1000d);
        if (second != _second)
        {
            _second = second;
            for (var index = _rows.Count - 1; index >= 0; index--)
            {
                var left = (int)Math.Ceiling((_rows[index].EndMs - nowMs) / 1000d);
                if (left <= 0)
                {
                    _rows.RemoveAt(index);
                }
                else
                {
                    _rows[index] = _rows[index] with { Time = string.Create(CultureInfo.InvariantCulture, $"{left / 3600:D2}:{left / 60 % 60:D2}:{left % 60:D2} (DOES NOT PAUSE)") };
                }
            }
        }

        _shown = _rows.Count > 0 || personal.Count > 0;
        if (!_shown)
        {
            return;
        }

        _bodyHeight = (personal.Count > 0 ? Step + 4 + personal.Count * PersonalRow + 6 : 0)
            + (_rows.Count > 0 ? Step + 4 + (_rows.Count + TilesPerRow - 1) / TilesPerRow * (Tile + 2) : 0);
        var bounds = Bounds(layout);
        painter.Fill(bounds, BoxColor);
        Border(painter, bounds);
        var strip = Strip(bounds);
        painter.Fill(new Rectangle(strip.X + 1, strip.Y + 1, strip.Width - 2, strip.Height - 1), strip.Contains(pointer) || _dragging ? StripLitColor : StripColor);
        painter.Fill(new Rectangle(strip.X + 1, strip.Bottom, strip.Width - 2, 1), BorderColor);
        painter.Text("Active Buffs", bounds.X + Padding, bounds.Y + 3, Heading);
        var fold = Fold(bounds);
        painter.Sprite(Collapsed ? CollapsedArt : ExpandedArt, fold.X, fold.Y);
        if (Collapsed)
        {
            return;
        }

        var x = bounds.X + Padding;
        var y = bounds.Y + StripHeight + Padding;
        if (personal.Count > 0)
        {
            painter.Text("Personal", x, y, Section);
            painter.Fill(new Rectangle(x, y + Step, InnerWidth, 1), RuleColor);
            y = personal.Draw(painter, x, y + Step + 4, InnerWidth, nowMs) + 6;
        }

        if (_rows.Count == 0)
        {
            return;
        }

        painter.Text("World", x, y, Section);
        painter.Fill(new Rectangle(x, y + Step, InnerWidth, 1), RuleColor);
        y += Step + 4;
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            var tile = new Rectangle(x + index % TilesPerRow * (Tile + 2), y + index / TilesPerRow * (Tile + 2), Tile, Tile);
            painter.Stretch(row.Icon, tile);
            if (tile.Contains(pointer))
            {
                DrawTip(painter, bounds, row);
            }
        }
    }

    // The hovered tile's name, effect and time, in a box under the panel.
    private static void DrawTip(HudPainter painter, Rectangle bounds, (string Name, string Effect, string Icon, double EndMs, string Time) row)
    {
        var tip = new Rectangle(bounds.X, bounds.Bottom + 2, Width, Step * 3 + Padding);
        painter.Fill(tip, BoxColor);
        Border(painter, tip);
        painter.Text(row.Name, tip.X + Padding, tip.Y + 4, Heading);
        painter.Text(row.Effect, tip.X + Padding, tip.Y + 4 + Step, Name);
        painter.Text(row.Time, tip.X + Padding, tip.Y + 4 + Step * 2, Time);
    }

    private static void Border(HudPainter painter, Rectangle bounds)
    {
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), BorderColor);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), BorderColor);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), BorderColor);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), BorderColor);
    }
}
