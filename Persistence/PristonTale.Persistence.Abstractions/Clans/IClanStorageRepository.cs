using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.Clans;

// A clan's warehouse: Items hold SavedItems JSON whose anchors span its pages; the clan's upgrade levels come with it.
public sealed record ClanWarehouseRecord(long ClanId, long Revision, JsonText Items, int SlotLevel = 0, int WeightLevel = 0);

// A clan warehouse log row: action as Simulation.Social.Clans.ClanWarehouseLogAction, time in Unix seconds.
public sealed record ClanWarehouseLogRecord(long ClanId, long At, string CharacterName, short Action, string ItemKey, string ItemName, int Quantity);

// A clan's running world buff: its kind and when it ends (Unix seconds).
public sealed record ClanBuffRecord(long ClanId, short Kind, long EndsAt);

// What clans own besides their members: the warehouse with its log, and the running world buffs.
public interface IClanStorageRepository
{
    // The clan's warehouse with its levels (revision 0 and no items before anything was stored); null without the clan.
    Task<ClanWarehouseRecord?> FindWarehouseAsync(long clanId, CancellationToken ct = default);

    // Writes the character, the clan's warehouse and its new log lines in one transaction. The warehouse row must still be
    // at expectedRevision (no row counts as revision 0); otherwise nothing is written and the result is false.
    Task<bool> SaveWarehouseWithCharacterAsync(CharacterRecord character, ClanWarehouseRecord warehouse, long expectedRevision,
        IReadOnlyList<ClanWarehouseLogRecord> log, CancellationToken ct = default);

    // The clan's latest log lines, newest first.
    Task<IReadOnlyList<ClanWarehouseLogRecord>> WarehouseLogAsync(long clanId, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<ClanBuffRecord>> LoadBuffsAsync(CancellationToken ct = default);

    // Replaces every stored clan buff with these (those of clans no longer stored are left out).
    Task ReplaceBuffsAsync(IReadOnlyList<ClanBuffRecord> buffs, CancellationToken ct = default);
}
