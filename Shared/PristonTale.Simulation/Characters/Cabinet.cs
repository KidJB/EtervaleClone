using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// How a look of The Cabinet may be unlocked; a look lists every way it is offered.
[Flags]
public enum CosmeticSource : byte
{
    None = 0,
    Coins = 1,
    MonsterDrop = 2,
    BossDrop = 4,
    Quest = 8,
    Event = 16,
    Brush = 32,
    Deal = 64
}

public enum CosmeticCategory : byte
{
    Armor = 0,
    Robe = 1,
    Costume = 2,
    Weapon = 3,
    Shield = 4,
    Hair = 5
}

public enum CabinetBrush : byte
{
    None = 0,
    ArmorRobe = 1,
    WeaponShield = 2
}

// One look's settings in data/cabinet.json. Unset fields keep the defaults of CabinetRules (Rarity None, Sources None,
// Price 0). Token names the item that unlocks the look when used in The Cabinet: CT1xx trades, CU1xx is bound.
public sealed record CosmeticRule(string Key, ItemRarity Rarity = ItemRarity.None, CosmeticSource Sources = CosmeticSource.None, int Price = 0,
    bool Enabled = true, string Description = "", string Token = "");

// A drop of a Cabinet item (a look's token or a brush): from one monster (its code), or from every boss, per ten thousand kills.
public sealed record CosmeticDrop(string Item, int Monster = 0, bool Bosses = false, int PerTenThousand = 0);

// The Cabinet's settings: per-look rules and the drops of its items.
public sealed record CabinetConfig(IReadOnlyList<CosmeticRule> Cosmetics, IReadOnlyList<CosmeticDrop> Drops)
{
    public static readonly CabinetConfig Empty = new([], []);

    // A config read from JSON may hold nulls; Normalized never does.
    public CabinetConfig Normalized() => new(Cosmetics ?? [], Drops ?? []);

    public CosmeticRule Rule(string key)
    {
        foreach (var rule in Cosmetics)
        {
            if (string.Equals(rule.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    public CosmeticRule RuleOfToken(string token)
    {
        foreach (var rule in Cosmetics)
        {
            if (rule.Token.Length > 0 && string.Equals(rule.Token, token, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    // Why a rule cannot be used, or null.
    public static string Problem(CosmeticRule rule)
    {
        if (rule.Key is not { Length: > 0 })
        {
            return "has no key";
        }

        if (rule.Price < 0)
        {
            return "has a negative price";
        }

        if (rule.Token is { Length: > 0 } token && !CabinetRules.IsToken(token))
        {
            return $"names token '{token}' outside the {CabinetRules.TokenFamily} and {CabinetRules.BoundTokenFamily} families";
        }

        return null;
    }

    public static string Problem(CosmeticDrop drop)
    {
        if (!CabinetRules.IsToken(drop.Item) && CabinetRules.BrushOfItem(drop.Item) == CabinetBrush.None)
        {
            return $"drops '{drop.Item}', which is neither a token nor a brush";
        }

        return drop.PerTenThousand is < 0 or > 10_000 ? "has a chance outside 0 to 10000" : drop.Monster == 0 && !drop.Bosses ? "names no monster" : null;
    }
}

// The rules of The Cabinet beside SkinRules: categories, default rarity and sources, the two brushes and the token items.
public static class CabinetRules
{
    public const string ArmorBrushKey = "CB101";
    public const string WeaponBrushKey = "CB102";
    public const string ArmorBrushName = "Armor & Robe Brush";
    public const string WeaponBrushName = "Weapon & Shield Brush";
    public const string TokenFamily = "CT1";
    public const string BoundTokenFamily = "CU1";
    public const string TokenSuffix = " Look";
    public const int BrushStack = 100;
    public const CosmeticSource DefaultItemSources = CosmeticSource.Coins | CosmeticSource.Brush;
    private const int EpicLevel = 100;
    private const int RareLevel = 60;
    private const int UncommonLevel = 30;

    public static CosmeticCategory Category(string key, SkinSlot slot) => slot switch
    {
        SkinSlot.Hair => CosmeticCategory.Hair,
        SkinSlot.OffHand => CosmeticCategory.Shield,
        SkinSlot.Weapon => CosmeticCategory.Weapon,
        _ => Costumes.Find(key) is { Kind: "" } || Costumes.CostumeItems.Contains(key) ? CosmeticCategory.Costume
            : Costumes.Find(key) is { Kind: "robe" } || SkinRules.IsRobe(key) ? CosmeticCategory.Robe : CosmeticCategory.Armor
    };

    public static CabinetBrush BrushFor(SkinSlot slot) => slot switch
    {
        SkinSlot.Armor => CabinetBrush.ArmorRobe,
        SkinSlot.Weapon or SkinSlot.OffHand => CabinetBrush.WeaponShield,
        _ => CabinetBrush.None
    };

    public static CabinetBrush BrushOfItem(string key) =>
        string.Equals(key, ArmorBrushKey, StringComparison.OrdinalIgnoreCase) ? CabinetBrush.ArmorRobe
        : string.Equals(key, WeaponBrushKey, StringComparison.OrdinalIgnoreCase) ? CabinetBrush.WeaponShield
        : CabinetBrush.None;

    public static string BrushKey(CabinetBrush brush) => brush == CabinetBrush.ArmorRobe ? ArmorBrushKey : brush == CabinetBrush.WeaponShield ? WeaponBrushKey : "";

    public static string BrushName(CabinetBrush brush) => brush == CabinetBrush.ArmorRobe ? ArmorBrushName : brush == CabinetBrush.WeaponShield ? WeaponBrushName : "";

    public static bool IsToken(string key) => ItemCodes.TryParse(key, out _) && ItemCodes.InFamily(key, [TokenFamily, BoundTokenFamily]);

    public static bool IsItem(string key) => IsToken(key) || BrushOfItem(key) != CabinetBrush.None;

    // Gear looks rise in rarity with the item's level; costumes and gear-only looks are epic, hair uncommon.
    public static ItemRarity DefaultRarity(CosmeticCategory category, int level, bool gearOnly) => category switch
    {
        CosmeticCategory.Costume => ItemRarity.Epic,
        CosmeticCategory.Hair => ItemRarity.Uncommon,
        _ when gearOnly => ItemRarity.Epic,
        _ => level >= EpicLevel ? ItemRarity.Epic : level >= RareLevel ? ItemRarity.Rare : level >= UncommonLevel ? ItemRarity.Uncommon : ItemRarity.Common
    };

    // A look of a world item may be bought or brushed from the item; a costume, hair or gear-only look has no item to brush.
    public static CosmeticSource DefaultSources(bool hasItem) => hasItem ? DefaultItemSources : CosmeticSource.Coins;

    public static bool Purchasable(CosmeticSource sources) => (sources & CosmeticSource.Coins) != 0;

    // Sold for coins nowhere: only found, earned, brushed or won.
    public static bool Exclusive(CosmeticSource sources) => !Purchasable(sources);

    public static IEnumerable<ItemDefinition> ItemDefinitions(CabinetConfig config, Func<string, string> nameOf)
    {
        yield return new ItemDefinition(ItemCodes.Parse(ArmorBrushKey), ArmorBrushName, ItemKind.Misc, MaxStack: BrushStack) { CatalogKey = ArmorBrushKey };
        yield return new ItemDefinition(ItemCodes.Parse(WeaponBrushKey), WeaponBrushName, ItemKind.Misc, MaxStack: BrushStack) { CatalogKey = WeaponBrushKey };
        foreach (var rule in config.Cosmetics)
        {
            if (rule.Token.Length > 0 && IsToken(rule.Token))
            {
                yield return new ItemDefinition(ItemCodes.Parse(rule.Token), nameOf(rule.Key) + TokenSuffix, ItemKind.Misc) { CatalogKey = rule.Token.ToUpperInvariant() };
            }
        }
    }

    public static string SourceText(CosmeticSource sources)
    {
        if (sources == CosmeticSource.None)
        {
            return "Not obtainable";
        }

        var parts = new List<string>();
        foreach (var (flag, text) in SourceNames)
        {
            if ((sources & flag) != 0)
            {
                parts.Add(text);
            }
        }

        return string.Join(", ", parts);
    }

    public static CosmeticSource ParseSources(string text)
    {
        var sources = CosmeticSource.None;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            sources |= Enum.TryParse<CosmeticSource>(part, true, out var flag) ? flag : throw new FormatException($"'{part}' is not a cosmetic source.");
        }

        return sources;
    }

    private static readonly (CosmeticSource Flag, string Text)[] SourceNames =
    [
        (CosmeticSource.Coins, "Coins"), (CosmeticSource.MonsterDrop, "Monster drop"), (CosmeticSource.BossDrop, "Boss drop"),
        (CosmeticSource.Quest, "Quest"), (CosmeticSource.Event, "Event"), (CosmeticSource.Brush, "Brush"), (CosmeticSource.Deal, "Bidding deal")
    ];
}

public enum CabinetBidStatus : byte
{
    Placed = 0,
    Unknown = 1,
    Ended = 2,
    TooLow = 3,
    Leading = 4,
    NotEnoughCoins = 5,
    AlreadyOwned = 6,
    LevelTooLow = 7,
    Rejected = 8
}
