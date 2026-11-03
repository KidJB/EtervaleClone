namespace PristonTale.Application.World;

// A boss of a field at its spot (wire x, z), and whether its master is alive in the field now.
public readonly record struct BossSpotView(string BossName, float X, float Z, bool Alive);
