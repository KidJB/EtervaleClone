namespace PristonTale.Simulation.Quests;

// Quest Courier hunts (data/couriers.json): each hunting field has a Courier offering hunts of the monsters that spawn there.
public enum CourierCategory : byte
{
    // Once per day (the day turns at the schedule's DailyResetHour on the server's clock).
    Daily = 0,
    // Accepted only within the night window (MidnightStartHour to MidnightEndHour), once per day.
    Midnight = 1,
    // Once per character, ever.
    OneTime = 2,
    // Again after each turn-in, once its cooldown has passed.
    Repeatable = 3,
    // Once per week (the week turns on WeeklyResetDay at WeeklyResetHour).
    Weekly = 4
}

public enum CourierDifficulty : byte
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
    Elite = 3
}

// One objective of a hunt: kill Count of the monster (its world data id) in the quest's field.
public sealed record CourierObjective(int MonsterId, string Monster, int Count);

// A Courier hunt. Giver and TurnIn are NPC codes (a standard Courier quest names its own Courier for both). Experience,
// Gold and the item are the turn-in reward; CooldownMinutes applies to Repeatable quests; PartyCredit lets a kill by a
// party member in the same field count too.
public sealed record CourierQuest(
    int Id,
    string Name,
    string Description,
    CourierCategory Category,
    CourierDifficulty Difficulty,
    int FieldCode,
    int Giver,
    int TurnIn,
    int MinLevel,
    int MaxLevel,
    IReadOnlyList<CourierObjective> Objectives,
    long Experience,
    long Gold = 0,
    string ItemKey = "",
    int ItemCount = 0,
    int CooldownMinutes = 0,
    bool PartyCredit = true,
    bool Enabled = true)
{
    public const int MaxObjectives = 4;

    public bool Offered(int level) => Enabled && level >= MinLevel && level <= MaxLevel;

    public bool Requires(int monsterId)
    {
        foreach (var objective in Objectives)
        {
            if (objective.MonsterId == monsterId)
            {
                return true;
            }
        }

        return false;
    }
}

// A Courier: the NPC code it is given, the field it stands in and where (world units; Angle 4096 per turn).
public sealed record CourierPost(int NpcCode, string Name, int FieldCode, int X, int Z, int Angle);

// When the timed categories turn, on the server's local clock.
public sealed record CourierSchedule(int DailyResetHour = 0, DayOfWeek WeeklyResetDay = DayOfWeek.Monday, int WeeklyResetHour = 0,
    int MidnightStartHour = 0, int MidnightEndHour = 2)
{
    // The day a moment belongs to: days turn at DailyResetHour.
    public int DayOf(DateTimeOffset utc) => DateOnly.FromDateTime(Local(utc).AddHours(-DailyResetHour)).DayNumber;

    // The week a moment belongs to: weeks turn on WeeklyResetDay at WeeklyResetHour.
    public int WeekOf(DateTimeOffset utc)
    {
        var shifted = Local(utc).AddHours(-WeeklyResetHour);
        var day = DateOnly.FromDateTime(shifted).DayNumber;
        var sinceReset = ((int)shifted.DayOfWeek - (int)WeeklyResetDay + 7) % 7;
        return (day - sinceReset) / 7;
    }

    // Within the night window; a window past midnight (22 to 2) wraps.
    public bool InMidnightWindow(DateTimeOffset utc)
    {
        var hour = Local(utc).Hour;
        return MidnightStartHour <= MidnightEndHour
            ? hour >= MidnightStartHour && hour < MidnightEndHour
            : hour >= MidnightStartHour || hour < MidnightEndHour;
    }

    private static DateTime Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local).DateTime;
}

// Every Courier and hunt (read from data/couriers.json by the server's CourierCatalogStore).
public sealed record CourierCatalog(IReadOnlyList<CourierPost> Couriers, IReadOnlyList<CourierQuest> Quests, CourierSchedule Schedule)
{
    public const int FirstNpcCode = 9100;
    public const int MaxActive = 8;
    public static readonly CourierCatalog Empty = new([], [], new CourierSchedule());

    private Dictionary<int, CourierQuest> _byId;

    public CourierQuest Get(int id)
    {
        _byId ??= Quests.GroupBy(static quest => quest.Id).ToDictionary(static group => group.Key, static group => group.First());
        return _byId.TryGetValue(id, out var quest) ? quest : null;
    }

    public CourierPost CourierAt(int npcCode)
    {
        foreach (var courier in Couriers)
        {
            if (courier.NpcCode == npcCode)
            {
                return courier;
            }
        }

        return null;
    }

    // The hunts a Courier gives, in catalog order.
    public IEnumerable<CourierQuest> GivenBy(int npcCode) => Quests.Where(quest => quest.Giver == npcCode);

    // Why a definition cannot be used, or null: the store skips such a quest and reports it.
    public static string Problem(CourierQuest quest)
    {
        if (quest.Id <= 0 || string.IsNullOrWhiteSpace(quest.Name))
        {
            return "needs an id and a name";
        }

        if (quest.Objectives is null || quest.Objectives.Count is 0 or > CourierQuest.MaxObjectives
            || quest.Objectives.Any(static objective => objective.MonsterId <= 0 || objective.Count <= 0))
        {
            return $"needs 1 to {CourierQuest.MaxObjectives} objectives with a monster and a count";
        }

        return quest.MinLevel < 1 || quest.MaxLevel < quest.MinLevel || quest.Experience < 0 || quest.Gold < 0 || quest.CooldownMinutes < 0
            ? "has an invalid level range or reward"
            : null;
    }
}
