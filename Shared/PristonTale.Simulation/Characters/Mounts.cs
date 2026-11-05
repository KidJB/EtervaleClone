using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// How a rider sits on a mount: each seat has its own riding poses for every class.
public enum MountSeat
{
    Horse,
    Wolf,
    Pingu,
    Raptor,
    Dragon
}

public enum MountRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

// A mount a player rides: Model is its .inx (with Stand and Run motions) drawn at Scale; the rider stands SaddleHeight
// above the ground while riding when no riding pose seats it. Portrait is its picture in the mount window
// (assets/Hud/Mount/mounts/). Its item (ItemKey) adds it to the account's stable; Seat picks the rider's poses.
public sealed record Mount(int Id, string Name, string Model, float Scale, float SaddleHeight, MountRarity Rarity, string Portrait, string ItemKey,
    MountSeat Seat)
{
    public uint ItemCode { get; } = ItemCodes.Parse(ItemKey);

    // Move speed points the mount adds while ridden (each point is about 4% of the running speed).
    public int Speed => Mounts.SpeedOf(Rarity);
}

// The mounts a player can own, by id (1 to Max). An account keeps the mounts it owns (a bit per id, PlayerCharacter.Mounts);
// a character picks one (MountChosen) and rides it or walks (Riding). Riding adds the mount's speed; an attack or a skill
// takes the rider down.
public static class Mounts
{
    public const int Max = 63;

    private const string Folder = @"game\objects\mounts\";

    public static readonly IReadOnlyList<Mount> All =
    [
        new(1, "Horse", Folder + @"Horse\Horse.inx", 1f, 30f, MountRarity.Common, "Horse.png", "MT101", MountSeat.Horse),
        new(2, "Dark Horse", Folder + @"Horse\Horse_1.inx", 1f, 30f, MountRarity.Rare, "Horse_1.png", "MT102", MountSeat.Horse),
        new(3, "Royal Horse", Folder + @"Horse\Horse_2.inx", 1f, 30f, MountRarity.Epic, "Horse_2.png", "MT103", MountSeat.Horse),
        new(4, "Snowdeer", Folder + @"Horse\Snowdeer.inx", 1f, 34f, MountRarity.Rare, "Snowdeer.png", "MT104", MountSeat.Horse),
        new(5, "Frost Snowdeer", Folder + @"Horse\Snowdeer_1.inx", 1f, 34f, MountRarity.Epic, "Snowdeer_1.png", "MT105", MountSeat.Horse),
        new(6, "Unicorn", Folder + @"Unicorn\Unicorn.inx", 1f, 30f, MountRarity.Epic, "Unicorn.png", "MT106", MountSeat.Horse),
        new(7, "Snowflake Unicorn", Folder + @"Unicorn\Unicorn_4.inx", 1f, 30f, MountRarity.Legendary, "SnowflakeUnicorn.png", "MT107", MountSeat.Horse),
        new(8, "Wolf", Folder + @"Wolf\Wolf.inx", 1f, 24f, MountRarity.Common, "Wolf.png", "MT108", MountSeat.Wolf),
        new(9, "Lion", Folder + @"Lion\Lion.inx", 1f, 34f, MountRarity.Rare, "Lion.png", "MT109", MountSeat.Wolf),
        new(10, "Golden Lion", Folder + @"Lion\Lion_1.inx", 1f, 34f, MountRarity.Epic, "Lion_1.png", "MT110", MountSeat.Wolf),
        new(11, "Hog", Folder + @"Hog\Hog.inx", 1f, 24f, MountRarity.Common, "Hog.png", "MT111", MountSeat.Wolf),
        new(12, "Obscure Hog", Folder + @"Hog\ObscureHog.inx", 1f, 24f, MountRarity.Rare, "ObscureHog.png", "MT112", MountSeat.Wolf),
        new(13, "Pingu", Folder + @"Pingu\Pingu.inx", 1f, 18f, MountRarity.Common, "Pingu.png", "MT113", MountSeat.Pingu),
        new(14, "Raptor", Folder + @"Raptor\Raptor.inx", 1f, 14f, MountRarity.Rare, "raptor.png", "MT114", MountSeat.Raptor),
        new(15, "Crimson Raptor", Folder + @"Raptor\Raptor_1.inx", 1f, 14f, MountRarity.Epic, "raptor_1.png", "MT115", MountSeat.Raptor),
        new(16, "Ice Tiger", Folder + @"Tiger\IceTiger.inx", 1f, 26f, MountRarity.Epic, "IceTiger.png", "MT116", MountSeat.Wolf),
        new(17, "Blue Dragon", Folder + @"Dragon\Dragon.inx", 1f, 32f, MountRarity.Legendary, "BlueDragon.bmp", "MT117", MountSeat.Dragon),
        new(18, "Red Dragon", Folder + @"Dragon\Dragon1.inx", 1f, 32f, MountRarity.Legendary, "RedDragon.bmp", "MT118", MountSeat.Dragon)
    ];

    public static int SpeedOf(MountRarity rarity) => rarity switch
    {
        MountRarity.Common => 4,
        MountRarity.Rare => 6,
        MountRarity.Epic => 8,
        _ => 10
    };

    public static Mount Get(int id) => id >= 1 && id <= All.Count && All[id - 1].Id == id ? All[id - 1] : null;

    public static ulong Bit(int id) => id is >= 1 and <= Max ? 1UL << (id - 1) : 0;

    public static bool Has(ulong owned, int id) => Get(id) is not null && (owned & Bit(id)) != 0;

    public static bool IsItem(string catalogKey)
    {
        foreach (var mount in All)
        {
            if (mount.ItemKey.Equals(catalogKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<ItemDefinition> ItemDefinitions() =>
        All.Select(mount => new ItemDefinition(mount.ItemCode, $"{mount.Name} Mount", ItemKind.Misc) { CatalogKey = mount.ItemKey });
}
