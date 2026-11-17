using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// C->S {header, Guid OperationId, Guid ItemId}: turns the bag item into its look in The Cabinet (a brush of its kind is used
// with gear; a token is used alone); answered by SkinUnlockResult.
public sealed record SkinUnlockPacket(Guid OperationId, Guid ItemId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 16;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkinUnlock);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        writer.Guid(ItemId);
        return writer.Finish();
    }

    public static SkinUnlockPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Skin unlock");
        return new SkinUnlockPacket(reader.Guid(), reader.Guid());
    }
}

// S->C {header, Guid OperationId, uint8 Status (SkinStatus), 3 zero bytes, int64 Balance, text Key (32 B)}: the look unlocked.
public sealed record SkinUnlockResultPacket(Guid OperationId, byte Status, long Balance, string Key) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 4 + 8 + 2;
    public const int MaxSize = MinimumSize + SkinWire.MaxKeyBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkinUnlockResult);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int64(Balance);
        Utf8Text.Write(ref writer, Key, SkinWire.MaxKeyBytes);
        return writer.Finish();
    }

    public static SkinUnlockResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Skin unlock result");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        var balance = reader.Int64();
        return new SkinUnlockResultPacket(operation, status, balance, Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, "Skin unlock result"));
    }
}

// C->S {header, Guid OperationId}: asks for The Cabinet's open bidding deals.
public sealed record CabinetDealsRequestPacket(Guid OperationId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CabinetDealsRequest);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        return writer.Finish();
    }

    public static CabinetDealsRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Cabinet deals request");
        return new CabinetDealsRequestPacket(reader.Guid());
    }
}

public readonly record struct CabinetWireBid(string Name, long Amount, long PlacedAt);

// A deal as its viewer sees it: the look, its rarity (ItemRarity), the end (Unix seconds), the least next bid, the leading
// bid and its bidder, whether the viewer leads, the level needed, the bids so far and the latest of them.
public sealed record CabinetWireDeal(long Id, string Key, string Name, byte Rarity, long EndsAt, long NextBid, long TopBid, string TopName, bool Leading,
    int MinLevel, int Bids, IReadOnlyList<CabinetWireBid> History);

// S->C {header, Guid OperationId, int64 Balance, int64 Now, uint8 Count (at most 8), deals}: a deal is {int64 Id, text Key
// (32 B), text Name (64 B), uint8 Rarity, int64 EndsAt, int64 NextBid, int64 TopBid, text TopName (32 B), uint8 Leading,
// uint16 MinLevel, int32 Bids, uint8 Count (at most 5), {text Name (32 B), int64 Amount, int64 PlacedAt} x Count}.
public sealed record CabinetDealsPacket(Guid OperationId, long Balance, long Now, IReadOnlyList<CabinetWireDeal> Deals) : IFramePacket
{
    public const int MaxDeals = 8;
    public const int MaxHistory = 5;
    private const int MaxNameBytes = 32;
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 8 + 8 + 1;
    private const int BidMaxSize = 2 + MaxNameBytes + 8 + 8;
    private const int DealMaxSize = 8 + 2 + SkinWire.MaxKeyBytes + 2 + SkinWire.MaxNameBytes + 1 + 8 + 8 + 8 + 2 + MaxNameBytes + 1 + 2 + 4 + 1 + MaxHistory * BidMaxSize;
    public const int MaxSize = MinimumSize + MaxDeals * DealMaxSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CabinetDeals);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        writer.Int64(Balance);
        writer.Int64(Now);
        var count = Math.Min(Deals.Count, MaxDeals);
        writer.Byte((byte)count);
        for (var index = 0; index < count; index++)
        {
            var deal = Deals[index];
            writer.Int64(deal.Id);
            Utf8Text.Write(ref writer, deal.Key, SkinWire.MaxKeyBytes);
            Utf8Text.Write(ref writer, deal.Name, SkinWire.MaxNameBytes);
            writer.Byte(deal.Rarity);
            writer.Int64(deal.EndsAt);
            writer.Int64(deal.NextBid);
            writer.Int64(deal.TopBid);
            Utf8Text.Write(ref writer, deal.TopName, MaxNameBytes);
            writer.Byte(deal.Leading ? (byte)1 : (byte)0);
            writer.UInt16((ushort)Math.Clamp(deal.MinLevel, 0, ushort.MaxValue));
            writer.Int32(deal.Bids);
            var bids = Math.Min(deal.History.Count, MaxHistory);
            writer.Byte((byte)bids);
            for (var bid = 0; bid < bids; bid++)
            {
                Utf8Text.Write(ref writer, deal.History[bid].Name, MaxNameBytes);
                writer.Int64(deal.History[bid].Amount);
                writer.Int64(deal.History[bid].PlacedAt);
            }
        }

        return writer.Finish();
    }

    public static CabinetDealsPacket Read(ReadOnlySpan<byte> packet)
    {
        const string what = "Cabinet deals";
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, what);
        var operation = reader.Guid();
        var balance = reader.Int64();
        var now = reader.Int64();
        var count = reader.Byte();
        if (count > MaxDeals)
        {
            throw new InvalidDataException("Cabinet deals has too many deals.");
        }

        var deals = new CabinetWireDeal[count];
        for (var index = 0; index < count; index++)
        {
            var id = reader.Int64();
            var key = Utf8Text.Read(ref reader, SkinWire.MaxKeyBytes, what);
            var name = Utf8Text.Read(ref reader, SkinWire.MaxNameBytes, what);
            var rarity = reader.Byte();
            var endsAt = reader.Int64();
            var nextBid = reader.Int64();
            var topBid = reader.Int64();
            var topName = Utf8Text.Read(ref reader, MaxNameBytes, what);
            var leading = reader.Byte() != 0;
            var minLevel = reader.UInt16();
            var total = reader.Int32();
            var bids = reader.Byte();
            if (bids > MaxHistory)
            {
                throw new InvalidDataException("Cabinet deals has too many bids.");
            }

            var history = new CabinetWireBid[bids];
            for (var bid = 0; bid < bids; bid++)
            {
                history[bid] = new CabinetWireBid(Utf8Text.Read(ref reader, MaxNameBytes, what), reader.Int64(), reader.Int64());
            }

            deals[index] = new CabinetWireDeal(id, key, name, rarity, endsAt, nextBid, topBid, topName, leading, minLevel, total, history);
        }

        return new CabinetDealsPacket(operation, balance, now, deals);
    }
}

// C->S {header, Guid OperationId, int64 DealId, int64 Amount}: a bid of coins; answered by CabinetBidResult.
public sealed record CabinetBidPacket(Guid OperationId, long DealId, long Amount) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 8 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CabinetBid);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        writer.Int64(DealId);
        writer.Int64(Amount);
        return writer.Finish();
    }

    public static CabinetBidPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Cabinet bid");
        return new CabinetBidPacket(reader.Guid(), reader.Int64(), reader.Int64());
    }
}

// S->C {header, Guid OperationId, uint8 Status (CabinetBidStatus), 3 zero bytes, int64 Balance}.
public sealed record CabinetBidResultPacket(Guid OperationId, byte Status, long Balance) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CabinetBidResult);
        WireHeader.Write(ref writer, SkinWire.Version);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int64(Balance);
        return writer.Finish();
    }

    public static CabinetBidResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Cabinet bid result");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        return new CabinetBidResultPacket(operation, status, reader.Int64());
    }
}
