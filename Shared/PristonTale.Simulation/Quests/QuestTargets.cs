namespace PristonTale.Simulation.Quests;

// The kills that count for a quest right now: Monsters are quest codes, or AnyMonster counts every monster; Field limits
// where (AnyField: everywhere). The kill credit and the markers over the monsters read the same value.
public sealed record QuestHunt(IReadOnlyList<int> Monsters, int Field = QuestHunt.AnyField, bool AnyMonster = false)
{
    public const int AnyField = -1;
    public static readonly QuestHunt None = new([]);

    public bool Includes(int monsterCode, int fieldCode)
    {
        if (Field != AnyField && Field != fieldCode)
        {
            return false;
        }

        if (AnyMonster)
        {
            return true;
        }

        for (var index = 0; index < Monsters.Count; index++)
        {
            if (Monsters[index] == monsterCode)
            {
                return true;
            }
        }

        return false;
    }
}

public static class QuestTargets
{
    // The hunt of a quest that names one prey (ActiveQuest.Hunting); the families with other rules answer for themselves.
    public static QuestHunt Prey(ActiveQuest active) =>
        active.Target != 0 && active.KillsNeeded > 0 && !active.HuntComplete ? new QuestHunt([active.Target]) : QuestHunt.None;

    public static bool IsPrey(ActiveQuest active, int monsterCode) =>
        active.Target != 0 && monsterCode == active.Target && active.KillsNeeded > 0 && !active.HuntComplete;

    public static QuestHunt Of(Characters.PlayerCharacter character)
    {
        var active = character.Quests.Active;
        if (EliteQuests.IsElite(active.Code))
        {
            return EliteQuests.Hunt(active);
        }

        if (active.Code == FuryOfPhantomQuest.Code)
        {
            return FuryOfPhantomQuest.Hunt(active);
        }

        return active.Code == RankUpByQuestWeapon.Code
            ? RankUpByQuestWeapon.Hunt(active, QuestClasses.WeaponHunt(QuestClasses.ClassOf(character)))
            : Prey(active);
    }
}
