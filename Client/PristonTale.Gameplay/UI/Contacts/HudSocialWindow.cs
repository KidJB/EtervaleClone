using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Contacts;

public enum HudSocialTab
{
    Friends,
    Clan
}

// The Social window (O): the Friends tab lists the recent, friend or blocked names (the lists of HudFriendsPage), each with
// block and remove buttons, and the Clan tab the clan's members. The wheel scrolls; a double click whispers to a name.
// The first bottom button makes the picked recent player a friend.
public sealed class HudSocialWindow(HudFriendsPage friends)
{
    public const int Width = 270;
    public const int Height = 415;
    private const string Folder = "Hud/Social/";
    private const string WindowArt = Folder + "window.png";
    private const string TitleArt = Folder + "title.png";
    private const string OptionsArt = Folder + "options_bg.png";
    private const string SeparatorArt = Folder + "separator.png";
    private const string TabSeparatorArt = Folder + "tab_separator.png";
    private const string ScrollArt = Folder + "scroll.png";
    private const string ScrollerArt = Folder + "scroller.png";
    private const int ListX = 34;
    private const int ListY = 72;
    private const int RowHeight = 24;
    private const int VisibleRows = 11;
    private const int ListHeight = 278;
    private const int ButtonY = ListY + 282;
    private static readonly string[] Buttons = ["friendsbtn", "clanbtn", "closebtn", "addfriendbtn", "recentbtn", "blockedbtn", "favoritebtn", "blockbtn", "removebtn"];
    private static readonly Color NameColor = new(235, 230, 220);
    private static readonly Color OfflineColor = new(150, 150, 150);
    private static readonly Color PickedColor = new(120, 255, 150);
    private static readonly HudFriendsView[] ListButtons = [HudFriendsView.Recent, HudFriendsView.Ignored, HudFriendsView.Friends];
    private static readonly string[] ButtonTips = ["Social.Add", "Social.Recent", "Social.Blocked", "Social.Friends"];

    public static readonly string[] Art =
    [
        WindowArt, TitleArt, OptionsArt, SeparatorArt, TabSeparatorArt, ScrollArt, ScrollerArt,
        .. Buttons.Select(static name => Art1(name, false)), .. Buttons.Select(static name => Art1(name, true))
    ];

    private readonly HudFriendsPage _friends = friends;
    private ClanInfo _clan = ClanInfo.None;
    private HudFriendsView _view = HudFriendsView.Recent;
    private int _first;
    private string _picked = "";
    private string _lastClicked = "";
    private double _lastClickSeconds;

    public bool Open { get; private set; }
    public HudSocialTab Tab { get; private set; }
    public HudFriendsView View => _view;
    public string Picked => _picked;
    public Action<string> Whispered { get; set; }

    private static string Art1(string name, bool active) => $"{Folder}{name}{(active ? "_" : "")}.png";

    public static Point Origin(HudLayout layout) => new(layout.Width - Width - 32, (layout.Height - Height) / 2);

    public static Rectangle Row(Point origin, int row) => new(origin.X + ListX, origin.Y + ListY + row * RowHeight, 195, RowHeight);

    private static Rectangle Block(Rectangle row) => new(row.X + 150, row.Y, 16, 16);

    private static Rectangle Remove(Rectangle row) => new(row.X + 168, row.Y - 2, 20, 20);

    public static Rectangle FriendsTab(Point origin) => new(origin.X + 35, origin.Y + 30, 103, 29);

    public static Rectangle ClanTab(Point origin) => new(origin.X + 138, origin.Y + 30, 96, 29);

    public static Rectangle Close(Point origin) => new(origin.X + 212, origin.Y + 2, 30, 28);

    // 0 add a friend, then the recent, blocked and friend lists.
    public static Rectangle BottomButton(Point origin, int index) => new(origin.X + ListX + 30 + (index == 0 ? 0 : 37 * index), origin.Y + ButtonY, 32, 32);

    public void Toggle()
    {
        Open = !Open;
        if (Open)
        {
            Tab = HudSocialTab.Friends;
            Show(HudFriendsView.Recent);
        }
    }

    public void ShowFriends()
    {
        Open = true;
        Tab = HudSocialTab.Friends;
        Show(HudFriendsView.Friends);
    }

    public void Hide() => Open = false;

    public void Observe(ClanInfo clan) => _clan = clan;

    public bool Contains(HudLayout layout, Point pointer)
    {
        var origin = Origin(layout);
        return Open && new Rectangle(origin.X, origin.Y, Width, Height).Contains(pointer);
    }

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            _first = Math.Clamp(_first - Math.Sign(wheelDelta), 0, MaxFirst);
        }
    }

    public bool Click(HudLayout layout, Point pointer, double nowSeconds)
    {
        if (!Contains(layout, pointer))
        {
            return false;
        }

        var origin = Origin(layout);
        if (Close(origin).Contains(pointer))
        {
            Hide();
        }
        else if (FriendsTab(origin).Contains(pointer) || ClanTab(origin).Contains(pointer))
        {
            Tab = FriendsTab(origin).Contains(pointer) ? HudSocialTab.Friends : HudSocialTab.Clan;
            _first = 0;
            _picked = "";
        }
        else if (Tab != HudSocialTab.Friends || !ClickBottom(origin, pointer))
        {
            ClickRows(origin, pointer, nowSeconds);
        }

        return true;
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var origin = Origin(layout);
        painter.Sprite(WindowArt, origin.X, origin.Y);
        DrawButton(painter, "friendsbtn", FriendsTab(origin), Tab == HudSocialTab.Friends || FriendsTab(origin).Contains(pointer));
        DrawButton(painter, "clanbtn", ClanTab(origin), Tab == HudSocialTab.Clan || ClanTab(origin).Contains(pointer));
        painter.Sprite(TitleArt, origin.X + 26, origin.Y - 24);
        DrawButton(painter, "closebtn", Close(origin), Close(origin).Contains(pointer));
        DrawRows(painter, origin, pointer);
        if (Tab == HudSocialTab.Friends)
        {
            DrawBottom(painter, origin, pointer);
        }
    }

    private bool ClickBottom(Point origin, Point pointer)
    {
        if (BottomButton(origin, 0).Contains(pointer))
        {
            if (_picked.Length > 0 && _view == HudFriendsView.Recent)
            {
                _friends.Changed?.Invoke(ContactList.Friends, true, _picked);
                _friends.Forget(_picked);
                _picked = "";
            }

            return true;
        }

        for (var index = 0; index < ListButtons.Length; index++)
        {
            if (BottomButton(origin, index + 1).Contains(pointer))
            {
                Show(ListButtons[index]);
                return true;
            }
        }

        return false;
    }

    private void ClickRows(Point origin, Point pointer, double nowSeconds)
    {
        for (var row = 0; row < VisibleRows && _first + row < Count; row++)
        {
            var bounds = Row(origin, row);
            if (!bounds.Contains(pointer))
            {
                continue;
            }

            var name = NameAt(_first + row);
            if (Tab == HudSocialTab.Friends && Remove(bounds).Contains(pointer))
            {
                RemoveName(name);
            }
            else if (Tab == HudSocialTab.Friends && _view != HudFriendsView.Ignored && Block(bounds).Contains(pointer))
            {
                BlockName(name);
            }
            else
            {
                var whisper = name == _lastClicked && nowSeconds - _lastClickSeconds < HudPartyWindow.DoubleClickSeconds && OnlineAt(_first + row)
                    && (Tab == HudSocialTab.Clan || _view != HudFriendsView.Ignored);
                _picked = name;
                _lastClicked = whisper ? "" : name;
                _lastClickSeconds = nowSeconds;
                if (whisper)
                {
                    Whispered?.Invoke(name);
                }
            }

            return;
        }
    }

    private void RemoveName(string name)
    {
        if (_view == HudFriendsView.Recent)
        {
            _friends.Forget(name);
        }
        else
        {
            _friends.Changed?.Invoke(_view == HudFriendsView.Friends ? ContactList.Friends : ContactList.Ignored, false, name);
        }
    }

    private void BlockName(string name)
    {
        if (_view == HudFriendsView.Friends)
        {
            _friends.Changed?.Invoke(ContactList.Friends, false, name);
        }

        _friends.Forget(name);
        _friends.Changed?.Invoke(ContactList.Ignored, true, name);
    }

    private void Show(HudFriendsView view)
    {
        _view = view;
        _first = 0;
        _picked = "";
    }

    private void DrawRows(HudPainter painter, Point origin, Point pointer)
    {
        _first = Math.Clamp(_first, 0, MaxFirst);
        var end = Math.Min(Count, _first + VisibleRows);
        for (var index = _first; index < end; index++)
        {
            var row = Row(origin, index - _first);
            var name = NameAt(index);
            var color = name == _picked ? PickedColor : OnlineAt(index) ? NameColor : OfflineColor;
            painter.Text(painter.Fit(name, Tab == HudSocialTab.Clan ? 172 : 138), row.X + 10, row.Y + 1, color);
            painter.Sprite(SeparatorArt, row.X + 10, row.Y + 18);
            if (Tab == HudSocialTab.Friends)
            {
                if (_view != HudFriendsView.Ignored)
                {
                    DrawButton(painter, "blockbtn", Block(row), Block(row).Contains(pointer));
                }

                DrawButton(painter, "removebtn", Remove(row), Remove(row).Contains(pointer));
            }
        }

        if (Count > VisibleRows)
        {
            var x = origin.X + ListX + 196;
            var y = origin.Y + ListY;
            painter.Sprite(ScrollArt, x, y);
            painter.Sprite(ScrollerArt, x - 3, y + (ListHeight - 24) * _first / Math.Max(1, MaxFirst));
        }
    }

    private void DrawBottom(HudPainter painter, Point origin, Point pointer)
    {
        painter.Sprite(OptionsArt, origin.X + ListX + 28, origin.Y + ButtonY);
        var add = BottomButton(origin, 0);
        DrawButton(painter, "addfriendbtn", add, add.Contains(pointer));
        DrawListButton(painter, origin, pointer, 0, "recentbtn");
        DrawListButton(painter, origin, pointer, 1, "blockedbtn");
        DrawListButton(painter, origin, pointer, 2, "favoritebtn");
        for (var index = 0; index < ButtonTips.Length; index++)
        {
            var button = BottomButton(origin, index);
            if (button.Contains(pointer))
            {
                painter.TextCentered(ClientText.Get(ButtonTips[index]), origin.X, origin.X + Width, button.Y - 16, NameColor);
            }
        }
    }

    private void DrawListButton(HudPainter painter, Point origin, Point pointer, int index, string art)
    {
        var button = BottomButton(origin, index + 1);
        painter.Sprite(TabSeparatorArt, button.X - 3, button.Y + 2);
        DrawButton(painter, art, button, _view == ListButtons[index] || button.Contains(pointer));
    }

    private static void DrawButton(HudPainter painter, string name, Rectangle bounds, bool active) => painter.Sprite(Art1(name, active), bounds.X, bounds.Y);

    private int MaxFirst => Math.Max(0, Count - VisibleRows);

    private int Count => Tab == HudSocialTab.Clan ? _clan.Members.Count : _view switch
    {
        HudFriendsView.Recent => _friends.Recent.Count,
        HudFriendsView.Friends => _friends.FriendList.Count,
        _ => _friends.IgnoredList.Count
    };

    private string NameAt(int index) => Tab == HudSocialTab.Clan ? _clan.Members[index].Name : _view switch
    {
        HudFriendsView.Recent => _friends.Recent[index],
        HudFriendsView.Friends => _friends.FriendList[index].Name,
        _ => _friends.IgnoredList[index].Name
    };

    private bool OnlineAt(int index) => Tab == HudSocialTab.Clan ? _clan.Members[index].Online : _view != HudFriendsView.Friends || _friends.FriendList[index].Online;
}
