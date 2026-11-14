using System.Text;
using PristonTale.Simulation.Social;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Chat;

// What the input line sends: a spoken line of the local player (shown at once in the log and over the head), a party line
// (shown at once in the log), or a command the server answers.
public enum HudChatSendKind
{
    Spoken,
    Party,
    Command
}

// ItemId: the item linked into the line (Guid.Empty for none), its tooltip shown with the line.
public readonly record struct HudChatSend(HudChatSendKind Kind, string Text, string Shown, Guid ItemId = default);

// The chat input line: 78 characters, the last 16 commands recalled with Up and Down, and the flood rules. A line is
// composed for the selected tab (clan, party, raid and trade tabs prefix their channel); a slash line goes as typed, "$text"
// to the trade channel and "#text" to the raid. An emote word goes as its code. A linked item adds its name in brackets;
// the line carries it while the name stays in the text.
public sealed class HudChatInput
{
    public const int HistorySize = 16;
    private readonly StringBuilder _text = new();
    private readonly string[] _history = new string[HistorySize];
    private int _historyCount;
    private int _recall;
    private readonly ChatFloodGuard _flood = new();

    // The characters the HUD font draws; the window sets it from its painter. The wire carries any character.
    public Func<char, bool> CanDraw { get; set; } = static _ => true;
    private string _whisperName = "";
    private int _whisperPrefix;
    private Guid _linkedId;
    private string _linkedName = "";

    // The text as a string, made once per change (the window reads it every frame).
    public string Text => _shown ??= _text.ToString();
    private string _shown;

    // The whole line is selected (Ctrl+A): typing replaces it, Backspace erases it, a paste takes its place.
    public bool AllSelected { get; private set; }

    public void SelectAll() => AllSelected = _text.Length > 0;

    // What Ctrl+C copies: the selection, or the whole line when nothing is selected.
    public string Copied => Text;

    // Ctrl+X: the line goes to the clipboard and leaves the input.
    public string Cut()
    {
        var text = Text;
        Clear();
        return text;
    }

    // Ctrl+V: the clipboard's text joins the line (in place of a selection); line breaks and tabs become spaces, other
    // control characters and characters the font cannot draw are dropped, and the line stops at its limit.
    public void Paste(string text)
    {
        if (AllSelected)
        {
            Clear();
        }

        foreach (var character in text)
        {
            var value = character is '\r' or '\n' or '\t' ? ' ' : character;
            if (char.IsControl(value) || !CanDraw(value))
            {
                continue;
            }

            if (_text.Length >= ChatRules.InputLimit)
            {
                break;
            }

            _text.Append(value);
        }

        _shown = null;
    }

    public void Clear()
    {
        _text.Clear();
        _shown = null;
        AllSelected = false;
        _whisperName = "";
        _whisperPrefix = 0;
        _linkedId = Guid.Empty;
        _linkedName = "";
    }

    // Links an item: its name in brackets joins the line.
    public void Link(Guid itemId, string name)
    {
        var token = "[" + name + "]";
        if (_text.Length + token.Length + 1 > ChatRules.InputLimit)
        {
            return;
        }

        if (_text.Length > 0 && _text[^1] != ' ')
        {
            _text.Append(' ');
        }

        _text.Append(token);
        _shown = null;
        (_linkedId, _linkedName) = (itemId, token);
    }

    public void Set(string text)
    {
        _text.Clear().Append(text.Length > ChatRules.InputLimit ? text[..ChatRules.InputLimit] : text);
        _shown = null;
        AllSelected = false;
    }

    // Starts a whisper conversation: the line holds "/:name " and comes back to it after each line sent.
    public void WhisperTo(string name)
    {
        Set("/:" + name + " ");
        _whisperName = name;
        _whisperPrefix = _text.Length;
    }

    public bool Type(char value)
    {
        if (char.IsControl(value) || !CanDraw(value))
        {
            return false;
        }

        if (AllSelected)
        {
            Clear();
        }

        if (_text.Length >= ChatRules.InputLimit)
        {
            return false;
        }

        _text.Append(value);
        _shown = null;
        return true;
    }

    public void Backspace()
    {
        if (AllSelected)
        {
            Clear();
            return;
        }

        if (_text.Length > 0)
        {
            _text.Remove(_text.Length - 1, 1);
            _shown = null;
        }
    }

    // Up walks back through the saved commands, Down forward; past the newest the line empties.
    public void Recall(bool older)
    {
        if (_historyCount == 0)
        {
            return;
        }

        if (older)
        {
            _recall--;
            if (_recall < 0)
            {
                _recall = _historyCount - 1;
            }
        }
        else
        {
            _recall++;
            if (_recall >= _historyCount)
            {
                _recall = _historyCount - 1;
                _text.Clear();
                _shown = null;
                return;
            }
        }

        Set(_history[_recall % HistorySize]);
    }

    // The line to send for Enter, or null when nothing goes out (empty line, frozen by flooding, a repeated line). The
    // spoken text the player sees is "Name: text"; a frozen chat answers with the overhead complaint instead.
    public HudChatSend? Submit(string playerName, HudChatTab tab, double nowMs, out string complaint)
    {
        complaint = null;
        var typed = HudEmotes.Code(ChatRules.WithoutRoute(Text.TrimEnd(' ')));
        var linked = _linkedName.Length > 0 && typed.Contains(_linkedName, StringComparison.Ordinal) ? _linkedId : Guid.Empty;
        (_linkedId, _linkedName) = (Guid.Empty, "");
        if (typed.Length > 1 && typed[0] == '$')
        {
            typed = "/TRADE> " + typed[1..];
        }
        else if (typed.Length > 1 && typed[0] == '#')
        {
            typed = "/raid " + typed[1..];
        }
        var continued = _whisperName.Length > 0 && Text.Length > _whisperPrefix;
        if (!continued)
        {
            _whisperName = "";
            _whisperPrefix = 0;
        }

        if (typed.Length == 0)
        {
            return null;
        }

        var command = ChatRules.IsCommand(typed) || typed[0] == '@';
        var whisper = ChatRules.IsWhisper(typed);
        switch (_flood.Check(typed, command, whisper, nowMs))
        {
            case ChatFloodVerdict.Froze:
                complaint = ClientText.Format("Chat.Frozen", playerName);
                return null;
            case ChatFloodVerdict.Frozen or ChatFloodVerdict.Repeated:
                return null;
        }

        if (command && typed.Length > 1 && typed[1] != '*' && typed[0] != '@')
        {
            Save(typed);
        }

        _text.Clear();
        _shown = null;
        if (continued)
        {
            _text.Append("/:").Append(_whisperName).Append(' ');
            _shown = null;
        }

        if (command)
        {
            return typed[0] == '@'
                ? new HudChatSend(HudChatSendKind.Party, typed[1..], playerName + ": " + typed[1..], linked)
                : new HudChatSend(HudChatSendKind.Command, typed, typed, linked);
        }

        return tab switch
        {
            HudChatTab.Clan => new HudChatSend(HudChatSendKind.Command, "/CLAN> " + typed, typed, linked),
            HudChatTab.Party => new HudChatSend(HudChatSendKind.Party, typed, playerName + ": " + typed, linked),
            HudChatTab.Raid => new HudChatSend(HudChatSendKind.Command, "/raid " + typed, typed, linked),
            HudChatTab.Trade => new HudChatSend(HudChatSendKind.Command, "/TRADE> " + typed, typed, linked),
            _ when YahooRules.IsWord(typed) => new HudChatSend(HudChatSendKind.Command, typed, typed),
            _ => new HudChatSend(HudChatSendKind.Spoken, typed, ChatRules.Compose(playerName, typed), linked)
        };
    }

    // A command is remembered by its first word and a space, once.
    private void Save(string line)
    {
        var space = line.IndexOf(' ');
        var word = (space < 0 ? line : line[..space]) + " ";
        for (var index = 0; index < Math.Min(_historyCount, HistorySize); index++)
        {
            if (_history[index] == word)
            {
                return;
            }
        }

        _history[_historyCount % HistorySize] = word;
        _historyCount++;
        _recall = _historyCount;
    }
}
