using PristonTale.Contracts.Characters;
using System.Text.Json;

namespace PristonTale.Simulation.Characters;

// A costume sold only as a skin: its catalog key (also its icon) and the body file of each class that can wear it.
// A weapon or off-hand look sold only as a skin: it has a model but no item in the world data.
// Like: the item it stands for when its key names no item family (held and fitted as that item, on either grip). Two: held in both hands.
public sealed record SkinGear(string Key, string Name, int Slot, string Like = "", bool Two = false);

public sealed record Costume(string Key, string Name, IReadOnlyDictionary<PlayerClass, string> Bodies);

// The costumes of The Cabinet (Data/Costumes.json), with the armor table row of each class.
public static class Costumes
{
    private sealed record Entry(string Key, string Name, Dictionary<string, string> Bodies);

    private sealed record Document(Dictionary<string, int> Rows, Entry[] Costumes, SkinGear[] Gear, string[] Hidden, int[] Hair);

    private static readonly Dictionary<int, PlayerClass> ClassOfRow = new();
    // Items The Cabinet does not sell: their model is a duplicate of another skin, a placeholder, or does not load.
    public static HashSet<string> Hidden { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The numbered heads sold as hair (CharacterHead.CustomHair), every class having a model of each.
    public static HashSet<int> Hair { get; } = [];

    private static readonly Dictionary<string, SkinGear> GearByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Costume> ByKey = Load();

    public static IReadOnlyCollection<Costume> All => ByKey.Values;

    public static IReadOnlyCollection<SkinGear> Gear => GearByKey.Values;

    public static SkinGear FindGear(string key) => GearByKey.GetValueOrDefault(key);

    public static Costume Find(string key) => ByKey.GetValueOrDefault(key);

    // The body file of a costume on an armor table row, or null.
    public static string Body(string key, int row) =>
        ClassOfRow.TryGetValue(row, out var playerClass) && Find(key) is { } costume ? costume.Bodies.GetValueOrDefault(playerClass) : null;

    private static Dictionary<string, Costume> Load()
    {
        var assembly = typeof(Costumes).Assembly;
        using var stream = assembly.GetManifestResourceStream("PristonTale.Simulation.Characters.Data.Costumes.json")!;
        var document = JsonSerializer.Deserialize<Document>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var (name, row) in document.Rows)
        {
            ClassOfRow[row] = Enum.Parse<PlayerClass>(name);
        }

        Hidden.UnionWith(document.Hidden ?? []);
        Hair.UnionWith(document.Hair ?? []);
        foreach (var gear in document.Gear ?? [])
        {
            GearByKey[gear.Key] = gear;
        }

        var result = new Dictionary<string, Costume>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.Costumes)
        {
            var bodies = new Dictionary<PlayerClass, string>();
            foreach (var (name, file) in entry.Bodies)
            {
                bodies[Enum.Parse<PlayerClass>(name)] = file;
            }

            result[entry.Key] = new Costume(entry.Key, entry.Name, bodies);
        }

        return result;
    }
}
