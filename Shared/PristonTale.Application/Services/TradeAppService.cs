using PristonTale.Application.Saves;
using PristonTale.Application.Chat;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using System.Globalization;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.Trades;

namespace PristonTale.Application.Services;

// Trades outside the tick (WorldNoticeRequests). The lock that completes a trade holds the locking side's save gate, then
// waits up to PartnerGateWait for its partner's (the partner's own lock or a save holds it briefly; nothing holds the
// partner's gate while waiting for the locker's, so the wait cannot deadlock). It stores both characters in one
// transaction, with the coins each side offered (CoinTrading) when any, and commits both on the tick; a partner's gate
// still busy unlocks the trade.
public sealed class TradeAppService(ISessionRegistry sessions, ICharacterSaves saves, ICharacterRepository characters, ICoinRepository coins, WorldServer world,
    ChatDirectory chat)
{
    private static readonly TimeSpan PartnerGateWait = TimeSpan.FromSeconds(2);
    private readonly DurableOperations _durable = new(saves);

    public Task RequestAsync(string sessionToken, ulong targetId) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.RequestTradeAsync(session, targetId));

    public Task AnswerAsync(string sessionToken, ulong fromId, bool accept) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.AnswerTradeAsync(session, fromId, accept));

    public Task OfferAsync(string sessionToken, TradeOffer offer) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, async session =>
            await world.OfferTradeAsync(session, offer, offer.Coins > 0 ? await coins.BalanceAsync(session.AccountId) : 0));

    public Task CancelAsync(string sessionToken) => WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, world.CancelTradeAsync);

    public async Task LockAsync(string sessionToken, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        if (lease.Failure is not null)
        {
            return;
        }

        TradeLock locked;
        try
        {
            locked = await world.LockTradeAsync(lease.Session);
        }
        catch (WorldBusyException)
        {
            return;
        }

        chat.Deliver(locked.Notices);
        if (locked is not { Trade: { } trade, Partner: { } partner })
        {
            return;
        }

        using var partnerLease = await _durable.AdmitAsync(partner, PartnerGateWait, CancellationToken.None);
        if (partnerLease.Failure is not null)
        {
            chat.Deliver(await world.ReleaseTradeAsync(trade, TradeStatus.Busy));
            return;
        }

        var shortOfCoins = false;
        var notices = await lease.RunAsync(
            session => world.PrepareTradeAsync(session, partner, trade),
            async (session, prepared) => await world.CommitTradeAsync(session, partner, prepared),
            static prepared => prepared.Notices,
            static _ => (WorldNotice[])[],
            async (session, prepared) =>
            {
                var stored = await StoreAsync(session, partner, prepared, trade);
                shortOfCoins = stored == CoinTradeStore.NotEnoughCoins;
                return stored == CoinTradeStore.Stored ? null : shortOfCoins ? DurableFailure.Conflict : DurableFailure.StorageFailed;
            },
            async (_, _) => chat.Deliver(await world.ReleaseTradeAsync(trade, shortOfCoins ? TradeStatus.NotEnoughCoins : TradeStatus.StorageFailed)));
        chat.Deliver(notices);
    }

    // Both characters, with the coins each side offered moved in the same transaction (the receiver gets them less the fee).
    private async Task<CoinTradeStore> StoreAsync(PlayerSession session, PlayerSession partner, PreparedTrade prepared, Trade trade)
    {
        CharacterRecord[] records = [session.Stamp(prepared.Snapshot!), partner.Stamp(prepared.PartnerSnapshot!)];
        var side = trade.SideOf(session.EntityId);
        var transfers = new List<CoinTransfer>(2);
        AddTransfer(transfers, session, partner, trade.OfferOf(side).Coins);
        AddTransfer(transfers, partner, session, trade.OfferOf(1 - side).Coins);
        if (transfers.Count == 0)
        {
            return await characters.UpdateAllAsync(records, CancellationToken.None) ? CoinTradeStore.Stored : CoinTradeStore.Failed;
        }

        return await coins.TradeWithCharactersAsync(records, transfers, CancellationToken.None);
    }

    private static void AddTransfer(List<CoinTransfer> transfers, PlayerSession from, PlayerSession to, long amount)
    {
        if (amount > 0)
        {
            transfers.Add(new CoinTransfer(from.AccountId, to.AccountId, amount, CoinTrading.Received(amount),
                string.Create(CultureInfo.InvariantCulture, $"trade {from.Character?.Name} -> {to.Character?.Name}")));
        }
    }
}
