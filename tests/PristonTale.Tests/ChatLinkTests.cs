using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Tests;

// The chat taken from RuneTale's: the raid tab and the "$" and "#" shortcuts, emote words, macros, a dragged window, and an
// item linked into a line, checked on the client and carried by the server to the hearers.
internal static class ChatLinkTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("chat: the raid tab and the $ and # shortcuts reach their channels, raid lines land in the raid tab, emote words go as codes and show over the speaker instead of a bubble", ChannelsAsync);
        yield return ("chat: Ctrl+F keys type their macro, Tab starts a clan line, and the window drags by its log within the screen", KeysAndDragAsync);
        yield return ("chat: Ctrl+A selects the line, Ctrl+C copies it, Ctrl+X cuts it and Ctrl+V pastes in place of a selection", ClipboardAsync);
        yield return ("chat: a linked item's name carries its rarity before it and its aging after it", LinkNameAsync);
        yield return ("chat: a linked line colours only the bracketed item name, not the speaker", LinkColourAsync);
        yield return ("chat: a linked item goes with the line while its name stays, and the server hands hearers the item it holds", LinkAsync);
    }

    private static Task ChannelsAsync()
    {
        HudChatSend Send(string text, HudChatTab tab = HudChatTab.All)
        {
            var input = new HudChatInput();
            input.Set(text);
            return input.Submit("Ann", tab, 0, out _)!.Value;
        }

        Assert.True(Send("hello", HudChatTab.Raid) is { Kind: HudChatSendKind.Command, Text: "/raid hello" }, "the raid tab did not send to the raid");
        Assert.True(Send("$cheap axes").Text == "/TRADE> cheap axes" && Send("#pull now").Text == "/raid pull now", "a shortcut did not reach its channel");
        Assert.True(Send("/love") is { Kind: HudChatSendKind.Spoken, Text: "e2" }, "an emote word did not go as its code");
        Assert.True(HudEmotes.Of("Ann: e2") == HudEmotes.Art[1] && HudEmotes.Of("Ann: e21") is null && HudEmotes.Of("Ann: hello") is null, "an emote line is misread");

        var bubbles = new HudChatBubbles();
        bubbles.Say(7, "Ann: e2", 0, 5000);
        bubbles.Say(8, "Bob: hello", 0, 5000);
        Assert.True(!bubbles.TryGet(7, out _) && bubbles.TryGet(8, out _), "an emote line showed a bubble instead of the emote");

        var log = new HudChatLog();
        log.Add("[Raid] Bob> pull", ChatKind.Party);
        log.Add("Bob: hi", ChatKind.Party);
        Assert.True(log.Count(HudChatTab.Raid) == 1 && log.Count(HudChatTab.Party) == 1 && log.Count(HudChatTab.All) == 2, "raid lines are not filed in the raid tab");
        return Task.CompletedTask;
    }

    private static Task KeysAndDragAsync()
    {
        var chat = new HudChat { Macros = ["/stall", "", "Selling axes"] };
        var none = new KeyboardState();
        chat.Update(new KeyboardState(Keys.LeftControl, Keys.F3), new KeyboardState(Keys.LeftControl), [], 0, HudChatTab.All);
        Assert.True(chat.Window.Focused && chat.Window.Input.Text == "Selling axes", "Ctrl+F3 did not type its macro");
        chat.Update(new KeyboardState(Keys.Tab), none, [], 0, HudChatTab.All);
        Assert.True(chat.Window.Input.Text == "/CLAN> ", "Tab did not start a clan line");

        var layout = HudLayout.Create(800, 600);
        var start = HudChatWindow.Box(layout, false).Center;
        var released = new MouseState();
        MouseState Down(Point at) => new(at.X, at.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        chat.Window.Drag(layout, Down(start), released, start, false);
        var moved = new Point(start.X + 100, start.Y - 50);
        chat.Window.Drag(layout, Down(moved), Down(start), moved, false);
        Assert.True(chat.Window.Offset == new Point(100, -50), $"the window did not follow the drag: {chat.Window.Offset}");
        var far = new Point(start.X + 5000, start.Y);
        chat.Window.Drag(layout, Down(far), Down(moved), far, false);
        Assert.True(HudChatWindow.Box(layout, false).Right + chat.Window.Offset.X <= layout.Width, "the window was dragged off the screen");
        return Task.CompletedTask;
    }

    private static Task ClipboardAsync()
    {
        var clipboard = "";
        var chat = new HudChat { ReadClipboard = () => clipboard, WriteClipboard = text => clipboard = text };
        var none = new KeyboardState();
        var control = new KeyboardState(Keys.LeftControl);
        chat.Window.OpenInput();
        chat.Update(none, none, "hello".ToCharArray(), 0, HudChatTab.All);
        chat.Update(new KeyboardState(Keys.LeftControl, Keys.A), control, [], 0, HudChatTab.All);
        chat.Update(new KeyboardState(Keys.LeftControl, Keys.C), control, [], 0, HudChatTab.All);
        Assert.True(chat.Window.Input.AllSelected && clipboard == "hello", "Ctrl+A then Ctrl+C did not copy the line");

        chat.Update(none, none, "w".ToCharArray(), 0, HudChatTab.All);
        Assert.True(chat.Window.Input.Text == "w" && !chat.Window.Input.AllSelected, "typing over a selection did not replace it");

        clipboard = "line one\r\nline\ttwo\u0001";
        chat.Update(new KeyboardState(Keys.LeftControl, Keys.V), control, [], 0, HudChatTab.All);
        Assert.True(chat.Window.Input.Text == "wline one  line two", $"the paste was not cleaned: '{chat.Window.Input.Text}'");

        chat.Update(new KeyboardState(Keys.LeftControl, Keys.X), control, [], 0, HudChatTab.All);
        Assert.True(clipboard == "wline one  line two" && chat.Window.Input.Text.Length == 0, "Ctrl+X did not cut the line");

        clipboard = new string('a', Simulation.Social.ChatRules.InputLimit + 50);
        chat.Update(new KeyboardState(Keys.LeftControl, Keys.V), control, [], 0, HudChatTab.All);
        Assert.True(chat.Window.Input.Text.Length == Simulation.Social.ChatRules.InputLimit, "a paste went past the line's limit");
        chat.Update(new KeyboardState(Keys.LeftControl, Keys.A), control, [], 0, HudChatTab.All);
        chat.Update(none, none, ['\b'], 0, HudChatTab.All);
        Assert.True(chat.Window.Input.Text.Length == 0, "Backspace did not erase the selected line");
        return Task.CompletedTask;
    }

    private static Task LinkNameAsync()
    {
        var owner = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Ann", 100);
        string Named(int level, ItemRarity rarity)
        {
            var item = new ItemInstance(ChatFixture.Axe) { UpgradeLevel = level, Rarity = rarity };
            return HudChat.LinkName(OwnedItemView.Of(item, 0, -1) with { Details = ItemDetailView.Of(item, owner) });
        }

        Assert.True(Named(20, ItemRarity.Legendary) == "Legendary Stone Axe +20", $"a legendary +20 link reads '{Named(20, ItemRarity.Legendary)}'");
        Assert.True(Named(0, ItemRarity.Rare) == "Rare Stone Axe" && Named(5, ItemRarity.Common) == "Stone Axe +5" && Named(0, ItemRarity.None) == "Stone Axe",
            "a link's grade or aging is misplaced");
        var plain = new ItemInstance(ChatFixture.Axe) { UpgradeLevel = 3, Rarity = ItemRarity.Epic };
        Assert.True(HudChat.LinkName(OwnedItemView.Of(plain, 0, -1)) == "+3 Stone Axe", "an item without details changed its name");
        return Task.CompletedTask;
    }

    private static Task LinkColourAsync()
    {
        Assert.True(HudChatWindow.SplitLink("TestKnight: [+20 Draxos Sword]") == ("TestKnight: ", "[+20 Draxos Sword]", ""), "the speaker was coloured as the link");
        Assert.True(HudChatWindow.SplitLink("Ann: look [Axe] here") == ("Ann: look ", "[Axe]", " here"), "the text after the link was coloured");
        Assert.True(HudChatWindow.SplitLink("Ann: a long [Dark Horse") == ("Ann: a long ", "[Dark Horse", "")
            && HudChatWindow.SplitLink("Mount] ok") == ("", "Mount]", " ok"), "a link wrapped over two lines lost its colour");
        return Task.CompletedTask;
    }

    private static async Task LinkAsync()
    {
        var input = new HudChatInput();
        var itemId = Guid.NewGuid();
        input.Set("look");
        input.Link(itemId, "Stone Axe");
        var sent = input.Submit("Ann", HudChatTab.All, 0, out _)!.Value;
        Assert.True(sent.ItemId == itemId && sent.Text == "look [Stone Axe]", $"the link did not go with the line: {sent}");
        input.Link(itemId, "Stone Axe");
        input.Set("no link");
        Assert.True(input.Submit("Ann", HudChatTab.All, 0, out _)!.Value.ItemId == Guid.Empty, "a link whose name was erased still went");

        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 100, 100);
        var bob = await fixture.JoinAsync("Bob", 120, 100);
        var axe = new ItemInstance(ChatFixture.Axe);
        Assert.True(ann.Session.Character!.Inventory.TryPlaceFirstFree(axe), "fixture axe");
        await fixture.RunAsync(fixture.Chat.SayAsync(ann.Session.EntityId, "look [Stone Axe]", axe.InstanceId));
        Assert.True(bob.Link.Lines.LastOrDefault().Item is { InstanceId: var linked, Details: not null } && linked == axe.InstanceId, "the hearer did not get the linked axe");
        await fixture.RunAsync(fixture.Chat.SayAsync(ann.Session.EntityId, "not mine", Guid.NewGuid()));
        Assert.True(bob.Link.Lines.LastOrDefault() is { Item: null, Text: "Ann: not mine" }, "an item the speaker does not hold was linked");
    }
}
