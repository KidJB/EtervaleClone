using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Markets;

// The two market NPCs of Ricarten, Etervale's own and added when the world data loads. Each borrows the model and motions of an
// imported NPC; positions are in the field's fixed-point units.
public static class MarketNpcs
{
    public const string Stage = "village-2";
    public const int CharacterMarketCode = 9001;
    public const int MarketplaceCode = 9002;
    public const string CharacterMarketName = "Character Market";
    public const string MarketplaceName = "Marketplace";
    private const string CharacterMarketModel = "Hermit";
    private const string MarketplaceModel = "Derik";

    public static readonly IReadOnlyList<(int Code, int X, int Y, int Z, int Angle)> Placements =
    [
        (CharacterMarketCode, 470_000, 54_057, -4_462_000, 2860),
        (MarketplaceCode, 485_000, 54_057, -4_535_000, 2860)
    ];

    // The catalog with the market NPCs added, when the NPCs whose models they borrow were imported.
    public static List<NpcDefinition> AddTo(List<NpcDefinition> npcs)
    {
        var characterBase = npcs.Find(static npc => npc.ModelInxPath.Contains(CharacterMarketModel, StringComparison.OrdinalIgnoreCase));
        var marketBase = npcs.Find(static npc => npc.ModelInxPath.Contains(MarketplaceModel, StringComparison.OrdinalIgnoreCase));
        if (characterBase is null || marketBase is null)
        {
            return npcs;
        }

        return
        [
            .. npcs,
            Made(characterBase, CharacterMarketCode, CharacterMarketName, NpcServiceKind.CharacterMarket, "Characters change hands here, for those with the coins."),
            Made(marketBase, MarketplaceCode, MarketplaceName, NpcServiceKind.Marketplace, "Every open shop in the land, without the walk.")
        ];
    }

    private static NpcDefinition Made(NpcDefinition source, int code, string name, NpcServiceKind service, string line) => source with
    {
        Code = code,
        Name = name,
        DisplayName = name,
        Services = [new NpcService(service)],
        Shop = NpcShopStock.Empty,
        Dialog = [line],
        MoveRange = 0
    };
}
