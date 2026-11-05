using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;

// The quest book, drawn on the open book of Hud/QuestBook: the quests of the player's side (QuestBook) on the left page,
// "In Progress" showing the accepted (running) one and "Quest List" all of them, each marked done, running,
// available or locked from the journal the server sends; the clicked quest on the right page, its task beside the green
// mark, its status beside the blue one and its rewards beside the check. L or the book button under the pet button opens
// and closes it; Escape closes it. Without the book art it draws on a plain dark box.
public sealed class HudQuestBook
{
    public const string Folder = "Hud/QuestBook/";
    public const string Background = Folder + "background.png";
    private const string CloseArt = Folder + "closemenu.png";
    private const string CloseHoverArt = Folder + "closemenu_d.png";
    private const string ScrollArt = Folder + "scroll.png";
    private const string ScrollerArt = Folder + "scroller_icon.png";
    private static readonly string[] StatusArt = [Folder + "icon_unavaliable.png", Folder + "icon_avaliable.png", Folder + "icon_progress.png", Folder + "icon_check.png"];
    // The marks over quest givers' heads: a quest to take, a repeatable one, one taken and one ready to hand in.
    private const string MarksFolder = "Hud/QuestMarks/";
    public const string AvailableMark = MarksFolder + "notify_quest.png";
    public const string RepeatableMark = MarksFolder + "notify_repeatable_quest.png";
    public const string TakenMark = MarksFolder + "Progress_Quest.png";
    public const string ReadyMark = MarksFolder + "Complete_Quest.png";
    public static readonly string[] Art =
        [Background, CloseArt, CloseHoverArt, ScrollArt, ScrollerArt, .. StatusArt, AvailableMark, RepeatableMark, TakenMark, ReadyMark];

    // The book's art is 620x460; every place below is in its pixels.
    private const int Width = 620;
    private const int Height = 460;
    private static readonly Rectangle ProgressTab = new(50, 111, 120, 27);
    private static readonly Rectangle ListTab = new(178, 111, 120, 27);
    private static readonly Rectangle ListArea = new(52, 142, 232, 274);
    private static readonly Rectangle Track = new(288, 144, 4, 270);
    private static readonly Rectangle CloseMark = new(585, 10, 24, 24);
    private const int RowHeight = 18;
    private const int VisibleRows = 15;
    private const int PageLeft = 334;
    private const int PageRight = 574;
    private const int SectionText = 378;
    private const int LineStep = 14;
    private static readonly int[] SectionTops = [151, 218, 280];
    private static readonly int[] SectionLines = [3, 3, 9];
    private const int MaxLineChars = 120;
    private const int GiverLines = 2;

    private static readonly Color ListText = new(235, 225, 200);
    private static readonly Color Ink = new(60, 40, 20);
    private static readonly Color Heading = new(110, 30, 20);
    private static readonly Color[] StatusColors = [new(110, 100, 90), new(150, 110, 10), new(20, 90, 150), new(30, 110, 30)];
    private static readonly string[] StatusNames = ["Locked", "Available", "Running", "Done"];
    private static readonly QuestBookEntry[] Tempskron = QuestBook.For(tempskron: true);
    private static readonly QuestBookEntry[] Morion = QuestBook.For(tempskron: false);

    private readonly List<string> _task = [];
    private readonly List<string> _giver = [];
    private readonly List<string> _rewards = [];
    private readonly QuestBookStatus[] _statuses = new QuestBookStatus[Math.Max(Tempskron.Length, Morion.Length)];
    private readonly int[] _shown = new int[Math.Max(Tempskron.Length, Morion.Length)];
    private QuestBookEntry[] _entries = Tempskron;
    private QuestBookFacts? _facts;
    private QuestBookEntry _detailFor;
    private string _levelLine = "";
    private int _shownCount;
    private bool _inProgress = true;
    private int _scroll;
    private int _selected = -1;

    public bool Open { get; private set; }

    public void Toggle() => Open = !Open;

    public void Close() => Open = false;

    public static Rectangle Button(Rectangle minimapArea)
    {
        var pet = HudPetWindow.Button(minimapArea);
        return new Rectangle(pet.X, pet.Bottom + 2, pet.Width, pet.Height);
    }

    public static Rectangle Window(HudLayout layout) =>
        new(Math.Max(0, (layout.Width - Width) / 2), Math.Max(0, (layout.Height - Height) / 2 - 40), Width, Height);

    private static Rectangle At(Rectangle window, Rectangle place) => new(window.X + place.X, window.Y + place.Y, place.Width, place.Height);

    private static Rectangle Row(Rectangle window, int visibleIndex) =>
        new(window.X + ListArea.X, window.Y + ListArea.Y + visibleIndex * RowHeight, ListArea.Width, RowHeight - 1);

    private int MaxScroll => Math.Max(0, _shownCount - VisibleRows);

    public bool Captures(HudLayout layout, Rectangle minimapArea, Point pointer, bool minimapShown) =>
        minimapShown && Button(minimapArea).Contains(pointer) || Open && Window(layout).Contains(pointer);

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            _scroll = Math.Clamp(_scroll - Math.Sign(wheelDelta), 0, MaxScroll);
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
            _selected = -1;
        }

        for (var index = 0; index < _entries.Length; index++)
        {
            _statuses[index] = QuestBook.StatusOf(_entries[index], facts);
        }

        Filter();
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

    // The rows of the open tab: In Progress keeps the accepted quest.
    private void Filter()
    {
        _shownCount = 0;
        for (var index = 0; index < _entries.Length; index++)
        {
            if (!_inProgress || _statuses[index] == QuestBookStatus.Running)
            {
                _shown[_shownCount++] = index;
            }
        }

        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        if (_selected < 0 && _shownCount > 0)
        {
            _selected = _shown[0];
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

        if (At(window, CloseMark).Contains(pointer))
        {
            Close();
            return true;
        }

        if (At(window, ProgressTab).Contains(pointer) || At(window, ListTab).Contains(pointer))
        {
            _inProgress = At(window, ProgressTab).Contains(pointer);
            _scroll = 0;
            Filter();
            return true;
        }

        for (var row = 0; row < VisibleRows && _scroll + row < _shownCount; row++)
        {
            if (Row(window, row).Contains(pointer))
            {
                _selected = _shown[_scroll + row];
            }
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
        if (painter.Texture(Background) is null)
        {
            painter.ColorBox(window, 30, 24, 18, 230);
        }

        painter.Sprite(Background, window.X, window.Y);
        var close = At(window, CloseMark);
        painter.Sprite(close.Contains(pointer) ? CloseHoverArt : CloseArt, close.X, close.Y);
        painter.ColorBox(At(window, _inProgress ? ProgressTab : ListTab), 255, 240, 200, 40);

        for (var row = 0; row < VisibleRows && _scroll + row < _shownCount; row++)
        {
            var index = _shown[_scroll + row];
            var bounds = Row(window, row);
            if (index == _selected || bounds.Contains(pointer))
            {
                painter.ColorBox(bounds, 120, 90, 50, index == _selected ? 110 : 60);
            }

            var status = (int)_statuses[index];
            painter.Sprite(StatusArt[status], bounds.X + 3, bounds.Y + 1);
            painter.Text(painter.Fit(_entries[index].Title, bounds.Width - 22), bounds.X + 18, bounds.Y, ListText);
        }

        if (_shownCount == 0)
        {
            painter.TextCentered(_inProgress ? "No quest accepted." : "No quest here.", window.X + ListArea.X, window.X + ListArea.Right, window.Y + ListArea.Y + 4, ListText);
        }

        if (MaxScroll > 0)
        {
            var track = At(window, Track);
            painter.Stretch(ScrollArt, track);
            var thumbY = track.Y + (track.Height - 18) * _scroll / MaxScroll;
            painter.Sprite(ScrollerArt, track.X + track.Width / 2 - 9, thumbY);
        }

        if (_selected >= 0 && _selected < _entries.Length)
        {
            DrawPage(painter, window, _entries[_selected], _statuses[_selected]);
        }
    }

    private void DrawPage(HudPainter painter, Rectangle window, QuestBookEntry entry, QuestBookStatus status)
    {
        if (!ReferenceEquals(entry, _detailFor))
        {
            _detailFor = entry;
            Describe(painter, entry);
        }

        var left = window.X + PageLeft;
        var right = window.X + PageRight;
        painter.TextCentered(painter.Fit(entry.Title, PageRight - PageLeft), left, right, window.Y + 74, Heading);
        painter.TextCentered(_levelLine, left, right, window.Y + 90, Ink);
        for (var line = 0; line < _giver.Count; line++)
        {
            painter.TextCentered(_giver[line], left, right, window.Y + 106 + line * LineStep, Ink);
        }
        Section(painter, window, 0, "Task", Heading, _task);
        Section(painter, window, 1, StatusNames[(int)status], StatusColors[(int)status], null);
        Section(painter, window, 2, "Rewards", Heading, _rewards);
    }

    private static void Section(HudPainter painter, Rectangle window, int section, string label, Color labelColor, List<string> lines)
    {
        var x = window.X + SectionText;
        var y = window.Y + SectionTops[section];
        painter.Text(label, x, y, labelColor, shadow: false);
        if (lines is null)
        {
            return;
        }

        for (var line = 0; line < lines.Count && line < SectionLines[section]; line++)
        {
            painter.Text(lines[line], window.X + PageLeft + 6, y + 20 + line * LineStep, Ink, shadow: false);
        }
    }

    // The level line, the wrapped task and the rewards, made again only when another quest is shown.
    private void Describe(HudPainter painter, QuestBookEntry entry)
    {
        var width = PageRight - PageLeft - 12;
        _levelLine = entry.Repeatable ? "Repeatable" : $"From level {entry.Level}";
        _giver.Clear();
        _giver.AddRange(HudChatText.Wrap("Get it from: " + entry.Giver, width, GiverLines, MaxLineChars, painter.Measure));
        _task.Clear();
        _task.AddRange(HudChatText.Wrap(entry.Task, width, SectionLines[0], MaxLineChars, painter.Measure));
        _rewards.Clear();
        foreach (var reward in entry.Rewards)
        {
            _rewards.AddRange(HudChatText.Wrap("- " + reward, width, SectionLines[2], MaxLineChars, painter.Measure));
        }
    }
}
