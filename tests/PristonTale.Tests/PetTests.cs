using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class PetTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a pet sent out, put away or renamed raises the player's description revision, so others see it", RevisionAsync);
        yield return ("pet skins have unique ids, items and monsters, and their items unlock them", TableAsync);
        yield return ("a character unlocks a skin once, sends out only unlocked skins, joins its account's skins and republishes its pets at once", CharacterAsync);
        yield return ("the pet command sends a skin out or puts the pet away and refuses anything else", CommandAsync);
        yield return ("the pet collection and the pet skin in a player's looks travel on the wire; older looks read without a pet", WireAsync);
        yield return ("the pet window's rename box types a name of letters, digits and spaces and sends it as /petname on Enter", RenameAsync);
    }

    private static Task TableAsync()
    {
        var skins = PetSkins.All;
        Assert.True(skins.Count is > 0 and <= PetSkins.Max, "the skin table is empty or too large");
        Assert.True(skins.Select(skin => skin.Id).SequenceEqual(Enumerable.Range(1, skins.Count)), "skin ids are not 1 to the table's size");
        Assert.True(skins.Select(skin => skin.ItemCode).Distinct().Count() == skins.Count, "two skins share an item");
        Assert.True(skins.Select(skin => skin.MonsterCode).Distinct().Count() == skins.Count, "two skins share a monster");
        Assert.True(skins.All(skin => skin.Scale is > 0f and <= 1f && skin.Model.EndsWith(".inx", StringComparison.OrdinalIgnoreCase)),
            "a skin's model is not a converted model (.inx) or its scale is odd");
        if (Assets() is { } root)
        {
            foreach (var skin in skins)
            {
                Assert.True(File.Exists(PristonTale.Assets.AssetFiles.Combine(root, skin.Model)), $"{skin.Name}'s model {skin.Model} is not in the assets");
            }
        }
        foreach (var skin in skins)
        {
            Assert.True(UsableItemCatalog.TryGet(skin.ItemKey, out var rule) && rule.Action == UsableItemAction.PetSkin && rule.Argument == skin.Id,
                $"{skin.Name}'s item does not unlock it");
            Assert.True(PetSkins.ForMonster(skin.MonsterCode) == skin && PetSkins.IsItem(skin.ItemKey), $"{skin.Name} is not found by its monster or item");
        }

        var items = PetSkins.ItemDefinitions().ToArray();
        Assert.True(items.Length == skins.Count && items.All(item => item.Kind == ItemKind.Misc && ItemCodes.Parse(item.CatalogKey) == item.Code),
            "the skins' items are not one-cell items under their own keys");
        Assert.True(PetSkins.Get(0) is null && PetSkins.Get(skins.Count + 1) is null && PetSkins.Bit(0) == 0 && PetSkins.Bit(64) == 0, "out-of-range skins exist");
        return Task.CompletedTask;
    }

    private static string? Assets()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "assets");
            if (Directory.Exists(Path.Combine(candidate, "char")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Task CharacterAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Pet keeper");
        Assert.True(character.PetSkins == 0 && character.PetOut == 0, "a new character has pets");
        Assert.True(!character.SendPetOut(3), "a locked skin was sent out");
        Assert.True(character.UnlockPetSkin(3) && !character.UnlockPetSkin(3) && !character.UnlockPetSkin(0) && !character.UnlockPetSkin(99),
            "a skin unlocked twice, or an unknown one unlocked");
        Assert.True(character.SendPetOut(3) && character.PetOut == 3 && character.SendPetOut(0) && character.PetOut == 0, "the pet did not go out and back");
        var before = OwnerState.Capture(character);
        character.SendPetOut(3);
        var summoned = OwnerState.Capture(character, before);
        character.RenamePet("Fluffy");
        var renamed = OwnerState.Capture(character, summoned);
        Assert.True(!ReferenceEquals(before, summoned) && summoned.PetOut == 3 && !ReferenceEquals(summoned, renamed) && renamed.PetName == "Fluffy",
            "the owner state did not change with the pet out or its name, so the stable would wait for a field change");
        character.RenamePet("");
        character.SendPetOut(0);

        // The account's other characters' skins join; a saved pet out that the set lacks is not out.
        character.JoinAccountPetSkins(PetSkins.Bit(5));
        Assert.True(PetSkins.Has(character.PetSkins, 3) && PetSkins.Has(character.PetSkins, 5) && character.SendPetOut(5), "account skins did not join");
        var clone = character.CloneForInventoryOperation();
        Assert.True(clone.PetSkins == character.PetSkins && clone.PetOut == 5, "a copy for an item use lost the pets");
        var restored = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Restored");
        restored.RestorePets(PetSkins.Bit(1), 2);
        Assert.True(restored.PetOut == 0, "a pet out that was never unlocked came back");
        return Task.CompletedTask;
    }

    private static Task CommandAsync()
    {
        Assert.True(PetCommand.TryParse("/pet 4", out var skin) && skin == 4, "/pet 4 was not read");
        Assert.True(PetCommand.TryParse("/PET off", out skin) && skin == 0 && PetCommand.TryParse("/pet 0", out skin) && skin == 0, "/pet off was not read");
        Assert.True(PetCommand.Out(7) == "/pet 7" && PetCommand.Out(0) == "/pet off", "the window's commands are wrong");
        Assert.True(PetNames.Shown("", "TestKnight") == "TestKnight's Pet" && PetNames.Shown("Rex", "TestKnight") == "Rex",
            "an unnamed pet does not show its owner's name, or a named one not its own");
        foreach (var text in new[] { "/pet", "/pet -1", "/pet 64", "/pet two", "/pets 1", "/pet 1 2", "hello" })
        {
            Assert.True(!PetCommand.TryParse(text, out _), $"'{text}' was read as a pet command");
        }

        return Task.CompletedTask;
    }

    private static Task RenameAsync()
    {
        var window = new PristonTale.Gameplay.UI.Widgets.HudPetWindow();
        var layout = PristonTale.Gameplay.UI.HudLayout.Create(800, 600);
        var map = new Microsoft.Xna.Framework.Rectangle(656, 410, 128, 144);
        var pets = new PristonTale.Client.Net.Presence.PetCollectionInfo(PetSkins.Bit(1), 1);
        window.Toggle();
        var frame = PristonTale.Gameplay.UI.Widgets.HudPetWindow.Window(layout);
        var rename = PristonTale.Gameplay.UI.Widgets.HudPetWindow.Pencil(frame).Center;
        Assert.True(window.Click(layout, map, rename, true, pets) && window.Naming, "the pencil did not open the name box");

        static bool Never(Microsoft.Xna.Framework.Input.Keys key) => false;
        static bool Enter(Microsoft.Xna.Framework.Input.Keys key) => key == Microsoft.Xna.Framework.Input.Keys.Enter;
        window.Type("Fluffy! 2", 0.1, Never);
        Assert.True(window.TakeRequest() is null && window.Naming, "the name was sent before Enter");
        window.Type("", 0.1, Enter);
        Assert.True(window.TakeRequest() == "/petname Fluffy 2" && !window.Naming, "Enter did not send the typed name without the refused '!'");

        // An empty name clears it; without a pet out, the pencil does nothing.
        window.Click(layout, map, rename, true, pets);
        window.Type("", 0.1, Enter);
        Assert.True(window.TakeRequest() == "/petname", "an empty name did not clear the name");
        window.Click(layout, map, rename, true, pets with { Out = 0 });
        Assert.True(!window.Naming, "the pencil opened the box without a pet out");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[EntityDescriptionPacket.MaximumSize + 64];
        var collection = new PetCollectionPacket(PetSkins.Bit(1) | PetSkins.Bit(10), 10);
        Assert.True(PetCollectionPacket.Read(buffer.AsSpan(0, collection.Write(buffer))) == collection, "the pet collection did not survive the wire");

        var looks = new DescribedPlayer(PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, false, false, 0, 0) { ShopSign = "Pets", PetSkin = 6 };
        var packet = new EntityDescriptionPacket(9, 1, Contracts.World.EntityKind.Player, 10, 0, 1f, "Walker", "", looks);
        var length = packet.Write(buffer);
        Assert.True(EntityDescriptionPacket.Read(buffer.AsSpan(0, length)).Player is { PetSkin: 6, ShopSign: "Pets" }, "the pet skin did not survive the wire");

        // A version 2 description: the same bytes without the pet's, its header saying 2.
        var older = buffer.AsSpan(0, length - 1).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(older, older.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(older.AsSpan(8), 2);
        Assert.True(EntityDescriptionPacket.Read(older).Player is { PetSkin: 0, ShopSign: "Pets" }, "a version 2 description did not read without a pet");
        return Task.CompletedTask;
    }

    private static Task RevisionAsync()
    {
        var descriptors = new PristonTale.Application.World.FieldDescriptors();
        var looks = new PristonTale.Application.World.PlayerAppearanceView("Ann", PlayerClass.Fighter, 0, 0, 0, "", "");
        static PristonTale.Application.World.EntityView View(PristonTale.Application.World.PlayerAppearanceView appearance) =>
            new(1, PristonTale.Contracts.World.EntityKind.Player, false, default, 0f, default, 1, 100, 100, appearance);

        var first = descriptors.RevisionOf(View(looks));
        var summoned = descriptors.RevisionOf(View(looks with { PetSkin = 1 }));
        var renamed = descriptors.RevisionOf(View(looks with { PetSkin = 1, PetName = "Rex" }));
        var away = descriptors.RevisionOf(View(looks));
        Assert.True(summoned != first && renamed != summoned && away != renamed, "a pet change did not raise the description revision");
        return Task.CompletedTask;
    }
}
