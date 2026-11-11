using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Markets;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Markets;

// The Marketplace NPC's window: the filters and categories down the left, six offers of the open personal shops and stalls on the right. Every change asks the server for the page; a page
// is what stands at that moment, and a purchase asks again after it.
public sealed class HudMarketplace
{
    private const int Width = 720;
    private const int Height = 517;
    private const string FrameArt = "Hud/Marketplace/frame.png";
    public static readonly string[] Art = [FrameArt];
    private const int CardWidth = 246;
    private const int CardHeight = 150;
    private const int CardGap = 4;
    private const int RowHeight = 34;
    private const int SubRowHeight = 22;
    private static readonly string[] Classes = ["All Spec", .. Enum.GetValues<PlayerClass>().Where(static job => Simulation.Characters.JobBits.Of(job) != 0).Select(static job => job.ToString())];
    private static readonly (string Name, long Min, long Max)[] Prices =
        [("All Prices", 0, 0), ("Under 10k", 0, 10_000), ("10k - 100k", 10_000, 100_000), ("100k - 1M", 100_000, 1_000_000), ("Over 1M", 1_000_000, 0)];
    private static readonly string[] Rarities = ["All Attribute", .. ItemRarities.Names.Skip(1)];
    private static readonly string[] Sorts = ["Newest", "Price: low", "Price: high", "Name"];
    private static readonly MarketplaceCategory[] Categories =
        [MarketplaceCategory.Defense, MarketplaceCategory.Weapons, MarketplaceCategory.Accessory, MarketplaceCategory.Craft, MarketplaceCategory.Miscellaneous, MarketplaceCategory.Costumes];

    // An offer with its texts, made when its page arrives so drawing allocates nothing.
    private sealed record Shown(MarketplaceOfferInfo Offer, string Title, string Price, string Grade);

    private static readonly string[] CategoryNames = Categories.Select(static category => category.ToString()).ToArray();

    private readonly TextInputField _search = new() { MaxLength = MarketplaceRules.MaxSearchLength, Accepts = static ch => ch >= ' ' };
    private readonly HudMarketCombo _class = new(Classes);
    private readonly HudMarketCombo _price = new(Prices.Select(static price => price.Name).ToArray());
    private readonly HudMarketCombo _rarity = new(Rarities);
    private readonly HudMarketCombo _sort = new(Sorts);
    private readonly List<string> _iconKeys = [];
    private readonly Dictionary<OwnedItemView, List<(string Text, Color Color)>> _tips = [];
    private readonly List<Shown> _offers = [];
    private Task<MarketplaceView> _loading;
    private Task<PersonalShopReply> _buying;
    private MarketplaceCategory _category = MarketplaceCategory.All;
    private int _subcategory;
    private int _page;
    private int _pages = 1;
    private string _pageText = "1/1";
    private string _status = "";
    private Color _statusColor = HudMarketFrame.Dim;
    private ulong _npcId;
    private string _lastSearch = "";

    public bool Open { get; private set; }
    public bool Searching => Open && _search.Focused;
    public int IconRevision { get; private set; }
    public IEnumerable<string> IconKeys => Open ? _iconKeys : [];
    public Func<MarketplaceQueryPacket, Task<MarketplaceView>> QueryRequest { get; set; }
    public Func<ulong, PersonalShopPurchase, Task<PersonalShopReply>> BuyRequest { get; set; }

    public static Rectangle Window(HudLayout layout) => new((layout.Width - Width) / 2, Math.Max(0, (layout.Height - Height) / 2 - 20), Width, Height);

    private static Rectangle SearchBox(Rectangle window) => new(window.Right - 200, window.Y + 8, 160, 22);
    private static Rectangle ComboBox(Rectangle window, int index) => new(window.X + 10, window.Y + 48 + index * 34, 180, 28);
    private static Rectangle SortBox(Rectangle window) => ComboBox(window, 3);
    private static Rectangle Previous(Rectangle window) => new(window.X + 44, window.Bottom - 34, 24, 24);
    private static Rectangle Next(Rectangle window) => new(window.X + 138, window.Bottom - 34, 24, 24);
    private static Rectangle Refresh(Rectangle window) => new(window.X + 10, window.Bottom - 34, 30, 24);

    private static Rectangle Card(Rectangle window, int index) =>
        new(window.X + 210 + index % 2 * (CardWidth + CardGap), window.Y + 48 + index / 2 * (CardHeight + CardGap), CardWidth, CardHeight);

    private static Rectangle Buy(Rectangle card) => new(card.X + 92, card.Y + 92, 86, 28);

    public void Show(ulong npcId)
    {
        _npcId = npcId;
        Open = true;
        _status = "";
        _page = 0;
        Ask();
    }

    public void Hide()
    {
        Open = false;
        _search.Focused = false;
    }

    public void CancelSearch() => _search.Focused = false;

    public void Type(ReadOnlySpan<char> typed, double elapsedSeconds)
    {
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
            Page(_page - Math.Sign(wheelDelta));
        }
    }

    public void Update(OwnerState state)
    {
        if (_loading is { IsCompleted: true } loading)
        {
            _loading = null;
            ReadPage(loading);
        }

        if (_buying is { IsCompleted: true } buying)
        {
            _buying = null;
            ReadPurchase(buying);
            Ask();
        }

        _state = state;
    }

    private OwnerState _state;

    public bool Click(HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return false;
        }

        var window = Window(layout);
        if (ClickCombos(window, pointer))
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
            return true;
        }

        if (Refresh(window).Contains(pointer))
        {
            Ask();
        }
        else if (Previous(window).Contains(pointer))
        {
            Page(_page - 1);
        }
        else if (Next(window).Contains(pointer))
        {
            Page(_page + 1);
        }
        else
        {
            ClickRows(window, pointer);
            ClickCards(window, pointer);
        }

        return true;
    }

    private bool ClickCombos(Rectangle window, Point pointer)
    {
        var moved = false;
        var combos = (ReadOnlySpan<HudMarketCombo>)[_class, _price, _rarity, _sort];
        for (var index = 0; index < combos.Length; index++)
        {
            if (combos[index].Click(ComboBox(window, index), pointer, out var changed))
            {
                moved = true;
                for (var other = 0; other < combos.Length; other++)
                {
                    if (other != index)
                    {
                        combos[other].Close();
                    }
                }

                if (changed)
                {
                    _page = 0;
                    Ask();
                }

                break;
            }
        }

        return moved;
    }

    private void ClickRows(Rectangle window, Point pointer)
    {
        var y = window.Y + 48 + 4 * 34;
        foreach (var category in Categories)
        {
            if (new Rectangle(window.X + 10, y, 180, RowHeight - 4).Contains(pointer))
            {
                _category = _category == category ? MarketplaceCategory.All : category;
                _subcategory = 0;
                _page = 0;
                Ask();
                return;
            }

            y += RowHeight;
            if (_category != category)
            {
                continue;
            }

            var entries = MarketplaceRules.Subcategories(category);
            for (var sub = 1; sub <= entries.Count; sub++)
            {
                if (new Rectangle(window.X + 24, y, 166, SubRowHeight).Contains(pointer))
                {
                    _subcategory = _subcategory == sub ? 0 : sub;
                    _page = 0;
                    Ask();
                    return;
                }

                y += SubRowHeight;
            }
        }
    }

    private void ClickCards(Rectangle window, Point pointer)
    {
        for (var index = 0; index < _offers.Count; index++)
        {
            var offer = _offers[index].Offer;
            if (!offer.Own && offer.Currency != PersonalShopCurrency.ViewOnly && Buy(Card(window, index)).Contains(pointer) && _buying is null && BuyRequest is not null && _state is not null)
            {
                _status = "Buying...";
                _statusColor = HudMarketFrame.Dim;
                _buying = BuyRequest(_npcId, new PersonalShopPurchase(Guid.NewGuid(), offer.SellerId, offer.Item.InstanceId, offer.UnitPrice, Math.Max(1, offer.Item.Count), _state.InventoryRevision));
                return;
            }
        }
    }

    public void Draw(HudPainter painter, HudLayout layout, HudItemIcons icons, OwnerState state, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        HudMarketFrame.Window(painter, window, FrameArt, "Marketplace", pointer);
        HudMarketFrame.Field(painter, SearchBox(window), _search, "Search... (@seller)");
        DrawLeft(painter, window, pointer);
        var hovered = DrawCards(painter, window, icons, pointer);
        painter.Text(painter.Fit(_status, 460), window.X + 210, window.Bottom - 26, _statusColor);
        DrawCombos(painter, window, pointer);
        if (hovered is not null && state is not null)
        {
            DrawTip(painter, layout, hovered, state, pointer);
        }
    }

    private void DrawLeft(HudPainter painter, Rectangle window, Point pointer)
    {
        var y = window.Y + 48 + 4 * 34;
        for (var line = 0; line < Categories.Length; line++)
        {
            var category = Categories[line];
            var row = new Rectangle(window.X + 10, y, 180, RowHeight - 4);
            HudMarketFrame.DropBox(painter, row, CategoryNames[line], _category == category || row.Contains(pointer), arrow: false);
            painter.Text(_category == category ? "v" : ">", row.X + 6, row.Y + 7, HudMarketFrame.Dim);
            y += RowHeight;
            if (_category != category)
            {
                continue;
            }

            var entries = MarketplaceRules.Subcategories(category);
            for (var sub = 1; sub <= entries.Count; sub++)
            {
                painter.Text(entries[sub - 1].Name, window.X + 30, y + 3, _subcategory == sub ? HudMarketFrame.GoldText : HudMarketFrame.Text);
                y += SubRowHeight;
            }
        }

        HudMarketFrame.GoldButton(painter, Refresh(window), "R", pointer);
        HudMarketFrame.GoldButton(painter, Previous(window), "<", pointer, _page > 0);
        HudMarketFrame.GoldButton(painter, Next(window), ">", pointer, _page + 1 < _pages);
        painter.TextCentered(_pageText, window.X + 68, window.X + 138, window.Bottom - 29, HudMarketFrame.Text);
    }

    private void DrawCombos(HudPainter painter, Rectangle window, Point pointer)
    {
        var combos = (ReadOnlySpan<HudMarketCombo>)[_class, _price, _rarity, _sort];
        for (var index = 0; index < combos.Length; index++)
        {
            combos[index].Draw(painter, ComboBox(window, index), pointer);
        }

        for (var index = 0; index < combos.Length; index++)
        {
            combos[index].DrawList(painter, ComboBox(window, index), pointer);
        }
    }

    private MarketplaceOfferInfo DrawCards(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        MarketplaceOfferInfo hovered = null;
        for (var index = 0; index < MarketplaceRules.PageSize; index++)
        {
            var card = Card(window, index);
            HudMarketFrame.Box(painter, card, HudMarketFrame.Panel);
            if (index >= _offers.Count)
            {
                continue;
            }

            var shown = _offers[index];
            var offer = shown.Offer;
            var slot = new Rectangle(card.X + 8, card.Y + 8, 76, 104);
            HudMarketFrame.Box(painter, slot, HudMarketFrame.Slot);
            var rarity = offer.Item.Details?.Rarity ?? ItemRarity.None;
            var inner = new Rectangle(slot.X + 1, slot.Y + 1, slot.Width - 2, slot.Height - 2);
            HudRarityColors.Wash(painter, inner, rarity);
            icons.Draw(painter, offer.Item.CatalogKey, new Rectangle(slot.X + 14, slot.Y + 28, 48, 48));
            HudRarityColors.Border(painter, inner, rarity);
            if (slot.Contains(pointer))
            {
                hovered = offer;
            }

            painter.Text(painter.Fit(shown.Title, 150), card.X + 92, card.Y + 12, HudMarketFrame.GoldText);
            if (offer.Own)
            {
                painter.Text("your offer", card.X + 92, card.Y + 44, HudMarketFrame.Coin);
            }
            else
            {
                painter.Text(shown.Price, card.X + 92, card.Y + 44, HudMarketFrame.Coin);
                painter.Text(shown.Grade, card.X + 92, card.Y + 64, HudMarketFrame.Dim);
                if (offer.Currency != PersonalShopCurrency.ViewOnly)
                {
                    HudMarketFrame.GoldButton(painter, Buy(card), "Purchase", pointer, _buying is null);
                }
            }

            painter.Text("Seller:", card.X + 8, card.Y + 116, HudMarketFrame.Dim);
            painter.Text(painter.Fit(offer.Seller, 90), card.X + 8, card.Y + 130, HudMarketFrame.Text);
            painter.Text(painter.Fit(offer.Location, 130), card.X + 108, card.Y + 130, HudMarketFrame.Dim);
        }

        return hovered;
    }

    // The required level and the rarity, as a card's second line.
    private static string Grade(MarketplaceOfferInfo offer)
    {
        if (offer.Item.Details is not { } details)
        {
            return "";
        }

        var rarity = ItemRarities.Names[(int)details.Rarity];
        return details.Roll.RequiredLevel > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Lv {details.Roll.RequiredLevel} {rarity}")
            : rarity;
    }

    // The whole stack's price in its currency, as the card shows it.
    private static string PriceText(MarketplaceOfferInfo offer) => offer.Currency switch
    {
        PersonalShopCurrency.ViewOnly => "viewonly.",
        PersonalShopCurrency.Coins => string.Create(CultureInfo.InvariantCulture, $"{offer.UnitPrice * Math.Max(1, offer.Item.Count):N0} coins."),
        _ => string.Create(CultureInfo.InvariantCulture, $"{offer.UnitPrice * Math.Max(1, offer.Item.Count):N0} gold.")
    };

    private static string Title(MarketplaceOfferInfo offer) => offer.Item.Count > 1
        ? string.Create(CultureInfo.InvariantCulture, $"{offer.Item.Name} x{offer.Item.Count}")
        : offer.Item.Name;

    private void DrawTip(HudPainter painter, HudLayout layout, MarketplaceOfferInfo offer, OwnerState state, Point pointer)
    {
        if (!_tips.TryGetValue(offer.Item, out var lines))
        {
            lines = HudItemDetailTooltip.Lines(offer.Item, state, offer.UnitPrice.ToString(CultureInfo.InvariantCulture), "");
            _tips[offer.Item] = lines;
        }

        HudItemDetailTooltip.Draw(painter, layout, pointer, lines, rarity: offer.Item.Details?.Rarity ?? ItemRarity.None);
    }

    private void Page(int page)
    {
        var moved = Math.Clamp(page, 0, _pages - 1);
        if (moved != _page)
        {
            _page = moved;
            Ask();
        }
    }

    private void Ask()
    {
        if (QueryRequest is null)
        {
            return;
        }

        var (_, min, max) = Prices[_price.Selected];
        var jobs = Enum.GetValues<PlayerClass>().Where(static job => Simulation.Characters.JobBits.Of(job) != 0).ToArray();
        var job = _class.Selected == 0 ? (byte)0 : (byte)jobs[_class.Selected - 1];
        // "@name" searches sellers, anything else item names.
        var text = _search.Text.Trim();
        var (item, seller) = text.StartsWith('@') ? ("", text[1..]) : (text, "");
        _loading = QueryRequest(new MarketplaceQueryPacket(Guid.NewGuid(), _npcId, _page, (byte)_category, (byte)_subcategory, job, (byte)_rarity.Selected, (byte)_sort.Selected, min, max, item, seller));
    }

    private void ReadPage(Task<MarketplaceView> loading)
    {
        if (!loading.IsCompletedSuccessfully || loading.Result is not { } view)
        {
            _status = "The market could not be read.";
            _statusColor = HudMarketFrame.Bad;
            return;
        }

        if (view.Status != 0)
        {
            _offers.Clear();
            _status = view.Status == 1 ? "The Marketplace is closed." : "Stand by the Marketplace to browse it.";
            _statusColor = HudMarketFrame.Bad;
            return;
        }

        _offers.Clear();
        if (view.Offers.Count > 0 && _statusColor != HudMarketFrame.Good)
        {
            _status = "";
        }

        foreach (var offer in view.Offers)
        {
            _offers.Add(new Shown(offer, Title(offer), PriceText(offer), Grade(offer)));
        }

        _tips.Clear();
        _page = view.Page;
        _pages = MarketplaceRules.Pages(view.Total);
        _pageText = string.Create(CultureInfo.InvariantCulture, $"{_page + 1}/{_pages}");
        _iconKeys.Clear();
        foreach (var shown in _offers)
        {
            _iconKeys.Add(shown.Offer.Item.CatalogKey);
        }

        IconRevision++;
        if (_offers.Count == 0 && _status.Length == 0)
        {
            _status = "No shop offers that now.";
            _statusColor = HudMarketFrame.Dim;
        }
    }

    private void ReadPurchase(Task<PersonalShopReply> buying)
    {
        var status = buying.IsCompletedSuccessfully ? buying.Result.Outcome.Status : PersonalShopStatus.Rejected;
        (_status, _statusColor) = status switch
        {
            PersonalShopStatus.Ok => ("Bought! The item is in your bag.", HudMarketFrame.Good),
            PersonalShopStatus.Closed or PersonalShopStatus.SoldOut => ("That offer is gone.", HudMarketFrame.Bad),
            PersonalShopStatus.PriceChanged or PersonalShopStatus.CountChanged => ("That offer changed; look again.", HudMarketFrame.Bad),
            PersonalShopStatus.NotEnoughGold => ("You do not have enough gold.", HudMarketFrame.Bad),
            PersonalShopStatus.NotEnoughCoins => ("You do not have enough coins.", HudMarketFrame.Bad),
            PersonalShopStatus.TooSoon => ("Wait a moment before buying again.", HudMarketFrame.Bad),
            PersonalShopStatus.ViewOnly => (PersonalShopMessages.ViewOnlyText, HudMarketFrame.Bad),
            PersonalShopStatus.NoSpace or PersonalShopStatus.OverWeight => ("Your bag has no room for it.", HudMarketFrame.Bad),
            PersonalShopStatus.Busy or PersonalShopStatus.Stale => ("Try again in a moment.", HudMarketFrame.Bad),
            PersonalShopStatus.Selling => ("Close your own shop first.", HudMarketFrame.Bad),
            _ => ("The purchase was refused.", HudMarketFrame.Bad)
        };
    }
}
