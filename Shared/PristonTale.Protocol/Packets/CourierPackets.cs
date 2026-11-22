using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class CourierWire
{
    public const ushort Version = 1;
    public const int MaxNameBytes = 48;
    public const int MaxDescriptionBytes = 160;
    public const int MaxMonsterBytes = 32;
    public const int MaxItemKeyBytes = 16;
    public const int MaxObjectives = 4;
    public const int MaxQuests = 19;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader, string what) => WireHeader.Read(ref reader, what, Version);

    // {text Monster (32 B), uint16 Count, uint16 Kills}
    internal const int ObjectiveMaxSize = 2 + MaxMonsterBytes + 2 + 2;

    // {int32 Id, text Name (48 B), text Description (160 B), uint8 Category, uint8 Difficulty, int32 FieldCode, uint16 MinLevel,
    // uint16 MaxLevel, int64 Experience, int64 Gold, text ItemKey (16 B), uint16 ItemCount, uint8 Standing, int32 CooldownSeconds,
    // uint8 Count, objectives x Count}
    internal const int QuestMaxSize = 4 + 2 + MaxNameBytes + 2 + MaxDescriptionBytes + 1 + 1 + 4 + 2 + 2 + 8 + 8 + 2 + MaxItemKeyBytes + 2 + 1 + 4 + 1
        + MaxObjectives * ObjectiveMaxSize;

    internal static void Write(ref PacketWriter writer, CourierWireQuest quest)
    {
        writer.Int32(quest.Id);
        Utf8Text.Write(ref writer, quest.Name, MaxNameBytes);
        Utf8Text.Write(ref writer, quest.Description, MaxDescriptionBytes);
        writer.Byte(quest.Category);
        writer.Byte(quest.Difficulty);
        writer.Int32(quest.FieldCode);
        writer.UInt16((ushort)Math.Clamp(quest.MinLevel, 0, ushort.MaxValue));
        writer.UInt16((ushort)Math.Clamp(quest.MaxLevel, 0, ushort.MaxValue));
        writer.Int64(quest.Experience);
        writer.Int64(quest.Gold);
        Utf8Text.Write(ref writer, quest.ItemKey, MaxItemKeyBytes);
        writer.UInt16((ushort)Math.Clamp(quest.ItemCount, 0, ushort.MaxValue));
        writer.Byte(quest.Standing);
        writer.Int32(quest.CooldownSeconds);
        var count = Math.Min(quest.Objectives.Count, MaxObjectives);
        writer.Byte((byte)count);
        for (var index = 0; index < count; index++)
        {
            var objective = quest.Objectives[index];
            Utf8Text.Write(ref writer, objective.Monster, MaxMonsterBytes);
            writer.UInt16((ushort)Math.Clamp(objective.Count, 0, ushort.MaxValue));
            writer.UInt16((ushort)Math.Clamp(objective.Kills, 0, ushort.MaxValue));
        }
    }

    internal static CourierWireQuest Read(ref PacketReader reader, string what)
    {
        var id = reader.Int32();
        var name = Utf8Text.Read(ref reader, MaxNameBytes, what);
        var description = Utf8Text.Read(ref reader, MaxDescriptionBytes, what);
        var category = reader.Byte();
        var difficulty = reader.Byte();
        var field = reader.Int32();
        var minLevel = reader.UInt16();
        var maxLevel = reader.UInt16();
        var experience = reader.Int64();
        var gold = reader.Int64();
        var itemKey = Utf8Text.Read(ref reader, MaxItemKeyBytes, what);
        var itemCount = reader.UInt16();
        var standing = reader.Byte();
        var cooldown = reader.Int32();
        var count = reader.Byte();
        if (count > MaxObjectives)
        {
            throw new InvalidDataException($"{what} has too many objectives.");
        }

        var objectives = new CourierWireObjective[count];
        for (var index = 0; index < count; index++)
        {
            objectives[index] = new CourierWireObjective(Utf8Text.Read(ref reader, MaxMonsterBytes, what), reader.UInt16(), reader.UInt16());
        }

        return new CourierWireQuest(id, name, description, category, difficulty, field, minLevel, maxLevel, experience, gold, itemKey, itemCount, standing,
            cooldown, objectives);
    }
}

public readonly record struct CourierWireObjective(string Monster, int Count, int Kills);

// A Courier hunt as a player sees it: its definition, how it stands for the player (Quests.CourierStanding), the seconds a
// repeatable quest still waits, and each objective's kills.
public sealed record CourierWireQuest(int Id, string Name, string Description, byte Category, byte Difficulty, int FieldCode, int MinLevel, int MaxLevel,
    long Experience, long Gold, string ItemKey, int ItemCount, byte Standing, int CooldownSeconds, IReadOnlyList<CourierWireObjective> Objectives);

// C->S {header, uint64 NpcId}: asks a Courier in reach for its board; answered by CourierBoard.
public readonly record struct CourierBoardRequestPacket(ulong NpcId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CourierBoardRequest);
        CourierWire.Header(ref writer);
        writer.UInt64(NpcId);
        return writer.Finish();
    }

    public static CourierBoardRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        CourierWire.ReadHeader(ref reader, "Courier board request");
        return new CourierBoardRequestPacket(reader.UInt64());
    }
}

// S->C {header, uint64 NpcId (0: the Courier is out of reach), text Name (48 B), uint8 Count (at most CourierWire.MaxQuests),
// quests x Count}: the Courier's hunts offered at the player's level and those the player accepted from it.
public sealed record CourierBoardPacket(ulong NpcId, string Name, IReadOnlyList<CourierWireQuest> Quests) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 8 + 2 + CourierWire.MaxNameBytes + 1 + CourierWire.MaxQuests * CourierWire.QuestMaxSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CourierBoard);
        CourierWire.Header(ref writer);
        writer.UInt64(NpcId);
        Utf8Text.Write(ref writer, Name, CourierWire.MaxNameBytes);
        var count = Math.Min(Quests.Count, CourierWire.MaxQuests);
        writer.Byte((byte)count);
        for (var index = 0; index < count; index++)
        {
            CourierWire.Write(ref writer, Quests[index]);
        }

        return writer.Finish();
    }

    public static CourierBoardPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 8 + 2 + 1);
        CourierWire.ReadHeader(ref reader, "Courier board");
        var npc = reader.UInt64();
        var name = Utf8Text.Read(ref reader, CourierWire.MaxNameBytes, "Courier board");
        var count = reader.Byte();
        if (count > CourierWire.MaxQuests)
        {
            throw new InvalidDataException("Courier board has too many quests.");
        }

        var quests = new CourierWireQuest[count];
        for (var index = 0; index < count; index++)
        {
            quests[index] = CourierWire.Read(ref reader, "Courier board");
        }

        return new CourierBoardPacket(npc, name, quests);
    }
}

// C->S {header, Guid OperationId, uint64 NpcId, int32 QuestId, uint8 Action (1 accept, 2 abandon, 3 turn in), uint8 0,
// uint16 0}; answered by CourierResult.
public sealed record CourierRequestPacket(Guid OperationId, ulong NpcId, int QuestId, byte Action) : IFramePacket
{
    public const byte Accept = 1;
    public const byte Abandon = 2;
    public const byte TurnIn = 3;
    public const int Size = 8 + WireHeader.Size + 16 + 8 + 4 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CourierRequest);
        CourierWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Int32(QuestId);
        writer.Byte(Action);
        writer.Zero(3);
        return writer.Finish();
    }

    public static CourierRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        CourierWire.ReadHeader(ref reader, "Courier request");
        var operation = reader.Guid();
        var npc = reader.UInt64();
        var quest = reader.Int32();
        var action = reader.Byte();
        reader.Skip(3);
        return new CourierRequestPacket(operation, npc, quest, action);
    }
}

// S->C {header, Guid OperationId, uint8 Status (Quests.CourierStatus), uint8 0, uint16 0, int64 Experience}: the result,
// with the experience a turn-in gave.
public sealed record CourierResultPacket(Guid OperationId, byte Status, long Experience) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CourierResult);
        CourierWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Zero(3);
        writer.Int64(Experience);
        return writer.Finish();
    }

    public static CourierResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        CourierWire.ReadHeader(ref reader, "Courier result");
        var operation = reader.Guid();
        var status = reader.Byte();
        reader.Skip(3);
        return new CourierResultPacket(operation, status, reader.Int64());
    }
}

// S->C {header, uint8 Count, quests x Count}: the receiver's accepted Courier hunts (at most Quests.CourierCatalog.MaxActive),
// when it enters the world and whenever they change; the descriptions are empty.
public sealed record CourierStatePacket(IReadOnlyList<CourierWireQuest> Quests) : IFramePacket
{
    public const int MaxQuests = 8;
    public const int MaxSize = 8 + WireHeader.Size + 1 + MaxQuests * CourierWire.QuestMaxSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CourierState);
        CourierWire.Header(ref writer);
        var count = Math.Min(Quests.Count, MaxQuests);
        writer.Byte((byte)count);
        for (var index = 0; index < count; index++)
        {
            CourierWire.Write(ref writer, Quests[index]);
        }

        return writer.Finish();
    }

    public static CourierStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        CourierWire.ReadHeader(ref reader, "Courier state");
        var count = reader.Byte();
        if (count > MaxQuests)
        {
            throw new InvalidDataException("Courier state has too many quests.");
        }

        var quests = new CourierWireQuest[count];
        for (var index = 0; index < count; index++)
        {
            quests[index] = CourierWire.Read(ref reader, "Courier state");
        }

        return new CourierStatePacket(quests);
    }
}
