using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C {header, uint16 Version = 1, uint16 0, uint64 Skins, uint8 Out, uint8 0, uint16 0, text Name}: the pet skins the
// receiver's account unlocked (a bit per skin id, PetSkins), the one its character has out (0 none) and the name it gave
// its pet, when it enters the world and whenever one of them changes. Text is UTF-8 with a uint16 byte length.
public readonly record struct PetCollectionPacket(ulong Skins, byte Out) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int FixedSize = 8 + WireHeader.Size + 8 + 4 + 2;

    public string Name { get; init; } = "";

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PetCollection);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt64(Skins);
        writer.Byte(Out);
        writer.Byte(0);
        writer.UInt16(0);
        Utf8Text.Write(ref writer, Name, Contracts.Characters.PetNameLimits.MaxBytes);
        return writer.Finish();
    }

    public static PetCollectionPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, FixedSize);
        WireHeader.Read(ref reader, "Pet collection");
        var skins = reader.UInt64();
        var petOut = reader.Byte();
        reader.Skip(3);
        return new PetCollectionPacket(skins, petOut) { Name = Utf8Text.Read(ref reader, Contracts.Characters.PetNameLimits.MaxBytes, "Pet collection") };
    }
}
