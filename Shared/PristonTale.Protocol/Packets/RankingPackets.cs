using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// The rankings window's boards.
public enum RankingBoard : byte
{
    Level = 0,
    Bellatra = 1
}

public static class RankingWire
{
    public const ushort Version = 1;
    // A board's lines go in parts of at most MaxEntries (a frame holds no more), each with the place of its first line.
    public const int MaxEntries = 50;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Ranking message", Version);
}

// C->S {header, uint8 Board (RankingBoard), uint8 Class (a PlayerClass for one class's level board, 0 all)}.
public readonly record struct RankingsRequestPacket(RankingBoard Board, byte Class) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RankingsRequest);
        RankingWire.Header(ref writer);
        writer.Byte((byte)Board);
        writer.Byte(Class);
        return writer.Finish();
    }

    public static RankingsRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 2);
        RankingWire.ReadHeader(ref reader);
        var board = (RankingBoard)reader.Byte();
        if (!Enum.IsDefined(board))
        {
            throw new InvalidDataException($"Ranking board {(byte)board} is unknown.");
        }

        return new RankingsRequestPacket(board, reader.Byte());
    }
}

// One line of a board: a character (Class, Level, ExpBasisPoints the share of its level's experience in hundredths of a
// percent, Clan) or a clan (Name, Value its Bellatra points).
public readonly record struct RankingWireEntry(string Name, byte Class, int Level, ushort ExpBasisPoints, string Clan, long Value);

// S->C {header, uint8 Board, uint8 Class, uint8 Available (0 while storage refuses), uint8 First (the place of the first
// entry less 1: 0 starts the board, later parts follow it), uint8 Count (at most RankingWire.MaxEntries), {text Name
// (64 B), uint8 Class, varuint Level, uint16 ExpBasisPoints, text Clan (48 B), varuint Value} x Count}; the entries are in
// rank order.
public sealed record RankingsPacket(RankingBoard Board, byte Class, bool Available, IReadOnlyList<RankingWireEntry> Entries, int First = 0) : IFramePacket
{
    private const int EntryMaxSize = 2 + AccountWire.MaxNameBytes + 1 + VarInt.MaxBytes64 + 2 + 2 + ClanWire.MaxClanNameBytes + VarInt.MaxBytes64;
    public const int MaxSize = 8 + WireHeader.Size + 5 + RankingWire.MaxEntries * EntryMaxSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Rankings);
        RankingWire.Header(ref writer);
        writer.Byte((byte)Board);
        writer.Byte(Class);
        writer.Byte((byte)(Available ? 1 : 0));
        writer.Byte((byte)Math.Clamp(First, 0, byte.MaxValue));
        var count = Math.Min(Entries.Count, RankingWire.MaxEntries);
        writer.Byte((byte)count);
        for (var index = 0; index < count; index++)
        {
            var entry = Entries[index];
            Utf8Text.Write(ref writer, entry.Name, AccountWire.MaxNameBytes);
            writer.Byte(entry.Class);
            writer.VarUInt((ulong)Math.Max(0, entry.Level));
            writer.UInt16(entry.ExpBasisPoints);
            Utf8Text.Write(ref writer, entry.Clan, ClanWire.MaxClanNameBytes);
            writer.VarUInt((ulong)Math.Max(0, entry.Value));
        }

        return writer.Finish();
    }

    public static RankingsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 5);
        RankingWire.ReadHeader(ref reader);
        var board = (RankingBoard)reader.Byte();
        var job = reader.Byte();
        var available = reader.Byte() != 0;
        var first = reader.Byte();
        var count = reader.Byte();
        if (count > RankingWire.MaxEntries)
        {
            throw new InvalidDataException("Rankings have too many entries.");
        }

        var entries = new RankingWireEntry[count];
        for (var index = 0; index < count; index++)
        {
            var name = Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Ranking");
            var klass = reader.Byte();
            var level = (int)Math.Min(reader.VarUInt(), int.MaxValue);
            var share = reader.UInt16();
            var clan = Utf8Text.Read(ref reader, ClanWire.MaxClanNameBytes, "Ranking");
            entries[index] = new RankingWireEntry(name, klass, level, share, clan, (long)Math.Min(reader.VarUInt(), long.MaxValue));
        }

        return new RankingsPacket(board, job, available, entries, first);
    }
}
