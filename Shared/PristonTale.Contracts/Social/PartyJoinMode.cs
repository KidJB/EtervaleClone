namespace PristonTale.Contracts.Social;

// How the party finder lets players join a party: Approval asks its leader, Open joins at once, Closed hides the party from
// the finder, Password joins at once with the leader's password.
public enum PartyJoinMode : byte
{
    Approval = 0,
    Open = 1,
    Closed = 2,
    Password = 3
}

// Why the party finder refused a join.
public enum PartyFinderRefusal : byte
{
    None = 0,
    Closed = 1,
    Level = 2,
    Class = 3,
    Password = 4
}
