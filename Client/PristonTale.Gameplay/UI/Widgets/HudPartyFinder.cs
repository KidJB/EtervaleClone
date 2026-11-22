using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Parties;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI.Widgets;

// The party finder (key F) in its own art (assets/Hud/PartyFinder/pt_*.png, drawn by tools/ui/build_party_finder.py: stone
// panels with a gold trim; the titles and labels are written over it), with two tabs. All Group lists the parties with room that
// are not closed: each leader and its members' class icons; clicking one opens its information beside the list (the
// field and its level, the classes it takes, the party's level and modes, its members), whose Join joins it through the
// finder (with its password when it asks one). My Group lists the player's own party and, for its leader, the settings
// panel: how players join (Open, Approval, Closed, Password), the lowest level, the password and the classes it takes.
// Both windows drag by their top strip. Opening the finder or a tab asks the server for the list; texts are made once when
// it arrives.
public sealed class HudPartyFinder
{
    private const string Folder = "Hud/PartyFinder/";
    private const string ListArt = Folder + "pt_list.png";
    private const string InfoArt = Folder + "pt_info.png";
    private const string SettingsArt = Folder + "pt_settings.png";
    private const string RowArt = Folder + "pt_row.png";
    private const string RowLit = Folder + "pt_row_lit.png";
    private const string LineArt = Folder + "pt_line.png";
    private const string TrackArt = Folder + "pt_track.png";
    private const string ThumbArt = Folder + "pt_thumb.png";
    private const string JoinArt = Folder + "pt_join_normal.png";
    private const string JoinLit = Folder + "pt_join_lit.png";
    private const string JoinPressed = Folder + "pt_join_pressed.png";
    private const string ButtonArt = Folder + "pt_button_normal.png";
    private const string ButtonLit = Folder + "pt_button_lit.png";
    private const string CloseArt = Folder + "pt_close.png";
    private const string CloseLit = Folder + "pt_close_lit.png";
    private const string TabArt = Folder + "pt_tab.png";
    private const string TabOn = Folder + "pt_tab_on.png";
    private const string CheckArt = Folder + "pt_check.png";
    private const string CheckOn = Folder + "pt_check_on.png";
    private const string ChoiceArt = Folder + "pt_choice.png";
    private const string ChoiceOn = Folder + "pt_choice_on.png";
    private const string FieldArt = Folder + "pt_field.png";
    private const string FieldOn = Folder + "pt_field_on.png";
    private const int ListWidth = 374;
    private const int ListHeight = 430;
    private const int InfoWidth = 398;
    private const int InfoHeight = 342;
    private const int Gap = 10;
    private const int StripHeight = 36;
    private const int TitleY = 13;
    private const int TabY = 40;
    private const int TabWidth = 90;
    private const int TabHeight = 22;
    private const int HeaderY = 68;
    private const int RowsX = 24;
    private const int RowsTop = 94;
    private const int RowHeight = 31;
    private const int VisibleRows = 10;
    private const int RowWidth = 316;
    private const int IconSize = 22;
    private const int NameX = 40;
    private const int MemberIconsX = 176;
    private const int MemberIconStep = 25;
    private const int TrackX = 344;
    private const int TrackY = 100;
    private const int TrackHeight = 300;
    private const int ThumbHeight = 24;
    private const int InfoTextX = 32;
    private const int InfoTextTop = 54;
    private const int MarkSize = 14;
    private const int MembersPerLine = 3;
    private const int WheelStep = 120;
    private const int FieldHeight = 20;


    private static readonly Dictionary<int, (string Name, int Level)> Fields = new WorldCatalog().AllFields
        .GroupBy(static field => field.FieldCode).ToDictionary(static group => group.Key, static group => (group.First().Name, group.First().LimitLevel));

    private static readonly PlayerClass[] Classes = Enum.GetValues<PlayerClass>();
    private static readonly PartyJoinMode[] Modes = [PartyJoinMode.Open, PartyJoinMode.Approval, PartyJoinMode.Closed, PartyJoinMode.Password];

    public static readonly string[] Art =
    [
        ListArt, InfoArt, SettingsArt, RowArt, RowLit, LineArt, TrackArt, ThumbArt, JoinArt, JoinLit, JoinPressed, ButtonArt, ButtonLit, CloseArt, CloseLit,
        TabArt, TabOn, CheckArt, CheckOn, ChoiceArt, ChoiceOn, FieldArt, FieldOn,
        .. Classes.Select(Icon)
    ];

    private readonly record struct Line(ulong LeaderId, string Leader, string LeaderIcon, string[] MemberIcons, string Field, string FieldLevel, string Level,
        string Mode, string[] Members, bool AsksPassword);

    private readonly List<Line> _lines = [];
    private readonly HudWindowDrag _listDrag = new();
    private readonly HudWindowDrag _infoDrag = new();
    private readonly TextInputField _joinPassword = new() { MaxLength = PartyFinderSettings.MaxPasswordLength, Masked = true, Accepts = static ch => !char.IsControl(ch) };
    private readonly TextInputField _minLevel = new() { MaxLength = 3, Accepts = char.IsAsciiDigit };
    private readonly TextInputField _password = new() { MaxLength = PartyFinderSettings.MaxPasswordLength, Accepts = static ch => !char.IsControl(ch) };
    private Line? _own;
    private bool _leads;
    private PartyJoinMode _mode = PartyJoinMode.Approval;
    private ushort _classes = PartyFinderSettings.AllClasses;
    private bool _myTab;
    private int _scroll;
    private int _chosen = -1;
    private bool _waiting;
    private bool _down;
    private string[] _classNames;

    // Asks the list; set by the screen.
    public Action Ask { get; set; }

    // Joins a listed party through the finder (its leader and the password typed); set by the screen.
    public Action<ulong, string> Join { get; set; }

    // Saves the leader's settings; set by the screen.
    public Action<PartyFinderSettingsPacket> Save { get; set; }

    public bool Open { get; private set; }

    public bool Dragging => _listDrag.Dragging || _infoDrag.Dragging;

    // A text box has the keyboard.
    public bool Typing => Open && (_joinPassword.Focused || _minLevel.Focused || _password.Focused);

    // A class's icon; the art numbers them by class, the Martial Artist's 12 and the Rogue's 13.
    private static string Icon(PlayerClass job) => Folder + "Class_icon/" + job switch
    {
        PlayerClass.MartialArtist => 12,
        PlayerClass.Rogue => 13,
        _ => (int)job
    } + ".png";

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

    private static Rectangle JoinButton(Rectangle box) => new(box.X + 142, box.Y + 296, 114, 34);

    private static Rectangle Tab(Rectangle box, bool mine) => new(box.X + RowsX + (mine ? TabWidth + 6 : 0), box.Y + TabY, TabWidth, TabHeight);

    private static Rectangle Row(Rectangle box, int shown) => new(box.X + RowsX, box.Y + RowsTop + shown * RowHeight, RowWidth, RowHeight);

    private static Rectangle JoinPasswordBox(Rectangle box) => new(box.X + InfoTextX + 90, box.Y + 254, 180, FieldHeight);

    private static Rectangle ModeBox(Rectangle box, int index) => new(box.X + 100 + index % 2 * 140, box.Y + 44 + index / 2 * 22, 130, 20);

    private static Rectangle MinLevelBox(Rectangle box) => new(box.X + 100, box.Y + 94, 60, FieldHeight);

    private static Rectangle PasswordBox(Rectangle box) => new(box.X + 100, box.Y + 120, 180, FieldHeight);

    private static Rectangle ClassBox(Rectangle box, int index) => new(box.X + 20 + index % 2 * 180, box.Y + 168 + index / 2 * 22, 170, 20);

    private static Rectangle SaveButton(Rectangle box) => new(box.X + 80, box.Bottom - 34, 100, 24);

    private static Rectangle CloseButton(Rectangle box) => new(box.X + 218, box.Bottom - 34, 100, 24);

    private bool InfoOpen => !_myTab && _chosen >= 0 && _chosen < _lines.Count;

    private bool SettingsOpen => _myTab && _leads;

    private string[] _texts;

    // The window's fixed words, read once: the list's, the information's and the settings' titles and the list's columns.
    private string[] Texts => _texts ??= [ClientText.Get("PartyFinder.Title"), ClientText.Get("PartyFinder.Information"), ClientText.Get("PartyFinder.Settings"),
        ClientText.Get("PartyFinder.Leader"), ClientText.Get("PartyFinder.MembersColumn")];

    private int ShownCount => _myTab ? _own is null ? 0 : 1 : _lines.Count;

    private int MaxScroll => Math.Max(0, ShownCount - VisibleRows);

    public void Toggle()
    {
        Open = !Open;
        if (Open)
        {
            Refresh();
        }
        else
        {
            Unfocus();
        }
    }

    public void Hide()
    {
        Open = false;
        _chosen = -1;
        Unfocus();
    }

    private void Unfocus()
    {
        _joinPassword.Focused = false;
        _minLevel.Focused = false;
        _password.Focused = false;
    }

    private void Refresh()
    {
        _waiting = true;
        _chosen = -1;
        _scroll = 0;
        _lines.Clear();
        _own = null;
        _leads = false;
        _classNames ??= [.. Classes.Select(static job => DataText.ClassTitle(job, 0))];
        Ask?.Invoke();
    }

    public void Type(ReadOnlySpan<char> typed, double elapsedSeconds)
    {
        _joinPassword.Update(typed, elapsedSeconds);
        _minLevel.Update(typed, elapsedSeconds);
        _password.Update(typed, elapsedSeconds);
    }

    public bool Contains(HudLayout layout, Point pointer) =>
        Open && (ListBox(layout).Contains(pointer) || (InfoOpen || SettingsOpen) && InfoBox(layout).Contains(pointer));

    // Moves a window dragged by its top strip.
    public void Drag(HudLayout layout, MouseState mouse, MouseState previous, Point pointer)
    {
        _down = mouse.LeftButton == ButtonState.Pressed;
        if (!Open)
        {
            return;
        }

        var list = ListHome(layout);
        _listDrag.Update(layout, list, new Rectangle(list.X, list.Y, list.Width, StripHeight), mouse, previous, pointer);
        if (InfoOpen || SettingsOpen)
        {
            var info = InfoHome(layout);
            _infoDrag.Update(layout, info, new Rectangle(info.X, info.Y, info.Width, StripHeight), mouse, previous, pointer);
        }
    }

    public void Click(HudLayout layout, Point pointer)
    {
        Unfocus();
        if (InfoOpen && ClickInfo(InfoBox(layout), pointer) || SettingsOpen && ClickSettings(InfoBox(layout), pointer))
        {
            return;
        }

        var list = ListBox(layout);
        if (ListClose(list).Contains(pointer))
        {
            Hide();
            return;
        }

        foreach (var mine in (ReadOnlySpan<bool>)[false, true])
        {
            if (mine != _myTab && Tab(list, mine).Contains(pointer))
            {
                _myTab = mine;
                Refresh();
                return;
            }
        }

        for (var shown = 0; !_myTab && shown < VisibleRows && _scroll + shown < _lines.Count; shown++)
        {
            if (Row(list, shown).Contains(pointer))
            {
                _chosen = _scroll + shown;
                _joinPassword.Clear();
                return;
            }
        }
    }

    private bool ClickInfo(Rectangle info, Point pointer)
    {
        if (InfoClose(info).Contains(pointer))
        {
            _chosen = -1;
            return true;
        }

        var line = _lines[_chosen];
        if (line.AsksPassword && JoinPasswordBox(info).Contains(pointer))
        {
            _joinPassword.Focused = true;
            return true;
        }

        if (JoinButton(info).Contains(pointer))
        {
            Join?.Invoke(line.LeaderId, line.AsksPassword ? _joinPassword.Text : "");
            return true;
        }

        return info.Contains(pointer);
    }

    private bool ClickSettings(Rectangle box, Point pointer)
    {
        for (var index = 0; index < Modes.Length; index++)
        {
            if (ModeBox(box, index).Contains(pointer))
            {
                _mode = Modes[index];
                return true;
            }
        }

        for (var index = 0; index < Classes.Length; index++)
        {
            if (ClassBox(box, index).Contains(pointer))
            {
                _classes ^= (ushort)PartyFinderSettings.Bit(Classes[index]);
                return true;
            }
        }

        if (MinLevelBox(box).Contains(pointer))
        {
            _minLevel.Focused = true;
            return true;
        }

        if (_mode == PartyJoinMode.Password && PasswordBox(box).Contains(pointer))
        {
            _password.Focused = true;
            return true;
        }

        if (SaveButton(box).Contains(pointer))
        {
            var level = int.TryParse(_minLevel.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            var settings = new PartyFinderSettings(_mode, level, _password.Text, _classes).Valid();
            Save?.Invoke(new PartyFinderSettingsPacket(settings.Mode, settings.MinLevel, settings.Password, settings.Classes));
            return true;
        }

        if (CloseButton(box).Contains(pointer))
        {
            _myTab = false;
            Refresh();
            return true;
        }

        return box.Contains(pointer);
    }

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            var steps = Math.Sign(wheelDelta) * Math.Max(1, Math.Abs(wheelDelta) / WheelStep);
            _scroll = Math.Clamp(_scroll - steps, 0, MaxScroll);
        }
    }

    public void Observe(PartyFinderPacket packet)
    {
        if (packet.First == 0)
        {
            _lines.Clear();
            _chosen = -1;
            _scroll = 0;
            ObserveOwn(packet.Own);
        }
        else if (packet.First != _lines.Count)
        {
            return;
        }

        _waiting = false;
        foreach (var listing in packet.Listings)
        {
            if (listing.Members.Count > 0)
            {
                _lines.Add(LineOf(listing.LeaderId, listing.FieldId, listing.AverageLevel, listing.Mode, listing.JoinMode, listing.Classes, listing.Members));
            }
        }
    }

    private void ObserveOwn(PartyFinderWireOwn own)
    {
        _leads = own is not null;
        _own = own is null || own.Members.Count == 0 ? null : LineOf(0, 0, 0, PartyMode.Normal, own.JoinMode, own.Classes, own.Members);
        if (own is null)
        {
            return;
        }

        _mode = own.JoinMode;
        _classes = own.Classes;
        _minLevel.Set(own.MinLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _password.Set(own.Password);
    }

    private static Line LineOf(ulong leaderId, int fieldId, int averageLevel, PartyMode mode, PartyJoinMode joinMode, ushort classes,
        IReadOnlyList<PartyFinderWireMember> members)
    {
        var others = new string[members.Count - 1];
        for (var index = 1; index < members.Count; index++)
        {
            others[index - 1] = IconOf(members[index].Class);
        }

        var names = new string[(members.Count + MembersPerLine - 1) / MembersPerLine];
        for (var line = 0; line < names.Length; line++)
        {
            names[line] = string.Join(", ", members.Skip(line * MembersPerLine).Take(MembersPerLine).Select(static member => member.Name));
        }

        var field = Fields.TryGetValue(fieldId, out var known) ? known : (ClientText.Get("PartyFinder.UnknownField"), 0);
        var settings = new PartyFinderSettings(joinMode, 0, "", classes);
        var taken = settings.TakesAll
            ? ClientText.Get("PartyFinder.AllClasses")
            : ClientText.Format("PartyFinder.SomeClasses", Classes.Count(settings.Takes));
        return new Line(leaderId, members[0].Name, IconOf(members[0].Class), others, field.Item1,
            ClientText.Format("PartyFinder.MapLevel", field.Item2) + " " + taken, ClientText.Format("PartyFinder.Level", averageLevel),
            ClientText.Get("PartyFinder.Mode." + mode) + "  " + ClientText.Get("PartyFinder.Join." + joinMode), names, joinMode == PartyJoinMode.Password);
    }

    private static string IconOf(byte job) => Enum.IsDefined((PlayerClass)job) ? Icon((PlayerClass)job) : "";

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
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
        painter.TextCentered(Texts[0], list.Left, list.Right, list.Y + TitleY, HudTextColors.BoxTitle);
        painter.Text(Texts[3], list.X + RowsX + NameX, list.Y + HeaderY, HudTextColors.Hint);
        painter.Text(Texts[4], list.X + RowsX + MemberIconsX, list.Y + HeaderY, HudTextColors.Hint);
        DrawTab(painter, list, false, "PartyFinder.TabAll", pointer);
        DrawTab(painter, list, true, "PartyFinder.TabMy", pointer);
        DrawRows(painter, list, pointer);

        if (InfoOpen)
        {
            DrawInfo(painter, InfoBox(layout), _lines[_chosen], pointer);
        }
        else if (SettingsOpen)
        {
            DrawSettings(painter, InfoBox(layout), pointer);
        }
    }

    private void DrawTab(HudPainter painter, Rectangle list, bool mine, string label, Point pointer)
    {
        var tab = Tab(list, mine);
        painter.Sprite(mine == _myTab ? TabOn : TabArt, tab.X, tab.Y);
        painter.TextCentered(ClientText.Get(label), tab.Left, tab.Right, tab.Y + 4,
            mine == _myTab || tab.Contains(pointer) ? HudTextColors.Highlight : Color.White);
    }

    private void DrawRows(HudPainter painter, Rectangle list, Point pointer)
    {
        if (ShownCount == 0)
        {
            var message = _waiting ? "PartyFinder.Loading" : _myTab ? "PartyFinder.NoParty" : "PartyFinder.Empty";
            painter.TextCentered(ClientText.Get(message), list.X + RowsX, list.X + RowsX + RowWidth, list.Y + RowsTop + 40, HudTextColors.Hint);
            return;
        }

        for (var shown = 0; shown < VisibleRows && _scroll + shown < ShownCount; shown++)
        {
            var index = _scroll + shown;
            var line = _myTab ? _own!.Value : _lines[index];
            var row = Row(list, shown);
            if (!_myTab && (index == _chosen || row.Contains(pointer)))
            {
                painter.Sprite(row.Contains(pointer) ? RowLit : RowArt, row.X, row.Y);
            }

            if (line.LeaderIcon.Length > 0)
            {
                painter.Stretch(line.LeaderIcon, new Rectangle(row.X + 10, row.Y + 4, IconSize, IconSize));
            }

            painter.Text(painter.Fit(line.Leader, MemberIconsX - NameX - 8), row.X + NameX, row.Y + 8, !_myTab && index == _chosen ? HudTextColors.Highlight : Color.White);
            for (var member = 0; member < line.MemberIcons.Length; member++)
            {
                if (line.MemberIcons[member].Length > 0)
                {
                    painter.Stretch(line.MemberIcons[member], new Rectangle(row.X + MemberIconsX + member * MemberIconStep, row.Y + 4, IconSize, IconSize));
                }
            }

            painter.Sprite(LineArt, row.X, row.Bottom - 1);
        }

        if (MaxScroll > 0)
        {
            painter.Sprite(TrackArt, list.X + TrackX, list.Y + TrackY);
            painter.Sprite(ThumbArt, list.X + TrackX, list.Y + TrackY + (TrackHeight - ThumbHeight) * _scroll / MaxScroll);
        }
    }

    private void DrawInfo(HudPainter painter, Rectangle box, Line line, Point pointer)
    {
        painter.Flush();
        painter.Sprite(InfoArt, box.X, box.Y);
        var close = InfoClose(box);
        painter.Sprite(close.Contains(pointer) ? CloseLit : CloseArt, close.X, close.Y);
        painter.TextCentered(Texts[1], box.Left, box.Right, box.Y + TitleY, HudTextColors.BoxTitle);
        var x = box.X + InfoTextX;
        var y = box.Y + InfoTextTop;
        painter.Text(line.Field, x, y, HudTextColors.Title);
        painter.Text(line.FieldLevel, x, y + 22, Color.White);
        painter.Text(line.Level, x, y + 44, Color.White);
        painter.Text(line.Mode, x, y + 66, HudTextColors.Highlight);
        painter.Sprite(LineArt, box.X + 36, y + 92);
        painter.Text(ClientText.Get("PartyFinder.Members"), x, y + 102, HudTextColors.Hint);
        for (var index = 0; index < line.Members.Length; index++)
        {
            painter.Text(line.Members[index], x, y + 124 + index * 20, Color.White);
        }

        if (line.AsksPassword)
        {
            var field = JoinPasswordBox(box);
            painter.Text(ClientText.Get("PartyFinder.Password"), x, field.Y + 3, Color.White);
            DrawField(painter, field, _joinPassword);
        }

        var join = JoinButton(box);
        painter.Sprite(join.Contains(pointer) ? _down ? JoinPressed : JoinLit : JoinArt, join.X, join.Y);
        painter.TextCentered(ClientText.Get("PartyFinder.JoinButton"), join.Left, join.Right, join.Y + 10, join.Contains(pointer) ? HudTextColors.Highlight : Color.White);
    }

    private void DrawSettings(HudPainter painter, Rectangle box, Point pointer)
    {
        painter.Flush();
        painter.Sprite(SettingsArt, box.X, box.Y);
        painter.TextCentered(Texts[2], box.Left, box.Right, box.Y + TitleY, HudTextColors.BoxTitle);
        painter.Text(ClientText.Get("PartyFinder.JoinMode"), box.X + 20, box.Y + 47, Color.White);
        for (var index = 0; index < Modes.Length; index++)
        {
            var option = ModeBox(box, index);
            DrawMark(painter, option, Modes[index] == _mode, ClientText.Get("PartyFinder.Join." + Modes[index]), pointer, round: true);
        }

        painter.Text(ClientText.Get("PartyFinder.MinLevel"), box.X + 20, box.Y + 97, Color.White);
        DrawField(painter, MinLevelBox(box), _minLevel);
        painter.Text(ClientText.Get("PartyFinder.Password"), box.X + 20, box.Y + 123, _mode == PartyJoinMode.Password ? Color.White : HudTextColors.Hint);
        if (_mode == PartyJoinMode.Password)
        {
            DrawField(painter, PasswordBox(box), _password);
        }

        painter.Text(ClientText.Get("PartyFinder.Classes"), box.X + 20, box.Y + 149, Color.White);
        for (var index = 0; index < Classes.Length; index++)
        {
            DrawMark(painter, ClassBox(box, index), (_classes & PartyFinderSettings.Bit(Classes[index])) != 0, _classNames[index], pointer);
        }

        DrawButton(painter, SaveButton(box), "PartyFinder.Save", pointer);
        DrawButton(painter, CloseButton(box), "PartyFinder.Close", pointer);
    }

    // A check box (or a round choice) and its label.
    private static void DrawMark(HudPainter painter, Rectangle option, bool on, string label, Point pointer, bool round = false)
    {
        var art = round ? on ? ChoiceOn : ChoiceArt : on ? CheckOn : CheckArt;
        painter.Sprite(art, option.X, option.Y + (option.Height - MarkSize) / 2);
        painter.Text(label, option.X + MarkSize + 6, option.Y + 3, on ? HudTextColors.Highlight : option.Contains(pointer) ? Color.White : HudTextColors.Hint);
    }

    private static void DrawField(HudPainter painter, Rectangle box, TextInputField field)
    {
        painter.Stretch(field.Focused ? FieldOn : FieldArt, box);
        painter.Text(field.CaretVisible ? field.DisplayText + "_" : field.DisplayText, box.X + 5, box.Y + 3, Color.White);
    }

    private static void DrawButton(HudPainter painter, Rectangle box, string label, Point pointer)
    {
        var lit = box.Contains(pointer);
        painter.Sprite(lit ? ButtonLit : ButtonArt, box.X, box.Y);
        painter.TextCentered(ClientText.Get(label), box.Left, box.Right, box.Y + 5, lit ? HudTextColors.Highlight : Color.White);
    }
}
