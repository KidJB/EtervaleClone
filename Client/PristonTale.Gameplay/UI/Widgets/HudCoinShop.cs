using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Shops;

namespace PristonTale.Gameplay.UI.Widgets;

// The coin shop, drawn on the item shop frame of Hud/CoinShop (560x450): the tabs as their category art down the left
// panel, the account's coins by the coin pile under it, the shop's message across the top right, and the open tab's
// articles as cards three at a time on the right (the wheel and the scroll bar move them). A card shows the item's icon,
// name, description and price (a discounted one beside the struck-through price), a buy button and, for a quantity
// article, a counter, for a spec article, the class it will be made for. Requests go through Open and Buy and are read
// back without waiting; K or the shop button under the quest book's opens and closes it, Escape closes it.
public sealed class HudCoinShop
{
    private const string Folder = "Hud/CoinShop/";
    private const string Background = Folder + "background.png";
    private const string Card = Folder + "bgitem.png";
    private const string CardHover = Folder + "bgitem_.png";
    private const string BuyArt = Folder + "btnbuy.png";
    private const string BuyHoverArt = Folder + "btnbuy_.png";
    private const string CloseArt = Folder + "close.png";
    private const string CloseHoverArt = Folder + "close_.png";
    private const string CounterArt = Folder + "counter.png";
    private const string UpArt = Folder + "counter_up.png";
    private const string DownArt = Folder + "counter_down.png";
    private const string LeftArt = Folder + "left.png";
    private const string RightArt = Folder + "right.png";
    private const string ScrollArt = Folder + "scrollbar.png";
    public static readonly string[] Art = [Background, Card, CardHover, BuyArt, BuyHoverArt, CloseArt, CloseHoverArt, CounterArt, UpArt, DownArt, LeftArt,
        RightArt, ScrollArt];

    // The frame's art is 560x450; every place below is in its pixels.
    private const int Width = 560;
    private const int Height = 450;
    private static readonly Rectangle TabPanel = new(24, 50, 156, 238);
    private static readonly Rectangle MessageBar = new(192, 50, 346, 26);
    private static readonly Rectangle CardPanel = new(192, 88, 346, 336);
    private static readonly Rectangle CloseButton = new(470, 428, 80, 24);
    private const int TabRow = 20;
    private const int CardStep = 106;
    private const int VisibleCards = 3;
    private const int BalanceY = 396;
    private const int MaxDescriptionLines = 2;

    private static readonly Color Gold = new(255, 215, 110);
    private static readonly Color TextColor = new(235, 230, 220);
    private static readonly Color DimText = new(170, 165, 155);
    private static readonly Color PriceColor = new(255, 220, 120);
    private static readonly Color OldPriceColor = new(150, 140, 130);
    private static readonly Color GoodColor = new(140, 230, 140);
    private static readonly Color BadColor = new(255, 120, 110);

    private readonly List<(string Text, string Old, List<string> Lines)> _texts = [];
    // Each tab's category art, plain and lit, made when the catalog arrives.
    private (string Plain, string Lit)[] _tabArt = [];
    private Task<CoinShopView> _opening;
    private Task<(CoinShopStatus Status, long Balance)> _buying;
    private CoinShopCatalog _catalog = CoinShopCatalog.Empty;
    private long _balance;
    private string _balanceText = "0";
    private string _status = "";
    private Color _statusColor = TextColor;
    private int _tab;
    private int _scroll;
    private int _textsFor = -1;
    private readonly Dictionary<int, int> _quantities = new();
    private readonly Dictionary<int, string> _quantityTexts = new();
    private PlayerClass _specClass = PlayerClass.Fighter;
    private string _specText = "";

    public bool Open { get; private set; }

    public Func<Task<CoinShopView>> OpenRequest { get; set; }
    public Func<int, int, int, int, Task<(CoinShopStatus Status, long Balance)>> BuyRequest { get; set; }

    // The class a spec article is made for, starting at the player's own.
    public PlayerClass OwnClass
    {
        set
        {
            if (_specText.Length == 0)
            {
                SetSpec(value);
            }
        }
    }

    public void Toggle()
    {
        if (Open)
        {
            Close();
            return;
        }

        Open = true;
        _status = "Loading...";
        _statusColor = DimText;
        _opening = OpenRequest?.Invoke();
    }

    public void Close() => Open = false;

    public static Rectangle Button(Rectangle minimapArea)
    {
        var quest = Quests.HudQuestBook.Button(minimapArea);
        return new Rectangle(quest.X, quest.Bottom + 2, quest.Width, quest.Height);
    }

    public static Rectangle Window(HudLayout layout) =>
        new(Math.Max(0, (layout.Width - Width) / 2), Math.Max(0, (layout.Height - Height) / 2 - 30), Width, Height);

    private static Rectangle At(Rectangle window, Rectangle place) => new(window.X + place.X, window.Y + place.Y, place.Width, place.Height);

    private static Rectangle TabAt(Rectangle window, int index) =>
        new(window.X + TabPanel.X, window.Y + TabPanel.Y + index * TabRow, TabPanel.Width, TabRow - 2);

    private static Rectangle CardAt(Rectangle window, int visible) =>
        new(window.X + CardPanel.X + 6, window.Y + CardPanel.Y + 8 + visible * CardStep, 324, 90);

    private static Rectangle BuyAt(Rectangle card) => new(card.Right - 82, card.Bottom - 32, 75, 26);

    private static Rectangle CounterAt(Rectangle card) => new(card.Right - 82 - 4 - 80, card.Bottom - 32, 80, 26);

    private static Rectangle DownAt(Rectangle card) => new(CounterAt(card).X, card.Bottom - 32, 19, 26);

    private static Rectangle UpAt(Rectangle card) => new(CounterAt(card).Right - 19, card.Bottom - 32, 19, 26);

    private static Rectangle SpecLeftAt(Rectangle card) => new(card.Right - 160, card.Y + 8, 12, 12);

    private static Rectangle SpecRightAt(Rectangle card) => new(card.Right - 20, card.Y + 8, 12, 12);

    private IReadOnlyList<CoinShopItem> Articles => _tab < _catalog.Tabs.Count ? _catalog.Tabs[_tab].Items : [];

    private int MaxScroll => Math.Max(0, Articles.Count - VisibleCards);

    public bool Captures(HudLayout layout, Rectangle minimapArea, Point pointer, bool minimapShown) =>
        minimapShown && Button(minimapArea).Contains(pointer) || Open && Window(layout).Contains(pointer);

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            _scroll = Math.Clamp(_scroll - Math.Sign(wheelDelta), 0, MaxScroll);
        }
    }

    // Reads back the open and buy requests once they finish; never waits on them.
    public void Update(HudItemIcons icons, HudSprites sprites)
    {
        if (_opening is { IsCompleted: true } opening)
        {
            _opening = null;
            if (opening.IsCompletedSuccessfully && opening.Result is { } view)
            {
                _catalog = view.Catalog;
                SetBalance(view.Balance);
                _tab = Math.Min(_tab, Math.Max(0, _catalog.Tabs.Count - 1));
                _scroll = 0;
                _textsFor = -1;
                _status = _catalog.Tabs.Count == 0 ? "The shop has nothing for sale." : _catalog.Message;
                _statusColor = TextColor;
                _tabArt = new (string, string)[_catalog.Tabs.Count];
                for (var index = 0; index < _tabArt.Length; index++)
                {
                    var tab = _catalog.Tabs[index];
                    _tabArt[index] = (Folder + "categories/" + tab.Icon + ".png", Folder + "categories/" + tab.Icon + "_.png");
                    sprites.Request([_tabArt[index].Plain, _tabArt[index].Lit]);
                    icons.Request(tab.Items.Select(static item => item.ItemKey));
                }
            }
            else
            {
                _status = "The shop could not be opened.";
                _statusColor = BadColor;
            }
        }

        if (_buying is { IsCompleted: true } buying)
        {
            _buying = null;
            var (status, balance) = buying.IsCompletedSuccessfully ? buying.Result : (CoinShopStatus.Rejected, _balance);
            SetBalance(balance);
            (_status, _statusColor) = status switch
            {
                CoinShopStatus.Bought => ("Bought! The item is in your bag.", GoodColor),
                CoinShopStatus.NotEnoughCoins => ("You do not have enough coins.", BadColor),
                CoinShopStatus.NoRoom => ("Your bag has no room for it.", BadColor),
                CoinShopStatus.Busy => ("The server is busy; try again.", BadColor),
                CoinShopStatus.UnknownItem => ("That article is no longer sold.", BadColor),
                _ => ("The purchase was refused.", BadColor)
            };
        }
    }

    public bool Click(HudLayout layout, Rectangle minimapArea, Point pointer, bool minimapShown)
    {
        if (minimapShown && Button(minimapArea).Contains(pointer))
        {
            Toggle();
            return true;
        }

        var window = Window(layout);
        if (!Open || !window.Contains(pointer))
        {
            return false;
        }

        if (At(window, CloseButton).Contains(pointer))
        {
            Close();
            return true;
        }

        for (var index = 0; index < _catalog.Tabs.Count; index++)
        {
            if (TabAt(window, index).Contains(pointer))
            {
                _tab = index;
                _scroll = 0;
                _textsFor = -1;
                return true;
            }
        }

        var articles = Articles;
        for (var visible = 0; visible < VisibleCards && _scroll + visible < articles.Count; visible++)
        {
            ClickCard(CardAt(window, visible), articles[_scroll + visible], pointer);
        }

        return true;
    }

    private void ClickCard(Rectangle card, CoinShopItem item, Point pointer)
    {
        if (item.Quantity && DownAt(card).Contains(pointer))
        {
            SetQuantity(item, QuantityOf(item) - 1);
        }
        else if (item.Quantity && UpAt(card).Contains(pointer))
        {
            SetQuantity(item, QuantityOf(item) + 1);
        }
        else if (item.Spec && SpecLeftAt(card).Contains(pointer))
        {
            SetSpec(StepClass(-1));
        }
        else if (item.Spec && SpecRightAt(card).Contains(pointer))
        {
            SetSpec(StepClass(1));
        }
        else if (BuyAt(card).Contains(pointer) && _buying is null && BuyRequest is not null)
        {
            _status = "Buying...";
            _statusColor = DimText;
            _buying = BuyRequest(_catalog.Tabs[_tab].Id, item.Id, QuantityOf(item), item.Spec ? (int)_specClass : 0);
        }
    }

    public static void DrawButton(HudPainter painter, Rectangle minimapArea, Point pointer, bool open)
    {
        var button = Button(minimapArea);
        painter.ColorBox(button, 0, 0, 0, open || button.Contains(pointer) ? 200 : 140);
        painter.TextCentered("$", button.X, button.Right, button.Y - 1, Gold);
    }

    public void Draw(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        if (painter.Texture(Background) is null)
        {
            painter.ColorBox(window, 24, 22, 20, 235);
        }

        painter.Sprite(Background, window.X, window.Y);
        DrawTabs(painter, window, pointer);
        var bar = At(window, MessageBar);
        painter.TextCentered(painter.Fit(_status, bar.Width - 12), bar.X, bar.Right, bar.Y + 5, _statusColor);
        painter.Text("Coins", window.X + TabPanel.X + 4, window.Y + BalanceY, DimText);
        painter.Text(_balanceText, window.X + TabPanel.X + 4, window.Y + BalanceY + 16, Gold);
        DrawCards(painter, window, icons, pointer);
        var close = At(window, CloseButton);
        painter.Sprite(close.Contains(pointer) ? CloseHoverArt : CloseArt, close.X, close.Y);
    }

    private void DrawTabs(HudPainter painter, Rectangle window, Point pointer)
    {
        for (var index = 0; index < _catalog.Tabs.Count; index++)
        {
            var tab = _catalog.Tabs[index];
            var row = TabAt(window, index);
            var lit = index == _tab || row.Contains(pointer);
            if (lit)
            {
                painter.ColorBox(row, 120, 100, 60, index == _tab ? 110 : 60);
            }

            var art = lit ? _tabArt[index].Lit : _tabArt[index].Plain;
            if (painter.Texture(art) is { } texture)
            {
                painter.Sprite(art, row.X + (row.Width - texture.Width) / 2, row.Y + (row.Height - texture.Height) / 2);
            }
            else
            {
                painter.TextCentered(painter.Fit(tab.Name, row.Width - 8), row.X, row.Right, row.Y + 1, lit ? Gold : TextColor);
            }
        }
    }

    private void DrawCards(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        var articles = Articles;
        if (_textsFor != _tab)
        {
            BuildTexts(painter, articles);
        }

        for (var visible = 0; visible < VisibleCards && _scroll + visible < articles.Count; visible++)
        {
            var index = _scroll + visible;
            var item = articles[index];
            var card = CardAt(window, visible);
            painter.Sprite(card.Contains(pointer) ? CardHover : Card, card.X, card.Y, new Color(70, 64, 58));
            icons.Draw(painter, item.ItemKey, new Rectangle(card.X + 10, card.Y + 14, 44, 44));
            painter.Text(painter.Fit(item.Name, 180), card.X + 64, card.Y + 6, Gold);
            var lines = _texts[index].Lines;
            for (var line = 0; line < lines.Count; line++)
            {
                painter.Text(lines[line], card.X + 64, card.Y + 24 + line * 14, DimText);
            }

            var (price, old, _) = _texts[index];
            painter.Text(price, card.X + 10, card.Bottom - 26, PriceColor);
            if (old.Length > 0)
            {
                var oldX = card.X + 14 + painter.Measure(price);
                painter.Text(old, oldX, card.Bottom - 26, OldPriceColor);
                painter.Fill(new Rectangle(oldX, card.Bottom - 19, painter.Measure(old), 1), OldPriceColor);
            }

            if (item.Spec)
            {
                painter.Sprite(LeftArt, SpecLeftAt(card).X, SpecLeftAt(card).Y);
                painter.Sprite(RightArt, SpecRightAt(card).X, SpecRightAt(card).Y);
                painter.TextCentered(_specText, SpecLeftAt(card).Right, SpecRightAt(card).X, card.Y + 5, TextColor);
            }

            if (item.Quantity)
            {
                var counter = CounterAt(card);
                painter.Sprite(CounterArt, counter.X, counter.Y);
                painter.Sprite(DownArt, DownAt(card).X, DownAt(card).Y);
                painter.Sprite(UpArt, UpAt(card).X, UpAt(card).Y);
                painter.TextCentered(QuantityText(item), DownAt(card).Right, UpAt(card).X, counter.Y + 5, TextColor);
            }

            var buy = BuyAt(card);
            painter.Sprite(buy.Contains(pointer) && _buying is null ? BuyHoverArt : BuyArt, buy.X, buy.Y);
        }

        if (MaxScroll > 0)
        {
            var track = new Rectangle(window.X + CardPanel.Right - 8, window.Y + CardPanel.Y + 4, 6, CardPanel.Height - 8);
            painter.Stretch(ScrollArt, track);
            var thumb = track.Y + (track.Height - 20) * _scroll / MaxScroll;
            painter.ColorBox(new Rectangle(track.X - 1, thumb, 8, 20), 200, 170, 110, 230);
        }
    }

    // The price, struck price and wrapped description of each article of the open tab, made when the tab changes.
    private void BuildTexts(HudPainter painter, IReadOnlyList<CoinShopItem> articles)
    {
        _texts.Clear();
        foreach (var item in articles)
        {
            _texts.Add((PriceText(item, QuantityOf(item)), item.Discount > 0 ? item.Price.ToString(CultureInfo.InvariantCulture) : "",
                HudChatText.Wrap(item.Description, 250, MaxDescriptionLines, 120, painter.Measure)));
        }

        _textsFor = _tab;
    }

    private static string PriceText(CoinShopItem item, int quantity) =>
        string.Create(CultureInfo.InvariantCulture, $"{CoinShopPricing.Total(item, quantity):N0} coins");

    private int QuantityOf(CoinShopItem item) => item.Quantity ? _quantities.GetValueOrDefault(item.Id, 1) : 1;

    private string QuantityText(CoinShopItem item) =>
        _quantityTexts.TryGetValue(item.Id, out var text) ? text : "1";

    private void SetQuantity(CoinShopItem item, int quantity)
    {
        var clamped = Math.Clamp(quantity, 1, CoinShopPricing.MaxQuantity);
        _quantities[item.Id] = clamped;
        _quantityTexts[item.Id] = clamped.ToString(CultureInfo.InvariantCulture);
        _textsFor = -1;
    }

    private void SetBalance(long balance)
    {
        _balance = balance;
        _balanceText = balance.ToString("N0", CultureInfo.InvariantCulture);
    }

    private PlayerClass StepClass(int step)
    {
        var classes = Enum.GetValues<PlayerClass>();
        var index = Array.IndexOf(classes, _specClass);
        for (var tries = 0; tries < classes.Length; tries++)
        {
            index = (index + step + classes.Length) % classes.Length;
            if (Simulation.Characters.JobBits.Of(classes[index]) != 0)
            {
                return classes[index];
            }
        }

        return _specClass;
    }

    private void SetSpec(PlayerClass playerClass)
    {
        _specClass = playerClass;
        _specText = playerClass + " Spec.";
    }
}
