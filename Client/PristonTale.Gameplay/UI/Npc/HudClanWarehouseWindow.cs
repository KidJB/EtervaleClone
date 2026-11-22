using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// The clan warehouse's own window, opened at the Clan Warehouse keeper, centred on the screen until its header is dragged
// (it stays whole on the screen), drawn on its own art (assets/Hud/ClanWarehouse/cw_*.png, made by
// tools/ui/build_clan_warehouse.py to this layout): a header with the clan's name and Close; on the left the grid of the
// page shown (HudStorageGrid) with Prev, "Page X of Y" and Next under it (they also turn the page while an item is
// carried, so it can be set down on the other one) and the free slots and weight under those; on the right the tabs
// Items (the player's rights), Log (for a rank that may read it: time, member, action, amount and item, newest first,
// scrolled by Newer and Older), Permissions (the chief's: the vice's and members' rights as boxes, saved with Save),
// Upgrades (the treasury, donations and the chief's upgrades) and Buffs (the clan's world buffs and their items).
public sealed class HudClanWarehouseWindow : IHudStorageWindow
{
    private const int Width = 720;
    private const int Height = 440;
    private const int Margin = 20;
    private const int HeaderHeight = 56;
    private const int GridColumn = StoreGrid.Columns * HudGridPlacement.CellSize;
    private const int GridX = Margin + 12;
    private const int GridY = HeaderHeight + 28;
    private const int PageY = GridY + StoreGrid.Rows * HudGridPlacement.CellSize + 18;
    private const int CapacityY = PageY + 50;
    private const int PaneX = Margin + GridColumn + 36;
    private const int PaneWidth = Width - PaneX - Margin - 6;
    private const int TabHeight = 24;
    private const int TabY = HeaderHeight + 16;
    private const int ContentY = TabY + TabHeight + 14;
    private const int FooterY = Height - 46;
    private const int DragThreshold = 3;
    private const int LogRows = 12;
    private const int LogStep = 20;
    private const int LogTop = ContentY + 22;
    private const int BarHeight = 8;
    private const float TitleScale = HudPainter.TextScale * 20f / 14f;
    private const float SmallScale = HudPainter.TextScale * 11f / 14f;
    private const string Folder = "Hud/ClanWarehouse/";
    private const string WindowArt = Folder + "cw_window.png";
    private const string TabArt = Folder + "cw_tab.png";
    private const string TabOnArt = Folder + "cw_tab_on.png";
    private const string ButtonArt = Folder + "cw_button_normal.png";
    private const string ButtonLitArt = Folder + "cw_button_lit.png";
    private const string ButtonOffArt = Folder + "cw_button_pressed.png";
    private const string CloseArt = Folder + "cw_close.png";
    private const string CloseLitArt = Folder + "cw_close_lit.png";
    private const string RowArt = Folder + "cw_row.png";
    private const string RowLitArt = Folder + "cw_row_lit.png";
    private const string CheckArt = Folder + "cw_check.png";
    private const string CheckOnArt = Folder + "cw_check_on.png";
    private static readonly string[] ActionArt = [Folder + "cw_in.png", Folder + "cw_out.png", Folder + "cw_gold.png", Folder + "cw_upgrade.png"];
    public static readonly string[] Art =
        [WindowArt, TabArt, TabOnArt, ButtonArt, ButtonLitArt, ButtonOffArt, CloseArt, CloseLitArt, RowArt, RowLitArt, CheckArt, CheckOnArt, .. ActionArt];
    private static readonly Color Brass = new(0xd6, 0xa3, 0x5c);
    private static readonly Color BrassDim = new(0x9a, 0x7a, 0x4a);
    private static readonly Color Ink = new(0xe9, 0xdf, 0xd2);
    private static readonly Color InkDim = new(0x9c, 0x8f, 0x80);
    private static readonly Color Well = new(0x0c, 0x0a, 0x09);
    private static readonly Color Edge = new(0x4a, 0x3f, 0x35);
    private static readonly Color Gain = new(0x7c, 0xd0, 0x7c);
    private static readonly Color Loss = new(0xe0, 0x84, 0x6c);
    private static readonly Color Gold = new(0xf0, 0xc8, 0x6a);
    private static readonly Color Upgrade = new(0x8c, 0xbe, 0xff);
    private static readonly Color[] ActionColors = [Gain, Loss, Gold, Upgrade];
    private static readonly string[] ActionKeys = ["ClanWarehouse.ActionDeposit", "ClanWarehouse.ActionWithdraw", "ClanWarehouse.ActionDonate", "ClanWarehouse.ActionUpgrade"];
    private static readonly string[] ColumnKeys = ["ClanWarehouse.ColTime", "ClanWarehouse.ColMember", "ClanWarehouse.ColAction", "ClanWarehouse.ColAmount", "ClanWarehouse.ColItem"];
    private static readonly int[] Columns = [0, 92, 196, 320, 334];
    private static readonly ClanPermission[] Rights =
        [ClanPermission.Deposit, ClanPermission.Withdraw, ClanPermission.ViewLog, ClanPermission.ActivateBuffs, ClanPermission.SpendCoins];
    private static readonly string[] RightKeys = ["ClanWarehouse.Deposit", "ClanWarehouse.Withdraw", "ClanWarehouse.Logs", "ClanWarehouse.Buffs", "ClanWarehouse.Coins"];
    private static readonly string[] RankKeys = ["ClanWarehouse.Chief", "ClanWarehouse.Vice", "ClanWarehouse.Member"];
    private static readonly string[] TabKeys = ["ClanWarehouse.Items", "ClanWarehouse.Log", "ClanWarehouse.Permissions", "ClanWarehouse.Upgrades", "ClanWarehouse.Buffs"];
    private static readonly ClanUpgradeKind[] Upgrades = [ClanUpgradeKind.WarehouseSlots, ClanUpgradeKind.WarehouseWeight];
    private static readonly string[] UpgradeKeys = ["ClanWarehouse.SlotsUpgrade", "ClanWarehouse.WeightUpgrade"];

    private enum Tab
    {
        Items,
        Log,
        Permissions,
        Upgrades,
        Buffs
    }

    private readonly HudSlide _slide = new();
    private readonly HudStorageGrid _grid = new();
    private readonly string[] _rightLines = new string[Rights.Length];
    private readonly string[] _upgradeLevels = new string[Upgrades.Length];
    private readonly string[] _upgradeNext = new string[Upgrades.Length];
    private OwnerState _state;
    private double _nowMs;
    private Point _screen = new(1280, 720);
    private Point _offset;
    private Point _pressedAt;
    private Point _pressedOffset;
    private bool _pressed;
    private bool _dragging;
    private ClanInfo _clan = ClanInfo.None;
    private ClanRankPermissions _editing;
    private Tab _tab;
    private int _logFirst;
    private IReadOnlyList<ClanWarehouseLogLine> _log = [];
    private LogRow[] _logRows = [];
    private string _clanName = "";
    private string _pageLine = "";
    private string _freeLine = "";
    private string _weightLine = "";
    private string _treasuryLine = "";
    private string _coinTreasuryLine = "";
    private float _freeShare;
    private float _weightShare;

    // A log line as shown: its time, the action's mark (ActionArt, ActionColors), its amount and what it was about.
    private readonly record struct LogRow(string Time, int Action, string Amount, string Item);

    public WarehouseView View => _grid.View;
    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public bool BlocksWorld => true;
    public Guid Dragging => _grid.Dragging;
    public OwnedItemView Carried => _grid.Carried;
    public int Page => _grid.Page;

    // The clan's world buffs, shown in the Buffs tab.
    public Clans.HudClanBuffs Buffs { get; } = new();

    // The window's top left: centred on the screen, moved by the drag, kept whole on the screen.
    public Point Origin => new(Math.Clamp((_screen.X - Width) / 2 + _offset.X, 0, Math.Max(0, _screen.X - Width)),
        Math.Clamp((_screen.Y - Height) / 2 - 40 + _offset.Y, 0, Math.Max(0, _screen.Y - Height)));

    public static Rectangle Window(Point origin) => new(origin.X, origin.Y, Width, Height);
    public static Rectangle Header(Point origin) => new(origin.X, origin.Y, Width - Margin - 36, HeaderHeight);
    public static Rectangle Close(Point origin) => Local(origin, Width - Margin - 26, 16, 24, 24);
    public static Rectangle Grid(Point origin) => Local(origin, GridX, GridY, GridColumn, StoreGrid.Rows * HudGridPlacement.CellSize);
    public static Rectangle Previous(Point origin) => Local(origin, GridX, PageY, 58, 26);
    public static Rectangle Next(Point origin) => Local(origin, GridX + GridColumn - 58, PageY, 58, 26);
    public static Rectangle TabButton(Point origin, int tab) => Local(origin, PaneX + tab * (PaneWidth / 5), TabY, PaneWidth / 5 - 4, TabHeight);
    public static Rectangle BuffArea(Point origin) =>
        Local(origin, PaneX, ContentY, PaneWidth, Clans.HudClanBuffs.TitleHeight + WorldBuffs.All.Count * Clans.HudClanBuffs.RowStep);
    public static Rectangle DonateButton(Point origin) => Local(origin, PaneX + PaneWidth - 140, ContentY + 4, 140, 24);
    public static Rectangle DonateCoinsButton(Point origin) => Local(origin, PaneX + PaneWidth - 140, ContentY + 32, 140, 24);
    public static Rectangle UpgradeButton(Point origin, int upgrade) => Local(origin, PaneX + PaneWidth - 110, ContentY + 92 + upgrade * 72, 110, 26);
    public static Rectangle Newer(Point origin) => Local(origin, PaneX - 8, FooterY, 110, 26);
    public static Rectangle Older(Point origin) => Local(origin, PaneX + PaneWidth - 102, FooterY, 110, 26);
    public static Rectangle PermissionBox(Point origin, int rank, int right) => Local(origin, PaneX + 112 + right * 66, ContentY + 34 + rank * 36, 18, 18);
    public static Rectangle Save(Point origin) => Local(origin, PaneX + PaneWidth - 102, FooterY, 110, 26);

    private static Rectangle Local(Point origin, int x, int y, int width, int height) => new(origin.X + x, origin.Y + y, width, height);

    public void Show(WarehouseView view, ClanInfo clan)
    {
        _tab = Tab.Items;
        _grid.Show(view);
        ObserveClan(clan);
        _slide.Open = true;
    }

    public void Refresh(WarehouseView view)
    {
        _grid.Refresh(view);
        Texts();
    }

    // The clan's name, the player's rights, the treasury and the upgrades as the server last sent them; a tab the rank
    // lost closes.
    public void ObserveClan(ClanInfo clan)
    {
        _clan = clan;
        _clanName = clan.Name;
        _editing = clan.Permissions;
        for (var index = 0; index < Rights.Length; index++)
        {
            _rightLines[index] = ClientText.Get(RightKeys[index]);
        }

        _treasuryLine = clan.Treasury.ToString("N0", CultureInfo.InvariantCulture);
        _coinTreasuryLine = clan.CoinTreasury.ToString("N0", CultureInfo.InvariantCulture);
        for (var index = 0; index < Upgrades.Length; index++)
        {
            var kind = Upgrades[index];
            var level = ClanUpgrades.LevelOf(kind, clan.SlotLevel, clan.WeightLevel);
            _upgradeLevels[index] = ClientText.Format("ClanWarehouse.Level", level, ClanUpgrades.MaxLevel, UpgradeValue(kind, level));
            _upgradeNext[index] = level >= ClanUpgrades.MaxLevel ? ClientText.Get("ClanWarehouse.MaxedOut")
                : ClientText.Format("ClanWarehouse.NextLevel", UpgradeValue(kind, level + 1), ClanUpgrades.Cost(kind, level).ToString("N0", CultureInfo.InvariantCulture));
        }

        Texts();
        if (_tab == Tab.Log && !clan.May(ClanPermission.ViewLog) || _tab == Tab.Permissions && clan.Rank != ClanRank.Chief)
        {
            _tab = Tab.Items;
        }
    }

    public void ShowLog(IReadOnlyList<ClanWarehouseLogLine> lines)
    {
        _log = lines;
        _logFirst = 0;
        _logRows = new LogRow[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var time = DateTimeOffset.FromUnixTimeSeconds(line.AtSeconds).ToLocalTime().ToString("MMM dd  HH:mm", CultureInfo.InvariantCulture);
            var count = line.Quantity.ToString("N0", CultureInfo.InvariantCulture);
            _logRows[index] = line.Action switch
            {
                ClanWarehouseLogAction.Deposit => new LogRow(time, 0, "x" + count, line.ItemName),
                ClanWarehouseLogAction.Donate => new LogRow(time, 2, count, ClientText.Get("ClanWarehouse.Treasury")),
                ClanWarehouseLogAction.DonateCoins => new LogRow(time, 2, count + "c", ClientText.Get("ClanWarehouse.CoinTreasury")),
                ClanWarehouseLogAction.SpendCoins => new LogRow(time, 3, count + "c", line.ItemName),
                ClanWarehouseLogAction.Upgrade => new LogRow(time, 3, ClientText.Format("ClanWarehouse.LevelShort", count),
                    ClientText.Get(line.ItemKey == nameof(ClanUpgradeKind.WarehouseWeight) ? UpgradeKeys[1] : UpgradeKeys[0])),
                _ => new LogRow(time, 1, "x" + count, line.ItemName)
            };
        }
    }

    public void CarryAfter(Guid itemId) => _grid.CarryAfter(itemId);

    public void Hide()
    {
        _slide.Open = false;
        _grid.Clear();
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool Contains(Point pointer) => _slide.Visible && Window(Origin).Contains(pointer);

    public HudPlacement Placement(OwnedItemView carried, Point pointer) => FullyOpen ? _grid.Placement(Grid(Origin), carried, pointer) : default;

    public HudWarehouseAction SetDownOnGrid(OwnedItemView carried, Point pointer) => FullyOpen ? _grid.SetDownOnGrid(Grid(Origin), carried, pointer) : default;

    public HudWarehouseAction Update(MouseState mouse, MouseState previous, Point pointer, Func<OwnedItemView, Point, HudPlacement> bagPlacement,
        bool blocked, bool incoming)
    {
        if (!FullyOpen || View is null)
        {
            _grid.Clear();
            return default;
        }

        var origin = Origin;
        if (Drag(mouse, previous, pointer, origin, blocked || incoming || _grid.Carried is not null))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        var grid = Grid(origin);
        _grid.Hover(grid, pointer, !incoming && !blocked);
        if (blocked || !HudStorageGrid.Pressed(mouse, previous))
        {
            return default;
        }

        if (Previous(origin).Contains(pointer) || Next(origin).Contains(pointer))
        {
            _grid.Turn(Next(origin).Contains(pointer) ? 1 : -1);
            Texts();
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        if (_grid.Carried is not null)
        {
            return _grid.SetDown(grid, pointer, bagPlacement);
        }

        if (incoming)
        {
            return Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Consumed) : default;
        }

        if (Close(origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Exit);
        }

        if (PaneClick(origin, pointer) is { } paneAction)
        {
            return paneAction;
        }

        if (_grid.Lift() is { } lifted)
        {
            return lifted;
        }

        return Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Consumed) : default;
    }

    // A press on the header moved past DragThreshold carries the window; true while it is pressed or carried.
    private bool Drag(MouseState mouse, MouseState previous, Point pointer, Point origin, bool busy)
    {
        if (mouse.LeftButton == ButtonState.Released)
        {
            var was = _pressed;
            _pressed = false;
            _dragging = false;
            return was && previous.LeftButton == ButtonState.Pressed && Header(origin).Contains(pointer);
        }

        if (previous.LeftButton == ButtonState.Released)
        {
            _pressed = !busy && Header(origin).Contains(pointer);
            _pressedAt = pointer;
            _pressedOffset = _offset;
            return _pressed;
        }

        if (!_pressed)
        {
            return false;
        }

        var moved = new Point(pointer.X - _pressedAt.X, pointer.Y - _pressedAt.Y);
        _dragging |= Math.Abs(moved.X) > DragThreshold || Math.Abs(moved.Y) > DragThreshold;
        if (_dragging)
        {
            _offset = new Point(_pressedOffset.X + moved.X, _pressedOffset.Y + moved.Y);
        }

        return true;
    }

    private HudWarehouseAction? PaneClick(Point origin, Point pointer)
    {
        for (var tab = 0; tab < TabKeys.Length; tab++)
        {
            if (TabShown((Tab)tab) && TabButton(origin, tab).Contains(pointer))
            {
                _tab = (Tab)tab;
                _editing = _clan.Permissions;
                return new HudWarehouseAction(_tab == Tab.Log ? HudWarehouseActionKind.ClanLog : HudWarehouseActionKind.Consumed);
            }
        }

        switch (_tab)
        {
            case Tab.Log when Newer(origin).Contains(pointer) || Older(origin).Contains(pointer):
                var step = Older(origin).Contains(pointer) ? LogRows : -LogRows;
                _logFirst = Math.Clamp(_logFirst + step, 0, Math.Max(0, _log.Count - LogRows));
                return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
            case Tab.Buffs:
                var use = Buffs.Click(BuffArea(origin), pointer, _clan, _state);
                return use.Kind switch
                {
                    Clans.HudClanBuffsActionKind.Activate => new HudWarehouseAction(HudWarehouseActionKind.ClanBuff, use.Item.InstanceId),
                    Clans.HudClanBuffsActionKind.SpendCoins => new HudWarehouseAction(HudWarehouseActionKind.ClanCoinBuff, Anchor: use.Argument),
                    _ => null
                };
            case Tab.Upgrades:
                return UpgradesClick(origin, pointer);
            case Tab.Permissions:
                return PermissionsClick(origin, pointer);
            default:
                return null;
        }
    }

    private HudWarehouseAction? UpgradesClick(Point origin, Point pointer)
    {
        if (DonateButton(origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.ClanDonate);
        }

        if (DonateCoinsButton(origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.ClanDonateCoins);
        }

        for (var index = 0; index < Upgrades.Length; index++)
        {
            if (UpgradeButton(origin, index).Contains(pointer) && CanBuy(index))
            {
                return new HudWarehouseAction(HudWarehouseActionKind.ClanUpgrade, Anchor: (int)Upgrades[index]);
            }
        }

        return null;
    }

    private HudWarehouseAction? PermissionsClick(Point origin, Point pointer)
    {
        for (var right = 0; right < Rights.Length; right++)
        {
            if (PermissionBox(origin, 1, right).Contains(pointer))
            {
                _editing = _editing with { Vice = _editing.Vice ^ Rights[right] };
                return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
            }

            if (PermissionBox(origin, 2, right).Contains(pointer))
            {
                _editing = _editing with { Member = _editing.Member ^ Rights[right] };
                return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
            }
        }

        return Save(origin).Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.ClanPermissions, Anchor: _editing.Packed) : null;
    }

    private bool TabShown(Tab tab) => tab switch
    {
        Tab.Log => _clan.May(ClanPermission.ViewLog),
        Tab.Permissions => _clan.Rank == ClanRank.Chief,
        _ => true
    };

    void IHudServiceWindow.Draw(in HudServiceFrame frame)
    {
        _screen = new Point(frame.Layout.Width, frame.Layout.Height);
        _state = frame.State;
        _nowMs = frame.NowMs;
        Draw(frame.Painter, frame.Icons, frame.Pointer, frame.Incoming, FullyOpen && !frame.SettingsOpen);
    }

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame)
    {
        if (!_grid.DrawOverlay(frame.Painter, frame.Layout, frame.Icons, frame.Pointer, frame.State) && FullyOpen && _tab == Tab.Log)
        {
            DrawLogHint(frame.Painter, frame.Layout, frame.Pointer);
        }
    }

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer, OwnedItemView incoming, bool interactive)
    {
        var origin = Origin;
        var window = Window(origin);
        painter.Stretch(WindowArt, window);
        painter.Text(painter.Fit(_clanName, Width - 160), window.X + Margin + 4, window.Y + 9, Brass, TitleScale);
        painter.Text(ClientText.Get("ClanWarehouse.Title"), window.X + Margin + 6, window.Y + 35, InkDim, SmallScale);
        var close = Close(origin);
        painter.Stretch(close.Contains(pointer) ? CloseLitArt : CloseArt, close);
        if (View is null)
        {
            return;
        }

        _grid.Draw(painter, icons, Grid(origin), pointer, incoming, interactive);
        Button(painter, Previous(origin), pointer, ClientText.Get("ClanWarehouse.Previous"), Page > 0);
        Button(painter, Next(origin), pointer, ClientText.Get("ClanWarehouse.Next"), Page < _grid.PageCount - 1);
        painter.TextCentered(_pageLine, Previous(origin).Right, Next(origin).Left, Previous(origin).Y + 6, Ink);
        DrawCapacity(painter, origin);

        for (var tab = 0; tab < TabKeys.Length; tab++)
        {
            if (TabShown((Tab)tab))
            {
                var button = TabButton(origin, tab);
                var chosen = (int)_tab == tab;
                painter.Stretch(chosen ? TabOnArt : TabArt, button, null, !chosen && button.Contains(pointer) ? new Color(255, 235, 200) : Color.White);
                Centered(painter, painter.Fit(ClientText.Get(TabKeys[tab]), button.Width - 6), button, button.Y + 7, chosen ? Gold : Ink);
            }
        }

        switch (_tab)
        {
            case Tab.Log:
                DrawLog(painter, origin, pointer);
                break;
            case Tab.Permissions:
                DrawPermissions(painter, origin, pointer);
                break;
            case Tab.Upgrades:
                DrawUpgrades(painter, origin, pointer);
                break;
            case Tab.Buffs:
                Buffs.Draw(painter, BuffArea(origin), pointer, _clan, _state, _nowMs);
                break;
            default:
                DrawItems(painter, origin);
                break;
        }
    }

    // The free slots and weight under the page bar, each with its gauge.
    private void DrawCapacity(HudPainter painter, Point origin)
    {
        var x = origin.X + GridX;
        var y = origin.Y + CapacityY;
        painter.Text(ClientText.Get("ClanWarehouse.FreeSlots"), x, y, BrassDim, SmallScale);
        painter.Text(_freeLine, x + GridColumn - painter.MeasureAt(_freeLine, SmallScale), y, Ink, SmallScale);
        Bar(painter, new Rectangle(x, y + 16, GridColumn, BarHeight), _freeShare);
        y += 36;
        painter.Text(ClientText.Get("ClanWarehouse.Weight"), x, y, BrassDim, SmallScale);
        painter.Text(_weightLine, x + GridColumn - painter.MeasureAt(_weightLine, SmallScale), y, Ink, SmallScale);
        Bar(painter, new Rectangle(x, y + 16, GridColumn, BarHeight), _weightShare);
    }

    private void DrawItems(HudPainter painter, Point origin)
    {
        var x = origin.X + PaneX;
        var y = origin.Y + ContentY;
        painter.Text(ClientText.Get("ClanWarehouse.YourRights"), x, y, Brass);
        for (var index = 0; index < _rightLines.Length; index++)
        {
            var allowed = _clan.May(Rights[index]);
            painter.Sprite(allowed ? CheckOnArt : CheckArt, x + 6, y + 26 + index * 26);
            painter.Text(_rightLines[index], x + 32, y + 27 + index * 26, allowed ? Ink : InkDim);
        }

        var hint = y + 40 + Rights.Length * 26;
        painter.Fill(new Rectangle(x, hint - 8, PaneWidth, 1), Edge);
        painter.Text(ClientText.Get("ClanWarehouse.Hint"), x, hint, InkDim, SmallScale);
        painter.Text(ClientText.Get("ClanWarehouse.Hint2"), x, hint + 18, InkDim, SmallScale);
    }

    // The treasury with its donation button, then each upgrade's level, its next level's cost and the chief's button.
    private void DrawUpgrades(HudPainter painter, Point origin, Point pointer)
    {
        var top = Local(origin, PaneX, ContentY, 0, 0);
        painter.Sprite(ActionArt[2], top.X, top.Y + 8);
        painter.Text(ClientText.Get("ClanWarehouse.Treasury"), top.X + 20, top.Y + 7, BrassDim, SmallScale);
        painter.Text(_treasuryLine, top.X + 150, top.Y + 6, Gold);
        Button(painter, DonateButton(origin), pointer, ClientText.Get("ClanWarehouse.Donate"));
        painter.Sprite(ActionArt[3], top.X, top.Y + 36);
        painter.Text(ClientText.Get("ClanWarehouse.CoinTreasury"), top.X + 20, top.Y + 35, BrassDim, SmallScale);
        painter.Text(_coinTreasuryLine, top.X + 150, top.Y + 34, Upgrade);
        Button(painter, DonateCoinsButton(origin), pointer, ClientText.Get("ClanWarehouse.DonateCoins"));
        for (var index = 0; index < Upgrades.Length; index++)
        {
            var y = top.Y + 70 + index * 72;
            painter.Fill(new Rectangle(top.X, y - 8, PaneWidth, 1), Edge);
            painter.Sprite(ActionArt[3], top.X, y + 2);
            painter.Text(ClientText.Get(UpgradeKeys[index]), top.X + 20, y, Brass);
            painter.Text(_upgradeLevels[index], top.X + 20, y + 22, Ink);
            painter.Text(_upgradeNext[index], top.X + 20, y + 42, InkDim, SmallScale);
            if (_clan.Rank == ClanRank.Chief)
            {
                Button(painter, UpgradeButton(origin, index), pointer, ClientText.Get("ClanWarehouse.Buy"), CanBuy(index));
            }
        }

        painter.Text(ClientText.Get("ClanWarehouse.ChiefSpends"), top.X, top.Y + 66 + Upgrades.Length * 72, InkDim, SmallScale);
        painter.Text(ClientText.Get("ClanWarehouse.CoinsRule"), top.X, top.Y + 84 + Upgrades.Length * 72, InkDim, SmallScale);
    }

    private bool CanBuy(int index) =>
        ClanUpgrades.CheckUpgrade(_clan.Rank, Upgrades[index], ClanUpgrades.LevelOf(Upgrades[index], _clan.SlotLevel, _clan.WeightLevel), _clan.Treasury)
            == ClanStatus.Ok;

    private static string UpgradeValue(ClanUpgradeKind kind, int level) => kind == ClanUpgradeKind.WarehouseSlots
        ? ClientText.Format("ClanWarehouse.SlotsValue", ClanUpgrades.ItemLimit(level), ClanUpgrades.Pages(level))
        : ClientText.Format("ClanWarehouse.WeightValue", ClanUpgrades.WeightLimit(level).ToString("N0", CultureInfo.InvariantCulture));

    // Column titles, then one row per line: time, member, the action's mark and name, the amount and the item.
    private void DrawLog(HudPainter painter, Point origin, Point pointer)
    {
        var x = origin.X + PaneX;
        var y = origin.Y + ContentY;
        for (var column = 0; column < ColumnKeys.Length; column++)
        {
            var title = ClientText.Get(ColumnKeys[column]);
            var at = column == 3 ? x + Columns[column] - painter.MeasureAt(title, SmallScale) : x + Columns[column];
            painter.Text(title, at, y, BrassDim, SmallScale);
        }

        painter.Fill(new Rectangle(x, y + 16, PaneWidth, 1), Edge);
        if (_log.Count == 0)
        {
            painter.Text(ClientText.Get("ClanWarehouse.LogEmpty"), x, origin.Y + LogTop + 4, InkDim);
        }

        for (var row = 0; row < LogRows && _logFirst + row < _log.Count; row++)
        {
            var index = _logFirst + row;
            var shown = _logRows[index];
            var band = new Rectangle(x - 4, origin.Y + LogTop + row * LogStep, PaneWidth + 8, LogStep);
            if (band.Contains(pointer))
            {
                painter.Stretch(RowLitArt, band);
            }
            else if (row % 2 == 0)
            {
                painter.Stretch(RowArt, band);
            }

            var text = band.Y + 4;
            painter.Text(shown.Time, x + Columns[0], text, InkDim, SmallScale);
            painter.Text(painter.Fit(_log[index].Character, Columns[2] - Columns[1] - 8), x + Columns[1], text, Ink, SmallScale);
            painter.Sprite(ActionArt[shown.Action], x + Columns[2], band.Y + 3);
            painter.Text(ClientText.Get(ActionKeys[shown.Action]), x + Columns[2] + 18, text, ActionColors[shown.Action], SmallScale);
            painter.Text(shown.Amount, x + Columns[3] - painter.MeasureAt(shown.Amount, SmallScale), text, ActionColors[shown.Action], SmallScale);
            painter.Text(painter.Fit(shown.Item, PaneWidth - Columns[4]), x + Columns[4], text, Ink, SmallScale);
        }

        Button(painter, Newer(origin), pointer, ClientText.Get("ClanWarehouse.Newer"), _logFirst > 0);
        Button(painter, Older(origin), pointer, ClientText.Get("ClanWarehouse.Older"), _logFirst + LogRows < _log.Count);
    }

    // The hovered log line's item and who moved it, over the other windows.
    private void DrawLogHint(HudPainter painter, HudLayout layout, Point pointer)
    {
        var rows = Local(Origin, PaneX, LogTop, PaneWidth, LogRows * LogStep);
        if (!rows.Contains(pointer))
        {
            return;
        }

        var index = _logFirst + (pointer.Y - rows.Y) / LogStep;
        if (index < _log.Count)
        {
            HudItemTooltip.Draw(painter, layout, pointer, _logRows[index].Item, _log[index].Character);
        }
    }

    private void DrawPermissions(HudPainter painter, Point origin, Point pointer)
    {
        var top = Local(origin, PaneX, ContentY, 0, 0);
        painter.Text(ClientText.Get("ClanWarehouse.PermissionsTitle"), top.X, top.Y, Brass);
        for (var right = 0; right < Rights.Length; right++)
        {
            var box = PermissionBox(origin, 0, right);
            Centered(painter, ClientText.Get(RightKeys[right]), new Rectangle(box.X - 24, 0, box.Width + 48, 0), box.Y - 22, BrassDim);
        }

        for (var rank = 0; rank < RankKeys.Length; rank++)
        {
            var rights = rank == 0 ? ClanPermission.All : rank == 1 ? _editing.Vice : _editing.Member;
            var first = PermissionBox(origin, rank, 0);
            painter.Text(ClientText.Get(RankKeys[rank]), top.X + 8, first.Y + 1, rank == 0 ? InkDim : Ink);
            for (var right = 0; right < Rights.Length; right++)
            {
                var box = PermissionBox(origin, rank, right);
                var tint = rank == 0 ? new Color(150, 150, 150) : box.Contains(pointer) ? new Color(255, 240, 200) : Color.White;
                painter.Sprite((rights & Rights[right]) != 0 ? CheckOnArt : CheckArt, box.X, box.Y, tint);
            }
        }

        painter.Text(ClientText.Get("ClanWarehouse.ChiefAlways"), top.X, top.Y + 46 + RankKeys.Length * 36, InkDim, SmallScale);
        Button(painter, Save(origin), pointer, ClientText.Get("ClanWarehouse.Save"), _editing != _clan.Permissions);
    }

    private void Texts()
    {
        if (View is null)
        {
            return;
        }

        _pageLine = ClientText.Format("ClanWarehouse.Page", Page + 1, _grid.PageCount);
        var limit = ClanUpgrades.ItemLimit(_clan.SlotLevel);
        var free = Math.Max(0, limit - View.Items.Count);
        _freeLine = ClientText.Format("ClanWarehouse.Free", free, limit);
        _weightLine = $"{View.Weight:N0}/{View.MaxWeight:N0}";
        _freeShare = 1f - (float)free / limit;
        _weightShare = View.MaxWeight > 0 ? Math.Clamp((float)View.Weight / View.MaxWeight, 0f, 1f) : 0f;
    }

    // Small text centred over the width of bounds.
    private static void Centered(HudPainter painter, string text, Rectangle bounds, int y, Color color) =>
        painter.Text(text, bounds.X + (bounds.Width - painter.MeasureAt(text, SmallScale)) / 2, y, color, SmallScale);

    private static void Bar(HudPainter painter, Rectangle bounds, float share)
    {
        painter.Fill(bounds, Well);
        painter.Fill(new Rectangle(bounds.X + 1, bounds.Y + 1, (int)((bounds.Width - 2) * share), bounds.Height - 2), share > 0.9f ? Loss : Brass);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), Edge);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), Edge);
    }

    private static void Button(HudPainter painter, Rectangle bounds, Point pointer, string label, bool enabled = true)
    {
        painter.Stretch(!enabled ? ButtonOffArt : bounds.Contains(pointer) ? ButtonLitArt : ButtonArt, bounds);
        painter.TextCentered(label, bounds.Left, bounds.Right, bounds.Y + 6, enabled ? Ink : InkDim);
    }
}
