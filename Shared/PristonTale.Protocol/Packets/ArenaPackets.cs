using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C {header (version 1), uint8 Shown, uint8 EventId, uint8 Running, varuint Seconds (until the start, or since it
// began), varuint BossLife, varuint BossMaxLife}: the boss arena's state for a player in its field; Shown 0 hides it
// when the player leaves the field.
public readonly record struct ArenaStatusPacket(bool Shown, byte EventId, bool Running, int Seconds, int BossLife, int BossMaxLife) : IFramePacket
{
    private const ushort Version = 1;
    public const int MaxSize = 8 + WireHeader.Size + 3 + 3 * VarInt.MaxBytes64;
    public static readonly ArenaStatusPacket Hidden = new(false, 0, false, 0, 0, 0);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ArenaStatus);
        WireHeader.Write(ref writer, Version);
        writer.Byte((byte)(Shown ? 1 : 0));
        writer.Byte(EventId);
        writer.Byte((byte)(Running ? 1 : 0));
        writer.VarUInt((ulong)Math.Max(0, Seconds));
        writer.VarUInt((ulong)Math.Max(0, BossLife));
        writer.VarUInt((ulong)Math.Max(0, BossMaxLife));
        return writer.Finish();
    }

    public static ArenaStatusPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 6);
        WireHeader.Read(ref reader, "Arena status", Version);
        var shown = reader.Byte() != 0;
        var id = reader.Byte();
        var running = reader.Byte() != 0;
        var seconds = (int)Math.Min(reader.VarUInt(), int.MaxValue);
        var life = (int)Math.Min(reader.VarUInt(), int.MaxValue);
        return new ArenaStatusPacket(shown, id, running, seconds, life, (int)Math.Min(reader.VarUInt(), int.MaxValue));
    }
}
