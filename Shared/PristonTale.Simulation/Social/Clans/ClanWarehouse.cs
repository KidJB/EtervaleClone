using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Social.Clans;

// What the clan warehouse log records; append values only (stored).
public enum ClanWarehouseLogAction : byte
{
    Deposit = 1,
    Withdraw = 2,

    // Gold given to the treasury (Quantity) and an upgrade bought (ItemKey: the upgrade, Quantity: its new level).
    Donate = 3,
    Upgrade = 4,

    // Coins given to the coin treasury (Quantity) and coins spent on a clan buff (ItemKey: the buff's clan item, Quantity: the
    // coins).
    DonateCoins = 5,
    SpendCoins = 6
}

// One line of the clan warehouse log: who, what, how many and when (Unix seconds).
public sealed record ClanWarehouseLogEntry(long AtSeconds, string Character, ClanWarehouseLogAction Action, string ItemKey, string ItemName, int Quantity);

// The clan warehouse: storage owned by the clan, shared by its members by their rights (ClanPermission), two pages of the
// store grid holding 40% more than the account warehouse (140 items, 1400 weight), without gold, which the clan's
// treasury upgrades (ClanUpgrades: more items, pages and weight).
public static class ClanWarehouse
{
    public const int Pages = 2;
    public const int MaxItems = Contracts.Social.ClanLimits.WarehouseItems;
    public const int MaxWeight = 1400;
    public const int LogLines = Contracts.Social.ClanLimits.WarehouseLogLines;

    // The Clan Warehouse keeper: an NPC of its own in the market town (Markets.MarketNpcs.Stage, beside the market NPCs),
    // drawn as the first imported warehouse keeper, offering only the clan warehouse.
    public const int KeeperCode = 9003;
    public const string KeeperName = "Clan Warehouse";
    public static readonly (int Code, int X, int Y, int Z, int Angle) KeeperPlacement = (KeeperCode, 500_000, 54_057, -4_608_000, 2860);

    // The catalog with the keeper added, when a warehouse keeper whose model it borrows was imported.
    public static List<NpcDefinition> AddTo(List<NpcDefinition> npcs)
    {
        var model = npcs.Find(static npc => npc.Find(NpcServiceKind.Warehouse) is not null);
        if (model is null || npcs.Exists(static npc => npc.Code == KeeperCode))
        {
            return npcs;
        }

        return
        [
            .. npcs,
            model with
            {
                Code = KeeperCode,
                Name = KeeperName,
                DisplayName = KeeperName,
                Services = [new NpcService(NpcServiceKind.ClanWarehouse)],
                Shop = NpcShopStock.Empty,
                Dialog = ["What your clan keeps, I keep for all of you."],
                MoveRange = 0
            }
        ];
    }

    public static WarehouseStorage Create(long revision = 0, int slotLevel = 0, int weightLevel = 0) =>
        new(revision, 0, ClanUpgrades.Pages(slotLevel), ClanUpgrades.ItemLimit(slotLevel), ClanUpgrades.WeightLimit(weightLevel));

    // The same items at the same anchors under the limits of these levels (an upgrade only raises them).
    public static WarehouseStorage Resized(WarehouseStorage storage, int slotLevel, int weightLevel)
    {
        var resized = Create(storage.Revision, slotLevel, weightLevel);
        foreach (var (anchor, item) in storage.Occupied())
        {
            resized.Restore(item.Copy(item.Count, preserveId: true), anchor);
        }

        return resized;
    }

    // The right each change needs: putting in, taking out, and moving (either).
    public static bool Allows(ClanPermission rights, NpcServiceAction action) => action switch
    {
        NpcServiceAction.Open or NpcServiceAction.Close or NpcServiceAction.Refresh => true,
        NpcServiceAction.Deposit => ClanRankPermissions.Allows(rights, ClanPermission.Deposit),
        NpcServiceAction.Withdraw => ClanRankPermissions.Allows(rights, ClanPermission.Withdraw),
        NpcServiceAction.Move => ClanRankPermissions.Allows(rights, ClanPermission.Deposit) || ClanRankPermissions.Allows(rights, ClanPermission.Withdraw),
        _ => false
    };
}
