using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Markets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Gameplay.World;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Markets;

// The Character Market NPC's window: the class and order drop-downs, search and sale button above two columns of cards, each with the character's face and
// its picture from the waist up. A click on a card turns the window to its details page: the whole character, its facts, its held and stored gear, the looks it
// shows and its accessories, the price and the buy button (asked twice before it goes); Return goes back. The sale panel lists the account's characters with why
// each cannot be sold. Every request is one page from the server.
public sealed class HudCharacterMarket
{
    private const int Width = 760;
    private const int Height = 612;
    private const string FrameArt = "Hud/CharacterMarket/frame.png";
    private const string CoinArt = "Hud/CoinShop/coin.bmp";
    public static readonly string[] Art = [FrameArt, CoinArt];
    private const int GearSlotWidth = 66;
    private const int GearSlotHeight = 110;
    private const int AccessorySize = 34;
    private const int AccessoryColumns = 4;
    private const int AccessorySlots = 12;
    private const int CardWidth = 352;
    private const int CardHeight = 134;
    private const int PanelWidth = 420;
    private const int PanelHeight = 390;
    private static readonly PlayerClass[] Jobs = Enum.GetValues<PlayerClass>().Where(static job => Simulation.Characters.JobBits.Of(job) != 0).ToArray();
    private static readonly string[] Sorts = ["Newest", "Price: low", "Price: high", "Level", "Name"];

    private enum View
    {
        Browse,
        Mine
    }

    // A card with its texts, made when its page arrives so drawing allocates nothing.
    private sealed record Shown(CharacterMarketRow Row, string ClassName, string Initial, string Price, string LevelText, string Experience, string Announced, string State, string SaleLevel, string Refused,
        MarketLook Bust, MarketLook Body);

    private readonly TextInputField _search = new() { MaxLength = CharacterMarketRules.MaxSearchLength, Accepts = static ch => ch >= ' ' };
    private readonly TextInputField _priceField = new() { MaxLength = 10, Accepts = char.IsDigit };
    private readonly HudMarketCombo _job = new(["All", .. Jobs.Select(static job => job.ToString())]);
    private readonly HudMarketCombo _sort = new(Sorts);
    private readonly List<string> _iconKeys = [];
    private readonly List<Shown> _rows = [];
    private readonly List<Shown> _forSale = [];
    private Task<CharacterMarketPagePacket> _loading;
    private Task<CharacterMarketPagePacket> _acting;
    private Task<CharacterMarketPagePacket> _saleList;
    private Task<CharacterDetailView> _detailLoading;
    private CharacterDetailView _detail;
    private OwnerState _owner;
    private readonly Dictionary<OwnedItemView, List<(string Text, Color Color)>> _tips = [];
    private Shown _opened;
    private string _openedNick = "";
    private string _openedClass = "";
    private string _openedLevel = "";
    private string _openedAnnounced = "";
    private string _openedSeller = "";
    private string _openedPrice = "";
    private string _detailStats = "";
    private string _detailMounts = "";
    private readonly List<CharacterGearInfo>[] _gear = [[], [], [], []];
    private string _pageText = "1/1";
    private string _balanceText = "0 coins";
    private string _ruleText = "";
    private CharacterMarketPagePacket _rules;
    private View _view = View.Browse;
    private ulong _npcId;
    private int _page;
    private int _total;
    private int _picked = -1;
    private bool _saleOpen;
    private bool _hideSeller = true;
    private bool _confirming;
    private string _status = "";
    private Color _statusColor = HudMarketFrame.Dim;
    private string _lastSearch = "";

    public bool Open { get; private set; }
    public bool Typing => Open && (_search.Focused || _priceField.Focused);
    public int IconRevision { get; private set; }
    public IEnumerable<string> IconKeys => Open ? _iconKeys : [];
    public Func<CharacterMarketRequestPacket, Task<CharacterMarketPagePacket>> Request { get; set; }
    public Func<CharacterMarketRequestPacket, Task<CharacterDetailView>> DetailRequest { get; set; }

    // The picture of a listed character, or null while the scene makes it.
    public Func<MarketLook, Texture2D> Portrait { get; set; }

    public static Rectangle Window(HudLayout layout) => new((layout.Width - Width) / 2, Math.Max(0, (layout.Height - Height) / 2 - 10), Width, Height);

    private static Rectangle JobBox(Rectangle window) => new(window.X + 24, window.Y + 48, 150, 28);
    private static Rectangle SortBox(Rectangle window) => new(window.X + 182, window.Y + 48, 150, 28);
    private static Rectangle SearchBox(Rectangle window) => new(window.X + 340, window.Y + 48, 130, 28);
    private static Rectangle Mine(Rectangle window) => new(window.X + 478, window.Y + 48, 86, 28);
    private static Rectangle Sale(Rectangle window) => new(window.X + 572, window.Y + 48, 164, 28);
    private static Rectangle Previous(Rectangle window) => new(window.X + 320, window.Bottom - 30, 24, 24);
    private static Rectangle Next(Rectangle window) => new(window.X + 416, window.Bottom - 30, 24, 24);

    private static Rectangle Card(Rectangle window, int index) =>
        new(window.X + 22 + index % 2 * (CardWidth + 8), window.Y + 84 + index / 2 * (CardHeight + 6), CardWidth, CardHeight);

    private static Rectangle PanelBox(Rectangle window) => new(window.X + (Width - PanelWidth) / 2, window.Y + 110, PanelWidth, PanelHeight);
    private static Rectangle PanelClose(Rectangle panel) => new(panel.Right - 28, panel.Y + 6, 22, 22);
    private static Rectangle Action(Rectangle panel) => new(panel.X + 134, panel.Bottom - 38, 150, 28);
    private static Rectangle ReturnLink(Rectangle window) => new(window.X + 18, window.Y + 44, 80, 20);
    private static Rectangle BodyBox(Rectangle window) => new(window.X + 18, window.Y + 68, 252, 470);
    private static Rectangle InfoBox(Rectangle window) => new(window.X + 290, window.Y + 68, 450, 118);
    private static Rectangle GearBox(Rectangle window) => new(window.X + 290, window.Y + 196, 450, 342);
    private static Rectangle BuyButton(Rectangle window) => new(window.X + 560, window.Y + 552, 164, 34);

    private static Rectangle GearSlot(Rectangle box, int column, int row, int index) =>
        new(box.X + 12 + column + index * (GearSlotWidth + 6), box.Y + 30 + row * 160, GearSlotWidth, GearSlotHeight);

    private static Rectangle Accessory(Rectangle box, int index) =>
        new(box.X + 262 + index % AccessoryColumns * (AccessorySize + 4), box.Y + 190 + index / AccessoryColumns * (AccessorySize + 4), AccessorySize, AccessorySize);

    private static Rectangle SaleRow(Rectangle panel, int index) => new(panel.X + 12, panel.Y + 36 + index * 34, PanelWidth - 24, 30);
    private static Rectangle PriceBox(Rectangle panel) => new(panel.X + 100, panel.Bottom - 104, 120, 26);
    private static Rectangle HideBox(Rectangle panel) => new(panel.X + 12, panel.Bottom - 70, 200, 22);

    public void Show(ulong npcId)
    {
        _npcId = npcId;
        Open = true;
        _view = View.Browse;
        _page = 0;
        _status = "";
        ClosePanels();
        Ask();
    }

    public void Hide()
    {
        Open = false;
        CancelTyping();
    }

    public void CancelTyping()
    {
        _search.Focused = false;
        _priceField.Focused = false;
    }

    public void Type(ReadOnlySpan<char> typed, double elapsedSeconds)
    {
        _priceField.Update(typed, elapsedSeconds);
        _search.Update(typed, elapsedSeconds);
        if (_search.Text != _lastSearch)
        {
            _lastSearch = _search.Text;
            _page = 0;
            Ask();
        }
    }

    public bool Captures(HudLayout layout, Point pointer) => Open && Window(layout).Contains(pointer);

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            Move(_page - Math.Sign(wheelDelta));
        }
    }

    public void Update(OwnerState state)
    {
        _owner = state;
        if (_loading is { IsCompleted: true } loading)
        {
            _loading = null;
            ReadPage(loading);
        }

        if (_saleList is { IsCompleted: true } saleList)
        {
            _saleList = null;
            if (saleList.IsCompletedSuccessfully)
            {
                _rules = saleList.Result;
                _ruleText = RuleText(saleList.Result);
                Fill(_forSale, saleList.Result.Rows);
            }
        }

        if (_acting is { IsCompleted: true } acting)
        {
            _acting = null;
            ReadAction(acting);
        }

        if (_detailLoading is { IsCompleted: true } detail)
        {
            _detailLoading = null;
            _detail = detail.IsCompletedSuccessfully ? detail.Result : null;
            _detailStats = _detail is { } read
                ? string.Create(CultureInfo.InvariantCulture, $"STR {read.Strength}  SPI {read.Spirit}  TAL {read.Talent}  AGI {read.Agility}  HEA {read.Health}")
                : "";
            _detailMounts = string.Create(CultureInfo.InvariantCulture, $"Mount: {_detail?.Mounts ?? 0}");
            _iconKeys.Clear();
            _tips.Clear();
            foreach (var group in _gear)
            {
                group.Clear();
            }

            foreach (var gear in _detail?.Gear ?? [])
            {
                _iconKeys.Add(gear.Item.CatalogKey);
                if (gear.Group < _gear.Length)
                {
                    _gear[gear.Group].Add(gear);
                }
            }

            // Held gear shows as weapon, armour, off-hand.
            _gear[0].Sort(static (a, b) => HeldOrder(a.Slot).CompareTo(HeldOrder(b.Slot)));

            IconRevision++;
        }
    }

    public bool Click(HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return false;
        }

        var window = Window(layout);
        if (_opened is not null)
        {
            ClickDetail(window, pointer);
            return window.Contains(pointer);
        }

        if (ClickPanel(window, pointer) || ClickCombos(window, pointer))
        {
            return true;
        }

        if (!window.Contains(pointer))
        {
            return false;
        }

        _search.Focused = SearchBox(window).Contains(pointer);
        if (HudMarketFrame.CloseBox(window).Contains(pointer))
        {
            Hide();
        }
        else if (Mine(window).Contains(pointer))
        {
            _view = _view == View.Browse ? View.Mine : View.Browse;
            _page = 0;
            Ask();
        }
        else if (Sale(window).Contains(pointer))
        {
            OpenSale();
        }
        else if (Previous(window).Contains(pointer))
        {
            Move(_page - 1);
        }
        else if (Next(window).Contains(pointer))
        {
            Move(_page + 1);
        }
        else
        {
            ClickCards(window, pointer);
        }

        return true;
    }

    private bool ClickCombos(Rectangle window, Point pointer)
    {
        if (_job.Click(JobBox(window), pointer, out var jobChanged))
        {
            _sort.Close();
            Refilter(jobChanged);
            return true;
        }

        if (_sort.Click(SortBox(window), pointer, out var sortChanged))
        {
            _job.Close();
            Refilter(sortChanged);
            return true;
        }

        return false;
    }

    private void Refilter(bool changed)
    {
        if (changed)
        {
            _page = 0;
            Ask();
        }
    }

    private void ClickCards(Rectangle window, Point pointer)
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            if (Card(window, index).Contains(pointer))
            {
                _opened = _rows[index];
                var row = _opened.Row;
                _openedNick = "Nick: " + row.Name;
                _openedClass = "Class: " + _opened.ClassName;
                _openedLevel = string.Create(CultureInfo.InvariantCulture, $"Level: {row.Level} ({row.ExpBasisPoints / 100.0:0.00}%)");
                _openedAnnounced = _opened.Announced;
                _openedSeller = row.Seller.Length > 0 ? "Seller: " + row.Seller : "Seller: hidden";
                _openedPrice = string.Create(CultureInfo.InvariantCulture, $"Price: {row.Price:N0} coins");
                _detailMounts = "";
                _confirming = false;
                _detail = null;
                _detailLoading = DetailRequest?.Invoke(Packet(MarketWire.ActionDetail, _opened.Row.Id, 0, ""));
                return;
            }
        }
    }

    private bool ClickPanel(Rectangle window, Point pointer)
    {
        var panel = PanelBox(window);
        if (!_saleOpen)
        {
            return false;
        }

        ClickSale(panel, pointer);
        return true;
    }

    private void ClickDetail(Rectangle window, Point pointer)
    {
        if (HudMarketFrame.CloseBox(window).Contains(pointer))
        {
            ClosePanels();
            Hide();
        }
        else if (ReturnLink(window).Contains(pointer))
        {
            ClosePanels();
        }
        else if (BuyButton(window).Contains(pointer) && _acting is null && _opened?.Row is { } row)
        {
            if (_view == View.Mine)
            {
                _acting = Request?.Invoke(Packet(MarketWire.ActionCancel, row.Id, 0, ""));
            }
            else if (!_confirming)
            {
                _confirming = true;
            }
            else
            {
                _acting = Request?.Invoke(Packet(MarketWire.ActionBuy, row.Id, row.Price, ""));
            }
        }
    }

    private void ClickSale(Rectangle panel, Point pointer)
    {
        _priceField.Focused = PriceBox(panel).Contains(pointer);
        if (PanelClose(panel).Contains(pointer))
        {
            ClosePanels();
            return;
        }

        if (HideBox(panel).Contains(pointer))
        {
            _hideSeller = !_hideSeller;
        }

        for (var index = 0; index < _forSale.Count; index++)
        {
            if (_forSale[index].Row.Blocked == 0 && SaleRow(panel, index).Contains(pointer))
            {
                _picked = index;
            }
        }

        if (Action(panel).Contains(pointer) && _acting is null && _picked >= 0 && long.TryParse(_priceField.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var price))
        {
            _acting = Request?.Invoke(Packet(MarketWire.ActionList, 0, price, _forSale[_picked].Row.Name));
        }
    }

    private void OpenSale()
    {
        ClosePanels();
        _saleOpen = true;
        _forSale.Clear();
        _picked = -1;
        _priceField.Clear();
        _saleList = Request?.Invoke(Packet(MarketWire.ActionForSale, 0, 0, ""));
    }

    private void ClosePanels()
    {
        _opened = null;
        _detail = null;
        _saleOpen = false;
        _confirming = false;
        _priceField.Focused = false;
    }

    public void Draw(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        HudMarketFrame.Window(painter, window, FrameArt, "CharacterMarket", pointer);
        if (_opened is not null)
        {
            DrawDetail(painter, layout, window, icons, pointer);
            return;
        }

        HudMarketFrame.Field(painter, SearchBox(window), _search, "Search...");
        HudMarketFrame.GoldButton(painter, Mine(window), _view == View.Mine ? "Browse" : "My Listings", pointer);
        HudMarketFrame.GoldButton(painter, Sale(window), "List character for sale", pointer);
        for (var index = 0; index < _rows.Count; index++)
        {
            DrawCard(painter, Card(window, index), _rows[index], pointer);
        }

        HudMarketFrame.GoldButton(painter, Previous(window), "<", pointer, _page > 0);
        HudMarketFrame.GoldButton(painter, Next(window), ">", pointer, _page + 1 < Pages);
        painter.TextCentered(_pageText, window.X + 344, window.X + 416, window.Bottom - 25, HudMarketFrame.Text);
        painter.Text(_balanceText, window.X + 24, window.Bottom - 25, HudMarketFrame.GoldText);
        painter.Text(painter.Fit(_status, 260), window.X + 460, window.Bottom - 25, _statusColor);
        _job.Draw(painter, JobBox(window), pointer);
        _sort.Draw(painter, SortBox(window), pointer);
        if (_saleOpen)
        {
            DrawSale(painter, PanelBox(window), pointer);
        }

        _job.DrawList(painter, JobBox(window), pointer);
        _sort.DrawList(painter, SortBox(window), pointer);
    }

    private int Pages => Math.Max(1, (_total + CharacterMarketRules.PageSize - 1) / CharacterMarketRules.PageSize);

    private static string ClassName(int jobCode) => Enum.IsDefined((PlayerClass)jobCode) ? ((PlayerClass)jobCode).ToString() : "?";

    // The character's picture fitted in the box, its proportions kept and standing on the box's floor; its class initial until the picture is made.
    private void DrawPortrait(HudPainter painter, Rectangle box, MarketLook look, string initial)
    {
        if (Portrait?.Invoke(look) is not { } picture)
        {
            painter.TextCentered(initial, box.X, box.Right, box.Y + (box.Height - 14) / 2, HudMarketFrame.GoldText);
            return;
        }

        var scale = Math.Min(box.Width / (float)picture.Width, box.Height / (float)picture.Height);
        var width = (int)(picture.Width * scale);
        var height = (int)(picture.Height * scale);
        painter.Image(picture, new Rectangle(box.X + (box.Width - width) / 2, box.Bottom - height, width, height));
    }

    // The face: the head of the waist-up picture.
    private void DrawFace(HudPainter painter, Rectangle box, MarketLook look, string initial)
    {
        HudMarketFrame.Box(painter, box, HudMarketFrame.Slot);
        var inner = new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2);
        if (Portrait?.Invoke(look) is not { } picture)
        {
            painter.TextCentered(initial, box.X, box.Right, box.Y + (box.Height - 14) / 2, HudMarketFrame.GoldText);
            return;
        }

        var side = picture.Width * 2 / 3;
        painter.Image(picture, inner, new Rectangle((picture.Width - side) / 2, 0, side, side));
    }

    private static void DrawCoin(HudPainter painter, int x, int y)
    {
        if (painter.Texture(CoinArt) is not null)
        {
            painter.Sprite(CoinArt, x, y);
        }
        else
        {
            painter.Fill(new Rectangle(x, y + 2, 12, 12), HudMarketFrame.GoldText);
        }
    }

    private void DrawCard(HudPainter painter, Rectangle card, Shown shown, Point pointer)
    {
        HudMarketFrame.Box(painter, card, card.Contains(pointer) ? HudMarketFrame.Back : HudMarketFrame.Panel);
        DrawFace(painter, new Rectangle(card.X + 12, card.Y + 12, 44, 44), shown.Bust, shown.Initial);
        DrawPortrait(painter, new Rectangle(card.Right - 128, card.Y + 4, 120, CardHeight - 8), shown.Bust, shown.Initial);
        painter.Text(painter.Fit(shown.Row.Name, 140), card.X + 66, card.Y + 10, HudMarketFrame.Text);
        painter.Text(shown.ClassName, card.X + 66, card.Y + 26, HudMarketFrame.Text);
        DrawCoin(painter, card.X + 66, card.Y + 42);
        painter.Text(shown.Price, card.X + 84, card.Y + 42, HudMarketFrame.GoldText);
        painter.Text(shown.LevelText, card.X + 14, card.Y + 66, HudMarketFrame.Text);
        painter.Text(shown.Experience, card.X + 14, card.Y + 82, HudMarketFrame.Text);
        painter.Text(shown.Announced, card.X + 14, card.Y + 98, HudMarketFrame.Text);
        if (_view == View.Mine)
        {
            painter.Text(shown.State, card.X + 14, card.Y + 114, HudMarketFrame.Dim);
        }
    }

    private static string StateText(CharacterMarketRow row) => row.State switch
    {
        0 => string.Create(CultureInfo.InvariantCulture, $"Active, {row.ExpiresDays} days left"),
        1 => "Sold",
        2 => "Cancelled",
        3 => "Expired",
        _ => "Removed"
    };

    private void DrawDetail(HudPainter painter, HudLayout layout, Rectangle window, HudItemIcons icons, Point pointer)
    {
        var row = _opened.Row;
        var back = ReturnLink(window);
        painter.Text("< Return", back.X, back.Y + 2, back.Contains(pointer) ? HudMarketFrame.GoldText : HudMarketFrame.Text);
        var body = BodyBox(window);
        HudMarketFrame.Box(painter, body, HudMarketFrame.Panel);
        DrawPortrait(painter, new Rectangle(body.X + 4, body.Y + 4, body.Width - 8, body.Height - 8), _opened.Body, _opened.Initial);

        var info = InfoBox(window);
        HudMarketFrame.Box(painter, info, HudMarketFrame.Panel);
        painter.Text(_openedNick, info.X + 14, info.Y + 10, HudMarketFrame.Text);
        painter.Text(_openedClass, info.X + 14, info.Y + 30, HudMarketFrame.Text);
        painter.Text(_openedLevel, info.X + 14, info.Y + 50, HudMarketFrame.Text);
        painter.Text(_detailMounts, info.X + 14, info.Y + 70, HudMarketFrame.Text);
        painter.Text(_openedSeller, info.X + 14, info.Y + 90, HudMarketFrame.Dim);
        painter.Text(_openedAnnounced, info.Right - 14 - painter.Measure(_openedAnnounced), info.Y + 90, HudMarketFrame.Text);
        painter.Text(_detailStats, info.Right - 14 - painter.Measure(_detailStats), info.Y + 10, HudMarketFrame.Dim);

        var gear = GearBox(window);
        HudMarketFrame.Box(painter, gear, HudMarketFrame.Panel);
        painter.Text("Equipment 1:", gear.X + 12, gear.Y + 8, HudMarketFrame.Dim);
        painter.Text("Equipment 2:", gear.X + 262, gear.Y + 8, HudMarketFrame.Dim);
        painter.Text("Costumes:", gear.X + 12, gear.Y + 168, HudMarketFrame.Dim);
        painter.Text("Accessories:", gear.X + 262, gear.Y + 168, HudMarketFrame.Dim);
        var held = DrawGearRow(painter, icons, gear, _gear[0], 0, 0, 3, pointer);
        var stored = DrawGearRow(painter, icons, gear, _gear[1], 250, 0, 2, pointer);
        var looks = DrawGearRow(painter, icons, gear, _gear[2], 0, 1, 3, pointer);
        var hovered = held ?? stored ?? looks;
        var accessories = _gear[3];
        for (var index = 0; index < AccessorySlots; index++)
        {
            var cell = Accessory(gear, index);
            HudMarketFrame.Box(painter, cell, HudMarketFrame.Slot);
            if (index < accessories.Count)
            {
                var item = accessories[index].Item;
                var inner = new Rectangle(cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2);
                HudRarityColors.Wash(painter, inner, item.Details?.Rarity ?? ItemRarity.None);
                icons.Draw(painter, item.CatalogKey, new Rectangle(cell.X + 2, cell.Y + 2, cell.Width - 4, cell.Height - 4));
                HudRarityColors.Border(painter, inner, item.Details?.Rarity ?? ItemRarity.None);
                if (cell.Contains(pointer))
                {
                    hovered = accessories[index].Item;
                }
            }
        }

        DrawCoin(painter, window.X + 300, window.Y + 562);
        painter.Text(_openedPrice, window.X + 318, window.Y + 562, HudMarketFrame.Text);
        painter.Text(painter.Fit(_status, 250), window.X + 18, window.Y + 562, _statusColor);
        var label = _view == View.Mine ? "Cancel Listing" : _confirming ? "Confirm Purchase" : "Buy Character";
        HudMarketFrame.TealButton(painter, BuyButton(window), label, pointer, _acting is null && (_view == View.Browse || row.State == 0));
        if (hovered is not null)
        {
            DrawTip(painter, layout, hovered, pointer);
        }
    }

    // The item's own tooltip, as the bag shows it: its lines (made once per piece) in its grade's frame.
    private void DrawTip(HudPainter painter, HudLayout layout, OwnedItemView item, Point pointer)
    {
        if (_owner is null)
        {
            return;
        }

        if (!_tips.TryGetValue(item, out var lines))
        {
            lines = HudItemDetailTooltip.Lines(item, _owner, "", "");
            _tips[item] = lines;
        }

        HudItemDetailTooltip.Draw(painter, layout, pointer, lines, rarity: item.Details?.Rarity ?? ItemRarity.None);
    }

    // One row of tall gear slots holding the group's pieces in order (empty slots stay dark); the piece under the pointer, if any.
    private static OwnedItemView DrawGearRow(HudPainter painter, HudItemIcons icons, Rectangle box, List<CharacterGearInfo> pieces, int column, int row, int slots, Point pointer)
    {
        OwnedItemView hovered = null;
        for (var index = 0; index < slots; index++)
        {
            var slot = GearSlot(box, column, row, index);
            HudMarketFrame.Box(painter, slot, HudMarketFrame.Slot);
            if (index < pieces.Count)
            {
                var rarity = pieces[index].Item.Details?.Rarity ?? ItemRarity.None;
                var inner = new Rectangle(slot.X + 1, slot.Y + 1, slot.Width - 2, slot.Height - 2);
                HudRarityColors.Wash(painter, inner, rarity);
                icons.Draw(painter, pieces[index].Item.CatalogKey, new Rectangle(slot.X + 5, slot.Y + 22, slot.Width - 10, slot.Width - 10));
                HudRarityColors.Border(painter, inner, rarity);
                if (slot.Contains(pointer))
                {
                    hovered = pieces[index].Item;
                }
            }
        }

        return hovered;
    }

    private static int HeldOrder(byte slot) => slot switch
    {
        (byte)EquipmentSlot.Weapon => 0,
        (byte)EquipmentSlot.Armor => 1,
        _ => 2
    };

    private void DrawSale(HudPainter painter, Rectangle panel, Point pointer)
    {
        HudMarketFrame.Box(painter, panel, HudMarketFrame.Back);
        HudMarketFrame.GoldButton(painter, PanelClose(panel), "X", pointer);
        painter.Text("List a character", panel.X + 14, panel.Y + 12, HudMarketFrame.GoldText);
        for (var index = 0; index < _forSale.Count; index++)
        {
            var row = SaleRow(panel, index);
            var entry = _forSale[index].Row;
            HudMarketFrame.Box(painter, row, index == _picked ? HudMarketFrame.PanelLine : HudMarketFrame.Panel);
            painter.Text(painter.Fit(entry.Name, 130), row.X + 8, row.Y + 8, entry.Blocked == 0 ? HudMarketFrame.Text : HudMarketFrame.Dim);
            painter.Text(_forSale[index].SaleLevel, row.X + 144, row.Y + 8, HudMarketFrame.Dim);
            if (entry.Blocked != 0)
            {
                painter.Text(painter.Fit(_forSale[index].Refused, 170), row.X + 214, row.Y + 8, HudMarketFrame.Bad);
            }
        }

        painter.Text("Price:", panel.X + 14, panel.Bottom - 100, HudMarketFrame.Text);
        HudMarketFrame.Field(painter, PriceBox(panel), _priceField, "coins");
        painter.Text(_ruleText, panel.X + 226, panel.Bottom - 100, HudMarketFrame.Dim);

        var hide = HideBox(panel);
        painter.Text(_hideSeller ? "[x] Hide my name" : "[ ] Hide my name", hide.X + 2, hide.Y + 4, HudMarketFrame.Text);
        HudMarketFrame.GoldButton(painter, Action(panel), "List", pointer, _acting is null && _picked >= 0 && _priceField.Text.Length > 0);
    }

    private static string RuleText(CharacterMarketPagePacket rules) =>
        string.Create(CultureInfo.InvariantCulture, $"{rules.MinPrice:N0}-{rules.MaxPrice:N0}, fee {rules.Fee:N0}, tax {rules.TaxPercent}%");

    private static void Fill(List<Shown> shown, IReadOnlyList<CharacterMarketRow> rows)
    {
        shown.Clear();
        foreach (var row in rows)
        {
            var name = ClassName(row.JobCode);
            var bust = new MarketLook((PlayerClass)row.JobCode, row.Tier, row.Look.Face, row.Look.Hair, row.Look.Armor, row.Look.Weapon, row.Look.OffHand, Full: false);
            shown.Add(new Shown(row, name, name[..1], string.Create(CultureInfo.InvariantCulture, $"{row.Price:N0}"), string.Create(CultureInfo.InvariantCulture, $"Level: {row.Level}"),
                string.Create(CultureInfo.InvariantCulture, $"EXP: {row.ExpBasisPoints / 100.0:0.00}%"), string.Create(CultureInfo.InvariantCulture, $"Announced: {row.AnnouncedDays} Days"),
                StateText(row), string.Create(CultureInfo.InvariantCulture, $"Lv {row.Level}"), row.Blocked == 0 ? "" : Refusal((CharacterMarketStatus)row.Blocked),
                bust, bust with { Full = true }));
        }
    }

    private void Move(int page)
    {
        var moved = Math.Clamp(page, 0, Pages - 1);
        if (moved != _page)
        {
            _page = moved;
            Ask();
        }
    }

    private void Ask()
    {
        var action = _view == View.Mine ? MarketWire.ActionMine : MarketWire.ActionBrowse;
        var job = _job.Selected == 0 ? 0 : (int)Jobs[_job.Selected - 1];
        _loading = Request?.Invoke(new CharacterMarketRequestPacket(Guid.NewGuid(), action, _npcId, 0, 0, _page, job, (byte)_sort.Selected, false, _search.Text));
    }

    private CharacterMarketRequestPacket Packet(byte action, long id, long price, string text) => new(Guid.NewGuid(), action, _npcId, id, price, 0, 0, 0, _hideSeller, text);

    private void ReadPage(Task<CharacterMarketPagePacket> loading)
    {
        if (!loading.IsCompletedSuccessfully)
        {
            Say("The market could not be read.", HudMarketFrame.Bad);
            return;
        }

        var page = loading.Result;
        _rules = page;
        _ruleText = RuleText(page);
        if (page.Status != (byte)CharacterMarketStatus.Ok)
        {
            _rows.Clear();
            Say(Refusal((CharacterMarketStatus)page.Status), HudMarketFrame.Bad);
            return;
        }

        Fill(_rows, page.Rows);
        _total = page.Total;
        _balanceText = string.Create(CultureInfo.InvariantCulture, $"{page.Balance:N0} coins");
        _pageText = string.Create(CultureInfo.InvariantCulture, $"{_page + 1}/{Pages}");
    }

    private void ReadAction(Task<CharacterMarketPagePacket> acting)
    {
        var status = acting.IsCompletedSuccessfully ? (CharacterMarketStatus)acting.Result.Status : CharacterMarketStatus.Rejected;
        if (status == CharacterMarketStatus.Ok)
        {
            var bought = acting.Result.Action == MarketWire.ActionBuy;
            Say(bought ? "Bought! The character is on your account." : "Done.", HudMarketFrame.Good);
            ClosePanels();
            Ask();
            return;
        }

        _confirming = false;
        Say(Refusal(status), HudMarketFrame.Bad);
        if (status == CharacterMarketStatus.NotFound)
        {
            ClosePanels();
            Ask();
        }
    }

    private void Say(string text, Color color)
    {
        _status = text;
        _statusColor = color;
    }

    private static string Refusal(CharacterMarketStatus status) => status switch
    {
        CharacterMarketStatus.Disabled => "The Character Market is closed.",
        CharacterMarketStatus.OutOfReach => "Stand by the Character Market.",
        CharacterMarketStatus.NotFound => "That listing is gone.",
        CharacterMarketStatus.NotOwner => "That is not your character.",
        CharacterMarketStatus.LevelTooLow => "Level too low",
        CharacterMarketStatus.PriceOutOfRange => "That price is not allowed.",
        CharacterMarketStatus.TooManyListings => "You have too many listings.",
        CharacterMarketStatus.AlreadyListed => "Already listed",
        CharacterMarketStatus.InPlay => "In play",
        CharacterMarketStatus.InClan => "In a clan",
        CharacterMarketStatus.HasStall => "Has a stall",
        CharacterMarketStatus.NotEnoughCoins => "You do not have enough coins.",
        CharacterMarketStatus.NoFreeSlot => "Your account has no free slot.",
        CharacterMarketStatus.OwnListing => "That is your own listing.",
        CharacterMarketStatus.PriceChanged => "The price changed; look again.",
        CharacterMarketStatus.Busy => "Try again in a moment.",
        CharacterMarketStatus.StorageFailed => "Storage failed; try again.",
        _ => "Refused."
    };
}
