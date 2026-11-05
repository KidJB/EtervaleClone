using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C {header, uint16 Version = 1, uint16 0, uint8 Exact, uint8 0, uint16 0, uint32 SecondsLeft, uint16 NameBytes, UTF-8
// BossName}: the next boss pass of the receiver's field, sent when it enters the world, changes field or the pass after
// the next one becomes the next. An empty name: the field has no boss. Exact 0: the pass falls on a later day, of which
// only the hour is known (SecondsLeft counts to that hour at today's minute).
public readonly record struct BossTimePacket(string BossName, uint SecondsLeft, bool Exact) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int MaxNameBytes = 64;
    public const int FixedSize = 8 + WireHeader.Size + 8 + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.BossTime);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Byte(Exact ? (byte)1 : (byte)0);
        writer.Byte(0);
        writer.UInt16(0);
        writer.UInt32(SecondsLeft);
        Utf8Text.Write(ref writer, BossName, MaxNameBytes);
        return writer.Finish();
    }

    public static BossTimePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, FixedSize);
        WireHeader.Read(ref reader, "Boss time");
        var exact = reader.Byte() != 0;
        reader.Skip(3);
        var seconds = reader.UInt32();
        return new BossTimePacket(Utf8Text.Read(ref reader, MaxNameBytes, "Boss time"), seconds, exact);
    }
}
