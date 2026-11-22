using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

// The Morion caster of blood and shadow who reads from a grimoire; built on the Assassin's skeleton, with her own models in
// char/vampira.
public sealed class RogueClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Rogue", "Shadow Rogue", "Blood Rogue", "Night Reaper", "Vampire Queen"],
        new MotionFallback(3200, 21, 3040),
        static weapon => weapon.Definition.Kind == ItemKind.Grimoire,
        CastingWeapon: ItemKind.Grimoire,
        Shift: new RequirementShift(new(-25, -20), new(15, 25), new(-15, -10), new(-20, -15)),
        QuestWeapon: "WX109",
        BeginnerWeapon: "WX101",
        QuestWeaponHunt.Field,
        QuestItems.EmperorRing,
        CharacterBrood.Morion,
        Female: true);

    public RogueClass()
        : base(BuildDefinition(
            PlayerClass.Rogue,
            "Rogue",
            Profile.Brood,
            bodyInx: "char\\vampira\\body.inx",
            headInx: "char\\vampira\\h1t1.inx",
            armorTableIndex: 11,
            new CharacterAttributes(15, 27, 20, 15, 22),
            new CharacterFunctionProfile(200, 2, 1, 2, 3, 2, 1)))
    {
    }
}
