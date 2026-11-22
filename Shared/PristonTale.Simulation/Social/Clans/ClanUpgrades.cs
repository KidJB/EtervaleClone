using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Social.Clans;

// What the clan treasury buys; stored as the clan's level of each (append only).
public enum ClanUpgradeKind : byte
{
    WarehouseSlots = 1,
    WarehouseWeight = 2
}

// The clan treasury and the clan warehouse's upgrades: members donate their gold to the treasury at the Clan Warehouse
// keeper, and only the chief spends it. Each upgrade has five levels over the base warehouse (ClanWarehouse), each costing
// twice the one before, so the late levels sink a clan's gold for weeks: slots 140 to 240 items (a third page from 180),
// weight 1,400 to 3,200.
public static class ClanUpgrades
{
    public const int MaxLevel = 5;
    public const long MinDonation = 1_000;
    public const long MaxDonation = 1_000_000_000;
    public const long MaxTreasury = 1_000_000_000_000;

    private static readonly int[] Items = [140, 160, 180, 200, 220, 240];
    private static readonly int[] Weights = [1_400, 1_700, 2_000, 2_400, 2_800, 3_200];
    private static readonly long[] SlotCosts = [5_000_000, 10_000_000, 20_000_000, 40_000_000, 80_000_000];
    private static readonly long[] WeightCosts = [4_000_000, 8_000_000, 16_000_000, 32_000_000, 64_000_000];

    public static int ItemLimit(int slotLevel) => Items[Math.Clamp(slotLevel, 0, MaxLevel)];

    public static int WeightLimit(int weightLevel) => Weights[Math.Clamp(weightLevel, 0, MaxLevel)];

    // Pages enough for the items one cell each, never fewer than the base warehouse's.
    public static int Pages(int slotLevel) => Math.Max(ClanWarehouse.Pages, (ItemLimit(slotLevel) + StoreGrid.Cells - 1) / StoreGrid.Cells);

    public static int LevelOf(ClanUpgradeKind kind, int slotLevel, int weightLevel) => kind == ClanUpgradeKind.WarehouseSlots ? slotLevel : weightLevel;

    // The gold the next level costs from this one; 0 at the last level.
    public static long Cost(ClanUpgradeKind kind, int level) =>
        level < 0 || level >= MaxLevel ? 0 : kind == ClanUpgradeKind.WarehouseSlots ? SlotCosts[level] : WeightCosts[level];

    public static ClanStatus CheckDonation(long amount, long treasury) =>
        amount < MinDonation || amount > MaxDonation ? ClanStatus.DonationOutOfRange
        : treasury > MaxTreasury - amount ? ClanStatus.TreasuryFull
        : ClanStatus.Ok;

    // Only the chief spends the treasury, on a level below the last that the treasury pays for.
    public static ClanStatus CheckUpgrade(ClanRank rank, ClanUpgradeKind kind, int level, long treasury)
    {
        if (rank != ClanRank.Chief)
        {
            return ClanStatus.NotChief;
        }

        if (!Enum.IsDefined(kind) || level >= MaxLevel)
        {
            return ClanStatus.MaxLevel;
        }

        return treasury < Cost(kind, level) ? ClanStatus.NotEnoughTreasury : ClanStatus.Ok;
    }
}
