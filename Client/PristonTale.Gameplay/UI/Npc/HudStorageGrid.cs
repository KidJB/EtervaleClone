using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// A warehouse's grid where a window puts it: one page of the store grid at a time (StoreGrid's 9x9; Shown holds that page's
// items at their cells on it, an anchor sent back adds the page's first cell). Items are carried as in the bag: a click
// picks one up and the next sets it down on the grid, on this page or another, or in the bag (exchanged with the one item
// it covers there).
public sealed class HudStorageGrid
{
    private static readonly Func<OwnedItemView, bool> OnGrid = static _ => true;
    private readonly HudItemCounts _counts = new();
    private OwnedItemView _hovered;
    private OwnedItemView _carried;
    private int _carriedPage;
    // The item an exchange leaves on the grid, carried once the reply's view shows it.
    private Guid _carryAfter;
    private IReadOnlyList<OwnedItemView> _shown = [];
    private OwnedItemView _described;
    private List<(string Text, Color Color)> _description;

    public WarehouseView View { get; private set; }
    public int Page { get; private set; }
    public int PageCount => View is null ? 1 : Math.Max(1, View.Rows / StoreGrid.Rows);
    public Guid Dragging => _carried?.InstanceId ?? Guid.Empty;
    public OwnedItemView Carried => _carried;
    public OwnedItemView Hovered => _hovered;

    public void Show(WarehouseView view)
    {
        Page = 0;
        _carried = null;
        _carryAfter = Guid.Empty;
        Refresh(view);
    }

    public void Refresh(WarehouseView view)
    {
        View = view ?? View;
        if (View is null)
        {
            return;
        }

        Page = Math.Clamp(Page, 0, PageCount - 1);
        _shown = PageItems(View, Page);
        _carried = Localized(OwnedItemView.Find(View.Items, _carryAfter != Guid.Empty ? _carryAfter : Dragging));
        _carryAfter = Guid.Empty;
        _counts.Refresh(View.Revision, View.Items);
    }

    // Another page shown; an item carried stays carried.
    public void Turn(int step)
    {
        Page = Math.Clamp(Page + step, 0, PageCount - 1);
        if (View is not null)
        {
            _shown = PageItems(View, Page);
        }
    }

    public void CarryAfter(Guid itemId) => _carryAfter = itemId;

    public void Clear()
    {
        _carried = null;
        _carryAfter = Guid.Empty;
        _hovered = null;
    }

    public HudPlacement Placement(Rectangle grid, OwnedItemView carried, Point pointer) =>
        View is not null ? Placement(grid, _shown, carried, pointer, carried == _carried && _carriedPage != Page) : default;

    // Where an item carried over the grid lands; what the warehouse keeps out is refused on the cells it would take,
    // except what it refuses only when set down (WarehouseStorage.RefusedOnSetDown). fromOtherPage: the carried item lies
    // on another page, so none of the shown cells are its own.
    public static HudPlacement Placement(Rectangle grid, IReadOnlyList<OwnedItemView> stored, OwnedItemView carried, Point pointer, bool fromOtherPage = false)
    {
        var placed = fromOtherPage ? carried with { InstanceId = Guid.Empty } : carried;
        if (HudGridPlacement.Of(grid, StoreGrid.Columns, StoreGrid.Rows, stored, OnGrid, placed, pointer) is not { } placement)
        {
            return default;
        }

        return WarehouseStorage.RefusedOnSetDown(carried.Kind) || WarehouseStorage.CanStore(carried.Kind, carried.CatalogKey)
            ? placement
            : new HudPlacement(HudPlacementKind.Blocked, HudGridPlacement.Cell(grid, StoreGrid.Columns, placement.Anchor, carried.Width, carried.Height),
                placement.Anchor);
    }

    // A click setting an item down over the grid, the stored item carried or a bag item: Move or Deposit at its cell
    // (Lifted when a stored item goes back where it was), Refused with the original's message, Consumed when it stays
    // carried; None off the grid.
    public HudWarehouseAction SetDownOnGrid(Rectangle grid, OwnedItemView carried, Point pointer)
    {
        var placement = Placement(grid, carried, pointer);
        if (placement.Kind == HudPlacementKind.None)
        {
            return default;
        }

        if (WarehouseStorage.RefusedOnSetDown(carried.Kind))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Refused, Message: ClientText.Get(GameTextId.PotionNotStored));
        }

        if (!placement.SetsDown)
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        var stored = carried == _carried;
        if (stored)
        {
            _carried = null;
        }

        return stored && placement.Kind == HudPlacementKind.Fits && placement.Anchor == carried.Anchor && _carriedPage == Page
            ? new HudWarehouseAction(HudWarehouseActionKind.Lifted, carried.InstanceId)
            : new HudWarehouseAction(stored ? HudWarehouseActionKind.Move : HudWarehouseActionKind.Deposit, carried.InstanceId,
                Page * StoreGrid.Cells + placement.Anchor, placement.Covered?.InstanceId ?? Guid.Empty);
    }

    // The item under the pointer, for the hover and the next click; none while one is carried or the grid is blocked.
    public void Hover(Rectangle grid, Point pointer, bool allowed) =>
        _hovered = allowed && _carried is null && View is not null ? HudGridPlacement.ItemAt(grid, StoreGrid.Columns, _shown, OnGrid, pointer) : null;

    // A click on the grid while nothing is carried: the hovered item is picked up.
    public HudWarehouseAction? Lift()
    {
        if (_hovered is not { } item)
        {
            return null;
        }

        _carried = item;
        _carriedPage = Page;
        _hovered = null;
        return new HudWarehouseAction(HudWarehouseActionKind.Lifted, item.InstanceId);
    }

    // The carried item set down on the grid, else in the bag where bagPlacement puts it.
    public HudWarehouseAction SetDown(Rectangle grid, Point pointer, Func<OwnedItemView, Point, HudPlacement> bagPlacement)
    {
        var carried = _carried;
        if (SetDownOnGrid(grid, carried, pointer) is { Kind: not HudWarehouseActionKind.None } action)
        {
            return action;
        }

        var placement = bagPlacement(carried, pointer);
        if (!placement.SetsDown)
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        _carried = null;
        return new HudWarehouseAction(HudWarehouseActionKind.Withdraw, carried.InstanceId, placement.Anchor, placement.Covered?.InstanceId ?? Guid.Empty);
    }

    // The page's items, the hover and where the carried (or incoming bag) item would land.
    public void Draw(HudPainter painter, HudItemIcons icons, Rectangle grid, Point pointer, OwnedItemView incoming, bool interactive)
    {
        if (View is null)
        {
            return;
        }

        if (interactive && _hovered is not null)
        {
            painter.Fill(Cell(grid, _hovered), HudPlacementColors.Selection);
        }

        if ((_carried ?? incoming) is { } moving && interactive)
        {
            HudPlacementColors.Draw(painter, Placement(grid, moving, pointer));
        }

        for (var index = 0; index < _shown.Count; index++)
        {
            var item = _shown[index];
            _counts.Draw(painter, icons, item, Cell(grid, item), item == _carried && _carriedPage == Page ? HudPlacementColors.Carried : Color.White);
        }
    }

    // The carried item under the pointer or the hovered one's values, as the bag shows them; false when there is neither.
    public bool DrawOverlay(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer, OwnerState state)
    {
        if (_carried is not null)
        {
            _counts.Draw(painter, icons, _carried, HudGridPlacement.Frame(_carried, pointer), Color.White);
            return true;
        }

        if (_hovered is { } item)
        {
            if (!ReferenceEquals(item, _described))
            {
                (_described, _description) = (item, HudItemDetailTooltip.Lines(_counts.Named(item), state, null, ClientText.Get("Inventory.HintCarry")));
            }

            HudItemDetailTooltip.Draw(painter, layout, pointer, _description, null, HudItemDetailTooltip.RarityOf(item));
            return true;
        }

        return false;
    }

    public static bool Pressed(MouseState mouse, MouseState previous) => mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;

    private static Rectangle Cell(Rectangle grid, OwnedItemView item) => HudGridPlacement.Cell(grid, StoreGrid.Columns, item.Anchor, item.Width, item.Height);

    // The page's items at their cells on it.
    private static IReadOnlyList<OwnedItemView> PageItems(WarehouseView view, int page)
    {
        if (view.Rows <= StoreGrid.Rows)
        {
            return view.Items;
        }

        var items = new List<OwnedItemView>();
        foreach (var item in view.Items)
        {
            if (item.Anchor / StoreGrid.Cells == page)
            {
                items.Add(item with { Anchor = item.Anchor % StoreGrid.Cells });
            }
        }

        return items;
    }

    // The carried item at its cell on its own page, which _carriedPage keeps.
    private OwnedItemView Localized(OwnedItemView item)
    {
        if (item is null)
        {
            return null;
        }

        _carriedPage = item.Anchor / StoreGrid.Cells;
        return item with { Anchor = item.Anchor % StoreGrid.Cells };
    }
}
