using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C {header, uint16 Version = 2, uint16 0, uint64 Skins, uint8 Out, uint8 0, uint16 0, text Name, uint64 Mounts, uint8
// MountChosen, uint8 Riding (0 or 1)}: the pet skins the receiver's account unlocked (a bit per skin id, PetSkins), the
// one its character has out (0 none) and the name it gave its pet, then the account's mounts (a bit per Mounts id), the
// one picked (0 none) and whether the character rides it, then {uint8 Caravan (Caravans id, 0 none), int64 CaravanEnds (Unix
// seconds), text CaravanName} from version 3 and uint8 CaravanStays (0 follows, 1 stays) from version 4; sent when it enters
// the world and whenever one of them changes. Text is UTF-8
// with a uint16 byte length; readers refuse version 1, which had no mounts.
public readonly record struct PetCollectionPacket(ulong Skins, byte Out) : IFramePacket
{
    public const ushort CurrentVersion = 4;
    public const int FixedSize = 8 + WireHeader.Size + 8 + 4 + 2 + 8 + 2;
    public const int MaxSize = FixedSize + Contracts.Characters.PetNameLimits.MaxBytes + 1 + 8 + 2 + Contracts.Characters.PetNameLimits.MaxBytes + 1;

    public string Name { get; init; } = "";
    public ulong Mounts { get; init; }
    public byte MountChosen { get; init; }
    public bool Riding { get; init; }
    public byte Caravan { get; init; }
    public long CaravanEnds { get; init; }
    public string CaravanName { get; init; } = "";
    public bool CaravanStays { get; init; }

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PetCollection);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt64(Skins);
        writer.Byte(Out);
        writer.Byte(0);
        writer.UInt16(0);
        Utf8Text.Write(ref writer, Name, Contracts.Characters.PetNameLimits.MaxBytes);
        writer.UInt64(Mounts);
        writer.Byte(MountChosen);
        writer.Byte(Riding ? (byte)1 : (byte)0);
        writer.Byte(Caravan);
        writer.Int64(CaravanEnds);
        Utf8Text.Write(ref writer, CaravanName, Contracts.Characters.PetNameLimits.MaxBytes);
        writer.Byte(CaravanStays ? (byte)1 : (byte)0);
        return writer.Finish();
    }

    public static PetCollectionPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, FixedSize);
        var version = WireHeader.Read(ref reader, "Pet collection", 2);
        var skins = reader.UInt64();
        var petOut = reader.Byte();
        reader.Skip(3);
        var name = Utf8Text.Read(ref reader, Contracts.Characters.PetNameLimits.MaxBytes, "Pet collection");
        var mounts = reader.UInt64();
        var chosen = reader.Byte();
        var riding = reader.Byte() switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Pet collection riding flag is neither 0 nor 1.")
        };
        var caravan = version >= 3 ? reader.Byte() : (byte)0;
        var ends = version >= 3 ? reader.Int64() : 0L;
        var caravanName = version >= 3 ? Utf8Text.Read(ref reader, Contracts.Characters.PetNameLimits.MaxBytes, "Pet collection") : "";
        var caravanStays = version >= 4 && reader.Byte() != 0;
        return new PetCollectionPacket(skins, petOut)
        {
            Name = name, Mounts = mounts, MountChosen = chosen, Riding = riding, Caravan = caravan, CaravanEnds = ends, CaravanName = caravanName,
            CaravanStays = caravanStays
        };
    }
}
