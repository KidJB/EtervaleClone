using System.Globalization;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.Services;

public sealed record SkinCatalogView(long Balance, SkinSet Skins, IReadOnlyList<SkinArticle> Articles);

// The Cabinet outside the tick: skins are owned by the account and bought with coins in one transaction; the looks a
// character shows are set on the tick and saved with it.
public sealed class SkinAppService(ISessionRegistry sessions, ICoinRepository coins, WorldServer world, WorldDataHolder worldData)
{
    public async Task<SkinCatalogView> CatalogAsync(string sessionToken, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new SkinCatalogView(0, SkinSet.None, []);
        }

        var owned = (await coins.OwnedSkinsAsync(session.AccountId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var articles = new List<SkinArticle>();
        foreach (var number in Costumes.Hair)
        {
            var key = SkinRules.HeadKey(number);
            articles.Add(new SkinArticle(key, string.Create(CultureInfo.InvariantCulture, $"KPT Hair {number}"), (byte)SkinSlot.Hair, 0, SkinRules.HairPrice, owned.Contains(key)));
        }

        foreach (var item in worldData.Current.Items.All)
        {
            if (Costumes.Hidden.Contains(item.CatalogKey))
            {
                continue;
            }

            if (SkinRules.HairOf(item.CatalogKey) is not null)
            {
                articles.Add(new SkinArticle(item.CatalogKey, item.Name, (byte)SkinSlot.Hair, 0, SkinRules.HairPrice, owned.Contains(item.CatalogKey)));
                continue;
            }

            if (SkinRules.SlotOf(item) is { } slot && SkinRules.Listed(item) && Costumes.Find(item.CatalogKey) is null)
            {
                articles.Add(new SkinArticle(item.CatalogKey, SkinRules.DisplayName(item.Name), (byte)slot, item.RequiredLevel, SkinRules.Price(item), owned.Contains(item.CatalogKey)));
            }
        }

        foreach (var gear in Costumes.Gear)
        {
            articles.Add(new SkinArticle(gear.Key, gear.Name, (byte)gear.Slot, 0, SkinRules.GearPrice, owned.Contains(gear.Key)));
        }

        foreach (var costume in Costumes.All)
        {
            articles.Add(new SkinArticle(costume.Key, costume.Name, (byte)SkinSlot.Armor, 0, SkinRules.CostumePrice, owned.Contains(costume.Key)));
        }

        articles.Sort(static (left, right) => right.Price.CompareTo(left.Price));
        return new SkinCatalogView(await coins.BalanceAsync(session.AccountId, ct), await world.SkinsOfAsync(session), articles);
    }

    public async Task<(SkinStatus Status, long Balance)> BuyAsync(string sessionToken, string key, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return (SkinStatus.Rejected, 0);
        }

        var balance = await coins.BalanceAsync(session.AccountId, ct);
        if (Find(key) is not { } skin)
        {
            return (SkinStatus.UnknownSkin, balance);
        }

        if ((await coins.OwnedSkinsAsync(session.AccountId, ct)).Contains(skin.Key, StringComparer.OrdinalIgnoreCase))
        {
            return (SkinStatus.AlreadyOwned, balance);
        }

        var reason = string.Create(CultureInfo.InvariantCulture, $"bought skin {skin.Key}");
        var paid = await coins.BuySkinAsync(session.AccountId, skin.Key, skin.Price, reason, ct);
        return paid is { } left ? (SkinStatus.Ok, left) : (SkinStatus.NotEnoughCoins, balance);
    }

    // Every look must be an owned skin of its slot ("" clears the slot).
    public async Task<(SkinStatus Status, long Balance)> ApplyAsync(string sessionToken, SkinSet skins, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return (SkinStatus.Rejected, 0);
        }

        var balance = await coins.BalanceAsync(session.AccountId, ct);
        var owned = await coins.OwnedSkinsAsync(session.AccountId, ct);
        if (!Allowed(skins.Weapon, SkinSlot.Weapon, owned) || !Allowed(skins.Armor, SkinSlot.Armor, owned) || !Allowed(skins.OffHand, SkinSlot.OffHand, owned)
            || !Allowed(skins.Hair, SkinSlot.Hair, owned))
        {
            return (SkinStatus.NotOwned, balance);
        }

        return (await world.ApplySkinsAsync(session, skins) ? SkinStatus.Ok : SkinStatus.Rejected, balance);
    }

    private bool Allowed(string key, SkinSlot slot, IReadOnlyCollection<string> owned) =>
        key.Length == 0 || owned.Contains(key, StringComparer.OrdinalIgnoreCase) && Find(key) is { } skin && skin.Slot == slot;

    private (string Key, SkinSlot Slot, int Price)? Find(string key)
    {
        if (Costumes.Find(key) is { } costume)
        {
            return (costume.Key, SkinSlot.Armor, SkinRules.CostumePrice);
        }

        if (SkinRules.HairOf(key) is not null)
        {
            return (key, SkinSlot.Hair, SkinRules.HairPrice);
        }

        if (Costumes.FindGear(key) is { } gear)
        {
            return (gear.Key, (SkinSlot)gear.Slot, SkinRules.GearPrice);
        }

        return ItemCodes.TryParse(key, out var code) && worldData.Current.Items.TryGet(code, out var item) && SkinRules.SlotOf(item) is { } slot
            ? (item.CatalogKey, slot, SkinRules.Price(item))
            : null;
    }
}
