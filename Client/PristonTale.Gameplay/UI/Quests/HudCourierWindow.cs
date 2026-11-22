using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI.Quests;

// A Quest Courier's window, in the party finder's art (assets/Hud/PartyFinder/pt_*.png): a list of its hunts in three tabs
// (Available: offered at the player's level, with those done for the period or waiting greyed; Active: accepted and still
// running; Completed: ready to turn in) and, for the chosen hunt, its details beside it (category, difficulty, level,
// place, each objective's kills, the reward and the progress) with Accept, Abandon or Turn In. The Active and Completed
// tabs also show hunts accepted from other Couriers, which are turned in where they were given. Both windows drag by their
// top strip; the board is asked when the window opens and after each answer.
public sealed class HudCourierWindow
{
    private const string Folder = "Hud/PartyFinder/";
    private const string ListArt = Folder + "pt_list.png";
    private const string InfoArt = Folder + "pt_info.png";
    private const string RowArt = Folder + "pt_row.png";
    private const string RowLit = Folder + "pt_row_lit.png";
    private const string LineArt = Folder + "pt_line.png";
    private const string TrackArt = Folder + "pt_track.png";
    private const string ThumbArt = Folder + "pt_thumb.png";
    private const string ButtonArt = Folder + "pt_join_normal.png";
    private const string ButtonLit = Folder + "pt_join_lit.png";
    private const string CloseArt = Folder + "pt_close.png";
    private const string CloseLit = Folder + "pt_close_lit.png";
    private const string TabArt = Folder + "pt_tab.png";
    private const string TabOn = Folder + "pt_tab_on.png";
    public static readonly string[] Art = [ListArt, InfoArt, RowArt, RowLit, LineArt, TrackArt, ThumbArt, ButtonArt, ButtonLit, CloseArt, CloseLit, TabArt, TabOn];

    private const int ListWidth = 374;
    private const int ListHeight = 430;
    private const int InfoWidth = 398;
    private const int InfoHeight = 342;
    private const int Gap = 10;
    private const int StripHeight = 36;
    private const int TitleY = 13;
    private const int TabY = 40;
    private const int TabWidth = 100;
    private const int TabHeight = 22;
    private const int RowsX = 24;
    private const int RowsTop = 94;
    private const int RowHeight = 31;
    private const int VisibleRows = 10;
    private const int RowWidth = 316;
    private const int TrackX = 344;
    private const int TrackY = 100;
    private const int TrackHeight = 300;
    private const int ThumbHeight = 24;
    private const int TextX = 32;
    private const int TextTop = 50;
    private const int LineStep = 18;
    private const int WheelStep = 120;
    private const int MessageMs = 4000;

    private static readonly Color[] DifficultyColors = [new(150, 220, 150), new(235, 225, 205), new(255, 180, 90), new(255, 110, 110)];
    private static readonly Color Muted = new(130, 120, 105);
    private static readonly Color ReadyColor = new(255, 214, 90);

    // A field's name as the Couriers and the Quest Book show it: its Teleport Core destination's, else the field's own.
    public static string PlaceName(int fieldCode) => Places.TryGetValue(fieldCode, out var place) ? place : "?";

    private static readonly Dictionary<int, string> Places = new WorldCatalog().AllFields.GroupBy(static field => field.FieldCode)
        .ToDictionary(static group => group.Key,
            static group => TeleportCoreDestinations.All.FirstOrDefault(destination => destination.FieldCode == group.Key)?.Name ?? group.First().Name);

    private enum Tab
    {
        Available,
        Active,
        Completed
    }

    private readonly HudWindowDrag _listDrag = new();
    private readonly HudWindowDrag _infoDrag = new();
    private readonly List<CourierWireQuest> _shown = [];
    private CourierBoardPacket _board;
    private CourierStatePacket _state = new([]);
    private ulong _npcId;
    private Tab _tab;
    private int _scroll;
    private int _chosen = -1;
    private bool _waiting;
    private Task<(byte Status, long Experience)> _answer;
    private string _message = "";
    private double _messageUntilMs;
    private string[] _details = [];
    private int _detailsFor = -1;

    public bool Open { get; private set; }

    public bool Dragging => _listDrag.Dragging || _infoDrag.Dragging;

    // Asks a Courier's board; set by the HUD.
    public Action<ulong> Ask { get; set; }

    // Accepts (1), abandons (2) or turns in (3) a hunt at a Courier; set by the HUD.
    public Func<ulong, int, byte, Task<(byte Status, long Experience)>> Request { get; set; }

    private static Rectangle ListHome(HudLayout layout) =>
        new((layout.Width - ListWidth - Gap - InfoWidth) / 2, (layout.Height - ListHeight) / 2, ListWidth, ListHeight);

    private static Rectangle InfoHome(HudLayout layout)
    {
        var list = ListHome(layout);
        return new Rectangle(list.Right + Gap, list.Y, InfoWidth, InfoHeight);
    }

    private Rectangle ListBox(HudLayout layout) => _listDrag.Moved(ListHome(layout));

    private Rectangle InfoBox(HudLayout layout) => _infoDrag.Moved(InfoHome(layout));

    private static Rectangle ListClose(Rectangle box) => new(box.X + 346, box.Y + 10, 18, 18);

    private static Rectangle InfoClose(Rectangle box) => new(box.X + 370, box.Y + 10, 18, 18);

    private static Rectangle TabBox(Rectangle box, Tab tab) => new(box.X + RowsX + (int)tab * (TabWidth + 8), box.Y + TabY, TabWidth, TabHeight);

    private static Rectangle Row(Rectangle box, int shown) => new(box.X + RowsX, box.Y + RowsTop + shown * RowHeight, RowWidth, RowHeight);

    // The details' buttons: the main one (Accept, Turn In) and Abandon beside it.
    private static Rectangle MainButton(Rectangle box) => new(box.X + 74, box.Y + 296, 114, 34);

    private static Rectangle AbandonButton(Rectangle box) => new(box.X + 210, box.Y + 296, 114, 34);

    private bool InfoOpen => _chosen >= 0 && _chosen < _shown.Count;

    private int MaxScroll => Math.Max(0, _shown.Count - VisibleRows);

    public void Show(ulong npcId)
    {
        Open = true;
        _npcId = npcId;
        _tab = Tab.Available;
        _board = null;
        Refresh();
    }

    public void Hide()
    {
        Open = false;
        _chosen = -1;
    }

    private void Refresh()
    {
        _waiting = true;
        Ask?.Invoke(_npcId);
    }

    public void ObserveBoard(CourierBoardPacket board)
    {
        if (!Open || ReferenceEquals(board, _board))
        {
            return;
        }

        _board = board;
        _waiting = false;
        if (board.NpcId == 0)
        {
            Hide();
            return;
        }

        Rebuild();
    }

    public void ObserveState(CourierStatePacket state)
    {
        if (ReferenceEquals(state, _state))
        {
            return;
        }

        _state = state;
        if (Open && _tab != Tab.Available)
        {
            Rebuild();
        }
    }

    // The tab's hunts: the board's for Available; the player's own (the board's copy when it has one) for the others.
    private void Rebuild()
    {
        var chosenId = InfoOpen ? _shown[_chosen].Id : 0;
        _shown.Clear();
        if (_tab == Tab.Available)
        {
            _shown.AddRange(_board?.Quests.Where(static quest => quest.Standing is not ((byte)CourierStanding.Active or (byte)CourierStanding.Ready)) ?? []);
        }
        else
        {
            var wanted = _tab == Tab.Active ? CourierStanding.Active : CourierStanding.Ready;
            foreach (var quest in _state.Quests.Where(quest => quest.Standing == (byte)wanted))
            {
                _shown.Add(_board?.Quests.FirstOrDefault(listed => listed.Id == quest.Id) is { } listed ? listed with { Standing = quest.Standing,
                    Objectives = quest.Objectives } : quest);
            }
        }

        _chosen = _shown.FindIndex(quest => quest.Id == chosenId);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        _detailsFor = -1;
    }

    public bool Contains(HudLayout layout, Point pointer) => Open && (ListBox(layout).Contains(pointer) || InfoOpen && InfoBox(layout).Contains(pointer));

    public void Drag(HudLayout layout, MouseState mouse, MouseState previous, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var list = ListHome(layout);
        _listDrag.Update(layout, list, new Rectangle(list.X, list.Y, list.Width, StripHeight), mouse, previous, pointer);
        if (InfoOpen)
        {
            var info = InfoHome(layout);
            _infoDrag.Update(layout, info, new Rectangle(info.X, info.Y, info.Width, StripHeight), mouse, previous, pointer);
        }
    }

    public void Scroll(int wheelDelta) => _scroll = Math.Clamp(_scroll - wheelDelta / WheelStep, 0, MaxScroll);

    public void Click(HudLayout layout, Point pointer, double nowMs)
    {
        var list = ListBox(layout);
        if (ListClose(list).Contains(pointer))
        {
            Hide();
            return;
        }

        foreach (var tab in Enum.GetValues<Tab>())
        {
            if (TabBox(list, tab).Contains(pointer))
            {
                _tab = tab;
                _scroll = 0;
                _chosen = -1;
                Rebuild();
                return;
            }
        }

        for (var shown = 0; shown < VisibleRows && _scroll + shown < _shown.Count; shown++)
        {
            if (Row(list, shown).Contains(pointer))
            {
                _chosen = _scroll + shown;
                _detailsFor = -1;
                return;
            }
        }

        if (!InfoOpen)
        {
            return;
        }

        var info = InfoBox(layout);
        if (InfoClose(info).Contains(pointer))
        {
            _chosen = -1;
            return;
        }

        var quest = _shown[_chosen];
        var standing = (CourierStanding)quest.Standing;
        if (MainButton(info).Contains(pointer) && standing is CourierStanding.Available or CourierStanding.Ready)
        {
            Send(quest.Id, standing == CourierStanding.Ready ? CourierRequestPacket.TurnIn : CourierRequestPacket.Accept, nowMs);
        }
        else if (AbandonButton(info).Contains(pointer) && standing is CourierStanding.Active or CourierStanding.Ready)
        {
            Send(quest.Id, CourierRequestPacket.Abandon, nowMs);
        }
    }

    private void Send(int questId, byte action, double nowMs)
    {
        if (_answer is not null || Request is null)
        {
            return;
        }

        _answer = Request(_npcId, questId, action);
        _messageUntilMs = nowMs + MessageMs;
    }

    // The answer to the last request: its message, then the board asked again.
    public void Update(double nowMs)
    {
        if (_answer is not { IsCompleted: true } answer)
        {
            return;
        }

        _answer = null;
        var (status, experience) = answer.IsCompletedSuccessfully ? answer.Result : ((byte)CourierStatus.Rejected, 0L);
        _message = status == (byte)CourierStatus.Ok && experience > 0
            ? ClientText.Format("Courier.TurnedIn", experience.ToString("N0", CultureInfo.CurrentCulture))
            : ClientText.Get("Courier.Status." + status.ToString(CultureInfo.InvariantCulture));
        _messageUntilMs = nowMs + MessageMs;
        if (Open)
        {
            Refresh();
        }
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer, double nowMs)
    {
        if (!Open)
        {
            return;
        }

        painter.Flush();
        var list = ListBox(layout);
        painter.Sprite(ListArt, list.X, list.Y);
        var close = ListClose(list);
        painter.Sprite(close.Contains(pointer) ? CloseLit : CloseArt, close.X, close.Y);
        painter.TextCentered(_board is { Name.Length: > 0 } ? _board.Name.ToUpperInvariant() : ClientText.Get("Courier.Title"), list.Left, list.Right, list.Y + TitleY,
            HudTextColors.BoxTitle);
        DrawTab(painter, list, Tab.Available, "Courier.TabAvailable", pointer);
        DrawTab(painter, list, Tab.Active, "Courier.TabActive", pointer);
        DrawTab(painter, list, Tab.Completed, "Courier.TabCompleted", pointer);
        DrawRows(painter, list, pointer);
        if (nowMs < _messageUntilMs && _message.Length > 0)
        {
            painter.TextCentered(_message, list.Left, list.Right, list.Bottom - 16, ReadyColor);
        }

        if (InfoOpen)
        {
            DrawInfo(painter, InfoBox(layout), _shown[_chosen], pointer);
        }
    }

    private void DrawTab(HudPainter painter, Rectangle list, Tab tab, string label, Point pointer)
    {
        var box = TabBox(list, tab);
        painter.Stretch(tab == _tab ? TabOn : TabArt, box);
        painter.TextCentered(ClientText.Get(label), box.Left, box.Right, box.Y + 4, tab == _tab || box.Contains(pointer) ? HudTextColors.Highlight : Color.White);
    }

    private void DrawRows(HudPainter painter, Rectangle list, Point pointer)
    {
        if (_shown.Count == 0)
        {
            var empty = _waiting ? "Courier.Loading" : "Courier.Empty";
            painter.TextCentered(ClientText.Get(empty), list.X + RowsX, list.X + RowsX + RowWidth, list.Y + RowsTop + 40, HudTextColors.Hint);
            return;
        }

        for (var shown = 0; shown < VisibleRows && _scroll + shown < _shown.Count; shown++)
        {
            var index = _scroll + shown;
            var quest = _shown[index];
            var row = Row(list, shown);
            if (index == _chosen || row.Contains(pointer))
            {
                painter.Sprite(row.Contains(pointer) ? RowLit : RowArt, row.X, row.Y);
            }

            var standing = (CourierStanding)quest.Standing;
            var greyed = standing is CourierStanding.Completed or CourierStanding.Cooldown or CourierStanding.Closed;
            var color = greyed ? Muted : index == _chosen ? HudTextColors.Highlight : DifficultyColors[Math.Min(quest.Difficulty, DifficultyColors.Length - 1)];
            painter.Text(painter.Fit(quest.Name, 200), row.X + 10, row.Y + 8, color);
            painter.Text(StandingTag(quest), row.X + 216, row.Y + 8, standing == CourierStanding.Ready ? ReadyColor : greyed ? Muted : HudTextColors.Hint);
            painter.Sprite(LineArt, row.X, row.Bottom - 1);
        }

        if (MaxScroll > 0)
        {
            painter.Sprite(TrackArt, list.X + TrackX, list.Y + TrackY);
            painter.Sprite(ThumbArt, list.X + TrackX, list.Y + TrackY + (TrackHeight - ThumbHeight) * _scroll / MaxScroll);
        }
    }

    // What the row says beside the name: the level range, or why the hunt cannot be taken now.
    private static string StandingTag(CourierWireQuest quest) => (CourierStanding)quest.Standing switch
    {
        CourierStanding.Completed => ClientText.Get("Courier.Standing.Completed"),
        CourierStanding.Cooldown => ClientText.Format("Courier.Standing.Cooldown", Math.Max(1, (quest.CooldownSeconds + 59) / 60)),
        CourierStanding.Closed => ClientText.Get("Courier.Standing.Closed"),
        CourierStanding.Ready => ClientText.Get("Courier.Standing.Ready"),
        _ => ClientText.Format("Courier.Levels", quest.MinLevel, quest.MaxLevel)
    };

    private void DrawInfo(HudPainter painter, Rectangle box, CourierWireQuest quest, Point pointer)
    {
        painter.Flush();
        painter.Sprite(InfoArt, box.X, box.Y);
        var close = InfoClose(box);
        painter.Sprite(close.Contains(pointer) ? CloseLit : CloseArt, close.X, close.Y);
        painter.TextCentered(painter.Fit(quest.Name, InfoWidth - 80), box.Left, box.Right, box.Y + TitleY, HudTextColors.BoxTitle);
        if (_detailsFor != _chosen)
        {
            _detailsFor = _chosen;
            _details = Details(painter, quest);
        }

        var y = box.Y + TextTop;
        foreach (var line in _details)
        {
            painter.Text(line, box.X + TextX, y, Color.White);
            y += LineStep;
        }

        var standing = (CourierStanding)quest.Standing;
        if (standing is CourierStanding.Available or CourierStanding.Ready)
        {
            DrawButton(painter, MainButton(box), standing == CourierStanding.Ready ? "Courier.TurnIn" : "Courier.Accept", pointer);
        }

        if (standing is CourierStanding.Active or CourierStanding.Ready)
        {
            DrawButton(painter, AbandonButton(box), "Courier.Abandon", pointer);
        }
    }

    private static void DrawButton(HudPainter painter, Rectangle button, string label, Point pointer)
    {
        painter.Sprite(button.Contains(pointer) ? ButtonLit : ButtonArt, button.X, button.Y);
        painter.TextCentered(ClientText.Get(label), button.Left, button.Right, button.Y + 10, button.Contains(pointer) ? HudTextColors.Highlight : Color.White);
    }

    // The chosen hunt's lines, made once when it is chosen: its description, category and difficulty, level and place, each
    // objective's kills, the reward and the progress.
    private static string[] Details(HudPainter painter, CourierWireQuest quest)
    {
        var lines = new List<string>();
        if (quest.Description.Length > 0)
        {
            lines.AddRange(HudChatText.Wrap(quest.Description, InfoWidth - TextX * 2, 2, 120, painter.Measure));
        }

        lines.Add(ClientText.Format("Courier.Kind", ClientText.Get("Courier.Category." + (CourierCategory)quest.Category),
            ClientText.Get("Courier.Difficulty." + (CourierDifficulty)quest.Difficulty)));
        lines.Add(ClientText.Format("Courier.Level", quest.MinLevel, quest.MaxLevel));
        lines.Add(ClientText.Format("Courier.Location", Places.TryGetValue(quest.FieldCode, out var place) ? place : "?"));
        lines.Add(ClientText.Get("Courier.Objectives"));
        var done = 0;
        var need = 0;
        foreach (var objective in quest.Objectives)
        {
            var kills = Math.Min(objective.Kills, objective.Count);
            done += kills;
            need += objective.Count;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {objective.Monster}: {kills} / {objective.Count}"));
        }

        lines.Add(ClientText.Format("Courier.Reward", quest.Experience.ToString("N0", CultureInfo.CurrentCulture)));
        if (quest.Gold > 0)
        {
            lines.Add(ClientText.Format("Courier.Gold", quest.Gold.ToString("N0", CultureInfo.CurrentCulture)));
        }

        if (quest.ItemCount > 0 && quest.ItemKey.Length > 0)
        {
            lines.Add(ClientText.Format("Courier.Item", quest.ItemKey, quest.ItemCount));
        }

        lines.Add(ClientText.Format("Courier.ProgressValue", need == 0 ? 0 : done * 100 / need));
        return [.. lines];
    }
}
