using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SpawnScheduleTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("spawn mask matches the original 70 Hz counter across host rates and hitches", CadenceAsync);
        yield return ("boss minute is daily, global, and protected against duplicate hours and clock changes", ScheduleAsync);
        yield return ("bosses require configured hours and active anchors", AnchorsAsync);
        yield return ("the world boss list holds every field's bosses soonest first, rebuilds when a pass comes, travels and opens beside the map", BossListAsync);
        yield return ("a boss always spawns at its field's first active spawn point, is remembered as its master, and its spots travel", BossSpotAsync);
        yield return ("the next boss pass is the first boss hour after now at the day's minute, travels and counts down", NextBossAsync);
        yield return ("world host shares boss passes across fields and does not replay them on entry", HostAsync);
    }

    private static Task CadenceAsync()
    {
        foreach (int mask in new[] { 0, 1, 5, 15, 127, 255 })
        {
            foreach (int dt in new[] { 10, 20, 50, 100 })
            {
                var cadence = new SpawnCadence(mask);
                int previousFrame = 0;
                for (int elapsed = dt; elapsed <= 10000; elapsed += dt)
                {
                    int frame = elapsed * 70 / 1000;
                    bool expected = false;
                    for (int counter = previousFrame + 1; counter <= frame; counter++)
                    {
                        expected |= (counter & mask) == 0;
                    }

                    Assert.True(cadence.Advance(dt) == expected, $"mask {mask}, step {dt}, elapsed {elapsed}");
                    previousFrame = frame;
                }
            }
        }

        var hitch = new SpawnCadence(127);
        Assert.True(hitch.Advance(10000), "hitch skipped every due opportunity");
        Assert.True(!hitch.Advance(50), "hitch retained a spawn backlog");
        Assert.True(!hitch.Advance(double.NaN), "invalid elapsed time created a pass");
        Assert.True(hitch.Advance(950), "cadence failed to resume at counter 768");
        return Task.CompletedTask;
    }

    private static Task ScheduleAsync()
    {
        var rng = new MinuteRng();
        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(-3));
        var schedule = new BossSpawnSchedule(start, rng);
        Assert.True(schedule.Minute == 15 && rng.Draws == 1, "startup minute was not selected once");
        Assert.True(!schedule.TryBeginPass(start, TimeSpan.Zero), "startup spawned outside its minute");
        Assert.True(schedule.TryBeginPass(start.AddMinutes(15), TimeSpan.FromMinutes(15)), "scheduled pass missing");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(15.5), TimeSpan.FromMinutes(15.5)), "same minute duplicated");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(75), TimeSpan.FromMinutes(20)), "wall-clock jump bypassed monotonic guard");
        Assert.True(schedule.TryBeginPass(start.AddMinutes(75), TimeSpan.FromMinutes(26)), "next eligible hour blocked");
        Assert.True(rng.Draws == 1, "hour change rerolled daily minute");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(15), TimeSpan.FromMinutes(45)), "clock rollback respawned old hour");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(136), TimeSpan.FromMinutes(87)), "missed minute was replayed");
        Assert.True(schedule.TryBeginPass(start.AddDays(1).AddMinutes(30), TimeSpan.FromDays(1)), "new daily minute missing");
        Assert.True(rng.Draws == 2 && schedule.Minute == 30, "day change did not choose a new minute");
        Assert.True(!schedule.TryBeginPass(start, TimeSpan.FromDays(2)) && rng.Draws == 2, "date rollback rerolled schedule");
        return Task.CompletedTask;
    }

    private static Task NextBossAsync()
    {
        var bosses = new[] { new BossSpawn(10, 0, 0, [3, 15]), new BossSpawn(11, 0, 0, [15, 20]) };
        var morning = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

        // Later today: the 15:20 pass, and the first boss of the list when two share the hour.
        Assert.True(BossSpawnSchedule.TryNext(bosses, morning, 20, out var boss, out var slot, out var exact)
            && boss.BossMonsterCode == 10 && slot == new DateTime(2026, 11, 2, 15, 20, 0) && exact, "the next pass today was missed");

        // In the pass's own minute the pass is gone: the next is 20:20.
        Assert.True(BossSpawnSchedule.TryNext(bosses, new DateTimeOffset(2026, 11, 2, 15, 20, 30, TimeSpan.Zero), 20, out boss, out slot, out _)
            && boss.BossMonsterCode == 11 && slot.Hour == 20, "a pass in its own minute counted as next");

        // After the last pass of the day: tomorrow's 03:xx, whose minute is not known yet.
        Assert.True(BossSpawnSchedule.TryNext(bosses, new DateTimeOffset(2026, 11, 2, 21, 0, 0, TimeSpan.Zero), 20, out _, out slot, out exact)
            && slot.Day == 3 && slot.Hour == 3 && !exact, "tomorrow's pass was not marked as not exact");
        Assert.True(!BossSpawnSchedule.TryNext([], morning, 20, out _, out _, out _), "a field without bosses had a pass");

        // On the wire and on the minimap.
        var buffer = new byte[256];
        var length = new PristonTale.Protocol.Packets.BossTimePacket("Valento", 3725, true).Write(buffer);
        var read = PristonTale.Protocol.Packets.BossTimePacket.Read(buffer.AsSpan(0, length));
        Assert.True(read == new PristonTale.Protocol.Packets.BossTimePacket("Valento", 3725, true), "the boss time did not survive the wire");
        var line = new PristonTale.Gameplay.UI.Widgets.HudBossTime();
        line.Receive(new PristonTale.Client.Net.Presence.BossTimeInfo("Valento", 3725, true), 1000);
        Assert.True(line.Text(1000) == "Next boss: Valento 01:02:05" && line.Text(3500) == "Next boss: Valento 01:02:03", "the countdown did not run");
        Assert.True(line.Text(1000 + 3725_000) == "Next boss: Valento now", "a due pass did not read now");
        line.Receive(new PristonTale.Client.Net.Presence.BossTimeInfo("", 0, false), 2000);
        Assert.True(line.Text(2000) == "", "a field without bosses showed a line");
        return Task.CompletedTask;
    }

    private static Task BossSpotAsync()
    {
        var definition = new MonsterDefinition(7, "Spot fixture", MonsterFaction.Enemy, MonsterClass.Boss, MonsterBrood.Normal,
            new MonsterCombatStats(1, 1, new MonsterRange(1, 1), 1, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "");
        var catalog = new MonsterCatalog([definition]);
        var data = new FieldSpawnData([new SpawnPoint(10, 20, false), new SpawnPoint(300, 400, true), new SpawnPoint(500, 600, true)], [],
            [new BossSpawn(7, 0, 0, [9]), new BossSpawn(99, 0, 0, [9])], []);
        var spawner = new FieldSpawner(data, catalog, new CombatRng(1));

        // The spot is the first active point, known before any pass; no player is near it.
        Assert.True(spawner.TryBossSpot(out var spot) && spot == new Vector3(300, 0, 400), "the boss spot was not the first active point");
        Assert.True(spawner.BossMaster(0) == 0 && spawner.BossName(0) == "Spot fixture" && spawner.BossName(1) == "Boss", "boss names or masters wrong");
        var sink = new SpawnSink();
        spawner.SpawnScheduledBosses(9, sink);
        Assert.True(sink.Count == 1 && sink.Anchor == spot && spawner.BossMaster(0) != 0 && spawner.BossMaster(1) == 0,
            "the boss did not spawn at its spot or was not remembered");
        Assert.True(!new FieldSpawner(data with { SpawnPoints = [new SpawnPoint(1, 1, false)] }, catalog, new CombatRng(1)).TryBossSpot(out _),
            "a field without an active point had a boss spot");

        var buffer = new byte[512];
        var spots = new[] { new PristonTale.Protocol.Packets.BossSpotEntry("Spot fixture", 300 * 256, -400 * 256, true),
            new PristonTale.Protocol.Packets.BossSpotEntry("Boss", 300 * 256, -400 * 256, false) };
        var length = new PristonTale.Protocol.Packets.BossSpotsPacket(spots).Write(buffer);
        var read = PristonTale.Protocol.Packets.BossSpotsPacket.Read(buffer.AsSpan(0, length));
        Assert.True(read.Spots.SequenceEqual(spots), "the boss spots did not survive the wire");
        var empty = PristonTale.Protocol.Packets.BossSpotsPacket.Read(buffer.AsSpan(0, new PristonTale.Protocol.Packets.BossSpotsPacket([]).Write(buffer)));
        Assert.True(empty.Spots.Count == 0, "an empty boss list did not read back empty");
        return Task.CompletedTask;
    }

    private static Task BossListAsync()
    {
        MonsterDefinition Boss(int code, string name) => new(code, name, MonsterFaction.Enemy, MonsterClass.Boss, MonsterBrood.Normal,
            new MonsterCombatStats(1, 1, new MonsterRange(1, 1), 1, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "");
        var monsters = new MonsterCatalog([Boss(1, "Late"), Boss(2, "Early")]);
        var point = new SpawnPoint(1, 1, true);
        var spawns = new Dictionary<int, FieldSpawnData>
        {
            [3] = new FieldSpawnData([point], [], [new BossSpawn(1, 0, 0, [20])], []),
            [4] = new FieldSpawnData([point], [], [new BossSpawn(2, 0, 0, [12])], []),
            [5] = new FieldSpawnData([point], [], [], [])
        };
        var first = new FieldDef("first", "Forest", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 };
        var fields = new WorldCatalog([first, first with { Id = "second", Name = "Desert", FieldCode = 4 }]);
        var morning = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        var list = BossList.Build(spawns, monsters, fields, morning, 10);
        Assert.True(list.Entries.Length == 2 && list.Entries[0] is { BossName: "Early", FieldName: "Desert" } && list.Entries[1].BossName == "Late",
            "the boss list was not every boss soonest first with its field");
        Assert.True(!list.Due(morning, 10) && list.Due(morning, 11) && list.Due(new DateTimeOffset(2026, 11, 2, 12, 10, 0, TimeSpan.Zero), 10),
            "the list was not due after a pass or a new minute only");

        var buffer = new byte[PristonTale.Protocol.Binary.FrameCodec.MaxFrameSize];
        var wide = new string('W', 80);
        var bosses = Enumerable.Range(0, PristonTale.Protocol.Packets.BossListPacket.MaxBosses)
            .Select(index => new PristonTale.Protocol.Packets.BossListWireEntry(wide, wide, (uint)index, index % 2 == 0)).ToArray();
        var length = new PristonTale.Protocol.Packets.BossListPacket(bosses).Write(buffer);
        var read = PristonTale.Protocol.Packets.BossListPacket.Read(buffer.AsSpan(0, length));
        Assert.True(read.Bosses.Count == bosses.Length && read.Bosses[3] is { SecondsLeft: 3, Exact: false } && read.Bosses[3].BossName.Length == 32,
            "the longest boss list did not fit one frame with its names cut");

        var window = new PristonTale.Gameplay.UI.Widgets.HudBossList();
        var layout = PristonTale.Gameplay.UI.HudLayout.Create(800, 600);
        var map = new Microsoft.Xna.Framework.Rectangle(656, 410, 128, 144);
        window.Receive([new PristonTale.Client.Net.Presence.BossListInfo("Early", "Desert", 90, true)], 0);
        var button = PristonTale.Gameplay.UI.Widgets.HudBossList.Button(map);
        Assert.True(button.Right <= map.X && window.Click(layout, map, button.Center) && window.Open, "the skull button did not open the list");
        var shown = window.Window(layout, map);
        Assert.True(!shown.Intersects(map) && window.Click(layout, map, shown.Center) && window.Open, "the list covered the map or closed on a row click");
        Assert.True(window.Click(layout, map, PristonTale.Gameplay.UI.Widgets.HudBossList.CloseMark(shown).Center) && !window.Open, "the close mark did not close it");
        return Task.CompletedTask;
    }

    private static Task AnchorsAsync()
    {
        var definition = new MonsterDefinition(1, "Boss fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(1, 1, new MonsterRange(1, 1), 1, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "");
        var catalog = new MonsterCatalog([definition]);
        var data = new FieldSpawnData([new SpawnPoint(100, 200, true)], [], [new BossSpawn(1, 1, 2, [9])], []);
        var sink = new SpawnSink();
        var spawner = new FieldSpawner(data, catalog, new CombatRng(1));
        spawner.SpawnScheduledBosses(8, sink);
        Assert.True(sink.Count == 0, "boss ignored hour list");
        spawner.SpawnScheduledBosses(9, sink);
        Assert.True(sink.Count == 3 && sink.Anchor == new Vector3(100, 0, 200), "boss/escorts lost their active anchor");
        foreach (var invalid in new[]
        {
            data with { SpawnPoints = [] },
            data with { SpawnPoints = [new SpawnPoint(100, 200, false)] },
            data with { BossSpawns = [new BossSpawn(1, 1, 2, [])] }
        })
        {
            new FieldSpawner(invalid, catalog, new CombatRng(1)).SpawnScheduledBosses(9, sink);
        }

        Assert.True(sink.Count == 3, "missing anchor or empty hour list produced a boss");
        return Task.CompletedTask;
    }

    private static async Task HostAsync()
    {
        var clock = new ManualClock();
        var monster = new MonsterDefinition(1, "Boss fixture", MonsterFaction.Enemy, MonsterClass.Boss, MonsterBrood.Normal,
            new MonsterCombatStats(1, 1, new MonsterRange(1, 1), 1, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "");
        var spawn = new FieldSpawnData([new SpawnPoint(100, 100, true)], [], [new BossSpawn(1, 0, 0, [9, 10])], []);
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, new MonsterCatalog([monster]), empty.Npcs,
                new Dictionary<int, FieldSpawnData> { [3] = spawn, [4] = spawn, [5] = spawn }, empty.Motions)
        };
        var first = new FieldDef("first", "First", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 };
        var catalog = new WorldCatalog([first, first with { Id = "second", FieldCode = 4 }, first with { Id = "third", FieldCode = 5 }]);
        var world = new WorldServer(holder, catalog, clock, new MinuteRng());
        var outboxes = new[] { new WorldOutbox(), new WorldOutbox(), new WorldOutbox() };
        for (int index = 0; index < 2; index++)
        {
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Boss clock fixture");
            var join = world.JoinAsync(index + 3, new WorldPlayer((uint)index + 1, Vector3.Zero, character, false, default), outboxes[index].Writer);
            world.Tick(.05f, 1);
            Assert.True(await join, "host fixture join failed");
        }

        AssertMonsterCount(outboxes[0], 0);
        AssertMonsterCount(outboxes[1], 0);
        clock.Advance(TimeSpan.FromMinutes(15));
        world.Tick(.05f, 2);
        AssertMonsterCount(outboxes[0], 1);
        AssertMonsterCount(outboxes[1], 1);

        var lateCharacter = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Late field fixture");
        var lateJoin = world.JoinAsync(5, new WorldPlayer(3, Vector3.Zero, lateCharacter, false, default), outboxes[2].Writer);
        world.Tick(.05f, 3);
        Assert.True(await lateJoin, "late field join failed");
        AssertMonsterCount(outboxes[0], 1);
        AssertMonsterCount(outboxes[2], 0);

        clock.Advance(TimeSpan.FromHours(1));
        world.Tick(.05f, 4);
        AssertMonsterCount(outboxes[0], 2);
        AssertMonsterCount(outboxes[1], 2);
        AssertMonsterCount(outboxes[2], 1);
    }

    private static void AssertMonsterCount(WorldOutbox outbox, int expected)
    {
        Assert.True(outbox.Reader.TryRead(out var snapshot), "host emitted no snapshot");
        Assert.True(snapshot.Entities.Count(entity => entity.Kind == EntityKind.Monster) == expected,
            $"expected {expected} scheduled monsters");
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            _timestamp += elapsed.Ticks;
        }
    }

    private sealed class MinuteRng : ISimRng
    {
        public int Draws { get; private set; }

        public int NextInclusive(int minInclusive, int maxInclusive)
        {
            Assert.True(minInclusive == 1 && maxInclusive == 45, "boss minute range differs from source");
            Draws++;
            return Draws == 1 ? 15 : 30;
        }

        public int NextExclusive(int minInclusive, int maxExclusive) => throw new InvalidOperationException();
        public float NextUnit() => throw new InvalidOperationException();
    }

    private sealed class SpawnSink : ISpawnSink, IPlayerProximity
    {
        public int Count { get; private set; }
        public Vector3 Anchor { get; private set; }
        public bool AnyPlayerNear(Vector3 position) => false;

        public MonsterInstance Spawn(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing)
        {
            if (Count == 0)
            {
                Anchor = position;
            }

            Count++;
            return new MonsterInstance((uint)Count, definition, position, facing, spawnPointIndex);
        }
    }
}
