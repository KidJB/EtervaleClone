namespace PristonTale.Contracts.Social;

// The original's party sizes, shared by its rules, its messages and the HUD: the members of one party (the leader
// first), the planar distance within which a member shares a kill's experience and gold, and the shorter one within which
// the minimap draws a member in its own colour (red beyond it).
public static class PartyLimits
{
    public const int MaxMembers = 6;
    public const float ShareRange = 18 * 64;
    public const float NearRange = 17 * 64;
}

// What a party member asks of its party, as the party window's buttons number them. The first three are the leader's.
public enum PartyCommand : byte
{
    Delegate = 0,
    Disband = 1,
    Kick = 2,
    Leave = 3,
    ChangeMode = 4,
    LeaveRaid = 5
}

// How a party shares: Normal as the original, Exp a larger experience share for fewer drops, Hunt a smaller share for more.
public enum PartyMode : byte
{
    Normal = 0,
    Exp = 1,
    Hunt = 2
}
