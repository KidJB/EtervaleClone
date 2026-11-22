namespace PristonTale.Simulation.Social.Clans;

// What a member's rank lets it do with what the clan owns: put items in and take them out of the clan warehouse, read
// its log, and start clan world buffs.
[Flags]
public enum ClanPermission : byte
{
    None = 0,
    Deposit = 1,
    Withdraw = 2,
    ViewLog = 4,
    ActivateBuffs = 8,

    // Spend the clan coin treasury on clan world buffs (ClanBuffs.CoinPrice).
    SpendCoins = 16,
    All = Deposit | Withdraw | ViewLog | ActivateBuffs | SpendCoins
}

// The rights of the vice chief and the members, which the chief sets; the chief always has them all. Stored packed: the
// members' rights in the low byte, the vice's in the next.
public readonly record struct ClanRankPermissions(ClanPermission Member, ClanPermission Vice)
{
    public const int DefaultPacked = (int)(ClanPermission.Deposit | ClanPermission.Withdraw) | (int)ClanPermission.All << 8;

    public static readonly ClanRankPermissions Default = Unpack(DefaultPacked);

    public int Packed => (int)(Member & ClanPermission.All) | (int)(Vice & ClanPermission.All) << 8;

    public static ClanRankPermissions Unpack(int packed) =>
        new((ClanPermission)(packed & 0xFF) & ClanPermission.All, (ClanPermission)((packed >> 8) & 0xFF) & ClanPermission.All);

    public ClanPermission For(ClanRank rank) => rank switch
    {
        ClanRank.Chief => ClanPermission.All,
        ClanRank.Vice => Vice,
        _ => Member
    };

    public static bool Allows(ClanPermission rights, ClanPermission needed) => needed != ClanPermission.None && (rights & needed) == needed;
}
