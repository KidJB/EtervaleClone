using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Parties;

public enum HudTargetAction
{
    Trade,
    Friend,
    Party,
    Raid,
    Clan
}

// The menu of another player's target frame (art assets/Hud/Menu): a small "more" button at the frame's top right opens
// a list beside it of what the player can ask of that target: trade, add as a friend, a party, a raid (a party leader)
// and a clan invitation (a recruiter beside a player without a clan). A choice, a click elsewhere or another target
// closes it.
public sealed class HudTargetMenu
{
    private const string Folder = "Hud/Menu/";
    private const string MoreArt = Folder + "more.png";
    private const string MoreLit = Folder + "more_.png";
    private const string FirstRow = Folder + "topleft.png";
    private const string MiddleRow = Folder + "middle.png";
    private const string LastRow = Folder + "bottom.png";
    public static readonly string[] Art = [MoreArt, MoreLit, FirstRow, MiddleRow, LastRow];

    public const int MoreSize = 25;
    public const int RowWidth = 94;
    public const int RowHeight = 24;
    // Where the button sits on the new frame's 301x80 bar, and in from the classic frame's top right.
    private static readonly Point BarMore = new(262, 16);
    private const int ClassicInset = 4;

    private readonly HudTargetAction[] _actions = new HudTargetAction[Enum.GetValues<HudTargetAction>().Length];
    private string[] _labels;
    private int _count;
    private ulong _target;

    public bool Open { get; private set; }

    public static Rectangle More(HudTargetFrame frame, bool classic) => classic
        ? new Rectangle(frame.Box.Right - MoreSize - ClassicInset, frame.Box.Y + ClassicInset, MoreSize, MoreSize)
        : new Rectangle(frame.Box.X + BarMore.X, frame.Box.Y + BarMore.Y, MoreSize, MoreSize);

    // A row of the list, beside the button to its right (the first row's pointer at the button).
    public static Rectangle Row(HudTargetFrame frame, bool classic, int index)
    {
        var more = More(frame, classic);
        return new Rectangle(more.Right + 2, more.Y + index * RowHeight, RowWidth, RowHeight);
    }

    // The choices for the targeted player: a new target closes the list.
    public void Offer(ulong target, bool raid, bool clan)
    {
        if (target != _target)
        {
            _target = target;
            Open = false;
        }

        _count = 0;
        _actions[_count++] = HudTargetAction.Trade;
        _actions[_count++] = HudTargetAction.Friend;
        _actions[_count++] = HudTargetAction.Party;
        if (raid)
        {
            _actions[_count++] = HudTargetAction.Raid;
        }

        if (clan)
        {
            _actions[_count++] = HudTargetAction.Clan;
        }
    }

    public int Count => _count;

    public HudTargetAction this[int index] => _actions[index];

    public void Close() => Open = false;

    public bool Contains(HudTargetFrame frame, bool classic, Point pointer) => More(frame, classic).Contains(pointer) || Open && RowAt(frame, classic, pointer) >= 0;

    // The button opens or closes the list; a row gives its action and closes it; a click elsewhere closes it.
    public HudTargetAction? Click(HudTargetFrame frame, bool classic, Point pointer)
    {
        if (More(frame, classic).Contains(pointer))
        {
            Open = !Open;
            return null;
        }

        var row = Open ? RowAt(frame, classic, pointer) : -1;
        Open = false;
        return row >= 0 ? _actions[row] : null;
    }

    private int RowAt(HudTargetFrame frame, bool classic, Point pointer)
    {
        for (var index = 0; index < _count; index++)
        {
            if (Row(frame, classic, index).Contains(pointer))
            {
                return index;
            }
        }

        return -1;
    }

    public void Draw(HudPainter painter, HudTargetFrame frame, bool classic, Point pointer)
    {
        var more = More(frame, classic);
        painter.Sprite(more.Contains(pointer) || Open ? MoreLit : MoreArt, more.X, more.Y);
        if (!Open)
        {
            return;
        }

        _labels ??= [.. Enum.GetValues<HudTargetAction>().Select(static action => ClientText.Get("TargetMenu." + action))];
        painter.Flush();
        for (var index = 0; index < _count; index++)
        {
            var row = Row(frame, classic, index);
            painter.Sprite(index == 0 ? FirstRow : index == _count - 1 ? LastRow : MiddleRow, row.X, row.Y);
            painter.TextCentered(_labels[(int)_actions[index]], row.Left + 6, row.Right, row.Y + 5, row.Contains(pointer) ? HudTextColors.Highlight : Color.White);
        }
    }
}
