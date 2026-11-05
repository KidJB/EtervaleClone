using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Shops;

namespace PristonTale.Gameplay.World;

// The coin shop's requests through the play channel; with no channel the shop opens empty and buys nothing.
public sealed partial class GameScene
{
    public Task<CoinShopView> OpenCoinShopAsync() =>
        _playChannel is { } channel ? channel.OpenCoinShopAsync(_presenceCts.Token) : Task.FromResult(new CoinShopView(CoinShopCatalog.Empty, 0));

    public Task<(CoinShopStatus Status, long Balance)> BuyCoinShopAsync(int tabId, int itemId, int quantity, int specClass) =>
        _playChannel is { } channel
            ? channel.BuyCoinShopAsync(tabId, itemId, quantity, specClass, _presenceCts.Token)
            : Task.FromResult((CoinShopStatus.Rejected, 0L));
}
