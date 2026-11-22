using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.Caravans;

// A character's caravan storage: Items hold SavedItems JSON whose anchors are cells of the warehouse grid.
public sealed record CaravanStorageRecord(long CharacterId, long Revision, JsonText Items);

public interface ICaravanStorageRepository
{
    Task<CaravanStorageRecord?> FindAsync(long characterId, CancellationToken ct = default);

    // Writes the character and its caravan storage in one transaction. The storage row must still be at expectedRevision
    // (no row counts as revision 0); otherwise nothing is written and the result is false.
    Task<bool> SaveWithCharacterAsync(CharacterRecord character, CaravanStorageRecord storage, long expectedRevision, CancellationToken ct = default);
}
