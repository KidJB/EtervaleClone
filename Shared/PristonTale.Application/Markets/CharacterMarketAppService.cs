using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Markets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Markets;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Markets;

// The settings of both markets, written when the settings table is read and read by each request.
public sealed class MarketOptionsHolder
{
    private CharacterMarketOptions _character = CharacterMarketOptions.Default;
    private bool _marketplace = true;
    private int _purchaseCooldownMs = Simulation.PersonalShops.PersonalShopRules.PurchaseCooldownMs;

    // The wait between two purchases from personal shops and stalls.
    public int PurchaseCooldownMs
    {
        get => Volatile.Read(ref _purchaseCooldownMs);
        set => Volatile.Write(ref _purchaseCooldownMs, value);
    }

    public CharacterMarketOptions Character
    {
        get => Volatile.Read(ref _character);
        set => Volatile.Write(ref _character, value);
    }

    public bool MarketplaceEnabled
    {
        get => Volatile.Read(ref _marketplace);
        set => Volatile.Write(ref _marketplace, value);
    }
}

// An account's character as the sale window offers it; Blocked names why it cannot be listed (Ok when it can).
public sealed record CharacterForSale(string Name, int JobCode, int Level, int Tier, int ExpBasisPoints, CharacterMarketStatus Blocked);

// Where a piece of a listing's gear shows in its details; carried on the wire.
public enum CharacterGearGroup : byte
{
    Held = 0,
    StoredPair = 1,
    Costume = 2,
    Accessory = 3
}

// A piece of gear with its values as the viewer would see them in a tooltip.
public sealed record CharacterGear(CharacterGearGroup Group, int Slot, OwnedItemView Item);

// A listing's gear by group, the character's attributes and how many mounts it has unlocked.
public sealed record CharacterListingDetail(CharacterListing Listing, IReadOnlyList<CharacterGear> Equipment, int Strength, int Spirit, int Talent, int Agility, int Health,
    int Mounts);

// The Character Market outside the tick. Every request needs the player at an NPC offering it. Each change holds the character's
// claim, the one a world entry takes, so it never runs while the character is in play or entering; the listing, coins and owner
// then change in one storage transaction.
public sealed class CharacterMarketAppService(ISessionRegistry sessions, ICharacterMarketRepository market, ICharacterRepository characters, CharacterAppService summaries,
    WorldServer world, WorldDataHolder worldData, MarketOptionsHolder options, TimeProvider? clock = null)
{
    private static readonly CharacterClassRegistry Classes = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private long Now => _clock.GetUtcNow().ToUnixTimeSeconds();

    public CharacterMarketOptions Options => options.Character;

    public async Task<(CharacterMarketStatus Status, CharacterListingPage Page, long Balance)> BrowseAsync(string token, ulong npcId, int page, int jobCode, CharacterMarketSort sort,
        string search, CancellationToken ct = default)
    {
        var (status, _) = await AdmitAsync(token, npcId);
        return status != CharacterMarketStatus.Ok
            ? (status, Empty, 0)
            : (status, await market.BrowseAsync(new CharacterListingQuery(page, jobCode, sort, Trim(search), Now), ct), 0);
    }

    public async Task<(CharacterMarketStatus Status, CharacterListingPage Page)> MineAsync(string token, ulong npcId, int page, CancellationToken ct = default)
    {
        var (status, session) = await AdmitAsync(token, npcId);
        return status != CharacterMarketStatus.Ok ? (status, Empty) : (status, await market.ListingsOfAsync(session!.AccountId, page, ct));
    }

    public async Task<(CharacterMarketStatus Status, IReadOnlyList<CharacterForSale> Characters)> CharactersForSaleAsync(string token, ulong npcId, CancellationToken ct = default)
    {
        var (status, session) = await AdmitAsync(token, npcId);
        if (status != CharacterMarketStatus.Ok)
        {
            return (status, []);
        }

        var blocks = await market.BlocksAsync(session!.AccountId, Now, ct);
        var rows = await characters.ListByAccountAsync(session.AccountId, ct);
        var result = new List<CharacterForSale>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(new CharacterForSale(row.Name, row.JobCode, row.Level, row.Tier, ExperienceShare(row), BlockOf(row, blocks)));
        }

        return (CharacterMarketStatus.Ok, result);
    }

    public async Task<(CharacterMarketStatus Status, CharacterListingDetail? Detail)> DetailAsync(string token, ulong npcId, long listingId, CancellationToken ct = default)
    {
        var (status, _) = await AdmitAsync(token, npcId);
        if (status != CharacterMarketStatus.Ok)
        {
            return (status, null);
        }

        if (await market.FindAsync(listingId, ct) is not { CharacterId: { } characterId } listing || await market.CharacterOfAsync(characterId, ct) is not { } record)
        {
            return (CharacterMarketStatus.NotFound, null);
        }

        var items = worldData.Current.Items;
        if (!Enum.IsDefined((PlayerClass)record.JobCode))
        {
            return (CharacterMarketStatus.NotFound, null);
        }

        // The character as it would enter the world, so each piece carries its rolled values.
        var character = new PlayerCharacter(Classes.Get((PlayerClass)record.JobCode), record.Name, record.Level,
            new CharacterAttributes(record.Strength, record.Spirit, record.Talent, record.Agility, record.Health));
        CharacterStateCodec.Restore(character, record, items);
        var worn = new List<CharacterGear>();
        foreach (var (slot, item) in character.Equipment)
        {
            var group = slot is EquipmentSlot.Weapon or EquipmentSlot.OffHand or EquipmentSlot.Armor ? CharacterGearGroup.Held : CharacterGearGroup.Accessory;
            worn.Add(new CharacterGear(group, (int)slot, View(item, character)));
        }

        foreach (var (slot, item) in (ReadOnlySpan<(EquipmentSlot, ItemInstance?)>)[(EquipmentSlot.Weapon, character.StoredWeapon), (EquipmentSlot.OffHand, character.StoredOffHand)])
        {
            if (item is not null)
            {
                worn.Add(new CharacterGear(CharacterGearGroup.StoredPair, (int)slot, View(item, character)));
            }
        }

        var skins = SkinCodec.Restore(record.Skins);
        foreach (var key in (ReadOnlySpan<string>)[skins.Weapon, skins.TwoHand, skins.Armor, skins.OffHand])
        {
            if (key.Length > 0 && items.TryGet(ItemCodes.Parse(key), out var look))
            {
                worn.Add(new CharacterGear(CharacterGearGroup.Costume, 0, View(new ItemInstance(look), character)));
            }
        }

        var mounts = System.Numerics.BitOperations.PopCount(unchecked((ulong)record.Mounts));
        return (CharacterMarketStatus.Ok, new CharacterListingDetail(listing, worn, record.Strength, record.Spirit, record.Talent, record.Agility, record.Health, mounts));
    }

    public async Task<CharacterMarketStore> ListAsync(string token, ulong npcId, Guid operationId, string characterName, long price, bool hideSeller, CancellationToken ct = default)
    {
        var (status, session) = await AdmitAsync(token, npcId);
        if (status != CharacterMarketStatus.Ok)
        {
            return new CharacterMarketStore(status);
        }

        var rules = options.Character;
        if (!CharacterMarketRules.PriceAllowed(rules, price))
        {
            return new CharacterMarketStore(CharacterMarketStatus.PriceOutOfRange);
        }

        if (await characters.FindByNameForAccountAsync(session!.AccountId, characterName, ct) is not { } record)
        {
            return new CharacterMarketStore(CharacterMarketStatus.NotOwner);
        }

        if (!sessions.TryClaimCharacter(record.Id, token))
        {
            return new CharacterMarketStore(CharacterMarketStatus.InPlay);
        }

        try
        {
            // Read again under the claim, when nothing else writes it.
            if (await characters.FindByNameForAccountAsync(session.AccountId, characterName, ct) is not { } held || held.Id != record.Id)
            {
                return new CharacterMarketStore(CharacterMarketStatus.NotOwner);
            }

            var summary = summaries.Summary(held);
            return await market.ListAsync(new CharacterListingDraft(operationId, held.Id, session.AccountId, session.CharacterName ?? "", hideSeller, held.Name, held.JobCode, held.Level,
                held.Tier, ExperienceShare(held), held.FaceIndex, summary.ArmorKey, summary.WeaponKey, summary.OffHandKey, price, Now), rules, ct);
        }
        finally
        {
            sessions.ReleaseCharacter(record.Id, token);
        }
    }

    public async Task<CharacterMarketStore> CancelAsync(string token, ulong npcId, long listingId, CancellationToken ct = default)
    {
        var (status, session) = await AdmitAsync(token, npcId);
        return status != CharacterMarketStatus.Ok ? new CharacterMarketStore(status) : await market.CancelAsync(listingId, session!.AccountId, Now, ct);
    }

    public async Task<CharacterMarketStore> BuyAsync(string token, ulong npcId, Guid operationId, long listingId, long expectedPrice, CancellationToken ct = default)
    {
        var (status, session) = await AdmitAsync(token, npcId);
        if (status != CharacterMarketStatus.Ok)
        {
            return new CharacterMarketStore(status);
        }

        var purchase = new CharacterMarketPurchase(operationId, listingId, session!.AccountId, expectedPrice, options.Character.TaxPercent, CharacterSlots.Maximum, Now);
        if (await market.FindAsync(listingId, ct) is not { CharacterId: { } characterId })
        {
            return new CharacterMarketStore(CharacterMarketStatus.NotFound);
        }

        if (!sessions.TryClaimCharacter(characterId, token))
        {
            return new CharacterMarketStore(CharacterMarketStatus.Busy);
        }

        try
        {
            return await market.BuyAsync(purchase, ct);
        }
        finally
        {
            sessions.ReleaseCharacter(characterId, token);
        }
    }

    private static CharacterListingPage Empty { get; } = new([], 0);

    private async Task<(CharacterMarketStatus Status, PlayerSession? Session)> AdmitAsync(string token, ulong npcId)
    {
        if (!options.Character.Enabled)
        {
            return (CharacterMarketStatus.Disabled, null);
        }

        if (!sessions.TryGet(token, out var session))
        {
            return (CharacterMarketStatus.Rejected, null);
        }

        return await world.AtNpcServiceAsync(session, npcId, NpcServiceKind.CharacterMarket) ? (CharacterMarketStatus.Ok, session) : (CharacterMarketStatus.OutOfReach, null);
    }

    private static OwnedItemView View(ItemInstance item, PlayerCharacter owner) => OwnedItemView.Of(item, 0, -1) with { Details = ItemDetailView.Of(item, owner) };

    private CharacterMarketStatus BlockOf(CharacterRecord row, CharacterListingBlocks blocks)
    {
        if (sessions.TryGetCharacterSession(row.Id, out _))
        {
            return CharacterMarketStatus.InPlay;
        }

        if (blocks.Listed.Contains(row.Id))
        {
            return CharacterMarketStatus.AlreadyListed;
        }

        if (blocks.InClan.Contains(row.Id))
        {
            return CharacterMarketStatus.InClan;
        }

        if (blocks.WithStall.Contains(row.Id))
        {
            return CharacterMarketStatus.HasStall;
        }

        return row.Level < options.Character.MinLevel ? CharacterMarketStatus.LevelTooLow : CharacterMarketStatus.Ok;
    }

    // A character at the last level has no next level to measure against.
    private static int ExperienceShare(CharacterRecord record) => record.Level < 1 || record.Level >= ExperienceTable.MaximumLevel
        ? 0
        : CharacterMarketRules.ExperienceBasisPoints(unchecked((ulong)record.Exp), ExperienceTable.ForLevel(record.Level), ExperienceTable.ForLevel(record.Level + 1));

    private static string Trim(string search) => search.Length <= CharacterMarketRules.MaxSearchLength ? search : search[..CharacterMarketRules.MaxSearchLength];
}
