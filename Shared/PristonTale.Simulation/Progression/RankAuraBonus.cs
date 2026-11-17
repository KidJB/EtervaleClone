using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Progression;

// The bonus a ranking aura gives its wearer while worn: extra experience on kills, by place.
public static class RankAuraBonus
{
    // Percent of extra experience, indexed by RankAura (None, First, Second, Third, Overall).
    private static readonly int[] ExperiencePercents = [0, 10, 7, 5, 10];

    public static int ExperiencePercent(RankAura aura) => (int)aura < ExperiencePercents.Length ? ExperiencePercents[(int)aura] : 0;
}
