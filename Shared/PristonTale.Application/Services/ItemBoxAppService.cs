using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.GameLogs;
using PristonTale.Persistence.Abstractions.ItemBoxes;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

public enum ItemBoxStatus : byte
{
    Ok = 0,
    OutOfReach = 1,
    NotFound = 2,
    NoRoom = 3,
    NoSuchCharacter = 4,
    NotSendable = 5,
    Busy = 6,
    StorageFailed = 7,
    Rejected = 8
}

// A waiting item as its account's characters see it.
public sealed record ItemBoxEntryView(long Id, string Sender, DateTime CreatedAt, OwnedItemView Item);

// The Item Distributor outside the tick: every request needs the player at an NPC offering it (GiftExpress). An item taken
// is prepared into the bag, stored with its row removed in one transaction, then committed; an item sent leaves the bag
// and lands in the other account's box in one transaction before its commit.
public sealed class ItemBoxAppService(ISessionRegistry sessions, ICharacterSaves saves, IItemBoxRepository box, WorldServer world, WorldDataHolder worldData)
{
    private readonly DurableOperations _durable = new(saves);

    public async Task<(ItemBoxStatus Status, IReadOnlyList<ItemBoxEntryView> Entries)> ListAsync(string sessionToken, ulong npcEntityId, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session) || session.Character is not { } character)
        {
            return (ItemBoxStatus.Rejected, []);
        }

        if (!await world.AtNpcServiceAsync(session, npcEntityId, NpcServiceKind.GiftExpress))
        {
            return (ItemBoxStatus.OutOfReach, []);
        }

        var entries = new List<ItemBoxEntryView>();
        foreach (var record in await box.ListAsync(session.AccountId, ct))
        {
            if (TryRestore(record.Item) is { } item)
            {
                entries.Add(new ItemBoxEntryView(record.Id, record.Sender, record.CreatedAt,
                    OwnedItemView.Of(item, 0, -1) with { Details = ItemDetailView.Of(item, character) }));
            }
        }

        return (ItemBoxStatus.Ok, entries);
    }

    public async Task<ItemBoxStatus> TakeAsync(string sessionToken, ulong npcEntityId, long id, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return ItemBoxStatus.Rejected;
        }

        if (!await world.AtNpcServiceAsync(session, npcEntityId, NpcServiceKind.GiftExpress))
        {
            return ItemBoxStatus.OutOfReach;
        }

        if (await box.FindAsync(session.AccountId, id, ct) is not { } record || TryRestore(record.Item) is not { } item)
        {
            return ItemBoxStatus.NotFound;
        }

        var stored = true;
        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        var taken = await lease.RunAsync(
            leased => world.PrepareGiftAsync(leased.EntityId, leased, _ => item),
            (leased, prepared) => world.CommitItemBoxAsync(leased, prepared, ItemTransferKind.ItemBoxTaken),
            static prepared => prepared.Outcome,
            static _ => false,
            async (leased, prepared) =>
            {
                stored = await box.TakeWithCharacterAsync(id, leased.Stamp(prepared.Snapshot!), CancellationToken.None);
                return stored ? null : DurableFailure.Conflict;
            },
            storeFirst: static prepared => prepared.Outcome);
        return taken ? ItemBoxStatus.Ok
            : !stored ? ItemBoxStatus.NotFound
            : lease.Failure == DurableFailure.Busy ? ItemBoxStatus.Busy
            : ItemBoxStatus.NoRoom;
    }

    public async Task<ItemBoxStatus> DiscardAsync(string sessionToken, ulong npcEntityId, long id, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return ItemBoxStatus.Rejected;
        }

        if (!await world.AtNpcServiceAsync(session, npcEntityId, NpcServiceKind.GiftExpress))
        {
            return ItemBoxStatus.OutOfReach;
        }

        return await box.DiscardAsync(session.AccountId, id, ct) ? ItemBoxStatus.Ok : ItemBoxStatus.NotFound;
    }

    public async Task<ItemBoxStatus> SendAsync(string sessionToken, ulong npcEntityId, Guid itemId, string recipient, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session) || session.Character is not { } character)
        {
            return ItemBoxStatus.Rejected;
        }

        if (!await world.AtNpcServiceAsync(session, npcEntityId, NpcServiceKind.GiftExpress))
        {
            return ItemBoxStatus.OutOfReach;
        }

        if (string.IsNullOrWhiteSpace(recipient) || await box.AccountOfCharacterAsync(recipient.Trim(), ct) is not { } toAccount)
        {
            return ItemBoxStatus.NoSuchCharacter;
        }

        var senderName = character.Name;
        var stored = true;
        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        var sent = await lease.RunAsync(
            leased => world.PrepareItemBoxSendAsync(leased, itemId),
            (leased, prepared) => world.CommitItemBoxAsync(leased, prepared, ItemTransferKind.ItemBoxSent),
            static prepared => prepared.Outcome is not null,
            static _ => false,
            async (leased, prepared) =>
            {
                stored = await box.SendWithCharacterAsync(leased.Stamp(prepared.Snapshot!), toAccount, senderName, ItemBoxCodec.Capture(prepared.Outcome!),
                    CancellationToken.None);
                return stored ? null : DurableFailure.StorageFailed;
            },
            storeFirst: static prepared => prepared.Outcome is not null);
        return sent ? ItemBoxStatus.Ok
            : !stored ? ItemBoxStatus.StorageFailed
            : lease.Failure == DurableFailure.Busy ? ItemBoxStatus.Busy
            : ItemBoxStatus.NotSendable;
    }

    private ItemInstance? TryRestore(string json)
    {
        try
        {
            return ItemBoxCodec.Restore(json, worldData.Current.Items);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }
}
