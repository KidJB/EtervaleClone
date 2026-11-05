using System.Text.Json;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Persistence.Abstractions.ItemBoxes;

// One item waiting in an account's Item Distributor: who sent it, when, and the item (a SavedItem in JSON).
public sealed record ItemBoxRecord(long Id, string Sender, string Item, DateTime CreatedAt);

// The Item Distributor: items waiting for an account (sent by a player, returned from a stall, given by the server) until
// one of its characters takes them into the bag or discards them.
public interface IItemBoxRepository
{
    // At most MaxListed waiting items, oldest first.
    Task<IReadOnlyList<ItemBoxRecord>> ListAsync(long accountId, CancellationToken ct = default);

    Task<ItemBoxRecord?> FindAsync(long accountId, long id, CancellationToken ct = default);

    Task<long> AddAsync(long accountId, string sender, string item, CancellationToken ct = default);

    // Takes the item into the character (written with it) in one transaction; false when it was already taken or the
    // character row was not written.
    Task<bool> TakeWithCharacterAsync(long id, CharacterRecord character, CancellationToken ct = default);

    Task<bool> DiscardAsync(long accountId, long id, CancellationToken ct = default);

    // Writes the sender's character (without the item) and the item into the receiving account's box in one transaction.
    Task<bool> SendWithCharacterAsync(CharacterRecord sender, long toAccountId, string senderName, string item, CancellationToken ct = default);

    // The account of the character with that name, or null when none has it.
    Task<long?> AccountOfCharacterAsync(string characterName, CancellationToken ct = default);
}

public static class ItemBoxLimits
{
    public const int MaxListed = 100;
}

// An item in the box's JSON form, the bag's saved item form without a place.
public static class ItemBoxCodec
{
    public static string Capture(ItemInstance item) => JsonSerializer.Serialize(CharacterStateCodec.CaptureItem(item, 0, null), CharacterStateCodec.Options);

    public static ItemInstance Restore(string json, ItemCatalog catalog)
    {
        var saved = JsonSerializer.Deserialize<SavedItem>(json, CharacterStateCodec.Options) ?? throw new InvalidDataException("An item box item is empty.");
        return CharacterStateCodec.RestoreItem(saved with { Anchor = 0, Slot = null, Belt = null, QuestBox = null, Hidden = false }, catalog, []);
    }
}
