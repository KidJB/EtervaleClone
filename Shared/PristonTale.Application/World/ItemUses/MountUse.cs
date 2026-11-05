using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World.ItemUses;

// A mount's item adds the mount to the account's stable and picks it; a mount already owned refuses, so the item is kept.
internal sealed class MountUse : ItemUseAction
{
    public override bool ReappliesAtCommit => false;

    public override ItemUseStatus Check(in ItemUseRequest use) =>
        Mounts.Get(use.Rule.Argument) is null || Mounts.Has(use.Character.Mounts, use.Rule.Argument)
            ? ItemUseStatus.NothingToChange
            : ItemUseStatus.Used;

    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate) =>
        candidate.UnlockMount(use.Rule.Argument) ? ItemUseExtra.None : new ItemUseExtra(ItemUseStatus.NothingToChange);

    public override string Message(ItemDefinition item) => $"{item.Name} joined your stable";

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.Character.UnlockMount(use.Rule.Argument);
        return new ItemUseEffect(ItemEffectKind.Learn);
    }
}
