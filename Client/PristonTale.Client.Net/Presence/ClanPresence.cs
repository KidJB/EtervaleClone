using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Client.Net.Presence;

public sealed record ClanMemberInfo(string Name, PlayerClass Class, int Level, ClanRank Rank, bool Online);

// The local player's clan as the server last sent it; None without one.
// Permissions: what the chief lets each rank do with what the clan owns; Rights: what the local player may do; the
// treasury's gold and the clan warehouse's upgrade levels (ClanUpgrades).
public sealed record ClanInfo(long ClanId, string Name, ClanState State, long DeadlineSeconds, ClanRank Rank, int Capacity, string Intro, int MarkVersion,
    IReadOnlyList<ClanMemberInfo> Members, ClanRankPermissions Permissions = default, ClanPermission Rights = ClanPermission.None, long Treasury = 0,
    int SlotLevel = 0, int WeightLevel = 0, long CoinTreasury = 0)
{
    public static readonly ClanInfo None = new(0, "", ClanState.Registering, 0, ClanRank.Member, 0, "", 0, []);

    public bool HasClan => ClanId != 0;

    public bool CanRecruit => HasClan && ClanRules.CanRecruit(Rank);

    public bool May(ClanPermission needed) => HasClan && ClanRankPermissions.Allows(Rights, needed);
}

// One line of the clan warehouse log as the server sent it (AtSeconds: Unix seconds).
public sealed record ClanWarehouseLogLine(long AtSeconds, string Character, ClanWarehouseLogAction Action, string ItemKey, string ItemName, int Quantity);

public sealed record ClanInvitationInfo(ulong RecruiterId, string ClanName, string ChiefName);

// How a clan request ended (ClanStatus; a refusal's line comes in the chat).
public sealed record ClanResultInfo(byte Action, ClanStatus Status);
