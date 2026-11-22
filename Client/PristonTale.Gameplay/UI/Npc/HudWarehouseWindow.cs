using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Gameplay.UI.Npc;

public enum HudWarehouseActionKind
{
    None,
    Consumed,
    Exit,
    // An item picked up or set back where it was: only its sound.
    Lifted,
    Deposit,
    Move,
    Withdraw,
    WithdrawGold,
    Refused,

    // The clan warehouse's log asked for; the chief's rank rights saved (Anchor: ClanRankPermissions packed).
    ClanLog,
    ClanPermissions,

    // A donation to the clan treasury asked for (the amount is asked next); the chief's upgrade (Anchor: ClanUpgradeKind).
    ClanDonate,
    ClanUpgrade,

    // A clan buff item used from the Buffs tab (ItemId); a buff the coin treasury pays for (Anchor: WorldBuffs.Argument); a
    // donation of coins asked for (the amount is asked next).
    ClanBuff,
    ClanCoinBuff,
    ClanDonateCoins
}

// A window showing a warehouse's grid, which the HUD drives the same way whichever it is (HudWarehouseWindow,
// HudClanWarehouseWindow).
public interface IHudStorageWindow : IHudServiceWindow
{
    WarehouseView View { get; }
    Guid Dragging { get; }
    OwnedItemView Carried { get; }

    void Refresh(WarehouseView view);

    void CarryAfter(Guid itemId);

    HudPlacement Placement(OwnedItemView carried, Point pointer);

    HudWarehouseAction SetDownOnGrid(OwnedItemView carried, Point pointer);

    HudWarehouseAction Update(MouseState mouse, MouseState previous, Point pointer, Func<OwnedItemView, Point, HudPlacement> bagPlacement, bool blocked,
        bool incoming);
}

// ItemId at Anchor, exchanged with Covered when there is one. Message: why a set-down was refused.
public readonly record struct HudWarehouseAction(HudWarehouseActionKind Kind, Guid ItemId = default, int Anchor = -1, Guid Covered = default,
    string Message = null);

// The account warehouse in the original window: stored gold in the upper bar (its coin takes gold out), weight in the lower
// one and the 9x9 grid (HudStorageGrid). The caravan's storage shows in it with its own controls (HudWarehouseWindow.Caravan).
public sealed partial class HudWarehouseWindow : IHudStorageWindow
{
    private readonly HudSlide _slide = new();
    private readonly HudStorageGrid _grid = new();
    private string _money = "";
    private string _weight = "";

    public WarehouseView View => _grid.View;
    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);
    public Guid Dragging => _grid.Dragging;
    public OwnedItemView Carried => _grid.Carried;

    public void Show(WarehouseView view)
    {
        CaravanMode = false;
        _grid.Show(view);
        Texts();
        _slide.Open = true;
    }

    public void Refresh(WarehouseView view)
    {
        _grid.Refresh(view);
        Texts();
    }

    public void CarryAfter(Guid itemId) => _grid.CarryAfter(itemId);

    public void Hide()
    {
        _caravanName.Focused = false;
        _slide.Open = false;
        _grid.Clear();
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool BlocksWorld => true;

    void IHudServiceWindow.Draw(in HudServiceFrame frame) => Draw(frame.Painter, frame.Icons, frame.Pointer, frame.Incoming, FullyOpen && !frame.SettingsOpen);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.Icons, frame.Pointer, frame.State);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    public HudPlacement Placement(OwnedItemView carried, Point pointer) => FullyOpen ? _grid.Placement(HudStoreLayout.Grid(Origin), carried, pointer) : default;

    public static HudPlacement Placement(Point origin, IReadOnlyList<OwnedItemView> stored, OwnedItemView carried, Point pointer) =>
        HudStorageGrid.Placement(HudStoreLayout.Grid(origin), stored, carried, pointer);

    public HudWarehouseAction SetDownOnGrid(OwnedItemView carried, Point pointer) =>
        FullyOpen ? _grid.SetDownOnGrid(HudStoreLayout.Grid(Origin), carried, pointer) : default;

    // bagPlacement: where the carried item would land in the bag (None while the inventory is closed). incoming: the bag
    // carries an item, which the inventory sets down.
    public HudWarehouseAction Update(MouseState mouse, MouseState previous, Point pointer, Func<OwnedItemView, Point, HudPlacement> bagPlacement,
        bool blocked, bool incoming)
    {
        if (!FullyOpen || View is null)
        {
            _grid.Clear();
            return default;
        }

        var grid = HudStoreLayout.Grid(Origin);
        _grid.Hover(grid, pointer, !incoming && !blocked);
        if (blocked || !HudStorageGrid.Pressed(mouse, previous))
        {
            return default;
        }

        if (_grid.Carried is not null)
        {
            return _grid.SetDown(grid, pointer, bagPlacement);
        }

        if (incoming)
        {
            return Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Consumed) : default;
        }

        if (HudStoreLayout.Exit(Origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Exit);
        }

        if (CaravanMode && CaravanClick(pointer) is { } caravanAction)
        {
            return caravanAction;
        }

        if (!CaravanMode && HudStoreLayout.WarehouseGold(Origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.WithdrawGold);
        }

        if (_grid.Lift() is { } lifted)
        {
            return lifted;
        }

        return Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Consumed) : default;
    }

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer, OwnedItemView incoming, bool interactive)
    {
        var origin = Origin;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        if (!CaravanMode)
        {
            var title = HudStoreLayout.WarehouseTitle(origin);
            painter.Sprite(HudNpcArt.WarehouseTitle, title.X, title.Y);
        }

        if (View is null)
        {
            return;
        }

        _grid.Draw(painter, icons, HudStoreLayout.Grid(origin), pointer, incoming, interactive);
        if (CaravanMode)
        {
            DrawCaravan(painter, origin, pointer);
            return;
        }

        var plate = HudStoreLayout.GoldPlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
        var weight = HudStoreLayout.WeightIcon(origin);
        painter.Sprite(HudNpcArt.Weight, weight.X, weight.Y);
        var upper = HudStoreLayout.UpperBar(origin);
        painter.TextCentered(_money, upper.Left, upper.Right, upper.Y + 3);
        var lower = HudStoreLayout.LowerBar(origin);
        painter.TextCentered(_weight, lower.Left + 18, lower.Right, lower.Y + 3);
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer, OwnerState state)
    {
        if (!_grid.DrawOverlay(painter, layout, icons, pointer, state) && CaravanMode && FullyOpen)
        {
            DrawCaravanHint(painter, layout, pointer);
        }
    }

    private void Texts()
    {
        if (View is not null)
        {
            _money = View.Money.ToString("N0");
            _weight = $"{View.Weight}/{View.MaxWeight}";
        }
    }
}
