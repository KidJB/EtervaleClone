using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Markets;

public enum MarketplaceCategory : byte
{
    All = 0,
    Defense = 1,
    Weapons = 2,
    Accessory = 3,
    Craft = 4,
    Miscellaneous = 5,
    Costumes = 6
}

public enum MarketplacePageStatus : byte
{
    Ok = 0,
    Disabled = 1,
    OutOfReach = 2,
    Rejected = 3
}

public enum MarketplaceSort : byte
{
    Newest = 0,
    PriceLow = 1,
    PriceHigh = 2,
    Name = 3
}

// A kind of goods under a category. Subcategory numbers are the positions in MarketplaceRules.Subcategories(parent), from 1.
public sealed record MarketplaceSubcategory(string Name, MarketplaceCategory Parent, ItemKind[] Kinds);

// A search of the live shops. A null Class and Rarity None are any; MaxPrice 0 has no ceiling.
public sealed record MarketplaceQuery(int Page, MarketplaceCategory Category, int Subcategory, PlayerClass? Class, ItemRarity Rarity, long MinPrice, long MaxPrice,
    MarketplaceSort Sort, string Item, string Seller);

public static class MarketplaceRules
{
    public const int PageSize = 6;
    public const int MaxSearchLength = 24;

    public static readonly IReadOnlyList<MarketplaceSubcategory> AllSubcategories =
    [
        new("Armor", MarketplaceCategory.Defense, [ItemKind.Armor]),
        new("Shields", MarketplaceCategory.Defense, [ItemKind.Shield]),
        new("Gauntlets", MarketplaceCategory.Defense, [ItemKind.Gloves]),
        new("Boots", MarketplaceCategory.Defense, [ItemKind.Boots]),
        new("Swords", MarketplaceCategory.Weapons, [ItemKind.Sword]),
        new("Axes", MarketplaceCategory.Weapons, [ItemKind.Axe]),
        new("Hammers", MarketplaceCategory.Weapons, [ItemKind.Hammer]),
        new("Claws", MarketplaceCategory.Weapons, [ItemKind.Claw]),
        new("Scythes", MarketplaceCategory.Weapons, [ItemKind.Pole]),
        new("Bows", MarketplaceCategory.Weapons, [ItemKind.Bow]),
        new("Javelins", MarketplaceCategory.Weapons, [ItemKind.ThrowingWeapon]),
        new("Wands", MarketplaceCategory.Weapons, [ItemKind.Staff]),
        new("Daggers", MarketplaceCategory.Weapons, [ItemKind.Dagger]),
        new("Phantoms", MarketplaceCategory.Weapons, [ItemKind.Phantom]),
        new("Amulets", MarketplaceCategory.Accessory, [ItemKind.Amulet]),
        new("Rings", MarketplaceCategory.Accessory, [ItemKind.Ring]),
        new("Bracelets", MarketplaceCategory.Accessory, [ItemKind.Bracelet]),
        new("Orbs", MarketplaceCategory.Accessory, [ItemKind.Orb]),
        new("Vambraces", MarketplaceCategory.Accessory, [ItemKind.Vambrace]),
        new("Sheltoms", MarketplaceCategory.Craft, [ItemKind.Sheltom]),
        new("Forces", MarketplaceCategory.Craft, [ItemKind.ForceOrb]),
        new("Materials", MarketplaceCategory.Craft, [ItemKind.Material]),
        new("Potions", MarketplaceCategory.Miscellaneous, [ItemKind.Potion]),
        new("Quest", MarketplaceCategory.Miscellaneous, [ItemKind.Quest]),
        new("Other", MarketplaceCategory.Miscellaneous, [ItemKind.Misc])
    ];

    private static readonly Dictionary<MarketplaceCategory, MarketplaceSubcategory[]> ByParent =
        AllSubcategories.GroupBy(static entry => entry.Parent).ToDictionary(static group => group.Key, static group => group.ToArray());

    public static IReadOnlyList<MarketplaceSubcategory> Subcategories(MarketplaceCategory parent) => ByParent.TryGetValue(parent, out var entries) ? entries : [];

    public static MarketplaceCategory CategoryOf(ItemDefinition definition)
    {
        if (Costumes.Find(definition.CatalogKey) is not null)
        {
            return MarketplaceCategory.Costumes;
        }

        foreach (var entry in AllSubcategories)
        {
            if (Array.IndexOf(entry.Kinds, definition.Kind) >= 0)
            {
                return entry.Parent;
            }
        }

        return MarketplaceCategory.Miscellaneous;
    }

    public static bool Matches(MarketplaceQuery query, ItemInstance item, long unitPrice, string seller)
    {
        var definition = item.Definition;
        if (query.Category != MarketplaceCategory.All && CategoryOf(definition) != query.Category)
        {
            return false;
        }

        if (query.Subcategory > 0 && !InSubcategory(query, definition))
        {
            return false;
        }

        if (query.Class is { } wanted && !FitsClass(item, (uint)JobBits.Of(wanted)))
        {
            return false;
        }

        return (query.Rarity == ItemRarity.None || item.Rarity == query.Rarity)
            && unitPrice >= query.MinPrice
            && (query.MaxPrice <= 0 || unitPrice <= query.MaxPrice)
            && Contains(item.DisplayName, query.Item)
            && Contains(seller, query.Seller);
    }

    public static int Pages(int total) => total <= 0 ? 1 : (total + PageSize - 1) / PageSize;

    private static bool InSubcategory(MarketplaceQuery query, ItemDefinition definition)
    {
        var index = 0;
        foreach (var entry in AllSubcategories)
        {
            if (entry.Parent == query.Category && ++index == query.Subcategory)
            {
                return Array.IndexOf(entry.Kinds, definition.Kind) >= 0;
            }
        }

        return false;
    }

    // A piece specialized for classes fits those; any other fits the classes that may use it.
    private static bool FitsClass(ItemInstance item, uint job)
    {
        if (item.JobSpecMask != 0)
        {
            return (item.JobSpecMask & job) != 0;
        }

        return item.Definition.AllowedJobs == 0 || (item.Definition.AllowedJobs & job) != 0;
    }

    private static bool Contains(string text, string search) => search.Length == 0 || text.Contains(search, StringComparison.OrdinalIgnoreCase);
}
