using System.Globalization;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Quests;

public enum QuestBookSide : byte
{
    Both,
    Tempskron,
    Morion
}

public enum QuestBookStatus : byte
{
    Locked,
    Available,
    Running,
    Done
}

// What the quest book knows of one character: its level, class tier and side, the running quest and the finished logs.
public readonly record struct QuestBookFacts(int Level, int Tier, bool Tempskron, QuestCode Active, LevelQuestLog LevelLog, ElementaryQuestLog ElementaryLog);

// One quest of the book. Codes: the quest codes that run it (a rank-up has one per branch). A quest is done when its log
// bit is set or, for a rank-up, once the tier is past RankUpTier.
public sealed record QuestBookEntry(string Title, int Level, QuestBookSide Side, string Task, IReadOnlyList<string> Rewards)
{
    public IReadOnlyList<QuestCode> Codes { get; init; } = [];
    public LevelQuestLog LevelBit { get; init; }
    public LevelQuestLog LevelRequires { get; init; }
    public ElementaryQuestLog ElementaryBit { get; init; }
    public int ElementaryIndex { get; init; } = -1;
    public int RankUpTier { get; init; } = -1;
    public bool Repeatable { get; init; }

    // The NPC who offers the quest (by the town it stands in when the sides differ).
    public string Giver { get; init; } = "";

    // The givers' NPC names exactly as the world names them, for the marks over their heads.
    public IReadOnlyList<string> GiverNpcs { get; init; } = [];
}

// Every quest the game offers, with its task and rewards, for the client's quest book. Levels, hunts and point rewards
// come from the quest families' own tables and constants.
public static class QuestBook
{
    private static readonly string[] BeginnerTitles =
    [
        "A tired warehouse keeper", "Signs of fear", "Candy Delivery", "Goblin Hill", "Threats of the Undead", "One-eyed Monsters", "A lost Hammer"
    ];

    private static readonly string[] BeginnerTasks =
    [
        "Take the instructor's cake to the warehouse keeper.",
        "Hunt 5 Hopy (Tempskron) or Rabie (Morion).",
        "Take the instructor's candy to the smith.",
        "Hunt 5 Hobgoblin (Tempskron) or North Goblin (Morion).",
        "Hunt 10 Skeleton (Tempskron) or Corrupt (Morion).",
        "Hunt 10 Cyclops.",
        "Find the lost hammer the Bargons drop."
    ];

    private static readonly string[][] BeginnerRewards =
    [
        ["A beginner weapon for your class"],
        ["A class armlet"],
        ["10 life, 10 mana and 10 stamina potions"],
        ["A class amulet and 2 class rings"],
        ["2 force orbs"],
        ["20 life, 20 mana and 20 stamina potions"],
        ["2 force orbs"]
    ];

    private static readonly string[] MilestoneTitles = ["For her", "The Cave", "The Cave", "Helping Michelle", "Enclosed Fury", "Tears of Khalia", "Village of Eura"];

    private const string HomeMasters = "Skill Master Verkan (Ricarten) or Witchcraft Academy Raymon (Pillai)";
    private const string Verkan = "Skill Master, Verkan";
    private const string Raymon = "Witchcraft Academy, Raymon";
    private const string Derik = "Knight Commander Derik";
    private const string Ray = "Royalmage Ray";
    private const string Priest = "Priest of the temple";

    private static readonly string[] MilestoneGivers =
    [
        "Miscellaneous Store Allya (Ricarten) or Pillia (Pillai)", "Skill Master Verkan (Ricarten)", "Witchcraft Academy Raymon (Pillai)",
        "Priest of the temple", "Priest of the temple", "Priest of the temple", "Force Master"
    ];

    private static readonly string[][] MilestoneGiverNpcs =
    [
        ["Miscellaneous Store, Allya", "Miscellaneous Store, Pillia"], [Verkan], [Raymon], [Priest], [Priest], [Priest], ["Force Master"]
    ];

    public static IReadOnlyList<QuestBookEntry> All { get; } = Build();

    // The book's entry of a running quest code, or null.
    public static QuestBookEntry Of(QuestCode code)
    {
        foreach (var entry in All)
        {
            for (var index = 0; index < entry.Codes.Count; index++)
            {
                if (entry.Codes[index] == code)
                {
                    return entry;
                }
            }
        }

        return null;
    }

    // The quests of one side: the level 55 hunt has a branch per side.
    public static QuestBookEntry[] For(bool tempskron)
    {
        var side = tempskron ? QuestBookSide.Tempskron : QuestBookSide.Morion;
        return All.Where(entry => entry.Side == QuestBookSide.Both || entry.Side == side).ToArray();
    }

    public static QuestBookStatus StatusOf(QuestBookEntry entry, in QuestBookFacts facts)
    {
        for (var index = 0; index < entry.Codes.Count; index++)
        {
            if (entry.Codes[index] == facts.Active)
            {
                return QuestBookStatus.Running;
            }
        }

        if (entry.Repeatable)
        {
            return QuestBookStatus.Available;
        }

        if (entry.LevelBit != LevelQuestLog.None && (facts.LevelLog & entry.LevelBit) != 0
            || entry.ElementaryBit != ElementaryQuestLog.None && (facts.ElementaryLog & entry.ElementaryBit) != 0
            || entry.RankUpTier >= 0 && facts.Tier > entry.RankUpTier)
        {
            return QuestBookStatus.Done;
        }

        var open = facts.Level >= entry.Level
            && (facts.LevelLog & entry.LevelRequires) == entry.LevelRequires
            && (entry.ElementaryIndex < 0 || facts.Level <= ElementaryQuests.LevelCap && entry.ElementaryIndex == ElementaryQuests.Completed(facts.ElementaryLog))
            && (entry.RankUpTier < 0 || facts.Tier == entry.RankUpTier);
        return open ? QuestBookStatus.Available : QuestBookStatus.Locked;
    }

    // One entry per elite chain: the client does not receive the elite log, so a chain shows Available from its level on.
    private static void AddEliteChain(List<QuestBookEntry> entries, int chain, string title, string task, string reward, string giver, string giverNpc)
    {
        var codes = new List<QuestCode>();
        foreach (var quest in EliteQuests.All)
        {
            if (quest.Chain == chain)
            {
                codes.Add(quest.Code);
            }
        }

        entries.Add(new QuestBookEntry(title, EliteQuests.ChainLevels[chain], QuestBookSide.Both, task, [reward])
        {
            Codes = codes,
            Giver = giver,
            GiverNpcs = [giverNpc]
        });
    }

    private static QuestBookEntry[] Build()
    {
        var entries = new List<QuestBookEntry>();
        for (var index = 0; index < BeginnerTitles.Length; index++)
        {
            entries.Add(new QuestBookEntry(BeginnerTitles[index], 1, QuestBookSide.Both,
                BeginnerTasks[index] + string.Create(CultureInfo.InvariantCulture, $" The instructor offers these in order up to level {ElementaryQuests.LevelCap}."),
                BeginnerRewards[index])
            {
                Codes = [(QuestCode)((int)QuestCode.ElementaryA + index)],
                ElementaryBit = QuestCodes.ElementaryBit(index),
                ElementaryIndex = index,
                Giver = "Guide Ran (Ricarten or Pillai)",
                GiverNpcs = ["Guide Ran"]
            });
        }

        AddRankUp(entries, 0, "1st Rank-Up", "Tempskron classes and the Assassin bring the silver, bronze and golden wolverine trophies; the other "
            + "Morion classes brew a royal jelly or a hair tonic for a villager.", ["The second class tier"],
            [QuestCode.RankUpTrophies, QuestCode.RankUpHoney, QuestCode.RankUpOil]);
        for (var index = 0; index < LevelQuests.Milestones.Count; index++)
        {
            var spec = LevelQuests.Milestones[index];
            var side = spec.Code switch
            {
                QuestCode.Level55 => QuestBookSide.Tempskron,
                QuestCode.Level55Morion => QuestBookSide.Morion,
                _ => QuestBookSide.Both
            };
            entries.Add(new QuestBookEntry(MilestoneTitles[index], spec.Level, side,
                string.Create(CultureInfo.InvariantCulture, $"Hunt {spec.Count} {QuestMonsters.NameOf(spec.Prey)}."), MilestoneRewards(spec))
            {
                Codes = [spec.Code],
                Giver = MilestoneGivers[index],
                GiverNpcs = MilestoneGiverNpcs[index],
                LevelBit = spec.Bit,
                LevelRequires = spec.Requires
            });
        }

        AddRankUp(entries, 1, "2nd Rank-Up", "Buy your class's quest weapon with three vamp sheltoms and grow it three times by hunting.",
            ["The third class tier", "The class ring", string.Create(CultureInfo.InvariantCulture, $"+{QuestRewards.LifeAfterSecondRankUp} life")],
            [QuestCode.RankUpQuestWeapon]);
        AddRankUp(entries, 2, "3rd Rank-Up", "Two timed hunts, the royal amulet from a named monster, then a duel with your class master.",
            ["The fourth class tier"], [QuestCode.RankUpMasterTrial]);
        AddRankUp(entries, 3, "4th Rank-Up", string.Create(CultureInfo.InvariantCulture,
                $"Three hunts of {RankUpByHestian.HuntKills} for {RankUpByHestian.MoonStones} Moon Stones, then defeat Hestian in the arena."),
            ["The fifth class tier"], [QuestCode.RankUpHestian]);

        entries.Add(new QuestBookEntry("Bitter ordeal", QuestRewards.FirstChainLevel, QuestBookSide.Both, "Bring three bonus vamps for a recommendation.",
            [string.Create(CultureInfo.InvariantCulture, $"+{QuestRewards.FirstChainPoints} stat points per level from {QuestRewards.FirstChainLevel}")])
        {
            Codes = [QuestCode.Level80Bonus],
            Giver = "Knight Commander Derik (Ricarten), then Civilian Minuren",
            GiverNpcs = [Derik, "Civilian Minuren"],
            LevelBit = LevelQuestLog.Level80Bonus
        });
        entries.Add(new QuestBookEntry("Test of Kingdom", QuestRewards.SecondChainLevel, QuestBookSide.Both,
            string.Create(CultureInfo.InvariantCulture, $"Hunt {LevelBonusQuests.CursedHuntCount} Omega, D-Machine or Mountain under a {QuestRewards.CursedDamagePercent}% damage curse."),
            [string.Create(CultureInfo.InvariantCulture, $"+{QuestRewards.SecondChainPoints} more stat points per level from {QuestRewards.SecondChainLevel}")])
        {
            Codes = [QuestCode.Level90Bonus],
            Giver = "Knight Commander Derik (Ricarten), then Royalmage Ray (Pillai)",
            GiverNpcs = [Derik, Ray],
            LevelBit = LevelQuestLog.Level90Bonus
        });
        entries.Add(new QuestBookEntry("Fury of Phantom", FuryOfPhantomQuest.MinimumLevel, QuestBookSide.Both,
            "Two field hunts and three duels with Fury in the arena.", ["One of eleven weapons of your choice, any class's"])
        {
            Codes = [QuestCode.FuryOfPhantom],
            Giver = "Royalmage Ray (Pillai)",
            GiverNpcs = [Ray],
            ElementaryBit = ElementaryQuestLog.FuryOfPhantom
        });
        AddEliteChain(entries, 0, "Lovers of history", "Six quests for the Force Master and Royalmage Ray.", "+2 elite skill points", "Force Master", "Force Master");
        AddEliteChain(entries, 1, "War of the Ancient", "Eight quests for Knight Commander Derik, Craftman Salon and Clever Newter.", "+3 elite skill points",
            "Knight Commander Derik (Ricarten)", Derik);
        AddEliteChain(entries, 2, "Mission from the ancestor", "Seven quests for the Wandering Soul and Sage Celina.",
            "+3 elite skill points, +10 stat points and the Dragon Crystal", "Wandering Soul", "Wandering Soul");
        for (var index = 0; index < WingQuest.ExchangeCount; index++)
        {
            var wing = WingGates.Wings[index].Name;
            var bring = index == 0 ? "Bring two crystals" : $"Bring the {WingGates.Wings[index - 1].Name} and two crystals";
            entries.Add(new QuestBookEntry("Wing exchange: " + wing, 1, QuestBookSide.Both,
                string.Create(CultureInfo.InvariantCulture, $"{bring} and {WingQuest.ExchangeGold(index):N0} gold to the wing keeper."), [wing])
            {
                Repeatable = true,
                Giver = "Inventor of Warpgate (Ricarten)",
                GiverNpcs = ["Inventor of Warpgate"]
            });
        }

        return entries.ToArray();
    }

    private static void AddRankUp(List<QuestBookEntry> entries, int tier, string title, string task, string[] rewards, QuestCode[] codes) =>
        entries.Add(new QuestBookEntry(title, RankUpRules.RequiredLevel(tier), QuestBookSide.Both, task, rewards)
        {
            Codes = codes,
            Giver = tier == 3 ? HomeMasters + ", then Elder Hosean" : HomeMasters,
            GiverNpcs = tier == 3 ? [Verkan, Raymon, "Elder Hosean"] : [Verkan, Raymon],
            RankUpTier = tier
        });

    private static string[] MilestoneRewards(LevelQuests.Spec spec) => spec.Code switch
    {
        QuestCode.Level85 => [string.Create(CultureInfo.InvariantCulture, $"Death costs less experience from level {spec.Level}")],
        QuestCode.Level90 => [string.Create(CultureInfo.InvariantCulture, $"+{QuestRewards.LifeAfterLevel90} life from level {spec.Level}"), "The level 90 token"],
        _ => Points(spec.StatPoints, spec.SkillPoints)
    };

    private static string[] Points(int stat, int skill)
    {
        var lines = new List<string>(2);
        if (stat > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"+{stat} stat points"));
        }

        if (skill > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"+{skill} skill point{(skill == 1 ? "" : "s")}"));
        }

        return lines.ToArray();
    }
}
