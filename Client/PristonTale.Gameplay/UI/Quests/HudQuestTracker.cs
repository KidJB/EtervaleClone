using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;

// The active quest tracker: a bordered dark box headed "Active Quests" and the number tracked, then one block per quest:
// the running quest (its quest book title, task and hunt count), then each accepted daily quest (its name, its hunt and
// kills, or that it is ready to claim). A block shows its title with a QUEST or DAILY tag, its task wrapped to the box,
// a progress row (what to hunt on the left, the count on the right) and a bar that turns gold once ready; a faint line
// parts the blocks. The heading strip drags the box (kept on screen) and its arrow folds it to the strip; Offset,
// Collapsed and Revision let the HUD save both. It shows nothing while no quest runs; every line is made again only when
// the server sends a new journal or daily state.
public sealed class HudQuestTracker
{
    private const int Width = 250;
    private const int StripHeight = 20;
    private const int Padding = 8;
    private const int LineStep = 15;
    private const int TaskLines = 3;
    private const int MaxLineChars = 120;
    private const int BarHeight = 4;
    private const int BlockGap = 7;
    private const int DragThreshold = 3;
    private const string ExpandedArt = HudQuestBook.Folder + "icon_collapse_white.png";
    private const string CollapsedArt = HudQuestBook.Folder + "icon_expand_white.png";
    public static readonly string[] Art = [ExpandedArt, CollapsedArt];
    private static readonly Point Home = new(-Width - 10, 210);
    private static readonly Color BoxColor = new(10, 8, 6, 205);
    private static readonly Color BorderColor = new(120, 92, 52);
    private static readonly Color StripColor = new(52, 38, 22, 235);
    private static readonly Color StripLitColor = new(70, 52, 30, 245);
    private static readonly Color HeadingColor = new(240, 205, 120);
    private static readonly Color TitleColor = new(255, 240, 210);
    private static readonly Color TaskColor = new(190, 186, 176);
    private static readonly Color LabelColor = new(200, 200, 200);
    private static readonly Color CountColor = new(140, 230, 140);
    private static readonly Color ReadyColor = new(255, 214, 90);
    private static readonly Color BarBack = new(40, 34, 26);
    private static readonly Color QuestTagColor = new(120, 180, 255);
    private static readonly Color DailyTagColor = new(255, 170, 80);
    private static readonly Color RuleColor = new(90, 70, 44, 160);

    private readonly List<Block> _blocks = [];
    private Block _quest;
    private readonly List<Block> _dailies = [];
    private QuestView _shownFor;
    private DailyQuestsInfo _dailiesFor;
    private string _heading = "Active Quests";
    private int _bodyHeight;
    private Point _pressedAt;
    private Point _pressedOffset;
    private bool _pressed;
    private bool _dragging;

    // One tracked quest as drawn: its title and tag, its task lines, and its progress (Done of Need; Need 0 shows the label
    // alone).
    private sealed record Block(string Title, string Tag, Color TagColor, IReadOnlyList<string> Task, string Label, string Count, int Done, int Need,
        bool Ready)
    {
        public int Height => LineStep * (1 + Task.Count + (Label.Length > 0 ? 1 : 0)) + (Need > 0 ? BarHeight + 3 : 0);
    }

    public bool Visible { get; set; } = true;

    // Where the player moved the box from its place at the right of the screen, and whether it is folded.
    public Point Offset { get; set; }
    public bool Collapsed { get; set; }
    public int Revision { get; private set; }
    public bool Dragging => _dragging;

    private bool Shown => Visible && _blocks.Count > 0;

    public Rectangle Bounds(HudLayout layout)
    {
        var height = Collapsed ? StripHeight : StripHeight + Padding + _bodyHeight + Padding;
        var x = Math.Clamp(layout.Width + Home.X + Offset.X, 0, Math.Max(0, layout.Width - Width));
        var y = Math.Clamp(Home.Y + Offset.Y, 0, Math.Max(0, layout.Height - StripHeight));
        return new Rectangle(x, y, Width, height);
    }

    private static Rectangle Strip(Rectangle bounds) => new(bounds.X, bounds.Y, bounds.Width, StripHeight);

    private static Rectangle Fold(Rectangle bounds) => new(bounds.Right - 19, bounds.Y + 2, 16, 16);

    public bool Captures(HudLayout layout, Point pointer) => Shown && (Bounds(layout).Contains(pointer) || _dragging);

    // weaponHunt: the quest weapon's hunt line, which stands for the count during the second rank-up.
    public void Refresh(HudPainter painter, QuestView view, string weaponHunt)
    {
        if (ReferenceEquals(view, _shownFor))
        {
            return;
        }

        _shownFor = view;
        var entry = view is null || view.Code == QuestCode.None ? null : QuestBook.Of(view.Code);
        if (entry is null)
        {
            _quest = null;
        }
        else
        {
            var task = HudChatText.Wrap(entry.Task, TextWidth, TaskLines, MaxLineChars, painter.Measure);
            var need = view.KillsNeeded;
            var done = Math.Min(view.Kills, need);
            _quest = need > 0
                ? new Block(entry.Title, "QUEST", QuestTagColor, task, view.TargetName, Count(done, need), done, need, done >= need)
                : new Block(entry.Title, "QUEST", QuestTagColor, task, weaponHunt.Trim(), "", 0, 0, false);
        }

        Rebuild();
    }

    // The accepted daily quests of the day, each with its hunt and kills or that it is ready to claim at its giver.
    public void RefreshDailies(HudPainter painter, DailyQuestsInfo dailies)
    {
        if (ReferenceEquals(dailies, _dailiesFor))
        {
            return;
        }

        _dailiesFor = dailies;
        _dailies.Clear();
        foreach (var quest in DailyQuests.All)
        {
            if (!dailies.Accepted.TryGetValue(quest.Id, out var kills))
            {
                continue;
            }

            var done = Math.Min(kills, quest.Kills);
            var ready = done >= quest.Kills;
            var task = HudChatText.Wrap(ready ? "Hunt complete. Claim the reward at " + DailyQuests.GiverName + "." : "Defeat monsters of level "
                + quest.MonsterLevel.ToString(CultureInfo.InvariantCulture) + " or higher.", TextWidth, 2, MaxLineChars, painter.Measure);
            _dailies.Add(new Block(quest.Name, "DAILY", DailyTagColor, task, "Monsters slain", Count(done, quest.Kills), done, quest.Kills, ready));
        }

        Rebuild();
    }

    private const int TextWidth = Width - Padding * 2 - 6;

    private static string Count(int done, int need) => string.Create(CultureInfo.InvariantCulture, $"{done}/{need}");

    private void Rebuild()
    {
        _blocks.Clear();
        if (_quest is not null)
        {
            _blocks.Add(_quest);
        }

        _blocks.AddRange(_dailies);
        _bodyHeight = 0;
        foreach (var block in _blocks)
        {
            _bodyHeight += block.Height + BlockGap;
        }

        _bodyHeight = Math.Max(0, _bodyHeight - BlockGap);
        _heading = _blocks.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $"Active Quests ({_blocks.Count})") : "Active Quests";
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
        painter.Fill(bounds, BoxColor);
        Border(painter, bounds);
        var strip = Strip(bounds);
        painter.Fill(new Rectangle(strip.X + 1, strip.Y + 1, strip.Width - 2, strip.Height - 1), strip.Contains(pointer) || _dragging ? StripLitColor : StripColor);
        painter.Fill(new Rectangle(strip.X + 1, strip.Bottom, strip.Width - 2, 1), BorderColor);
        painter.Text(_heading, bounds.X + Padding, bounds.Y + 3, HeadingColor);
        var fold = Fold(bounds);
        painter.Sprite(Collapsed ? CollapsedArt : ExpandedArt, fold.X, fold.Y);
        if (Collapsed)
        {
            return;
        }

        var x = bounds.X + Padding;
        var right = bounds.Right - Padding;
        var y = bounds.Y + StripHeight + Padding;
        for (var index = 0; index < _blocks.Count; index++)
        {
            if (index > 0)
            {
                painter.Fill(new Rectangle(x, y - BlockGap / 2 - 1, right - x, 1), RuleColor);
            }

            y = DrawBlock(painter, _blocks[index], x, right, y) + BlockGap;
        }
    }

    private static int DrawBlock(HudPainter painter, Block block, int x, int right, int y)
    {
        var tagWidth = painter.Measure(block.Tag);
        painter.Text(block.Tag, right - tagWidth, y, block.TagColor);
        painter.Text(painter.Fit(block.Title, right - x - tagWidth - 8), x, y, TitleColor);
        y += LineStep;
        foreach (var line in block.Task)
        {
            painter.Text(line, x + 6, y, TaskColor);
            y += LineStep;
        }

        if (block.Label.Length > 0)
        {
            var countWidth = block.Count.Length > 0 ? painter.Measure(block.Count) : 0;
            painter.Text(painter.Fit(block.Label, right - x - 6 - countWidth - 8), x + 6, y, block.Ready ? ReadyColor : LabelColor);
            if (countWidth > 0)
            {
                painter.Text(block.Count, right - countWidth, y, block.Ready ? ReadyColor : CountColor);
            }

            y += LineStep;
        }

        if (block.Need > 0)
        {
            var bar = new Rectangle(x + 6, y + 1, right - x - 6, BarHeight);
            painter.Fill(bar, BarBack);
            painter.Fill(new Rectangle(bar.X, bar.Y, bar.Width * Math.Min(block.Done, block.Need) / block.Need, bar.Height), block.Ready ? ReadyColor : CountColor);
            y += BarHeight + 3;
        }

        return y;
    }

    private static void Border(HudPainter painter, Rectangle bounds)
    {
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), BorderColor);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), BorderColor);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), BorderColor);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), BorderColor);
    }
}
