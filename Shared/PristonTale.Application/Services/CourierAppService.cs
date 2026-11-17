using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.Services;

// Quest Couriers outside the tick. Accepting and abandoning change the live character (stored with its next save); a turn-in
// prepares the experience, gold and item on a copy, stores the character, then commits, so a reward is never given twice
// nor lost.
public sealed class CourierAppService(ISessionRegistry sessions, ICharacterSaves saves, WorldServer world)
{
    private readonly DurableOperations _durable = new(saves);

    public Task<CourierBoardView?> BoardAsync(string sessionToken, ulong npcEntityId) =>
        sessions.TryGet(sessionToken, out var session) ? world.CourierBoardAsync(session, npcEntityId) : Task.FromResult<CourierBoardView?>(null);

    public Task<CourierStatus> AcceptAsync(string sessionToken, ulong npcEntityId, int questId) =>
        sessions.TryGet(sessionToken, out var session)
            ? world.AcceptCourierQuestAsync(session, npcEntityId, questId)
            : Task.FromResult(CourierStatus.Rejected);

    public Task<CourierStatus> AbandonAsync(string sessionToken, int questId) =>
        sessions.TryGet(sessionToken, out var session) ? world.AbandonCourierQuestAsync(session, questId) : Task.FromResult(CourierStatus.Rejected);

    public async Task<CourierTurnIn> TurnInAsync(string sessionToken, ulong npcEntityId, int questId, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new CourierTurnIn(CourierStatus.Rejected);
        }

        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        return await lease.RunAsync(
            leased => world.PrepareCourierTurnInAsync(leased, npcEntityId, questId),
            world.CommitCourierTurnInAsync,
            static prepared => prepared.Outcome,
            static _ => new CourierTurnIn(CourierStatus.Rejected),
            storeFirst: static prepared => prepared.Outcome.Status == CourierStatus.Ok);
    }
}
