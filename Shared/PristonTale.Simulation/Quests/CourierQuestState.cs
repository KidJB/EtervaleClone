namespace PristonTale.Simulation.Quests;

public enum CourierStatus : byte
{
    Ok = 0,
    // Disabled, unknown, or the player's level is outside its range.
    NotOffered = 1,
    AlreadyAccepted = 2,
    // Turned in within its period (today, this week, ever for a one-time quest).
    Completed = 3,
    // A repeatable quest waits for its cooldown.
    Cooldown = 4,
    // A midnight quest outside its window.
    Closed = 5,
    TooMany = 6,
    NotAccepted = 7,
    NotFinished = 8,
    NoRoom = 9,
    NotAtCourier = 10,
    Rejected = 11
}

// How a hunt stands for a player at a Courier, as the board shows it.
public enum CourierStanding : byte
{
    Available = 0,
    Active = 1,
    Ready = 2,
    Completed = 3,
    Cooldown = 4,
    Closed = 5
}

// One accepted hunt: the kills of each objective (in the quest's objective order) and when it was accepted (Unix seconds).
public sealed class CourierProgress(int[] kills, long acceptedAt)
{
    public int[] Kills { get; } = kills;
    public long AcceptedAt { get; } = acceptedAt;

    public CourierProgress Copy() => new([.. Kills], AcceptedAt);
}

// One character's Courier hunts: the accepted ones with their kills, and when each quest was last turned in (Unix seconds),
// which decides when a quest may be taken again (CourierCategory). A level outside a quest's range only stops accepting
// it: an accepted hunt can still be finished and turned in.
public sealed class CourierQuestState
{
    private readonly Dictionary<int, CourierProgress> _active = new();
    private readonly Dictionary<int, long> _turnedIn = new();

    public long Revision { get; private set; }
    public IReadOnlyDictionary<int, CourierProgress> Active => _active;
    public IReadOnlyDictionary<int, long> TurnedIn => _turnedIn;

    public void Restore(IEnumerable<KeyValuePair<int, CourierProgress>> active, IEnumerable<KeyValuePair<int, long>> turnedIn)
    {
        _active.Clear();
        _turnedIn.Clear();
        foreach (var (id, progress) in active)
        {
            _active[id] = progress.Copy();
        }

        foreach (var (id, at) in turnedIn)
        {
            _turnedIn[id] = at;
        }

        Revision++;
    }

    public void CopyFrom(CourierQuestState other)
    {
        Restore(other._active, other._turnedIn);
        Revision = other.Revision;
    }

    // Every objective has its count; kills past a lowered count still finish it.
    public static bool Finished(CourierQuest quest, CourierProgress progress)
    {
        for (var index = 0; index < quest.Objectives.Count; index++)
        {
            if (index >= progress.Kills.Length || progress.Kills[index] < quest.Objectives[index].Count)
            {
                return false;
            }
        }

        return true;
    }

    // Whether the quest was turned in within its period, so it cannot be taken again yet.
    public CourierStatus Repeat(CourierQuest quest, DateTimeOffset now, CourierSchedule schedule)
    {
        if (!_turnedIn.TryGetValue(quest.Id, out var at))
        {
            return CourierStatus.Ok;
        }

        var then = DateTimeOffset.FromUnixTimeSeconds(at);
        return quest.Category switch
        {
            CourierCategory.OneTime => CourierStatus.Completed,
            CourierCategory.Daily or CourierCategory.Midnight => schedule.DayOf(then) == schedule.DayOf(now) ? CourierStatus.Completed : CourierStatus.Ok,
            CourierCategory.Weekly => schedule.WeekOf(then) == schedule.WeekOf(now) ? CourierStatus.Completed : CourierStatus.Ok,
            _ => now < then.AddMinutes(quest.CooldownMinutes) ? CourierStatus.Cooldown : CourierStatus.Ok
        };
    }

    public CourierStanding Standing(CourierQuest quest, DateTimeOffset now, CourierSchedule schedule)
    {
        if (_active.TryGetValue(quest.Id, out var progress))
        {
            return Finished(quest, progress) ? CourierStanding.Ready : CourierStanding.Active;
        }

        return Repeat(quest, now, schedule) switch
        {
            CourierStatus.Completed => CourierStanding.Completed,
            CourierStatus.Cooldown => CourierStanding.Cooldown,
            _ => quest.Category == CourierCategory.Midnight && !schedule.InMidnightWindow(now) ? CourierStanding.Closed : CourierStanding.Available
        };
    }

    public CourierStatus Accept(CourierQuest quest, int level, DateTimeOffset now, CourierSchedule schedule)
    {
        if (!quest.Offered(level))
        {
            return CourierStatus.NotOffered;
        }

        if (_active.ContainsKey(quest.Id))
        {
            return CourierStatus.AlreadyAccepted;
        }

        var repeat = Repeat(quest, now, schedule);
        if (repeat != CourierStatus.Ok)
        {
            return repeat;
        }

        if (quest.Category == CourierCategory.Midnight && !schedule.InMidnightWindow(now))
        {
            return CourierStatus.Closed;
        }

        if (_active.Count >= CourierCatalog.MaxActive)
        {
            return CourierStatus.TooMany;
        }

        _active[quest.Id] = new CourierProgress(new int[quest.Objectives.Count], now.ToUnixTimeSeconds());
        Revision++;
        return CourierStatus.Ok;
    }

    public CourierStatus Abandon(int questId)
    {
        if (!_active.Remove(questId))
        {
            return CourierStatus.NotAccepted;
        }

        Revision++;
        return CourierStatus.Ok;
    }

    // A kill in a field counts once for every objective of that monster in every accepted hunt of that field.
    public bool OnKill(int monsterId, int fieldCode, CourierCatalog catalog) => Count(monsterId, fieldCode, catalog, false);

    // A party member's kill counts only for hunts that share party credit.
    public bool OnPartyKill(int monsterId, int fieldCode, CourierCatalog catalog) => Count(monsterId, fieldCode, catalog, true);

    private bool Count(int monsterId, int fieldCode, CourierCatalog catalog, bool party)
    {
        var changed = false;
        foreach (var (id, progress) in _active)
        {
            if (catalog.Get(id) is not { } quest || quest.FieldCode != fieldCode || party && !quest.PartyCredit)
            {
                continue;
            }

            for (var index = 0; index < quest.Objectives.Count && index < progress.Kills.Length; index++)
            {
                if (quest.Objectives[index].MonsterId == monsterId && progress.Kills[index] < quest.Objectives[index].Count)
                {
                    progress.Kills[index]++;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Revision++;
        }

        return changed;
    }

    // A midnight hunt is accepted only in its window but turned in at any time.
    public CourierStatus CanTurnIn(CourierQuest quest)
    {
        if (!_active.TryGetValue(quest.Id, out var progress))
        {
            return CourierStatus.NotAccepted;
        }

        if (!Finished(quest, progress))
        {
            return CourierStatus.NotFinished;
        }

        return CourierStatus.Ok;
    }

    public void MarkTurnedIn(int questId, DateTimeOffset now)
    {
        _active.Remove(questId);
        _turnedIn[questId] = now.ToUnixTimeSeconds();
        Revision++;
    }

    // A game master's reset: every accepted hunt and record, or only the records of one category.
    public void Reset(CourierCatalog catalog, CourierCategory? category)
    {
        if (category is null)
        {
            _active.Clear();
            _turnedIn.Clear();
        }
        else
        {
            foreach (var id in _turnedIn.Keys.ToArray())
            {
                if (catalog.Get(id)?.Category == category)
                {
                    _turnedIn.Remove(id);
                }
            }
        }

        Revision++;
    }
}
