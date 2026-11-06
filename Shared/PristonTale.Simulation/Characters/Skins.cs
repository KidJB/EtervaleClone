using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

public enum SkinSlot : byte
{
    Weapon = 0,
    Armor = 1,
    OffHand = 2,
    Hair = 3
}

public enum SkinStatus : byte
{
    Ok = 0,
    UnknownSkin = 1,
    AlreadyOwned = 2,
    NotEnoughCoins = 3,
    NotOwned = 4,
    Rejected = 5
}

// The looks a character chose in The Cabinet, one catalog key per slot ("" for the worn item's own look).
public sealed record SkinSet(string Weapon = "", string Armor = "", string OffHand = "", string Hair = "")
{
    public static readonly SkinSet None = new();
}

// Skins: every piece of gear sells its look for coins to the whole account; a character shows an owned look over the item it
// wears in that slot, without changing what the item does. A skin fits worn gear of its own item kind and grip (a
// two-handed sword's look on a two-handed sword), so held-weapon motions stay right: body gear splits into armor (DA1, DA3) and
// robes (DA2, DA4), and costumes (Costumes) fit over either for the classes they have a body for.
public static class SkinRules
{
    public const string NpcName = "Knight Commander Derik";
    public const int MinPrice = 550;
    public const int MaxPrice = 6000;
    public const int PricePerLevel = 45;
    public const int CostumePrice = 4500;
    public const int GearPrice = 3000;
    public const int HairPrice = 1500;
    private const string HeadPrefix = "KH";
    private const string ShortTerm = "(7Day)";
    private const string LongTerm = "(30Day)";
    private const int BodyFamilyLetters = 3;
    private const string BodyPrefix = "DA";

    public static SkinSlot? SlotOf(ItemDefinition item) => item.Kind == ItemKind.Quest || item.CatalogKey.Length == 0 ? null : item.EquipSlot switch
    {
        EquipmentSlot.Weapon => SkinSlot.Weapon,
        EquipmentSlot.Armor => SkinSlot.Armor,
        EquipmentSlot.OffHand => SkinSlot.OffHand,
        _ => null
    };

    public static int Price(ItemDefinition item) => Math.Clamp(MinPrice + item.RequiredLevel * PricePerLevel, MinPrice, MaxPrice);

    // The hair a hair tint's look shows, or null when the key is not one. It replaces the character's own hair while chosen.
    public static int? HairOf(string skin)
    {
        if (UsableItemCatalog.TryGet(skin, out var rule) && rule.Action == UsableItemAction.HairTint)
        {
            return rule.Argument;
        }

        return skin.StartsWith(HeadPrefix, StringComparison.Ordinal) && int.TryParse(skin.AsSpan(HeadPrefix.Length), out var number) && Costumes.Hair.Contains(number)
            ? CharacterHead.CustomHair + number
            : null;
    }

    public static string HeadKey(int number) => HeadPrefix + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);

    public static bool Fits(string worn, string skin)
    {
        if (worn.Length < BodyFamilyLetters || skin.Length < BodyFamilyLetters)
        {
            return false;
        }

        var gear = Costumes.FindGear(skin);
        if (gear is { Like.Length: > 0 })
        {
            return ItemEquipmentMetadata.KindOf(worn) is { } wornKind && ItemEquipmentMetadata.KindOf(gear.Like) == wornKind;
        }

        if (ItemEquipmentMetadata.IsTwoHanded(worn) != (gear?.Two ?? ItemEquipmentMetadata.IsTwoHanded(skin)))
        {
            return false;
        }

        if (!worn.StartsWith(BodyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ItemEquipmentMetadata.KindOf(worn) is { } kind && ItemEquipmentMetadata.KindOf(skin) == kind;
        }

        return Costumes.Find(skin) is not null
            || skin.StartsWith(BodyPrefix, StringComparison.OrdinalIgnoreCase) && BodyType(worn) == BodyType(skin);
    }

    // Armor bodies are 1 and 3, robes 2 and 4.
    private static int BodyType(string key) => key[BodyFamilyLetters - 1] % 2;

    // Timed items share a body: the Cabinet sells the look once, without the term in its name.
    public static bool Listed(ItemDefinition item) => !item.Name.Contains(ShortTerm, StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(string name) => name.Replace(LongTerm, "", StringComparison.OrdinalIgnoreCase).Trim();

    // What the class may wear (armor by class and body type); jobs is its job bit.
    public static bool Wearable(string skin, uint jobs)
    {
        if (Costumes.Find(skin) is { } costume)
        {
            foreach (var playerClass in costume.Bodies.Keys)
            {
                if (((uint)JobBits.Of(playerClass) & jobs) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        var allowed = ItemClassRestrictions.AllowedJobs(skin);
        return allowed == 0 || (allowed & jobs) != 0;
    }

    // The key to show for the worn item: the skin when it fits, else the item's own.
    public static string Look(string worn, string skin) => Fits(worn, skin) ? skin : worn;
}
