using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Parties;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Tests;

// Quest Couriers: accepting by level, the categories' resets, kill credit by field and objective, party credit, turn-in
// once, the saved form, the wire, the data file and the generated rewards.
internal static class CourierQuestTests
{
    private const int Field = 2;
    private const int Courier = CourierCatalog.FirstNpcCode;
    private const int Bargon = 11;
    private const int HeadCutter = 12;
    private const int Stranger = 13;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("courier: accepted by level and only once, rechecked at accept, at most eight at a time", AcceptAsync);
        yield return ("courier: kills count per objective and quest in the quest's field only, never past a count, and not for other monsters", KillsAsync);
        yield return ("courier: a turn-in needs every objective, is given once, and each category reopens on its own schedule", ResetsAsync);
        yield return ("courier: a kill in the field credits the killer and party members in share range for party hunts only", FieldCreditAsync);
        yield return ("courier: the hunts survive the save and the wire", PersistenceAsync);
        yield return ("courier: the catalog file skips bad quests, and the shipped one names a Courier and field for each hunt, and the Courier service runs", CatalogAsync);
        yield return ("courier: generated hunts reward a share of their kills' experience by difficulty and category", RewardsAsync);
        yield return ("courier: the Quest Book lists the hunts of the three best fields at the level and every accepted one, a section per field", BookAsync);
    }

    private static CourierQuest Quest(int id, CourierCategory category, int minLevel = 10, int maxLevel = 30, bool party = true, int cooldown = 0,
        params (int Monster, int Count)[] objectives) =>
        new(id, $"Hunt {id}", "", category, CourierDifficulty.Normal, Field, Courier, Courier, minLevel, maxLevel,
            [.. (objectives.Length == 0 ? [(Bargon, 3)] : objectives).Select(objective => new CourierObjective(objective.Monster, $"M{objective.Monster}", objective.Count))],
            1000, CooldownMinutes: cooldown, PartyCredit: party);

    private static CourierCatalog Catalog(params CourierQuest[] quests) => new([new CourierPost(Courier, CourierNpcs.Name, Field, 0, 0, 0)], quests, new CourierSchedule());

    private static Task BookAsync()
    {
        CourierQuest At(int id, int field, int minLevel, int maxLevel) => Quest(id, CourierCategory.Daily, minLevel, maxLevel) with { FieldCode = field };
        var catalog = new CourierCatalog([], [At(1, 1, 10, 30), At(2, 2, 20, 40), At(3, 3, 25, 45), At(4, 4, 28, 50), At(5, 4, 15, 35), At(6, 5, 60, 80),
            At(7, 6, 5, 20), At(8, 1, 12, 30) with { Enabled = false }], new CourierSchedule());
        var offered = catalog.BookQuests(30, static _ => false).Select(static quest => quest.Id).Order().ToArray();
        Assert.True(offered.SequenceEqual([2, 3, 4, 5]), $"the book's fields are not the three best at the level: {string.Join(",", offered)}");
        var accepted = catalog.BookQuests(30, static id => id == 7).Select(static quest => quest.Id).ToHashSet();
        Assert.True(accepted.Contains(7) && !accepted.Contains(1) && !accepted.Contains(6) && !accepted.Contains(8), "an accepted hunt is missing from the book");

        static Protocol.Packets.CourierWireQuest Wire(int id, int field, int minLevel) =>
            new(id, $"Hunt {id}", "", 0, 0, field, minLevel, minLevel + 10, 0, 0, "", 0, 0, 0, []);
        var hunts = new[] { Wire(1, 7, 20), Wire(2, 9, 40), Wire(3, 7, 25) };
        var fields = Gameplay.UI.Quests.HudQuestBook.HuntFields(hunts);
        Assert.True(fields.SequenceEqual([9, 7]), "the fields are not ordered by their hunts' levels");
        Assert.True(Gameplay.UI.Quests.HudQuestBook.HuntStatus(hunts[0]) == QuestBookStatus.Available
            && Gameplay.UI.Quests.HudQuestBook.HuntStatus(hunts[0] with { Standing = (byte)CourierStanding.Ready }) == QuestBookStatus.Running
            && Gameplay.UI.Quests.HudQuestBook.HuntStatus(hunts[0] with { Standing = (byte)CourierStanding.Cooldown }) == QuestBookStatus.Locked,
            "a hunt's standing does not map to its book status");
        Assert.True(Gameplay.UI.Quests.HudQuestBook.Shows(QuestBookStatus.Running, false) && !Gameplay.UI.Quests.HudQuestBook.Shows(QuestBookStatus.Done, false)
            && !Gameplay.UI.Quests.HudQuestBook.Shows(QuestBookStatus.Locked, false) && Gameplay.UI.Quests.HudQuestBook.Shows(QuestBookStatus.Locked, true),
            "Show all does not decide which quests show");
        var book = QuestBook.For(tempskron: true);
        Assert.True(book.Where(static entry => entry.RankUpTier >= 0).All(static entry => Gameplay.UI.Quests.HudQuestBook.StorySection(entry) == "Rank up")
            && book.Select(Gameplay.UI.Quests.HudQuestBook.StorySection).Distinct().Count() > 1, "the story quests are not split into sections");
        Assert.True(CourierCatalog.BookNpc == ulong.MaxValue && CourierCatalog.BookFields == 3, "the book's board changed");
        return Task.CompletedTask;
    }

    private static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 16, 12, 0, 0)));

    private static Task AcceptAsync()
    {
        var schedule = new CourierSchedule();
        var state = new CourierQuestState();
        var quest = Quest(1, CourierCategory.Daily);
        Assert.True(state.Accept(quest, 9, Noon, schedule) == CourierStatus.NotOffered && state.Accept(quest, 31, Noon, schedule) == CourierStatus.NotOffered,
            "a quest outside its level range was accepted");
        Assert.True(state.Accept(quest with { Enabled = false }, 20, Noon, schedule) == CourierStatus.NotOffered, "a disabled quest was accepted");
        Assert.True(state.Accept(quest, 20, Noon, schedule) == CourierStatus.Ok && state.Accept(quest, 20, Noon, schedule) == CourierStatus.AlreadyAccepted,
            "a quest was refused, or accepted twice");
        for (var id = 2; id <= CourierCatalog.MaxActive; id++)
        {
            Assert.True(state.Accept(Quest(id, CourierCategory.Daily), 20, Noon, schedule) == CourierStatus.Ok, $"quest {id} was refused");
        }

        Assert.True(state.Accept(Quest(99, CourierCategory.Daily), 20, Noon, schedule) == CourierStatus.TooMany, "a ninth hunt was accepted");
        Assert.True(state.Abandon(1) == CourierStatus.Ok && state.Abandon(1) == CourierStatus.NotAccepted && state.Accept(Quest(99, CourierCategory.Daily), 20, Noon, schedule) == CourierStatus.Ok,
            "abandoning did not free a place");
        return Task.CompletedTask;
    }

    private static Task KillsAsync()
    {
        var both = Quest(1, CourierCategory.Daily, objectives: [(Bargon, 2), (HeadCutter, 1)]);
        var same = Quest(2, CourierCategory.Daily, objectives: [(Bargon, 5)]);
        var elsewhere = Quest(3, CourierCategory.Daily) with { FieldCode = Field + 1 };
        var catalog = Catalog(both, same, elsewhere);
        var state = new CourierQuestState();
        foreach (var quest in catalog.Quests)
        {
            state.Accept(quest, 20, Noon, catalog.Schedule);
        }

        Assert.True(!state.OnKill(Stranger, Field, catalog) && state.Active.Values.All(progress => progress.Kills.All(kills => kills == 0)),
            "a monster no hunt asks for counted");
        for (var kill = 0; kill < 4; kill++)
        {
            state.OnKill(Bargon, Field, catalog);
        }

        Assert.True(state.Active[1].Kills[0] == 2 && state.Active[2].Kills[0] == 4 && state.Active[3].Kills[0] == 0,
            "a kill did not count once for each hunt of its field, stopped at a count, or counted in another field");
        Assert.True(state.CanTurnIn(both) == CourierStatus.NotFinished, "a hunt with an objective left was ready");
        state.OnKill(HeadCutter, Field, catalog);
        Assert.True(state.CanTurnIn(both) == CourierStatus.Ok && state.Standing(both, Noon, catalog.Schedule) == CourierStanding.Ready, "the finished hunt was not ready");
        return Task.CompletedTask;
    }

    private static Task ResetsAsync()
    {
        var schedule = new CourierSchedule(DailyResetHour: 0, WeeklyResetDay: DayOfWeek.Monday, MidnightStartHour: 0, MidnightEndHour: 2);
        var state = new CourierQuestState();
        var daily = Quest(1, CourierCategory.Daily);
        var weekly = Quest(2, CourierCategory.Weekly);
        var once = Quest(3, CourierCategory.OneTime);
        var repeat = Quest(4, CourierCategory.Repeatable, cooldown: 60);
        var midnight = Quest(5, CourierCategory.Midnight);
        var catalog = Catalog(daily, weekly, once, repeat, midnight);
        Assert.True(state.Accept(midnight, 20, Noon, schedule) == CourierStatus.Closed, "a midnight hunt was accepted at noon");
        var night = Noon.AddHours(-11);
        Assert.True(state.Accept(midnight, 20, night, schedule) == CourierStatus.Ok, "a midnight hunt was refused at one in the night");
        foreach (var quest in new[] { daily, weekly, once, repeat })
        {
            Assert.True(state.Accept(quest, 20, Noon, schedule) == CourierStatus.Ok, $"{quest.Category} was refused");
        }

        foreach (var quest in catalog.Quests)
        {
            for (var kill = 0; kill < 3; kill++)
            {
                state.OnKill(Bargon, Field, catalog);
            }

            Assert.True(state.CanTurnIn(quest) == CourierStatus.Ok, $"{quest.Category} was not ready");
            state.MarkTurnedIn(quest.Id, Noon);
            Assert.True(state.CanTurnIn(quest) == CourierStatus.NotAccepted, $"{quest.Category} could be turned in twice");
        }

        Assert.True(state.Accept(daily, 20, Noon, schedule) == CourierStatus.Completed && state.Accept(daily, 20, Noon.AddDays(1), schedule) == CourierStatus.Ok,
            "the daily hunt did not reopen the next day only");
        Assert.True(state.Accept(midnight, 20, Noon.AddDays(1).AddHours(-11), schedule) == CourierStatus.Ok, "the midnight hunt did not reopen the next night");
        var monday = Noon.AddDays(((int)DayOfWeek.Monday - (int)Noon.DayOfWeek + 7) % 7 + 7);
        Assert.True(state.Accept(weekly, 20, Noon.AddDays(1).AddHours(1), schedule) is CourierStatus.Completed or CourierStatus.Ok
            && state.Accept(weekly, 20, monday, schedule) is CourierStatus.Ok or CourierStatus.AlreadyAccepted, "the weekly hunt did not reopen the next week");
        Assert.True(state.Accept(repeat, 20, Noon.AddMinutes(30), schedule) == CourierStatus.Cooldown
            && state.Standing(repeat, Noon.AddMinutes(30), schedule) == CourierStanding.Cooldown && state.Accept(repeat, 20, Noon.AddMinutes(61), schedule) == CourierStatus.Ok,
            "the repeatable hunt ignored its cooldown");
        Assert.True(state.Accept(once, 20, Noon.AddYears(1), schedule) == CourierStatus.Completed, "the one-time hunt was offered again");
        state.Reset(catalog, CourierCategory.OneTime);
        Assert.True(state.Accept(once, 20, Noon, schedule) == CourierStatus.Ok, "a game master's reset did not reopen the one-time hunt");
        return Task.CompletedTask;
    }

    private static Task FieldCreditAsync()
    {
        var definition = new MonsterDefinition(Bargon, "Bargon", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "")
        {
            ExpReward = 10
        };
        var shared = Quest(1, CourierCategory.Daily, objectives: [(Bargon, 5)]);
        var solo = Quest(2, CourierCategory.Daily, party: false, objectives: [(Bargon, 5)]);
        var catalog = Catalog(shared, solo);
        var field = new FieldInstance(Field, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(5))
        {
            CourierCatalog = () => catalog
        };
        var classes = new CharacterClassRegistry();
        WorldPlayer Join(ulong id, Vector3 position)
        {
            var player = new WorldPlayer(id, position, classes.CreatePlayer(PlayerClass.Fighter, "Hunter" + id, 20), false, default);
            foreach (var quest in catalog.Quests)
            {
                player.Character.Couriers.Accept(quest, 20, DateTimeOffset.UtcNow, catalog.Schedule);
            }

            field.Add(player);
            return player;
        }

        var killer = Join(1, Vector3.Zero);
        var near = Join(2, new Vector3(100, 0, 0));
        var far = Join(3, new Vector3(PartyLimits.ShareRange * 3, 0, 0));
        var stranger = Join(4, new Vector3(50, 0, 0));
        SkillFixtures.Group(killer, near, far);
        var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(24, 0, 0), -1, 0);
        Assert.True(field.SetAttackTarget(1, monster.EntityId, true), "fixture target rejected");
        for (var index = 0; index < 400 && monster.IsAlive; index++)
        {
            if (!killer.InAttackRange(monster.Position))
            {
                killer.ReportMovement(killer.Position + Vector3.Normalize(monster.Position - killer.Position) * 6f, 0f, true, true, monster.EntityId, field.NowMs);
            }

            field.Step(0.05f);
        }

        int Kills(WorldPlayer player, int quest) => player.Character.Couriers.Active[quest].Kills[0];
        Assert.True(!monster.IsAlive && Kills(killer, 1) == 1 && Kills(killer, 2) == 1, "the killer's hunts did not count the kill once");
        Assert.True(Kills(near, 1) == 1 && Kills(near, 2) == 0, "a member in range was not credited for the party hunt only");
        Assert.True(Kills(far, 1) == 0 && Kills(stranger, 1) == 0, "a member out of range or a stranger was credited");
        return Task.CompletedTask;
    }

    private static Task PersistenceAsync()
    {
        var catalog = Catalog(Quest(1, CourierCategory.Daily, objectives: [(Bargon, 2), (HeadCutter, 3)]), Quest(2, CourierCategory.OneTime));
        var state = new CourierQuestState();
        state.Accept(catalog.Quests[0], 20, Noon, catalog.Schedule);
        state.Accept(catalog.Quests[1], 20, Noon, catalog.Schedule);
        state.OnKill(HeadCutter, Field, catalog);
        state.MarkTurnedIn(2, Noon);
        var copy = new CourierQuestState();
        CourierQuestCodec.Restore(copy, CourierQuestCodec.Capture(state));
        Assert.True(copy.Active[1].Kills.SequenceEqual([0, 1]) && copy.Active[1].AcceptedAt == Noon.ToUnixTimeSeconds() && copy.TurnedIn[2] == Noon.ToUnixTimeSeconds()
            && CourierQuestCodec.Capture(new CourierQuestState()) == "", "the hunts did not survive the save");

        var buffer = new byte[FrameCodec.MaxFrameSize];
        var wire = new CourierWireQuest(7, "Clear the Forest", "Thin them out.", (byte)CourierCategory.Weekly, (byte)CourierDifficulty.Hard, Field, 20, 30, 150_000,
            5_000, "PL104", 3, (byte)CourierStanding.Active, 0, [new CourierWireObjective("Bargon", 20, 12), new CourierWireObjective("Head Cutter", 15, 5)]);
        var board = new CourierBoardPacket(91UL, CourierNpcs.Name, [wire, wire with { Id = 8, Objectives = [] }]);
        var readBoard = CourierBoardPacket.Read(buffer.AsSpan(0, board.Write(buffer)));
        Assert.True(readBoard.NpcId == 91 && readBoard.Name == CourierNpcs.Name && readBoard.Quests.Count == 2
            && readBoard.Quests[0] with { Objectives = [] } == wire with { Objectives = [] } && readBoard.Quests[0].Objectives.SequenceEqual(wire.Objectives),
            "the board did not survive the wire");
        var full = new CourierBoardPacket(1UL, new string('n', 60), [.. Enumerable.Range(0, CourierWire.MaxQuests).Select(index => wire with
        {
            Id = index, Name = new string('q', 60), Description = new string('d', 200),
            Objectives = [.. Enumerable.Range(0, CourierWire.MaxObjectives).Select(_ => new CourierWireObjective(new string('m', 40), 999, 999))]
        })]);
        Assert.True(CourierBoardPacket.MaxSize <= FrameCodec.MaxFrameSize && full.Write(new byte[CourierBoardPacket.MaxSize]) <= CourierBoardPacket.MaxSize,
            "a full board does not fit a frame");
        var stateRead = CourierStatePacket.Read(buffer.AsSpan(0, new CourierStatePacket([wire]).Write(buffer)));
        Assert.True(stateRead.Quests.Count == 1 && stateRead.Quests[0].Objectives[1] == new CourierWireObjective("Head Cutter", 15, 5), "the state did not survive the wire");
        var request = new CourierRequestPacket(Guid.NewGuid(), 91UL, 7, CourierRequestPacket.TurnIn);
        Assert.True(CourierRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer))) == request, "the request did not survive the wire");
        var result = new CourierResultPacket(request.OperationId, (byte)CourierStatus.Ok, 150_000);
        Assert.True(CourierResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "the result did not survive the wire");
        return Task.CompletedTask;
    }

    private static Task CatalogAsync()
    {
        var problems = new List<string>();
        var parsed = CourierCatalogStore.Parse(CourierCatalogStore.Write(Catalog(Quest(1, CourierCategory.Daily), Quest(1, CourierCategory.Weekly),
            Quest(2, CourierCategory.Daily) with { Objectives = [] }, Quest(3, CourierCategory.Daily) with { MinLevel = 40, MaxLevel = 30 })), problems);
        Assert.True(parsed.Quests.Count == 1 && problems.Count == 3, $"bad quests were kept: {string.Join("; ", problems)}");

        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "couriers.json");
        var shipped = CourierCatalogStore.Parse(File.ReadAllText(path), problems = []);
        var world = new WorldCatalog();
        Assert.True(problems.Count == 0 && shipped.Couriers.Count > 0 && shipped.Quests.Count > 0, $"the shipped catalog has problems: {string.Join("; ", problems)}");
        Assert.True(shipped.Quests.All(quest => shipped.CourierAt(quest.Giver) is { } post && post.FieldCode == quest.FieldCode && quest.TurnIn == quest.Giver),
            "a shipped hunt names no Courier of its field");
        Assert.True(shipped.Couriers.All(post => world.TryGetFieldByCode(post.FieldCode, out var field) && field.State is not (FieldState.Village or FieldState.Castle)),
            "a Courier stands outside a hunting field");
        Assert.True(Enum.GetValues<CourierCategory>().All(category => shipped.Quests.Any(quest => quest.Category == category))
            && Enum.GetValues<CourierDifficulty>().All(difficulty => shipped.Quests.Any(quest => quest.Difficulty == difficulty)),
            "a category or difficulty has no shipped hunt");
        Assert.True(NpcServiceAvailability.IsAvailable(new NpcService(NpcServiceKind.Courier, 0)), "a Courier's service shows as unavailable");
        return Task.CompletedTask;
    }

    private static Task RewardsAsync()
    {
        MonsterDefinition Monster(int code, int level, int exp) => new(code, "M" + code, MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(level, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "") { ExpReward = (ulong)exp };
        var monsters = new[] { Monster(1, 10, 100), Monster(2, 14, 200), Monster(3, 18, 400) };
        var spawns = new Dictionary<int, FieldSpawnData>
        {
            [Field] = new([], [.. monsters.Select(monster => new SpawnTableEntry(monster.Code, 10))], [], [])
        };
        var data = new WorldData(new ItemCatalog([]), new MonsterCatalog(monsters), new Simulation.World.Npcs.NpcCatalog([]), spawns, MotionTimingCatalog.Empty);
        var catalog = CourierCatalogProbe.Generate(new WorldCatalog(), data);
        Assert.True(catalog.Couriers.Count == 1 && catalog.Quests.Count == 6, $"{catalog.Couriers.Count} Couriers and {catalog.Quests.Count} hunts for one field");
        var patrol = catalog.Quests.Single(quest => quest.Category == CourierCategory.Daily && quest.Difficulty == CourierDifficulty.Easy);
        var master = catalog.Quests.Single(quest => quest.Category == CourierCategory.OneTime);
        // 30 of the weakest at 100 each, a 30 % share; 100 of the strongest at 400 each, 60 % doubled for the one-time hunt.
        Assert.True(patrol.Experience == 900 && patrol.Objectives.Single().MonsterId == 1 && master.Experience == 48_000 && master.Objectives.Single().MonsterId == 3,
            $"rewards {patrol.Experience} and {master.Experience}");
        Assert.True(catalog.Quests.All(quest => quest.MinLevel <= quest.Objectives.Min(objective => monsters.Single(monster => monster.Code == objective.MonsterId).Combat.Level)
            && quest.MaxLevel >= quest.MinLevel), "a hunt's level range does not reach its monsters");
        return Task.CompletedTask;
    }
}
