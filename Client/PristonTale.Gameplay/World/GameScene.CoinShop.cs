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

    public Task<ItemBoxListInfo> OpenItemBoxAsync(ulong npcId) =>
        _playChannel is { } channel ? channel.OpenItemBoxAsync(npcId, _presenceCts.Token) : Task.FromResult(new ItemBoxListInfo(8, []));

    public Task<byte> ActItemBoxAsync(ulong npcId, byte action, long id, Guid itemId, string recipient) =>
        _playChannel is { } channel ? channel.ActItemBoxAsync(npcId, action, id, itemId, recipient, _presenceCts.Token) : Task.FromResult((byte)8);

    public Task<long> CoinBalanceAsync() => _playChannel is { } channel ? channel.CoinBalanceAsync(_presenceCts.Token) : Task.FromResult(0L);
}
