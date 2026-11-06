using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.World;

// One boss arena event: the boss (a monster definition the world data adds, BossArena.BossDefinitions), the minions that
// guard it, the level to enter, the experience by kill time (percent of what the level needs, within 10 and 20 minutes and
// after), the gold, and the prizes one of which each winner draws (item key and count).
public sealed record BossArenaEvent(int Id, string Name, string Boss, string Minion, int MinLevel, int[] ExpPercent, long Gold,
    IReadOnlyList<(string ItemKey, int Count)> Prizes);

// The boss arena, as the King Fury event of the original's private servers: every six hours of the server's clock (00:00
// and 12:00 King Fury, 06:00 and 18:00 Hagios) a boss and its minions appear in the middle of Cursed Temple 2F, a hunting field that stays open: the arena is the ground within Radius of the middle. Players of the event's
// level enter through the arena NPC of Ricarten in the 15 minutes before it starts; the event ends when the boss falls
// (every player still in the arena is rewarded), after two hours, or once no player is left inside.
public static class BossArena
{
    public const int FieldCode = 23;
    public const string FieldStage = "dun-5";
    public const int StatueNpcId = 47;
    public const int ReturnFieldCode = 3;
    public const string NpcName = "Knight Commander Derik";
    public const int EntryMinutes = 15;
    public const int MaxMinutes = 120;
    public const int MinionRespawnSeconds = 60;
    public const int Minions = 6;
    public static readonly int[] AnnounceMinutes = [30, 15, 10, 5];
    public static readonly int[] StartHours = [0, 6, 12, 18];

    // Where the boss stands, the minions around it, where players arrive at the arena entrance and where they return in
    // Ricarten (world units).
    public static readonly (float X, float Z) Center = (-3617f, -43178f);
    public const float MinionRing = 260f;
    public const float Radius = 1200f;
    public static readonly (float X, float Z) Arrival = (-3617f, -42950f);
    public static readonly (float X, float Z) Return = (2560f, -18100f);

    public static readonly IReadOnlyList<BossArenaEvent> Events =
    [
        new(0, "King Fury", "King Fury", "Fire Worm 2", 100, [30, 25, 20], 1_000_000, [("FO104", 1), ("FO106", 1), ("PL104", 50)]),
        new(1, "Hagios", "Hagios", "Fire Worm 2", 120, [40, 35, 30], 2_000_000, [("FO106", 1), ("FO106", 2), ("PL104", 100)])
    ];

    // The event that starts at an hour of StartHours: King Fury at 00:00 and 12:00, Hagios at 06:00 and 18:00.
    public static BossArenaEvent EventAt(int hour) => Events[hour % 12 == 0 ? 0 : 1];

    // The next start at or after the moment (server-local time).
    public static DateTimeOffset NextStart(DateTimeOffset now)
    {
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        foreach (var hour in StartHours)
        {
            var start = day.AddHours(hour);
            if (start >= now)
            {
                return start;
            }
        }

        return day.AddDays(1);
    }

    public static bool Inside(float x, float z)
    {
        var (dx, dz) = (x - Center.X, z - Center.Z);
        return dx * dx + dz * dz <= Radius * Radius;
    }

    public static bool EntryOpen(DateTimeOffset now, DateTimeOffset start) => now >= start.AddMinutes(-EntryMinutes) && now < start;

    // The experience a winner of that level gets for a kill after that many minutes.
    public static ulong Experience(BossArenaEvent arena, int level, double minutes)
    {
        if (level >= ExperienceTable.MaximumLevel)
        {
            return 0;
        }

        var percent = minutes <= 10 ? arena.ExpPercent[0] : minutes <= 20 ? arena.ExpPercent[1] : arena.ExpPercent[2];
        return (ExperienceTable.ForLevel(level + 1) - ExperienceTable.ForLevel(level)) / 100 * (ulong)percent;
    }
}

public static class BossArenaBosses
{
    // The arena's bosses, made from the imported monsters when the world data loads: King Fury from Fury (larger, with a
    // boss's life and doubled damage), Hagios from the strongest Fury with its own model (assets/char/monster/Hagios).
    public static IEnumerable<Monsters.MonsterDefinition> From(IEnumerable<Monsters.MonsterDefinition> monsters)
    {
        var fury = monsters.FirstOrDefault(static monster => monster.Name == "Fury");
        var strongest = monsters.FirstOrDefault(static monster => monster.Name == "Fury 4") ?? fury;
        if (fury is not null)
        {
            yield return Boss(fury, 900_001, "King Fury", fury.ModelInxPath, 1.8f, 40);
        }

        if (strongest is not null)
        {
            yield return Boss(strongest, 900_002, "Hagios", @"char\monster\Hagios\Hagios.inx", 1.3f, 60);
        }
    }

    private static Monsters.MonsterDefinition Boss(Monsters.MonsterDefinition from, int code, string name, string model, float scale, int lifeTimes) =>
        new(code, name, from.Faction, Monsters.MonsterClass.Boss, from.Brood, from.Combat with
        {
            MaxLife = from.Combat.MaxLife * lifeTimes,
            AttackDamage = new Monsters.MonsterRange(from.Combat.AttackDamage.Min * 2, from.Combat.AttackDamage.Max * 2)
        }, model)
        {
            Key = name,
            Skill = from.Skill,
            Ai = from.Ai,
            ModelScale = scale,
            SoundCode = from.SoundCode,
            ShadowSizeLevel = 3,
            PortraitOffsetY = from.PortraitOffsetY,
            PortraitOffsetZ = from.PortraitOffsetZ,
            MagicResistance = from.MagicResistance,
            MoveType = from.MoveType
        };
}

public static class BossArenaMessages
{
    public static string Soon(BossArenaEvent arena, int minutes) =>
        $"Boss Arena> {arena.Name} appears in {minutes} minutes! Talk to {BossArena.NpcName} in Ricarten to enter (level {arena.MinLevel}+).";

    public static string Started(BossArenaEvent arena) => $"Boss Arena> {arena.Name} has appeared! The arena is closed.";

    public static string Defeated(BossArenaEvent arena, double minutes) =>
        $"Boss Arena> {arena.Name} was defeated in {(int)Math.Ceiling(minutes)} minutes!";

    public static string Ended(BossArenaEvent arena) => $"Boss Arena> The {arena.Name} event has ended.";

    public static string Next(BossArenaEvent arena, DateTimeOffset start, bool open) => open
        ? $"Boss Arena> {arena.Name} (level {arena.MinLevel}+) starts at {start:HH:mm}. Entry is open: talk to {BossArena.NpcName}."
        : $"Boss Arena> Next: {arena.Name} (level {arena.MinLevel}+) at {start:HH:mm} server time. Entry opens {BossArena.EntryMinutes} minutes before.";

    public const string NotOpen = "Boss Arena> The arena is not open for entry now.";
    public const string NotAtNpc = "Boss Arena> Talk to " + BossArena.NpcName + " in Ricarten to enter.";
    public const string Busy = "Boss Arena> Close your shop or trade first.";

    public static string TooLow(BossArenaEvent arena) => $"Boss Arena> You must be level {arena.MinLevel} or above.";

    public static string Rewarded(ulong experience, long gold, string prize) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Boss Arena> Reward: {experience:N0} experience, {gold:N0} gold, and {prize} waiting in the Item Distributor.");
}
