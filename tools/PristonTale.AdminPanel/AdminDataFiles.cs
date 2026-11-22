using System.Text.Json;

namespace PristonTale.AdminPanel;

// The JSON files beside the database the server reads (data/coinshop.json, cabinet.json, couriers.json, worldbuffs.json):
// listed, read and replaced whole. A new text must parse as JSON (comments and trailing commas allowed, as the server's
// readers allow them); the file it replaces is kept as <name>.bak. Only plain file names in the data folder are reachable.
public sealed class AdminDataFiles(string directory)
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // What picks a file's change up in the running server.
    private static readonly Dictionary<string, string> Reloads = new(StringComparer.OrdinalIgnoreCase)
    {
        ["coinshop.json"] = "/@coinshop_reload in game",
        ["cabinet.json"] = "/@cabinet_reload in game",
        ["couriers.json"] = "/@courier_reload in game",
        ["worldbuffs.json"] = "a server restart (the server rewrites it while running: stop it first)"
    };

    public IReadOnlyList<(string Name, long Bytes, string Reload)> List() =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json").Select(static path => new FileInfo(path))
                .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static file => (file.Name, file.Length, Reloads.GetValueOrDefault(file.Name, "a server restart"))).ToArray()
            : [];

    public string? Read(string name) => Resolve(name) is { } path && File.Exists(path) ? File.ReadAllText(path) : null;

    // Null when written; else why the text was refused (nothing is written then).
    public string? Write(string name, string text)
    {
        if (Resolve(name) is not { } path || !File.Exists(path))
        {
            return $"no data file {name}";
        }

        if (Validate(text) is { } error)
        {
            return error;
        }

        File.Copy(path, path + ".bak", overwrite: true);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
        return null;
    }

    public static string? Validate(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, Lenient);
            return null;
        }
        catch (JsonException exception)
        {
            return $"not valid JSON: {exception.Message}";
        }
    }

    private string? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(directory, name);
    }
}
