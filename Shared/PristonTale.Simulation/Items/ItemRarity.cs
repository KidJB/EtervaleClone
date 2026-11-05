using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Items;

// The grade a dropped piece of gear rolls. The values are persisted with the item and sent on the wire, so they never
// change; None is every item made before rarity existed and every item that cannot take one.
public enum ItemRarity : byte
{
    None = 0,
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    Epic = 4,
    Legendary = 5
}

// What one rarity step adds to a copy of a kind of item in a level band; a copy gets (rarity - Common) steps. Negative
// requirement steps lower that requirement (never below zero).
public readonly record struct RarityStep(int Damage, int AttackRating, int Critical, int Defence, float Absorb, float Block, int AddLife, int AddMana,
    int AddStamina, int Strength, int Spirit, int Agility);

public static class ItemRarities
{
    public const ItemRarity Highest = ItemRarity.Legendary;

    // The chance of each grade in millionths (Common takes the rest), and how much a boss kill multiplies Rare and above.
    private const int ChanceScale = 1_000_000;
    private static readonly int[] Chances = [0, 0, 200_000, 70_000, 25_000, 5_000];
    private const int BossFactor = 3;

    // Copies of these levels and above take the higher band's steps.
    private const int HighBandLevel = 103;
    private const int TopBandLevel = 113;

    // Each grade above Common adds half the price again.
    private const int PricePercentPerStep = 50;

    public static readonly string[] Names = ["", "Common", "Uncommon", "Rare", "Epic", "Legendary"];

    // The grades' colours (red, green, blue): grey-white, green, blue, purple, orange.
    public static readonly (byte R, byte G, byte B)[] Colors = [(255, 255, 255), (235, 235, 235), (120, 240, 120), (110, 160, 255), (200, 120, 255), (255, 150, 60)];

    private static readonly RarityStep WeaponLow = new(3, 10, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static readonly RarityStep WeaponHigh = new(4, 13, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static readonly RarityStep WeaponTop = new(5, 20, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static readonly Dictionary<ItemKind, (RarityStep Low, RarityStep High)> Gear = new()
    {
        [ItemKind.Armor] = (new(0, 0, 0, 15, 1.4f, 0, 0, 0, 0, -3, 0, 0), new(0, 0, 0, 25, 4f, 0, 0, 0, 0, -3, 0, 0)),
        [ItemKind.Boots] = (new(0, 0, 0, 7, 0.7f, 0, 0, 0, 0, 0, 0, -3), new(0, 0, 0, 20, 1.6f, 0, 0, 0, 0, 0, 0, -3)),
        [ItemKind.Gloves] = (new(0, 0, 0, 7, 0.7f, 0, 0, 0, 0, 0, 0, 0), new(0, 0, 0, 20, 1.6f, 0, 0, 0, 0, 0, 0, 0)),
        [ItemKind.Bracelet] = (new(0, 5, 0, 6, 0, 0, 0, 0, 0, 0, 0, 0), new(0, 12, 0, 14, 0, 0, 0, 0, 0, 0, 0, 0)),
        [ItemKind.Vambrace] = (new(0, 5, 0, 6, 0, 0, 0, 0, 0, 0, 0, 0), new(0, 12, 0, 14, 0, 0, 0, 0, 0, 0, 0, 0)),
        [ItemKind.Amulet] = (new(0, 0, 0, 0, 0, 0, 2, 2, 0, 0, -3, 0), new(0, 0, 0, 0, 0, 0, 3, 4, 0, 0, -3, 0)),
        [ItemKind.Ring] = (new(0, 0, 0, 0, 0, 0, 1, 0, 2, 0, -3, 0), new(0, 0, 0, 0, 0, 0, 3, 0, 4, 0, -3, 0)),
        [ItemKind.Shield] = (new(0, 0, 0, 7, 0.5f, 0.3f, 0, 0, 0, -3, 0, 0), new(0, 0, 0, 13, 0.7f, 0.3f, 0, 0, 0, -3, 0, 0)),
        [ItemKind.Orb] = (new(0, 0, 0, 7, 0.5f, 0.3f, 0, 0, 0, 0, 0, 0), new(0, 0, 0, 13, 0.7f, 0.3f, 0, 0, 0, 0, 0, 0))
    };

    public static string NameOf(ItemRarity rarity) => Names[(int)rarity <= (int)Highest ? (int)rarity : 0];

    // Gear without a quest or unique marker: weapons and the worn kinds of the table.
    public static bool CanHave(ItemDefinition definition) =>
        definition is { UniqueItem: 0, UniqueVariant: 0, Template: not null } && (definition.IsWeapon || Gear.ContainsKey(definition.Kind));

    public static ItemRarity Roll(ISimRng rng, bool bossKill)
    {
        Span<int> weights = stackalloc int[Chances.Length];
        var rest = ChanceScale;
        for (var grade = (int)ItemRarity.Uncommon; grade <= (int)Highest; grade++)
        {
            weights[grade] = Chances[grade] * (bossKill && grade >= (int)ItemRarity.Rare ? BossFactor : 1);
            rest -= weights[grade];
        }

        weights[(int)ItemRarity.Common] = Math.Max(0, rest);
        var total = 0;
        foreach (var weight in weights)
        {
            total += weight;
        }

        var draw = rng.NextExclusive(0, total);
        for (var grade = (int)ItemRarity.Common; grade <= (int)Highest; grade++)
        {
            draw -= weights[grade];
            if (draw < 0)
            {
                return (ItemRarity)grade;
            }
        }

        return ItemRarity.Common;
    }

    // A dropped copy of gear that can take a grade rolls one and gains its steps.
    public static void RollOnto(ItemInstance item, ISimRng rng, bool bossKill)
    {
        if (CanHave(item.Definition))
        {
            Apply(item, Roll(rng, bossKill));
        }
    }

    // Sets the copy's grade and adds the grade's steps to its rolled values and price.
    public static void Apply(ItemInstance item, ItemRarity rarity)
    {
        if (rarity == ItemRarity.None || !CanHave(item.Definition))
        {
            return;
        }

        item.Rarity = rarity;
        var steps = (int)rarity - (int)ItemRarity.Common;
        if (steps <= 0)
        {
            return;
        }

        var step = StepOf(item.Definition);
        var roll = item.Roll;
        var requirements = roll.Requirements ?? CharacterAttributes.Empty;
        item.Roll = roll with
        {
            DamageMin = roll.DamageMin + step.Damage * steps,
            DamageMax = roll.DamageMax + step.Damage * steps,
            AttackRating = roll.AttackRating + step.AttackRating * steps,
            Defence = roll.Defence + step.Defence * steps,
            Absorb = roll.Absorb + step.Absorb * steps,
            Block = roll.Block + step.Block * steps,
            AddLife = roll.AddLife + step.AddLife * steps,
            AddMana = roll.AddMana + step.AddMana * steps,
            AddStamina = roll.AddStamina + step.AddStamina * steps,
            CriticalBonus = roll.CriticalBonus + step.Critical * steps,
            Requirements = new CharacterAttributes(
                Lower(requirements.Strength, step.Strength * steps),
                Lower(requirements.Spirit, step.Spirit * steps),
                requirements.Talent,
                Lower(requirements.Agility, step.Agility * steps),
                requirements.Health),
            Price = roll.Price + roll.Price * PricePercentPerStep * steps / 100
        };
    }

    private static int Lower(int requirement, int change) => requirement == 0 ? 0 : Math.Max(0, requirement + change);

    private static RarityStep StepOf(ItemDefinition definition)
    {
        var level = definition.RequiredLevel;
        if (definition.IsWeapon)
        {
            return level >= TopBandLevel ? WeaponTop : level >= HighBandLevel ? WeaponHigh : WeaponLow;
        }

        var (low, high) = Gear[definition.Kind];
        return level >= HighBandLevel ? high : low;
    }
}
