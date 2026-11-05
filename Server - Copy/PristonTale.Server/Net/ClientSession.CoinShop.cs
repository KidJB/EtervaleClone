using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Shops;

namespace PristonTale.Server.Net;

// The coin shop's messages: an open answers each tab (up to CoinShopWire's limits) then the catalog with the balance; a
// purchase answers its result with the new balance.
internal sealed partial class ClientSession
{
    private async Task OpenCoinShopAsync(CoinShopOpenPacket request)
    {
        if (_session is null || _player is null)
        {
            return;
        }

        var catalog = _services.CoinShop.Catalog;
        var balance = await _services.CoinShop.BalanceAsync(RequireToken());
        var tabs = Math.Min(catalog.Tabs.Count, CoinShopWire.MaxTabs);
        for (var index = 0; index < tabs; index++)
        {
            var tab = catalog.Tabs[index];
            var articles = new CoinShopArticle[Math.Min(tab.Items.Count, CoinShopWire.MaxItemsPerTab)];
            for (var item = 0; item < articles.Length; item++)
            {
                articles[item] = Article(tab.Items[item]);
            }

            var packet = new CoinShopTabPacket(request.OperationId, tab.Id, tab.Name, tab.Icon, articles);
            SendLarge(packet.Write, CoinShopTabPacket.MaxSize);
        }

        var end = new CoinShopCatalogPacket(request.OperationId, balance, tabs, catalog.Name, catalog.Message);
        SendLarge(end.Write, CoinShopCatalogPacket.MaxSize);
    }

    private async Task BuyCoinShopAsync(CoinShopBuyPacket request)
    {
        CoinShopReply reply;
        try
        {
            AdmitStoredOperation();
            reply = _session is null || _player is null
                ? new CoinShopReply(CoinShopStatus.Rejected, 0)
                : await _services.CoinShop.BuyAsync(RequireToken(),
                    new CoinShopPurchase(request.OperationId, request.TabId, request.ItemId, request.Quantity, request.SpecClass));
        }
        catch (WorldBusyException)
        {
            reply = new CoinShopReply(CoinShopStatus.Busy, 0);
        }

        var result = new CoinShopBuyResultPacket(request.OperationId, (byte)reply.Status, reply.Balance);
        SendLarge(result.Write, CoinShopBuyResultPacket.Size);
    }

    private static CoinShopArticle Article(CoinShopItem item) =>
        new(item.Id, item.Name, item.Description, item.ItemKey, item.Price, item.Discount, item.Bulk, item.MaxBulk,
            (byte)((item.Spec ? CoinShopWire.FlagSpec : 0) | (item.Quantity ? CoinShopWire.FlagQuantity : 0)));
}
