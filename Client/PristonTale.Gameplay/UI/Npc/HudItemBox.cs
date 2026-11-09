using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Npc;

// The Item Distributor, opened from an NPC offering it (GiftExpress): a dark frame with the art of Hud/Distributor. Retrieve
// Items shows the waiting items one at a time (sender, date, the item with its values on hover; arrows page them) with
// Accept, which takes it into the bag, and Discard, which drops it; an empty box shows the box picture. Send Items takes a
// bag item the player clicks while it shows and a character's name, and sends it to that character's account. Requests are
// read back without waiting; Escape closes it.
public sealed class HudItemBox
{
    private const string Folder = "Hud/Distributor/";
    private const string TitleArt = Folder + "title.png";
    private const string UnderlineArt = Folder + "underline.png";
    private const string RetrieveArt = Folder + "retrieveitems.png";
    private const string RetrieveLitArt = Folder + "retrieveitems_.png";
    private const string SendTabArt = Folder + "senditems.png";
    private const string SendTabLitArt = Folder + "senditems_.png";
    private const string AcceptArt = Folder + "button_accept.png";
    private const string AcceptLitArt = Folder + "button_accept_.png";
    private const string DiscardArt = Folder + "button_discard.png";
    private const string DiscardLitArt = Folder + "button_discard_.png";
    private const string SendArt = Folder + "button_send.png";
    private const string SendLitArt = Folder + "button_send_.png";
    private const string EmptyArt = Folder + "emptyitembox_noglow.png";
    private const string DemoArt = Folder + "itemboxdemonstration.png";
    public static readonly string[] Art =
    [
        TitleArt, UnderlineArt, RetrieveArt, RetrieveLitArt, SendTabArt, SendTabLitArt, AcceptArt, AcceptLitArt, DiscardArt, DiscardLitArt, SendArt,
        SendLitArt, EmptyArt, DemoArt
    ];

    private const int Width = 360;
    private const int Height = 330;
    private static readonly Color FrameColor = new(10, 9, 8, 235);
    private static readonly Color BorderColor = new(150, 120, 70);
    private static readonly Color TextColor = new(235, 225, 205);
    private static readonly Color DimColor = new(160, 150, 135);
    private static readonly Color GoodColor = new(120, 220, 120);
    private static readonly Color BadColor = new(240, 110, 90);

    private readonly TextInputField _recipient = new() { MaxLength = 20, Accepts = static ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' };
    private Task<ItemBoxListInfo> _listing;
    private Task<byte> _acting;
    private IReadOnlyList<ItemBoxEntryInfo> _entries = [];
    private int _index;
    private string _pageLine = "";
    private string _senderLine = "";
    private string _status = "";
    private Color _statusColor = TextColor;
    private ulong _npcId;
    private OwnedItemView _hovered;
    private List<(string Text, Color Color)> _description;
    private OwnedItemView _described;

    public bool Open { get; private set; }
    public bool Sending { get; private set; }

    // The bag item picked to send.
    public OwnedItemView Picked { get; private set; }

    // The name box has the keyboard.
    public bool Naming => Open && Sending && _recipient.Focused;

    public Func<ulong, Task<ItemBoxListInfo>> ListRequest { get; set; }
    public Func<ulong, byte, long, Guid, string, Task<byte>> ActRequest { get; set; }

    public static Rectangle Window(HudLayout layout) => new((layout.Width - Width) / 2, Math.Max(0, (layout.Height - Height) / 2 - 60), Width, Height);

    private static Rectangle RetrieveTab(Rectangle window) => new(window.X + 14, window.Y + 40, 165, 21);
    private static Rectangle SendTab(Rectangle window) => new(window.X + 196, window.Y + 40, 137, 21);
    private static Rectangle ItemCell(Rectangle window) => new(window.X + (Width - 88) / 2, window.Y + 108, 88, 88);
    private static Rectangle Previous(Rectangle window) => new(window.X + 40, window.Y + 140, 24, 24);
    private static Rectangle Next(Rectangle window) => new(window.Right - 64, window.Y + 140, 24, 24);
    private static Rectangle Accept(Rectangle window) => new(window.X + 14, window.Bottom - 92, 165, 80);
    private static Rectangle Discard(Rectangle window) => new(window.X + 189, window.Bottom - 92, 157, 81);
    private static Rectangle NameBox(Rectangle window) => new(window.X + 110, window.Y + 232, 200, 20);
    private static Rectangle SendButton(Rectangle window) => new(window.X + (Width - 116) / 2, window.Bottom - 50, 116, 36);
    private static Rectangle Close(Rectangle window) => new(window.Right - 24, window.Y + 6, 18, 18);

    public void Show(ulong npcId)
    {
        _npcId = npcId;
        Open = true;
        Sending = false;
        Picked = null;
        _recipient.Clear();
        _recipient.Focused = false;
        SetStatus("", TextColor);
        Reload();
    }

    public void Hide()
    {
        Open = false;
        Picked = null;
        _recipient.Focused = false;
    }

    public void CancelNaming() => _recipient.Focused = false;

    public void Type(ReadOnlySpan<char> typed, double elapsedSeconds, Func<Microsoft.Xna.Framework.Input.Keys, bool> pressed)
    {
        _recipient.Update(typed, elapsedSeconds);
        if (pressed(Microsoft.Xna.Framework.Input.Keys.Enter))
        {
            SendPicked();
        }
    }

    // A bag click while Send Items shows picks that item; true when the window took the click.
    public bool InterceptBagClick(OwnedItemView item)
    {
        if (!Open || !Sending || !item.InBag)
        {
            return false;
        }

        Picked = item;
        IconRevision++;
        _recipient.Focused = true;
        return true;
    }

    public bool Captures(HudLayout layout, Point pointer) => Open && Window(layout).Contains(pointer);

    // Rises whenever the waiting items or the picked item change, so the HUD requests their icons with the rest.
    public int IconRevision { get; private set; }

    public IEnumerable<string> IconKeys =>
        Open ? _entries.Select(static entry => entry.Item.CatalogKey).Concat(Picked is null ? [] : [Picked.CatalogKey]) : [];

    public void Update()
    {
        if (_listing is { IsCompleted: true } listing)
        {
            _listing = null;
            if (listing.IsCompletedSuccessfully && listing.Result.Status == 0)
            {
                _entries = listing.Result.Entries;
                IconRevision++;
                _index = Math.Clamp(_index, 0, Math.Max(0, _entries.Count - 1));
                Describe();
            }
            else
            {
                Hide();
            }
        }

        if (_acting is { IsCompleted: true } acting)
        {
            _acting = null;
            var status = acting.IsCompletedSuccessfully ? acting.Result : (byte)8;
            var (text, good) = status switch
            {
                0 => (Sending ? "Sent! It waits in their Item Distributor." : "Done.", true),
                1 => ("Come closer to the Item Distributor.", false),
                2 => ("That item is no longer here.", false),
                3 => ("Your bag has no room for it.", false),
                4 => ("There is no character with that name.", false),
                5 => ("That item cannot be sent.", false),
                6 => ("Busy; try again.", false),
                7 => ("The server could not store it; nothing changed.", false),
                _ => ("Refused.", false)
            };
            SetStatus(text, good ? GoodColor : BadColor);
            if (status == 0 && Sending)
            {
                Picked = null;
                _recipient.Clear();
            }

            Reload();
        }
    }

    public bool Click(HudLayout layout, Point pointer)
    {
        var window = Window(layout);
        if (!Open || !window.Contains(pointer))
        {
            return false;
        }

        if (Close(window).Contains(pointer))
        {
            Hide();
            return true;
        }

        if (RetrieveTab(window).Contains(pointer) || SendTab(window).Contains(pointer))
        {
            Sending = SendTab(window).Contains(pointer);
            _recipient.Focused = Sending && Picked is not null;
            SetStatus("", TextColor);
            return true;
        }

        if (_acting is not null)
        {
            return true;
        }

        if (Sending)
        {
            if (NameBox(window).Contains(pointer))
            {
                _recipient.Focused = true;
            }
            else if (SendButton(window).Contains(pointer))
            {
                SendPicked();
            }

            return true;
        }

        if (Previous(window).Contains(pointer) && _index > 0)
        {
            _index--;
            Describe();
        }
        else if (Next(window).Contains(pointer) && _index < _entries.Count - 1)
        {
            _index++;
            Describe();
        }
        else if (_entries.Count > 0 && (Accept(window).Contains(pointer) || Discard(window).Contains(pointer)))
        {
            var action = Accept(window).Contains(pointer) ? ItemBoxWire.ActionTake : ItemBoxWire.ActionDiscard;
            _acting = ActRequest?.Invoke(_npcId, action, _entries[_index].Id, Guid.Empty, "");
        }

        return true;
    }

    public void Draw(HudPainter painter, HudLayout layout, HudItemIcons icons, OwnerState state, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        painter.Fill(window, FrameColor);
        painter.ColorBox(new Rectangle(window.X, window.Y, window.Width, 1), BorderColor.R, BorderColor.G, BorderColor.B, 255);
        painter.ColorBox(new Rectangle(window.X, window.Bottom - 1, window.Width, 1), BorderColor.R, BorderColor.G, BorderColor.B, 255);
        painter.ColorBox(new Rectangle(window.X, window.Y, 1, window.Height), BorderColor.R, BorderColor.G, BorderColor.B, 255);
        painter.ColorBox(new Rectangle(window.Right - 1, window.Y, 1, window.Height), BorderColor.R, BorderColor.G, BorderColor.B, 255);
        painter.Sprite(TitleArt, window.X + (Width - 75) / 2, window.Y + 10);
        painter.Text("x", Close(window).X + 5, Close(window).Y, Close(window).Contains(pointer) ? TextColor : DimColor);
        var retrieve = RetrieveTab(window);
        var send = SendTab(window);
        painter.Sprite(!Sending || retrieve.Contains(pointer) ? RetrieveLitArt : RetrieveArt, retrieve.X, retrieve.Y);
        painter.Sprite(Sending || send.Contains(pointer) ? SendTabLitArt : SendTabArt, send.X, send.Y);
        painter.Sprite(UnderlineArt, window.X + (Width - 140) / 2, window.Y + 66);
        _hovered = null;
        if (Sending)
        {
            DrawSend(painter, window, icons, pointer);
        }
        else
        {
            DrawRetrieve(painter, window, icons, pointer);
        }

        painter.TextCentered(_status, window.X, window.Right, window.Y + 76, _statusColor);
        if (_hovered is { } item && state is not null)
        {
            if (!ReferenceEquals(item, _described))
            {
                (_described, _description) = (item, HudItemDetailTooltip.Lines(item, state, null, null));
            }

            HudItemDetailTooltip.Draw(painter, layout, pointer, _description, null, HudItemDetailTooltip.RarityOf(item));
        }
    }

    private void DrawRetrieve(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        if (_entries.Count == 0)
        {
            painter.Sprite(EmptyArt, window.X + (Width - 159) / 2, window.Y + 100);
            painter.TextCentered(_listing is null ? "Your Item Distributor is empty." : "Opening...", window.X, window.Right, window.Y + 250, DimColor);
            return;
        }

        var entry = _entries[_index];
        painter.TextCentered(_senderLine, window.X, window.Right, window.Y + 90, TextColor);
        var cell = ItemCell(window);
        painter.ColorBox(cell, 30, 26, 22, 255);
        var icon = new Rectangle(cell.Center.X - entry.Item.Width * 11, cell.Center.Y - entry.Item.Height * 11, entry.Item.Width * 22, entry.Item.Height * 22);
        icons.Draw(painter, entry.Item.CatalogKey, icon);
        if (cell.Contains(pointer))
        {
            _hovered = entry.Item;
        }

        painter.TextCentered(_pageLine, window.X, window.Right, cell.Bottom + 6, DimColor);
        if (_index > 0)
        {
            painter.TextCentered("<", Previous(window).X, Previous(window).Right, Previous(window).Y + 4, Previous(window).Contains(pointer) ? TextColor : DimColor);
        }

        if (_index < _entries.Count - 1)
        {
            painter.TextCentered(">", Next(window).X, Next(window).Right, Next(window).Y + 4, Next(window).Contains(pointer) ? TextColor : DimColor);
        }

        var accept = Accept(window);
        var discard = Discard(window);
        painter.Sprite(accept.Contains(pointer) ? AcceptLitArt : AcceptArt, accept.X, accept.Y);
        painter.Sprite(discard.Contains(pointer) ? DiscardLitArt : DiscardArt, discard.X, discard.Y);
    }

    private void DrawSend(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        painter.Sprite(DemoArt, window.X + (Width - 210) / 2, window.Y + 96);
        painter.TextCentered(Picked is null ? "Click an item in your bag to send it." : Picked.Name, window.X, window.Right, window.Y + 212,
            Picked is null ? DimColor : TextColor);
        if (Picked is { } picked)
        {
            var slot = new Rectangle(window.X + (Width - 210) / 2 + 100, window.Y + 96, 110, 112);
            var icon = new Rectangle(slot.Center.X - picked.Width * 11, slot.Center.Y - picked.Height * 11, picked.Width * 22, picked.Height * 22);
            icons.Draw(painter, picked.CatalogKey, icon);
            if (slot.Contains(pointer))
            {
                _hovered = picked;
            }
        }

        var box = NameBox(window);
        painter.Text("To:", window.X + 70, box.Y + 1, TextColor);
        painter.InputBox(box);
        painter.Text(_recipient.CaretVisible ? _recipient.Text + "_" : _recipient.Text, box.X + 4, box.Y + 1, TextColor);
        var button = SendButton(window);
        painter.Sprite(button.Contains(pointer) ? SendLitArt : SendArt, button.X, button.Y);
    }

    private void SendPicked()
    {
        if (Picked is null || _recipient.Text.Trim().Length == 0 || _acting is not null)
        {
            return;
        }

        _recipient.Focused = false;
        _acting = ActRequest?.Invoke(_npcId, ItemBoxWire.ActionSend, 0, Picked.InstanceId, _recipient.Text.Trim());
    }

    private void Reload()
    {
        _listing = ListRequest?.Invoke(_npcId);
    }

    private void Describe()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var entry = _entries[_index];
        _senderLine = string.Create(CultureInfo.InvariantCulture, $"From {entry.Sender}, {entry.SentAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        _pageLine = string.Create(CultureInfo.InvariantCulture, $"{_index + 1} of {_entries.Count}");
    }

    private void SetStatus(string text, Color color) => (_status, _statusColor) = (text, color);
}
