using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// A pet skin: a monster's model drawn small beside its owner. MonsterCode is the monster whose kills may drop the skin's
// item (ItemKey); Model is that monster's model, which the client draws at Scale.
public sealed record PetSkin(int Id, string Name, int MonsterCode, string Model, float Scale, string ItemKey)
{
    public uint ItemCode { get; } = ItemCodes.Parse(ItemKey);
}

// The pet skins a player can collect, by id (1 to Max). An account keeps the skins it unlocked (a bit per id, PlayerCharacter
// .PetSkins); a character has at most one pet out (PlayerCharacter.PetOut, 0 none), which every viewer sees and which
// fetches gold and potions as a helper pet does (PetFetch). A skin's item drops from its monster at DropPerTenThousand.
public static class PetSkins
{
    public const int Max = 63;
    public const int DropPerTenThousand = 50;

    // Every skin's item looks like the 30-day helper pet's.
    public const string IconKey = "BI152";

    public static readonly IReadOnlyList<PetSkin> All =
    [
        new(1, "Hopy", 196, @"char\monster\Hopy\hopy.inx", 0.5f, "PE101"),
        new(2, "Cuepy", 82, @"char\monster\Hopy\mopy.inx", 0.5f, "PE102"),
        new(3, "Rabie", 418, @"char\monster\Monrebi\Monrebi.inx", 0.45f, "PE103"),
        new(4, "Tobie", 534, @"char\monster\Monrebi\Monrebi-1.inx", 0.45f, "PE104"),
        new(5, "Mush", 360, @"char\monster\Monmushroom-mo\Monmushroom.inx", 0.45f, "PE105"),
        new(6, "Mudy", 356, @"char\monster\MonMudy\Monmudy.inx", 0.5f, "PE106"),
        new(7, "Arma", 7, @"char\monster\Mouse\mouse.inx", 0.5f, "PE107"),
        new(8, "Orbit", 409, @"char\monster\Moneye\eye.inx", 0.45f, "PE108"),
        new(9, "Imp", 246, @"char\monster\Monimp\Monimp-a.inx", 0.4f, "PE109"),
        new(10, "MiniG", 347, @"char\monster\MonminiG\MonminiG-a1.inx", 0.4f, "PE110")
    ];

    public static PetSkin Get(int id) => id >= 1 && id <= All.Count ? All[id - 1] : null;

    public static PetSkin ForMonster(int monsterCode)
    {
        foreach (var skin in All)
        {
            if (skin.MonsterCode == monsterCode)
            {
                return skin;
            }
        }

        return null;
    }

    // A pet skin's item; its sprite is IconKey's.
    public static bool IsItem(string catalogKey)
    {
        foreach (var skin in All)
        {
            if (string.Equals(skin.ItemKey, catalogKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static ulong Bit(int id) => id is >= 1 and <= Max ? 1UL << (id - 1) : 0;

    public static bool Has(ulong skins, int id) => (skins & Bit(id)) != 0;

    // The skins' items, added to the imported catalog: one cell, used from the bag to unlock the skin.
    public static IEnumerable<ItemDefinition> ItemDefinitions() =>
        All.Select(skin => new ItemDefinition(skin.ItemCode, $"{skin.Name} Pet", ItemKind.Misc) { CatalogKey = skin.ItemKey });
}
