using PristonTale.Simulation.Characters;

namespace PristonTale.Client.Net.Presence;

public sealed record MarketplaceOfferInfo(ulong SellerId, string Seller, string Location, bool Own, OwnedItemView Item, long UnitPrice,
    PristonTale.Simulation.PersonalShops.PersonalShopCurrency Currency = PristonTale.Simulation.PersonalShops.PersonalShopCurrency.Gold);

// A piece of a listed character's gear: its group (CharacterGearGroup), its slot and the item with its values.
public sealed record CharacterGearInfo(byte Group, byte Slot, OwnedItemView Item);

public sealed record CharacterDetailView(byte Status, int Strength, int Spirit, int Talent, int Agility, int Health, int Mounts, IReadOnlyList<CharacterGearInfo> Gear);

public sealed record MarketplaceView(byte Status, int Page, int Total, IReadOnlyList<MarketplaceOfferInfo> Offers);
