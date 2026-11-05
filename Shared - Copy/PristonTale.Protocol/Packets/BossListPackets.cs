using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public readonly record struct BossListWireEntry(string BossName, string FieldName, uint SecondsLeft, bool Exact);

// S->C {header, uint16 Version = 1, uint16 0, uint16 Count, uint16 0, Count x {uint32 SecondsLeft, uint8 Exact, uint8 0,
// uint16 NameBytes, UTF-8 BossName, uint16 FieldBytes, UTF-8 FieldName}}: every scheduled boss of the world, soonest pass
// first, when the receiver enters the world and whenever a pass in the list has come. Exact 0: the pass is on a later
// day (only its hour is known). Names are cut to 32 bytes so the longest list fits one frame.
public sealed record BossListPacket(IReadOnlyList<BossListWireEntry> Bosses) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int MaxBosses = 96;
    public const int MaxNameBytes = 32;
    public const int FixedSize = 8 + WireHeader.Size + 4;

    public int Write(Span<byte> destination)
    {
        var count = Math.Min(Bosses.Count, MaxBosses);
        var writer = new PacketWriter(destination, EtervaleOpcodes.BossList);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt16((ushort)count);
        writer.UInt16(0);
        for (var index = 0; index < count; index++)
        {
            var boss = Bosses[index];
            writer.UInt32(boss.SecondsLeft);
            writer.Byte(boss.Exact ? (byte)1 : (byte)0);
            writer.Byte(0);
            Utf8Text.Write(ref writer, boss.BossName, MaxNameBytes);
            Utf8Text.Write(ref writer, boss.FieldName, MaxNameBytes);
        }

        return writer.Finish();
    }

    public static BossListPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, FixedSize);
        WireHeader.Read(ref reader, "Boss list");
        var count = reader.UInt16();
        reader.Skip(2);
        if (count > MaxBosses)
        {
            throw new InvalidDataException("Boss list lists more bosses than allowed.");
        }

        var bosses = new BossListWireEntry[count];
        for (var index = 0; index < count; index++)
        {
            var seconds = reader.UInt32();
            var exact = reader.Byte() != 0;
            reader.Skip(1);
            var name = Utf8Text.Read(ref reader, MaxNameBytes, "Boss list");
            bosses[index] = new BossListWireEntry(name, Utf8Text.Read(ref reader, MaxNameBytes, "Boss list"), seconds, exact);
        }

        return new BossListPacket(bosses);
    }
}
