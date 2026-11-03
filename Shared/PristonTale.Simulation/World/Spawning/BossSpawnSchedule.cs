using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Simulation.World.Spawning;

// The next boss of a field: its name, when its pass comes (local time) and how long until then. Exact is false when the
// pass falls on a later day, whose minute is rolled only when that day begins (the hour is known, the minute is not).
public readonly record struct BossCountdown(string BossName, DateTime Slot, int SecondsLeft, bool Exact);

public sealed class BossSpawnSchedule
{
    private readonly ISimRng _rng;
    private DateOnly _date;
    private DateTime _lastSlot = DateTime.MinValue;
    private TimeSpan? _lastPassElapsed;

    public int Minute { get; private set; }

    public BossSpawnSchedule(DateTimeOffset localNow, ISimRng rng)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _date = DateOnly.FromDateTime(localNow.DateTime);
        Minute = _rng.NextInclusive(1, 45);
    }

    // The first pass after localNow, today or tomorrow, that one of the bosses spawns in (the first boss of the list when
    // two share an hour); false for a field without bosses. minute is today's (Minute).
    public static bool TryNext(IReadOnlyList<BossSpawn> bosses, DateTimeOffset localNow, int minute, out BossSpawn boss, out DateTime slot, out bool exact)
    {
        var today = localNow.DateTime.Date;
        for (var day = 0; day < 2; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                var at = today.AddDays(day).AddHours(hour).AddMinutes(minute);
                if (at <= localNow.DateTime)
                {
                    continue;
                }

                for (var index = 0; index < bosses.Count; index++)
                {
                    if (bosses[index].SpawnHours.Contains(hour))
                    {
                        boss = bosses[index];
                        slot = at;
                        exact = day == 0;
                        return true;
                    }
                }
            }
        }

        boss = null;
        slot = default;
        exact = false;
        return false;
    }

    public bool TryBeginPass(DateTimeOffset localNow, TimeSpan elapsed)
    {
        var date = DateOnly.FromDateTime(localNow.DateTime);
        if (date < _date)
        {
            return false;
        }

        if (date > _date)
        {
            _date = date;
            Minute = _rng.NextInclusive(1, 45);
        }

        var slot = new DateTime(date.Year, date.Month, date.Day, localNow.Hour, Minute, 0);
        if (localNow.Minute != Minute || slot <= _lastSlot)
        {
            return false;
        }

        if (_lastPassElapsed is TimeSpan previous && elapsed - previous <= TimeSpan.FromMinutes(10))
        {
            return false;
        }

        _lastSlot = slot;
        _lastPassElapsed = elapsed;
        return true;
    }
}
