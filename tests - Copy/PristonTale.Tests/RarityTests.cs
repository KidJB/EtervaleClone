using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items;

namespace PristonTale.Tests;

// Item rarity: which gear takes a grade, the grade roll and its boss odds, the bonus steps a grade adds, and the grade on
// the wire.
internal static class RarityTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("rarity: gear takes a grade, quest and unique items and potions do not", EligibilityAsync);
        yield return ("rarity: the roll follows the chance table and a boss kill favours Rare and above", RollAsync);
        yield return ("rarity: a million rolls land on the chance table, and a boss kill triples Rare and above", OddsAsync);
        yield return ("rarity: each grade above Common adds the kind's step and raises the price", ApplyAsync);
        yield return ("rarity: the grade travels in the item details and on ground drops", WireAsync);
    }

    private static Task EligibilityAsync()
    {
        Assert.True(ItemRarities.CanHave(ChatFixture.Axe), "a weapon cannot take a grade");
        Assert.True(!ItemRarities.CanHave(ChatFixture.Potion), "a potion took a grade");
        Assert.True(!ItemRarities.CanHave(ChatFixture.Axe with { UniqueItem = ItemDefinition.QuestUnique }), "a quest weapon took a grade");
        Assert.True(!ItemRarities.CanHave(ChatFixture.Axe with { UniqueVariant = 1 }), "a named unique took a grade");
        return Task.CompletedTask;
    }

    private static Task RollAsync()
    {
        // Draws in millionths: Common is the first 700,000, then Uncommon 200,000, Rare 70,000, Epic 25,000, Legendary 5,000.
        Assert.True(ItemRarities.Roll(new ScriptedRng(0), false) == ItemRarity.Common, "the lowest draw is not Common");
        Assert.True(ItemRarities.Roll(new ScriptedRng(700_000), false) == ItemRarity.Uncommon, "the draw after Common is not Uncommon");
        Assert.True(ItemRarities.Roll(new ScriptedRng(900_000), false) == ItemRarity.Rare, "the draw after Uncommon is not Rare");
        Assert.True(ItemRarities.Roll(new ScriptedRng(999_999), false) == ItemRarity.Legendary, "the top draw is not Legendary");

        // A boss triples Rare and above (300,000 together), which comes out of Common: 500,000 Common, then 200,000 Uncommon.
        Assert.True(ItemRarities.Roll(new ScriptedRng(700_000), true) == ItemRarity.Rare, "a boss kill does not favour Rare");
        Assert.True(ItemRarities.Roll(new ScriptedRng(500_000), true) == ItemRarity.Uncommon
            && ItemRarities.Roll(new ScriptedRng(500_000), false) == ItemRarity.Common, "an ordinary kill rolled like a boss");
        return Task.CompletedTask;
    }

    private static Task OddsAsync()
    {
        const int rolls = 1_000_000;
        foreach (var boss in new[] { false, true })
        {
            var counts = new int[(int)ItemRarities.Highest + 1];
            var rng = new Simulation.Combat.CombatRng(1234);
            for (var roll = 0; roll < rolls; roll++)
            {
                counts[(int)ItemRarities.Roll(rng, boss)]++;
            }

            double[] expected = boss ? [0, 0.50, 0.20, 0.21, 0.075, 0.015] : [0, 0.70, 0.20, 0.07, 0.025, 0.005];
            for (var grade = (int)ItemRarity.Common; grade <= (int)ItemRarities.Highest; grade++)
            {
                var share = counts[grade] / (double)rolls;
                Assert.True(Math.Abs(share - expected[grade]) < 0.003,
                    $"{(ItemRarity)grade} came {share:P2} of {(boss ? "boss" : "ordinary")} rolls, not {expected[grade]:P2}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task ApplyAsync()
    {
        var common = new ItemInstance(ChatFixture.Axe);
        var before = common.Roll;
        ItemRarities.Apply(common, ItemRarity.Common);
        Assert.True(common.Rarity == ItemRarity.Common && common.Roll == before, "Common changed the copy's values");

        var legendary = new ItemInstance(ChatFixture.Axe);
        ItemRarities.Apply(legendary, ItemRarity.Legendary);
        var roll = legendary.Roll;
        Assert.True(legendary.Rarity == ItemRarity.Legendary && roll.DamageMin == before.DamageMin + 12 && roll.DamageMax == before.DamageMax + 12
            && roll.AttackRating == before.AttackRating + 40 && roll.CriticalBonus == before.CriticalBonus + 4 && roll.Price == before.Price * 3,
            "a Legendary weapon did not gain four weapon steps and three times the price");
        Assert.True(legendary.Copy(1).Rarity == ItemRarity.Legendary, "a copy lost its grade");

        var potion = new ItemInstance(ChatFixture.Potion);
        ItemRarities.Apply(potion, ItemRarity.Epic);
        Assert.True(potion.Rarity == ItemRarity.None, "a potion took a grade");
        Assert.True(Application.GameMasters.GmItemCommands.RarityNamed("EPIC") == ItemRarity.Epic
            && Application.GameMasters.GmItemCommands.RarityNamed("shiny") is null, "the GM grade names are not read");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[ItemDetailEntry.Size + 64];
        var writer = new PacketWriter(buffer, 0);
        new ItemDetailEntry { InstanceId = Guid.NewGuid(), Rarity = (int)ItemRarity.Epic }.Write(ref writer);
        var reader = new PacketReader(buffer.AsSpan(0, writer.Finish()), 0);
        Assert.True(ItemDetailEntry.Read(ref reader).Rarity == (int)ItemRarity.Epic, "the item details lost the grade");

        writer = new PacketWriter(buffer, 0);
        new GroundItemEntry(Guid.NewGuid(), 1, false, 1, 0, 0, 0, "Axe") { Rarity = (byte)ItemRarity.Rare }.Write(ref writer);
        reader = new PacketReader(buffer.AsSpan(0, writer.Finish()), 0);
        Assert.True(GroundItemEntry.Read(ref reader).Rarity == (byte)ItemRarity.Rare, "the ground drop lost the grade");
        return Task.CompletedTask;
    }
}
