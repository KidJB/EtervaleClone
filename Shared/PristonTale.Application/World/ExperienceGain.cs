namespace PristonTale.Application.World;

// Experience a kill gave a player; PartySize 0 when it was not shared.
public readonly record struct ExperienceGain(ulong PlayerId, ulong Amount, int PartyPercent, int PartySize);
