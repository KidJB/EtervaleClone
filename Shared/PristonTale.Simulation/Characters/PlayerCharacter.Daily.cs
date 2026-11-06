namespace PristonTale.Simulation.Characters;

public sealed partial class PlayerCharacter
{
    // The daily quests of the current day (Quests.DailyQuests), saved with the character.
    public Quests.DailyQuestState DailyQuests { get; } = new();

    // The looks chosen in The Cabinet (SkinRules), saved with the character.
    public SkinSet Skins { get; set; } = SkinSet.None;
}
