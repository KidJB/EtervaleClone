using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Chat;

// The chat window in the bottom-left corner, in 800x600 design units with the bottom offset added. Enter opens it with
// the input line focused; with a status, skill or inventory window open it shrinks to a small box above them. Its tab
// buttons filter the log (All, Clan, Party, Raid, Trade, Whisper); the notice button cycles the assistant's page: off, three
// assistant lines over eight chat lines, or the assistant's page alone. The window can be dragged by its log (Offset moves
// every part of it); it darkens while the player types. A line with a linked item is drawn in the link colour and its
// item's tooltip shows while the pointer is on it; an emote line shows its icon when the art is present.
public sealed class HudChatWindow
{
    // The most messages a frame walks back through the log, and the painter's delegates and the input's visible tail,
    // made once instead of every frame.
    private const int MostMessages = 32;
    private Func<char, bool> _canDraw;
    private Func<string, int> _measure;
    private string _inputText;
    private string _inputShown = "";

    private const string Chat = "image/Sinimage/Inter/Chat/";
    private const string Folder = "Hud/Chat/";
    public const string InputArt = Folder + "input.png";
    public const string Send = Folder + "send.png";
    public const string SendLit = Folder + "send_.png";
    public const string Divider = Folder + "chatbar.png";
    public const string DiscordArt = Folder + "discord.png";
    public const string DiscordLitArt = Folder + "discord_.png";
    public const string DownButton = Chat + "ChatDownButton.bmp";
    private static readonly string[] TabNames = ["all", "clan", "party", "raid", "trade", "pm"];
    public static readonly string[] Tabs = TabNames.Select(name => $"{Folder}{name}.png").ToArray();
    public static readonly string[] TabsLit = TabNames.Select(name => $"{Folder}{name}_.png").ToArray();
    public static readonly string[] Art = [InputArt, Send, SendLit, Divider, DownButton, DiscordArt, DiscordLitArt, .. Tabs, .. TabsLit, .. HudEmotes.Art];
    private static readonly string[] NoticeLabels = ["notice", "split", "page"];
    private const int Top = 381;
    private const int TabWidth = 43;
    private const int TabStep = 45;
    private const int InputY = 576;
    private const int InputWidth = 270;
    private const int InputHeight = 23;
    private const int SendWidth = 25;
    private const int NewestY = 543;
    private const int NoticeNewestY = 433;
    private const int DragThreshold = 4;
    private const int IdleAlpha = 90;
    private const int TypingAlpha = 160;
    private static readonly Color LinkColor = new(240, 240, 0);

    // A side window lifts the input line and the log by this much.
    public const int SideLift = 170;

    // Lines wrap at 44 six-pixel characters.
    public const int WrapPixels = 44 * 6;
    public const int MessageX = 16;

    // The typed line starts after the 22-pixel chat button drawn at x 7.
    public const int InputX = 32;
    public const int InputPixels = 234;
    private static readonly Color InputColor = Color.White;
    private static readonly Color SelectionColor = new Color(60, 110, 200) * 0.6f;
    private readonly List<(Rectangle Bounds, HudChatEntry Entry)> _drawnLines = new();
    private readonly HudWrapCache _wrap = new(WrapPixels, 255);
    private Point _dragFrom;
    private Point _dragOffset;
    private bool _pressed;
    private bool _dragging;

    public HudChatLog Log { get; } = new();
    public HudChatInput Input { get; } = new();

    // The window shows (the classic chat flag) and the input line has the keyboard.
    public bool Open { get; private set; }
    public bool Focused { get; private set; }

    // 0 chat only, 1 assistant lines over the chat, 2 the assistant's page.
    public int NoticeMode { get; private set; }

    // How far the player dragged the window from its place, in design units.
    public Point Offset { get; private set; }

    // The send button beside the input line; set by the chat.
    public Action SendClicked { get; set; }

    // The Discord button beside it, shown while an invite is set; the HUD asks before it opens the link.
    public Action DiscordClicked { get; set; }

    // Messages drawn in the last frame; Page Up and Page Down scroll by this many.
    public int ShownMessages { get; private set; }

    // The linked item of the line under the pointer in the last frame, for its tooltip.
    public OwnedItemView HoveredItem { get; private set; }

    public void OpenInput()
    {
        Open = true;
        Focused = true;
    }

    // The input line takes the keyboard holding "/:name " (a whisper line clicked, a party member double-clicked).
    public void WhisperTo(string name)
    {
        Input.WhisperTo(name);
        OpenInput();
    }

    // The input line takes the keyboard on the tab, which its lines then go to (the clan page's chat button: the clan's).
    public void TalkOn(HudChatTab tab)
    {
        Log.SelectTab(tab);
        OpenInput();
    }

    // A whisper (and Enter) opens the window without taking the keyboard.
    public void Reveal() => Open = true;

    public void Unfocus()
    {
        Focused = false;
        Log.ResetScroll();
    }

    public void Close()
    {
        Open = false;
        Focused = false;
        Input.Clear();
        Log.ResetScroll();
    }

    public static Rectangle TabButton(HudLayout layout, int tab) => new(3 + tab * TabStep, Top + 177 + layout.BottomOffset, TabWidth, 15);

    public static Rectangle NoticeButton(HudLayout layout) => TabButton(layout, Tabs.Length);

    private static Rectangle SendButton(HudLayout layout, bool side) => new(InputWidth, InputY - Lift(side) + layout.BottomOffset, SendWidth, InputHeight);

    private static Rectangle DiscordButton(HudLayout layout, bool side) => new(InputWidth + SendWidth, InputY - Lift(side) + layout.BottomOffset, SendWidth, InputHeight);

    public static Rectangle Toggle(HudLayout layout, bool side) => new(7, 579 - Lift(side) + layout.BottomOffset, 15, 19);

    public static Rectangle Box(HudLayout layout, bool side) =>
        side ? new Rectangle(0, 294 + layout.BottomOffset, 300, 116) : new Rectangle(0, Top + layout.BottomOffset, 295, 218);

    private Rectangle Moved(Rectangle bounds) => new(bounds.X + Offset.X, bounds.Y + Offset.Y, bounds.Width, bounds.Height);

    private bool DiscordShown => DiscordClicked is not null && HudChatLinks.DiscordInvite.Length > 0;

    public bool Contains(HudLayout layout, Point pointer, bool side) =>
        Open && (Moved(Box(layout, side)).Contains(pointer) || Moved(InputBox(layout, side)).Contains(pointer)) || Moved(Toggle(layout, side)).Contains(pointer);

    // Dragging the log moves the window; a press that does not move is a click (Click, the same frame it went down).
    public void Drag(HudLayout layout, MouseState mouse, MouseState previous, Point pointer, bool side)
    {
        var down = mouse.LeftButton == ButtonState.Pressed;
        if (down && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Open && Moved(Box(layout, side)).Contains(pointer);
            _dragging = false;
            (_dragFrom, _dragOffset) = (pointer, Offset);
            return;
        }

        if (!down)
        {
            _pressed = false;
            _dragging = false;
            return;
        }

        if (_pressed && (_dragging || Math.Abs(pointer.X - _dragFrom.X) + Math.Abs(pointer.Y - _dragFrom.Y) > DragThreshold))
        {
            _dragging = true;
            var box = Box(layout, side);
            Offset = new Point(Math.Clamp(_dragOffset.X + pointer.X - _dragFrom.X, -box.X, Math.Max(-box.X, layout.Width - box.Right)),
                Math.Clamp(_dragOffset.Y + pointer.Y - _dragFrom.Y, -box.Y, Math.Max(-box.Y, layout.Height - box.Y - InputY + Top - InputHeight)));
        }
    }

    // Tabs, the notice button, the toggle and a whisper line; true when the click belonged to the chat.
    public bool Click(HudLayout layout, Point pointer, bool side)
    {
        if (Moved(Toggle(layout, side)).Contains(pointer))
        {
            Open = !Open;
            if (!Open)
            {
                Focused = false;
            }

            return true;
        }

        if (!Open)
        {
            return false;
        }

        if (Moved(SendButton(layout, side)).Contains(pointer))
        {
            SendClicked?.Invoke();
            return true;
        }

        if (DiscordShown && Moved(DiscordButton(layout, side)).Contains(pointer))
        {
            DiscordClicked();
            return true;
        }

        if (!side)
        {
            for (var tab = 0; tab < Tabs.Length; tab++)
            {
                if (Moved(TabButton(layout, tab)).Contains(pointer))
                {
                    Log.SelectTab((HudChatTab)tab);
                    return true;
                }
            }

            if (Moved(NoticeButton(layout)).Contains(pointer))
            {
                NoticeMode = (NoticeMode + 1) % 3;
                return true;
            }
        }

        foreach (var (bounds, entry) in _drawnLines)
        {
            if (bounds.Contains(pointer) && WhisperPartner(entry) is { } partner)
            {
                WhisperTo(partner);
                return true;
            }
        }

        return Moved(Box(layout, side)).Contains(pointer) || Moved(InputBox(layout, side)).Contains(pointer);
    }

    // The other side of a whisper line ("From> name: ..." or "To> name: ...").
    public static string WhisperPartner(HudChatEntry entry) => WhisperPartnerOf(entry.Kind, entry.Text);

    public static string WhisperPartnerOf(ChatKind kind, string text)
    {
        if (kind != ChatKind.Whisper)
        {
            return null;
        }

        var start = text.StartsWith("From> ", StringComparison.Ordinal) ? 6 : text.StartsWith("To> ", StringComparison.Ordinal) ? 4 : -1;
        var colon = start < 0 ? -1 : text.IndexOf(':', start);
        return colon > start ? text[start..colon].Trim() : null;
    }

    public void Draw(HudPainter painter, HudLayout layout, bool side, Point pointer)
    {
        Input.CanDraw = _canDraw ??= painter.CanDraw;
        _measure ??= painter.Measure;
        _drawnLines.Clear();
        ShownMessages = 0;
        HoveredItem = null;
        if (!Open)
        {
            return;
        }

        var (x, y) = (Offset.X, Offset.Y + layout.BottomOffset);
        var box = Moved(Box(layout, side));
        painter.ColorBox(box, 0, 0, 0, Focused ? TypingAlpha : IdleAlpha);
        if (!side)
        {
            var selected = Math.Clamp((int)Log.Tab, 0, Tabs.Length - 1);
            for (var tab = 0; tab < Tabs.Length; tab++)
            {
                var button = Moved(TabButton(layout, tab));
                painter.Sprite(tab == selected || button.Contains(pointer) ? TabsLit[tab] : Tabs[tab], button.X, button.Y);
            }

            var notice = Moved(NoticeButton(layout));
            painter.ColorBox(notice, 40, 34, 26, NoticeMode == 0 ? 170 : 240);
            painter.TextCentered(NoticeLabels[NoticeMode], notice.X, notice.Right, notice.Y - 2, NoticeMode == 0 ? Color.LightGray : Color.White);
            if (NoticeMode == 1)
            {
                painter.Sprite(Divider, x, Top + 66 + y);
            }
        }

        var lift = Lift(side);
        painter.Sprite(InputArt, x, InputY - lift + y);
        var send = Moved(SendButton(layout, side));
        painter.Sprite(send.Contains(pointer) ? SendLit : Send, send.X, send.Y);
        if (DiscordShown)
        {
            var discord = Moved(DiscordButton(layout, side));
            if (painter.Texture(DiscordArt) is null)
            {
                painter.ColorBox(discord, 88, 101, 242, discord.Contains(pointer) ? 255 : 200);
                painter.TextCentered("D", discord.X, discord.Right, discord.Y + 3, Color.White);
            }
            else
            {
                painter.Sprite(discord.Contains(pointer) ? DiscordLitArt : DiscordArt, discord.X, discord.Y);
            }
        }

        if (Focused)
        {
            painter.Sprite(DownButton, x + 7, 580 - lift + y);
            // The input's visible tail is cut again only when the text changes.
            var text = Input.Text;
            if (!ReferenceEquals(text, _inputText))
            {
                _inputText = text;
                var start = 0;
                while (start < text.Length && painter.Measure(text[start..]) > InputPixels)
                {
                    start++;
                }

                _inputShown = start == 0 ? text : text[start..];
            }

            var inputY = 584 - lift + y - (Math.Max(12, HudPainter.LineHeight) - 12) / 2;
            if (Input.AllSelected)
            {
                painter.Fill(new Rectangle(x + InputX - 1, inputY - 1, painter.Measure(_inputShown) + 2, Math.Max(12, HudPainter.LineHeight) + 2), SelectionColor);
            }

            painter.Text(_inputShown, x + InputX, inputY, InputColor);
        }

        painter.Flush();
        DrawMessages(painter, layout, side, pointer);
    }

    private void DrawMessages(HudPainter painter, HudLayout layout, bool side, Point pointer)
    {
        var bottom = layout.BottomOffset + Offset.Y;
        if (side)
        {
            DrawLines(painter, NoticeMode == 2 ? HudChatTab.Assist : Log.Tab, 570 - SideLift - 5 + bottom, 8, NoticeMode == 2 ? 0 : Log.Scroll, pointer);
            return;
        }

        switch (NoticeMode)
        {
            case 1:
                DrawLines(painter, HudChatTab.Assist, NoticeNewestY + bottom, 5, 0, pointer);
                DrawLines(painter, Log.Tab, NewestY + bottom, 8, Log.Scroll, pointer);
                break;
            case 2:
                DrawLines(painter, HudChatTab.Assist, NewestY + bottom, 13, Log.Scroll, pointer);
                break;
            default:
                DrawLines(painter, Log.Tab, NewestY + bottom, 13, Log.Scroll, pointer);
                break;
        }
    }

    // The newest message sits at y; older messages go up line by line, each wrapped and drawn from its last line upward.
    private void DrawLines(HudPainter painter, HudChatTab tab, int y, int maxLines, int scroll, Point pointer)
    {
        var lineHeight = Math.Max(12, HudPainter.LineHeight);
        // The classic lines are 12 pixels high; a taller line keeps the newest line's bottom where the classic one ended
        // and draws fewer lines so each block stays inside its classic area.
        y -= lineHeight - 12;
        maxLines = Math.Max(1, maxLines * 12 / lineHeight);
        var x = MessageX + Offset.X;
        var drawn = 0;
        var messages = 0;
        var available = Math.Min(Log.NewestCount(tab, scroll), MostMessages);
        for (var n = 0; n < available; n++)
        {
            var entry = Log.NewestAt(tab, scroll, n);
            if (drawn >= maxLines)
            {
                break;
            }

            var lines = _wrap.Wrap(entry.Text, maxLines, _measure);
            messages++;
            var color = HudChatLog.ColorOf(entry.Kind);
            for (var index = lines.Count - 1; index >= 0 && drawn < maxLines; index--)
            {
                var bounds = new Rectangle(x, y, WrapPixels, lineHeight);
                // An emote line shows the speaker's name and the icon in place of its code.
                if (index == lines.Count - 1 && HudEmotes.Of(entry.Text) is { } emote && painter.Texture(emote) is not null)
                {
                    var name = Speaker(entry.Text);
                    painter.Text(name, x, y, color, true, new Point(-1, -1));
                    painter.Stretch(emote, new Rectangle(x + painter.Measure(name) + 3, y - 3, lineHeight + 6, lineHeight + 6));
                }
                else if (entry.Item is not null)
                {
                    var link = DrawLinked(painter, lines[index], x, y, color, LinkColorOf(entry.Item), lineHeight);
                    if (link.Contains(pointer))
                    {
                        HoveredItem = entry.Item;
                    }
                }
                else
                {
                    painter.Text(lines[index], x, y, color, true, new Point(-1, -1));
                }

                _drawnLines.Add((bounds, entry));
                y -= lineHeight;
                drawn++;
            }
        }

        ShownMessages = Math.Max(ShownMessages, messages);
    }

    // A linked line in its channel's colour with only the bracketed item name in the link colour (a name wrapped over two
    // lines is coloured on each); returns where the link part was drawn.
    // An Uncommon or rarer item's link takes its grade's colour; any other link stays yellow.
    private static Color LinkColorOf(OwnedItemView item) =>
        item.Details is { Rarity: > ItemRarity.Common } details ? HudRarityColors.Of(details.Rarity) : LinkColor;

    private Rectangle DrawLinked(HudPainter painter, string line, int x, int y, Color color, Color linkColor, int lineHeight)
    {
        if (!_linkParts.TryGetValue(line, out var parts))
        {
            if (_linkParts.Count > MostSpeakers)
            {
                _linkParts.Clear();
            }

            parts = SplitLink(line);
            _linkParts[line] = parts;
        }

        var shadow = new Point(-1, -1);
        painter.Text(parts.Before, x, y, color, true, shadow);
        var linkX = x + painter.Measure(parts.Before);
        painter.Text(parts.Link, linkX, y, linkColor, true, shadow);
        var linkWidth = painter.Measure(parts.Link);
        painter.Text(parts.After, linkX + linkWidth, y, color, true, shadow);
        return new Rectangle(linkX, y, linkWidth, lineHeight);
    }

    // The part of a wrapped line that belongs to "[item]": from its "[" (or the line's start) to its "]" (or the end).
    internal static (string Before, string Link, string After) SplitLink(string line)
    {
        var open = line.IndexOf('[');
        var close = line.IndexOf(']', Math.Max(open, 0));
        if (open < 0 && close < 0)
        {
            return (line, "", "");
        }

        var start = open < 0 ? 0 : open;
        var end = close < 0 ? line.Length : close + 1;
        return (line[..start], line[start..end], line[end..]);
    }

    private readonly Dictionary<string, (string Before, string Link, string After)> _linkParts = new();

    // "Name:" of an emote line, cut once per line.
    private string Speaker(string line)
    {
        if (!_speakers.TryGetValue(line, out var name))
        {
            if (_speakers.Count > MostSpeakers)
            {
                _speakers.Clear();
            }

            name = HudEmotes.Speaker(line);
            _speakers[line] = name;
        }

        return name;
    }

    private const int MostSpeakers = 256;
    private readonly Dictionary<string, string> _speakers = new();

    private static int Lift(bool side) => side ? SideLift : 0;

    private static Rectangle InputBox(HudLayout layout, bool side) =>
        new(0, InputY - Lift(side) + layout.BottomOffset, InputWidth + 2 * SendWidth, InputHeight);
}
