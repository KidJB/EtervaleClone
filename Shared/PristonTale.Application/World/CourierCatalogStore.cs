using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.World;

// The Quest Courier catalog, read from a JSON file beside the server (data/couriers.json) at start and on a game master's
// reload, and written back when a game master edits a quest. Couriers stand where the file placed them when the world data
// loads (a moved Courier needs a restart); quests change at once. A quest the rules refuse (CourierCatalog.Problem) is left
// out and reported; a missing or unreadable file leaves no Couriers.
public sealed class CourierCatalogStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private volatile CourierCatalog _current = CourierCatalog.Empty;

    public CourierCatalog Current => _current;

    public string Path => path;

    public static CourierCatalog Parse(string json, List<string> problems)
    {
        var read = JsonSerializer.Deserialize<CourierCatalog>(json, Options) ?? CourierCatalog.Empty;
        var quests = new List<CourierQuest>();
        var ids = new HashSet<int>();
        foreach (var quest in read.Quests ?? [])
        {
            var problem = CourierCatalog.Problem(quest) ?? (ids.Add(quest.Id) ? null : "repeats an id");
            if (problem is null)
            {
                quests.Add(quest);
            }
            else
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"quest {quest.Id} '{quest.Name}' {problem}"));
            }
        }

        return new CourierCatalog(read.Couriers ?? [], quests, read.Schedule ?? new CourierSchedule());
    }

    public static string Write(CourierCatalog catalog) => JsonSerializer.Serialize(catalog, Options);

    // What was read, or the reason nothing was.
    public string Reload()
    {
        if (!File.Exists(path))
        {
            _current = CourierCatalog.Empty;
            return $"no Courier file at {path}";
        }

        try
        {
            var problems = new List<string>();
            _current = Parse(File.ReadAllText(path), problems);
            var summary = string.Create(CultureInfo.InvariantCulture, $"Couriers: {_current.Couriers.Count} Couriers, {_current.Quests.Count} quests");
            return problems.Count == 0 ? summary : summary + "; skipped " + string.Join("; ", problems);
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            _current = CourierCatalog.Empty;
            return $"Courier file unreadable: {exception.Message}";
        }
    }

    // A game master's edit: the quest replaced (or added) in the catalog in use and in the file.
    public string Replace(CourierQuest quest)
    {
        if (CourierCatalog.Problem(quest) is { } problem)
        {
            return $"quest {quest.Id} {problem}";
        }

        var current = _current;
        var quests = current.Quests.Where(existing => existing.Id != quest.Id).Append(quest).OrderBy(static existing => existing.Id).ToList();
        var next = new CourierCatalog(current.Couriers, quests, current.Schedule);
        File.WriteAllText(path, Write(next));
        _current = next;
        return string.Create(CultureInfo.InvariantCulture, $"quest {quest.Id} '{quest.Name}' saved");
    }
}
