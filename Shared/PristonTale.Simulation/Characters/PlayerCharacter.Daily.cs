namespace PristonTale.Simulation.Characters;

public sealed partial class PlayerCharacter
{
    // The daily quests of the current day (Quests.DailyQuests), saved with the character.
    public Quests.DailyQuestState DailyQuests { get; } = new();

    // The Quest Courier hunts accepted and turned in (Quests.CourierQuestState), saved with the character.
    public Quests.CourierQuestState Couriers { get; } = new();

    // The looks chosen in The Cabinet (SkinRules), saved with the character.
    public SkinSet Skins { get; set; } = SkinSet.None;

    // The caravan out, or null (Caravans).
    public CaravanState Caravan { get; set; }
}
