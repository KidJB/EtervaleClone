using Microsoft.Xna.Framework;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Parties;

namespace PristonTale.Gameplay.World;

// Party requests through the world connection.
public sealed partial class GameScene : IPartyClient
{
    private enum ApproachKind
    {
        Npc,
        Seller,
        Player
    }

    public Task InviteToPartyAsync(ulong targetId) => SendSocial(_playChannel?.InviteToPartyAsync(targetId, _presenceCts.Token), "party invite");

    public Task AnswerPartyAsync(ulong fromId, bool accept) => SendSocial(_playChannel?.AnswerPartyAsync(fromId, accept, _presenceCts.Token), "party answer");

    public Task CommandPartyAsync(PartyCommand command, ulong memberId) =>
        SendSocial(_playChannel?.CommandPartyAsync(command, memberId, _presenceCts.Token), "party command");

    private Task SendSocial(Task send, string what)
    {
        if (send is not null)
        {
            SendUpstream(send, what);
        }

        return Task.CompletedTask;
    }
}
