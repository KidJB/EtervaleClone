using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class MarketWire
{
    public const ushort Version = 1;
    public const int MaxNameBytes = 32;
    public const int MaxKeyBytes = 32;
    public const int MaxSearchBytes = 32;
    public const int MaxLocationBytes = 48;
    public const int MaxCharacterRows = 8;
    public const int MaxOffers = 6;
    public const int MaxEquipment = 16;

    public const byte ActionBrowse = 0;
    public const byte ActionMine = 1;
    public const byte ActionForSale = 2;
    public const byte ActionDetail = 3;
    public const byte ActionList = 4;
    public const byte ActionCancel = 5;
    public const byte ActionBuy = 6;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Market message");

    internal static string Text(ref PacketReader reader, int maxBytes) => Utf8Text.Read(ref reader, maxBytes, "Market message");
}

// C->S {header, Guid OperationId, uint8 Action, uint8 HideSeller, uint8 Sort, uint8 0, uint64 NpcId, int64 Id, int64 Price, int32 Page, int32 JobCode, text Text}. Text is a search, or
// the character to list.
public sealed record CharacterMarketRequestPacket(Guid OperationId, byte Action, ulong NpcId, long Id, long Price, int Page, int JobCode, byte Sort, bool HideSeller, string Text) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 4 + 8 + 8 + 8 + 4 + 4 + 2;
    public const int MaxSize = MinimumSize + MarketWire.MaxSearchBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CharacterMarketRequest);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Action);
        writer.Byte((byte)(HideSeller ? 1 : 0));
        writer.Byte(Sort);
        writer.Zero(1);
        writer.UInt64(NpcId);
        writer.Int64(Id);
        writer.Int64(Price);
        writer.Int32(Page);
        writer.Int32(JobCode);
        Utf8Text.Write(ref writer, Text, MarketWire.MaxSearchBytes);
        return writer.Finish();
    }

    public static CharacterMarketRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        MarketWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var action = reader.Byte();
        var hide = reader.Byte() != 0;
        var sort = reader.Byte();
        reader.Skip(1);
        var npc = reader.UInt64();
        var id = reader.Int64();
        var price = reader.Int64();
        var page = reader.Int32();
        var job = reader.Int32();
        return new CharacterMarketRequestPacket(operation, action, npc, id, price, page, job, sort, hide, MarketWire.Text(ref reader, MarketWire.MaxSearchBytes));
    }
}

// How a listed character looks: its face and hair and the catalog keys of its worn armour, weapon and off-hand.
public sealed record CharacterMarketLook(int Face, int Hair, string Armor, string Weapon, string OffHand)
{
    public static readonly CharacterMarketLook None = new(0, 0, "", "", "");
}

// One card: a listing (Id above 0), or for ActionForSale one of the account's characters with Blocked saying why it cannot be listed.
public sealed record CharacterMarketRow(long Id, string Name, string Seller, int JobCode, int Level, int Tier, int ExpBasisPoints, long Price, int AnnouncedDays, int ExpiresDays, byte State, byte Blocked)
{
    public CharacterMarketLook Look { get; init; } = CharacterMarketLook.None;
}

// S->C {header, Guid OperationId, uint8 Status, uint8 Action, uint16 0, int64 Balance, int32 Page, int32 Total, int64 Fee, int32 TaxPercent, int32 MinLevel, int64 MinPrice, int64 MaxPrice,
// uint16 Count, rows}: the answer to every request but ActionDetail. The rules let the sale window explain itself. A row is {int64 Id, text Name, text Seller, int32 JobCode, Level, Tier,
// ExpBasisPoints, int64 Price, int32 AnnouncedDays, ExpiresDays, uint8 State, Blocked, uint8 Face, Hair, text Armor, text Weapon, text OffHand}.
public sealed record CharacterMarketPagePacket(Guid OperationId, byte Status, byte Action, long Balance, int Page, int Total, long Fee, int TaxPercent, int MinLevel, long MinPrice, long MaxPrice,
    IReadOnlyList<CharacterMarketRow> Rows)
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 4 + 8 + 4 + 4 + 8 + 4 + 4 + 8 + 8 + 2;
    private const int RowMaxSize = 8 + 2 + MarketWire.MaxNameBytes + 2 + MarketWire.MaxNameBytes + 4 + 4 + 4 + 4 + 8 + 4 + 4 + 2 + 2 + 3 * (2 + MarketWire.MaxKeyBytes);
    public const int MaxSize = MinimumSize + MarketWire.MaxCharacterRows * RowMaxSize;

    public int Write(Span<byte> destination)
    {
        if (Rows.Count > MarketWire.MaxCharacterRows)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many character market rows.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.CharacterMarketPage);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Byte(Action);
        writer.Zero(2);
        writer.Int64(Balance);
        writer.Int32(Page);
        writer.Int32(Total);
        writer.Int64(Fee);
        writer.Int32(TaxPercent);
        writer.Int32(MinLevel);
        writer.Int64(MinPrice);
        writer.Int64(MaxPrice);
        writer.UInt16((ushort)Rows.Count);
        foreach (var row in Rows)
        {
            writer.Int64(row.Id);
            Utf8Text.Write(ref writer, row.Name, MarketWire.MaxNameBytes);
            Utf8Text.Write(ref writer, row.Seller, MarketWire.MaxNameBytes);
            writer.Int32(row.JobCode);
            writer.Int32(row.Level);
            writer.Int32(row.Tier);
            writer.Int32(row.ExpBasisPoints);
            writer.Int64(row.Price);
            writer.Int32(row.AnnouncedDays);
            writer.Int32(row.ExpiresDays);
            writer.Byte(row.State);
            writer.Byte(row.Blocked);
            writer.Byte((byte)row.Look.Face);
            writer.Byte((byte)row.Look.Hair);
            Utf8Text.Write(ref writer, row.Look.Armor, MarketWire.MaxKeyBytes);
            Utf8Text.Write(ref writer, row.Look.Weapon, MarketWire.MaxKeyBytes);
            Utf8Text.Write(ref writer, row.Look.OffHand, MarketWire.MaxKeyBytes);
        }

        return writer.Finish();
    }

    public static CharacterMarketPagePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        MarketWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var status = reader.Byte();
        var action = reader.Byte();
        reader.Skip(2);
        var balance = reader.Int64();
        var page = reader.Int32();
        var total = reader.Int32();
        var fee = reader.Int64();
        var tax = reader.Int32();
        var minLevel = reader.Int32();
        var minPrice = reader.Int64();
        var maxPrice = reader.Int64();
        var count = reader.UInt16();
        if (count > MarketWire.MaxCharacterRows)
        {
            throw new InvalidDataException("Character market page has too many rows.");
        }

        var rows = new CharacterMarketRow[count];
        for (var index = 0; index < count; index++)
        {
            var id = reader.Int64();
            var name = MarketWire.Text(ref reader, MarketWire.MaxNameBytes);
            var seller = MarketWire.Text(ref reader, MarketWire.MaxNameBytes);
            var row = new CharacterMarketRow(id, name, seller, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int64(), reader.Int32(), reader.Int32(), reader.Byte(), reader.Byte());
            var face = reader.Byte();
            var hair = reader.Byte();
            var armor = MarketWire.Text(ref reader, MarketWire.MaxKeyBytes);
            var weapon = MarketWire.Text(ref reader, MarketWire.MaxKeyBytes);
            rows[index] = row with { Look = new CharacterMarketLook(face, hair, armor, weapon, MarketWire.Text(ref reader, MarketWire.MaxKeyBytes)) };
        }

        return new CharacterMarketPagePacket(operation, status, action, balance, page, total, fee, tax, minLevel, minPrice, maxPrice, rows);
    }
}

// Group: 0 held (weapon, off-hand, armour), 1 the stored weapon pair, 2 a costume or skin shown, 3 an accessory.
public readonly record struct CharacterMarketGear(byte Group, byte Slot, StoreItemEntry Item);

// S->C {header, Guid OperationId, uint8 Status, uint8[3] 0, int32 Strength, Spirit, Talent, Agility, Health, Mounts, uint16 Count, {uint8 Group, uint8 Slot, StoreItemEntry}}:
// ActionDetail's answer, after CharacterMarketItems with the pieces' values.
public sealed record CharacterMarketDetailPacket(Guid OperationId, byte Status, int Strength, int Spirit, int Talent, int Agility, int Health, int Mounts, IReadOnlyList<CharacterMarketGear> Gear)
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 4 + 24 + 2;
    public const int MaxSize = MinimumSize + MarketWire.MaxEquipment * (2 + StoreItemEntry.MaxSize);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CharacterMarketDetail);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int32(Strength);
        writer.Int32(Spirit);
        writer.Int32(Talent);
        writer.Int32(Agility);
        writer.Int32(Health);
        writer.Int32(Mounts);
        var count = Math.Min(Gear.Count, MarketWire.MaxEquipment);
        writer.UInt16((ushort)count);
        for (var index = 0; index < count; index++)
        {
            writer.Byte(Gear[index].Group);
            writer.Byte(Gear[index].Slot);
            Gear[index].Item.Write(ref writer);
        }

        return writer.Finish();
    }

    public static CharacterMarketDetailPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        MarketWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        var strength = reader.Int32();
        var spirit = reader.Int32();
        var talent = reader.Int32();
        var agility = reader.Int32();
        var health = reader.Int32();
        var mounts = reader.Int32();
        var count = reader.UInt16();
        if (count > MarketWire.MaxEquipment)
        {
            throw new InvalidDataException("Character market detail has too many items.");
        }

        var gear = new CharacterMarketGear[count];
        for (var index = 0; index < count; index++)
        {
            var group = reader.Byte();
            var slot = reader.Byte();
            gear[index] = new CharacterMarketGear(group, slot, StoreItemEntry.Read(ref reader));
        }

        return new CharacterMarketDetailPacket(operation, status, strength, spirit, talent, agility, health, mounts, gear);
    }
}

// S->C {header, Guid OperationId, int32 Count, ItemDetailEntry entries}: the values of a listing's gear for their tooltips; sent before CharacterMarketDetail.
public sealed record CharacterMarketItemsPacket(Guid OperationId, IReadOnlyList<ItemDetailEntry> Items)
{
    private const int HeaderSize = 8 + WireHeader.Size + 16 + 4;
    public const int MaxSize = HeaderSize + MarketWire.MaxEquipment * ItemDetailEntry.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CharacterMarketItems);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        WireList.Write(ref writer, Items);
        return writer.Finish();
    }

    public static CharacterMarketItemsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        MarketWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        return new CharacterMarketItemsPacket(operation, WireList.Read<ItemDetailEntry>(ref reader, MarketWire.MaxEquipment, "Character market items"));
    }
}

// C->S {header, Guid OperationId, uint64 NpcId, int32 Page, uint8 Category, Subcategory, Class, Rarity, Sort, uint8[3] 0, int64 MinPrice, MaxPrice, text Item, text Seller}.
public sealed record MarketplaceQueryPacket(Guid OperationId, ulong NpcId, int Page, byte Category, byte Subcategory, byte Class, byte Rarity, byte Sort, long MinPrice, long MaxPrice, string Item, string Seller) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 8 + 4 + 8 + 16 + 4;
    public const int MaxSize = MinimumSize + 2 * MarketWire.MaxSearchBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.MarketplaceQuery);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Int32(Page);
        writer.Byte(Category);
        writer.Byte(Subcategory);
        writer.Byte(Class);
        writer.Byte(Rarity);
        writer.Byte(Sort);
        writer.Zero(3);
        writer.Int64(MinPrice);
        writer.Int64(MaxPrice);
        Utf8Text.Write(ref writer, Item, MarketWire.MaxSearchBytes);
        Utf8Text.Write(ref writer, Seller, MarketWire.MaxSearchBytes);
        return writer.Finish();
    }

    public static MarketplaceQueryPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        MarketWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var npc = reader.UInt64();
        var page = reader.Int32();
        var category = reader.Byte();
        var subcategory = reader.Byte();
        var playerClass = reader.Byte();
        var rarity = reader.Byte();
        var sort = reader.Byte();
        reader.Skip(3);
        var min = reader.Int64();
        var max = reader.Int64();
        var item = MarketWire.Text(ref reader, MarketWire.MaxSearchBytes);
        return new MarketplaceQueryPacket(operation, npc, page, category, subcategory, playerClass, rarity, sort, min, max, item, MarketWire.Text(ref reader, MarketWire.MaxSearchBytes));
    }
}

public sealed record MarketplaceOfferWire(ulong SellerId, string Seller, string Location, bool Own, StoreItemEntry Item, long UnitPrice, byte Currency = 0);

// S->C {header, Guid OperationId, uint8 Status, uint8[3] 0, int32 Page, Total, uint16 Count, offers}.
public sealed record MarketplacePagePacket(Guid OperationId, byte Status, int Page, int Total, IReadOnlyList<MarketplaceOfferWire> Offers)
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 4 + 4 + 4 + 2;
    private const int OfferMaxSize = 8 + 2 + MarketWire.MaxNameBytes + 2 + MarketWire.MaxLocationBytes + 1 + StoreItemEntry.MaxSize + 8 + 1;
    public const int MaxSize = MinimumSize + MarketWire.MaxOffers * OfferMaxSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.MarketplacePage);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int32(Page);
        writer.Int32(Total);
        writer.UInt16((ushort)Offers.Count);
        foreach (var offer in Offers)
        {
            writer.UInt64(offer.SellerId);
            Utf8Text.Write(ref writer, offer.Seller, MarketWire.MaxNameBytes);
            Utf8Text.Write(ref writer, offer.Location, MarketWire.MaxLocationBytes);
            writer.Byte((byte)(offer.Own ? 1 : 0));
            offer.Item.Write(ref writer);
            writer.Int64(offer.UnitPrice);
            writer.Byte(offer.Currency);
        }

        return writer.Finish();
    }

    public static MarketplacePagePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        MarketWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        var page = reader.Int32();
        var total = reader.Int32();
        var count = reader.UInt16();
        if (count > MarketWire.MaxOffers)
        {
            throw new InvalidDataException("Marketplace page has too many offers.");
        }

        var offers = new MarketplaceOfferWire[count];
        for (var index = 0; index < count; index++)
        {
            var seller = reader.UInt64();
            var name = MarketWire.Text(ref reader, MarketWire.MaxNameBytes);
            var location = MarketWire.Text(ref reader, MarketWire.MaxLocationBytes);
            var own = reader.Byte() != 0;
            var item = StoreItemEntry.Read(ref reader);
            var price = reader.Int64();
            offers[index] = new MarketplaceOfferWire(seller, name, location, own, item, price, reader.Byte());
        }

        return new MarketplacePagePacket(operation, status, page, total, offers);
    }
}

// S->C {header, Guid OperationId, int32 Count, ItemDetailEntry entries}: the offered copies' values for their tooltips; sent before MarketplacePage.
public sealed record MarketplaceDetailsPacket(Guid OperationId, IReadOnlyList<ItemDetailEntry> Items)
{
    private const int HeaderSize = 8 + WireHeader.Size + 16 + 4;
    public const int MaxSize = HeaderSize + MarketWire.MaxOffers * ItemDetailEntry.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.MarketplaceDetails);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        WireList.Write(ref writer, Items);
        return writer.Finish();
    }

    public static MarketplaceDetailsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        MarketWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        return new MarketplaceDetailsPacket(operation, WireList.Read<ItemDetailEntry>(ref reader, MarketWire.MaxOffers, "Marketplace details"));
    }
}

// C->S {header, uint64 NpcId, then a PersonalShopBuy body's fields}: a purchase through the Marketplace NPC, answered by PersonalShopResult.
public sealed record MarketplaceBuyPacket(Guid OperationId, ulong NpcId, ulong SellerId, Guid ItemId, long UnitPrice, int Count, long ExpectedRevision) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 8 + 8 + 16 + 8 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.MarketplaceBuy);
        MarketWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.UInt64(SellerId);
        writer.Guid(ItemId);
        writer.Int64(UnitPrice);
        writer.Int32(Count);
        writer.Int64(ExpectedRevision);
        return writer.Finish();
    }

    public static MarketplaceBuyPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        MarketWire.ReadHeader(ref reader);
        return new MarketplaceBuyPacket(reader.Guid(), reader.UInt64(), reader.UInt64(), reader.Guid(), reader.Int64(), reader.Int32(), reader.Int64());
    }
}
