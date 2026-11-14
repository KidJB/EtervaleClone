using PristonTale.Application.Sessions;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Parties;

namespace PristonTale.Application.World;

// One member of a listed party, the leader first.
public sealed record PartyFinderMember(string Name, PlayerClass Class, int Level);

// A party the party finder lists: its leader, the field the leader is in, its mode and average level, its members, and its
// finder settings (the password left out).
public sealed record PartyFinderListing(ulong LeaderId, int FieldId, PartyMode Mode, int AverageLevel, IReadOnlyList<PartyFinderMember> Members,
    PartyJoinMode JoinMode, int MinLevel, ushort Classes);

// The finder's list, and the asking player's own party when it leads one (its settings with the password, for the leader
// to change); Own is null otherwise.
public sealed record PartyFinderView(IReadOnlyList<PartyFinderListing> Listings, PartyFinderSettings? Own, IReadOnlyList<PartyFinderMember> OwnMembers);

public sealed partial class WorldServer
{
    // The party finder lists at most this many parties.
    public const int PartyFinderMaxListings = 30;

    // The parties with room that are not closed, and the player's own party's settings when it leads one; read on the tick.
    public Task<PartyFinderView> PartyFinderAsync(PlayerSession session) =>
        EnqueueAsync(session.EntityId, () =>
        {
            var listings = new List<PartyFinderListing>();
            foreach (var party in _parties)
            {
                if (listings.Count == PartyFinderMaxListings)
                {
                    break;
                }

                if (party.IsFull || party.Finder.Mode == PartyJoinMode.Closed || !_connections.TryGetValue(party.LeaderId, out var leader))
                {
                    continue;
                }

                listings.Add(new PartyFinderListing(party.LeaderId, leader.FieldId, party.Mode, party.AverageLevel, MembersOf(party), party.Finder.Mode,
                    party.Finder.MinLevel, party.Finder.Classes));
            }

            var own = _connections.TryGetValue(session.EntityId, out var self) && self.Player.Party is { } mine && mine.LeaderId == session.EntityId ? mine : null;
            return new PartyFinderView(listings, own?.Finder, own is null ? [] : MembersOf(own));
        });

    // The leader's finder settings for its party, made valid.
    public Task<WorldNotice[]> SetPartyFinderAsync(PlayerSession session, PartyFinderSettings settings) =>
        EnqueueAsync(session.EntityId, () => PartyAction(session, player =>
        {
            if (player.Party is { } party && party.LeaderId == player.EntityId)
            {
                party.Finder = settings.Valid();
                Reply(player, GameText.Of(GameTextId.PartyFinderSaved));
            }
        }));

    // A player alone joins a listed party through the finder: refused by its settings, else at once (Open, or Password with
    // the right one) within the level gap of any party request, or by asking its leader (Approval), as asking a leader alone
    // does.
    public Task<WorldNotice[]> JoinFromPartyFinderAsync(PlayerSession session, ulong leaderId, string password) =>
        EnqueueAsync(session.EntityId, () => PartyAction(session, player =>
        {
            if (player.Party is not null)
            {
                Reply(player, PartyMessages.CannotEnter);
                return;
            }

            if (!_connections.TryGetValue(leaderId, out var leader) || leader.Player.Party is not { } party || party.LeaderId != leaderId)
            {
                Reply(player, GameText.Of(GameTextId.PartyFinderGone));
                return;
            }

            var refusal = party.Finder.Check(player.Level, player.Character.Class.Class, password ?? "");
            if (refusal != PartyFinderRefusal.None)
            {
                Reply(player, party.Finder.Text(refusal));
                return;
            }

            if (party.Finder.Mode == PartyJoinMode.Approval)
            {
                Invite(player, leader.Player);
                return;
            }

            // Joining at once keeps the level gap any party request keeps.
            if (!PartyRules.LevelsMatch(party.AverageLevel, player.Level))
            {
                Reply(player, PartyMessages.LevelGap);
                return;
            }

            Join(party, player);
        }));

    private List<PartyFinderMember> MembersOf(Party party)
    {
        var members = new List<PartyFinderMember>(party.Count);
        foreach (var memberId in party.Members)
        {
            if (_connections.TryGetValue(memberId, out var member))
            {
                var character = member.Player.Character;
                members.Add(new PartyFinderMember(character.Name, character.Class.Class, character.Level));
            }
        }

        return members;
    }
}
