using PristonTale.Gameplay.UI.Quests;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Widgets;

// The minimap icon of an NPC: its quest mark when it has one (HudQuestBook.FillMarks), otherwise the icon of its first
// service in the order below. A shop is a smith's anvil or a magic shop by its keeper's name, otherwise a supply shop.
public sealed partial class HudCombat
{
    private string NpcMapIcon(Entity entity)
    {
        var name = Name(entity.Spawn.EntityId);
        if (QuestMarks.TryGetValue(name, out var mark))
        {
            return mark switch
            {
                HudQuestBook.ReadyMark => HudMinimap.QuestFinishIcon,
                HudQuestBook.TakenMark => HudMinimap.QuestProgressIcon,
                HudQuestBook.RepeatableMark => HudMinimap.QuestRepeatableIcon,
                _ => HudMinimap.QuestStartIcon
            };
        }

        var services = _catalog.GetNpc(entity.Spawn.TemplateId)?.Services ?? NpcServiceSet.None;
        if (services.Has(NpcServiceKind.WarpGate) || services.Has(NpcServiceKind.Teleporter))
        {
            return HudMinimap.WarpIcon;
        }

        if (services.Has(NpcServiceKind.Warehouse))
        {
            return HudMinimap.WarehouseIcon;
        }

        if (services.Has(NpcServiceKind.ItemAging) || services.Has(NpcServiceKind.ItemMix) || services.Has(NpcServiceKind.Smelting)
            || services.Has(NpcServiceKind.Manufacture) || services.Has(NpcServiceKind.MixtureReset))
        {
            return HudMinimap.CraftIcon;
        }

        if (services.Has(NpcServiceKind.SkillMaster))
        {
            return HudMinimap.SkillMasterIcon;
        }

        if (services.Has(NpcServiceKind.Shop))
        {
            return ShopIcon(name);
        }

        return null;
    }

    private static string ShopIcon(string keeper)
    {
        if (keeper.Contains("Weapon", StringComparison.OrdinalIgnoreCase) || keeper.Contains("Armor", StringComparison.OrdinalIgnoreCase)
            || keeper.Contains("Defense", StringComparison.OrdinalIgnoreCase) || keeper.Contains("Smith", StringComparison.OrdinalIgnoreCase))
        {
            return HudMinimap.SmithIcon;
        }

        return keeper.Contains("Magic", StringComparison.OrdinalIgnoreCase) || keeper.Contains("Witchcraft", StringComparison.OrdinalIgnoreCase)
            ? HudMinimap.MagicIcon
            : HudMinimap.SupplyIcon;
    }
}
