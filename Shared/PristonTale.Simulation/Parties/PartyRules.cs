using System.Numerics;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Physics;

namespace PristonTale.Simulation.Parties;

// The original's party rules. Two players group when their levels (a party's average level stands for its members) are
// less than LevelGap apart. A kill's experience is taken at the party's average level and shared by the party's size:
// the killer always gets its share, every other member its own while within ShareRange of the killer. Gold a monster
// dropped, at any amount, and gold a player threw under SharedGoldBelow, picked up by a member, is split evenly among
// the members within ShareRange of the picker, the picker keeping the remainder. A request to join waits InviteMs for
// its answer.
public static class PartyRules
{
    public const int LevelGap = 15;
    public const long SharedGoldBelow = 10_000;
    // The original's request box closes after 1400 frames of 70 per second.
    public const long InviteMs = 20_000;

    // Percent of the kill's experience for the killer and for each other member, by the party's size.
    private static readonly (int Killer, int Other)[] ExpShares = [(100, 0), (65, 65), (50, 50), (45, 45), (40, 40), (37, 37)];

    public static (int Killer, int Other) ExpShare(int members) => ExpShares[Math.Clamp(members, 1, PartyLimits.MaxMembers) - 1];

    // In Exp and Hunt mode the party's total share grows with its size and each member takes an equal part of it.
    public static (int Killer, int Other) ExpShare(int members, PartyMode mode)
    {
        if (mode == PartyMode.Normal || members < 2)
        {
            return ExpShare(members);
        }

        var extra = members - 2;
        var each = (mode == PartyMode.Exp ? 180 + 80 * extra : 80 + 20 * extra) / members;
        return (each, each);
    }

    private static readonly int[] ExpModeExtraChance = [10, 20, 40, 60, 80];
    private static readonly int[] HuntModeExtraChance = [10, 20, 30, 40, 50];

    // Drops a kill gains (Hunt) or loses (Exp) with nearby members around the killer: one, or two when roll (0-99) falls
    // under the chance for that many members.
    public static int DropChange(PartyMode mode, int nearby, int roll)
    {
        if (mode == PartyMode.Normal || nearby < 2)
        {
            return 0;
        }

        var chances = mode == PartyMode.Exp ? ExpModeExtraChance : HuntModeExtraChance;
        var amount = roll < chances[Math.Min(nearby, PartyLimits.MaxMembers) - 2] ? 2 : 1;
        return mode == PartyMode.Exp ? -amount : amount;
    }

    public static ulong Share(ulong experience, int percent) => (ulong)((UInt128)experience * (uint)percent / 100);

    // Integer average, as the original keeps it.
    public static int AverageLevel(ReadOnlySpan<int> levels)
    {
        if (levels.IsEmpty)
        {
            return 0;
        }

        long sum = 0;
        foreach (var level in levels)
        {
            sum += level;
        }

        return (int)(sum / levels.Length);
    }

    public static bool LevelsMatch(int inviterLevel, int targetLevel) => Math.Abs(inviterLevel - targetLevel) < LevelGap;

    public static bool InShareRange(Vector3 from, Vector3 member) => Planar.Within(member, from, PartyLimits.ShareRange);

    public static bool SharesGold(long amount, bool monsterGold) => amount > 0 && (monsterGold || amount < SharedGoldBelow);

    // The picker's part and every other sharer's, for count players sharing amount.
    public static (long Picker, long Each) SplitGold(long amount, int count)
    {
        var each = amount / Math.Max(1, count);
        return (each + amount % Math.Max(1, count), each);
    }
}
