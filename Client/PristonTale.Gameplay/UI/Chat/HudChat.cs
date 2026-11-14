using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Social;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Chat;

// The chat on the client: the window, the input line, the speech bubbles and the lines the server sends. A line over a
// character in view becomes a bubble (and, from a player, a log line in the player colour); other lines go to the log in
// their own colour, and the first whisper or notice while the window is closed opens it with the whisper sound.
public sealed class HudChat
{
    private static readonly Color HoveredName = Color.White;

    public HudChatWindow Window { get; } = new();
    public HudChatBubbles Bubbles { get; } = new();

    private double _updatedMs;
    private HudChatTab _updatedTab;

    // Sends a typed line (party: the party channel) with the item linked into it; set by the screen.
    public Func<string, bool, Guid, Task> Send { get; set; }

    // The lines Ctrl+F1 to Ctrl+F10 type (client-settings.json chatMacros); set by the HUD.
    public IReadOnlyList<string> Macros { get; set; } = [];

    // The system clipboard (Ctrl+C, Ctrl+X, Ctrl+V); tests replace it.
    public Func<string> ReadClipboard { get; set; } = Clipboard.GetText;
    public Action<string> WriteClipboard { get; set; } = Clipboard.SetText;

    private static readonly Keys[] MacroKeys = [Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10];

    // Links an item into the open input line (Shift and a click on a bag or worn item); false when the input is closed.
    public bool LinkItem(PristonTale.Simulation.Characters.OwnedItemView item)
    {
        if (!Window.Focused)
        {
            return false;
        }

        Window.Input.Link(item.InstanceId, LinkName(item));
        _linked = item;
        return true;
    }

    // The name a link shows: an Uncommon or rarer item's grade first and its aging after ("Legendary Wand +20"); an item
    // without details keeps its plain name.
    internal static string LinkName(PristonTale.Simulation.Characters.OwnedItemView item)
    {
        if (item.Details is not { } details)
        {
            return item.Name;
        }

        var level = details.UpgradeLevel;
        var prefix = level > 0 ? "+" + level + " " : "";
        var name = prefix.Length > 0 && item.Name.StartsWith(prefix, StringComparison.Ordinal) ? item.Name[prefix.Length..] : item.Name;
        if (details.Rarity > PristonTale.Simulation.Items.ItemRarity.Common)
        {
            name = PristonTale.Simulation.Items.ItemRarities.Names[(int)details.Rarity] + " " + name;
        }

        return level > 0 ? name + " +" + level : name;
    }

    private PristonTale.Simulation.Characters.OwnedItemView _linked;

    // What the client knows of an entity in view (HudCombat): its kind and a summon's owner.
    public Func<ulong, (EntityKind Kind, ulong OwnerId)> Lookup { get; set; } = static _ => (EntityKind.None, 0);

    public string PlayerName { get; set; } = "";
    public ulong SelfId { get; set; }

    // Raised when a whisper or notice opens the closed window (the whisper sound).
    public event Action WhisperArrived;

    // The other side of each whisper line, sent or received (the friends page's recent players).
    public Action<string> WhisperPartner { get; set; }

    // Raised when the server asks the client to save and quit.
    public event Action CloseRequested;

    public bool KeyboardCaptured => Window.Focused;

    public void Observe(PresenceUpdate update, double nowMs)
    {
        foreach (var line in update.Chat)
        {
            Receive(line, nowMs);
        }

        if (update.CloseRequested)
        {
            CloseRequested?.Invoke();
        }
    }

    public void Receive(ChatLineInfo line, double nowMs)
    {
        var text = line.Line.IsNone ? line.Text : ClientText.Format(line.Line);
        var (kind, _) = line.SpeakerId == 0 ? (EntityKind.None, 0u) : Lookup(line.SpeakerId);
        if (kind != EntityKind.None && line.Kind != ChatKind.Party)
        {
            Bubbles.Say(line.SpeakerId, text, nowMs, ChatRules.OtherBubbleMs);
            if (kind == EntityKind.Player)
            {
                Window.Log.Add(text, ChatKind.Player, line.Item);
            }

            return;
        }

        Window.Log.Add(text, line.Kind, line.Item);
        if (HudChatWindow.WhisperPartnerOf(line.Kind, text) is { } partner)
        {
            WhisperPartner?.Invoke(partner);
        }

        if (line.Kind is ChatKind.Whisper or ChatKind.Notice && !Window.Open)
        {
            Window.Reveal();
            WhisperArrived?.Invoke();
        }
    }

    // A line of the client's own (results of the player's actions) in the server colour.
    public void System(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Window.Log.Add(text, ChatKind.Server);
        }
    }

    // A quest hint in the assistant's colour and page.
    public void Assist(string text) => Window.Log.Add(text, ChatKind.Assist);

    // Enter opens the input or sends its line (Escape closes the window through the HUD); Page Up and Page Down scroll; Up and Down recall
    // commands. Typed characters (backspace included) arrive from the window's text input.
    public void Update(KeyboardState keyboard, KeyboardState previous, IEnumerable<char> typed, double nowMs, HudChatTab tab)
    {
        _updatedMs = nowMs;
        _updatedTab = tab;
        Window.SendClicked ??= SendTyped;
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && !previous.IsKeyDown(key);
        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (control && Macro(Pressed) is { Length: > 0 } macro)
        {
            Window.OpenInput();
            Window.Input.Set(macro);
            return;
        }

        if (!Window.Focused)
        {
            if (Pressed(Keys.Enter) && !control)
            {
                Window.OpenInput();
            }

            return;
        }

        if (Pressed(Keys.Tab))
        {
            Window.Input.Set("/CLAN> ");
        }

        if (control)
        {
            EditKeys(Pressed(Keys.A), Pressed(Keys.C), Pressed(Keys.X), Pressed(Keys.V));
        }

        foreach (var character in typed)
        {
            if (character == '\b')
            {
                Window.Input.Backspace();
            }
            else
            {
                Window.Input.Type(character);
            }
        }

        if (Pressed(Keys.PageUp))
        {
            Window.Log.ScrollBy(Math.Max(1, Window.ShownMessages));
        }

        if (Pressed(Keys.PageDown))
        {
            Window.Log.ScrollBy(-Math.Max(1, Window.ShownMessages));
        }

        if (Pressed(Keys.Up))
        {
            Window.Input.Recall(older: true);
        }

        if (Pressed(Keys.Down))
        {
            Window.Input.Recall(older: false);
        }

        if (Pressed(Keys.Enter) && !control)
        {
            Submit(nowMs, tab);
        }
    }

    // Ctrl+A selects the line, Ctrl+C copies it, Ctrl+X cuts it and Ctrl+V pastes the clipboard.
    private void EditKeys(bool selectAll, bool copy, bool cut, bool paste)
    {
        var input = Window.Input;
        if (selectAll)
        {
            input.SelectAll();
        }

        if (copy && input.Text.Length > 0)
        {
            WriteClipboard(input.Copied);
        }

        if (cut && input.Text.Length > 0)
        {
            WriteClipboard(input.Cut());
        }

        if (paste && ReadClipboard() is { Length: > 0 } text)
        {
            input.Paste(text);
        }
    }

    // The macro of the Ctrl+F key pressed this frame, or null.
    private string Macro(Func<Keys, bool> pressed)
    {
        for (var index = 0; index < MacroKeys.Length && index < Macros.Count; index++)
        {
            if (pressed(MacroKeys[index]))
            {
                return Macros[index];
            }
        }

        return null;
    }

    // The send button beside the input line does what Enter does.
    private void SendTyped()
    {
        if (Window.Focused)
        {
            Submit(_updatedMs, _updatedTab);
        }
    }

    private void Submit(double nowMs, HudChatTab tab)
    {
        if (Window.Input.Text.Length == 0)
        {
            Window.Unfocus();
            return;
        }

        var send = Window.Input.Submit(PlayerName, tab, nowMs, out var complaint);
        if (complaint is not null)
        {
            Bubbles.Say(SelfId, complaint, nowMs, ChatRules.FloodPenaltyMs);
            Bubbles.Emote(SelfId, HudEmotes.Art[HudEmotes.Crying - 1], nowMs);
        }

        if (send is not { } line)
        {
            Window.Input.Clear();
            return;
        }

        var linked = line.ItemId != Guid.Empty && _linked?.InstanceId == line.ItemId ? _linked : null;
        _linked = null;
        switch (line.Kind)
        {
            case HudChatSendKind.Spoken:
                Window.Log.Add(line.Shown, ChatKind.Player, linked);
                Bubbles.Say(SelfId, line.Shown, nowMs, ChatRules.SelfBubbleMs);
                break;
            case HudChatSendKind.Party:
                Window.Log.Add(line.Shown, ChatKind.Party, linked);
                break;
        }

        _ = Send?.Invoke(line.Text, line.Kind == HudChatSendKind.Party, line.ItemId);
    }

    // Whether a player is in the local player's party (its name takes the party colour).
    public Func<ulong, bool> InParty { get; set; } = static _ => false;

    // The colour of a speaker's name in a bubble: players, the party and owned summons, monsters, NPCs; the hovered or
    // selected speaker is drawn brighter.
    public Color NameColor(ulong id, ulong hovered)
    {
        var (kind, owner) = id == SelfId ? (EntityKind.Player, 0u) : Lookup(id);
        if (id == hovered)
        {
            return kind == EntityKind.Monster && owner == 0 ? HudNameColors.HoveredMonster : HoveredName;
        }

        return kind switch
        {
            EntityKind.Monster => owner != 0 ? HudNameColors.Party : HudNameColors.Monster,
            EntityKind.Npc => HudNameColors.Npc,
            _ => InParty(id) ? HudNameColors.Party : HudNameColors.Player
        };
    }
}
