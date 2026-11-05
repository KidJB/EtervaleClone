using System.Globalization;
using System.Text.Json;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Shops;

namespace PristonTale.Application.Services;

// SpecClass: the class (PlayerClass number) whose specialization the buyer chose for a Spec article, 0 for none.
public sealed record CoinShopPurchase(Guid OperationId, int TabId, int ItemId, int Quantity, int SpecClass);

public sealed record CoinShopReply(CoinShopStatus Status, long Balance);

// The coin shop's articles, read from a JSON file beside the server (coinshop.json) at start and on a game master's
// reload. A missing or unreadable file leaves an empty shop and says why.
public sealed class CoinShopCatalogStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true };

    public CoinShopCatalog Current { get; private set; } = CoinShopCatalog.Empty;

    public string Path => path;

    // The number of articles read, or the reason none were.
    public string Reload()
    {
        if (!File.Exists(path))
        {
            Current = CoinShopCatalog.Empty;
            return $"no coin shop file at {path}";
        }

        try
        {
            Current = JsonSerializer.Deserialize<CoinShopCatalog>(File.ReadAllText(path), Options) ?? CoinShopCatalog.Empty;
            var count = 0;
            foreach (var tab in Current.Tabs)
            {
                count += tab.Items.Count;
            }

            return string.Create(CultureInfo.InvariantCulture, $"coin shop: {Current.Tabs.Count} tabs, {count} articles");
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            Current = CoinShopCatalog.Empty;
            return $"coin shop file unreadable: {exception.Message}";
        }
    }
}

// The coin shop outside the tick. A purchase is checked against the catalog and the balance, prepared on the tick as an
// item received into the bag, stored with the coins taken in one transaction (ICoinRepository.BuyWithCharacterAsync)
// and only then committed, so a crash never takes coins without the item or gives the item without the coins.
public sealed class CoinShopAppService(ISessionRegistry sessions, ICharacterSaves saves, ICoinRepository coins, WorldServer world,
    WorldDataHolder worldData, CoinShopCatalogStore catalog)
{
    private readonly DurableOperations _durable = new(saves);

    public CoinShopCatalog Catalog => catalog.Current;

    public async Task<long> BalanceAsync(string sessionToken, CancellationToken ct = default) =>
        sessions.TryGet(sessionToken, out var session) ? await coins.BalanceAsync(session.AccountId, ct) : 0;

    public async Task<CoinShopReply> BuyAsync(string sessionToken, CoinShopPurchase purchase, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new CoinShopReply(CoinShopStatus.Rejected, 0);
        }

        var article = catalog.Current.Find(purchase.TabId, purchase.ItemId);
        if (article is null || !ItemCodes.TryParse(article.ItemKey, out var code) || !worldData.Current.Items.TryGet(code, out var definition))
        {
            return new CoinShopReply(CoinShopStatus.UnknownItem, await coins.BalanceAsync(session.AccountId, ct));
        }

        var quantity = CoinShopPricing.Quantity(article, purchase.Quantity, definition.MaxStack);
        var price = CoinShopPricing.Total(article, quantity);
        if (await coins.BalanceAsync(session.AccountId, ct) < price)
        {
            return new CoinShopReply(CoinShopStatus.NotEnoughCoins, await coins.BalanceAsync(session.AccountId, ct));
        }

        var spec = article.Spec && Enum.IsDefined((PlayerClass)purchase.SpecClass) && JobBits.Of((PlayerClass)purchase.SpecClass) != 0
            && ItemRoller.CanTakeSpec(definition, (uint)JobBits.Of((PlayerClass)purchase.SpecClass))
            ? (uint)JobBits.Of((PlayerClass)purchase.SpecClass)
            : 0u;
        var reason = string.Create(CultureInfo.InvariantCulture, $"bought {article.ItemKey} x{quantity} (tab {purchase.TabId}, article {purchase.ItemId})");
        long? balance = null;
        var shortOfCoins = false;
        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        var bought = await lease.RunAsync(
            leased => world.PrepareGiftAsync(leased.EntityId, leased, _ => Make(definition, quantity, spec)),
            world.CommitGiftAsync,
            prepared => prepared.Outcome,
            _ => false,
            async (leased, prepared) =>
            {
                balance = await coins.BuyWithCharacterAsync(leased.Stamp(prepared.Snapshot!), price, reason, CancellationToken.None);
                shortOfCoins = balance is null;
                return balance is null ? DurableFailure.Conflict : null;
            },
            storeFirst: static prepared => prepared.Outcome);
        var status = bought ? CoinShopStatus.Bought
            : shortOfCoins ? CoinShopStatus.NotEnoughCoins
            : lease.Failure == DurableFailure.Busy ? CoinShopStatus.Busy
            : CoinShopStatus.NoRoom;
        return new CoinShopReply(status, balance ?? await coins.BalanceAsync(session.AccountId, ct));
    }

    // A stack of a stackable article; gear comes as its best copy, specialized when the buyer chose a class.
    private static ItemInstance Make(ItemDefinition definition, int quantity, uint spec) =>
        definition.IsStackable ? new ItemInstance(definition, quantity) : ItemRoller.CreatePerfect(definition, 1, spec);
}
