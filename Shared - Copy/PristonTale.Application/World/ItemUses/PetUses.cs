using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World.ItemUses;

// A pet skin's item unlocks its skin for the account and sends that pet out; a skin already unlocked refuses, so the
// item is kept. The candidate carries both into the stored snapshot, and the live character takes them at commit.
internal sealed class PetSkinUse : ItemUseAction
{
    public override bool ReappliesAtCommit => false;

    public override ItemUseStatus Check(in ItemUseRequest use) =>
        PetSkins.Get(use.Rule.Argument) is null || PetSkins.Has(use.Character.PetSkins, use.Rule.Argument)
            ? ItemUseStatus.NothingToChange
            : ItemUseStatus.Used;

    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate) =>
        candidate.UnlockPetSkin(use.Rule.Argument) && candidate.SendPetOut(use.Rule.Argument)
            ? ItemUseExtra.None
            : new ItemUseExtra(ItemUseStatus.NothingToChange);

    public override string Message(ItemDefinition item) => $"{item.Name} joined your pet collection";

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        var character = use.Player.Character;
        character.UnlockPetSkin(use.Rule.Argument);
        character.SendPetOut(use.Rule.Argument);
        return new ItemUseEffect(ItemEffectKind.Learn);
    }
}
