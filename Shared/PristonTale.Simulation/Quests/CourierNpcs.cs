using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Quests;

// The Quest Couriers, Etervale's own NPCs added when the world data loads: each borrows the model and motions of the daily
// quest giver (DailyQuests.GiverName) and offers the Courier service in its field.
public static class CourierNpcs
{
    public const string Name = "Quest Courier";
    public const string Line = "Hunts for every hunter in these lands. Take one, bring back proof, and be paid in experience.";

    // The catalog with a Courier NPC for each post, when the giver whose model they borrow was imported.
    public static List<NpcDefinition> AddTo(List<NpcDefinition> npcs, CourierCatalog catalog)
    {
        var model = npcs.Find(static npc => npc.Label == DailyQuests.GiverName || npc.Name == DailyQuests.GiverName);
        if (model is null || catalog.Couriers.Count == 0)
        {
            return npcs;
        }

        return
        [
            .. npcs,
            .. catalog.Couriers.Select(post => model with
            {
                Code = post.NpcCode,
                Name = post.Name,
                DisplayName = post.Name,
                Services = [new NpcService(NpcServiceKind.Courier)],
                Shop = NpcShopStock.Empty,
                Dialog = [Line],
                MoveRange = 0
            })
        ];
    }
}
