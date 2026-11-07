using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C {header, uint8 Count, 3 zero bytes, Count x {int32 Kind, int32 SecondsLeft}}: the running world buffs, when the
// receiver enters the world and whenever they change.
public sealed record WorldBuffPacket(IReadOnlyList<(int Kind, int SecondsLeft)> Buffs) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int MaxBuffs = 16;
    private const int HeaderSize = 8 + WireHeader.Size + 4;
    public const int MaxSize = HeaderSize + MaxBuffs * 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.WorldBuffState);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Byte((byte)Buffs.Count);
        writer.Zero(3);
        foreach (var (kind, secondsLeft) in Buffs)
        {
            writer.Int32(kind);
            writer.Int32(secondsLeft);
        }

        return writer.Finish();
    }

    public static WorldBuffPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        WireHeader.Read(ref reader, "World buffs");
        var count = reader.Byte();
        reader.Skip(3);
        if (count > MaxBuffs)
        {
            throw new InvalidDataException("Too many world buffs.");
        }

        var buffs = new (int Kind, int SecondsLeft)[count];
        for (var index = 0; index < count; index++)
        {
            var kind = reader.Int32();
            buffs[index] = (kind, reader.Int32());
        }

        return new WorldBuffPacket(buffs);
    }
}
