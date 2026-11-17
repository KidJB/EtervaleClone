using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

// The Cabinet's settings, read from a JSON file beside the server (data/cabinet.json) at start and on a game master's
// reload, and written back when a game master edits a look or a drop. Token items are made when the world data loads (a new
// token needs a restart to exist as an item); prices, sources, rarity and drops change at once. A rule or drop the rules
// refuse (CabinetConfig.Problem) is left out and reported; a missing or unreadable file leaves every look at its defaults.
public sealed class CabinetConfigStore(string path)
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

    private volatile CabinetConfig _current = CabinetConfig.Empty;

    public CabinetConfig Current => _current;

    public string Path => path;

    public static CabinetConfig Parse(string json, List<string> problems)
    {
        var read = (JsonSerializer.Deserialize<CabinetConfig>(json, Options) ?? CabinetConfig.Empty).Normalized();
        var rules = new List<CosmeticRule>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in read.Cosmetics)
        {
            var normalized = rule with { Description = rule.Description ?? "", Token = rule.Token ?? "" };
            var problem = CabinetConfig.Problem(normalized)
                ?? (keys.Add(normalized.Key) ? null : "repeats a look")
                ?? (normalized.Token.Length == 0 || tokens.Add(normalized.Token) ? null : "repeats a token");
            if (problem is null)
            {
                rules.Add(normalized);
            }
            else
            {
                problems.Add($"look '{rule.Key}' {problem}");
            }
        }

        var drops = new List<CosmeticDrop>();
        foreach (var drop in read.Drops)
        {
            var problem = drop.Item is null ? "names no item" : CabinetConfig.Problem(drop);
            if (problem is not null)
            {
                problems.Add($"drop of '{drop.Item}' {problem}");
            }
            else
            {
                drops.Add(drop);
            }
        }

        return new CabinetConfig(rules, drops);
    }

    public static string Write(CabinetConfig config) => JsonSerializer.Serialize(config, Options);

    public string Reload()
    {
        if (!File.Exists(path))
        {
            _current = CabinetConfig.Empty;
            return $"no Cabinet file at {path}";
        }

        try
        {
            var problems = new List<string>();
            _current = Parse(File.ReadAllText(path), problems);
            var summary = string.Create(CultureInfo.InvariantCulture, $"Cabinet: {_current.Cosmetics.Count} look rules, {_current.Drops.Count} drops");
            return problems.Count == 0 ? summary : summary + "; skipped " + string.Join("; ", problems);
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            _current = CabinetConfig.Empty;
            return $"Cabinet file unreadable: {exception.Message}";
        }
    }

    // A game master's edit of a look's rule (added when the look had none).
    public string Replace(CosmeticRule rule)
    {
        if (CabinetConfig.Problem(rule) is { } problem)
        {
            return $"look '{rule.Key}' {problem}";
        }

        var current = _current;
        var rules = current.Cosmetics.Where(existing => !string.Equals(existing.Key, rule.Key, StringComparison.OrdinalIgnoreCase)).Append(rule)
            .OrderBy(static existing => existing.Key, StringComparer.OrdinalIgnoreCase).ToList();
        Save(new CabinetConfig(rules, current.Drops));
        return $"look '{rule.Key}' saved";
    }

    // A game master's drop: replaces the drop of the same item and source, and removes it at a chance of 0.
    public string SetDrop(CosmeticDrop drop)
    {
        if (CabinetConfig.Problem(drop) is { } problem)
        {
            return $"drop of '{drop.Item}' {problem}";
        }

        var current = _current;
        var drops = current.Drops.Where(existing => !(string.Equals(existing.Item, drop.Item, StringComparison.OrdinalIgnoreCase)
            && existing.Monster == drop.Monster && existing.Bosses == drop.Bosses)).ToList();
        if (drop.PerTenThousand > 0)
        {
            drops.Add(drop);
        }

        Save(new CabinetConfig(current.Cosmetics, drops));
        return drop.PerTenThousand > 0
            ? string.Create(CultureInfo.InvariantCulture, $"drop of '{drop.Item}' saved at {drop.PerTenThousand}/10000")
            : $"drop of '{drop.Item}' removed";
    }

    private void Save(CabinetConfig next)
    {
        File.WriteAllText(path, Write(next).Replace("\r\n", "\n", StringComparison.Ordinal));
        _current = next;
    }
}
