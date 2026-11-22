using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.World.Npcs;

// Storage kept by warehouse NPCs: pages of the store grid (StoreGrid), the pages stacked one under the other so an anchor
// is page * StoreGrid.Cells + cell and an item never spans two pages. The account warehouse is one page of at most 100
// items and 1000 weight; a clan's (Social.Clans.ClanWarehouse) has more pages and higher limits.
public sealed class WarehouseStorage
{
    public const int MaxItems = 100;
    public const int MaxWeight = 1000;
    public const long MaxMoney = 100_000_000;

    private readonly Inventory[] _pages;

    public WarehouseStorage(long revision = 0, long money = 0, int pages = 1, int itemLimit = MaxItems, int weightLimit = MaxWeight)
    {
        if (revision < 0 || money < 0 || money > MaxMoney || pages < 1 || itemLimit < 1 || weightLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        Revision = revision;
        Money = money;
        ItemLimit = itemLimit;
        WeightLimit = weightLimit;
        _pages = new Inventory[pages];
        for (var page = 0; page < pages; page++)
        {
            _pages[page] = StoreGrid.Create();
        }
    }

    // The first page: all of the account warehouse.
    public Inventory Grid => _pages[0];
    public int Pages => _pages.Length;
    public int ItemLimit { get; }
    public int WeightLimit { get; }
    public long Money { get; private set; }
    public long Revision { get; private set; }

    public Inventory Page(int page) => _pages[page];

    public int Weight
    {
        get
        {
            var weight = 0;
            foreach (var page in _pages)
            {
                foreach (var item in page.Items())
                {
                    weight += CarriedWeight.Of(item);
                }
            }

            return weight;
        }
    }

    public int Count
    {
        get
        {
            var count = 0;
            foreach (var page in _pages)
            {
                count += page.Items().Count();
            }

            return count;
        }
    }

    // Every stored item with its anchor over all pages, in page and cell order.
    public IEnumerable<(int Anchor, ItemInstance Item)> Occupied()
    {
        for (var page = 0; page < _pages.Length; page++)
        {
            foreach (var (anchor, item) in _pages[page].Occupied())
            {
                yield return (page * StoreGrid.Cells + anchor, item);
            }
        }
    }

    public ItemInstance Find(Guid itemId) => FindAt(itemId, out var page, out var cell) ? _pages[page][cell] : null;

    // What BoundItems.CanHandOver lets go, potions apart.
    public static bool CanStore(PlayerCharacter character, ItemInstance item) =>
        BoundItems.CanHandOver(character, item) && !RefusedOnSetDown(item.Definition.Kind);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool CanStore(ItemKind kind, string key) => BoundItems.CanHandOver(kind, key) && !RefusedOnSetDown(kind);

    // The original places a potion over the grid as any item and refuses it only when it is set down, with
    // GameTextId.PotionNotStored; the other items CanStore refuses show red where they would land.
    public static bool RefusedOnSetDown(ItemKind kind) => kind == ItemKind.Potion;

    public WarehouseStorage Clone()
    {
        var clone = new WarehouseStorage(Revision, Money, _pages.Length, ItemLimit, WeightLimit);
        for (var page = 0; page < _pages.Length; page++)
        {
            clone._pages[page].CopyFrom(_pages[page]);
        }

        return clone;
    }

    public bool Restore(ItemInstance item, int anchor) =>
        Count < ItemLimit && Split(anchor, out var page, out var cell) && _pages[page].TryPlace(item, cell);

    // An item set down on the grid, as the original sets it: on free cells (-1: the first that fit, page by page), or over
    // one stored item, which goes to the item's bag cell (or wherever the bag has room) and is carried in turn. covered is
    // the item the client saw under it, or empty.
    public NpcServiceStatus TryDeposit(PlayerCharacter character, Guid itemId, int anchor, Guid covered = default)
    {
        var source = character.Inventory.Find(itemId);
        if (source < 0 || character.Inventory[source] is not { } item || !CanStore(character, item) || !Target(anchor, out var grid, out var cell)
            || !TryCovered(grid, cell, item, covered, out var displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        if (displaced is null && Count >= ItemLimit)
        {
            return NpcServiceStatus.NoSpace;
        }

        if (Weigh(character, item, displaced) is var weighed && weighed != NpcServiceStatus.Ok)
        {
            return weighed;
        }

        if (anchor < 0)
        {
            return DepositAnywhere(character.Inventory, source) ? Changed() : NpcServiceStatus.NoSpace;
        }

        return SetDown(character.Inventory, source, grid, cell, displaced) ? Changed() : NpcServiceStatus.NoSpace;
    }

    // A stored item set down in the bag: on free cells, or over one bag item, which goes to the stored item's cell (or
    // wherever its page has room) and is carried in turn.
    public NpcServiceStatus TryWithdraw(PlayerCharacter character, Guid itemId, int anchor, Guid covered = default)
    {
        if (!FindAt(itemId, out var page, out var source) || _pages[page][source] is not { } item
            || !TryCovered(character.Inventory, anchor, item, covered, out var displaced) || displaced is not null && !CanStore(character, displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        return Weigh(character, displaced, item) is var weighed && weighed != NpcServiceStatus.Ok ? weighed
            : SetDown(_pages[page], source, character.Inventory, anchor, displaced) ? Changed() : NpcServiceStatus.NoSpace;
    }

    // A stored item set down elsewhere, on its page or another, over free cells or one other stored item (carried in turn).
    public NpcServiceStatus TryMove(Guid itemId, int anchor, Guid covered = default)
    {
        if (!FindAt(itemId, out var page, out var source) || anchor < 0 || _pages[page][source] is not { } item
            || !CanStore(item.Definition.Kind, item.Definition.CatalogKey) || !Split(anchor, out var targetPage, out var cell)
            || !TryCovered(_pages[targetPage], cell, item, covered, out var displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        var from = _pages[page];
        if (targetPage != page)
        {
            return SetDown(from, source, _pages[targetPage], cell, displaced) ? Changed() : NpcServiceStatus.Rejected;
        }

        return (displaced is null ? from.TryMove(source, cell) : from.TrySwap(source, cell)) ? Changed() : NpcServiceStatus.Rejected;
    }

    public NpcServiceStatus TryDepositGold(PlayerCharacter character, long amount)
    {
        if (amount <= 0 || amount > MaxMoney - Money)
        {
            return amount <= 0 ? NpcServiceStatus.Rejected : NpcServiceStatus.GoldLimit;
        }

        if (!character.TryChangeMoney(-amount))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        Money += amount;
        Revision++;
        character.TouchInventory();
        return NpcServiceStatus.Ok;
    }

    public NpcServiceStatus TryWithdrawGold(PlayerCharacter character, long amount)
    {
        if (amount <= 0 || amount > Money)
        {
            return amount <= 0 ? NpcServiceStatus.Rejected : NpcServiceStatus.NotEnoughGold;
        }

        if (!MoneyLimits.CanCarry(character, amount) || !character.TryChangeMoney(amount))
        {
            return NpcServiceStatus.GoldLimit;
        }

        Money -= amount;
        Revision++;
        character.TouchInventory();
        return NpcServiceStatus.Ok;
    }

    // Whether the warehouse can bear `stored` coming in and the character `taken` coming out to it (either may be null);
    // a side whose load falls is never refused, even over its limit.
    private NpcServiceStatus Weigh(PlayerCharacter character, ItemInstance stored, ItemInstance taken)
    {
        var change = (stored is null ? 0 : CarriedWeight.Of(stored)) - (taken is null ? 0 : CarriedWeight.Of(taken));
        if (change > 0 && Weight + change > WeightLimit)
        {
            return NpcServiceStatus.NoSpace;
        }

        return change < 0 && !CarriedWeight.CanTake(character, -change) ? NpcServiceStatus.OverWeight : NpcServiceStatus.Ok;
    }

    private NpcServiceStatus Changed()
    {
        Revision++;
        return NpcServiceStatus.Ok;
    }

    // An anchor's page and cell; false outside the pages.
    private bool Split(int anchor, out int page, out int cell)
    {
        page = anchor / StoreGrid.Cells;
        cell = anchor % StoreGrid.Cells;
        return anchor >= 0 && page < _pages.Length;
    }

    // The grid an item is set down on: the anchor's page, or the first page for -1 (the first free cells).
    private bool Target(int anchor, out Inventory grid, out int cell)
    {
        if (anchor < 0)
        {
            (grid, cell) = (_pages[0], -1);
            return true;
        }

        var found = Split(anchor, out var page, out cell);
        grid = found ? _pages[page] : null;
        return found;
    }

    private bool FindAt(Guid itemId, out int page, out int cell)
    {
        for (page = 0; page < _pages.Length; page++)
        {
            cell = _pages[page].Find(itemId);
            if (cell >= 0)
            {
                return true;
            }
        }

        (page, cell) = (-1, -1);
        return false;
    }

    private bool DepositAnywhere(Inventory from, int source)
    {
        foreach (var page in _pages)
        {
            if (SetDown(from, source, page, -1, null))
            {
                return true;
            }
        }

        return false;
    }

    // The one item of `to` that `item` set down at anchor lies over (null on free cells). False when it lies over more,
    // or over another item than the client saw (its view was stale). Anchor -1 (the first free cells) covers nothing.
    private static bool TryCovered(Inventory to, int anchor, ItemInstance item, Guid expected, out ItemInstance displaced)
    {
        displaced = null;
        if (anchor < 0)
        {
            return expected == Guid.Empty;
        }

        var under = to.Covered(anchor, item.Definition, to.Find(item.InstanceId));
        if (under == Inventory.Several)
        {
            return false;
        }

        displaced = under >= 0 ? to[under] : null;
        return (displaced?.InstanceId ?? Guid.Empty) == expected;
    }

    // The item at source of `from` set down at anchor of `to`; the displaced item of `to` goes to the source cell or
    // wherever `from` has room. Both sides are checked before either changes.
    private static bool SetDown(Inventory from, int source, Inventory to, int anchor, ItemInstance displaced)
    {
        var item = from[source];
        if (displaced is null)
        {
            return (anchor >= 0 ? to.TryPlace(item, anchor) : to.TryAdd(item)) && from.TakeAt(source) is not null;
        }

        var under = to.Find(displaced.InstanceId);
        if (!to.CanExchange(under, item, anchor) || !from.CanExchange(source, displaced))
        {
            return false;
        }

        return to.TryExchange(under, item, anchor) && from.TryExchange(source, displaced);
    }

    // Each item with its values for the viewer (ItemDetailView), as the bag shows them.
    public WarehouseView View(PlayerCharacter viewer)
    {
        var items = Occupied().Select(cell => OwnedItemView.Of(cell.Item, cell.Anchor, -1) with { Details = ItemDetailView.Of(cell.Item, viewer) }).ToArray();
        return new WarehouseView(Revision, Money, Weight, WeightLimit, StoreGrid.Columns, StoreGrid.Rows * _pages.Length, items);
    }
}
