using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;

namespace PristonTale.Tests;

internal static class RogueTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("rogue: a female Morion caster with her own body, heads, specialisation bit and motion bit", ClassAsync);
        yield return ("rogue: grimoires are her weapon alone, robes her armour, and her skills read a grimoire", GrimoireRulesAsync);
        yield return ("rogue: each phantom with a model gets a grimoire twin in definitions, shops and drop lines", MirrorAsync);
    }

    private static Task ClassAsync()
    {
        var rogue = new CharacterClassRegistry().Get(PlayerClass.Rogue);
        var profile = ClassProfiles.Of(PlayerClass.Rogue);
        Assert.True(rogue.Definition.Brood == CharacterBrood.Morion && profile.Female && profile.CastingWeapon == ItemKind.Grimoire
            && profile.BeginnerWeapon == "WX101" && StarterGifts.WeaponKey(PlayerClass.Rogue) == "WX101", "the Rogue's profile");
        Assert.True(JobBits.Of(PlayerClass.Rogue) == JobBitCode.Rogue && JobBits.ClassOf(JobBitCode.Rogue) == PlayerClass.Rogue
            && PlayerClasses.JobMask(PlayerClass.Rogue) == 0x800, "the Rogue's bits");
        Assert.True(CharacterHead.Resolve(PlayerClass.Rogue, 0, 0) == "char/vampira/h1t1.inx" && CharacterHead.Resolve(PlayerClass.Rogue, 3, 12) == "char/vampira/h13t4.inx"
            && CharacterHead.Resolve(PlayerClass.Rogue, 4, 2) == "char/vampira/h3t4.inx", "the Rogue's heads");
        Assert.True(CharacterHead.BigHead(PlayerClass.Rogue, 1, 0) == CharacterHead.BigHead(PlayerClass.Assassin, 1, 0)
            && CharacterHead.SoccerHead(PlayerClass.Rogue) == CharacterHead.SoccerHead(PlayerClass.Assassin), "the Rogue borrows the Assassin's potion heads");
        return Task.CompletedTask;
    }

    private static Task GrimoireRulesAsync()
    {
        Assert.True(ItemEquipmentMetadata.TryGet("WX101", out var kind, out var slot) && kind == ItemKind.Grimoire && slot == EquipmentSlot.Weapon,
            "a grimoire is not a weapon");
        Assert.True(ItemClassRestrictions.AllowedJobs("WX101") == (uint)JobBitCode.Rogue, "another class may hold a grimoire");
        Assert.True((ItemClassRestrictions.AllowedJobs("WA101") & (uint)JobBitCode.Rogue) == 0
            && (ItemClassRestrictions.AllowedJobs("WN101") & (uint)JobBitCode.Rogue) == 0, "the Rogue may hold another weapon");
        Assert.True(ItemClassRestrictions.WearsRobe(PlayerClass.Rogue) && (ItemClassRestrictions.AllowedJobs("DA201") & (uint)JobBitCode.Rogue) != 0,
            "the Rogue does not wear robes");
        var skills = ClassSkillCatalog.For(PlayerClass.Rogue);
        Assert.True(skills.Count == 20 && skills.All(static skill => skill.Code >> 24 == 0x60), "the Rogue's skills");
        Assert.True(skills.Where(static skill => skill.Category != SkillCategory.Passive)
            .All(static skill => SkillRuleCatalog.TryGet(skill.Code, out _) && skill.AllowsWeapon(ItemKind.Grimoire) && !skill.AllowsWeapon(ItemKind.Phantom)),
            "a Rogue skill does not need a grimoire");
        return Task.CompletedTask;
    }

    private static Task MirrorAsync()
    {
        Assert.True(Grimoires.TryMirror("WN109", out var key) && key == "WX109" && !Grimoires.TryMirror("WN128", out _)
            && !Grimoires.TryMirror("WN151", out _) && !Grimoires.TryMirror("WA101", out _), "the grimoire of a phantom");

        var phantom = ItemRecords.Blank("WN105") with { Name = "Okute Phantom", Kind = (int)ItemKind.Phantom, Price = 900 };
        var items = GrimoireMirror.Items([phantom, ItemRecords.Blank("WA101")]);
        var grimoire = items.Single(static record => record.Code == "WX105");
        Assert.True(items.Count == 3 && grimoire.Name == "Okute Grimoire" && grimoire.Kind == (int)ItemKind.Grimoire && grimoire.Price == 900,
            "the grimoire does not mirror its phantom");
        Assert.True(GrimoireMirror.Items([phantom, grimoire]).Count == 2, "an imported grimoire was mirrored again");
        Assert.True(WorldDataFactory.ToItemDefinition(grimoire).IsWeapon, "a mirrored grimoire is not a weapon");

        var npc = new NpcRecord(1, "smith", "Smith", "npc.inx", 1, false, 0, 0, 1f, 0, 0, 0, 0, 0)
        {
            ShopItems = [new(1, 0, 0, "WN101"), new(1, 0, 3, "WA101")]
        };
        var shop = GrimoireMirror.Shops([npc]).Single().ShopItems;
        Assert.True(shop.Count == 3 && shop[2] == new NpcShopItemRecord(1, 0, 4, "WX101"), "the shop does not sell the grimoire after its goods");

        var line = new MonsterDropLineRecord(7, 0, 50, 2, 0, 0) { Items = ["WN103", null, "WA101"] };
        Assert.True(GrimoireMirror.DropLine(line).Items.SequenceEqual(["WN103", null, "WA101", "WX103"]), "the drop line lacks the grimoire");
        var plain = line with { Items = ["WA101"] };
        Assert.True(ReferenceEquals(GrimoireMirror.DropLine(plain), plain), "a line without a phantom changed");
        return Task.CompletedTask;
    }
}
