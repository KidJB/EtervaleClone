using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Npc;

// The Cabinet: skins for the worn gear, opened from its NPC or the U key. The left page shows the character in the chosen
// looks with one box per slot (a click clears it); the right page is a grid of skin cards with search, slot and owned
// filters, showing only what fits the worn gear and the class (SkinRules). A click on a card tries the look on; Apply Changes buys what is not owned yet, then shows the looks.
public sealed class HudCabinet
{
    private const int Width = 720;
    private const int Height = 440;
    private const int PageWidth = 250;
    private const int CardWidth = 82;
    private const int CardHeight = 138;
    private const int CardGap = 6;
    private const int Columns = 5;
    private const int Rows = 2;
    private const string CoinArt = "Hud/CoinShop/coin.bmp";
    public static readonly string[] Art = [CoinArt];
    private const string NothingText = "No skins fit your gear here. Equip an item to see its skins.";
    private static readonly string[] FilterNames = ["All", "Weapon", "Armor", "Off", "Hair"];
    private static readonly string[] SlotNames = ["Weapon", "Armor", "Off-hand", "Hair"];
    private const int PlateWidth = 220;
    private static readonly Color BorderColor = HudPteuWindow.BorderColor;
    private static readonly Color TextColor = HudPteuWindow.TextColor;
    private static readonly Color NameColor = HudPteuWindow.SectionColor;
    private static readonly Color CoinColor = new(255, 214, 90);
    private static readonly Color ChosenColor = new(70, 210, 140);
    private static readonly Color BadColor = new(240, 110, 90);
    private static readonly Color ThumbColor = new(150, 130, 100);
    private static readonly Color CardColor = new(30, 26, 23, 255);
    private static readonly Color CardLitColor = new(50, 42, 32, 255);
    private static readonly Color PriceBandColor = new(12, 10, 9, 255);
    private static readonly Color TabColor = new(36, 31, 27, 255);
    private static readonly Color TabLitColor = new(26, 92, 62, 255);
    private static readonly Color DimColor = new(160, 150, 135);
    private static readonly Color BarBackColor = new(14, 10, 7, 255);

    private readonly TextInputField _search = new() { MaxLength = 24 };
    private readonly List<SkinArticle> _shown = [];
    private readonly string[] _chosen = ["", "", "", ""];
    private readonly string[] _worn = ["", "", "", ""];
    private readonly List<string> _iconKeys = [];
    private IReadOnlyList<SkinArticle> _articles = [];
    private Task<SkinCatalogInfo> _loading;
    private Task<SkinStatus> _applying;
    private SkinSet _applied = SkinSet.None;
    private long _balance;
    private string _balanceText = "0";
    private string _status = "";
    private string _applyText = "";
    private string _countText = "";
    private int _filter;
    private bool _ownedOnly;
    private int _scroll;
    private long _wornRevision = -1;
    private string _searched = "";

    public bool Open { get; private set; }

    // The player's job bit: only what the class wears is shown.
    public uint Jobs { get; set; }
    public bool Searching => Open && _search.Focused;
    public Microsoft.Xna.Framework.Graphics.Texture2D Picture { get; set; }
    public Func<Task<SkinCatalogInfo>> ListRequest { get; set; }
    public Func<string, Task<(SkinStatus Status, long Balance)>> BuyRequest { get; set; }
    public Func<SkinSet, Task<(SkinStatus Status, long Balance)>> ApplyRequest { get; set; }

    // The worn gear in the chosen looks, for the preview; a new object only when it changes.
    public SkinSet Preview { get; private set; }

    public int IconRevision { get; private set; }
    public IEnumerable<string> IconKeys => Open ? _iconKeys : [];

    public static Rectangle Window(HudLayout layout) => new((layout.Width - Width) / 2, Math.Max(0, (layout.Height - Height) / 2 - 40), Width, Height);

    // The CLOSE button under the cards, centred on the right page.
    private static Rectangle Close(Rectangle window) => HudPteuWindow.CloseButton((window.X + 274 + window.Right - 12) / 2, window.Bottom - 34);
    private static Rectangle Left(Rectangle window) => new(window.X + 12, window.Y + 40, PageWidth, Height - 52);
    private static Rectangle SlotBox(Rectangle window, int slot) => new(window.X + 12 + PageWidth - 52, window.Y + 52 + slot * 58, 46, 50);
    private static Rectangle Apply(Rectangle window) => new(window.X + 42, window.Bottom - 46, 190, 24);
    private static Rectangle Search(Rectangle window) => new(window.X + 274, window.Y + 44, 130, 20);
    private static Rectangle Filter(Rectangle window, int index) => new(window.X + 410 + index * 50, window.Y + 44, 48, 20);
    private static Rectangle Owned(Rectangle window) => new(window.Right - 58, window.Y + 44, 48, 20);

    private static Rectangle Card(Rectangle window, int index) =>
        new(window.X + 274 + index % Columns * (CardWidth + CardGap), window.Y + 72 + index / Columns * (CardHeight + CardGap), CardWidth, CardHeight);

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
        _status = "";
        _loading = ListRequest?.Invoke();
    }

    public void Hide()
    {
        Open = false;
        Preview = null;
        _search.Focused = false;
    }

    public void CancelSearch() => _search.Focused = false;

    public void Type(ReadOnlySpan<char> typed, double elapsedSeconds) => _search.Update(typed, elapsedSeconds);

    public bool Captures(HudLayout layout, Point pointer) => Open && Window(layout).Contains(pointer);

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var rows = (_shown.Count + Columns - 1) / Columns;
        SetScroll(Math.Clamp(_scroll - Math.Sign(wheelDelta), 0, Math.Max(0, rows - Rows)));
    }

    public void Update(OwnerState state)
    {
        if (_loading is { IsCompleted: true } loading)
        {
            _loading = null;
            if (loading.IsCompletedSuccessfully)
            {
                var info = loading.Result;
                _articles = info.Articles;
                _applied = info.Skins;
                (_chosen[0], _chosen[1], _chosen[2], _chosen[3]) = (info.Skins.Weapon, info.Skins.Armor, info.Skins.OffHand, info.Skins.Hair);
                SetBalance(info.Balance);
                Refilter();
            }
        }

        if (_applying is { IsCompleted: true } applying)
        {
            _applying = null;
            var status = applying.IsCompletedSuccessfully ? applying.Result : SkinStatus.Rejected;
            _status = status == SkinStatus.Ok ? "Changes applied." : status == SkinStatus.NotEnoughCoins ? "Not enough coins." : "The change was refused.";
            _loading = ListRequest?.Invoke();
        }

        if (!Open)
        {
            return;
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
            if (slot >= 0)
            {
                _worn[slot] = item.CatalogKey;
            }
        }
    }

    private void RefreshPreview()
    {
        Preview = new SkinSet(SkinRules.Look(_worn[0], _chosen[0]), SkinRules.Look(_worn[1], _chosen[1]), SkinRules.Look(_worn[2], _chosen[2]), _chosen[3]);
        var cost = 0L;
        foreach (var key in _chosen)
        {
            cost += Find(key) is { Owned: false } article ? article.Price : 0;
        }

        _applyText = cost > 0 ? string.Create(CultureInfo.InvariantCulture, $"Apply Changes ({cost:N0})") : "Apply Changes";
    }

    private SkinArticle? Find(string key)
    {
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

    private void Refilter()
    {
        _shown.Clear();
        var owned = 0;
        foreach (var article in _articles)
        {
            owned += article.Owned ? 1 : 0;
            if ((article.Slot != (byte)SkinSlot.Armor || World.Remote.EquipmentVisuals.HasBody(article.Key)) && (article.Slot == (byte)SkinSlot.Hair || SkinRules.Fits(_worn[article.Slot], article.Key) && SkinRules.Wearable(article.Key, Jobs)) && (_filter == 0 || article.Slot == _filter - 1) && (!_ownedOnly || article.Owned)
                && (_search.Text.Length == 0 || article.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                _shown.Add(article);
            }
        }

        _countText = string.Create(CultureInfo.InvariantCulture, $"{owned} / {_articles.Count} Skins");
        SetScroll(0);
        RefreshPreview();
    }

    private void SetScroll(int scroll)
    {
        _scroll = scroll;
        _iconKeys.Clear();
        for (var index = _scroll * Columns; index < _shown.Count && index < (_scroll + Rows) * Columns; index++)
        {
            _iconKeys.Add(_shown[index].Key);
        }

        IconRevision++;
    }

    public bool Click(HudLayout layout, Point pointer)
    {
        var window = Window(layout);
        if (!Open || !window.Contains(pointer))
        {
            return false;
        }

        _search.Focused = Search(window).Contains(pointer);
        if (Close(window).Contains(pointer))
        {
            Hide();
            return true;
        }

        if (Owned(window).Contains(pointer))
        {
            _ownedOnly = !_ownedOnly;
            Refilter();
            return true;
        }

        for (var index = 0; index < FilterNames.Length; index++)
        {
            if (Filter(window, index).Contains(pointer))
            {
                _filter = index;
                Refilter();
                return true;
            }
        }

        for (var slot = 0; slot < _chosen.Length; slot++)
        {
            if (SlotBox(window, slot).Contains(pointer))
            {
                _chosen[slot] = "";
                RefreshPreview();
                return true;
            }
        }

        for (var card = 0; card < Columns * Rows; card++)
        {
            var index = _scroll * Columns + card;
            if (index < _shown.Count && Card(window, card).Contains(pointer))
            {
                var article = _shown[index];
                _chosen[article.Slot] = string.Equals(_chosen[article.Slot], article.Key, StringComparison.OrdinalIgnoreCase) ? "" : article.Key;
                RefreshPreview();
                return true;
            }
        }

        if (Apply(window).Contains(pointer) && _applying is null && _loading is null)
        {
            _status = "";
            _applying = BuyAndApplyAsync(new SkinSet(_chosen[0], _chosen[1], _chosen[2], _chosen[3]));
        }

        return true;
    }

    private async Task<SkinStatus> BuyAndApplyAsync(SkinSet skins)
    {
        foreach (var key in (string[])[skins.Weapon, skins.Armor, skins.OffHand, skins.Hair])
        {
            if (Find(key) is { Owned: false })
            {
                var (status, _) = await BuyRequest(key);
                if (status is not SkinStatus.Ok and not SkinStatus.AlreadyOwned)
                {
                    return status;
                }
            }
        }

        return (await ApplyRequest(skins)).Status;
    }

    public void Draw(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        HudPteuWindow.Frame(painter, window);
        HudPteuWindow.DrawPlate(painter, HudPteuWindow.Plate(window, PlateWidth), "The Cabinet");
        var balanceWidth = painter.Measure(_balanceText);
        painter.Text(_balanceText, window.Right - 16 - balanceWidth, window.Y + 14, CoinColor);
        painter.Sprite(CoinArt, window.Right - 42 - balanceWidth, window.Y + 15);
        HudPteuWindow.DrawClose(painter, Close(window), pointer);

        var left = Left(window);
        HudPteuWindow.Panel(painter, left);
        if (Picture is { } texture)
        {
            painter.Image(texture, new Rectangle(left.X + 6, left.Y + 26, 188, 270));
        }

        for (var slot = 0; slot < _chosen.Length; slot++)
        {
            var box = SlotBox(window, slot);
            painter.Fill(box, box.Contains(pointer) ? CardLitColor : CardColor);
            HudPteuWindow.Border(painter, box, _chosen[slot].Length > 0 ? ChosenColor : BorderColor);
            if (_chosen[slot].Length == 0)
            {
                painter.TextCentered(SlotNames[slot], box.X, box.Right, box.Y + 18, DimColor);
            }
            else
            {
                icons.Draw(painter, _chosen[slot], new Rectangle(box.X + 3, box.Y + 3, box.Width - 6, box.Height - 6));
            }
        }

        var apply = Apply(window);
        painter.ColorBox(apply, 30, 110, 70, apply.Contains(pointer) ? 255 : 210);
        HudPteuWindow.Border(painter, apply, ChosenColor);
        painter.TextCentered(_applyText, apply.X, apply.Right, apply.Y + 5, TextColor);
        painter.TextCentered(_status, left.X, left.Right, apply.Y - 18, TextColor);
        painter.TextCentered(_countText, left.X, left.Right, window.Bottom - 20, TextColor);

        var search = Search(window);
        painter.InputBox(search);
        var empty = _search.Text.Length == 0 && !_search.Focused;
        painter.Text(empty ? "Search" : _search.CaretVisible ? _search.Text + "_" : _search.Text, search.X + 4, search.Y + 2, empty ? DimColor : TextColor);
        for (var index = 0; index < FilterNames.Length; index++)
        {
            var filter = Filter(window, index);
            painter.Fill(filter, index == _filter ? TabLitColor : TabColor);
            HudPteuWindow.Border(painter, filter, index == _filter ? ChosenColor : BorderColor);
            painter.TextCentered(FilterNames[index], filter.X, filter.Right, filter.Y + 2, TextColor);
        }

        var owned = Owned(window);
        painter.Fill(owned, _ownedOnly ? TabLitColor : TabColor);
        HudPteuWindow.Border(painter, owned, _ownedOnly ? ChosenColor : BorderColor);
        painter.TextCentered("Owned", owned.X, owned.Right, owned.Y + 2, TextColor);

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
            painter.TextCentered(_articles.Count == 0 ? "Loading..." : NothingText, window.X + 274, window.Right - 18, window.Y + 200, DimColor);
        }

        DrawScrollBar(painter, window);
    }

    private void DrawScrollBar(HudPainter painter, Rectangle window)
    {
        var bar = new Rectangle(window.Right - 12, window.Y + 72, 6, Rows * CardHeight + CardGap);
        painter.Fill(bar, BarBackColor);
        var rows = (_shown.Count + Columns - 1) / Columns;
        if (rows > Rows)
        {
            var thumb = Math.Max(16, bar.Height * Rows / rows);
            painter.Fill(new Rectangle(bar.X, bar.Y + (bar.Height - thumb) * _scroll / (rows - Rows), bar.Width, thumb), ThumbColor);
        }
    }

    private void DrawCard(HudPainter painter, HudItemIcons icons, Rectangle card, SkinArticle article, Point pointer)
    {
        painter.Fill(card, card.Contains(pointer) ? CardLitColor : CardColor);
        var chosen = string.Equals(_chosen[article.Slot], article.Key, StringComparison.OrdinalIgnoreCase);
        HudPteuWindow.Border(painter, card, chosen ? ChosenColor : card.Contains(pointer) ? CoinColor : BorderColor);
        painter.Text(painter.Fit(article.Name, card.Width - 8), card.X + 4, card.Y + 6, NameColor);
        icons.Draw(painter, article.Key, new Rectangle(card.X + 15, card.Y + 34, 52, 68));
        painter.Fill(new Rectangle(card.X + 1, card.Bottom - 25, card.Width - 2, 24), PriceBandColor);
        var label = article.Owned ? "Owned" : article.Price.ToString("N0", CultureInfo.InvariantCulture);
        painter.TextCentered(label, card.X + (article.Owned ? 0 : 16), card.Right, card.Bottom - 20, article.Owned ? ChosenColor : CoinColor);
        if (!article.Owned)
        {
            painter.Sprite(CoinArt, card.X + 8, card.Bottom - 19);
        }
    }
}
