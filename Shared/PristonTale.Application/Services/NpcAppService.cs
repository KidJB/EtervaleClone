using System.Text.Json;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Caravans;
using PristonTale.Persistence.Abstractions.Clans;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.Services;

// Typed NPC services outside the tick: the world validates and prepares, then commits (DurableOperations). A change to the
// account warehouse is stored with the character before its commit; any other change is stored in the background.
public sealed class NpcAppService(
    ISessionRegistry sessions,
    ICharacterSaves saves,
    IWarehouseRepository warehouses,
    WorldServer world,
    WorldDataHolder worldData,
    ICaravanStorageRepository? caravans = null,
    IClanStorageRepository? clanStorage = null)
{
    private readonly DurableOperations _durable = new(saves);

    public async Task<NpcServiceReply> RequestAsync(string sessionToken, NpcServiceCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        if (lease.Failure is null && command.Service == NpcServiceKind.Warehouse && command.Action == NpcServiceAction.Open
            && await OpenWarehouseAsync(lease.Session, command, ct) is { } opened)
        {
            return opened;
        }

        if (lease.Failure is null && command.Service == NpcServiceKind.Caravan && command.Action == NpcServiceAction.Open
            && await OpenCaravanAsync(lease.Session, ct) is { } refused)
        {
            return refused;
        }

        if (lease.Failure is null && command.Service == NpcServiceKind.ClanWarehouse && command.Action == NpcServiceAction.Open
            && await OpenClanWarehouseAsync(lease.Session, ct) is { } unreadable)
        {
            return unreadable;
        }

        return await lease.RunAsync(
            session => world.PrepareNpcServiceAsync(session, command),
            world.CommitNpcServiceAsync,
            prepared => prepared.Reply,
            failure => Failed(failure switch
            {
                DurableFailure.Busy => NpcServiceStatus.Busy,
                DurableFailure.StorageFailed => NpcServiceStatus.StorageFailed,
                DurableFailure.Conflict => NpcServiceStatus.Conflict,
                _ => NpcServiceStatus.Rejected
            }, DurableFailures.Text(failure)),
            StoreAsync,
            storeFirst: static prepared => prepared.Warehouse is not null);
    }

    // A change to the warehouse is stored with the character. A warehouse stored elsewhere since this session read it is
    // a conflict, and the session drops its copy.
    private async Task<DurableFailure?> StoreAsync(PlayerSession session, PreparedNpcOperation prepared)
    {
        if (prepared.ClanWarehouse != 0)
        {
            return await StoreClanWarehouseAsync(session, prepared);
        }

        if (prepared.CaravanStorage)
        {
            var record = WarehouseStateCodec.Capture(session.AccountId, prepared.Warehouse!);
            if (caravans is not null && await caravans.SaveWithCharacterAsync(session.Stamp(prepared.Snapshot!), new CaravanStorageRecord(session.Loaded!.Id, record.Revision, record.Items),
                    prepared.ExpectedWarehouseRevision, CancellationToken.None))
            {
                return null;
            }

            session.Caravan = null;
            return DurableFailure.Conflict;
        }

        if (await warehouses.SaveWithCharacterAsync(session.Stamp(prepared.Snapshot!), WarehouseStateCodec.Capture(session.AccountId, prepared.Warehouse!),
                prepared.ExpectedWarehouseRevision, CancellationToken.None))
        {
            return null;
        }

        session.Warehouse = null;
        return DurableFailure.Conflict;
    }

    // The warehouse is read from storage on every open, after the NPC and its reach are confirmed.
    private async Task<NpcServiceReply?> OpenWarehouseAsync(PlayerSession session, NpcServiceCommand command, CancellationToken ct)
    {
        var check = await world.CheckNpcServiceAsync(session, command);
        if (!check.Ok)
        {
            return new NpcServiceReply(check);
        }

        try
        {
            var record = await warehouses.FindAsync(session.AccountId, ct);
            session.Warehouse = WarehouseStateCodec.Restore(record, worldData.Current.Items);
            return null;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            session.Warehouse = null;
            return Failed(NpcServiceStatus.StorageFailed, GameText.Of(GameTextId.WarehouseUnreadable));
        }
    }

    // The caravan storage is read on every open; whether the caravan is out is checked on the tick (CaravanTransactions).
    private async Task<NpcServiceReply?> OpenCaravanAsync(PlayerSession session, CancellationToken ct)
    {
        try
        {
            var record = session.Loaded is { } loaded && caravans is not null ? await caravans.FindAsync(loaded.Id, ct) : null;
            session.Caravan = WarehouseStateCodec.Restore(record is null ? null : new WarehouseRecord(session.AccountId, 0, record.Revision, record.Items),
                worldData.Current.Items);
            return null;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            session.Caravan = null;
            return Failed(NpcServiceStatus.StorageFailed, GameText.Of(GameTextId.WarehouseUnreadable));
        }
    }

    // A change to the clan warehouse is stored with the character and its log lines against the revision it was made from.
    // A warehouse another member stored first is a conflict: the world is given the stored one, which every open view then
    // shows.
    private async Task<DurableFailure?> StoreClanWarehouseAsync(PlayerSession session, PreparedNpcOperation prepared)
    {
        var clanId = prepared.ClanWarehouse;
        var log = (prepared.ClanLog ?? []).Select(line => new ClanWarehouseLogRecord(clanId, line.AtSeconds, line.Character, (short)line.Action,
            line.ItemKey, line.ItemName, line.Quantity)).ToArray();
        var record = new ClanWarehouseRecord(clanId, prepared.Warehouse!.Revision, WarehouseStateCodec.CaptureItems(prepared.Warehouse));
        if (clanStorage is not null && await clanStorage.SaveWarehouseWithCharacterAsync(session.Stamp(prepared.Snapshot!), record,
                prepared.ExpectedWarehouseRevision, log, CancellationToken.None))
        {
            return null;
        }

        await LoadClanWarehouseAsync(clanId, CancellationToken.None);
        return DurableFailure.Conflict;
    }

    // The clan warehouse is read once for the world, when a member first opens it; the world keeps it from then on.
    private async Task<NpcServiceReply?> OpenClanWarehouseAsync(PlayerSession session, CancellationToken ct)
    {
        var (clanId, load) = await world.ClanWarehouseToLoadAsync(session);
        if (!load)
        {
            return null;
        }

        return await LoadClanWarehouseAsync(clanId, ct) ? null : Failed(NpcServiceStatus.StorageFailed, GameText.Of(GameTextId.WarehouseUnreadable));
    }

    private async Task<bool> LoadClanWarehouseAsync(long clanId, CancellationToken ct)
    {
        try
        {
            var record = clanStorage is null ? null : await clanStorage.FindWarehouseAsync(clanId, ct);
            var storage = Simulation.Social.Clans.ClanWarehouse.Create(record?.Revision ?? 0, record?.SlotLevel ?? 0, record?.WeightLevel ?? 0);
            await world.ProvideClanWarehouseAsync(clanId, record is null ? storage : WarehouseStateCodec.RestoreItems(storage, record.Items, worldData.Current.Items));
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException or System.Data.Common.DbException)
        {
            return false;
        }
    }

    private static NpcServiceReply Failed(NpcServiceStatus status, GameText message) => new(NpcServiceOutcome.Failed(status, message));
}
