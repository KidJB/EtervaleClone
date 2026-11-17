using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

// Writes data/couriers.json from the imported world data: a Quest Courier beside the arrival point of every hunting field
// and six hunts of the monsters that spawn there (one per category, difficulty rising with the monsters' strength). The
// experience is a share of what the hunt's own kills give (Share), so a hunt adds to grinding rather than replacing it.
internal static class CourierCatalogProbe
{
    private const int CourierOffset = 40;
    // Fields without hunting: the towns, the castle, Bellatra.s arenas, the boss field, the quest arena and room, the action field.
    private static readonly HashSet<int> Skipped = [FieldCodes.Bellatra, Sod2Field.Definition.FieldCode, FieldCodes.Boss, FieldCodes.QuestArena, FieldCodes.OfficeRoom, FieldCodes.FallGame];

    // Share of the kills' own experience a hunt adds, by difficulty and by category.
    private static readonly Dictionary<CourierDifficulty, double> DifficultyShare = new()
    {
        [CourierDifficulty.Easy] = 0.30, [CourierDifficulty.Normal] = 0.40, [CourierDifficulty.Hard] = 0.50, [CourierDifficulty.Elite] = 0.60
    };

    private static readonly Dictionary<CourierCategory, double> CategoryShare = new()
    {
        [CourierCategory.Daily] = 1.0, [CourierCategory.Midnight] = 1.2, [CourierCategory.Repeatable] = 0.5, [CourierCategory.Weekly] = 1.5,
        [CourierCategory.OneTime] = 2.0
    };

    public static async Task RunAsync(string database, string output)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"priston-couriers-{Guid.NewGuid():N}.db");
        File.Copy(database, copy);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPristonSqlite(copy);
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var data = await new WorldDataAppService(provider.GetRequiredService<IWorldDataRepository>()).BuildAsync();
            var catalog = Generate(new WorldCatalog(), data);
            File.WriteAllText(output, CourierCatalogStore.Write(catalog).Replace("\r\n", "\n", StringComparison.Ordinal));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{catalog.Couriers.Count} Couriers, {catalog.Quests.Count} quests -> {output}"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }
    }

    public static CourierCatalog Generate(WorldCatalog world, WorldData data)
    {
        var couriers = new List<CourierPost>();
        var quests = new List<CourierQuest>();
        foreach (var field in world.AllFields.OrderBy(static field => field.FieldCode))
        {
            if (field.State is FieldState.Village or FieldState.Castle or FieldState.QuestArena || Skipped.Contains(field.FieldCode)
                || !data.FieldSpawns.TryGetValue(field.FieldCode, out var spawns))
            {
                continue;
            }

            var monsters = spawns.SpawnTable
                .GroupBy(static entry => entry.MonsterCode)
                .Select(group => (Weight: group.Sum(static entry => entry.Weight), Found: data.Monsters.TryGet(group.Key, out var monster), Monster: monster))
                .Where(static pick => pick.Found && pick.Monster.IsHostile && !pick.Monster.IsBoss && pick.Monster.ExpReward > 0)
                .OrderBy(static pick => pick.Monster.Combat.Level).ThenByDescending(static pick => pick.Weight)
                .Select(static pick => pick.Monster)
                .ToList();
            if (monsters.Count == 0)
            {
                continue;
            }

            var code = CourierCatalog.FirstNpcCode + couriers.Count;
            var place = TeleportCoreDestinations.All.FirstOrDefault(destination => destination.FieldCode == field.FieldCode)?.Name ?? field.Name;
            couriers.Add(new CourierPost(code, CourierNpcs.Name, field.FieldCode, field.Arrival.X + CourierOffset, field.Arrival.Z + CourierOffset, 0));
            var weakest = monsters[0];
            var strongest = monsters[^1];
            var middle = monsters[monsters.Count / 2];
            var common = monsters.Take(2).ToList();
            var three = monsters.Skip(Math.Max(0, monsters.Count - 3)).ToList();
            var id = field.FieldCode * 100;
            quests.Add(Hunt(++id, $"{place}: Patrol", $"The roads of {place} are thick with {weakest.Name}. Thin them out.",
                CourierCategory.Daily, CourierDifficulty.Easy, field, code, [(weakest, 30)]));
            quests.Add(Hunt(++id, $"{place}: Clearing", $"Clear {place} of the creatures that crowd its paths.",
                CourierCategory.Daily, CourierDifficulty.Normal, field, code, [.. common.Select(monster => (monster, 25))]));
            quests.Add(Hunt(++id, $"{place}: Bounty", $"A standing bounty on {middle.Name}, paid every hour to whoever brings proof.",
                CourierCategory.Repeatable, CourierDifficulty.Normal, field, code, [(middle, 20)], cooldownMinutes: 60));
            quests.Add(Hunt(++id, $"{place}: Night Watch", $"At night {strongest.Name} grow bold. Hunt them while the moon is high.",
                CourierCategory.Midnight, CourierDifficulty.Hard, field, code, [(strongest, 40)]));
            quests.Add(Hunt(++id, $"{place}: Purge", $"A week-long purge of the strongest creatures of {place}.",
                CourierCategory.Weekly, CourierDifficulty.Hard, field, code, [.. three.Select(monster => (monster, 60))], goldPerLevel: 200));
            quests.Add(Hunt(++id, $"{place}: Master Hunter", $"Prove yourself a master hunter of {place}: bring down a hundred {strongest.Name}.",
                CourierCategory.OneTime, CourierDifficulty.Elite, field, code, [(strongest, 100)], goldPerLevel: 500));
        }

        return new CourierCatalog(couriers, quests, new CourierSchedule());
    }

    private static CourierQuest Hunt(int id, string name, string description, CourierCategory category, CourierDifficulty difficulty, FieldDef field,
        int courier, IReadOnlyList<(MonsterDefinition Monster, int Count)> objectives, int cooldownMinutes = 0, int goldPerLevel = 0)
    {
        var levels = objectives.Select(static objective => objective.Monster.Combat.Level).ToList();
        var minLevel = Math.Clamp(Math.Max(levels.Min() - 5, field.LimitLevel), 1, ExperienceTable.MaximumLevel);
        var maxLevel = Math.Clamp(levels.Max() + 10, minLevel, ExperienceTable.MaximumLevel);
        var kills = objectives.Sum(static objective => (double)objective.Monster.ExpReward * objective.Count);
        var experience = (long)Math.Round(kills * DifficultyShare[difficulty] * CategoryShare[category]);
        return new CourierQuest(id, name, description, category, difficulty, field.FieldCode, courier, courier, minLevel, maxLevel,
            [.. objectives.Select(static objective => new CourierObjective(objective.Monster.Code, objective.Monster.Name, objective.Count))], experience,
            Gold: (long)goldPerLevel * minLevel, CooldownMinutes: cooldownMinutes);
    }
}
