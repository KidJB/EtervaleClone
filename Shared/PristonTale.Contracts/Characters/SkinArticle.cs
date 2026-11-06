namespace PristonTale.Contracts.Characters;

// One skin of The Cabinet: Slot is a SkinSlot, Owned whether the account has it.
public readonly record struct SkinArticle(string Key, string Name, byte Slot, int Level, int Price, bool Owned);
