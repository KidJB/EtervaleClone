using System.Globalization;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.Services;

// Daily quests outside the tick. Accepting changes the live character (stored with its next save); a claim prepares the
// experience, gold and item on a copy, stores it with the coins added to the account in one transaction
// (ICoinRepository.BuyWithCharacterAsync with a negative price), then commits.
public sealed class DailyQuestAppService(ISessionRegistry sessions, ICharacterSaves saves, ICoinRepository coins, WorldServer world)
{
    private readonly DurableOperations _durable = new(saves);

    public Task<DailyQuestStatus> AcceptAsync(string sessionToken, ulong npcEntityId, int questId) =>
        sessions.TryGet(sessionToken, out var session)
            ? world.AcceptDailyQuestAsync(session, npcEntityId, questId)
            : Task.FromResult(DailyQuestStatus.Rejected);

    public async Task<DailyQuestStatus> ClaimAsync(string sessionToken, ulong npcEntityId, int questId, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return DailyQuestStatus.Rejected;
        }

        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        return await lease.RunAsync(
            leased => world.PrepareDailyClaimAsync(leased, npcEntityId, questId),
            world.CommitDailyClaimAsync,
            static prepared => prepared.Outcome.Status,
            static _ => DailyQuestStatus.Rejected,
            async (leased, prepared) =>
            {
                var reason = string.Create(CultureInfo.InvariantCulture, $"daily quest {questId}");
                return await coins.BuyWithCharacterAsync(leased.Stamp(prepared.Snapshot!), -prepared.Outcome.Coins, reason, CancellationToken.None) is null
                    ? DurableFailure.StorageFailed
                    : null;
            },
            storeFirst: static prepared => prepared.Outcome.Status == DailyQuestStatus.Ok);
    }
}
