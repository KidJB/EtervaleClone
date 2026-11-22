using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World.ItemUses;

// A caravan crystal: calls its caravan for its hours, or adds them to the same caravan out; refused while a caravan of
// another kind is out, so the crystal is kept.
internal sealed class CaravanUse : ItemUseAction
{
    public override bool ReappliesAtCommit => false;

    public override ItemUseStatus Check(in ItemUseRequest use) =>
        Caravans.Call(use.Character.Caravan, use.Rule.Argument, use.Context.NowSeconds) is null ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;

    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate)
    {
        if (Caravans.Call(candidate.Caravan, use.Rule.Argument, use.Context.NowSeconds) is not { } called)
        {
            return new ItemUseExtra(ItemUseStatus.NothingToChange);
        }

        candidate.Caravan = called;
        return ItemUseExtra.None;
    }

    public override GameText Message(ItemDefinition item) => GameText.Of(GameTextId.CaravanJoined, item.Name);

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.Character.Caravan = use.Operation.Candidate?.Caravan ?? use.Player.Character.Caravan;
        return new ItemUseEffect(ItemEffectKind.Learn);
    }
}
