using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;

// The quest book: a framed window with a title plate. The left pane lists every quest in sections that collapse (Rank up,
// Level Quests, Beginner Quests, Side Quests, Daily, then one section per hunting field with the Quest Couriers' hunts,
// HudQuestBook.Couriers), each row with its status mark, its name and its level; "Show all" adds the locked and finished
// ones. The right pane shows the chosen quest: its title, its level and status, a Description (where to take it, each
// task line or objective with its kills, where to hand it in) and its Quest Rewards; HudQuestBook.Daily adds the daily
// giver's button. L or the book button under the pet button opens and closes it; Escape closes it.
public sealed partial class HudQuestBook
{
    public const string Folder = "Hud/QuestBook/";
    private const string ExpandedArt = Folder + "icon_collapse_white.png";
    private const string CollapsedArt = Folder + "icon_expand_white.png";
    private static readonly string[] StatusArt = [Folder + "icon_unavaliable.png", Folder + "icon_avaliable.png", Folder + "icon_progress.png", Folder + "icon_check.png"];
    // The marks over quest givers' heads: a quest to take, a repeatable one, one taken and one ready to hand in.
    private const string MarksFolder = "Hud/QuestMarks/";
    public const string AvailableMark = MarksFolder + "notify_quest.png";
    public const string RepeatableMark = MarksFolder + "notify_repeatable_quest.png";
    public const string TakenMark = MarksFolder + "Progress_Quest.png";
    public const string ReadyMark = MarksFolder + "Complete_Quest.png";
    public const string TargetMark = MarksFolder + "quest_target.png";
    public static readonly string[] Art = [ExpandedArt, CollapsedArt, .. StatusArt, AvailableMark, RepeatableMark, TakenMark, ReadyMark, TargetMark];

    private const int Width = 760;
    private const int Height = 540;
    private const int Margin = 14;
    private const int TopBand = 48;
    private const int ListWidth = 362;
    private const int RowHeight = 24;
    private const int ListTop = 64;
    private const int MaxLineChars = 120;
    private const int GiverLines = 2;
    private const int TaskLines = 7;
    private const int RewardLines = 5;
    private const int LineStep = 18;
    private const int IconSize = 16;

    private static readonly Color Frame = new(26, 24, 23);
    private static readonly Color FrameEdge = new(98, 88, 74);
    private static readonly Color FrameLight = new(150, 136, 112);
    private static readonly Color Pane = new(19, 17, 16);
    private static readonly Color PaneEdge = new(66, 60, 52);
    private static readonly Color Plate = new(44, 38, 34);
    private static readonly Color RowLit = new(58, 50, 40);
    private static readonly Color RowChosen = new(86, 68, 42);
    private static readonly Color ListText = new(222, 214, 196);
    private static readonly Color Muted = new(150, 142, 128);
    private static readonly Color SectionColor = new(240, 232, 214);
    private static readonly Color Heading = new(240, 228, 200);
    private static readonly Color Subtle = new(150, 168, 196);
    private static readonly Color Ink = new(222, 214, 196);
    private static readonly Color IconInk = new(176, 160, 128);
    private static readonly Color CloseRed = new(150, 28, 24);
    private static readonly Color[] StatusColors = [new(150, 140, 128), new(236, 196, 70), new(110, 170, 240), new(120, 210, 120)];
    private static readonly string[] StatusNames = ["Locked", "Quest is now available!", "In progress", "Done"];
    private static readonly QuestBookEntry[] Tempskron = QuestBook.For(tempskron: true);
    private static readonly QuestBookEntry[] Morion = QuestBook.For(tempskron: false);

    private enum RowKind
    {
        Header,
        Story,
        Daily,
        Hunt
    }

    // The mark before a Description line: words to read (talk, deliver) or a target to defeat.
    private enum LineMark
    {
        Talk,
        Target
    }

    // A row of the list: a section's header (Index is its section) or one quest of a kind (Index in its list).
    private readonly record struct BookRow(RowKind Kind, int Index, int Section);

    private readonly List<(LineMark Mark, string Text, bool Strong)> _description = [];
    private readonly List<string> _rewards = [];
    private readonly QuestBookStatus[] _statuses = new QuestBookStatus[Math.Max(Tempskron.Length, Morion.Length)];
    private readonly List<BookRow> _rows = [];
    private readonly List<(string Key, string Title)> _sections = [];
    private readonly HashSet<string> _collapsed = [];
    private QuestBookEntry[] _entries = Tempskron;
    private QuestBookFacts? _facts;
    private object _detailFor;
    private int _detailKills = -1;
    private string _levelLine = "";
    private int _scroll;
    private bool _showAll;
    private int _sectionStart;
    private int _sectionCount;
    private (RowKind Kind, int Index) _selected = (RowKind.Header, -1);

    public bool Open { get; private set; }

    public void Toggle()
    {
        Open = !Open;
        DailyNpc = 0;
        HuntsWanted |= Open;
    }

    public void Close()
    {
        Open = false;
        DailyNpc = 0;
    }

    public static Rectangle Button(Rectangle minimapArea)
    {
        var pet = HudPetWindow.Button(minimapArea);
        return new Rectangle(pet.X, pet.Bottom + 2, pet.Width, pet.Height);
    }

    public static Rectangle Window(HudLayout layout)
    {
        var width = Math.Min(Width, layout.Width);
        var height = Math.Min(Height, layout.Height - 40);
        return new Rectangle(Math.Max(0, (layout.Width - width) / 2), Math.Max(10, (layout.Height - height) / 2 - 20), width, height);
    }

    private static Rectangle CloseBox(Rectangle window) => new(window.Right - Margin - 22, window.Y + 12, 22, 22);

    private static Rectangle ListPane(Rectangle window) => new(window.X + Margin, window.Y + TopBand, ListWidth, window.Height - TopBand - Margin);

    private static Rectangle DetailPane(Rectangle window)
    {
        var list = ListPane(window);
        return new Rectangle(list.Right + 10, list.Y, window.Right - Margin - list.Right - 10, list.Height);
    }

    private static Rectangle ShowAllBox(Rectangle window) => new(ListPane(window).X + 16, ListPane(window).Y + 14, 13, 13);

    private static int VisibleRows(Rectangle window) => (ListPane(window).Height - ListTop - 8) / RowHeight;

    private static Rectangle Row(Rectangle window, int visibleIndex)
    {
        var list = ListPane(window);
        return new Rectangle(list.X + 8, list.Y + ListTop + visibleIndex * RowHeight, list.Width - 26, RowHeight - 2);
    }

    private int MaxScroll(Rectangle window) => Math.Max(0, _rows.Count - VisibleRows(window));

    public bool Captures(HudLayout layout, Rectangle minimapArea, Point pointer, bool minimapShown) =>
        minimapShown && Button(minimapArea).Contains(pointer) || Open && Window(layout).Contains(pointer);

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            _scroll = Math.Max(0, _scroll - Math.Sign(wheelDelta) * 2);
        }
    }

    // The facts change with the journal, the level, the tier or the class; the statuses are worked out again only then.
    public bool Refresh(in QuestBookFacts facts)
    {
        if (_facts == facts)
        {
            return false;
        }

        _facts = facts;
        var entries = facts.Tempskron ? Tempskron : Morion;
        if (!ReferenceEquals(entries, _entries))
        {
            _entries = entries;
            _selected = (RowKind.Header, -1);
        }

        for (var index = 0; index < _entries.Length; index++)
        {
            _statuses[index] = QuestBook.StatusOf(_entries[index], facts);
        }

        _detailFor = null;
        Rebuild();
        return true;
    }

    // The mark over each giver's head: the running quest's givers show it taken (ready once its hunt is complete), the
    // givers of available quests show one to take; a taken quest's mark wins over another's offer.
    public void FillMarks(Dictionary<string, string> marks, bool huntComplete)
    {
        marks.Clear();
        for (var index = 0; index < _entries.Length; index++)
        {
            var status = _statuses[index];
            if (status is QuestBookStatus.Available or QuestBookStatus.Running)
            {
                var mark = status == QuestBookStatus.Running ? huntComplete ? ReadyMark : TakenMark : _entries[index].Repeatable ? RepeatableMark : AvailableMark;
                foreach (var npc in _entries[index].GiverNpcs)
                {
                    if (status == QuestBookStatus.Running || !marks.ContainsKey(npc))
                    {
                        marks[npc] = mark;
                    }
                }
            }
        }
    }

    private static readonly string[] StorySections = ["Rank up", "Level Quests", "Beginner Quests", "Side Quests"];

    // The section a story quest is listed under.
    public static string StorySection(QuestBookEntry entry) =>
        entry.RankUpTier >= 0 ? StorySections[0] : entry.LevelBit != 0 ? StorySections[1] : entry.ElementaryIndex >= 0 ? StorySections[2] : StorySections[3];

    // A quest shows unless Show all is off and it is locked or finished.
    public static bool Shows(QuestBookStatus status, bool showAll) => showAll || status is QuestBookStatus.Available or QuestBookStatus.Running;

    // The list's rows: each section that has a quest to show, its header and, unless it is collapsed, its quests.
    private void Rebuild()
    {
        _rows.Clear();
        _sections.Clear();
        foreach (var title in StorySections)
        {
            var section = BeginSection("story:" + title, title);
            for (var index = 0; index < _entries.Length; index++)
            {
                if (StorySection(_entries[index]) == title && Shows(_statuses[index], _showAll))
                {
                    AddRow(new BookRow(RowKind.Story, index, section));
                }
            }

            EndSection();
        }

        var daily = BeginSection("daily", "Daily");
        for (var index = 0; index < _dailies.Count; index++)
        {
            if (Shows(DailyStatus(_dailies[index]), _showAll))
            {
                AddRow(new BookRow(RowKind.Daily, index, daily));
            }
        }

        EndSection();
        foreach (var field in _huntFields)
        {
            var section = BeginSection("field:" + field, HudCourierWindow.PlaceName(field));
            for (var index = 0; index < _hunts.Count; index++)
            {
                if (_hunts[index].FieldCode == field && Shows(HuntStatus(_hunts[index]), _showAll))
                {
                    AddRow(new BookRow(RowKind.Hunt, index, section));
                }
            }

            EndSection();
        }

        if (_selected.Index < 0 || !_rows.Exists(row => row.Kind == _selected.Kind && row.Index == _selected.Index))
        {
            _selected = _rows.Find(static row => row.Kind != RowKind.Header) is { Kind: not RowKind.Header } first ? (first.Kind, first.Index) : (RowKind.Header, -1);
            _detailFor = null;
        }
    }

    private int BeginSection(string key, string title)
    {
        _sections.Add((key, title));
        _sectionStart = _rows.Count;
        _sectionCount = 0;
        _rows.Add(new BookRow(RowKind.Header, _sections.Count - 1, _sections.Count - 1));
        return _sections.Count - 1;
    }

    private void AddRow(BookRow row)
    {
        _sectionCount++;
        if (!_collapsed.Contains(_sections[row.Section].Key))
        {
            _rows.Add(row);
        }
    }

    // A section without a quest to show leaves no header.
    private void EndSection()
    {
        if (_sectionCount == 0)
        {
            _rows.RemoveAt(_sectionStart);
        }
    }

    // Opens the book on a section, its first quest chosen (the daily giver opens it on Daily).
    private void ShowSection(string key)
    {
        Open = true;
        _collapsed.Remove(key);
        Rebuild();
        var header = _rows.FindIndex(row => row.Kind == RowKind.Header && _sections[row.Section].Key == key);
        if (header >= 0)
        {
            _scroll = header;
            if (header + 1 < _rows.Count && _rows[header + 1].Kind != RowKind.Header)
            {
                _selected = (_rows[header + 1].Kind, _rows[header + 1].Index);
                _detailFor = null;
            }
        }
    }

    public bool Click(HudLayout layout, Rectangle minimapArea, Point pointer, bool minimapShown)
    {
        if (minimapShown && Button(minimapArea).Contains(pointer))
        {
            Toggle();
            return true;
        }

        var window = Window(layout);
        if (!Open || !window.Contains(pointer))
        {
            return false;
        }

        if (CloseBox(window).Contains(pointer))
        {
            Close();
            return true;
        }

        var showAll = ShowAllBox(window);
        if (new Rectangle(showAll.X - 2, showAll.Y - 3, 90, showAll.Height + 6).Contains(pointer))
        {
            _showAll = !_showAll;
            Rebuild();
            return true;
        }

        _scroll = Math.Clamp(_scroll, 0, MaxScroll(window));
        for (var row = 0; row < VisibleRows(window) && _scroll + row < _rows.Count; row++)
        {
            if (!Row(window, row).Contains(pointer))
            {
                continue;
            }

            var picked = _rows[_scroll + row];
            if (picked.Kind == RowKind.Header)
            {
                var key = _sections[picked.Section].Key;
                if (!_collapsed.Remove(key))
                {
                    _collapsed.Add(key);
                }

                Rebuild();
            }
            else
            {
                _selected = (picked.Kind, picked.Index);
                _detailFor = null;
            }

            return true;
        }

        if (_selected.Kind == RowKind.Daily)
        {
            ClickDaily(DetailPane(window), pointer);
        }

        return true;
    }

    public static void DrawButton(HudPainter painter, Rectangle minimapArea, Point pointer, bool open)
    {
        var button = Button(minimapArea);
        painter.ColorBox(button, 0, 0, 0, open || button.Contains(pointer) ? 200 : 140);
        painter.TextCentered("Q", button.X, button.Right, button.Y - 1, new Color(255, 220, 140));
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        DrawFrame(painter, window, pointer);
        var detail = DetailPane(window);
        DrawPane(painter, ListPane(window));
        DrawPane(painter, detail);
        DrawList(painter, window, pointer);
        switch (_selected.Kind)
        {
            case RowKind.Story when _selected.Index < _entries.Length:
                DrawStory(painter, detail, _entries[_selected.Index], _statuses[_selected.Index]);
                break;
            case RowKind.Daily when _selected.Index < _dailies.Count:
                DrawDaily(painter, detail, pointer);
                break;
            case RowKind.Hunt when _selected.Index < _hunts.Count:
                DrawHunt(painter, detail);
                break;
            default:
                painter.TextCentered("Choose a quest.", detail.X, detail.Right, detail.Y + 20, Muted);
                break;
        }
    }

    // The window: a dark body in a two-tone edge, the title plate over its top edge and the red close box.
    private static void DrawFrame(HudPainter painter, Rectangle window, Point pointer)
    {
        painter.Fill(window, FrameEdge);
        painter.Fill(new Rectangle(window.X + 2, window.Y + 2, window.Width - 4, window.Height - 4), Frame);
        painter.Fill(new Rectangle(window.X + 6, window.Y + 6, window.Width - 12, 1), FrameLight);
        painter.Fill(new Rectangle(window.X + 6, window.Bottom - 7, window.Width - 12, 1), PaneEdge);
        var plate = new Rectangle(window.Center.X - 110, window.Y - 8, 220, 44);
        painter.Fill(plate, FrameLight);
        painter.Fill(new Rectangle(plate.X + 2, plate.Y + 2, plate.Width - 4, plate.Height - 4), Plate);
        painter.Fill(new Rectangle(plate.X + 6, plate.Y + 6, plate.Width - 12, plate.Height - 12), Frame);
        painter.TextCentered("Quest Book", plate.X, plate.Right, plate.Y + 14, SectionColor);
        var close = CloseBox(window);
        painter.Fill(close, FrameLight);
        painter.Fill(new Rectangle(close.X + 2, close.Y + 2, close.Width - 4, close.Height - 4), close.Contains(pointer) ? new Color(196, 44, 36) : CloseRed);
        painter.TextCentered("X", close.X, close.Right, close.Y + 4, Color.White);
    }

    private static void DrawPane(HudPainter painter, Rectangle pane)
    {
        painter.Fill(pane, PaneEdge);
        painter.Fill(new Rectangle(pane.X + 1, pane.Y + 1, pane.Width - 2, pane.Height - 2), Pane);
    }

    private void DrawList(HudPainter painter, Rectangle window, Point pointer)
    {
        var list = ListPane(window);
        var box = ShowAllBox(window);
        painter.Fill(box, FrameLight);
        painter.Fill(new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2), Pane);
        if (_showAll)
        {
            painter.Fill(new Rectangle(box.X + 3, box.Y + 3, box.Width - 6, box.Height - 6), StatusColors[1]);
        }

        painter.Text("Show all", box.Right + 8, box.Y - 2, SectionColor);
        painter.Text("Location", list.X + 16, list.Y + 38, SectionColor);
        painter.Text("Level", list.Right - 30 - painter.Measure("Level"), list.Y + 38, SectionColor);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll(window));
        var visible = VisibleRows(window);
        for (var row = 0; row < visible && _scroll + row < _rows.Count; row++)
        {
            DrawRow(painter, Row(window, row), _rows[_scroll + row], pointer);
        }

        if (_rows.Count == 0)
        {
            painter.TextCentered("No quest to show.", list.X, list.Right, list.Y + ListTop + 6, Muted);
        }

        if (MaxScroll(window) > 0)
        {
            var track = new Rectangle(list.Right - 12, list.Y + ListTop, 3, visible * RowHeight - 2);
            painter.Fill(track, PaneEdge);
            var thumbHeight = Math.Max(18, track.Height * visible / _rows.Count);
            var thumbY = track.Y + (track.Height - thumbHeight) * _scroll / MaxScroll(window);
            painter.Fill(new Rectangle(track.X - 1, thumbY, track.Width + 2, thumbHeight), FrameLight);
        }
    }

    private void DrawRow(HudPainter painter, Rectangle bounds, BookRow row, Point pointer)
    {
        if (row.Kind == RowKind.Header)
        {
            var (key, title) = _sections[row.Section];
            var top = bounds.Y + (_rows.Count > 0 && _rows[0] == row ? 0 : 4);
            painter.Text(painter.Fit(title, bounds.Width - 30), bounds.X + 4, top, SectionColor);
            var collapsed = _collapsed.Contains(key);
            var art = collapsed ? CollapsedArt : ExpandedArt;
            if (painter.Texture(art) is not null)
            {
                painter.Sprite(art, bounds.Right - 18, top + 2);
            }
            else
            {
                var sign = new Rectangle(bounds.Right - 16, top + 3, 11, 11);
                painter.Fill(sign, Muted);
                painter.Fill(new Rectangle(sign.X + 1, sign.Y + 1, 9, 9), Pane);
                painter.Fill(new Rectangle(sign.X + 3, sign.Center.Y, 5, 1), SectionColor);
                if (collapsed)
                {
                    painter.Fill(new Rectangle(sign.Center.X, sign.Y + 3, 1, 5), SectionColor);
                }
            }

            return;
        }

        var chosen = row.Kind == _selected.Kind && row.Index == _selected.Index;
        if (chosen || bounds.Contains(pointer))
        {
            painter.Fill(bounds, chosen ? RowChosen : RowLit);
        }

        var (name, level, status, repeatable) = RowFacts(row);
        painter.Sprite(StatusArt[(int)status], bounds.X + 14, bounds.Y + 3);
        var mark = status switch
        {
            QuestBookStatus.Running => StatusColors[2],
            QuestBookStatus.Done => StatusColors[3],
            QuestBookStatus.Locked => StatusColors[0],
            _ => repeatable ? StatusColors[2] : StatusColors[1]
        };
        painter.Text(status == QuestBookStatus.Running ? "?" : "!", bounds.X + 34, bounds.Y + 2, mark);
        var levelText = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var levelWidth = painter.Measure(levelText);
        var text = status == QuestBookStatus.Locked ? Muted : ListText;
        painter.Text(painter.Fit(name, bounds.Width - 46 - levelWidth - 10), bounds.X + 44, bounds.Y + 2, text);
        painter.Text(levelText, bounds.Right - levelWidth - 4, bounds.Y + 2, text);
    }

    private (string Name, int Level, QuestBookStatus Status, bool Repeatable) RowFacts(BookRow row) => row.Kind switch
    {
        RowKind.Story => (_entries[row.Index].Title, _entries[row.Index].Level, _statuses[row.Index], _entries[row.Index].Repeatable),
        RowKind.Daily => (_dailies[row.Index].Name, _dailies[row.Index].MinLevel, DailyStatus(_dailies[row.Index]), true),
        _ => (_hunts[row.Index].Name, _hunts[row.Index].MinLevel, HuntStatus(_hunts[row.Index]), (CourierCategory)_hunts[row.Index].Category != CourierCategory.OneTime)
    };

    // The chosen quest's page: its title and level line, the Description with a mark before each line, then the rewards.
    // Answers the y under it.
    private int DrawPage(HudPainter painter, Rectangle pane, string title, QuestBookStatus status)
    {
        var x = pane.X + 22;
        painter.Text(painter.Fit(title, pane.Width - 40), x, pane.Y + 16, Heading);
        var statusText = StatusNames[(int)status];
        painter.Text(painter.Fit(_levelLine + " / " + statusText, pane.Width - 40), x, pane.Y + 40, Subtle);
        var y = pane.Y + 78;
        painter.Text("Description", x, y, Heading);
        y += LineStep + 10;
        foreach (var (mark, text, strong) in _description)
        {
            DrawMark(painter, mark, x, y);
            painter.Text(text, x + IconSize + 10, y, strong ? Heading : Subtle);
            y += LineStep + (strong ? 2 : 0);
        }

        y += 16;
        painter.Text("Quest Rewards", x, y, Heading);
        y += LineStep + 8;
        foreach (var reward in _rewards)
        {
            painter.Text(reward, x + 4, y, Ink);
            y += LineStep + 2;
        }

        return y;
    }

    // A small drawn mark: a speech box for words, a ring with a cross for a target.
    private static void DrawMark(HudPainter painter, LineMark mark, int x, int y)
    {
        if (mark == LineMark.Talk)
        {
            painter.Fill(new Rectangle(x, y + 1, IconSize - 2, IconSize - 6), IconInk);
            painter.Fill(new Rectangle(x + 2, y + 3, IconSize - 6, 1), Pane);
            painter.Fill(new Rectangle(x + 2, y + 6, IconSize - 9, 1), Pane);
            painter.Fill(new Rectangle(x + 2, y + IconSize - 5, 3, 3), IconInk);
            return;
        }

        var center = new Point(x + IconSize / 2 - 1, y + IconSize / 2 - 1);
        painter.Fill(new Rectangle(center.X - 5, center.Y - 5, 11, 11), IconInk);
        painter.Fill(new Rectangle(center.X - 4, center.Y - 4, 9, 9), Pane);
        painter.Fill(new Rectangle(center.X - 7, center.Y, 15, 1), IconInk);
        painter.Fill(new Rectangle(center.X, center.Y - 7, 1, 15), IconInk);
    }

    // Lines wrapped to the page, each with the mark of the first; the first in the strong colour when asked.
    private void AddLines(HudPainter painter, int width, LineMark mark, string text, int maxLines, bool strong = false)
    {
        foreach (var line in HudChatText.Wrap(text, width, maxLines, MaxLineChars, painter.Measure))
        {
            _description.Add((mark, line, strong));
        }
    }

    private void DrawStory(HudPainter painter, Rectangle pane, QuestBookEntry entry, QuestBookStatus status)
    {
        if (!ReferenceEquals(entry, _detailFor))
        {
            _detailFor = entry;
            var width = pane.Width - 70;
            _levelLine = entry.Repeatable ? "Repeatable" : $"Level {entry.Level}";
            _description.Clear();
            AddLines(painter, width, LineMark.Talk, "Talk to " + entry.Giver, GiverLines, strong: true);
            AddLines(painter, width, LineMark.Target, entry.Task, TaskLines);
            _rewards.Clear();
            foreach (var reward in entry.Rewards)
            {
                _rewards.AddRange(HudChatText.Wrap(reward, width, RewardLines, MaxLineChars, painter.Measure));
            }
        }

        DrawPage(painter, pane, entry.Title, status);
    }
}
