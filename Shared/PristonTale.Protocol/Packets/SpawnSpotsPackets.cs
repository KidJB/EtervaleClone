using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public readonly record struct SpawnSpotEntry(int X, int Z);

// S->C {header, uint16 Version = 1, uint16 0, uint16 Count, uint16 0, Count x {int32 X, int32 Z (fixed point)}}: the
// active monster spawn points of the receiver's field, for the minimap, when it enters a field. No entries: none.
public sealed record SpawnSpotsPacket(IReadOnlyList<SpawnSpotEntry> Spots) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int MaxSpots = 900;
    public const int FixedSize = 8 + WireHeader.Size + 4;

    public int Write(Span<byte> destination)
    {
        if (Spots.Count > MaxSpots)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many spawn spots.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.SpawnSpots);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt16((ushort)Spots.Count);
        writer.UInt16(0);
        foreach (var spot in Spots)
        {
            writer.Int32(spot.X);
            writer.Int32(spot.Z);
        }

        return writer.Finish();
    }

    public static SpawnSpotsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, FixedSize);
        WireHeader.Read(ref reader, "Spawn spots");
        var count = reader.UInt16();
        reader.Skip(2);
        if (count > MaxSpots)
        {
            throw new InvalidDataException("Spawn spots lists more spots than allowed.");
        }

        var spots = new SpawnSpotEntry[count];
        for (var index = 0; index < count; index++)
        {
            var x = reader.Int32();
            var z = reader.Int32();
            spots[index] = new SpawnSpotEntry(x, z);
        }

        return new SpawnSpotsPacket(spots);
    }
}
