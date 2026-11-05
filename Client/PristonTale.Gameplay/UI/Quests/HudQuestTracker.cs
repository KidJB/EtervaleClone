using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;

// The active quest tracker: a dark box headed "Active Quests" over the running quest's title (from the quest book), its
// task and its hunt count. The heading strip drags it (kept on screen) and its arrow collapses it to the strip; Offset,
// Collapsed and Revision let the HUD save both. It shows nothing while no quest runs; the lines are made again only when
// the server sends a new journal view.
public sealed class HudQuestTracker
{
    private const int Width = 200;
    private const int StripHeight = 18;
    private const int Padding = 6;
    private const int LineStep = 15;
    private const int TaskLines = 3;
    private const int MaxLineChars = 120;
    private const int DragThreshold = 3;
    private const string ExpandedArt = HudQuestBook.Folder + "icon_collapse_white.png";
    private const string CollapsedArt = HudQuestBook.Folder + "icon_expand_white.png";
    public static readonly string[] Art = [ExpandedArt, CollapsedArt];
    private static readonly Point Home = new(-Width - 10, 210);
    private static readonly Color HeadingColor = new(235, 200, 110);
    private static readonly Color TitleColor = new(255, 240, 210);
    private static readonly Color TaskColor = new(210, 210, 210);
    private static readonly Color CountColor = new(140, 230, 140);

    private readonly List<string> _task = [];
    private QuestView _shownFor;
    private string _title = "";
    private string _count = "";
    private Point _pressedAt;
    private Point _pressedOffset;
    private bool _pressed;
    private bool _dragging;

    public bool Visible { get; set; } = true;

    // Where the player moved the box from its place at the right of the screen, and whether it is folded.
    public Point Offset { get; set; }
    public bool Collapsed { get; set; }
    public int Revision { get; private set; }
    public bool Dragging => _dragging;

    private bool Shown => Visible && _title.Length > 0;

    public Rectangle Bounds(HudLayout layout)
    {
        var height = Collapsed ? StripHeight : StripHeight + Padding + LineStep * (1 + _task.Count + (_count.Length > 0 ? 1 : 0)) + Padding;
        var x = Math.Clamp(layout.Width + Home.X + Offset.X, 0, Math.Max(0, layout.Width - Width));
        var y = Math.Clamp(Home.Y + Offset.Y, 0, Math.Max(0, layout.Height - StripHeight));
        return new Rectangle(x, y, Width, height);
    }

    private static Rectangle Strip(Rectangle bounds) => new(bounds.X, bounds.Y, bounds.Width, StripHeight);

    private static Rectangle Fold(Rectangle bounds) => new(bounds.Right - 18, bounds.Y + 1, 16, 16);

    public bool Captures(HudLayout layout, Point pointer) => Shown && (Bounds(layout).Contains(pointer) || _dragging);

    // weaponHunt: the quest weapon's hunt line, which stands for the count during the second rank-up.
    public void Refresh(HudPainter painter, QuestView view, string weaponHunt)
    {
        if (ReferenceEquals(view, _shownFor))
        {
            return;
        }

        _shownFor = view;
        _task.Clear();
        var entry = view is null || view.Code == QuestCode.None ? null : QuestBook.Of(view.Code);
        _title = entry?.Title ?? "";
        if (entry is null)
        {
            _count = "";
            return;
        }

        _task.AddRange(HudChatText.Wrap(entry.Task, Width - Padding * 2 - 6, TaskLines, MaxLineChars, painter.Measure));
        _count = view.KillsNeeded > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{view.TargetName}: {Math.Min(view.Kills, view.KillsNeeded)}/{view.KillsNeeded}")
            : weaponHunt.Trim();
    }

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
            _pressed = allowed && Shown && Strip(Bounds(layout)).Contains(pointer);
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
            var placed = Bounds(layout);
            Offset = new Point(placed.X - layout.Width - Home.X, placed.Y - Home.Y);
        }
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (!Shown)
        {
            return;
        }

        var bounds = Bounds(layout);
        painter.ColorBox(bounds, 10, 8, 6, 200);
        painter.ColorBox(Strip(bounds), 40, 30, 18, Strip(bounds).Contains(pointer) || _dragging ? 240 : 220);
        painter.Text("Active Quests", bounds.X + Padding, bounds.Y + 1, HeadingColor);
        var fold = Fold(bounds);
        painter.Sprite(Collapsed ? CollapsedArt : ExpandedArt, fold.X, fold.Y);
        if (Collapsed)
        {
            return;
        }

        var x = bounds.X + Padding;
        var y = bounds.Y + StripHeight + Padding;
        painter.Text(painter.Fit(_title, Width - Padding * 2), x, y, TitleColor);
        y += LineStep;
        foreach (var line in _task)
        {
            painter.Text(line, x + 6, y, TaskColor);
            y += LineStep;
        }

        if (_count.Length > 0)
        {
            painter.Text(painter.Fit(_count, Width - Padding * 2 - 6), x + 6, y, CountColor);
        }
    }
}
