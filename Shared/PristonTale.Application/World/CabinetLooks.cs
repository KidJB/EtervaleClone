using System.Globalization;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// One look of The Cabinet with the rule data/cabinet.json gives it (or the defaults): HasItem when a world item wears it,
// so a brush can unlock it from that item.
public sealed record CabinetLook(string Key, string Name, SkinSlot Slot, int Level, int Price, CosmeticCategory Category, ItemRarity Rarity,
    CosmeticSource Sources, bool Enabled, string Description, string Token, bool HasItem)
{
    public bool Purchasable => Enabled && CabinetRules.Purchasable(Sources);

    public bool Brushable => Enabled && HasItem && (Sources & CosmeticSource.Brush) != 0;
}

// Every look The Cabinet holds: the numbered class heads and hair tints, each listed worn item (SkinRules), the gear-only
// looks and the costumes (Costumes.json), with their rules applied. One place answers both the whole list and a single key.
public static class CabinetLooks
{
    public static List<CabinetLook> All(ItemCatalog items, CabinetConfig config)
    {
        var looks = new List<CabinetLook>();
        foreach (var number in Costumes.Hair)
        {
            looks.Add(Make(SkinRules.HeadKey(number), string.Create(CultureInfo.InvariantCulture, $"KPT Hair {number}"), SkinSlot.Hair, 0, SkinRules.HairPrice,
                false, false, config));
        }

        foreach (var item in items.All)
        {
            if (OfItem(item, config) is { } look)
            {
                looks.Add(look);
            }
        }

        foreach (var gear in Costumes.Gear)
        {
            if (!Costumes.Hidden.Contains(gear.Key))
            {
                looks.Add(Make(gear.Key, gear.Name, (SkinSlot)gear.Slot, 0, SkinRules.GearPrice, false, true, config));
            }
        }

        foreach (var costume in Costumes.All)
        {
            looks.Add(Make(costume.Key, costume.Name, SkinSlot.Armor, 0, SkinRules.CostumePrice, false, false, config));
        }

        return looks;
    }

    public static CabinetLook? Find(string key, ItemCatalog items, CabinetConfig config)
    {
        if (key is not { Length: > 0 })
        {
            return null;
        }

        if (Costumes.Find(key) is { } costume)
        {
            return Make(costume.Key, costume.Name, SkinSlot.Armor, 0, SkinRules.CostumePrice, false, false, config);
        }

        if (Costumes.FindGear(key) is { } gear)
        {
            return Costumes.Hidden.Contains(gear.Key) ? null : Make(gear.Key, gear.Name, (SkinSlot)gear.Slot, 0, SkinRules.GearPrice, false, true, config);
        }

        // A numbered class head has a key but no world item.
        return ItemCodes.TryParse(key, out var code) && items.TryGet(code, out var item) ? OfItem(item, config)
            : SkinRules.HairOf(key) is not null ? Make(key.ToUpperInvariant(), key, SkinSlot.Hair, 0, SkinRules.HairPrice, false, false, config)
            : null;
    }

    // The look a world item wears, or null when The Cabinet does not hold it (hidden, timed copy, not gear).
    public static CabinetLook? OfItem(ItemDefinition item, CabinetConfig config)
    {
        if (Costumes.Hidden.Contains(item.CatalogKey) || Costumes.FindGear(item.CatalogKey) is not null || Costumes.Find(item.CatalogKey) is not null)
        {
            return null;
        }

        if (SkinRules.HairOf(item.CatalogKey) is not null)
        {
            return Make(item.CatalogKey, item.Name, SkinSlot.Hair, 0, SkinRules.HairPrice, false, false, config);
        }

        return SkinRules.SlotOf(item) is { } slot && SkinRules.Listed(item)
            ? Make(item.CatalogKey, SkinRules.DisplayName(item.Name), slot, item.RequiredLevel, SkinRules.Price(item), true, false, config)
            : null;
    }

    private static CabinetLook Make(string key, string name, SkinSlot slot, int level, int price, bool hasItem, bool gearOnly, CabinetConfig config)
    {
        var category = CabinetRules.Category(key, slot);
        var rule = config.Rule(key);
        return new CabinetLook(key, name, slot, level, rule is { Price: > 0 } ? rule.Price : price, category,
            rule is { Rarity: not ItemRarity.None } ? rule.Rarity : CabinetRules.DefaultRarity(category, level, gearOnly),
            rule is { Sources: not CosmeticSource.None } ? rule.Sources : CabinetRules.DefaultSources(hasItem),
            rule?.Enabled ?? true, rule?.Description ?? "", rule?.Token ?? "", hasItem);
    }
}
