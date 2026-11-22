using PristonTale.Simulation.Shops;
using System.Numerics;
using System.Runtime.CompilerServices;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.World;
using PristonTale.Protocol.Packets;

namespace PristonTale.Client.Net.Online;

// Opening sends JoinWorld and completes on WorldJoined; disposing sends the Etervale leave request so the
// server saves before the client returns to another screen.
internal sealed class OnlinePlayChannel : IPlayChannel
{
    private readonly GameServerConnection _connection;
    private readonly CancellationTokenSource _lifetime;
    private readonly Task _start;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    public int GameMasterLevel => _connection.GameMasterLevel;

    public long? GameClockMs => _connection.HasGameClock ? _connection.GameClockMs : null;

    public OnlinePlayChannel(GameServerConnection connection, CancellationToken cancellationToken = default)
    {
        _connection = connection;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _start = connection.OpenPlayAsync(_lifetime.Token);
    }

    public async Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ApplyProgressionAsync(command, ct);
    }

    public async Task<NpcInteraction> InteractNpcAsync(ulong entityId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.InteractNpcAsync(entityId, ct);
    }

    public async Task<NpcServiceReply> RequestNpcServiceAsync(NpcServiceCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.RequestNpcServiceAsync(command, ct);
    }

    public async Task<QuestOutcome> RequestQuestAsync(QuestCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.RequestQuestAsync(command, ct);
    }

    public async Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ApplyInventoryAsync(command, ct);
    }

    public async Task<ItemUseOutcome> UseItemAsync(ItemUseCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.UseItemAsync(command, ct);
    }

    public async Task<RestartOutcome> RestartAsync(RestartChoice choice, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.RestartAsync(choice, ct);
    }

    public async Task<PersonalShopReply> OpenPersonalShopAsync(string sign, IReadOnlyList<PersonalShopEntry> entries, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.OpenAsync(sign, entries, ct);
    }

    public async Task<PersonalShopReply> ClosePersonalShopAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.CloseAsync(ct);
    }

    public async Task<PersonalShopReply> ViewPersonalShopAsync(ulong sellerId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.ViewAsync(sellerId, ct);
    }

    public async Task<PersonalShopReply> BuyFromPersonalShopAsync(PersonalShopPurchase purchase, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.BuyAsync(purchase, ct);
    }

    public async Task<CoinShopView> OpenCoinShopAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.OpenCoinShopAsync(ct);
    }

    public async Task<(CoinShopStatus Status, long Balance)> BuyCoinShopAsync(int tabId, int itemId, int quantity, int specClass, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.BuyCoinShopAsync(tabId, itemId, quantity, specClass, ct);
    }

    public async Task<CharacterMarketPagePacket> CharacterMarketAsync(CharacterMarketRequestPacket request, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.Markets.CharacterMarketAsync(request, ct);
    }

    public async Task<CharacterDetailView> CharacterMarketDetailAsync(CharacterMarketRequestPacket request, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.Markets.CharacterMarketDetailAsync(request, ct);
    }

    public async Task<MarketplaceView> MarketplaceAsync(MarketplaceQueryPacket query, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.Markets.MarketplaceAsync(query, ct);
    }

    public async Task<PersonalShopReply> BuyFromMarketplaceAsync(ulong npcId, PersonalShopPurchase purchase, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.BuyFromMarketplaceAsync(npcId, purchase, ct);
    }

    public async Task<long> CoinBalanceAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.CoinBalanceAsync(ct);
    }

    public async Task<byte> DailyQuestAsync(ulong npcId, int questId, byte action, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.DailyQuestAsync(npcId, questId, action, ct);
    }

    public async Task<SkinCatalogInfo> OpenSkinsAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.OpenSkinsAsync(ct);
    }

    public async Task<(PristonTale.Simulation.Characters.SkinStatus Status, long Balance)> BuySkinAsync(string key, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.BuySkinAsync(key, ct);
    }

    public async Task<(PristonTale.Simulation.Characters.SkinStatus Status, long Balance)> ApplySkinsAsync(PristonTale.Simulation.Characters.SkinSet skins, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ApplySkinsAsync(skins, ct);
    }

    public async Task<(PristonTale.Simulation.Characters.SkinStatus Status, long Balance, string Key)> UnlockSkinAsync(Guid itemId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.UnlockSkinAsync(itemId, ct);
    }

    public async Task<CabinetDealsPacket> CabinetDealsAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.CabinetDealsAsync(ct);
    }

    public async Task<(PristonTale.Simulation.Characters.CabinetBidStatus Status, long Balance)> CabinetBidAsync(long dealId, long amount, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.CabinetBidAsync(dealId, amount, ct);
    }

    public async Task<ItemBoxListInfo> OpenItemBoxAsync(ulong npcId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.OpenItemBoxAsync(npcId, ct);
    }

    public async Task<byte> ActItemBoxAsync(ulong npcId, byte action, long id, Guid itemId, string recipient, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ActItemBoxAsync(npcId, action, id, itemId, recipient, ct);
    }

    public async Task<WingGateOutcome> TravelWingGateAsync(int destination, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.TravelWingGateAsync(destination, ct);
    }

    public async Task SendMovementAsync(Vector3 position, float facing, bool moving, bool running, ulong actionTarget, int fieldCode, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendMovement(position, facing, moving, running, actionTarget, fieldCode);
    }

    public async Task SendAttackAsync(ulong targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0)
    {
        await _start.WaitAsync(ct);
        _connection.SendAttackRequest(targetEntityId, sustained, sequence);
    }

    public async Task SendSkillCastAsync(uint sequence, uint skillCode, ulong targetEntityId, Vector3? point, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSkillCast(sequence, skillCode, targetEntityId, point);
    }

    public async Task SendChatAsync(string text, bool party, Guid itemId = default, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendChat(text, party, itemId);
    }

    public async Task SendYahooKeyAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendYahooKey();
    }

    public async Task SendSwingAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSwing();
    }

    public async Task SendDismissCrystalPetsAsync(uint statusCode, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendDismissCrystalPets(statusCode);
    }

    public async Task SendClanAsync(ClanAction action, ulong id, string name, CancellationToken ct = default, ulong amount = 0)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new ClanRequestPacket(action, id, name ?? "", amount));
    }

    public async Task SendCastleOwnerAsync(CastleOwnerAction action, ulong npcId, Simulation.Social.Castle.CastleSetup setup, long amount,
        CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new CastleOwnerRequestPacket(action, npcId, (byte)setup.Tax, (byte)setup.Skill, [.. setup.Towers.Select(static tower => (byte)tower)],
            [.. setup.Mercenaries.Select(static count => (byte)Math.Clamp(count, 0, byte.MaxValue))], (ulong)Math.Max(0, amount)));
    }

    public async Task SendBellatraClanAsync(BellatraClanAction action, ulong npcId, long amount, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new BellatraClanRequestPacket(action, npcId, (ulong)Math.Max(0, amount)));
    }

    public async Task SendAchievementTitleAsync(uint id, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new AchievementTitlePacket(id));
    }

    public async Task SendPartyFinderRequestAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(default(PartyFinderRequestPacket));
    }

    public async Task SendPartyFinderSettingsAsync(PartyFinderSettingsPacket settings, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(settings);
    }

    public async Task SendPartyFinderJoinAsync(ulong leaderId, string password, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new PartyFinderJoinPacket(leaderId, password));
    }

    public async Task SendCourierBoardRequestAsync(ulong npcId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new CourierBoardRequestPacket(npcId));
    }

    public async Task<(byte Status, long Experience)> CourierAsync(ulong npcId, int questId, byte action, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.CourierAsync(npcId, questId, action, ct);
    }

    public async Task SendRankingsRequestAsync(RankingBoard board, byte job, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new RankingsRequestPacket(board, job));
    }

    public async Task SendAchievementsRequestAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(default(AchievementsRequestPacket));
    }

    public async Task SendBellatraResultAsync(int team, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new BellatraResultRequestPacket((byte)Math.Clamp(team, 0, Contracts.Social.BellatraLimits.Teams)));
    }

    public async Task SendClanMarkAsync(byte[] pixels, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new ClanMarkPacket(pixels));
    }

    public async Task InviteToPartyAsync(ulong targetId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new PartyInvitePacket(targetId));
    }

    public async Task AnswerPartyAsync(ulong fromId, bool accept, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new PartyAnswerPacket(fromId, accept));
    }

    public async Task CommandPartyAsync(PartyCommand command, ulong memberId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new PartyCommandPacket(command, memberId));
    }

    public async Task RequestTradeAsync(ulong targetId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeRequestPacket(targetId));
    }

    public async Task AnswerTradeAsync(ulong fromId, bool accept, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeAnswerPacket(fromId, accept));
    }

    public async Task OfferTradeAsync(Simulation.Trades.TradeOffer offer, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeOfferPacket(offer.Money, offer.Entries.Select(static entry => new TradeEntryWire(entry.ItemId, entry.Cell)).ToArray(),
            offer.Coins));
    }

    public async Task LockTradeAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeSignalPacket(Protocol.Binary.EtervaleOpcodes.TradeLock));
    }

    public async Task CancelTradeAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeSignalPacket(Protocol.Binary.EtervaleOpcodes.TradeCancel));
    }

    public async Task ChangeContactAsync(ContactList list, bool add, string name, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new ContactChangePacket(list, add, name));
    }

    public async Task RefreshContactsAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new ContactRefreshPacket());
    }

    public async Task SendSkillCommandAsync(uint sequence, uint skillCode, SkillCastCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSkillCast(sequence, skillCode, 0, null, command);
    }

    public async IAsyncEnumerable<PresenceUpdate> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await foreach (var update in _connection.ReadPresenceAsync(linked.Token))
        {
            yield return update;
        }
    }

    public void Recycle(PresenceUpdate update) => _connection.Recycle(update);

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (!_start.IsCompleted)
            {
                await _lifetime.CancelAsync();
            }

            try
            {
                await _start;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }

            if (_start.IsCompletedSuccessfully)
            {
                await _connection.ClosePlayAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            await _lifetime.CancelAsync();
            _lifetime.Dispose();
        }
    }
}
