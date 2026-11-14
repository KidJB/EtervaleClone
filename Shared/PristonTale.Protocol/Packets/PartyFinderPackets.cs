using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Social;

namespace PristonTale.Protocol.Packets;

public static class PartyFinderWire
{
    public const ushort Version = 1;

    // The finder lists at most MaxListings parties, sent in parts of at most MaxPerPart (a frame holds no more).
    public const int MaxListings = 30;
    public const int MaxPerPart = 10;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Party finder message", Version);
}

// C->S {header}: asks the parties the finder lists.
public readonly record struct PartyFinderRequestPacket : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyFinderRequest);
        PartyFinderWire.Header(ref writer);
        return writer.Finish();
    }

    public static PartyFinderRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size);
        PartyFinderWire.ReadHeader(ref reader);
        return default;
    }
}

public readonly record struct PartyFinderWireMember(string Name, byte Class, int Level);

public sealed record PartyFinderWireListing(ulong LeaderId, int FieldId, PartyMode Mode, int AverageLevel, IReadOnlyList<PartyFinderWireMember> Members,
    PartyJoinMode JoinMode = PartyJoinMode.Approval, int MinLevel = 0, ushort Classes = 0);

// The asking player's own party when it leads one: its finder settings (with the password) and its members.
public sealed record PartyFinderWireOwn(PartyJoinMode JoinMode, int MinLevel, string Password, ushort Classes, IReadOnlyList<PartyFinderWireMember> Members);

// S->C {header, uint8 First (the index of the part's first party: 0 starts the list), uint8 HasOwn, [own: uint8 JoinMode,
// varuint MinLevel, text Password (64 B), uint16 Classes, members], uint8 Count (at most PartyFinderWire.MaxPerPart),
// {varuint LeaderId, varint FieldId, uint8 Mode (PartyMode), varuint AverageLevel, uint8 JoinMode (PartyJoinMode), varuint
// MinLevel, uint16 Classes (a bit per PlayerClass), members} x Count}; members are {uint8 Count (at most
// PartyLimits.MaxMembers, the leader first), {text Name (64 B), uint8 Class, varuint Level} x Count}. A player alone joins a
// listed party with PartyFinderJoin.
public sealed record PartyFinderPacket(int First, IReadOnlyList<PartyFinderWireListing> Listings, PartyFinderWireOwn? Own = null) : IFramePacket
{
    public const int MaxPasswordBytes = 64;
    private const int MemberMaxSize = 2 + AccountWire.MaxNameBytes + 1 + VarInt.MaxBytes64;
    private const int MembersMaxSize = 1 + PartyLimits.MaxMembers * MemberMaxSize;
    private const int OwnMaxSize = 1 + VarInt.MaxBytes64 + 2 + MaxPasswordBytes + 2 + MembersMaxSize;
    private const int ListingMaxSize = 4 * VarInt.MaxBytes64 + 1 + 1 + 2 + MembersMaxSize;
    public const int MaxSize = 8 + WireHeader.Size + 3 + OwnMaxSize + PartyFinderWire.MaxPerPart * ListingMaxSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyFinder);
        PartyFinderWire.Header(ref writer);
        writer.Byte((byte)Math.Clamp(First, 0, byte.MaxValue));
        writer.Byte((byte)(Own is null ? 0 : 1));
        if (Own is { } own)
        {
            writer.Byte((byte)own.JoinMode);
            writer.VarUInt((ulong)Math.Max(0, own.MinLevel));
            Utf8Text.Write(ref writer, own.Password ?? "", MaxPasswordBytes);
            writer.UInt16(own.Classes);
            WriteMembers(ref writer, own.Members);
        }

        var count = Math.Min(Listings.Count, PartyFinderWire.MaxPerPart);
        writer.Byte((byte)count);
        for (var index = 0; index < count; index++)
        {
            var listing = Listings[index];
            writer.VarUInt(listing.LeaderId);
            writer.VarInt(listing.FieldId);
            writer.Byte((byte)listing.Mode);
            writer.VarUInt((ulong)Math.Max(0, listing.AverageLevel));
            writer.Byte((byte)listing.JoinMode);
            writer.VarUInt((ulong)Math.Max(0, listing.MinLevel));
            writer.UInt16(listing.Classes);
            WriteMembers(ref writer, listing.Members);
        }

        return writer.Finish();
    }

    private static void WriteMembers(ref PacketWriter writer, IReadOnlyList<PartyFinderWireMember> list)
    {
        var members = Math.Min(list.Count, PartyLimits.MaxMembers);
        writer.Byte((byte)members);
        for (var member = 0; member < members; member++)
        {
            var entry = list[member];
            Utf8Text.Write(ref writer, entry.Name, AccountWire.MaxNameBytes);
            writer.Byte(entry.Class);
            writer.VarUInt((ulong)Math.Max(0, entry.Level));
        }
    }

    private static PartyFinderWireMember[] ReadMembers(ref PacketReader reader)
    {
        var members = reader.Byte();
        if (members > PartyLimits.MaxMembers)
        {
            throw new InvalidDataException("A listed party has too many members.");
        }

        var list = new PartyFinderWireMember[members];
        for (var member = 0; member < members; member++)
        {
            var name = Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Party finder");
            var klass = reader.Byte();
            list[member] = new PartyFinderWireMember(name, klass, (int)Math.Min(reader.VarUInt(), int.MaxValue));
        }

        return list;
    }

    internal static PartyJoinMode JoinModeOf(byte value) =>
        Enum.IsDefined((PartyJoinMode)value) ? (PartyJoinMode)value : throw new InvalidDataException($"Party join mode {value} is unknown.");

    public static PartyFinderPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 3);
        PartyFinderWire.ReadHeader(ref reader);
        var first = reader.Byte();
        PartyFinderWireOwn? own = null;
        if (reader.Byte() != 0)
        {
            var ownMode = JoinModeOf(reader.Byte());
            var ownLevel = (int)Math.Min(reader.VarUInt(), int.MaxValue);
            var password = Utf8Text.Read(ref reader, MaxPasswordBytes, "Party finder");
            var ownClasses = reader.UInt16();
            own = new PartyFinderWireOwn(ownMode, ownLevel, password, ownClasses, ReadMembers(ref reader));
        }

        var count = reader.Byte();
        if (count > PartyFinderWire.MaxPerPart)
        {
            throw new InvalidDataException("The party finder sent too many parties.");
        }

        var listings = new PartyFinderWireListing[count];
        for (var index = 0; index < count; index++)
        {
            var leader = reader.VarUInt();
            var field = (int)reader.VarInt();
            var mode = (PartyMode)reader.Byte();
            var level = (int)Math.Min(reader.VarUInt(), int.MaxValue);
            var joinMode = JoinModeOf(reader.Byte());
            var minLevel = (int)Math.Min(reader.VarUInt(), int.MaxValue);
            var classes = reader.UInt16();
            listings[index] = new PartyFinderWireListing(leader, field, mode, level, ReadMembers(ref reader), joinMode, minLevel, classes);
        }

        return new PartyFinderPacket(first, listings, own);
    }
}

// C->S {header, uint8 JoinMode (PartyJoinMode), varuint MinLevel, text Password (64 B), uint16 Classes}: the leader's
// finder settings for its party; answered by a server line.
public readonly record struct PartyFinderSettingsPacket(PartyJoinMode JoinMode, int MinLevel, string Password, ushort Classes) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 1 + VarInt.MaxBytes64 + 2 + PartyFinderPacket.MaxPasswordBytes + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyFinderSettings);
        PartyFinderWire.Header(ref writer);
        writer.Byte((byte)JoinMode);
        writer.VarUInt((ulong)Math.Max(0, MinLevel));
        Utf8Text.Write(ref writer, Password ?? "", PartyFinderPacket.MaxPasswordBytes);
        writer.UInt16(Classes);
        return writer.Finish();
    }

    public static PartyFinderSettingsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 6);
        PartyFinderWire.ReadHeader(ref reader);
        var mode = PartyFinderPacket.JoinModeOf(reader.Byte());
        var level = (int)Math.Min(reader.VarUInt(), int.MaxValue);
        var password = Utf8Text.Read(ref reader, PartyFinderPacket.MaxPasswordBytes, "Party finder settings");
        return new PartyFinderSettingsPacket(mode, level, password, reader.UInt16());
    }
}

// C->S {header, varuint LeaderId, text Password (64 B)}: joins a listed party through the finder; answered by the party or
// a server line.
public readonly record struct PartyFinderJoinPacket(ulong LeaderId, string Password) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + VarInt.MaxBytes64 + 2 + PartyFinderPacket.MaxPasswordBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyFinderJoin);
        PartyFinderWire.Header(ref writer);
        writer.VarUInt(LeaderId);
        Utf8Text.Write(ref writer, Password ?? "", PartyFinderPacket.MaxPasswordBytes);
        return writer.Finish();
    }

    public static PartyFinderJoinPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 3);
        PartyFinderWire.ReadHeader(ref reader);
        var leader = reader.VarUInt();
        return new PartyFinderJoinPacket(leader, Utf8Text.Read(ref reader, PartyFinderPacket.MaxPasswordBytes, "Party finder join"));
    }
}
