using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.PersonalShops;

// How an offer is paid; carried on the wire and stored with stalls, so values are never renumbered. A view-only item shows
// in the shop but never sells; its price is 0.
public enum PersonalShopCurrency : byte
{
    Gold = 0,
    Coins = 1,
    ViewOnly = 2
}

// One item a seller offers: an item of its bag, the price of one unit (a stack sells by the unit), the store grid cell the
// item shows at, and what it is paid in.
public readonly record struct PersonalShopEntry(Guid ItemId, long UnitPrice, int Cell, PersonalShopCurrency Currency = PersonalShopCurrency.Gold);

// A seller's open shop: its sign and what it offers. The items stay in the seller's bag until sold; an entry whose item
// left the bag is dropped, and a shop with no entry left closes.
public sealed class PersonalShop
{
    private static long _lastSequence;
    private readonly List<PersonalShopEntry> _entries;

    internal PersonalShop(string sign, List<PersonalShopEntry> entries)
    {
        Sign = sign;
        _entries = entries;
    }

    // Rises with each shop opened, for the Marketplace's newest-first order; a stall's remaining listing keeps its own.
    public long Sequence { get; internal set; } = Interlocked.Increment(ref _lastSequence);

    public string Sign { get; }

    public IReadOnlyList<PersonalShopEntry> Entries => _entries;

    public bool IsEmpty => _entries.Count == 0;

    public PersonalShopEntry? Find(Guid itemId)
    {
        foreach (var entry in _entries)
        {
            if (entry.ItemId == itemId)
            {
                return entry;
            }
        }

        return null;
    }

    // Drops the entries whose item is no longer in the bag.
    public void Prune(Inventory bag) => _entries.RemoveAll(entry => bag.Find(entry.ItemId) < 0);
}
