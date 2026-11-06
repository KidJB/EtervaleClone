using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

// The boss arena: its schedule, entry window, kill-time rewards and the bosses made from Fury.
internal static class BossArenaTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("boss arena: starts every six hours alternating its bosses, entry opens fifteen minutes before", ScheduleAsync);
        yield return ("boss arena: a faster kill gives more experience, and the bosses are made from Fury", RewardsAsync);
    }

    private static Task ScheduleAsync()
    {
        var at = new DateTimeOffset(2026, 11, 5, 5, 50, 0, TimeSpan.Zero);
        var start = BossArena.NextStart(at);
        Assert.True(start.Hour == 6 && start.Day == 5 && BossArena.EventAt(6).Name == "Hagios" && BossArena.EventAt(12).Name == "King Fury",
            "the next start or its boss is wrong");
        Assert.True(BossArena.NextStart(at.AddHours(13)) is { Hour: 0, Day: 6 }, "the last start of the day did not roll to midnight");
        Assert.True(BossArena.EntryOpen(at, start) && !BossArena.EntryOpen(start.AddMinutes(-16), start) && !BossArena.EntryOpen(start, start),
            "the entry window is not the fifteen minutes before the start");
        return Task.CompletedTask;
    }

    private static Task RewardsAsync()
    {
        Assert.True(BossArena.Inside(BossArena.Arrival.X, BossArena.Arrival.Z) && !BossArena.Inside(BossArena.Center.X + BossArena.Radius + 1f, BossArena.Center.Z), "the arena ground");
        var arena = BossArena.Events[0];
        Assert.True(BossArena.Experience(arena, 100, 5) > BossArena.Experience(arena, 100, 15) && BossArena.Experience(arena, 100, 15) > BossArena.Experience(arena, 100, 30),
            "a faster kill did not pay more");
        Assert.True(BossArena.Experience(arena, ExperienceTable.MaximumLevel, 5) == 0, "the top level gained experience");
        var combat = new MonsterCombatStats(110, 1, new MonsterRange(10, 20), 1, 1, 1, 1, 1, 15000, default);
        var fury = new MonsterDefinition(1, "Fury", default, MonsterClass.Normal, default, combat, "fury.inx");
        var bosses = BossArenaBosses.From([fury]).ToArray();
        Assert.True(bosses is [{ Name: "King Fury", Class: MonsterClass.Boss }, { Name: "Hagios" }] && bosses[0].Combat.MaxLife == 600_000
            && bosses[0].Combat.AttackDamage.Max == 40 && bosses[0].ModelInxPath == "fury.inx", "the bosses are not made from Fury");
        return Task.CompletedTask;
    }
}
