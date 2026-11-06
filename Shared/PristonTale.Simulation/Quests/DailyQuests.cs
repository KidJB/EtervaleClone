using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Quests;

// A daily hunt: a player of MinLevel to MaxLevel accepts it from the daily quest giver, kills Kills monsters of at least
// MonsterLevel, and claims its reward there once a day. ExpPercent is a share of the experience the player's level needs.
public sealed record DailyQuest(int Id, string Name, int MinLevel, int MaxLevel, int Kills, int MonsterLevel, int ExpPercent, long Gold, int Coins,
    string ItemKey, int ItemCount, string Story);

// The daily quests, given by Messenger Ian in Ricarten; every quest in a player's level range is offered each day, and the
// day turns at midnight on the server's clock.
public static class DailyQuests
{
    public const string GiverName = "Messenger Ian";

    public static readonly IReadOnlyList<DailyQuest> All =
    [
        Hunt(1, "Pests of Ricarten", 10, 24, 30, "PL102", "Ian needs the roads around town cleared of pests."),
        Hunt(2, "The Bamboo Patrol", 20, 34, 40, "PL102", "The bamboo forest grows restless; thin out its creatures."),
        Hunt(3, "Shadows in the Ruins", 30, 44, 50, "PL103", "Something stirs in the old ruins. Push it back."),
        Hunt(4, "The Cursed Woods", 40, 54, 60, "PL103", "Cursed things wander the woods at night. Hunt them."),
        Hunt(5, "Desert Raiders", 50, 64, 70, "PL103", "Raiders from the desert harry the caravans."),
        Hunt(6, "The Iron Menace", 60, 74, 80, "PL104", "Iron beasts gather near the mines. Break their ranks."),
        Hunt(7, "Into the Deep", 70, 84, 90, "PL104", "Dark creatures crawl up from the depths."),
        Hunt(8, "Babel's Army", 80, 84, 100, "PL104", "Babel's army marches again. Meet it in the field."),
        Hunt(9, "The Second Army", 85, 89, 100, "PL104", "A second army follows the first. Hold the line."),
        Hunt(10, "Help Andrei", 90, 94, 110, "PL104", "Andrei is surrounded. Clear a way to him."),
        Hunt(11, "The Bosses' Army", 95, 99, 110, "PL104", "The bosses' guard has gathered. Scatter it."),
        Hunt(12, "The New Island", 100, 104, 120, "PL104", "A new island rose from the sea, and it is not empty."),
        Hunt(13, "The Lost Temple", 105, 109, 120, "PL104", "The lost temple's guardians have woken."),
        Hunt(14, "Help Muriel", 110, 114, 130, "PL104", "Muriel's camp is under attack. Go."),
        Hunt(15, "The Gorgonyte Threat", 115, 999, 140, "PL104", "The Gorgonyte threat grows every day. End it for today.")
    ];

    private static DailyQuest Hunt(int id, string name, int minLevel, int maxLevel, int kills, string potion, string story) =>
        new(id, name, minLevel, maxLevel, kills, Math.Max(1, minLevel - 5), 4, minLevel * 600L, minLevel >= 80 ? 3 : 1, potion, 20, story);

    public static DailyQuest Get(int id) => id >= 1 && id <= All.Count ? All[id - 1] : null;

    public static bool Offered(DailyQuest quest, int level) => level >= quest.MinLevel && level <= quest.MaxLevel;

    // The experience a claim gives a player of that level: ExpPercent of what the level needs to the next.
    public static ulong Experience(DailyQuest quest, int level)
    {
        if (level >= ExperienceTable.MaximumLevel)
        {
            return 0;
        }

        var need = ExperienceTable.ForLevel(level + 1) - ExperienceTable.ForLevel(level);
        return need / 100 * (ulong)quest.ExpPercent;
    }

    // The day number of a moment on the server's clock: the day turns at its local midnight.
    public static int DayOf(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local).DateTime).DayNumber;
}

public enum DailyQuestStatus : byte
{
    Ok = 0,
    NotOffered = 1,
    AlreadyAccepted = 2,
    DoneToday = 3,
    NotAccepted = 4,
    NotFinished = 5,
    NoRoom = 6,
    NotAtGiver = 7,
    Rejected = 8
}

// One character's daily quests for the current day: the ones accepted with their kills, and the ones claimed. A new day
// clears both (Turn).
public sealed class DailyQuestState
{
    private readonly Dictionary<int, int> _accepted = new();
    private readonly HashSet<int> _done = new();

    public int Day { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyDictionary<int, int> Accepted => _accepted;
    public IReadOnlySet<int> Done => _done;

    public void Restore(int day, IEnumerable<KeyValuePair<int, int>> accepted, IEnumerable<int> done)
    {
        Day = day;
        _accepted.Clear();
        _done.Clear();
        foreach (var (id, kills) in accepted)
        {
            if (DailyQuests.Get(id) is { } quest)
            {
                _accepted[id] = Math.Clamp(kills, 0, quest.Kills);
            }
        }

        foreach (var id in done)
        {
            if (DailyQuests.Get(id) is not null)
            {
                _done.Add(id);
            }
        }
    }

    public void CopyFrom(DailyQuestState other)
    {
        Restore(other.Day, other._accepted, other._done);
        Revision = other.Revision;
    }

    // A new day starts with nothing accepted or claimed; true when it turned.
    public bool Turn(int day)
    {
        if (day == Day)
        {
            return false;
        }

        Day = day;
        _accepted.Clear();
        _done.Clear();
        Revision++;
        return true;
    }

    public DailyQuestStatus Accept(int id, int level, int day)
    {
        Turn(day);
        if (DailyQuests.Get(id) is not { } quest || !DailyQuests.Offered(quest, level))
        {
            return DailyQuestStatus.NotOffered;
        }

        if (_done.Contains(id))
        {
            return DailyQuestStatus.DoneToday;
        }

        if (!_accepted.TryAdd(id, 0))
        {
            return DailyQuestStatus.AlreadyAccepted;
        }

        Revision++;
        return DailyQuestStatus.Ok;
    }

    // A kill counts for every accepted hunt of the day it is high enough for.
    public void OnKill(int monsterLevel, int day)
    {
        if (Turn(day) || _accepted.Count == 0)
        {
            return;
        }

        foreach (var id in _accepted.Keys.ToArray())
        {
            var quest = DailyQuests.Get(id);
            if (monsterLevel >= quest.MonsterLevel && _accepted[id] < quest.Kills)
            {
                _accepted[id]++;
                Revision++;
            }
        }
    }

    public DailyQuestStatus CanClaim(int id, int day)
    {
        Turn(day);
        if (_done.Contains(id))
        {
            return DailyQuestStatus.DoneToday;
        }

        if (!_accepted.TryGetValue(id, out var kills))
        {
            return DailyQuestStatus.NotAccepted;
        }

        return kills >= DailyQuests.Get(id).Kills ? DailyQuestStatus.Ok : DailyQuestStatus.NotFinished;
    }

    public void MarkClaimed(int id)
    {
        _accepted.Remove(id);
        _done.Add(id);
        Revision++;
    }
}
