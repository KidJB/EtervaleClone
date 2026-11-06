using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// A daily quest of the day as the owner sees it: {int32 Id, int32 Kills, uint8 Flags (1 accepted, 2 claimed), uint8 0,
// uint16 0}.
public readonly record struct DailyQuestEntry(int Id, int Kills, byte Flags) : IWireEntry<DailyQuestEntry>
{
    public const byte Accepted = 1;
    public const byte Claimed = 2;
    public const int Size = 4 + 4 + 4;

    static int IWireEntry<DailyQuestEntry>.Size => Size;

    public void Write(ref PacketWriter writer)
    {
        writer.Int32(Id);
        writer.Int32(Kills);
        writer.Byte(Flags);
        writer.Zero(3);
    }

    public static DailyQuestEntry Read(ref PacketReader reader)
    {
        var id = reader.Int32();
        var kills = reader.Int32();
        var flags = reader.Byte();
        reader.Skip(3);
        return new DailyQuestEntry(id, kills, flags);
    }
}

// S->C {header, int32 Day, int32 Count, entries}: the receiver's daily quests of the day (accepted or claimed), when it
// enters the world and whenever they change. Day is the server's day number (DailyQuests.DayOf).
public sealed record DailyQuestStatePacket(int Day, IReadOnlyList<DailyQuestEntry> Entries) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int MaxEntries = 64;
    private const int HeaderSize = 8 + WireHeader.Size + 4 + 4;
    public const int MaxSize = HeaderSize + MaxEntries * DailyQuestEntry.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DailyQuestState);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Int32(Day);
        WireList.Write(ref writer, Entries);
        return writer.Finish();
    }

    public static DailyQuestStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        WireHeader.Read(ref reader, "Daily quest state");
        var day = reader.Int32();
        return new DailyQuestStatePacket(day, WireList.Read<DailyQuestEntry>(ref reader, MaxEntries, "Daily quest state"));
    }
}

// C->S {header, Guid OperationId, uint64 NpcId, int32 QuestId, uint8 Action (1 accept, 2 claim), uint8 0, uint16 0}.
// Answered by DailyQuestResult.
public sealed record DailyQuestRequestPacket(Guid OperationId, ulong NpcId, int QuestId, byte Action) : IFramePacket
{
    public const byte Accept = 1;
    public const byte Claim = 2;
    public const int Size = 8 + WireHeader.Size + 16 + 8 + 4 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DailyQuestRequest);
        WireHeader.Write(ref writer, 1);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Int32(QuestId);
        writer.Byte(Action);
        writer.Zero(3);
        return writer.Finish();
    }

    public static DailyQuestRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Daily quest request");
        var operation = reader.Guid();
        var npc = reader.UInt64();
        var quest = reader.Int32();
        var action = reader.Byte();
        reader.Skip(3);
        return new DailyQuestRequestPacket(operation, npc, quest, action);
    }
}

// S->C {header, Guid OperationId, uint8 Status (DailyQuestStatus), uint8 0, uint16 0}.
public sealed record DailyQuestResultPacket(Guid OperationId, byte Status) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DailyQuestResult);
        WireHeader.Write(ref writer, 1);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        return writer.Finish();
    }

    public static DailyQuestResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Daily quest result");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        return new DailyQuestResultPacket(operation, status);
    }
}
