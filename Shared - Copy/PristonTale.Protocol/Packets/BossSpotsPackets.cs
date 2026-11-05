using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public readonly record struct BossSpotEntry(string BossName, int X, int Z, bool Alive);

// S->C {header, uint16 Version = 1, uint16 0, uint16 Count, uint16 0, Count x {int32 X, int32 Z (fixed point), uint8 Alive,
// uint8 0, uint16 NameBytes, UTF-8 BossName}}: the bosses of the receiver's field at their spot, when it enters a field
// and whenever a boss comes alive or goes. No entries: the field has no boss.
public sealed record BossSpotsPacket(IReadOnlyList<BossSpotEntry> Spots) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int MaxSpots = 16;
    public const int MaxNameBytes = 64;
    public const int FixedSize = 8 + WireHeader.Size + 4;

    public int Write(Span<byte> destination)
    {
        if (Spots.Count > MaxSpots)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many boss spots.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.BossSpots);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt16((ushort)Spots.Count);
        writer.UInt16(0);
        foreach (var spot in Spots)
        {
            writer.Int32(spot.X);
            writer.Int32(spot.Z);
            writer.Byte(spot.Alive ? (byte)1 : (byte)0);
            writer.Byte(0);
            Utf8Text.Write(ref writer, spot.BossName, MaxNameBytes);
        }

        return writer.Finish();
    }

    public static BossSpotsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, FixedSize);
        WireHeader.Read(ref reader, "Boss spots");
        var count = reader.UInt16();
        reader.Skip(2);
        if (count > MaxSpots)
        {
            throw new InvalidDataException("Boss spots lists more spots than allowed.");
        }

        var spots = new BossSpotEntry[count];
        for (var index = 0; index < count; index++)
        {
            var x = reader.Int32();
            var z = reader.Int32();
            var alive = reader.Byte() != 0;
            reader.Skip(1);
            spots[index] = new BossSpotEntry(Utf8Text.Read(ref reader, MaxNameBytes, "Boss spots"), x, z, alive);
        }

        return new BossSpotsPacket(spots);
    }
}
