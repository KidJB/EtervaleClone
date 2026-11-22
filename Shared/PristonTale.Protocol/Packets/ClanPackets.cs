using PristonTale.Contracts.Characters;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// What a player asks of the clan service. Found and Entrust name the Clan Master (Id) they are asked at; Invite names the
// player (Id); Expel, Appoint, Discharge and Entrust name a member (Name); Found names the new clan. Mark is the chief's
// upload (ClanMarkPacket), answered as the others. SetPermissions is the chief's new rank rights (Id: packed as
// ClanStatePacket.Permissions); WarehouseLog asks for the clan warehouse's log (ClanWarehouseLogPacket).
public enum ClanAction : byte
{
    Open = 0,
    Found = 1,
    Invite = 2,
    Accept = 3,
    Refuse = 4,
    Leave = 5,
    Expel = 6,
    Disband = 7,
    Appoint = 8,
    Discharge = 9,
    Entrust = 10,
    Mark = 11,
    SetPermissions = 12,
    WarehouseLog = 13,

    // Gold to the treasury (Id: the Clan Warehouse keeper, Amount) and the chief's upgrade (Id: ClanUpgradeKind).
    Donate = 14,
    Upgrade = 15,

    // Coins to the coin treasury (Id: the Clan Warehouse keeper, Amount) and coins spent on a clan buff (Id: the buff's
    // WorldBuffs.Argument).
    DonateCoins = 16,
    SpendCoins = 17
}

public readonly record struct ClanMemberWire(string Name, PlayerClass Class, int Level, byte Rank, bool Online);

// Clan messages. Each body starts with {uint16 Version = 1, uint16 Flags = 0}; names are UTF-8 with a uint16 byte length.
public static class ClanWire
{
    public const ushort Version = 1;
    public const int MaxNameBytes = 64;

    // A clan name: 16 characters of at most three UTF-8 bytes (ClanRules).
    public const int MaxClanNameBytes = 48;

    // A clan mark: MarkSize x MarkSize pixels of 8-bit red, green, blue and alpha, rows from the top.
    public const int MarkSize = 32;
    public const int PixelBytes = 4;
    public const int MarkBytes = MarkSize * MarkSize * PixelBytes;
    public const int MaxIntroBytes = 256;
    public const int MaxMembers = 80;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Clan message");

    internal static string Name(ref PacketReader reader) => Utf8Text.Read(ref reader, MaxNameBytes, "Clan message");

    internal static string ClanName(ref PacketReader reader) => Utf8Text.Read(ref reader, MaxClanNameBytes, "Clan message");
}

// C->S {header, uint8 Action, varuint Id, text Name, varuint Amount}.
public sealed record ClanRequestPacket(ClanAction Action, ulong Id, string Name, ulong Amount = 0) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 1 + 1 + 2 + 1;
    public const int MaxSize = MinimumSize + 2 * VarInt.MaxBytes64 + ClanWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanRequest);
        ClanWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.VarUInt(Id);
        Utf8Text.Write(ref writer, Name, ClanWire.MaxNameBytes);
        writer.VarUInt(Amount);
        return writer.Finish();
    }

    public static ClanRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ClanWire.ReadHeader(ref reader);
        var action = (ClanAction)reader.Byte();
        if (!Enum.IsDefined(action))
        {
            throw new InvalidDataException("Clan request has an unknown action.");
        }

        return new ClanRequestPacket(action, reader.VarUInt(), ClanWire.Name(ref reader), reader.VarUInt());
    }
}

// S->C the player's clan, or none: {header, uint8 HasClan; with a clan varuint ClanId, text Name, uint8 State, int64
// Deadline (Unix seconds, 0 none), uint8 Rank, uint8 Capacity, text Intro, varuint MarkVersion, uint16 Count, then per
// member {text Name, uint8 Class, varuint Level, uint8 Rank, uint8 Online}, then uint16 Permissions (the members' rights
// in the low byte, the vice's in the high one), uint8 Rights (the receiver's), varuint Treasury, uint8 SlotLevel, uint8
// WeightLevel, varuint CoinTreasury}. Rights are ClanPermission bits; the levels are the clan warehouse's upgrades.
public sealed record ClanStatePacket(ulong ClanId, string Name, byte State, long Deadline, byte Rank, int Capacity, string Intro, int MarkVersion,
    IReadOnlyList<ClanMemberWire> Members, ushort Permissions = 0, byte Rights = 0, long Treasury = 0, byte SlotLevel = 0, byte WeightLevel = 0,
    long CoinTreasury = 0) : IFramePacket
{
    private const int MemberSize = 2 + ClanWire.MaxNameBytes + 1 + VarInt.MaxBytes64 + 2;
    public const int MaxSize = 8 + WireHeader.Size + 1 + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 1 + 8 + 2 + 2 + ClanWire.MaxIntroBytes
        + VarInt.MaxBytes64 + 2 + ClanWire.MaxMembers * MemberSize + 2 + 1 + VarInt.MaxBytes64 + 2 + VarInt.MaxBytes64;

    public static readonly ClanStatePacket None = new(0, "", 0, 0, 0, 0, "", 0, []);

    public bool HasClan => ClanId != 0;

    public int Write(Span<byte> destination)
    {
        if (Members.Count > ClanWire.MaxMembers)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many clan members.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanState);
        ClanWire.Header(ref writer);
        writer.Byte(HasClan ? (byte)1 : (byte)0);
        if (HasClan)
        {
            writer.VarUInt(ClanId);
            Utf8Text.Write(ref writer, Name, ClanWire.MaxClanNameBytes);
            writer.Byte(State);
            writer.Int64(Deadline);
            writer.Byte(Rank);
            writer.Byte((byte)Math.Clamp(Capacity, 0, byte.MaxValue));
            Utf8Text.Write(ref writer, Intro, ClanWire.MaxIntroBytes);
            writer.VarUInt((ulong)Math.Max(0, MarkVersion));
            writer.UInt16((ushort)Members.Count);
            foreach (var member in Members)
            {
                Utf8Text.Write(ref writer, member.Name, ClanWire.MaxNameBytes);
                writer.Byte((byte)member.Class);
                writer.VarUInt((ulong)Math.Max(0, member.Level));
                writer.Byte(member.Rank);
                writer.Byte(member.Online ? (byte)1 : (byte)0);
            }

            writer.UInt16(Permissions);
            writer.Byte(Rights);
            writer.VarUInt((ulong)Math.Max(0, Treasury));
            writer.Byte(SlotLevel);
            writer.Byte(WeightLevel);
            writer.VarUInt((ulong)Math.Max(0, CoinTreasury));
        }

        return writer.Finish();
    }

    public static ClanStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        ClanWire.ReadHeader(ref reader);
        if (reader.Byte() == 0)
        {
            return None;
        }

        var clanId = reader.VarUInt();
        var name = ClanWire.ClanName(ref reader);
        var state = reader.Byte();
        var deadline = reader.Int64();
        var rank = reader.Byte();
        var capacity = reader.Byte();
        var intro = Utf8Text.Read(ref reader, ClanWire.MaxIntroBytes, "Clan message");
        var mark = reader.VarUInt31();
        var count = reader.UInt16();
        if (count > ClanWire.MaxMembers)
        {
            throw new InvalidDataException("Clan state has too many members.");
        }

        var members = new ClanMemberWire[count];
        for (var index = 0; index < count; index++)
        {
            members[index] = new ClanMemberWire(ClanWire.Name(ref reader), (PlayerClass)reader.Byte(), reader.VarUInt31(), reader.Byte(), reader.Byte() != 0);
        }

        return new ClanStatePacket(clanId, name, state, deadline, rank, capacity, intro, mark, members, reader.UInt16(), reader.Byte())
        {
            Treasury = (long)Math.Min(reader.VarUInt(), long.MaxValue),
            SlotLevel = reader.Byte(),
            WeightLevel = reader.Byte(),
            CoinTreasury = (long)Math.Min(reader.VarUInt(), long.MaxValue)
        };
    }
}

// S->C a clan's invitation: {header, varuint RecruiterId, text ClanName, text ChiefName}.
public sealed record ClanInvitePacket(ulong RecruiterId, string ClanName, string ChiefName) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 2 + ClanWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanInvite);
        ClanWire.Header(ref writer);
        writer.VarUInt(RecruiterId);
        Utf8Text.Write(ref writer, ClanName, ClanWire.MaxClanNameBytes);
        Utf8Text.Write(ref writer, ChiefName, ClanWire.MaxNameBytes);
        return writer.Finish();
    }

    public static ClanInvitePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1 + 4);
        ClanWire.ReadHeader(ref reader);
        return new ClanInvitePacket(reader.VarUInt(), ClanWire.ClanName(ref reader), ClanWire.Name(ref reader));
    }
}

// S->C how a request ended: {header, uint8 Action, uint8 Status (ClanStatus)}; a refusal's line comes in the chat.
public sealed record ClanResultPacket(ClanAction Action, byte Status) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanResult);
        ClanWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.Byte(Status);
        return writer.Finish();
    }

    public static ClanResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        ClanWire.ReadHeader(ref reader);
        return new ClanResultPacket((ClanAction)reader.Byte(), reader.Byte());
    }
}

// C->S the chief's new clan mark: {header, uint16 Size (ClanWire.MarkSize), Size x Size RGBA pixels}. The client fits the
// picture to the size before it sends it.
public sealed record ClanMarkPacket(byte[] Pixels) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 2 + ClanWire.MarkBytes;

    public int Write(Span<byte> destination)
    {
        if (Pixels.Length != ClanWire.MarkBytes)
        {
            throw new ArgumentException("A clan mark is ClanWire.MarkSize square RGBA pixels.", nameof(Pixels));
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanMark);
        ClanWire.Header(ref writer);
        writer.UInt16(ClanWire.MarkSize);
        writer.Bytes(Pixels);
        return writer.Finish();
    }

    public static ClanMarkPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        ClanWire.ReadHeader(ref reader);
        if (reader.UInt16() != ClanWire.MarkSize || reader.Remaining != ClanWire.MarkBytes)
        {
            throw new InvalidDataException("Clan mark has the wrong size.");
        }

        return new ClanMarkPacket(reader.Bytes(ClanWire.MarkBytes).ToArray());
    }
}

public readonly record struct ClanWarehouseLogWire(long At, string Character, byte Action, string ItemKey, string ItemName, int Quantity);

// S->C the clan warehouse's latest log lines, newest first, for a member whose rank may read them: {header, uint16 Count,
// then per line {int64 At (Unix seconds), text Character, uint8 Action (ClanWarehouseLogAction), text ItemKey, text
// ItemName, varuint Quantity}}.
public sealed record ClanWarehouseLogPacket(IReadOnlyList<ClanWarehouseLogWire> Lines) : IFramePacket
{
    private const int MaxKeyBytes = 16;
    private const int MaxItemNameBytes = NpcWire.MaxItemNameBytes;
    private const int LineSize = 8 + 2 + ClanWire.MaxNameBytes + 1 + 2 + MaxKeyBytes + 2 + MaxItemNameBytes + VarInt.MaxBytes64;
    public const int MaxSize = 8 + WireHeader.Size + 2 + Contracts.Social.ClanLimits.WarehouseLogLines * LineSize;

    public int Write(Span<byte> destination)
    {
        if (Lines.Count > Contracts.Social.ClanLimits.WarehouseLogLines)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many clan warehouse log lines.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanWarehouseLog);
        ClanWire.Header(ref writer);
        writer.UInt16((ushort)Lines.Count);
        foreach (var line in Lines)
        {
            writer.Int64(line.At);
            Utf8Text.Write(ref writer, line.Character, ClanWire.MaxNameBytes);
            writer.Byte(line.Action);
            Utf8Text.Write(ref writer, line.ItemKey, MaxKeyBytes);
            Utf8Text.Write(ref writer, line.ItemName, MaxItemNameBytes);
            writer.VarUInt((ulong)Math.Max(0, line.Quantity));
        }

        return writer.Finish();
    }

    public static ClanWarehouseLogPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 2);
        ClanWire.ReadHeader(ref reader);
        var count = reader.UInt16();
        if (count > Contracts.Social.ClanLimits.WarehouseLogLines)
        {
            throw new InvalidDataException("Clan warehouse log has too many lines.");
        }

        var lines = new ClanWarehouseLogWire[count];
        for (var index = 0; index < count; index++)
        {
            lines[index] = new ClanWarehouseLogWire(reader.Int64(), ClanWire.Name(ref reader), reader.Byte(), Utf8Text.Read(ref reader, MaxKeyBytes, "Clan message"),
                Utf8Text.Read(ref reader, MaxItemNameBytes, "Clan message"), reader.VarUInt31());
        }

        return new ClanWarehouseLogPacket(lines);
    }
}
