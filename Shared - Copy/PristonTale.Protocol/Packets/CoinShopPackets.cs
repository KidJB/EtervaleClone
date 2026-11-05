using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class CoinShopWire
{
    public const int MaxTabs = 16;
    public const int MaxItemsPerTab = 32;
    public const int MaxNameBytes = 32;
    public const int MaxIconBytes = 32;
    public const int MaxKeyBytes = 16;
    public const int MaxMessageBytes = 128;
    public const int MaxDescriptionBytes = 128;
    public const byte FlagSpec = 1;
    public const byte FlagQuantity = 2;
}

// C->S {header, Guid OperationId}: open the coin shop. The answer is one CoinShopTab per tab, then CoinShopCatalog.
public sealed record CoinShopOpenPacket(Guid OperationId) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 16;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CoinShopOpen);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Guid(OperationId);
        return writer.Finish();
    }

    public static CoinShopOpenPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Coin shop open");
        return new CoinShopOpenPacket(reader.Guid());
    }
}

// One article: {int32 Id, text Name, text Description, text ItemKey, int32 Price, Discount, Bulk, MaxBulk, uint8 Flags
// (1 spec, 2 quantity)}.
public sealed record CoinShopArticle(int Id, string Name, string Description, string ItemKey, int Price, int Discount, int Bulk, int MaxBulk, byte Flags);

// S->C {header, Guid OperationId, int32 Id, text Name, text Icon, uint16 Count, articles}: one tab of an open request.
public sealed record CoinShopTabPacket(Guid OperationId, int Id, string Name, string Icon, IReadOnlyList<CoinShopArticle> Articles)
{
    public const ushort CurrentVersion = 1;
    private const int MinimumSize = 8 + 4 + 16 + 4 + 2 + 2 + 2;
    private const int ArticleMaxSize = 4 + 2 + CoinShopWire.MaxNameBytes + 2 + CoinShopWire.MaxDescriptionBytes + 2 + CoinShopWire.MaxKeyBytes + 16 + 1;
    public const int MaxSize = MinimumSize + CoinShopWire.MaxNameBytes + CoinShopWire.MaxIconBytes + CoinShopWire.MaxItemsPerTab * ArticleMaxSize;

    public int Write(Span<byte> destination)
    {
        if (Articles.Count > CoinShopWire.MaxItemsPerTab)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many coin shop articles in one tab.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.CoinShopTab);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Guid(OperationId);
        writer.Int32(Id);
        Utf8Text.Write(ref writer, Name, CoinShopWire.MaxNameBytes);
        Utf8Text.Write(ref writer, Icon, CoinShopWire.MaxIconBytes);
        writer.UInt16((ushort)Articles.Count);
        foreach (var article in Articles)
        {
            writer.Int32(article.Id);
            Utf8Text.Write(ref writer, article.Name, CoinShopWire.MaxNameBytes);
            Utf8Text.Write(ref writer, article.Description, CoinShopWire.MaxDescriptionBytes);
            Utf8Text.Write(ref writer, article.ItemKey, CoinShopWire.MaxKeyBytes);
            writer.Int32(article.Price);
            writer.Int32(article.Discount);
            writer.Int32(article.Bulk);
            writer.Int32(article.MaxBulk);
            writer.Byte(article.Flags);
        }

        return writer.Finish();
    }

    public static CoinShopTabPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Coin shop tab");
        var operation = reader.Guid();
        var id = reader.Int32();
        var name = Utf8Text.Read(ref reader, CoinShopWire.MaxNameBytes, "Coin shop tab");
        var icon = Utf8Text.Read(ref reader, CoinShopWire.MaxIconBytes, "Coin shop tab");
        var count = reader.UInt16();
        if (count > CoinShopWire.MaxItemsPerTab)
        {
            throw new InvalidDataException("Coin shop tab has too many articles.");
        }

        var articles = new CoinShopArticle[count];
        for (var index = 0; index < count; index++)
        {
            var articleId = reader.Int32();
            var articleName = Utf8Text.Read(ref reader, CoinShopWire.MaxNameBytes, "Coin shop article");
            var description = Utf8Text.Read(ref reader, CoinShopWire.MaxDescriptionBytes, "Coin shop article");
            var key = Utf8Text.Read(ref reader, CoinShopWire.MaxKeyBytes, "Coin shop article");
            articles[index] = new CoinShopArticle(articleId, articleName, description, key, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(),
                reader.Byte());
        }

        return new CoinShopTabPacket(operation, id, name, icon, articles);
    }
}

// S->C {header, Guid OperationId, int64 Balance, uint16 TabCount, text Name, text Message}: the end of an open request,
// after its TabCount tabs.
public sealed record CoinShopCatalogPacket(Guid OperationId, long Balance, int TabCount, string Name, string Message)
{
    public const ushort CurrentVersion = 1;
    private const int MinimumSize = 8 + 4 + 16 + 8 + 2 + 2 + 2;
    public const int MaxSize = MinimumSize + CoinShopWire.MaxNameBytes + CoinShopWire.MaxMessageBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CoinShopCatalog);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Guid(OperationId);
        writer.Int64(Balance);
        writer.UInt16((ushort)TabCount);
        Utf8Text.Write(ref writer, Name, CoinShopWire.MaxNameBytes);
        Utf8Text.Write(ref writer, Message, CoinShopWire.MaxMessageBytes);
        return writer.Finish();
    }

    public static CoinShopCatalogPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Coin shop catalog");
        var operation = reader.Guid();
        var balance = reader.Int64();
        var tabs = reader.UInt16();
        if (tabs > CoinShopWire.MaxTabs)
        {
            throw new InvalidDataException("Coin shop has too many tabs.");
        }

        var name = Utf8Text.Read(ref reader, CoinShopWire.MaxNameBytes, "Coin shop catalog");
        return new CoinShopCatalogPacket(operation, balance, tabs, name, Utf8Text.Read(ref reader, CoinShopWire.MaxMessageBytes, "Coin shop catalog"));
    }
}

// C->S {header, Guid OperationId, int32 TabId, ItemId, Quantity, SpecClass}: buy an article.
public sealed record CoinShopBuyPacket(Guid OperationId, int TabId, int ItemId, int Quantity, int SpecClass) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 16 + 16;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CoinShopBuy);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Guid(OperationId);
        writer.Int32(TabId);
        writer.Int32(ItemId);
        writer.Int32(Quantity);
        writer.Int32(SpecClass);
        return writer.Finish();
    }

    public static CoinShopBuyPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Coin shop buy");
        return new CoinShopBuyPacket(reader.Guid(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
    }
}

// S->C {header, Guid OperationId, uint8 Status (CoinShopStatus), uint8 0, uint16 0, int64 Balance}.
public sealed record CoinShopBuyResultPacket(Guid OperationId, byte Status, long Balance)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 16 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CoinShopBuyResult);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int64(Balance);
        return writer.Finish();
    }

    public static CoinShopBuyResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Coin shop buy result");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        return new CoinShopBuyResultPacket(operation, status, reader.Int64());
    }
}
