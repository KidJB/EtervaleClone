using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Application.World;

// A boss of the world: its name, the field it spawns in and its next pass (local time; Exact as in BossCountdown).
public readonly record struct BossListEntry(string BossName, string FieldName, DateTime Slot, bool Exact);

// Every scheduled boss of every field, soonest pass first. Build makes a new list; Due says when a pass in it has come,
// so the world builds it again then (and when the day's minute changes) and the encoder sends a new list only then.
public sealed class BossList
{
    private BossList(BossListEntry[] entries, int minute)
    {
        Entries = entries;
        Minute = minute;
    }

    public static BossList Empty { get; } = new([], -1);

    public BossListEntry[] Entries { get; }
    public int Minute { get; }

    public bool Due(DateTimeOffset localNow, int minute) =>
        minute != Minute || Entries.Length > 0 && Entries[0].Slot <= localNow.DateTime;

    public static BossList Build(IReadOnlyDictionary<int, Simulation.World.Fields.FieldSpawnData> spawns, MonsterCatalog monsters, WorldCatalog fields,
        DateTimeOffset localNow, int minute)
    {
        var entries = new List<BossListEntry>();
        foreach (var (fieldCode, data) in spawns)
        {
            var fieldName = fields.TryGetFieldByCode(fieldCode, out var field) ? field.Name : $"Field {fieldCode}";
            foreach (var boss in data.BossSpawns)
            {
                if (BossSpawnSchedule.TryNext([boss], localNow, minute, out _, out var slot, out var exact))
                {
                    var name = monsters.TryGet(boss.BossMonsterCode, out var definition) ? definition.Name : "Boss";
                    entries.Add(new BossListEntry(name, fieldName, slot, exact));
                }
            }
        }

        entries.Sort(static (left, right) => left.Slot != right.Slot ? left.Slot.CompareTo(right.Slot)
            : string.CompareOrdinal(left.BossName, right.BossName) is var byName and not 0 ? byName : string.CompareOrdinal(left.FieldName, right.FieldName));
        return new BossList([.. entries], minute);
    }
}
