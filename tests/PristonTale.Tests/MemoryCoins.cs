using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Coins;

namespace PristonTale.Tests;

// Account coins in memory; a trade's characters go through the character book, after every transfer fits.
internal sealed class MemoryCoins(MemoryCharacterBook characters) : ICoinRepository
{
    public Dictionary<long, long> Balances { get; } = [];

    public Task<long> BalanceAsync(long accountId, CancellationToken ct = default) => Task.FromResult(Balances.GetValueOrDefault(accountId));

    public Task<long?> AddAsync(long accountId, long amount, string reason, CancellationToken ct = default)
    {
        var balance = Balances.GetValueOrDefault(accountId) + amount;
        if (balance < 0)
        {
            return Task.FromResult<long?>(null);
        }

        Balances[accountId] = balance;
        return Task.FromResult<long?>(balance);
    }

    public async Task<long?> BuyWithCharacterAsync(CharacterRecord character, long price, string reason, CancellationToken ct = default) =>
        Balances.GetValueOrDefault(character.AccountId) < price || !await characters.UpdateAllAsync([character], ct)
            ? null
            : await AddAsync(character.AccountId, -price, reason, ct);

    public Dictionary<long, List<OwnedSkinRecord>> Skins { get; } = new();

    public Task<IReadOnlyList<string>> OwnedSkinsAsync(long accountId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>((Skins.GetValueOrDefault(accountId) ?? []).Select(static skin => skin.ItemKey).ToList());

    public Task<IReadOnlyList<OwnedSkinRecord>> SkinRecordsAsync(long accountId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OwnedSkinRecord>>(Skins.GetValueOrDefault(accountId) ?? []);

    public async Task<long?> BuySkinAsync(long accountId, string itemKey, long price, string reason, long unlockedAt = 0, CancellationToken ct = default)
    {
        if (Balances.GetValueOrDefault(accountId) < price || Owns(accountId, itemKey))
        {
            return null;
        }

        Insert(new OwnedSkinRecord(accountId, itemKey, unlockedAt, 1, 0));
        return await AddAsync(accountId, -price, reason, ct);
    }

    public Task<bool> UnlockSkinAsync(OwnedSkinRecord skin, CancellationToken ct = default) => Task.FromResult(Insert(skin));

    public async Task<SkinUnlockStore> UnlockSkinWithCharacterAsync(CharacterRecord character, OwnedSkinRecord skin, CancellationToken ct = default)
    {
        if (Owns(skin.AccountId, skin.ItemKey))
        {
            return SkinUnlockStore.Owned;
        }

        if (!await characters.UpdateAllAsync([character], ct))
        {
            return SkinUnlockStore.Failed;
        }

        Insert(skin);
        return SkinUnlockStore.Stored;
    }

    public Task<int> ResetSkinsAsync(long accountId, CancellationToken ct = default)
    {
        var count = Skins.Remove(accountId, out var owned) ? owned.Count : 0;
        return Task.FromResult(count);
    }

    private bool Owns(long accountId, string key) =>
        Skins.TryGetValue(accountId, out var owned) && owned.Exists(skin => string.Equals(skin.ItemKey, key, StringComparison.OrdinalIgnoreCase));

    private bool Insert(OwnedSkinRecord skin)
    {
        if (Owns(skin.AccountId, skin.ItemKey))
        {
            return false;
        }

        if (!Skins.TryGetValue(skin.AccountId, out var owned))
        {
            Skins[skin.AccountId] = owned = [];
        }

        owned.Add(skin);
        return true;
    }

    public async Task<CoinTradeStore> TradeWithCharactersAsync(IReadOnlyList<CharacterRecord> records, IReadOnlyList<CoinTransfer> transfers,
        CancellationToken ct = default)
    {
        var after = new Dictionary<long, long>(Balances);
        foreach (var transfer in transfers)
        {
            after[transfer.FromAccountId] = after.GetValueOrDefault(transfer.FromAccountId) - transfer.Amount;
            if (after[transfer.FromAccountId] < 0)
            {
                return CoinTradeStore.NotEnoughCoins;
            }

            after[transfer.ToAccountId] = after.GetValueOrDefault(transfer.ToAccountId) + transfer.Received;
        }

        if (!await characters.UpdateAllAsync(records, ct))
        {
            return CoinTradeStore.Failed;
        }

        foreach (var (account, balance) in after)
        {
            Balances[account] = balance;
        }

        return CoinTradeStore.Stored;
    }
}
