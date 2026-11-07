using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World.ItemUses;

// A world buff's item starts the buff for every player on the next pass of the world (WorldServer.UpdateWorldBuffs), adding
// its time to what is left of a buff already running.
internal sealed class WorldBuffUse : ItemUseAction
{
    public override bool ReappliesAtCommit => false;

    public override ItemUseStatus Check(in ItemUseRequest use) => ItemUseStatus.Used;

    public override GameText Message(ItemDefinition item) => GameText.Plain(item.Name + " activated");

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.PendingWorldBuffs.Add(use.Rule.Argument);
        return new ItemUseEffect(ItemEffectKind.Learn);
    }
}

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

    public override GameText Message(ItemDefinition item) => GameText.Of(GameTextId.MountJoined, item.Name);

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.Character.UnlockMount(use.Rule.Argument);
        return new ItemUseEffect(ItemEffectKind.Learn);
    }
}
