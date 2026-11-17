using System.Globalization;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.Services;

public sealed record SkinCatalogView(long Balance, SkinSet Skins, IReadOnlyList<SkinArticle> Articles);

// The Cabinet outside the tick. The account owns its looks (account_skins), so every character of it may show a look its
// class wears. A look is bought with coins only when its sources include Coins (data/cabinet.json, CabinetLooks); an
// exclusive look comes from a token, a brushed item, a quest, an event, a bidding deal or a game master. Unlocking from a
// bag item is prepared on the tick, stored with the character in one transaction and only then committed. The looks a
// character shows are set on the tick and saved with it; they never change what the worn items do.
public sealed class SkinAppService(ISessionRegistry sessions, ICharacterSaves saves, ICoinRepository coins, WorldServer world, WorldDataHolder worldData)
{
    private readonly DurableOperations _durable = new(saves);

    public async Task<SkinCatalogView> CatalogAsync(string sessionToken, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new SkinCatalogView(0, SkinSet.None, []);
        }

        var owned = (await coins.OwnedSkinsAsync(session.AccountId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var articles = new List<SkinArticle>();
        foreach (var look in CabinetLooks.All(worldData.Current.Items, world.Cabinet))
        {
            var has = owned.Contains(look.Key);
            if (look.Enabled || has)
            {
                articles.Add(Article(look, has));
            }
        }

        articles.Sort(static (left, right) => right.Price.CompareTo(left.Price));
        return new SkinCatalogView(await coins.BalanceAsync(session.AccountId, ct), await world.SkinsOfAsync(session), articles);
    }

    public static SkinArticle Article(CabinetLook look, bool owned) =>
        new(look.Key, look.Name, (byte)look.Slot, look.Level, look.Price, owned)
        {
            Category = (byte)look.Category,
            Rarity = (byte)look.Rarity,
            Sources = (byte)(look.Enabled ? look.Sources : CosmeticSource.None),
            Description = look.Description
        };

    public async Task<(SkinStatus Status, long Balance)> BuyAsync(string sessionToken, string key, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return (SkinStatus.Rejected, 0);
        }

        var balance = await coins.BalanceAsync(session.AccountId, ct);
        if (Find(key) is not { Enabled: true } look)
        {
            return (SkinStatus.UnknownSkin, balance);
        }

        if (!look.Purchasable)
        {
            return (SkinStatus.NotForSale, balance);
        }

        if ((await coins.OwnedSkinsAsync(session.AccountId, ct)).Contains(look.Key, StringComparer.OrdinalIgnoreCase))
        {
            return (SkinStatus.AlreadyOwned, balance);
        }

        var reason = string.Create(CultureInfo.InvariantCulture, $"bought skin {look.Key}");
        var paid = await coins.BuySkinAsync(session.AccountId, look.Key, look.Price, reason, world.UtcNow.ToUnixTimeSeconds(), ct);
        return paid is { } left ? (SkinStatus.Ok, left) : (SkinStatus.NotEnoughCoins, balance);
    }

    // A brush turns a bag item into its look, or a token gives its look (WorldServer.PrepareCabinetUnlockAsync); the look
    // unlocked is in the reply. Nothing is taken when the account owns the look by the time it is stored.
    public async Task<(SkinStatus Status, string Key, long Balance)> UnlockFromItemAsync(string sessionToken, Guid itemId, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return (SkinStatus.Rejected, "", 0);
        }

        var owned = await coins.OwnedSkinsAsync(session.AccountId, ct);
        var key = "";
        var ownedMeanwhile = false;
        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        var status = await lease.RunAsync(
            leased => world.PrepareCabinetUnlockAsync(leased, itemId, owned),
            world.CommitCabinetUnlockAsync,
            prepared =>
            {
                key = prepared.Outcome.Key;
                return prepared.Outcome.Status;
            },
            failure => ownedMeanwhile ? SkinStatus.AlreadyOwned : failure == DurableFailure.Busy ? SkinStatus.Busy : SkinStatus.Rejected,
            async (leased, prepared) =>
            {
                if (prepared.Outcome.Status != SkinStatus.Ok)
                {
                    return null;
                }

                var unlock = prepared.Outcome;
                var skin = new OwnedSkinRecord(session.AccountId, unlock.Key, world.UtcNow.ToUnixTimeSeconds(), (short)unlock.Source, session.Loaded?.Id ?? 0);
                var stored = await coins.UnlockSkinWithCharacterAsync(leased.Stamp(prepared.Snapshot!), skin, CancellationToken.None);
                ownedMeanwhile = stored == SkinUnlockStore.Owned;
                return stored == SkinUnlockStore.Stored ? null : stored == SkinUnlockStore.Owned ? DurableFailure.Conflict : DurableFailure.StorageFailed;
            },
            storeFirst: static prepared => prepared.Outcome.Status == SkinStatus.Ok);
        return (status, key, await coins.BalanceAsync(session.AccountId, ct));
    }

    // Every look must be an owned skin of its slot ("" clears the slot); the class check is the world's.
    public async Task<(SkinStatus Status, long Balance)> ApplyAsync(string sessionToken, SkinSet skins, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return (SkinStatus.Rejected, 0);
        }

        var balance = await coins.BalanceAsync(session.AccountId, ct);
        var owned = await coins.OwnedSkinsAsync(session.AccountId, ct);
        if (!Allowed(skins.Weapon, SkinSlot.Weapon, owned) || !Allowed(skins.TwoHand, SkinSlot.Weapon, owned) || !Allowed(skins.Armor, SkinSlot.Armor, owned)
            || !Allowed(skins.OffHand, SkinSlot.OffHand, owned) || !Allowed(skins.Hair, SkinSlot.Hair, owned))
        {
            return (SkinStatus.NotOwned, balance);
        }

        return (await world.ApplySkinsAsync(session, skins) ? SkinStatus.Ok : SkinStatus.Rejected, balance);
    }

    private bool Allowed(string key, SkinSlot slot, IReadOnlyCollection<string> owned) =>
        key.Length == 0 || owned.Contains(key, StringComparer.OrdinalIgnoreCase) && Find(key) is { } look && look.Slot == slot;

    private CabinetLook? Find(string key) => CabinetLooks.Find(key, worldData.Current.Items, world.Cabinet);
}
