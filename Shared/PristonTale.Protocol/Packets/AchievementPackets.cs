using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Social;

namespace PristonTale.Protocol.Packets;

public static class AchievementWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Achievement message", Version);
}

// C->S {header}: asks the player's own badges.
public readonly record struct AchievementsRequestPacket : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.AchievementsRequest);
        AchievementWire.Header(ref writer);
        return writer.Finish();
    }

    public static AchievementsRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size);
        AchievementWire.ReadHeader(ref reader);
        return default;
    }
}

// C->S {header, varuint Id}: wears a badge's title, 0 hides the title; answered by an Achievements message.
public readonly record struct AchievementTitlePacket(uint Id) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + VarInt.MaxBytes64;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.AchievementTitle);
        AchievementWire.Header(ref writer);
        writer.VarUInt(Id);
        return writer.Finish();
    }

    public static AchievementTitlePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        AchievementWire.ReadHeader(ref reader);
        return new AchievementTitlePacket((uint)reader.VarUInt());
    }
}

public readonly record struct AchievementWireEntry(uint Id, ulong HolderKey);

// S->C {header, varuint ActiveId (the badge worn as a title, 0 none), uint8 Count (at most AchievementLimits.MaxShown), {varuint Id,
// varuint HolderKey (a top badge's class)} x Count}.
public sealed record AchievementsPacket(uint ActiveId, IReadOnlyList<AchievementWireEntry> Entries) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + VarInt.MaxBytes64 + 1 + AchievementLimits.MaxShown * (2 * VarInt.MaxBytes64);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Achievements);
        AchievementWire.Header(ref writer);
        writer.VarUInt(ActiveId);
        writer.Byte((byte)Entries.Count);
        foreach (var entry in Entries)
        {
            writer.VarUInt(entry.Id);
            writer.VarUInt(entry.HolderKey);
        }

        return writer.Finish();
    }

    public static AchievementsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 2);
        AchievementWire.ReadHeader(ref reader);
        var active = (uint)reader.VarUInt();
        int count = reader.Byte();
        if (count > AchievementLimits.MaxShown)
        {
            throw new InvalidDataException("Achievement list is too long.");
        }

        var entries = new AchievementWireEntry[count];
        for (var index = 0; index < count; index++)
        {
            entries[index] = new AchievementWireEntry((uint)reader.VarUInt(), reader.VarUInt());
        }

        return new AchievementsPacket(active, entries);
    }
}
