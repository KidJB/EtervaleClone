using System.Globalization;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;

namespace PristonTale.Gameplay.Discord;

// The lines the profile shows: where the player is, and whether alone or in a party with the character and its level. The
// large picture is the application's "logo" art asset, its hover text the class.
public static class DiscordPresenceText
{
    public const string LogoKey = "logo";
    private const string GameName = "Etervale";

    public static DiscordActivity Menu(long start, string doing) => new(GameName, doing, LogoKey, GameName, start);

    public static DiscordActivity Playing(long start, string name, PlayerClass playerClass, int level, string field, int partyMembers)
    {
        var who = string.Create(CultureInfo.InvariantCulture, $"{name} (Lv {level})");
        var state = partyMembers > 1
            ? string.Create(CultureInfo.InvariantCulture, $"Party ({partyMembers}/{PartyLimits.MaxMembers}) - {who}")
            : "Solo - " + who;
        return new DiscordActivity(field.Length > 0 ? field : GameName, state, LogoKey, playerClass.ToString(), start);
    }
}
