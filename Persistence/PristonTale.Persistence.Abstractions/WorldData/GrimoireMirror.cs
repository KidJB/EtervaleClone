using PristonTale.Simulation.Items;

namespace PristonTale.Persistence.Abstractions.WorldData;

// Adds the grimoires (Grimoires) beside the phantoms they mirror: their definitions, the shops that sell a phantom and
// the drop lines that hold one (where the grimoire shares the line's weight).
public static class GrimoireMirror
{
    public static IReadOnlyList<ItemTemplateRecord> Items(IReadOnlyList<ItemTemplateRecord> records)
    {
        var known = records.Select(static record => record.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mirrors = new List<ItemTemplateRecord>();
        foreach (var record in records)
        {
            if (Grimoires.TryMirror(record.Code, out var key) && !known.Contains(key))
            {
                mirrors.Add(record with { Code = key, Name = record.Name is { } name ? Grimoires.Name(name) : null, Kind = (int)ItemKind.Grimoire });
            }
        }

        return mirrors.Count == 0 ? records : [.. records, .. mirrors];
    }

    public static IReadOnlyList<NpcRecord> Shops(IReadOnlyList<NpcRecord> records) => records.Select(static npc =>
    {
        var mirrors = new List<NpcShopItemRecord>();
        var next = npc.ShopItems.Count == 0 ? 0 : npc.ShopItems.Max(static item => item.Ordinal) + 1;
        foreach (var item in npc.ShopItems)
        {
            if (Grimoires.TryMirror(item.ItemCode, out var key))
            {
                mirrors.Add(item with { Ordinal = next++, ItemCode = key });
            }
        }

        return mirrors.Count == 0 ? npc : npc with { ShopItems = [.. npc.ShopItems, .. mirrors] };
    }).ToList();

    public static IReadOnlyList<MonsterRecord> Drops(IReadOnlyList<MonsterRecord> records) => records.Select(static monster =>
    {
        var lines = monster.DropLines.Select(DropLine).ToList();
        var changed = !lines.SequenceEqual(monster.DropLines);
        var next = monster.BonusDrops.Count == 0 ? 0 : monster.BonusDrops.Max(static drop => drop.Ordinal) + 1;
        var bonus = monster.BonusDrops.Where(static drop => Grimoires.TryMirror(drop.ItemCode ?? "", out _))
            .Select(drop => drop with { Ordinal = next++, ItemCode = Grimoires.TryMirror(drop.ItemCode!, out var key) ? key : null }).ToList();
        return !changed && bonus.Count == 0 ? monster : monster with { DropLines = lines, BonusDrops = [.. monster.BonusDrops, .. bonus] };
    }).ToList();

    // A drop line with the grimoire of each phantom it holds; the same line when it holds none.
    public static MonsterDropLineRecord DropLine(MonsterDropLineRecord line)
    {
        var mirrors = line.Items.Select(static code => Grimoires.TryMirror(code ?? "", out var key) ? key : null).OfType<string>().ToList();
        return mirrors.Count == 0 ? line : line with { Items = [.. line.Items, .. mirrors] };
    }
}
