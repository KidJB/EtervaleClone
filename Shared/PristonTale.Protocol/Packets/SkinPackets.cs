using PristonTale.Contracts.Characters;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class SkinWire
{
    public const ushort Version = 1;
    public const int MaxKeyBytes = 32;
    public const int MaxNameBytes = 64;
    public const int MaxArticles = 64;
}

// C->S {header, Guid OperationId}: asks for The Cabinet.
public sealed record SkinOpenPacket(Guid OperationId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkinOpen);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        return writer.Finish();
    }

    public static SkinOpenPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Skin open");
        return new SkinOpenPacket(reader.Guid());
    }
}

// S->C {header, Guid OperationId, int64 Balance, uint8 Last, 3 zero bytes, weapon, armor, off-hand and hair skin keys, uint16 Count,
// articles}: one part of the catalog; Last marks the final part.
public sealed record SkinCatalogPacket(Guid OperationId, long Balance, bool Last, string Weapon, string Armor, string OffHand, string Hair,
    IReadOnlyList<SkinArticle> Articles) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 8 + 4 + 2 * 4 + 2;
    private const int ArticleMaxSize = 2 + SkinWire.MaxKeyBytes + 2 + SkinWire.MaxNameBytes + 1 + 4 + 4 + 1;
    public const int MaxSize = MinimumSize + 4 * SkinWire.MaxKeyBytes + SkinWire.MaxArticles * ArticleMaxSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkinCatalog);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        writer.Int64(Balance);
        writer.Byte(Last ? (byte)1 : (byte)0);
        writer.Zero(3);
        Utf8Text.Write(ref writer, Weapon, SkinWire.MaxKeyBytes);
        Utf8Text.Write(ref writer, Armor, SkinWire.MaxKeyBytes);
        Utf8Text.Write(ref writer, OffHand, SkinWire.MaxKeyBytes);
        Utf8Text.Write(ref writer, Hair, SkinWire.MaxKeyBytes);
        writer.UInt16((ushort)Articles.Count);
        foreach (var article in Articles)
        {
            Utf8Text.Write(ref writer, article.Key, SkinWire.MaxKeyBytes);
            Utf8Text.Write(ref writer, article.Name, SkinWire.MaxNameBytes);
            writer.Byte(article.Slot);
            writer.Int32(article.Level);
            writer.Int32(article.Price);
            writer.Byte(article.Owned ? (byte)1 : (byte)0);
        }

        return writer.Finish();
    }

    public static SkinCatalogPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Skin catalog");
        var operation = reader.Guid();
        var balance = reader.Int64();
        var last = reader.Byte() != 0;
        reader.Skip(3);
        var weapon = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin catalog");
        var armor = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin catalog");
        var offHand = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin catalog");
        var hair = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin catalog");
        var count = reader.UInt16();
        if (count > SkinWire.MaxArticles)
        {
            throw new InvalidDataException("Skin catalog has too many articles.");
        }

        var articles = new SkinArticle[count];
        for (var index = 0; index < count; index++)
        {
            var key = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin catalog");
            var name = Utf8Text.Read(ref reader, SkinWire.MaxNameBytes, "Skin catalog");
            var slot = reader.Byte();
            var level = reader.Int32();
            var price = reader.Int32();
            articles[index] = new SkinArticle(key, name, slot, level, price, reader.Byte() != 0);
        }

        return new SkinCatalogPacket(operation, balance, last, weapon, armor, offHand, hair, articles);
    }
}

// C->S {header, Guid OperationId, key}: buys a skin for the account.
public sealed record SkinBuyPacket(Guid OperationId, string Key) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 2;
    public const int MaxSize = MinimumSize + SkinWire.MaxKeyBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkinBuy);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        Utf8Text.Write(ref writer, Key, SkinWire.MaxKeyBytes);
        return writer.Finish();
    }

    public static SkinBuyPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Skin buy");
        var operation = reader.Guid();
        return new SkinBuyPacket(operation, Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin buy"));
    }
}

// C->S {header, Guid OperationId, weapon, armor, off-hand and hair keys}: the looks the character shows.
public sealed record SkinApplyPacket(Guid OperationId, string Weapon, string Armor, string OffHand, string Hair) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 2 * 4;
    public const int MaxSize = MinimumSize + 4 * SkinWire.MaxKeyBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkinApply);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        Utf8Text.Write(ref writer, Weapon, SkinWire.MaxKeyBytes);
        Utf8Text.Write(ref writer, Armor, SkinWire.MaxKeyBytes);
        Utf8Text.Write(ref writer, OffHand, SkinWire.MaxKeyBytes);
        Utf8Text.Write(ref writer, Hair, SkinWire.MaxKeyBytes);
        return writer.Finish();
    }

    public static SkinApplyPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Skin apply");
        var operation = reader.Guid();
        var weapon = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin apply");
        var armor = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin apply");
        var offHand = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin apply");
        return new SkinApplyPacket(operation, weapon, armor, offHand, Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin apply"));
    }
}

// S->C {header, Guid OperationId, uint8 Status (SkinStatus), 3 zero bytes, int64 Balance}.
public sealed record SkinResultPacket(Guid OperationId, byte Status, long Balance) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkinResult);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int64(Balance);
        return writer.Finish();
    }

    public static SkinResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Skin result");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        return new SkinResultPacket(operation, status, reader.Int64());
    }
}
