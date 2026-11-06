using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Tests;

internal static class SkinTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("skins: a look fits its item family, prices stay in range, and the look saves and travels", RulesAsync);
        yield return ("skins: a 0.64 costume body reads back unscrambled", ScrambledBodyAsync);
    }

    // A 0.64 body keeps its vertices near the origin once its scrambled positions are read back.
    private static Task ScrambledBodyAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "char", "tmABCD", "rPT_Costume_1_Male.smd");
        var mislabelled = path.Replace("rPT_Costume_1_Male", "tfbE39", StringComparison.Ordinal);
        if (File.Exists(mislabelled))
        {
            var body = new PristonTale.Engine3D.Formats.PristonSmdReader().ReadModel(mislabelled);
            Assert.True(body.Objects.SelectMany(static obj => obj.Vertices).All(static vertex => vertex.Position.Length() < 400f), "a 0.64 body signed 0.66 was misread");
        }

        if (File.Exists(path))
        {
            var model = new PristonTale.Engine3D.Formats.PristonSmdReader().ReadModel(path);
            var skinned = model.Objects.Where(static obj => obj.Vertices.Count > 0).SelectMany(static obj => obj.Vertices).ToArray();
            Assert.True(skinned.Length > 0 && skinned.All(static vertex => vertex.Position.Length() < 400f), "a 0.64 body kept scrambled vertices");
        }

        return Task.CompletedTask;
    }

    private static Task RulesAsync()
    {
        Assert.True(SkinRules.Look("DS101", "DS120") == "DS120", "a skin of the family did not show");
        Assert.True(SkinRules.Look("WA101", "WS120") == "WA101", "a skin of another family replaced the look");
        Assert.True(SkinRules.Look("", "WS120") == "" && SkinRules.Look("WA101", "") == "WA101", "an empty slot took a look");
        Assert.True(SkinRules.Fits("DA101", "DA120") && SkinRules.Fits("DA101", "DA301") && !SkinRules.Fits("DA101", "DA220"), "armor took a robe");
        Assert.True(!SkinRules.Wearable("DA220", (uint)JobBits.Of(PlayerClass.Fighter)) && SkinRules.Wearable("DA120", (uint)JobBits.Of(PlayerClass.Fighter)),
            "a class was offered what it cannot wear");
        Assert.True(SkinRules.Fits("WS201", "WS202") && !SkinRules.Fits("WS201", "WS101"), "a bow was offered over a sword");
        Assert.True(SkinRules.HairOf("BI117") == 1 && SkinRules.HairOf("WA101") is null, "a hair tint look");
        Assert.True(SkinRules.HairOf("KH999") is null, "an unknown head sold as hair");
        Assert.True(SkinRules.Fits("WA105", "CW131") && SkinRules.Fits("WA101", "CW131") && !SkinRules.Fits("WS201", "CW131"), "a CW skin fits its kind on either grip");
        var low = new ItemDefinition(1, "Low", ItemKind.Shield, EquipmentSlot.OffHand) { CatalogKey = "DS101" };
        var high = low with { RequiredLevel = 500 };
        Assert.True(SkinRules.Price(low) == SkinRules.MinPrice && SkinRules.Price(high) == SkinRules.MaxPrice, "skin prices left their range");
        Assert.True(SkinRules.SlotOf(low) == SkinSlot.OffHand && SkinRules.SlotOf(low with { EquipSlot = null }) is null, "skin slots");
        var skins = new SkinSet("WA120", "", "DS101");
        Assert.True(SkinCodec.Restore(SkinCodec.Capture(skins)) == skins && SkinCodec.Capture(SkinSet.None) == "", "the skin codec");
        Assert.True(SkinCatalogPacket.MaxSize <= PristonTale.Protocol.Binary.FrameCodec.MaxFrameSize, "a catalog part does not fit one frame");
        var buffer = new byte[SkinCatalogPacket.MaxSize];
        var packet = new SkinCatalogPacket(Guid.NewGuid(), 9, true, "WA120", "", "DS101", "BI116", [new SkinArticle("WA120", "Axe", 0, 50, 2800, true)]);
        var read = SkinCatalogPacket.Read(buffer.AsSpan(0, packet.Write(buffer)));
        Assert.True(read.Balance == 9 && read.Last && read.OffHand == "DS101" && read.Hair == "BI116" && read.Articles.Count == 1 && read.Articles[0] == packet.Articles[0], "the catalog packet");
        return Task.CompletedTask;
    }
}
