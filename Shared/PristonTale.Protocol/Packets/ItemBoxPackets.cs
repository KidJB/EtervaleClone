using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// The Item Distributor's messages. Each body starts with {uint16 Version = 1, uint16 Flags = 0}; text is UTF-8 with a
// uint16 byte length.
public static class ItemBoxWire
{
    public const ushort Version = 1;
    public const int MaxSenderBytes = 64;
    public const int MaxRecipientBytes = 64;
    public const byte ActionTake = 1;
    public const byte ActionDiscard = 2;
    public const byte ActionSend = 3;
}

// C->S {header, Guid OperationId, uint64 NpcId}: list the account's waiting items. Answered by one ItemBoxEntry per item,
// then ItemBoxList.
public sealed record ItemBoxOpenPacket(Guid OperationId, ulong NpcId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemBoxOpen);
        WireHeader.Write(ref writer, ItemBoxWire.Version);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        return writer.Finish();
    }

    public static ItemBoxOpenPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Item box open");
        return new ItemBoxOpenPacket(reader.Guid(), reader.UInt64());
    }
}

// S->C {header, Guid OperationId, int64 Id, int64 CreatedAtUnixMs, text Sender, StoreItemEntry Item, ItemDetailEntry
// Details}: one waiting item of an open.
public sealed record ItemBoxEntryPacket(Guid OperationId, long Id, long CreatedAtUnixMs, string Sender, StoreItemEntry Item, ItemDetailEntry Details)
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 8 + 8 + 2;
    public const int MaxSize = MinimumSize + ItemBoxWire.MaxSenderBytes + StoreItemEntry.MaxSize + ItemDetailEntry.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemBoxEntry);
        WireHeader.Write(ref writer, ItemBoxWire.Version);
        writer.Guid(OperationId);
        writer.Int64(Id);
        writer.Int64(CreatedAtUnixMs);
        Utf8Text.Write(ref writer, Sender, ItemBoxWire.MaxSenderBytes);
        Item.Write(ref writer);
        Details.Write(ref writer);
        return writer.Finish();
    }

    public static ItemBoxEntryPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Item box entry");
        var operation = reader.Guid();
        var id = reader.Int64();
        var created = reader.Int64();
        var sender = Utf8Text.Read(ref reader, ItemBoxWire.MaxSenderBytes, "Item box entry");
        var item = StoreItemEntry.Read(ref reader);
        return new ItemBoxEntryPacket(operation, id, created, sender, item, ItemDetailEntry.Read(ref reader));
    }
}

// S->C {header, Guid OperationId, uint8 Status, uint8 0, uint16 0, int32 Count}: ends an open, after its entries.
public sealed record ItemBoxListPacket(Guid OperationId, byte Status, int Count) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 4 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemBoxList);
        WireHeader.Write(ref writer, ItemBoxWire.Version);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int32(Count);
        return writer.Finish();
    }

    public static ItemBoxListPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Item box list");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        return new ItemBoxListPacket(operation, status, reader.Int32());
    }
}

// C->S {header, Guid OperationId, uint64 NpcId, uint8 Action (1 take, 2 discard, 3 send), uint8 0, uint16 0, int64 Id (take,
// discard), Guid ItemId (send: the bag item), text Recipient (send: a character's name)}. Answered by ItemBoxResult.
public sealed record ItemBoxActionPacket(Guid OperationId, ulong NpcId, byte Action, long Id, Guid ItemId, string Recipient) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 8 + 4 + 8 + 16 + 2;
    public const int MaxSize = MinimumSize + ItemBoxWire.MaxRecipientBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemBoxAction);
        WireHeader.Write(ref writer, ItemBoxWire.Version);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Byte(Action);
        writer.Zero(3);
        writer.Int64(Id);
        writer.Guid(ItemId);
        Utf8Text.Write(ref writer, Recipient, ItemBoxWire.MaxRecipientBytes);
        return writer.Finish();
    }

    public static ItemBoxActionPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        WireHeader.Read(ref reader, "Item box action");
        var operation = reader.Guid();
        var npc = reader.UInt64();
        var action = reader.Byte();
        reader.Skip(3);
        var id = reader.Int64();
        var item = reader.Guid();
        return new ItemBoxActionPacket(operation, npc, action, id, item, Utf8Text.Read(ref reader, ItemBoxWire.MaxRecipientBytes, "Item box action"));
    }
}

// S->C {header, Guid OperationId, uint8 Status (ItemBoxStatus), uint8 0, uint16 0}.
public sealed record ItemBoxResultPacket(Guid OperationId, byte Status) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemBoxResult);
        WireHeader.Write(ref writer, ItemBoxWire.Version);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        return writer.Finish();
    }

    public static ItemBoxResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Item box result");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        return new ItemBoxResultPacket(operation, status);
    }
}
