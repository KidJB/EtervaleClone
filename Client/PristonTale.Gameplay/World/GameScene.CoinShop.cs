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

    public Task<byte> DailyQuestAsync(ulong npcId, int questId, byte action) =>
        _playChannel is { } channel ? channel.DailyQuestAsync(npcId, questId, action, _presenceCts.Token) : Task.FromResult((byte)8);

    public Task<SkinCatalogInfo> OpenSkinsAsync() =>
        _playChannel is { } channel ? channel.OpenSkinsAsync(_presenceCts.Token) : Task.FromResult(new SkinCatalogInfo(0, Simulation.Characters.SkinSet.None, []));

    public Task<(Simulation.Characters.SkinStatus Status, long Balance)> BuySkinAsync(string key) =>
        _playChannel is { } channel ? channel.BuySkinAsync(key, _presenceCts.Token) : Task.FromResult((Simulation.Characters.SkinStatus.Rejected, 0L));

    public async Task<(Simulation.Characters.SkinStatus Status, long Balance)> ApplySkinsAsync(Simulation.Characters.SkinSet skins)
    {
        if (_playChannel is not { } channel)
        {
            return (Simulation.Characters.SkinStatus.Rejected, 0L);
        }

        var result = await channel.ApplySkinsAsync(skins, _presenceCts.Token);
        if (result.Status == Simulation.Characters.SkinStatus.Ok && _equipmentPresentation is { } presentation)
        {
            presentation.Skins = skins;
        }

        return result;
    }

    // The looks the character came in with, read once it stands in the world.
    private async Task LoadSkinsAsync(Remote.PlayerEquipmentPresentation presentation)
    {
        try
        {
            presentation.Skins = (await OpenSkinsAsync()).Skins;
        }
        catch (Exception failure) when (failure is OperationCanceledException or IOException or InvalidOperationException)
        {
            _log.Warn($"[Equipment] Skins not read: {failure.Message}");
        }
    }

    public Task<long> CoinBalanceAsync() => _playChannel is { } channel ? channel.CoinBalanceAsync(_presenceCts.Token) : Task.FromResult(0L);
}
