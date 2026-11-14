using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

internal sealed partial class ClientSession
{
    // The party finder's list: the parties with room whose leader is out of the towns, in parts a frame holds.
    private async Task PartyFinderAsync()
    {
        if (_session is not { Loaded: not null } session)
        {
            return;
        }

        var view = await _services.World.PartyFinderAsync(session);
        var wire = view.Listings.Take(PartyFinderWire.MaxListings).Select(static listing => new PartyFinderWireListing(listing.LeaderId, listing.FieldId, listing.Mode,
            listing.AverageLevel, Members(listing.Members), listing.JoinMode, listing.MinLevel, listing.Classes)).ToArray();
        var own = view.Own is { } settings ? new PartyFinderWireOwn(settings.Mode, settings.MinLevel, settings.Password, settings.Classes, Members(view.OwnMembers)) : null;
        var first = 0;
        do
        {
            var part = wire.AsSpan(first, Math.Min(PartyFinderWire.MaxPerPart, wire.Length - first)).ToArray();
            SendLarge(new PartyFinderPacket(first, part, own).Write, PartyFinderPacket.MaxSize);
            first += part.Length;
        }
        while (first < wire.Length);
    }

    private static PartyFinderWireMember[] Members(IReadOnlyList<Application.World.PartyFinderMember> members) =>
        [.. members.Select(static member => new PartyFinderWireMember(member.Name, (byte)member.Class, member.Level))];
}
