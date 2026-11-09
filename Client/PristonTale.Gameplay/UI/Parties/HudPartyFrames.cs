using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Parties;

// The party's frames down the left of the screen, every member with its class picture (dark while dead), level, name and
// life and mana bars, the leader crowned; a member past PartyLimits.NearRange fades. The arrow folds them; a right click on a
// frame opens its menu (the leader delegates or kicks a member, or disbands; a member leaves), and each choice asks first.
public sealed class HudPartyFrames
{
    private const string Folder = "Hud/Party/";
    private const string Background = Folder + "background.png";
    private const string LifeBar = Folder + "lifebar.png";
    private const string ManaBar = Folder + "manabar.png";
    private const string Leader = Folder + "leader.png";
    private const string Expand = Folder + "expand.png";
    private const string ExpandHover = Folder + "expand_.png";
    private const string RaidCrown = Folder + "raidleader.png";
    private const string MiniBack = Folder + "minilifebar.png";
    private const string MiniFill = Folder + "minilifebar_.png";
    private const int RaidColumnX = 180;
    private const int RaidRowStep = 20;
    private const int MiniWidth = 89;
    private const int Width = 173;
    private const int RowHeight = 49;
    private const int RowStep = 50;
    private const int ListTop = 40;
    private const int BarWidth = 119;
    private const int ManaWidth = 123;
    private const int BarArtHeight = 6;
    private const int PortraitSize = 34;
    private const int MenuWidth = 94;
    private const int MenuRow = 24;
    private const int ColumnHeight = 345;
    private const int DragThreshold = 3;
    private static readonly Color Far = Color.White * 0.6f;
    private static readonly Color LevelColor = new(255, 238, 100);
    private static readonly Color DeadColor = new(255, 45, 45);
    private static readonly Color FarNameColor = new(185, 175, 155);
    private static readonly PlayerClass[] Classes = Enum.GetValues<PlayerClass>();
    private static readonly string[] ModeArt = [Folder + "normalmode.png", Folder + "expmode.png", Folder + "huntmode.png"];
    private static readonly string[] ModeTips = ["Party.Mode.Normal", "Party.Mode.Exp", "Party.Mode.Hunt"];
    private static readonly string[] Members = Enumerable.Range(1, PartyLimits.MaxMembers).Select(static index => $"{Folder}member{index}.png").ToArray();

    public static readonly string[] Art =
    [
        Background, LifeBar, ManaBar, Leader, Expand, ExpandHover, RaidCrown, MiniBack, MiniFill, .. ModeArt, .. Members,
        .. Classes.Select(static job => Portrait(job, false)).Distinct(), .. Classes.Select(static job => Portrait(job, true)).Distinct()
    ];

    private readonly List<MenuItem> _menu = [];
    private PartyInfo _shown;
    private string[] _levels = [];
    private Point _menuAt;
    private MenuItem? _confirming;
    private bool _pressed;
    private bool _dragging;
    private Point _pressedAt;
    private Point _pressedOffset;

    public bool Folded { get; private set; }
    public bool Confirming => _confirming is not null;
    public Action<PartyCommand, ulong> Commanded { get; set; }

    // Where the player dragged the column from the left edge; Revision counts moves, for saving.
    public Point Offset { get; set; }
    public int Revision { get; private set; }
    public bool Dragging => _dragging;

    private Rectangle Toggle(HudLayout layout) => new(14 + Offset.X, Top(layout) + Offset.Y, 18, 14);

    private Rectangle FrameRow(HudLayout layout, int index)
    {
        var row = Row(layout, index);
        return new Rectangle(row.X + Offset.X, row.Y + Offset.Y, row.Width, row.Height);
    }

    private static int Top(HudLayout layout) => (layout.Height - ColumnHeight) / 2;

    public static Rectangle Row(HudLayout layout, int index) => new(0, Top(layout) + ListTop + index * RowStep, Width, RowHeight);

    public static string Portrait(PlayerClass job, bool dead) =>
        $"{Folder}{(job == PlayerClass.MartialArtist || !Enum.IsDefined(job) ? PlayerClass.Fighter : job)}{(dead ? "_" : "")}.png";

    // The fraction of a bar's width that a value fills.
    public static int Fill(int value, int maximum, int width) => maximum <= 0 ? 0 : (int)((long)Math.Clamp(value, 0, maximum) * width / maximum);

    public bool Contains(HudLayout layout, PartyInfo party, Point pointer)
    {
        if (party.Members.Count == 0)
        {
            return false;
        }

        if (Confirming || _menu.Count > 0 && Menu().Contains(pointer) || Toggle(layout).Contains(pointer))
        {
            return true;
        }

        return !Folded && RowAt(layout, party, pointer) >= 0;
    }

    // A left press on a frame moved past DragThreshold carries the column, kept on screen.
    public void Drag(HudLayout layout, PartyInfo party, Point pointer, MouseState mouse, MouseState previous, bool allowed)
    {
        if (mouse.LeftButton == ButtonState.Released)
        {
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
            _pressed = allowed && !Folded && !Confirming && RowAt(layout, party, pointer) >= 0;
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
            var top = Top(layout);
            Offset = new Point(Math.Clamp(_pressedOffset.X + moved.X, 0, Math.Max(0, layout.Width - Width)),
                Math.Clamp(_pressedOffset.Y + moved.Y, -top, Math.Max(-top, layout.Height - top - ListTop - RowStep)));
        }
    }

    public void Close()
    {
        _menu.Clear();
        _confirming = null;
    }

    public bool Click(HudLayout layout, PartyInfo party, Point pointer)
    {
        if (_confirming is { } confirming)
        {
            if (HudMessageBox.Accept.Contains(pointer))
            {
                Commanded?.Invoke(confirming.Command, confirming.MemberId);
                _confirming = null;
            }
            else if (HudMessageBox.Reject.Contains(pointer))
            {
                _confirming = null;
            }

            return true;
        }

        if (_menu.Count > 0)
        {
            var inside = Menu().Contains(pointer);
            if (inside)
            {
                _confirming = _menu[(pointer.Y - _menuAt.Y) / MenuRow];
            }

            _menu.Clear();
            if (inside)
            {
                return true;
            }
        }

        if (party.Members.Count > 0 && Toggle(layout).Contains(pointer))
        {
            Folded = !Folded;
            return true;
        }

        return !Folded && RowAt(layout, party, pointer) >= 0;
    }

    // A right click on a frame opens its menu; true when it fell on a frame.
    public bool RightClick(HudLayout layout, PartyInfo party, ulong self, Point pointer)
    {
        _menu.Clear();
        var index = Folded ? -1 : RowAt(layout, party, pointer);
        if (index < 0 || Confirming)
        {
            return false;
        }

        var member = party.Members[index];
        var leading = party.Members[0].EntityId == self;
        if (leading && member.EntityId != self)
        {
            _menu.Add(new MenuItem("Party.Menu.Delegate", "Party.Confirm.Delegate", PartyCommand.Delegate, member.EntityId, member.Name));
            _menu.Add(new MenuItem("Party.Menu.Kick", "Party.Confirm.Kick", PartyCommand.Kick, member.EntityId, member.Name));
        }
        else if (leading)
        {
            _menu.Add(new MenuItem("Party.Menu.Mode", "Party.Confirm.Mode", PartyCommand.ChangeMode, 0, ""));
            if (party.Raid.Count > 0)
            {
                _menu.Add(new MenuItem("Party.Menu.LeaveRaid", "Party.Confirm.LeaveRaid", PartyCommand.LeaveRaid, 0, ""));
            }

            _menu.Add(new MenuItem("Party.Menu.Disband", "Party.Confirm.Disband", PartyCommand.Disband, 0, ""));
            _menu.Add(new MenuItem("Party.Menu.Leave", "Party.Confirm.Leave", PartyCommand.Leave, 0, ""));
        }
        else if (member.EntityId == self)
        {
            _menu.Add(new MenuItem("Party.Menu.Leave", "Party.Confirm.Leave", PartyCommand.Leave, 0, ""));
        }

        var opened = FrameRow(layout, index);
        _menuAt = new Point(opened.Right + 2, opened.Y + 6);
        return true;
    }

    public void Draw(HudPainter painter, HudLayout layout, PartyInfo party, ulong self, Point pointer)
    {
        if (party.Members.Count == 0)
        {
            Close();
            return;
        }

        Refresh(party);
        var toggle = Toggle(layout);
        painter.Sprite(toggle.Contains(pointer) ? ExpandHover : Expand, toggle.X, toggle.Y);
        var mode = new Rectangle(toggle.Right + 4, toggle.Y, 15, 15);
        painter.Sprite(ModeArt[(int)party.Mode], mode.X, mode.Y);
        if (toggle.Contains(pointer) || mode.Contains(pointer))
        {
            painter.Text(ClientText.Get(toggle.Contains(pointer) ? "Party.Fold" : ModeTips[(int)party.Mode]), mode.Right + 4, toggle.Y - 1, HudPteuWindow.TextColor);
        }

        if (Folded)
        {
            return;
        }

        var near = party.Members.FirstOrDefault(member => member.EntityId == self)?.Position;
        for (var index = 0; index < party.Members.Count; index++)
        {
            DrawMember(painter, FrameRow(layout, index), party.Members[index], index, near, party.LeadsRaid);
        }

        var column = FrameRow(layout, 0);
        for (var index = 0; index < party.Raid.Count; index++)
        {
            DrawRaidMember(painter, column.X + RaidColumnX, column.Y + index * RaidRowStep, party.Raid[index], near);
        }

        DrawMenu(painter, pointer);
    }

    // Drawn over the windows.
    public void DrawConfirm(HudPainter painter, Point pointer)
    {
        if (_confirming is { } confirming)
        {
            painter.Flush();
            HudMessageBox.Draw(painter, confirming.Name, ClientText.Get(confirming.Question), pointer);
        }
    }

    private static void DrawRaidMember(HudPainter painter, int x, int y, PartyMemberInfo member, System.Numerics.Vector2? near)
    {
        var far = near is { } place && System.Numerics.Vector2.DistanceSquared(place, member.Position) > PartyLimits.NearRange * PartyLimits.NearRange;
        var tint = far ? Far : Color.White;
        painter.Text(painter.Fit(member.Name, 110), x, y - 2, member.Life <= 0 ? DeadColor : far ? FarNameColor : Color.White);
        painter.Stretch(MiniBack, new Rectangle(x, y + 14, MiniWidth, 4), null, tint);
        painter.Stretch(MiniFill, new Rectangle(x, y + 14, Fill(member.Life, member.MaxLife, MiniWidth), 4), null, tint);
    }

    private void DrawMember(HudPainter painter, Rectangle row, PartyMemberInfo member, int index, System.Numerics.Vector2? near, bool leadsRaid)
    {
        var far = near is { } place && System.Numerics.Vector2.DistanceSquared(place, member.Position) > PartyLimits.NearRange * PartyLimits.NearRange;
        var dead = member.Life <= 0;
        var tint = far ? Far : Color.White;
        painter.Stretch(Background, row, null, tint);
        painter.Stretch(Portrait(member.Class, dead), new Rectangle(row.X + 6, row.Y + 8, PortraitSize, PortraitSize), null, tint);
        Bar(painter, LifeBar, new Rectangle(row.X + 46, row.Y + 25, BarWidth, BarArtHeight), Fill(member.Life, member.MaxLife, BarWidth), BarWidth, tint);
        Bar(painter, ManaBar, new Rectangle(row.X + 42, row.Y + 37, ManaWidth, 3), Fill(member.Mana, member.MaxMana, ManaWidth), ManaWidth, tint);
        painter.Sprite(Members[index], row.X + 34, row.Y + 40, tint);
        if (index == 0 && leadsRaid)
        {
            painter.Sprite(RaidCrown, row.X + 5, row.Y - 18, tint);
        }

        if (index == 0)
        {
            painter.Sprite(Leader, row.X + 13, row.Y - 10, tint);
        }

        painter.TextCentered(_levels[index], row.X + 43, row.X + 63, row.Y + 6, LevelColor);
        painter.Text(painter.Fit(member.Name, Width - 74), row.X + 70, row.Y + 6, dead ? DeadColor : far ? FarNameColor : Color.White);
    }

    private static void Bar(HudPainter painter, string art, Rectangle area, int filled, int width, Color tint)
    {
        if (filled > 0)
        {
            painter.Stretch(art, new Rectangle(area.X, area.Y, filled, area.Height), new Rectangle(0, 0, filled * BarWidth / width, BarArtHeight), tint);
        }
    }

    private void DrawMenu(HudPainter painter, Point pointer)
    {
        if (_menu.Count == 0)
        {
            return;
        }

        var menu = Menu();
        for (var index = 0; index < _menu.Count; index++)
        {
            var row = new Rectangle(menu.X, menu.Y + index * MenuRow, menu.Width, MenuRow);
            painter.Sprite(index == 0 ? HudArt.MenuTop : index == _menu.Count - 1 ? HudArt.MenuBottom : HudArt.MenuMiddle, row.X, row.Y);
            if (row.Contains(pointer))
            {
                painter.Fill(row, HudPteuWindow.HoverColor);
            }

            painter.Text(ClientText.Get(_menu[index].Label), row.X + 13, row.Y + 4, new Color(230, 180, 150));
        }
    }

    private Rectangle Menu() => new(_menuAt.X, _menuAt.Y, MenuWidth, _menu.Count * MenuRow);

    private int RowAt(HudLayout layout, PartyInfo party, Point pointer)
    {
        for (var index = 0; index < party.Members.Count; index++)
        {
            if (FrameRow(layout, index).Contains(pointer))
            {
                return index;
            }
        }

        return -1;
    }

    // Members change as a new list: their numbers are worded only then.
    private void Refresh(PartyInfo party)
    {
        if (ReferenceEquals(party, _shown))
        {
            return;
        }

        _shown = party;
        _levels = new string[party.Members.Count];
        for (var index = 0; index < _levels.Length; index++)
        {
            _levels[index] = party.Members[index].Level.ToString(CultureInfo.InvariantCulture);
        }
    }

    private readonly record struct MenuItem(string Label, string Question, PartyCommand Command, ulong MemberId, string Name);
}
