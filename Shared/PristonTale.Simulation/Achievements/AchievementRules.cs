namespace PristonTale.Simulation.Achievements;

public enum AchievementId
{
    GameMaster = 1,
    LegacyMember = 2,
    Streamer = 3,
    TopLevel115 = 4,
    TopLevel125 = 5,
    TopLevel130 = 6,
    TopLevel145 = 7
}

public readonly record struct TopLevelTier(int Level, AchievementId Id);

// The badges: the first player of each class to reach a tier's level holds its top badge; game masters grant the others.
public static class AchievementRules
{
    public static readonly TopLevelTier[] TopLevels =
    [
        new(115, AchievementId.TopLevel115),
        new(125, AchievementId.TopLevel125),
        new(130, AchievementId.TopLevel130),
        new(145, AchievementId.TopLevel145)
    ];

    // Which badge names a holder of several, best first.
    private static readonly AchievementId[] TitleOrder =
    [
        AchievementId.TopLevel145, AchievementId.TopLevel130, AchievementId.TopLevel125, AchievementId.TopLevel115,
        AchievementId.GameMaster, AchievementId.Streamer, AchievementId.LegacyMember
    ];

    public const int NoTitle = 0;
    private const int TitleClassShift = 8;
    private const int TitleIdMask = 0xFF;

    // A player's title over its name: the best badge and, for a top badge, its class; 0 for none.
    public static int TitleOf(IEnumerable<(int Id, long HolderKey)> earned, int? chosen)
    {
        if (chosen is NoTitle)
        {
            return 0;
        }

        if (chosen is { } id)
        {
            foreach (var badge in earned)
            {
                if (badge.Id == id)
                {
                    return Title((AchievementId)id, badge.HolderKey);
                }
            }
        }

        return TitleOf(earned);
    }

    private static int Title(AchievementId id, long holderKey) => (int)id | (IsTopLevel(id) ? (int)holderKey << TitleClassShift : 0);

    public static int TitleOf(IEnumerable<(int Id, long HolderKey)> earned)
    {
        var held = earned.ToArray();
        foreach (var id in TitleOrder)
        {
            foreach (var badge in held)
            {
                if (badge.Id == (int)id)
                {
                    return Title(id, badge.HolderKey);
                }
            }
        }

        return 0;
    }

    public static AchievementId TitleId(int title) => (AchievementId)(title & TitleIdMask);

    public static long TitleHolder(int title) => title >> TitleClassShift;

    public static bool IsTopLevel(AchievementId id) => id is >= AchievementId.TopLevel115 and <= AchievementId.TopLevel145;

    public static bool IsGranted(int id) => Enum.IsDefined((AchievementId)id) && !IsTopLevel((AchievementId)id);

    public static int LevelOf(AchievementId id)
    {
        foreach (var tier in TopLevels)
        {
            if (tier.Id == id)
            {
                return tier.Level;
            }
        }

        return 0;
    }

    public static bool Crosses(TopLevelTier tier, int fromLevel, int toLevel) => fromLevel < tier.Level && toLevel >= tier.Level;
}
