using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Social.Clans;

// A clan's running world buff: when it ends, in Unix seconds.
public readonly record struct ClanBuffEnd(long ClanId, int Kind, long EndSeconds);

// Clan world buffs: the world buffs (World.WorldBuffs: the same kinds, effects, durations and extension) on items of their
// own, CL101-CL110 for an hour and CL201-CL210 for a day, started by a member with ClanPermission.ActivateBuffs for its clan
// only. Every current member has them, online or entering later, for the time left; a member who leaves loses them. A
// buff running for the world and for the clan counts once.
public static class ClanBuffs
{
    // The coins the clan coin treasury pays for a buff, as the coin shop prices the world buff items (an hour, a day).
    public const long HourCoinPrice = 200;
    public const long DayCoinPrice = 4_000;
    public const long MinCoinDonation = 1;
    public const long MaxCoinDonation = 1_000_000;
    public const long MaxCoinTreasury = 1_000_000_000;

    public static long CoinPrice(bool day) => day ? DayCoinPrice : HourCoinPrice;

    public static ClanStatus CheckCoinDonation(long amount, long coinTreasury) =>
        amount < MinCoinDonation || amount > MaxCoinDonation ? ClanStatus.DonationOutOfRange
        : coinTreasury > MaxCoinTreasury - amount ? ClanStatus.TreasuryFull
        : ClanStatus.Ok;

    // A rank with ClanPermission.SpendCoins starts a buff (WorldBuffs.Argument) the coin treasury pays for.
    public static ClanStatus CheckCoinSpend(ClanPermission rights, int argument, long coinTreasury)
    {
        if (!ClanRankPermissions.Allows(rights, ClanPermission.SpendCoins))
        {
            return ClanStatus.NotPermitted;
        }

        var (kind, seconds) = WorldBuffs.FromArgument(argument);
        if (WorldBuffs.Get(kind) is null)
        {
            return ClanStatus.Unavailable;
        }

        return coinTreasury < CoinPrice(seconds == WorldBuffs.DayDurationSeconds) ? ClanStatus.NotEnoughTreasury : ClanStatus.Ok;
    }

    public static string ItemKey(WorldBuff buff, bool day) => (day ? "CL2" : "CL1") + buff.ItemKey[3..];

    public static string Name(WorldBuff buff) => "Clan" + buff.Name[buff.Name.IndexOf(' ')..];

    public static bool IsItem(string catalogKey)
    {
        foreach (var buff in WorldBuffs.All)
        {
            if (ItemKey(buff, false).Equals(catalogKey, StringComparison.OrdinalIgnoreCase) || ItemKey(buff, true).Equals(catalogKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<ItemDefinition> ItemDefinitions() =>
        WorldBuffs.All.SelectMany(static buff => (ItemDefinition[])
        [
            new ItemDefinition(ItemCodes.Parse(ItemKey(buff, false)), Name(buff) + " (1 hour)", ItemKind.Misc) { CatalogKey = ItemKey(buff, false) },
            new ItemDefinition(ItemCodes.Parse(ItemKey(buff, true)), Name(buff) + " (24 hours)", ItemKind.Misc) { CatalogKey = ItemKey(buff, true) }
        ]);

    // The mask of the buffs whose end is after now.
    public static int Mask(ReadOnlySpan<long> ends, long nowSeconds)
    {
        var mask = 0;
        foreach (var buff in WorldBuffs.All)
        {
            var kind = (int)buff.Kind;
            if (kind < ends.Length && ends[kind] > nowSeconds)
            {
                mask |= buff.Bit;
            }
        }

        return mask;
    }
}
