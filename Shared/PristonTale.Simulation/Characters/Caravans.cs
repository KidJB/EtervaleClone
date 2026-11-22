using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// A caravan: a creature that walks beside its owner and carries a storage of its own (the warehouse grid, kept per
// character). Its crystal (Etervale's CV1xx items) calls it for Hours; the storage keeps its items while no caravan is out
// and opens again with the next one.
public sealed record CaravanKind(int Id, string Name, string Model, float Scale, string ItemKey, int Hours, string Icon)
{
    public uint ItemCode => ItemCodes.Parse(ItemKey);

    // Its picture in the premium timers and the Active Buffs box (game/images/items/premium).
    public string IconPath => "game/images/items/premium/" + Icon + ".png";
}

// The caravan a character has out: its kind, when it leaves (Unix seconds), the name the player gave it and whether it stays
// where it stands instead of following its owner.
public sealed record CaravanState(int Kind, long ExpiresAt, string Name = "", bool Stays = false)
{
    public bool ActiveAt(long nowSeconds) => ExpiresAt > nowSeconds;
}

public enum CaravanCommand
{
    Name,
    Follow,
    Stay,
    Dismiss
}

public static class Caravans
{
    private const int Week = 7 * 24;

    // Every crystal's sprite is the summon crystal's.
    public const string IconKey = "GP137";
    public const string Command = "/caravan";

    public static readonly IReadOnlyList<CaravanKind> All =
    [
        new(1, "Caravan Hopy", @"char\monster\Hopy\hopy.inx", 0.8f, "CV101", Week, "CaravanHopy"),
        new(2, "Caravan Cuepy", @"char\npc\Caravans\cuepy\CuepyCaravan.inx", 1f, "CV102", Week, "CaravanCuepy"),
        new(3, "Caravan Rabie", @"char\npc\Caravans\rabie\RabieCaravan.inx", 1f, "CV103", Week, "CaravanRabie"),
        new(4, "Caravan Tobie", @"char\npc\Caravans\tobie\TobieCaravan.inx", 1f, "CV104", Week, "CaravanTobie"),
        new(5, "Caravan Arma", @"char\npc\Caravans\arma\ArmaCaravan.inx", 1f, "CV105", Week, "CaravanArma"),
        new(6, "Caravan Buma", @"char\npc\Caravans\buma\BumaCaravan.inx", 1f, "CV106", Week, "CaravanBuma"),
        new(7, "Caravan Rudolph", @"char\npc\Caravans\rudolph\RudolphCaravan.inx", 1f, "CV107", Week, "CaravanRudolph"),
        new(8, "Caravan Hungky", @"char\npc\Caravans\hungky\HungkyCaravan.inx", 1f, "CV108", Week, "CaravanHungky"),
        new(9, "Caravan Pingu", @"char\npc\Caravans\Pingu\pingu.inx", 1f, "CV109", Week, "CaravanPingu"),
        new(10, "Caravan Yeti", @"char\npc\Caravans\Yeti\Yeti_Caravan.inx", 1f, "CV110", Week, "CaravanYeti"),
        new(11, "Caravan Golden Hopy", @"char\npc\Caravans\G_Hopt\G_Hopt.inx", 1f, "CV111", Week, "G_CaravanHopy"),
        new(12, "Caravan Sir Knight", @"char\npc\Caravans\Dinuks\DinuksCaravan.inx", 1f, "CV112", Week, "CaravanSirKnight")
    ];

    public static CaravanKind Get(int id) => id >= 1 && id <= All.Count ? All[id - 1] : null;

    public static bool IsItem(string catalogKey)
    {
        foreach (var kind in All)
        {
            if (string.Equals(kind.ItemKey, catalogKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<ItemDefinition> ItemDefinitions() =>
        All.Select(kind => new ItemDefinition(kind.ItemCode, kind.Name + " Crystal", ItemKind.Misc) { CatalogKey = kind.ItemKey });

    // The caravan a crystal calls: a new one, or the one out with its time extended when it is of the same kind; null when
    // another kind is out.
    public static CaravanState Call(CaravanState current, int kind, long nowSeconds)
    {
        if (Get(kind) is not { } called)
        {
            return null;
        }

        if (current is null || !current.ActiveAt(nowSeconds))
        {
            return new CaravanState(kind, nowSeconds + called.Hours * 3600L, current?.Name ?? "");
        }

        return current.Kind == kind ? current with { ExpiresAt = current.ExpiresAt + called.Hours * 3600L } : null;
    }

    // "/caravan name <name>" names the caravan out ("/caravan name" alone clears it, PetNames' rules); "/caravan follow" and
    // "/caravan stay" make it follow its owner or stand where it is; "/caravan dismiss" sends it away before its time.
    public static bool TryParse(string text, out CaravanCommand command, out string argument)
    {
        command = CaravanCommand.Name;
        argument = "";
        var trimmed = text.Trim();
        if (!trimmed.StartsWith(Command + " ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = trimmed[(Command.Length + 1)..].Trim();
        var space = rest.IndexOf(' ');
        var verb = space < 0 ? rest : rest[..space];
        argument = space < 0 ? "" : rest[(space + 1)..].Trim();
        command = verb.ToLowerInvariant() switch
        {
            "name" => CaravanCommand.Name,
            "follow" => CaravanCommand.Follow,
            "stay" => CaravanCommand.Stay,
            "dismiss" => CaravanCommand.Dismiss,
            _ => (CaravanCommand)(-1)
        };
        return Enum.IsDefined(command) && (command == CaravanCommand.Name || argument.Length == 0);
    }

    public static string NameCommand(string name) => (Command + " name " + name).TrimEnd();

    public static string FollowCommand(bool follows) => Command + (follows ? " follow" : " stay");

    public const string DismissCommand = Command + " dismiss";

    public static string Shown(CaravanState state) => state is null ? "" : string.IsNullOrEmpty(state.Name) ? Get(state.Kind)?.Name ?? "" : state.Name;
}
