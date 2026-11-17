namespace PristonTale.Contracts.Characters;

// One skin of The Cabinet: Slot is a SkinSlot, Owned whether the account has it; Category a CosmeticCategory, Rarity an
// ItemRarity and Sources the CosmeticSource flags of how it is unlocked (without Coins it is not sold).
public readonly record struct SkinArticle(string Key, string Name, byte Slot, int Level, int Price, bool Owned)
{
    public byte Category { get; init; }
    public byte Rarity { get; init; }
    public byte Sources { get; init; }
    public string Description { get; init; } = "";
}
