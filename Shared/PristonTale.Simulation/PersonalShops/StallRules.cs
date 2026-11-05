using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.PersonalShops;

// An offline stall: a personal shop left standing after its seller walks on. Its listed items leave the seller's bag for the
// stall's own stock (a store grid, each at its cell); a sale moves units from the stock to the buyer and the price from the
// buyer to the stall's owner, whose gold must stay within what it may carry. An account keeps one stall.
public static class StallRules
{
    // The stock of a listing: the listed items taken out of the seller's bag (a candidate's) onto a store grid at their
    // cells. Null when an entry's item is gone or no longer fits.
    public static Inventory TakeStock(PlayerCharacter seller, PersonalShop shop)
    {
        var stock = StoreGrid.Create();
        foreach (var entry in shop.Entries)
        {
            var anchor = seller.Inventory.Find(entry.ItemId);
            if (anchor < 0 || !stock.TryPlace(seller.Inventory.TakeAt(anchor), entry.Cell))
            {
                return null;
            }
        }

        return stock;
    }

    // Moves Count units of the listed item from the stock to the buyer and its price from the buyer, on candidates. The
    // price and the units must be the ones the buyer was shown; ownerMoney and the owner's level and tier bound the gold.
    public static PersonalShopStatus TrySell(PersonalShop shop, Inventory stock, PlayerCharacter buyer, PersonalShopPurchase purchase, int ownerLevel,
        int ownerTier, long ownerMoney, out long price)
    {
        price = 0;
        var anchor = stock.Find(purchase.ItemId);
        if (shop.Find(purchase.ItemId) is not { } entry || anchor < 0)
        {
            return PersonalShopStatus.SoldOut;
        }

        var item = stock[anchor];
        if (entry.UnitPrice != purchase.UnitPrice)
        {
            return PersonalShopStatus.PriceChanged;
        }

        if (!NpcShopRules.IsValidCount(item.Definition, purchase.Count))
        {
            return PersonalShopStatus.Rejected;
        }

        if (purchase.Count > item.Count)
        {
            return PersonalShopStatus.CountChanged;
        }

        price = entry.UnitPrice * purchase.Count;
        if (price > PersonalShopRules.MaxPrice)
        {
            return PersonalShopStatus.Rejected;
        }

        if (buyer.Money < price)
        {
            return PersonalShopStatus.NotEnoughGold;
        }

        if (PersonalShopRules.GoldRoom(ownerLevel, ownerTier, ownerMoney, price) < 0)
        {
            return PersonalShopStatus.GoldLimit;
        }

        if (!CarriedWeight.CanTake(buyer, CarriedWeight.Of(item.Definition, purchase.Count)))
        {
            return PersonalShopStatus.OverWeight;
        }

        if (!buyer.Inventory.TryAdd(stock.TakeFrom(anchor, purchase.Count)))
        {
            return PersonalShopStatus.NoSpace;
        }

        return buyer.TryChangeMoney(-price) ? PersonalShopStatus.Ok : PersonalShopStatus.Rejected;
    }

    // A stall's listing restored from storage.
    public static PersonalShop Listing(string sign, IEnumerable<PersonalShopEntry> entries) => new(sign, [.. entries]);

    // The listing with only the entries whose item the stock still holds.
    public static PersonalShop Remaining(PersonalShop shop, Inventory stock) =>
        new(shop.Sign, [.. shop.Entries.Where(entry => stock.Find(entry.ItemId) >= 0)]);
}

public static class StallMessages
{
    public const string Placed = "Your stall is set up. It keeps selling while you play; /stall close closes it from anywhere.";
    public const string Closed = "Your stall is closed. Its unsold items wait in the Item Distributor.";
    public const string NoShop = "Open a personal shop first, then type /stall to leave it as a stall.";
    public const string AlreadyOne = "Your account already has a stall. Close it with /stall close first.";
    public const string NoStall = "Your account has no stall.";
    public const string OwnStall = "You cannot buy from your own account's stall.";
    public const string Failed = "The stall could not be stored; nothing changed.";

    public static string Sold(string buyer, int count, string item, long price) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Your stall sold {count} {item} to {buyer} for {price:N0} Gold");
}
