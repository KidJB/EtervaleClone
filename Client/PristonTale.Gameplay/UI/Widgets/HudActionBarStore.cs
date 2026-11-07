using System.Text.Json;
using System.Threading.Channels;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Gameplay.UI.Widgets;

// The action bar as saved: shown, locked, turned, where it lies (HudActionBar.Anchor, -1 until moved), the shown page and
// every page's shortcuts (HudActionBarSlots.Save); how far the minimap was moved from its place (HudMinimap.Offset); and
// the quest tracker's move and fold (HudQuestTracker.Offset, Collapsed).
public sealed record HudActionBarDocument(int Version, bool Open, bool Locked, bool Vertical, int X, int Y, int Page, string[] Slots, int MapX = 0, int MapY = 0)
{
    public const int CurrentVersion = 1;
    public int TrackerX { get; init; }
    public int TrackerY { get; init; }
    public bool TrackerCollapsed { get; init; }
    public int BuffsX { get; init; }
    public int BuffsY { get; init; }
    public bool BuffsCollapsed { get; init; }
}

// One file per character in action-bars/ beside the executable, read and written off the game thread: Load starts the
// read and Loaded hands the document over once it is there; Save queues the latest document and drops older ones.
public sealed class HudActionBarStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _directory;
    private readonly IClientLog _log;
    private readonly Channel<(string Path, HudActionBarDocument Document)> _pending =
        Channel.CreateBounded<(string, HudActionBarDocument)>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private Task<HudActionBarDocument> _reading;
    private string _path;

    public HudActionBarStore(string directory, IClientLog log)
    {
        _directory = directory;
        _log = log;
        _ = Task.Run(WriteLoopAsync);
    }

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "action-bars");

    // Starts reading the character's file; an empty name stops saving.
    public void Load(string characterName)
    {
        _path = string.IsNullOrWhiteSpace(characterName) ? null : Path.Combine(_directory, FileName(characterName));
        var path = _path;
        _reading = path is null ? null : Task.Run(() => Read(path));
    }

    // The read document once it is there (null for a character without one), once per Load.
    public bool Loaded(out HudActionBarDocument document)
    {
        document = null;
        if (_reading is not { IsCompleted: true } reading)
        {
            return false;
        }

        _reading = null;
        document = reading.IsCompletedSuccessfully ? reading.Result : null;
        return true;
    }

    public bool Reading => _reading is not null;

    public void Save(HudActionBarDocument document)
    {
        if (_path is not null && _reading is null)
        {
            _pending.Writer.TryWrite((_path, document));
        }
    }

    private HudActionBarDocument Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<HudActionBarDocument>(File.ReadAllBytes(path), JsonOptions);
            return document?.Version == HudActionBarDocument.CurrentVersion ? document : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warn($"Cannot read the action bar {path}: {exception.Message}");
            return null;
        }
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var (path, document) in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var temporary = path + ".tmp";
                await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions)).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"Cannot save the action bar {path}: {exception.Message}");
            }
        }
    }

    private static string FileName(string characterName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(characterName.Select(letter => Array.IndexOf(invalid, letter) >= 0 ? '_' : letter).ToArray());
        return name + ".json";
    }
}
