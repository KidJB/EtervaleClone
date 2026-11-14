using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

// Party requests. None is answered: the members list and the chat lines show what changed.
internal sealed partial class ClientSession
{
    private Task SetPartyFinderAsync(PartyFinderSettingsPacket request) =>
        _player is null ? Task.CompletedTask : _services.Parties.SetFinderAsync(RequireToken(),
            new Simulation.Parties.PartyFinderSettings(request.JoinMode, request.MinLevel, request.Password, request.Classes));

    private Task JoinFromPartyFinderAsync(PartyFinderJoinPacket request) =>
        _player is null ? Task.CompletedTask : _services.Parties.JoinFromFinderAsync(RequireToken(), request.LeaderId, request.Password);

    private Task InviteToPartyAsync(PartyInvitePacket request) =>
        _player is null ? Task.CompletedTask : _services.Parties.InviteAsync(RequireToken(), request.TargetId);

    private Task AnswerPartyAsync(PartyAnswerPacket answer) =>
        _player is null ? Task.CompletedTask : _services.Parties.AnswerAsync(RequireToken(), answer.FromId, answer.Accept);

    private Task CommandPartyAsync(PartyCommandPacket command) =>
        _player is null ? Task.CompletedTask : _services.Parties.CommandAsync(RequireToken(), command.Command, command.MemberId);
}
