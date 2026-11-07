using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.World;

public enum WorldBuffKind
{
    HpRegen = 1,
    SpRegen = 2,
    MpRegen = 3,
    MpDown = 4,
    SpDown = 5,
    BonusDamage = 6,
    BonusAbsorb = 7,
    MoveSpeed = 8,
    Experience = 9,
    Drop = 10
}

public sealed record WorldBuff(WorldBuffKind Kind, string Name, string Effect, string ItemKey)
{
    public uint ItemCode { get; } = ItemCodes.Parse(ItemKey);

    // The 24-hour item of the same buff.
    public string DayItemKey { get; } = "WB2" + ItemKey[3..];

    public uint DayItemCode => ItemCodes.Parse(DayItemKey);

    public int Bit => 1 << (int)Kind;
}

// A running world buff: when it ends, in Unix seconds.
public readonly record struct WorldBuffEnd(int Kind, long EndSeconds);

// World buffs: an item any player uses gives every player, online or not, the buff for the same stretch of real time, added
// to whatever is left of it. The end time is the server's, so a player who logs in later has it for the time left, and it
// never pauses.
public static class WorldBuffs
{
    public const int DurationSeconds = 3600;
    public const int DayDurationSeconds = 24 * 3600;
    public const int RegenTenths = 30;
    public const int CostPercent = 10;
    public const int DamagePercent = 5;
    public const int AbsorbPercent = 5;
    public const int MoveSpeed = 3;
    public const int ExperiencePercent = 5;
    public const int DropPercent = 15;

    public static readonly IReadOnlyList<WorldBuff> All =
    [
        new(WorldBuffKind.HpRegen, "World HP Regen", "+3 HP regen", "WB101"),
        new(WorldBuffKind.SpRegen, "World SP Regen", "+3 SP regen", "WB102"),
        new(WorldBuffKind.MpRegen, "World MP Regen", "+3 MP regen", "WB103"),
        new(WorldBuffKind.MpDown, "World MP Down", "10% less MP cost", "WB104"),
        new(WorldBuffKind.SpDown, "World SP Down", "10% less SP cost", "WB105"),
        new(WorldBuffKind.BonusDamage, "World Bonus Damage", "+5% damage", "WB106"),
        new(WorldBuffKind.BonusAbsorb, "World Bonus Absorb", "5% less damage taken", "WB107"),
        new(WorldBuffKind.MoveSpeed, "World Move Speed", "+3 move speed", "WB108"),
        new(WorldBuffKind.Experience, "World EXP Buff", "+5% EXP gained", "WB109"),
        new(WorldBuffKind.Drop, "World Drop Buff", "15% chance of an extra drop", "WB110")
    ];

    public static WorldBuff Get(int kind) => kind >= 1 && kind <= All.Count ? All[kind - 1] : null;

    public static bool Has(int mask, WorldBuffKind kind) => (mask & (1 << (int)kind)) != 0;

    public static bool IsItem(string catalogKey)
    {
        foreach (var buff in All)
        {
            if (buff.ItemKey.Equals(catalogKey, StringComparison.OrdinalIgnoreCase) || buff.DayItemKey.Equals(catalogKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<ItemDefinition> ItemDefinitions() =>
        All.SelectMany(static buff => (ItemDefinition[])
        [
            new ItemDefinition(buff.ItemCode, buff.Name + " (1 hour)", ItemKind.Misc) { CatalogKey = buff.ItemKey },
            new ItemDefinition(buff.DayItemCode, buff.Name + " (24 hours)", ItemKind.Misc) { CatalogKey = buff.DayItemKey }
        ]);

    // The argument of a buff's item rule: its kind, and which of its two items it is.
    public static int Argument(WorldBuffKind kind, bool day) => (int)kind + (day ? DayFlag : 0);

    public static (int Kind, int Seconds) FromArgument(int argument) =>
        argument >= DayFlag ? (argument - DayFlag, DayDurationSeconds) : (argument, DurationSeconds);

    private const int DayFlag = 100;
}
