using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Npc;

// The Cabinet: the account's collection of looks, opened from its NPC or the U key, drawn as a forged-iron window (art
// assets/Hud/Cabinet: gradients, glows, round buttons and icons). The left panel shows the character in the chosen looks on a
// lit pedestal with Turn and No looks tools, one slot per look (a changed slot glows and has undo and remove buttons; a click
// on it shows its category), the chosen look's rarity, sources and description, what Apply Changes buys and its total, and
// the last result. The right panel has three modes:
// - Collection: search, Slot, Ownership and Rarity menus over the looks the class wears, the count and the collection's
//   progress, and a grid of cards (rarity border, glow and corners, price, Owned or Exclusive, a favourite star kept in the
//   client settings, a dot on looks applied now). A click tries a look on, again takes it off. Apply Changes asks first when
//   it buys, lists what and the total, and refuses when coins are short or a look is exclusive (tokens, brushes, quests,
//   events and deals give those).
// - Brush: the bag's gear a brush can turn into its look and the look tokens; a click asks first, naming what is used up,
//   and nothing is sent for a look already owned.
// - Deals: the open bidding deals with their leading bid, time left and latest bids; Bid offers the least next bid after a
//   confirmation (the coins are held, and given back when outbid).
// Closing throws away changes not applied. The server decides every unlock, purchase, bid and look; the window only asks.
public sealed class HudCabinet
{
    private const int Width = 790;
    private const int Height = 560;
    private const int Margin = 18;
    private const int LeftWidth = 286;
    private const int PanelGap = 14;
    private const int CardWidth = 100;
    private const int CardHeight = 136;
    private const int CardGap = 6;
    private const int Columns = 4;
    private const int Rows = 3;
    private const int DealRows = 4;
    private const int DealHeight = 70;
    private const int SlotWidth = 50;
    private const int SlotHeight = 54;
    private const int SlotStep = 74;
    private const int ToolSize = 28;
    private const int BadgeSize = 18;
    private const int FavoriteSize = 22;
    private const int FieldHeight = 30;
    private const int SearchWidth = 110;
    private const int MenuWidth = 98;
    private const int MenuRow = 22;
    private const int PlaqueHeight = 44;
    private const int Quarter = 4;
    private const float TitleScale = HudPainter.TextScale * 20f / 14f;
    private const float TagScale = HudPainter.TextScale * 10f / 14f;
    private const string Folder = "Hud/Cabinet/";
    private const string IronArt = Folder + "iron.png";
    private const string CardArt = Folder + "card.png";
    private const string PlaqueArt = Folder + "plaque.png";
    private const string ApplyArt = Folder + "apply.png";
    private const string CloseArt = Folder + "close.png";
    private const string GlowArt = Folder + "glow.png";
    private const string DiscArt = Folder + "disc.png";
    private const string StarArt = Folder + "star.png";
    private const string CrossArt = Folder + "cross.png";
    private const string TurnArt = Folder + "turn.png";
    private const string EyeArt = Folder + "eye.png";
    private const string SearchArt = Folder + "search.png";
    private const string ChevronArt = Folder + "chevron.png";
    private const string CoinArt = "Hud/CoinShop/coin.bmp";
    public static readonly string[] Art =
        [IronArt, CardArt, PlaqueArt, ApplyArt, CloseArt, GlowArt, DiscArt, StarArt, CrossArt, TurnArt, EyeArt, SearchArt, ChevronArt, CoinArt];
    private const string PreviewHint = "Choose a look to preview it";
    private const string NothingText = "No look matches these filters.";
    private const string NothingToBrush = "Nothing in your bag can be brushed.";
    private const string NoDeals = "No bidding deal is open now.";
    private static readonly string[] ModeNames = ["Collection", "Brush", "Deals"];
    private static readonly string[] CategoryNames = ["All", "Armor", "Robes", "Costume", "Weapon", "Shield", "Hair"];
    private static readonly string[] OwnershipNames = ["All looks", "Owned", "Locked", "For sale", "Exclusive", "Favorites"];
    private static readonly string[] RarityNames = ["Rarity", "Common", "Uncommon", "Rare", "Epic", "Legendary"];
    private static readonly string[] SlotNames = ["1-Hand", "Armor", "Off-hand", "Hair", "2-Hand"];
    // The category a slot's click shows: weapons, armor, shields, hair, weapons.
    private static readonly int[] SlotCategory = [4, 1, 5, 6, 4];
    private static readonly string[] RarityTags = BuildTags();
    private const int TwoHandSlot = 4;
    private const int CollectionMode = 0;
    private const int BrushMode = 1;
    private const int DealsMode = 2;
    private const int NoMenu = -1;
    private const int SlotMenu = 0;
    private const int OwnershipMenu = 1;
    private const int RarityMenu = 2;
    private const int OwnedFilter = 1;
    private const int LockedFilter = 2;
    private const int ForSaleFilter = 3;
    private const int ExclusiveFilter = 4;
    private const int FavoritesFilter = 5;
    private static readonly Color Iron = new(0x1d, 0x19, 0x16);
    private static readonly Color Well = new(0x0c, 0x0a, 0x09);
    private static readonly Color Edge = new(0x4a, 0x3f, 0x35);
    private static readonly Color Rim = new(0x0a, 0x08, 0x07);
    private static readonly Color Brass = new(0xd6, 0xa3, 0x5c);
    private static readonly Color BrassDim = new(0x8a, 0x6a, 0x3f);
    private static readonly Color Ink = new(0xe9, 0xdf, 0xd2);
    private static readonly Color InkDim = new(0x9c, 0x8f, 0x80);
    private static readonly Color Placeholder = new(0x7a, 0x6e, 0x62);
    private static readonly Color Field = new(0x15, 0x11, 0x0e);
    private static readonly Color SlotFace = new(0x1a, 0x15, 0x12);
    private static readonly Color CardEdge = new(0x3a, 0x31, 0x29);
    private static readonly Color Fresh = new(0x3f, 0xbf, 0x8a);
    private static readonly Color OkText = new(0x7f, 0xe0, 0xa6);
    private static readonly Color ErrText = new(0xff, 0x8c, 0x7f);
    private static readonly Color NeutralText = new(0xe8, 0xdc, 0xcb);
    private static readonly Color CommonRarity = new(0x7d, 0x72, 0x66);
    private static readonly Color StageGlow = new(0x2a, 0x22, 0x1b);
    private static readonly Color PlaqueRim = new(0xb9, 0xb2, 0xa8);
    private static readonly Color RivetColor = new(0x8a, 0x70, 0x50);
    private static readonly Color BadgeRim = new(0x6d, 0x61, 0x57);
    private static readonly Color BadgeFace = new(0x15, 0x11, 0x0e);
    private static readonly Color Shade = new(0, 0, 0, 180);
    private static readonly Color Hover = new(0x2b, 0x25, 0x20);
    private static readonly Color Dashed = new(0x3a, 0x31, 0x29);

    private readonly record struct ConfirmRow(string Left, string Right);

    private readonly TextInputField _search = new() { MaxLength = 24 };
    private readonly List<SkinArticle> _shown = [];
    private readonly List<OwnedItemView> _brushable = [];
    private readonly List<SkinArticle?> _brushLooks = [];
    private readonly string[] _chosen = ["", "", "", "", ""];
    // The looks applied now, per slot, as the server last listed them.
    private readonly string[] _saved = ["", "", "", "", ""];
    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _worn = ["", "", "", ""];
    private readonly List<string> _iconKeys = [];
    private readonly List<SkinArticle> _toBuy = [];
    // Each card name split over two lines that fit the card, made once per name.
    private readonly Dictionary<string, (string First, string Second)> _cardNames = new(StringComparer.Ordinal);
    // Each price's text, made once.
    private readonly Dictionary<long, string> _prices = [];
    private readonly int[] _ownedIn = new int[CategoryNames.Length];
    private readonly int[] _totalIn = new int[CategoryNames.Length];
    private IReadOnlyList<SkinArticle> _articles = [];
    private IReadOnlyList<CabinetWireDeal> _deals = [];
    private readonly string[] _dealTop = new string[DealRows];
    private readonly string[] _dealHistory = new string[DealRows];
    private readonly string[] _dealBid = new string[DealRows];
    private readonly string[] _dealTime = new string[DealRows];
    private long _dealTimeSecond = -1;
    private OwnerState _state;
    private Task<SkinCatalogInfo> _loading;
    private Task<(string Text, bool Ok)> _acting;
    private Task<CabinetDealsPacket> _dealsLoading;
    private long _balance;
    private long _dealClock;
    private double _dealClockAt;
    private double _seconds;
    private string _balanceText = "0";
    private string _status = "";
    private bool _statusOk;
    private string _billText = "No changes";
    private string _billCost = "";
    private long _cost;
    private bool _pending;
    private string _countText = "";
    private string _progressText = "";
    private string _brushText = "";
    private string _infoName = "";
    private string _infoSources = "";
    private string _infoDescription = "";
    private Color _infoColor = Ink;
    private string _confirmTitle = "";
    private ConfirmRow[] _confirmRows = [];
    private string _confirmTotal;
    private string _confirmYes = "";
    private Func<Task<(string Text, bool Ok)>> _confirmAction;
    private int _mode;
    private int _category;
    private int _ownership;
    private int _rarity;
    private int _menu = NoMenu;
    private int _scroll;
    private long _wornRevision = -1;
    private string _searched = "";
    private bool _bare;

    public bool Open { get; private set; }

    // How many quarter turns the preview stands turned.
    public int PreviewTurns { get; private set; }

    // The favourite looks' keys, comma-separated, as the settings keep them; FavoritesChanged hands a new list back.
    public string Favorites
    {
        get => string.Join(',', _favorites);
        set
        {
            _favorites.Clear();
            _favorites.UnionWith((value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    public Action<string> FavoritesChanged { get; set; }

    // The player's job bit: only what the class wears is shown.
    public uint Jobs { get; set; }
    public bool Searching => Open && _search.Focused;
    public Microsoft.Xna.Framework.Graphics.Texture2D Picture { get; set; }
    public Func<Task<SkinCatalogInfo>> ListRequest { get; set; }
    public Func<string, Task<(SkinStatus Status, long Balance)>> BuyRequest { get; set; }
    public Func<SkinSet, Task<(SkinStatus Status, long Balance)>> ApplyRequest { get; set; }
    public Func<Guid, Task<(SkinStatus Status, long Balance, string Key)>> UnlockRequest { get; set; }
    public Func<Task<CabinetDealsPacket>> DealsRequest { get; set; }
    public Func<long, long, Task<(CabinetBidStatus Status, long Balance)>> BidRequest { get; set; }

    // The worn gear in the chosen looks, for the preview; a new object only when it changes.
    public SkinSet Preview { get; private set; }

    public int IconRevision { get; private set; }
    public IEnumerable<string> IconKeys => Open ? _iconKeys : [];

    // The plaque stands above the window's top edge, so the window keeps room for it.
    public static Rectangle Window(HudLayout layout) =>
        new((layout.Width - Width) / 2, Math.Max(PlaqueHeight / 2 + 4, (layout.Height - Height) / 2), Width, Height);

    private static Rectangle LeftPanel(Rectangle window) => new(window.X + Margin, window.Y + 46, LeftWidth, Height - 64);
    private static Rectangle RightPanel(Rectangle window)
    {
        var left = LeftPanel(window);
        return new Rectangle(left.Right + PanelGap, left.Y, window.Right - Margin - left.Right - PanelGap, left.Height);
    }

    private static Rectangle ModeTab(Rectangle window, int index) => new(window.X + 40 + index * 88, window.Y + 12, 84, 24);
    private static Rectangle CloseButton(Rectangle window) => new(window.Right - 14 - 26, window.Y + 11, 26, 26);
    private static Rectangle PeekTool(Rectangle window) => new(LeftPanel(window).Right - 14 - ToolSize, LeftPanel(window).Y + 12, ToolSize, ToolSize);
    private static Rectangle TurnTool(Rectangle window) => PeekTool(window) with { X = PeekTool(window).X - ToolSize - 6 };
    private static Rectangle SlotBox(Rectangle window, int slot) =>
        new(LeftPanel(window).Right - 14 - SlotWidth, LeftPanel(window).Y + 52 + slot * SlotStep, SlotWidth, SlotHeight);
    private static Rectangle UndoBadge(Rectangle box) => new(box.X - BadgeSize / 2, box.Y - BadgeSize / 2, BadgeSize, BadgeSize);
    private static Rectangle ClearBadge(Rectangle box) => new(box.Right - BadgeSize / 2, box.Y - BadgeSize / 2, BadgeSize, BadgeSize);
    private static Rectangle Picture3D(Rectangle window) => new(LeftPanel(window).X + 12, LeftPanel(window).Y + 40, 188, 270);
    private static Rectangle InfoBox(Rectangle window) => new(LeftPanel(window).X + 10, LeftPanel(window).Y + 320, 198, 56);
    private static Rectangle Apply(Rectangle window) => new(LeftPanel(window).Center.X - 80, LeftPanel(window).Bottom - 62, 160, 30);
    private static Rectangle Search(Rectangle window) => new(RightPanel(window).X + 12, RightPanel(window).Y + 12, SearchWidth, FieldHeight);
    private static Rectangle MenuButton(Rectangle window, int menu) =>
        new(RightPanel(window).X + 12 + SearchWidth + 8 + menu * (MenuWidth + 8), RightPanel(window).Y + 12, MenuWidth, FieldHeight);
    private static Rectangle MenuItem(Rectangle button, int index) => new(button.X, button.Bottom + 2 + index * MenuRow, button.Width, MenuRow);
    private static Rectangle Card(Rectangle window, int index) =>
        new(RightPanel(window).X + 12 + index % Columns * (CardWidth + CardGap), RightPanel(window).Y + 72 + index / Columns * (CardHeight + CardGap),
            CardWidth, CardHeight);
    private static Rectangle FavoriteButton(Rectangle card) => new(card.Right - FavoriteSize / 2 - 2, card.Center.Y - FavoriteSize / 2 - 8, FavoriteSize, FavoriteSize);
    private static Rectangle DealRow(Rectangle window, int index) =>
        new(RightPanel(window).X + 12, RightPanel(window).Y + 44 + index * (DealHeight + 6), RightPanel(window).Width - 24, DealHeight);
    private static Rectangle BidButton(Rectangle row) => new(row.Right - 98, row.Y + 22, 90, 26);
    private Rectangle ConfirmBox(Rectangle window)
    {
        var height = 64 + _confirmRows.Length * 26 + (_confirmTotal is null ? 0 : 28) + 44;
        return new Rectangle(window.Center.X - 190, window.Center.Y - height / 2, 380, height);
    }

    private static Rectangle ConfirmYes(Rectangle box) => new(box.Right - 16 - 120, box.Bottom - 16 - 28, 120, 28);
    private static Rectangle ConfirmNo(Rectangle box) => new(box.Right - 16 - 120 - 8 - 90, box.Bottom - 16 - 28, 90, 28);

    private bool Changed(int slot) => !string.Equals(_chosen[slot], _saved[slot], StringComparison.OrdinalIgnoreCase);

    private bool Applied(string key) => Array.Exists(_saved, saved => string.Equals(saved, key, StringComparison.OrdinalIgnoreCase));

    private static string[] BuildTags()
    {
        var tags = new string[(int)ItemRarities.Highest + 1];
        for (var rarity = 0; rarity < tags.Length; rarity++)
        {
            tags[rarity] = ItemRarities.NameOf((ItemRarity)rarity).ToUpperInvariant();
        }

        return tags;
    }

    private static Color RarityColor(ItemRarity rarity) => rarity > ItemRarity.Common ? HudRarityColors.Of(rarity) : CommonRarity;

    public void Toggle()
    {
        if (Open)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        Open = true;
        _bare = false;
        PreviewTurns = 0;
        _menu = NoMenu;
        _status = "";
        _loading = ListRequest?.Invoke();
        if (_mode == DealsMode)
        {
            _dealsLoading = DealsRequest?.Invoke();
        }
    }

    // Closing throws away the changes not applied.
    public void Hide()
    {
        Open = false;
        Preview = null;
        _search.Focused = false;
        _confirmAction = null;
        _menu = NoMenu;
        Array.Copy(_saved, _chosen, _chosen.Length);
    }

    public void CancelSearch() => _search.Focused = false;

    public void Type(ReadOnlySpan<char> typed, double elapsedSeconds) => _search.Update(typed, elapsedSeconds);

    public bool Captures(HudLayout layout, Point pointer) => Open && Window(layout).Contains(pointer);

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta == 0 || _mode == DealsMode)
        {
            return;
        }

        var count = _mode == BrushMode ? _brushable.Count : _shown.Count;
        var rows = (count + Columns - 1) / Columns;
        SetScroll(Math.Clamp(_scroll - Math.Sign(wheelDelta), 0, Math.Max(0, rows - Rows)));
    }

    public void Update(OwnerState state, double elapsedSeconds = 0)
    {
        _seconds += elapsedSeconds;
        _state = state ?? _state;
        if (_loading is { IsCompleted: true } loading)
        {
            _loading = null;
            if (loading.IsCompletedSuccessfully)
            {
                var info = loading.Result;
                _articles = info.Articles;
                (_chosen[0], _chosen[1], _chosen[2], _chosen[3], _chosen[TwoHandSlot]) = (info.Skins.Weapon, info.Skins.Armor, info.Skins.OffHand, info.Skins.Hair, info.Skins.TwoHand);
                Array.Copy(_chosen, _saved, _chosen.Length);
                SetBalance(info.Balance);
                Refilter();
            }
        }

        if (_acting is { IsCompleted: true } acting)
        {
            _acting = null;
            (_status, _statusOk) = acting.IsCompletedSuccessfully ? acting.Result : ("The request failed.", false);
            _loading = ListRequest?.Invoke();
            if (_mode == DealsMode)
            {
                _dealsLoading = DealsRequest?.Invoke();
            }
        }

        if (_dealsLoading is { IsCompleted: true } dealsLoading)
        {
            _dealsLoading = null;
            if (dealsLoading.IsCompletedSuccessfully)
            {
                _deals = dealsLoading.Result.Deals;
                _dealClock = dealsLoading.Result.Now;
                _dealClockAt = _seconds;
                SetBalance(dealsLoading.Result.Balance);
                FormatDeals();
                RefreshIcons();
            }
        }

        if (!Open)
        {
            return;
        }

        var dealSecond = _dealClock + (long)(_seconds - _dealClockAt);
        if (_mode == DealsMode && dealSecond != _dealTimeSecond)
        {
            _dealTimeSecond = dealSecond;
            for (var index = 0; index < DealRows && index < _deals.Count; index++)
            {
                var deal = _deals[index];
                _dealTime[index] = TimeLeft(deal.EndsAt - dealSecond) + (deal.MinLevel > 0 ? string.Create(CultureInfo.InvariantCulture, $"  Lv {deal.MinLevel}+") : "");
            }
        }

        if (!string.Equals(_searched, _search.Text, StringComparison.Ordinal))
        {
            _searched = _search.Text;
            Refilter();
        }

        if (state is not null && state.InventoryRevision != _wornRevision)
        {
            _wornRevision = state.InventoryRevision;
            ReadWorn(state);
            Refilter();
        }
    }

    private void ReadWorn(OwnerState state)
    {
        (_worn[0], _worn[1], _worn[2]) = ("", "", "");
        foreach (var item in state.Items)
        {
            var slot = (EquipmentSlot)item.Slot switch
            {
                EquipmentSlot.Weapon => 0,
                EquipmentSlot.Armor => 1,
                EquipmentSlot.OffHand => 2,
                _ => -1
            };
            if (item.Equipped && slot >= 0)
            {
                _worn[slot] = item.CatalogKey;
            }
        }
    }

    // A weapon skin goes to the slot of the grip worn now.
    private int WornWeaponSlot => ItemEquipmentMetadata.IsTwoHanded(_worn[0]) ? TwoHandSlot : (int)SkinSlot.Weapon;

    private int ChosenSlot(SkinArticle article) => article.Slot == (byte)SkinSlot.Weapon ? WornWeaponSlot : article.Slot;

    private void RefreshPreview()
    {
        var weapon = _bare ? _worn[0] : SkinRules.Look(_worn[0], _chosen[WornWeaponSlot]);
        Preview = _bare
            ? new SkinSet(weapon, _worn[1], _worn[2], "", weapon)
            : new SkinSet(weapon, SkinRules.Look(_worn[1], _chosen[1]), SkinRules.Look(_worn[2], _chosen[2]), _chosen[3], weapon);
        _toBuy.Clear();
        _cost = 0;
        var changes = 0;
        for (var slot = 0; slot < _chosen.Length; slot++)
        {
            changes += Changed(slot) ? 1 : 0;
            if (Find(_chosen[slot]) is { Owned: false } article && !_toBuy.Exists(buy => string.Equals(buy.Key, article.Key, StringComparison.OrdinalIgnoreCase)))
            {
                _toBuy.Add(article);
                _cost += article.Price;
            }
        }

        _pending = changes > 0;
        _billCost = _toBuy.Count > 0 ? _cost.ToString("N0", CultureInfo.InvariantCulture) : "";
        _billText = !_pending ? "No changes"
            : _toBuy.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $"{_toBuy.Count} to buy  ·  ")
            : string.Create(CultureInfo.InvariantCulture, $"{changes} change{(changes > 1 ? "s" : "")}  ·  all owned");
        RefreshIcons();
    }

    private SkinArticle? Find(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        foreach (var article in _articles)
        {
            if (string.Equals(article.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return article;
            }
        }

        return null;
    }

    private void SetBalance(long balance)
    {
        _balance = balance;
        _balanceText = _balance.ToString("N0", CultureInfo.InvariantCulture);
    }

    private string PriceText(long price)
    {
        if (!_prices.TryGetValue(price, out var text))
        {
            text = price.ToString("N0", CultureInfo.InvariantCulture);
            _prices[price] = text;
        }

        return text;
    }

    // The category a look belongs to (0 is All).
    private static int TabOf(SkinArticle article) => article.Category + 1;

    // Only looks of the equipped items' types (a worn armor shows armor looks and costumes, a worn bow bow looks) that the class
    // wears, and every hair.
    private bool ClassWears(SkinArticle article) =>
        article.Slot == (byte)SkinSlot.Hair
        || (article.Slot != (byte)SkinSlot.Armor || World.Remote.EquipmentVisuals.HasBody(article.Key)) && SkinRules.Wearable(article.Key, Jobs)
        && article.Slot < _worn.Length && SkinRules.Fits(_worn[article.Slot], article.Key);

    private bool Passes(SkinArticle article)
    {
        var sources = (CosmeticSource)article.Sources;
        var owner = _ownership switch
        {
            OwnedFilter => article.Owned,
            LockedFilter => !article.Owned,
            ForSaleFilter => !article.Owned && CabinetRules.Purchasable(sources),
            ExclusiveFilter => CabinetRules.Exclusive(sources),
            FavoritesFilter => _favorites.Contains(article.Key),
            _ => true
        };
        return owner && (_rarity == 0 || article.Rarity == _rarity || _rarity == (int)ItemRarity.Common && article.Rarity == (byte)ItemRarity.None);
    }

    private void Refilter()
    {
        _shown.Clear();
        Array.Clear(_ownedIn);
        Array.Clear(_totalIn);
        foreach (var article in _articles)
        {
            if (!ClassWears(article))
            {
                continue;
            }

            var tab = TabOf(article);
            _totalIn[0]++;
            _totalIn[tab]++;
            _ownedIn[0] += article.Owned ? 1 : 0;
            _ownedIn[tab] += article.Owned ? 1 : 0;
            if ((_category == 0 || tab == _category) && Passes(article)
                && (_search.Text.Length == 0 || article.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                _shown.Add(article);
            }
        }

        _countText = string.Create(CultureInfo.InvariantCulture, $"{_shown.Count} of {_totalIn[_category]} looks");
        _progressText = _category == 0
            ? string.Create(CultureInfo.InvariantCulture, $"Collection {_ownedIn[0]} / {_totalIn[0]}")
            : string.Create(CultureInfo.InvariantCulture, $"{CategoryNames[_category]} {_ownedIn[_category]} / {_totalIn[_category]}  ·  All {_ownedIn[0]} / {_totalIn[0]}");
        ReadBrushable();
        SetScroll(0);
        RefreshPreview();
    }

    // The bag's items The Cabinet can take: gear whose look a brush unlocks, and look tokens.
    private void ReadBrushable()
    {
        _brushable.Clear();
        _brushLooks.Clear();
        var armorBrushes = 0;
        var weaponBrushes = 0;
        if (_state is not null)
        {
            foreach (var item in _state.Items)
            {
                if (!item.InBag)
                {
                    continue;
                }

                var brush = CabinetRules.BrushOfItem(item.CatalogKey);
                armorBrushes += brush == CabinetBrush.ArmorRobe ? item.Count : 0;
                weaponBrushes += brush == CabinetBrush.WeaponShield ? item.Count : 0;
                var look = Find(item.CatalogKey);
                if (CabinetRules.IsToken(item.CatalogKey)
                    || look is { } brushable && ((CosmeticSource)brushable.Sources & CosmeticSource.Brush) != 0 && brushable.Slot != (byte)SkinSlot.Hair)
                {
                    _brushable.Add(item);
                    _brushLooks.Add(look);
                }
            }
        }

        _brushText = string.Create(CultureInfo.InvariantCulture, $"Brushes: {armorBrushes} armor & robe, {weaponBrushes} weapon & shield");
    }

    private int BrushCount(CabinetBrush brush)
    {
        var count = 0;
        foreach (var item in _state?.Items ?? [])
        {
            count += item.InBag && CabinetRules.BrushOfItem(item.CatalogKey) == brush ? item.Count : 0;
        }

        return count;
    }

    private void SetScroll(int scroll)
    {
        _scroll = scroll;
        RefreshIcons();
    }

    private void RefreshIcons()
    {
        _iconKeys.Clear();
        if (_mode == DealsMode)
        {
            foreach (var deal in _deals)
            {
                _iconKeys.Add(deal.Key);
            }
        }
        else
        {
            var count = _mode == BrushMode ? _brushable.Count : _shown.Count;
            for (var index = _scroll * Columns; index < count && index < (_scroll + Rows) * Columns; index++)
            {
                _iconKeys.Add(_mode == BrushMode ? _brushable[index].CatalogKey : _shown[index].Key);
            }
        }

        // The slot boxes show the chosen looks, wherever their cards are.
        foreach (var key in _chosen)
        {
            if (key.Length > 0)
            {
                _iconKeys.Add(key);
            }
        }

        IconRevision++;
    }

    private void ShowInfo(SkinArticle article)
    {
        _infoName = article.Name;
        _infoColor = article.Rarity > (byte)ItemRarity.Common ? HudRarityColors.Of((ItemRarity)article.Rarity) : Brass;
        var sources = (CosmeticSource)article.Sources;
        _infoSources = (article.Rarity > 0 ? ItemRarities.NameOf((ItemRarity)article.Rarity) + " - " : "")
            + (article.Owned ? "Unlocked" : CabinetRules.Exclusive(sources) ? "Exclusive: " + CabinetRules.SourceText(sources) : CabinetRules.SourceText(sources));
        _infoDescription = article.Description ?? "";
    }

    private void Say(string text, bool ok)
    {
        _status = text;
        _statusOk = ok;
    }

    public bool Click(HudLayout layout, Point pointer)
    {
        var window = Window(layout);
        var plaque = Plaque(window);
        if (!Open || !window.Contains(pointer) && !plaque.Contains(pointer) && !(_menu != NoMenu && MenuArea(window).Contains(pointer)))
        {
            return false;
        }

        if (_confirmAction is { } action)
        {
            var box = ConfirmBox(window);
            if (ConfirmYes(box).Contains(pointer) && _acting is null)
            {
                _confirmAction = null;
                Say("", false);
                _acting = action();
            }
            else if (ConfirmNo(box).Contains(pointer))
            {
                _confirmAction = null;
            }

            return true;
        }

        if (_menu != NoMenu)
        {
            ClickMenu(window, pointer);
            return true;
        }

        _search.Focused = Search(window).Contains(pointer);
        if (CloseButton(window).Contains(pointer))
        {
            Hide();
            return true;
        }

        for (var index = 0; index < ModeNames.Length; index++)
        {
            if (ModeTab(window, index).Contains(pointer))
            {
                _mode = index;
                Say("", false);
                if (_mode == DealsMode)
                {
                    _dealsLoading = DealsRequest?.Invoke();
                }

                SetScroll(0);
                return true;
            }
        }

        if (TurnTool(window).Contains(pointer))
        {
            PreviewTurns = (PreviewTurns + 1) % Quarter;
            return true;
        }

        if (PeekTool(window).Contains(pointer))
        {
            _bare = !_bare;
            RefreshPreview();
            return true;
        }

        for (var slot = 0; slot < _chosen.Length; slot++)
        {
            var box = SlotBox(window, slot);
            if (Changed(slot) && UndoBadge(box).Contains(pointer))
            {
                _chosen[slot] = _saved[slot];
                RefreshPreview();
                return true;
            }

            if (_chosen[slot].Length > 0 && ClearBadge(box).Contains(pointer))
            {
                _chosen[slot] = "";
                RefreshPreview();
                return true;
            }

            if (box.Contains(pointer))
            {
                _mode = CollectionMode;
                _category = SlotCategory[slot];
                Refilter();
                return true;
            }
        }

        if (Apply(window).Contains(pointer))
        {
            ClickApply();
            return true;
        }

        if (_mode == CollectionMode)
        {
            ClickCollection(window, pointer);
        }
        else if (_mode == BrushMode)
        {
            ClickBrush(window, pointer);
        }
        else
        {
            ClickDeals(window, pointer);
        }

        return true;
    }

    // An open menu takes the next click: a choice, or anywhere else to close it.
    private void ClickMenu(Rectangle window, Point pointer)
    {
        var button = MenuButton(window, _menu);
        var names = MenuNames(_menu);
        for (var index = 0; index < names.Length; index++)
        {
            if (MenuItem(button, index).Contains(pointer))
            {
                switch (_menu)
                {
                    case SlotMenu:
                        _category = index;
                        break;
                    case OwnershipMenu:
                        _ownership = index;
                        break;
                    default:
                        _rarity = index;
                        break;
                }

                Refilter();
                break;
            }
        }

        _menu = NoMenu;
    }

    private static string[] MenuNames(int menu) => menu switch
    {
        SlotMenu => CategoryNames,
        OwnershipMenu => OwnershipNames,
        _ => RarityNames
    };

    private int MenuChoice(int menu) => menu switch
    {
        SlotMenu => _category,
        OwnershipMenu => _ownership,
        _ => _rarity
    };

    private Rectangle MenuArea(Rectangle window)
    {
        var button = MenuButton(window, _menu);
        return new Rectangle(button.X, button.Y, button.Width, button.Height + 2 + MenuNames(_menu).Length * MenuRow);
    }

    private static Rectangle Plaque(Rectangle window) => new(window.Center.X - 110, window.Y - PlaqueHeight / 2, 220, PlaqueHeight);

    private void ClickApply()
    {
        if (!_pending || _acting is not null || _loading is not null)
        {
            return;
        }

        var skins = new SkinSet(_chosen[0], _chosen[1], _chosen[2], _chosen[3], _chosen[TwoHandSlot]);
        foreach (var article in _toBuy)
        {
            if (CabinetRules.Exclusive((CosmeticSource)article.Sources))
            {
                Say($"{article.Name} is exclusive: {CabinetRules.SourceText((CosmeticSource)article.Sources)}. Remove it to continue.", false);
                return;
            }
        }

        if (_cost > _balance)
        {
            Say(string.Create(CultureInfo.InvariantCulture, $"You need {_cost - _balance:N0} more coins."), false);
            return;
        }

        if (_toBuy.Count == 0)
        {
            Say("", false);
            _acting = BuyAndApplyAsync(skins);
            return;
        }

        var rows = new ConfirmRow[_toBuy.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new ConfirmRow(_toBuy[index].Name, PriceText(_toBuy[index].Price));
        }

        Confirm("Confirm purchase", rows, PriceText(_cost), "Buy & Equip", () => BuyAndApplyAsync(skins));
    }

    private void ClickCollection(Rectangle window, Point pointer)
    {
        if (Search(window).Contains(pointer))
        {
            return;
        }

        for (var menu = SlotMenu; menu <= RarityMenu; menu++)
        {
            if (MenuButton(window, menu).Contains(pointer))
            {
                _menu = menu;
                return;
            }
        }

        for (var card = 0; card < Columns * Rows; card++)
        {
            var index = _scroll * Columns + card;
            if (index >= _shown.Count)
            {
                break;
            }

            var bounds = Card(window, card);
            var article = _shown[index];
            if (FavoriteButton(bounds).Contains(pointer))
            {
                if (!_favorites.Remove(article.Key))
                {
                    _favorites.Add(article.Key);
                }

                FavoritesChanged?.Invoke(Favorites);
                if (_ownership == FavoritesFilter)
                {
                    Refilter();
                }

                return;
            }

            if (bounds.Contains(pointer))
            {
                ShowInfo(article);
                var chosenSlot = ChosenSlot(article);
                _chosen[chosenSlot] = string.Equals(_chosen[chosenSlot], article.Key, StringComparison.OrdinalIgnoreCase) ? "" : article.Key;
                Say("", false);
                RefreshPreview();
                return;
            }
        }
    }

    private void ClickBrush(Rectangle window, Point pointer)
    {
        for (var card = 0; card < Columns * Rows; card++)
        {
            var index = _scroll * Columns + card;
            if (index >= _brushable.Count || !Card(window, card).Contains(pointer))
            {
                continue;
            }

            var item = _brushable[index];
            if (CabinetRules.IsToken(item.CatalogKey))
            {
                Confirm($"Use {item.Name}?", [new("It unlocks its look for every character of your account.", ""), new("The token is used up.", "")], null, "Use token",
                    () => UnlockAsync(item.InstanceId));
                return;
            }

            if (Find(item.CatalogKey) is not { } look)
            {
                return;
            }

            ShowInfo(look);
            if (look.Owned)
            {
                Say("Your account owns this look already.", false);
                return;
            }

            var brush = CabinetRules.BrushFor((SkinSlot)look.Slot);
            if (BrushCount(brush) == 0)
            {
                Say($"You need a {CabinetRules.BrushName(brush)}.", false);
                return;
            }

            Confirm($"Brush {item.Name}?", [new($"The item and one {CabinetRules.BrushName(brush)} are used up.", ""),
                new("The look is unlocked for your whole account.", "")], null, "Brush it", () => UnlockAsync(item.InstanceId));
            return;
        }
    }

    private void ClickDeals(Rectangle window, Point pointer)
    {
        for (var index = 0; index < DealRows && index < _deals.Count; index++)
        {
            var deal = _deals[index];
            if (!BidButton(DealRow(window, index)).Contains(pointer))
            {
                continue;
            }

            if (deal.Leading)
            {
                Say("You lead this deal.", true);
                return;
            }

            if (Find(deal.Key) is { Owned: true })
            {
                Say("Your account owns this look already.", false);
                return;
            }

            var amount = deal.NextBid;
            Confirm($"Bid on {deal.Name}?", [new("Your bid", PriceText(amount)), new("Held now, given back if you are outbid.", ""),
                new("The highest bid when time ends wins.", "")], null, "Place bid", () => BidAsync(deal.Id, amount));
            return;
        }
    }

    private void Confirm(string title, ConfirmRow[] rows, string total, string yes, Func<Task<(string Text, bool Ok)>> action)
    {
        _confirmTitle = title;
        _confirmRows = rows;
        _confirmTotal = total;
        _confirmYes = yes;
        _confirmAction = action;
    }

    private async Task<(string Text, bool Ok)> UnlockAsync(Guid itemId)
    {
        var (status, balance, key) = await UnlockRequest(itemId);
        SetBalance(balance);
        return status switch
        {
            SkinStatus.Ok => ($"{Find(key)?.Name ?? key} unlocked for your account.", true),
            SkinStatus.AlreadyOwned => ("Your account owns this look already; nothing was used.", false),
            SkinStatus.NoBrush => ("You carry no brush of this kind.", false),
            SkinStatus.NotEligible => ("This item cannot be brushed.", false),
            SkinStatus.Busy => ("Busy, try again.", false),
            _ => ("The request was refused.", false)
        };
    }

    private async Task<(string Text, bool Ok)> BidAsync(long dealId, long amount)
    {
        var (status, balance) = await BidRequest(dealId, amount);
        SetBalance(balance);
        return status switch
        {
            CabinetBidStatus.Placed => ("Your bid leads.", true),
            CabinetBidStatus.TooLow => ("Someone bid first; look at the new price.", false),
            CabinetBidStatus.Ended => ("This deal has ended.", false),
            CabinetBidStatus.Leading => ("You lead this deal already.", true),
            CabinetBidStatus.NotEnoughCoins => ("Not enough coins.", false),
            CabinetBidStatus.AlreadyOwned => ("Your account owns this look already.", false),
            CabinetBidStatus.LevelTooLow => ("Your level is too low for this deal.", false),
            _ => ("The bid was refused.", false)
        };
    }

    private async Task<(string Text, bool Ok)> BuyAndApplyAsync(SkinSet skins)
    {
        var bought = 0;
        foreach (var key in (string[])[skins.Weapon, skins.Armor, skins.OffHand, skins.Hair, skins.TwoHand])
        {
            if (Find(key) is { Owned: false } article)
            {
                if (CabinetRules.Exclusive((CosmeticSource)article.Sources))
                {
                    return ($"{article.Name} is exclusive: {CabinetRules.SourceText((CosmeticSource)article.Sources)}.", false);
                }

                var (status, _) = await BuyRequest(key);
                if (status is not SkinStatus.Ok and not SkinStatus.AlreadyOwned)
                {
                    return (status == SkinStatus.NotEnoughCoins ? "Not enough coins." : status == SkinStatus.NotForSale ? $"{article.Name} is not for sale." : "The purchase was refused.", false);
                }

                bought += status == SkinStatus.Ok ? 1 : 0;
            }
        }

        if ((await ApplyRequest(skins)).Status != SkinStatus.Ok)
        {
            return ("The change was refused.", false);
        }

        return bought > 0 ? (string.Create(CultureInfo.InvariantCulture, $"Bought {bought} look{(bought > 1 ? "s" : "")} and saved your appearance."), true) : ("Appearance saved.", true);
    }

    public void Draw(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        var menuOpen = _menu != NoMenu || _confirmAction is not null;
        var hoverPointer = menuOpen ? new Point(-1, -1) : pointer;
        DrawFrame(painter, window);
        DrawTopBar(painter, window, hoverPointer);
        DrawLeft(painter, window, icons, hoverPointer);
        DrawRight(painter, window, icons, hoverPointer);
        if (_menu != NoMenu)
        {
            painter.Flush();
            DrawMenu(painter, window, pointer);
        }

        if (_confirmAction is not null)
        {
            painter.Flush();
            DrawConfirm(painter, window, pointer);
        }
    }

    // The forged window: a black rim inside a dark ring and a bevel edge, the body lit at its top, two rivets and the plaque.
    private static void DrawFrame(HudPainter painter, Rectangle window)
    {
        Outline(painter, Inflate(window, 5), Edge);
        painter.Fill(Inflate(window, 4), Rim);
        painter.Fill(window, Iron);
        painter.Stretch(IronArt, new Rectangle(window.X, window.Y, window.Width, 120));
        Outline(painter, window, Color.Black);
        Outline(painter, Inflate(window, -1), Edge);
        Rivet(painter, window.X + 12, window.Y + 10);
        Rivet(painter, window.Right - 22, window.Y + 10);
        var plaque = Plaque(window);
        Outline(painter, Inflate(plaque, 4), Edge);
        painter.Fill(Inflate(plaque, 3), Rim);
        painter.Fill(Inflate(plaque, 2), PlaqueRim);
        painter.Stretch(PlaqueArt, plaque);
        painter.Flush();
        var title = "The Cabinet";
        var width = painter.MeasureAt(title, TitleScale);
        painter.Text(title, plaque.Center.X - width / 2, plaque.Y + 11, NeutralText, TitleScale);
    }

    private static void Rivet(HudPainter painter, int x, int y)
    {
        painter.Stretch(DiscArt, new Rectangle(x, y, 10, 10), null, new Color(0x3b, 0x2f, 0x22));
        painter.Stretch(DiscArt, new Rectangle(x + 2, y + 2, 5, 5), null, RivetColor);
    }

    private void DrawTopBar(HudPainter painter, Rectangle window, Point pointer)
    {
        for (var index = 0; index < ModeNames.Length; index++)
        {
            var tab = ModeTab(window, index);
            var lit = index == _mode;
            painter.Fill(tab, lit ? Hover : tab.Contains(pointer) ? Hover : Iron);
            Outline(painter, tab, lit ? Brass : Edge);
            painter.TextCentered(ModeNames[index], tab.X, tab.Right, tab.Y + 5, lit ? Brass : tab.Contains(pointer) ? Ink : InkDim, false);
        }

        var close = CloseButton(window);
        var balanceWidth = painter.Measure(_balanceText);
        var purse = new Rectangle(close.X - 8 - (balanceWidth + 36), close.Y, balanceWidth + 36, close.Height);
        painter.Fill(purse, Well);
        Outline(painter, purse, Edge);
        painter.Sprite(CoinArt, purse.X + 7, purse.Y + 6);
        painter.Text(_balanceText, purse.X + 27, purse.Y + 6, Ink, false);
        painter.Fill(close, Color.Black);
        painter.Stretch(CloseArt, Inflate(close, -1), null, close.Contains(pointer) ? new Color(255, 225, 220) : Color.White);
        painter.Stretch(CrossArt, new Rectangle(close.X + 6, close.Y + 6, 14, 14));
    }

    private void DrawLeft(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        var left = LeftPanel(window);
        Panel(painter, left);
        painter.Stretch(GlowArt, new Rectangle(left.X - 40, left.Y + 20, 320, 360), null, StageGlow);
        var picture = Picture3D(window);
        painter.Stretch(GlowArt, new Rectangle(picture.X + 24, picture.Bottom - 22, picture.Width - 48, 22), null, Color.Black * 0.8f);
        if (Picture is { } texture)
        {
            painter.Image(texture, picture);
        }

        Tool(painter, TurnTool(window), TurnArt, PreviewTurns != 0, pointer);
        Tool(painter, PeekTool(window), EyeArt, _bare, pointer);
        for (var slot = 0; slot < _chosen.Length; slot++)
        {
            DrawSlot(painter, icons, SlotBox(window, slot), slot, pointer);
        }

        var info = InfoBox(window);
        painter.Fill(info, new Color(0x13, 0x10, 0x0d));
        Outline(painter, info, Edge);
        painter.Fill(new Rectangle(info.X, info.Y, 3, info.Height), _infoName.Length > 0 ? _infoColor : Edge);
        painter.Text(painter.Fit(_infoName.Length > 0 ? _infoName : PreviewHint, info.Width - 14), info.X + 9, info.Y + 4, _infoName.Length > 0 ? _infoColor : InkDim, false);
        painter.Text(painter.Fit(_infoSources, info.Width - 14), info.X + 9, info.Y + 21, InkDim, false);
        painter.Text(painter.Fit(_infoDescription, info.Width - 14), info.X + 9, info.Y + 38, Ink, false);

        var billY = Apply(window).Y - 22;
        if (_billCost.Length > 0)
        {
            var textWidth = painter.Measure(_billText);
            var costWidth = painter.Measure(_billCost);
            var x = left.Center.X - (textWidth + 20 + costWidth) / 2;
            painter.Text(_billText, x, billY, InkDim, false);
            painter.Sprite(CoinArt, x + textWidth, billY + 1);
            painter.Text(_billCost, x + textWidth + 20, billY, Brass, false);
        }
        else
        {
            painter.TextCentered(_billText, left.X, left.Right, billY, InkDim, false);
        }

        var apply = Apply(window);
        var enabled = _pending && _acting is null && _loading is null;
        painter.Fill(Inflate(apply, 2), new Color(0x0d, 0x2a, 0x1a));
        painter.Fill(apply, Color.Black);
        painter.Stretch(ApplyArt, Inflate(apply, -1), null, !enabled ? new Color(110, 110, 110) : apply.Contains(pointer) ? new Color(235, 255, 240) : Color.White);
        painter.TextCentered("Apply Changes", apply.X, apply.Right, apply.Y + 8, enabled ? Color.White : new Color(200, 200, 200), false);
        painter.TextCentered(painter.Fit(_status, left.Width - 12), left.X, left.Right, apply.Bottom + 8, _statusOk ? OkText : ErrText, false);
    }

    private void DrawSlot(HudPainter painter, HudItemIcons icons, Rectangle box, int slot, Point pointer)
    {
        var changed = Changed(slot);
        if (changed)
        {
            painter.Stretch(GlowArt, Inflate(box, 14), null, Fresh * 0.35f);
        }

        painter.Fill(box, box.Contains(pointer) ? Hover : SlotFace);
        Outline(painter, box, changed ? Fresh : box.Contains(pointer) ? BrassDim : Edge);
        if (_chosen[slot].Length > 0)
        {
            icons.DrawFitted(painter, _chosen[slot], Inflate(box, -5));
        }

        painter.Text(SlotNames[slot], box.Center.X - painter.MeasureAt(SlotNames[slot], TagScale) / 2, box.Bottom + 3, InkDim, TagScale);
        if (changed)
        {
            Badge(painter, UndoBadge(box), TurnArt, pointer);
        }

        if (_chosen[slot].Length > 0)
        {
            Badge(painter, ClearBadge(box), CrossArt, pointer);
        }
    }

    private void DrawRight(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        var right = RightPanel(window);
        Panel(painter, right);
        if (_mode == CollectionMode)
        {
            DrawCollection(painter, window, icons, pointer);
        }
        else if (_mode == BrushMode)
        {
            DrawBrush(painter, window, icons, pointer);
        }
        else
        {
            DrawDeals(painter, window, icons, pointer);
        }
    }

    private void DrawCollection(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        var search = Search(window);
        painter.Fill(search, Field);
        Outline(painter, search, _search.Focused ? BrassDim : Edge);
        var empty = _search.Text.Length == 0 && !_search.Focused;
        painter.Text(painter.Fit(empty ? "Search" : _search.CaretVisible ? _search.Text + "_" : _search.Text, search.Width - 30), search.X + 9, search.Y + 8,
            empty ? Placeholder : Ink, false);
        painter.Stretch(SearchArt, new Rectangle(search.Right - 22, search.Y + 8, 14, 14), null, InkDim);
        for (var menu = SlotMenu; menu <= RarityMenu; menu++)
        {
            var button = MenuButton(window, menu);
            var lit = MenuChoice(menu) != 0;
            painter.Fill(button, button.Contains(pointer) ? Hover : Field);
            Outline(painter, button, lit ? BrassDim : Edge);
            painter.TextCentered(MenuNames(menu)[MenuChoice(menu)], button.X, button.Right - 14, button.Y + 8, lit ? Brass : Ink, false);
            painter.Stretch(ChevronArt, new Rectangle(button.Right - 21, button.Y + 8, 14, 14), null, InkDim);
        }

        var right = RightPanel(window);
        painter.Text(_countText, right.X + 14, right.Y + 50, InkDim, false);
        var progressWidth = painter.Measure(_progressText);
        painter.Text(_progressText, right.Right - 22 - progressWidth, right.Y + 50, BrassDim, false);
        for (var card = 0; card < Columns * Rows; card++)
        {
            var index = _scroll * Columns + card;
            if (index < _shown.Count)
            {
                DrawCard(painter, icons, Card(window, card), _shown[index], pointer);
            }
        }

        if (_shown.Count == 0)
        {
            painter.TextCentered(_articles.Count == 0 ? "Loading..." : NothingText, right.X, right.Right, right.Y + 220, InkDim, false);
        }

        DrawScrollBar(painter, window, _shown.Count);
    }

    private void DrawBrush(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        var right = RightPanel(window);
        painter.Text("Brush gear into its look", right.X + 14, right.Y + 16, Brass, false);
        painter.Text(_brushText, right.X + 14, right.Y + 40, InkDim, false);
        for (var card = 0; card < Columns * Rows; card++)
        {
            var index = _scroll * Columns + card;
            if (index >= _brushable.Count)
            {
                break;
            }

            var item = _brushable[index];
            var look = _brushLooks[index];
            var owned = look is { Owned: true };
            var rarity = look is { } known ? (ItemRarity)known.Rarity : ItemRarity.None;
            var bounds = Card(window, card);
            var lifted = bounds.Contains(pointer) ? bounds with { Y = bounds.Y - 2 } : bounds;
            DrawCardFrame(painter, icons, lifted, item.Name, item.CatalogKey, rarity, false, pointer);
            DrawFoot(painter, lifted, owned ? "Owned" : CabinetRules.IsToken(item.CatalogKey) ? "Use token" : "Brush it", owned ? InkDim : OkText, false);
        }

        if (_brushable.Count == 0)
        {
            painter.TextCentered(NothingToBrush, right.X, right.Right, right.Y + 220, InkDim, false);
        }

        DrawScrollBar(painter, window, _brushable.Count);
    }

    private void DrawDeals(HudPainter painter, Rectangle window, HudItemIcons icons, Point pointer)
    {
        var right = RightPanel(window);
        painter.Text("Bidding Deals", right.X + 14, right.Y + 16, Brass, false);
        for (var index = 0; index < DealRows && index < _deals.Count; index++)
        {
            var deal = _deals[index];
            var row = DealRow(window, index);
            var rarityColor = RarityColor((ItemRarity)deal.Rarity);
            painter.Stretch(CardArt, row, null, row.Contains(pointer) ? new Color(255, 245, 230) : Color.White);
            Outline(painter, row, deal.Leading ? Fresh : Color.Lerp(CardEdge, rarityColor, 0.55f));
            Corners(painter, row, rarityColor);
            var well = new Rectangle(row.X + 8, row.Y + 7, 48, 56);
            painter.Fill(well, Well);
            Outline(painter, well, Edge);
            icons.DrawFitted(painter, deal.Key, Inflate(well, -3));
            painter.Text(painter.Fit(deal.Name, 220), row.X + 66, row.Y + 7, Brass, false);
            painter.Text(_dealTop[index], row.X + 66, row.Y + 27, deal.Leading ? OkText : Ink, false);
            painter.Text(painter.Fit(_dealHistory[index], row.Width - 180), row.X + 66, row.Y + 47, InkDim, false);
            painter.Text(_dealTime[index] ?? "", row.Right - 150, row.Y + 7, Brass, false);
            var bid = BidButton(row);
            painter.Fill(bid, Color.Black);
            painter.Stretch(ApplyArt, Inflate(bid, -1), null, deal.Leading ? new Color(110, 110, 110) : bid.Contains(pointer) ? new Color(235, 255, 240) : Color.White);
            painter.TextCentered(_dealBid[index], bid.X, bid.Right, bid.Y + 6, Color.White, false);
        }

        if (_deals.Count == 0)
        {
            painter.TextCentered(_dealsLoading is null ? NoDeals : "Loading...", right.X, right.Right, right.Y + 220, InkDim, false);
        }
    }

    private void DrawMenu(HudPainter painter, Rectangle window, Point pointer)
    {
        var button = MenuButton(window, _menu);
        var names = MenuNames(_menu);
        var list = new Rectangle(button.X, button.Bottom + 2, button.Width, names.Length * MenuRow);
        painter.Fill(Inflate(list, 1), Edge);
        painter.Fill(list, Field);
        for (var index = 0; index < names.Length; index++)
        {
            var item = MenuItem(button, index);
            var chosen = index == MenuChoice(_menu);
            if (item.Contains(pointer))
            {
                painter.Fill(item, Hover);
            }

            painter.Text(names[index], item.X + 9, item.Y + 4, chosen ? Brass : Ink, false);
        }
    }

    private void FormatDeals()
    {
        _dealTimeSecond = -1;
        for (var index = 0; index < DealRows && index < _deals.Count; index++)
        {
            var deal = _deals[index];
            _dealTop[index] = deal.TopBid > 0 && deal.TopName.Length > 0 ? string.Create(CultureInfo.InvariantCulture, $"Top: {deal.TopBid:N0} by {deal.TopName}") : "No bids yet";
            _dealHistory[index] = History(deal);
            _dealBid[index] = deal.Leading ? "Leading" : string.Create(CultureInfo.InvariantCulture, $"Bid {deal.NextBid:N0}");
            _dealTime[index] = "";
        }
    }

    private static string History(CabinetWireDeal deal)
    {
        if (deal.History.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Starts at {deal.NextBid:N0}");
        }

        var parts = new string[deal.History.Count];
        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = string.Create(CultureInfo.InvariantCulture, $"{deal.History[index].Amount:N0} {deal.History[index].Name}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{deal.Bids} bids: ") + string.Join(", ", parts);
    }

    private static string TimeLeft(long seconds)
    {
        if (seconds <= 0)
        {
            return "Ending";
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalDays >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h left")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Hours:D2}:{span.Minutes:D2}:{span.Seconds:D2} left");
    }

    // The sheet over a dimmed window: a title, rows with their values over dashed lines, the total and the two answers.
    private void DrawConfirm(HudPainter painter, Rectangle window, Point pointer)
    {
        painter.Fill(Inflate(window, 5), Shade);
        var box = ConfirmBox(window);
        Outline(painter, Inflate(box, 4), Edge);
        painter.Fill(Inflate(box, 3), Color.Black);
        painter.Fill(box, Iron);
        Outline(painter, box, Edge);
        painter.Text(_confirmTitle, box.X + 18, box.Y + 18, Brass, HudPainter.TextScale * 16f / 14f);
        var y = box.Y + 52;
        foreach (var row in _confirmRows)
        {
            painter.Text(painter.Fit(row.Left, box.Width - 40 - painter.Measure(row.Right)), box.X + 18, y, Ink, false);
            if (row.Right.Length > 0)
            {
                painter.Text(row.Right, box.Right - 18 - painter.Measure(row.Right), y, Ink, false);
            }

            for (var x = box.X + 18; x < box.Right - 18; x += 6)
            {
                painter.Fill(new Rectangle(x, y + 19, 3, 1), Dashed);
            }

            y += 26;
        }

        if (_confirmTotal is { } total)
        {
            painter.Text("Total", box.X + 18, y + 2, Ink, false);
            var totalWidth = painter.Measure(total);
            painter.Sprite(CoinArt, box.Right - 18 - totalWidth - 20, y + 3);
            painter.Text(total, box.Right - 18 - totalWidth, y + 2, Brass, false);
        }

        var no = ConfirmNo(box);
        painter.Fill(no, no.Contains(pointer) ? Hover : new Color(0x2b, 0x25, 0x20));
        Outline(painter, no, Edge);
        painter.TextCentered("Cancel", no.X, no.Right, no.Y + 7, Ink, false);
        var yes = ConfirmYes(box);
        painter.Fill(yes, Color.Black);
        painter.Stretch(ApplyArt, Inflate(yes, -1), null, yes.Contains(pointer) ? new Color(235, 255, 240) : Color.White);
        painter.TextCentered(_confirmYes, yes.X, yes.Right, yes.Y + 7, Color.White, false);
    }

    private void DrawScrollBar(HudPainter painter, Rectangle window, int count)
    {
        var right = RightPanel(window);
        var bar = new Rectangle(right.Right - 12, right.Y + 72, 5, Rows * (CardHeight + CardGap) - CardGap);
        painter.Fill(bar, Rim);
        var rows = (count + Columns - 1) / Columns;
        if (rows > Rows)
        {
            var thumb = Math.Max(24, bar.Height * Rows / rows);
            painter.Fill(new Rectangle(bar.X, bar.Y + (bar.Height - thumb) * _scroll / (rows - Rows), bar.Width, thumb), new Color(0x5a, 0x4c, 0x3e));
        }
    }

    private void DrawCardName(HudPainter painter, Rectangle card, string name)
    {
        if (!_cardNames.TryGetValue(name, out var lines))
        {
            var width = card.Width - 16;
            var wrapped = HudTextLayout.Wrap(name, width, candidate => painter.Measure(candidate));
            lines = wrapped.Count switch
            {
                0 => ("", ""),
                1 => (painter.Fit(wrapped[0], width), ""),
                _ => (painter.Fit(wrapped[0], width), painter.Fit(string.Join(" ", wrapped.Skip(1)), width))
            };
            _cardNames[name] = lines;
        }

        painter.Text(lines.First, card.X + 8, card.Y + 7, Brass, false);
        painter.Text(lines.Second, card.X + 8, card.Y + 22, Brass, false);
    }

    // A card: the dark gradient, a rarity-tinted border with a glow and filigree corners above Common, the gold name, the look
    // and its rarity's name; green when tried on.
    private void DrawCardFrame(HudPainter painter, HudItemIcons icons, Rectangle card, string name, string key, ItemRarity rarity, bool chosen, Point pointer)
    {
        var hover = card.Contains(pointer);
        var rare = rarity > ItemRarity.Common;
        var rarityColor = RarityColor(rarity);
        if (chosen)
        {
            painter.Stretch(GlowArt, Inflate(card, 16), null, Fresh * 0.45f);
        }

        painter.Stretch(CardArt, card);
        if (rare)
        {
            painter.Stretch(GlowArt, new Rectangle(card.X + 6, card.Y + 26, card.Width - 12, 84), null, rarityColor * 0.3f);
        }

        var border = chosen ? Fresh : hover ? rarityColor : rare ? Color.Lerp(CardEdge, rarityColor, 0.55f) : CardEdge;
        Outline(painter, card, border);
        if (chosen)
        {
            Outline(painter, Inflate(card, 1), Fresh * 0.6f);
        }

        if (rare)
        {
            Corners(painter, card, rarityColor);
        }

        DrawCardName(painter, card, name);
        icons.DrawFitted(painter, key, new Rectangle(card.X + 20, card.Y + 42, card.Width - 40, 54));
        if (rare)
        {
            painter.Text(RarityTags[(int)rarity], card.X + 8, card.Bottom - 38, rarityColor, TagScale);
        }
    }

    private static void Corners(HudPainter painter, Rectangle card, Color color)
    {
        const int arm = 12;
        const int thick = 2;
        painter.Fill(new Rectangle(card.X - 1, card.Y - 1, arm, thick), color);
        painter.Fill(new Rectangle(card.X - 1, card.Y - 1, thick, arm), color);
        painter.Fill(new Rectangle(card.Right - arm + 1, card.Bottom - thick + 1, arm, thick), color);
        painter.Fill(new Rectangle(card.Right - thick + 1, card.Bottom - arm + 1, thick, arm), color);
    }

    private void DrawFoot(HudPainter painter, Rectangle card, string label, Color color, bool coin)
    {
        var y = card.Bottom - 22;
        if (coin)
        {
            var width = painter.Measure(label) + 20;
            var x = card.X + (card.Width - width) / 2;
            painter.Sprite(CoinArt, x, y + 1);
            painter.Text(label, x + 20, y, NeutralText, false);
            return;
        }

        painter.TextCentered(label, card.X, card.Right, y, color, false);
    }

    private void DrawCard(HudPainter painter, HudItemIcons icons, Rectangle card, SkinArticle article, Point pointer)
    {
        var chosen = string.Equals(_chosen[ChosenSlot(article)], article.Key, StringComparison.OrdinalIgnoreCase);
        var shown = card.Contains(pointer) ? card with { Y = card.Y - 2 } : card;
        DrawCardFrame(painter, icons, shown, article.Name, article.Key, (ItemRarity)article.Rarity, chosen, pointer);
        if (Applied(article.Key))
        {
            var pip = new Rectangle(shown.Right - 13, shown.Y + 6, 8, 8);
            painter.Stretch(GlowArt, Inflate(pip, 6), null, Fresh * 0.6f);
            painter.Stretch(DiscArt, pip, null, Fresh);
        }

        if (article.Owned)
        {
            DrawFoot(painter, shown, "Owned", OkText, false);
        }
        else if (!CabinetRules.Purchasable((CosmeticSource)article.Sources))
        {
            DrawFoot(painter, shown, "Exclusive", NeutralText, false);
        }
        else
        {
            DrawFoot(painter, shown, PriceText(article.Price), NeutralText, true);
        }

        var favorite = FavoriteButton(card);
        var on = _favorites.Contains(article.Key);
        painter.Stretch(DiscArt, favorite, null, on ? Brass : favorite.Contains(pointer) ? new Color(0xbb, 0xbb, 0xbb) : new Color(0x7b, 0x6e, 0x62));
        painter.Stretch(DiscArt, Inflate(favorite, -1), null, SlotFace);
        painter.Stretch(StarArt, Inflate(favorite, -5), null, on ? Brass : new Color(0xb3, 0xa6, 0x98));
    }

    // A recessed panel: the well with a black inner line inside the bevel edge.
    private static void Panel(HudPainter painter, Rectangle bounds)
    {
        painter.Fill(bounds, Well);
        Outline(painter, bounds, Edge);
        Outline(painter, Inflate(bounds, -1), Color.Black);
    }

    private static void Tool(HudPainter painter, Rectangle tool, string icon, bool pressed, Point pointer)
    {
        var hover = tool.Contains(pointer);
        painter.Fill(tool, Iron);
        Outline(painter, tool, pressed ? Brass : hover ? BrassDim : Edge);
        painter.Stretch(icon, Inflate(tool, -6), null, pressed ? Brass : hover ? Ink : InkDim);
    }

    private static void Badge(HudPainter painter, Rectangle badge, string icon, Point pointer)
    {
        var hover = badge.Contains(pointer);
        painter.Stretch(DiscArt, badge, null, hover ? new Color(0xbb, 0xbb, 0xbb) : BadgeRim);
        painter.Stretch(DiscArt, Inflate(badge, -1), null, BadgeFace);
        painter.Stretch(icon, Inflate(badge, -4), null, hover ? Color.White : new Color(0xb9, 0xad, 0xa0));
    }

    private static void Outline(HudPainter painter, Rectangle bounds, Color color)
    {
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Y + 1, 1, bounds.Height - 2), color);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y + 1, 1, bounds.Height - 2), color);
    }

    private static Rectangle Inflate(Rectangle bounds, int amount) =>
        new(bounds.X - amount, bounds.Y - amount, bounds.Width + amount * 2, bounds.Height + amount * 2);
}
