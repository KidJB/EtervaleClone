using System.Numerics;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Application.World.Stalls;

// An offline stall standing in a field: its owner's look and sign, where it stands, and its listing over its own stock. It
// changes only on the tick; the stored stall (IStallRepository) is written before each change commits.
public sealed class Stall(long id, long accountId, long ownerCharacterId, int fieldId, Vector3 position, float facing, PlayerAppearanceView look, int level,
    int tier)
{
    public long Id { get; } = id;
    public long AccountId { get; } = accountId;
    public long OwnerCharacterId { get; } = ownerCharacterId;
    public int FieldId { get; } = fieldId;
    public Vector3 Position { get; } = position;
    public float Facing { get; } = facing;
    public PlayerAppearanceView Look { get; } = look;
    public int Level { get; } = level;
    public int Tier { get; } = tier;
    public ulong EntityId => EntityIdAllocator.StallIdBase + (ulong)Id;
    public string OwnerName => Look.Name;

    public PersonalShop Shop { get; private set; } = StallRules.Listing("", []);
    public Inventory Stock { get; private set; } = StoreGrid.Create();

    public void SetStock(PersonalShop shop, Inventory stock)
    {
        Shop = shop;
        Stock = stock;
    }

    public static bool IsStallId(ulong entityId) => entityId >= EntityIdAllocator.StallIdBase;

    public EntityView ToView() => new(EntityId, EntityKind.Player, isSelf: false, Position, Facing, MotionState.Stand, Level, 1, 1,
        Look with { ShopSign = Shop.Sign, InVillage = true });

    // The listing as a viewer sees it: each copy's values for the viewer's class.
    public PersonalShopListing Listing(PlayerCharacter viewer)
    {
        var offers = new List<PersonalShopOffer>(Shop.Entries.Count);
        foreach (var entry in Shop.Entries)
        {
            var anchor = Stock.Find(entry.ItemId);
            if (anchor >= 0 && Stock[anchor] is { } item)
            {
                offers.Add(new PersonalShopOffer(OwnedItemView.Of(item, entry.Cell, -1) with { Details = ItemDetailView.Of(item, viewer) }, entry.UnitPrice));
            }
        }

        return new PersonalShopListing(EntityId, offers);
    }
}
