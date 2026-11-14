using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Parties;

// A party's finder settings, chosen by its leader: how players join, the lowest level it takes, its password (Password
// mode), and the classes it takes (a bit per PlayerClass value).
public readonly record struct PartyFinderSettings(PartyJoinMode Mode, int MinLevel, string Password, ushort Classes)
{
    public const int MaxPasswordLength = 16;
    public const int MaxMinLevel = 200;

    public static readonly ushort AllClasses = (ushort)Enum.GetValues<PlayerClass>().Aggregate(0, static (mask, job) => mask | Bit(job));

    public static readonly PartyFinderSettings Default = new(PartyJoinMode.Approval, 0, "", AllClasses);

    public static int Bit(PlayerClass job) => 1 << (int)job;

    public bool Takes(PlayerClass job) => (Classes & Bit(job)) != 0;

    public bool TakesAll => (Classes & AllClasses) == AllClasses;

    // These settings made valid: a known mode, a level within range, a password cut to its limit (none outside Password
    // mode), only known classes and at least one.
    public PartyFinderSettings Valid()
    {
        var mode = Enum.IsDefined(Mode) ? Mode : PartyJoinMode.Approval;
        var password = mode == PartyJoinMode.Password ? (Password ?? "").Trim() : "";
        if (password.Length > MaxPasswordLength)
        {
            password = password[..MaxPasswordLength];
        }

        var classes = (ushort)(Classes & AllClasses);
        return new PartyFinderSettings(mode == PartyJoinMode.Password && password.Length == 0 ? PartyJoinMode.Approval : mode,
            Math.Clamp(MinLevel, 0, MaxMinLevel), password, classes == 0 ? AllClasses : classes);
    }

    // Whether a player of this level and class, giving this password, may join through the finder.
    public PartyFinderRefusal Check(int level, PlayerClass job, string password)
    {
        if (Mode == PartyJoinMode.Closed)
        {
            return PartyFinderRefusal.Closed;
        }

        if (level < MinLevel)
        {
            return PartyFinderRefusal.Level;
        }

        if (!Takes(job))
        {
            return PartyFinderRefusal.Class;
        }

        return Mode == PartyJoinMode.Password && !string.Equals(password, Password, StringComparison.Ordinal) ? PartyFinderRefusal.Password : PartyFinderRefusal.None;
    }

    public GameText Text(PartyFinderRefusal refusal) => refusal switch
    {
        PartyFinderRefusal.Closed => GameText.Of(GameTextId.PartyFinderClosed),
        PartyFinderRefusal.Level => GameText.Of(GameTextId.PartyFinderLevel, MinLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        PartyFinderRefusal.Class => GameText.Of(GameTextId.PartyFinderClass),
        _ => GameText.Of(GameTextId.PartyFinderPassword)
    };
}
