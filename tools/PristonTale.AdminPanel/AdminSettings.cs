using System.Text.Json;
using PristonTale.Assets;

namespace PristonTale.AdminPanel;

// Where the panel listens and what it edits: adminpanel.json beside the executable, then the command line (--url, --sqlite,
// --conn, --data). A relative path resolves from the repository root (DataPath). Postgres, when set, wins over Sqlite.
public sealed record AdminSettings(string Url, string Sqlite, string Postgres, string DataDirectory, int SessionHours)
{
    public const string FileName = "adminpanel.json";
    public static readonly AdminSettings Default = new("http://127.0.0.1:5080", "data/pristontale.db", "", "data", 8);

    public bool UsesPostgres => Postgres.Length > 0;

    public string SqlitePath => DataPath.Resolve(Sqlite);

    public string DataPathResolved => DataPath.Resolve(DataDirectory);

    // The listening address is a loopback one unless the owner names another on purpose.
    public bool Loopback => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && (uri.IsLoopback || uri.Host == "localhost");

    public static AdminSettings Load(string directory, IReadOnlyList<string> args)
    {
        var settings = Default;
        var path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            settings = JsonSerializer.Deserialize<AdminSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Default;
        }

        for (var index = 0; index + 1 < args.Count; index++)
        {
            var value = args[index + 1];
            settings = args[index] switch
            {
                "--url" => settings with { Url = value },
                "--sqlite" => settings with { Sqlite = value, Postgres = "" },
                "--conn" => settings with { Postgres = value },
                "--data" => settings with { DataDirectory = value },
                _ => settings
            };
        }

        return settings with { SessionHours = Math.Clamp(settings.SessionHours, 1, 72) };
    }
}
