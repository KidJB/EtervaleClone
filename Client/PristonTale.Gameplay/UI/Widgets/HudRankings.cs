using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.UI.Widgets;

// The rankings window (key H) in the rank-list art (assets/Hud/Rankings): tabs down the left for the level board of all
// classes, of each class and Bellatra's best clans of the day before; column titles in the top bar and the board below,
// with medals for the first three, class icons, a scroll bar (and the wheel) and the player's own line highlighted.
// Opening it or a tab asks the server for that board; each line's texts are made once when its board arrives.
public sealed class HudRankings
{
    private const string Folder = "Hud/Rankings/";
    private const string WindowArt = Folder + "window.png";
    private const string CloseArt = Folder + "btnclose.png";
    private const string CloseLit = Folder + "btnclose_.png";
    private const string TrackArt = Folder + "scroll.png";
    private const string ThumbArt = Folder + "scroller.png";
    private const string PlainTab = Folder + "menu.png";
    private const int Width = 560;
    private const int Height = 450;
    private const int TabX = 23;
    private const int TabTop = 50;
    private const int TabStep = 26;
    private const int TabWidth = 159;
    private const int TabHeight = 29;
    private const int HeaderY = 56;
    private const int ListX = 194;
    private const int ListTop = 92;
    private const int RowHeight = 25;
    private const int VisibleRows = 13;
    private const int TrackX = 530;
    private const int TrackY = 112;
    private const int TrackHeight = 269;
    private const int ThumbWidth = 10;
    private const int ThumbHeight = 15;
    private const int MedalSize = 22;
    private const int IconSize = 22;
    private const int WheelStep = 120;

    // The list's columns from the box's left: place or medal, class icon, name (or clan), level, experience, clan (or points).
    private const int PlaceX = 198;
    private const int IconX = 226;
    private const int NameX = 252;
    private const int LevelX = 368;
    private const int ExpX = 400;
    private const int ClanX = 456;
    private const int PointsX = 420;
    private const int ListRight = 524;

    private static readonly Color OwnFill = new Color(90, 80, 20) * 0.6f;

    // A tab: its board and class (0 all), its art and lit art (empty: the plain tab with its label).
    private readonly record struct TabInfo(RankingBoard Board, PlayerClass Class, string Art, string Lit);

    private static readonly TabInfo[] Tabs =
    [
        new(RankingBoard.Level, 0, Folder + "topplayers.png", Folder + "topplayers_.png"),
        new(RankingBoard.Level, PlayerClass.Fighter, Folder + "topfighters.png", Folder + "topfighters_.png"),
        new(RankingBoard.Level, PlayerClass.Mechanician, Folder + "topmechanicians.png", Folder + "topmechanicians_.png"),
        new(RankingBoard.Level, PlayerClass.Archer, Folder + "toparchers.png", Folder + "toparchers_.png"),
        new(RankingBoard.Level, PlayerClass.Pikeman, Folder + "toppikemen.png", Folder + "toppikemen_.png"),
        new(RankingBoard.Level, PlayerClass.Atalanta, Folder + "topatalantas.png", Folder + "topatalantas_.png"),
        new(RankingBoard.Level, PlayerClass.Knight, Folder + "topknights.png", Folder + "topknights_.png"),
        new(RankingBoard.Level, PlayerClass.Magician, Folder + "topmagicians.png", Folder + "topmagicians_.png"),
        new(RankingBoard.Level, PlayerClass.Priestess, Folder + "toppriestesses.png", Folder + "toppriestesses_.png"),
        new(RankingBoard.Level, PlayerClass.Assassin, Folder + "topassassins.png", Folder + "topassassins_.png"),
        new(RankingBoard.Level, PlayerClass.Shaman, Folder + "topshamans.png", Folder + "topshamans_.png"),
        new(RankingBoard.Level, PlayerClass.MartialArtist, "", ""),
        new(RankingBoard.Bellatra, 0, Folder + "sodclans.png", Folder + "sodclans_.png")
    ];

    private static readonly Dictionary<PlayerClass, string> ClassIcons = new()
    {
        [PlayerClass.Fighter] = Folder + "class_fs.png",
        [PlayerClass.Mechanician] = Folder + "class_ms.png",
        [PlayerClass.Archer] = Folder + "class_as.png",
        [PlayerClass.Pikeman] = Folder + "class_ps.png",
        [PlayerClass.Atalanta] = Folder + "class_ata.png",
        [PlayerClass.Knight] = Folder + "class_ks.png",
        [PlayerClass.Magician] = Folder + "class_mg.png",
        [PlayerClass.Priestess] = Folder + "class_prs.png",
        [PlayerClass.Assassin] = Folder + "class_ass.png",
        [PlayerClass.Shaman] = Folder + "class_sh.png"
    };

    private static readonly string[] PlayerMedals = [Folder + "level_1.png", Folder + "level_2.png", Folder + "level_3.png"];
    private static readonly string[] ClanMedals = [Folder + "sod1.png", Folder + "sod2.png", Folder + "sod3.png"];

    public static readonly string[] Art =
    [
        WindowArt, CloseArt, CloseLit, TrackArt, ThumbArt, PlainTab,
        .. Tabs.Where(static tab => tab.Art.Length > 0).SelectMany(static tab => new[] { tab.Art, tab.Lit }),
        .. ClassIcons.Values, .. PlayerMedals, .. ClanMedals
    ];

    private readonly record struct Row(string Place, string Name, string Level, string Exp, string Clan, string Icon);

    private readonly List<Row> _rows = [];
    private int _tab;
    private int _scroll;
    private bool _waiting;
    private bool _unavailable;
    private int _ownRow = -1;
    private string _plainLabel;
    private string[] _levelHeader;
    private string[] _bellatraHeader;

    // Asks a board (a PlayerClass for one class's level board, 0 all); set by the screen.
    public Action<RankingBoard, byte> Ask { get; set; }

    // The player's character name, whose line is highlighted.
    public string Self { get; set; } = "";

    public bool Open { get; private set; }

    private TabInfo Current => Tabs[_tab];

    public static Rectangle Box(HudLayout layout) => new((layout.Width - Width) / 2, (layout.Height - Height) / 2, Width, Height);

    private static Rectangle TabBox(Rectangle box, int index) => new(box.X + TabX, box.Y + TabTop + index * TabStep, TabWidth, TabHeight);

    private static Rectangle CloseBox(Rectangle box) => new(box.X + 56, box.Y + 403, 93, 23);

    public void Toggle()
    {
        Open = !Open;
        if (Open)
        {
            Request();
        }
    }

    public void Hide() => Open = false;

    public bool Contains(HudLayout layout, Point pointer) => Open && Box(layout).Contains(pointer);

    public void Click(HudLayout layout, Point pointer)
    {
        var box = Box(layout);
        if (CloseBox(box).Contains(pointer))
        {
            Hide();
            return;
        }

        for (var index = 0; index < Tabs.Length; index++)
        {
            if (index != _tab && TabBox(box, index).Contains(pointer))
            {
                _tab = index;
                Request();
                return;
            }
        }
    }

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var steps = Math.Sign(wheelDelta) * Math.Max(1, Math.Abs(wheelDelta) / WheelStep);
        _scroll = Math.Clamp(_scroll - steps, 0, MaxScroll);
    }

    private int MaxScroll => Math.Max(0, _rows.Count - VisibleRows);

    public void Observe(RankingsPacket packet)
    {
        var current = Current;
        if (packet.Board != current.Board || packet.Board == RankingBoard.Level && packet.Class != (byte)current.Class)
        {
            return;
        }

        // A later part of the board continues the lines it already holds.
        if (packet.First == 0)
        {
            _rows.Clear();
            _ownRow = -1;
            _scroll = 0;
        }
        else if (packet.First != _rows.Count)
        {
            return;
        }

        _waiting = false;
        _unavailable = !packet.Available;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        for (var index = 0; index < packet.Entries.Count; index++)
        {
            var entry = packet.Entries[index];
            var place = (packet.First + index + 1).ToString(invariant);
            if (packet.Board == RankingBoard.Bellatra)
            {
                _rows.Add(new Row(place, entry.Name, "", "", entry.Value.ToString("N0", invariant), ""));
                continue;
            }

            var job = (PlayerClass)entry.Class;
            _rows.Add(new Row(place, entry.Name, entry.Level.ToString(invariant), (entry.ExpBasisPoints / 100f).ToString("0.00", invariant) + "%", entry.Clan,
                ClassIcons.GetValueOrDefault(job, "")));
            if (string.Equals(entry.Name, Self, StringComparison.OrdinalIgnoreCase))
            {
                _ownRow = packet.First + index;
            }
        }
    }

    private void Request()
    {
        _waiting = true;
        _unavailable = false;
        _rows.Clear();
        _scroll = 0;
        _plainLabel ??= DataText.ClassTitle(PlayerClass.MartialArtist, 0);
        _levelHeader ??= [ClientText.Get("Rankings.Place"), ClientText.Get("Rankings.Name"), ClientText.Get("Rankings.LevelColumn"), ClientText.Get("Rankings.Exp"),
            ClientText.Get("Rankings.Clan")];
        _bellatraHeader ??= [ClientText.Get("Rankings.Place"), ClientText.Get("Rankings.Clan"), ClientText.Get("Rankings.Points")];
        Ask?.Invoke(Current.Board, (byte)Current.Class);
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var box = Box(layout);
        painter.Flush();
        painter.Sprite(WindowArt, box.X, box.Y);
        for (var index = 0; index < Tabs.Length; index++)
        {
            var tab = Tabs[index];
            var bounds = TabBox(box, index);
            var lit = index == _tab || bounds.Contains(pointer);
            if (tab.Art.Length > 0)
            {
                painter.Sprite(lit ? tab.Lit : tab.Art, bounds.X, bounds.Y);
                continue;
            }

            painter.Sprite(PlainTab, bounds.X, bounds.Y);
            painter.TextCentered(_plainLabel, bounds.Left, bounds.Right, bounds.Y + 8, lit ? HudTextColors.Highlight : Color.White);
        }

        var close = CloseBox(box);
        painter.Sprite(close.Contains(pointer) ? CloseLit : CloseArt, close.X, close.Y);
        DrawHeader(painter, box);

        var message = _waiting ? "Rankings.Loading" : _unavailable ? "Rankings.Unavailable" : _rows.Count == 0 ? "Rankings.Empty" : null;
        if (message is not null)
        {
            painter.TextCentered(ClientText.Get(message), box.X + ListX, box.X + ListRight, box.Y + ListTop + 60, HudTextColors.Hint);
            return;
        }

        painter.Flush();
        for (var index = _scroll; index < Math.Min(_rows.Count, _scroll + VisibleRows); index++)
        {
            DrawRow(painter, box, box.Y + ListTop + (index - _scroll) * RowHeight, index);
        }

        if (MaxScroll > 0)
        {
            painter.Sprite(TrackArt, box.X + TrackX + (ThumbWidth - 4) / 2, box.Y + TrackY);
            var thumbY = box.Y + TrackY + (TrackHeight - ThumbHeight) * _scroll / MaxScroll;
            painter.Sprite(ThumbArt, box.X + TrackX, thumbY);
        }
    }

    private void DrawHeader(HudPainter painter, Rectangle box)
    {
        var y = box.Y + HeaderY;
        var color = HudTextColors.Hint;
        if (Current.Board == RankingBoard.Bellatra)
        {
            painter.Text(_bellatraHeader[0], box.X + PlaceX, y, color);
            painter.Text(_bellatraHeader[1], box.X + NameX, y, color);
            painter.Text(_bellatraHeader[2], box.X + PointsX, y, color);
            return;
        }

        painter.Text(_levelHeader[0], box.X + PlaceX, y, color);
        painter.Text(_levelHeader[1], box.X + NameX, y, color);
        painter.Text(_levelHeader[2], box.X + LevelX, y, color);
        painter.Text(_levelHeader[3], box.X + ExpX, y, color);
        painter.Text(_levelHeader[4], box.X + ClanX, y, color);
    }

    private void DrawRow(HudPainter painter, Rectangle box, int y, int index)
    {
        var row = _rows[index];
        if (index == _ownRow)
        {
            painter.Fill(new Rectangle(box.X + ListX, y - 3, ListRight - ListX, RowHeight - 2), OwnFill);
        }

        var medals = Current.Board == RankingBoard.Bellatra ? ClanMedals : PlayerMedals;
        if (index < medals.Length)
        {
            painter.Stretch(medals[index], new Rectangle(box.X + PlaceX, y - 5, MedalSize, MedalSize));
        }
        else
        {
            painter.Text(row.Place, box.X + PlaceX + 2, y, Color.White);
        }

        var color = index < medals.Length ? HudTextColors.Highlight : Color.White;
        if (Current.Board == RankingBoard.Bellatra)
        {
            painter.Text(painter.Fit(row.Name, PointsX - NameX - 8), box.X + NameX, y, color);
            painter.Text(row.Clan, box.X + PointsX, y, color);
            return;
        }

        if (row.Icon.Length > 0)
        {
            painter.Stretch(row.Icon, new Rectangle(box.X + IconX, y - 5, IconSize, IconSize));
        }

        painter.Text(painter.Fit(row.Name, LevelX - NameX - 6), box.X + NameX, y, color);
        painter.Text(row.Level, box.X + LevelX, y, color);
        painter.Text(row.Exp, box.X + ExpX, y, color);
        painter.Text(painter.Fit(row.Clan, ListRight - ClanX), box.X + ClanX, y, color);
    }
}
