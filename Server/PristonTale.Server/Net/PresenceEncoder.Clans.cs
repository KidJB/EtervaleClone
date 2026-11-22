using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Server.Net;

// What the receiver's clan owns: its running buffs whenever they change (the client counts down by itself), and the clan
// warehouse while the receiver has it open, whenever another change gives it a new view.
internal sealed partial class PresenceEncoder
{
    private WorldBuffEnd[]? _clanBuffs;
    private WarehouseView? _clanWarehouse;

    private void SendClanBuffs(in WorldSnapshot snapshot, IPacketSink connection)
    {
        if (ReferenceEquals(snapshot.ClanBuffs, _clanBuffs))
        {
            return;
        }

        _clanBuffs = snapshot.ClanBuffs;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var buffs = new (int Kind, int SecondsLeft)[_clanBuffs.Length];
        for (var index = 0; index < buffs.Length; index++)
        {
            buffs[index] = (_clanBuffs[index].Kind, (int)Math.Max(0, _clanBuffs[index].EndSeconds - now));
        }

        connection.Send(_scratch.AsSpan(0, new WorldBuffPacket(buffs, Clan: true).Write(_scratch)));
    }

    private void SendClanWarehouse(in WorldSnapshot snapshot, IPacketSink connection)
    {
        var view = snapshot.ClanWarehouse;
        if (ReferenceEquals(view, _clanWarehouse))
        {
            return;
        }

        _clanWarehouse = view;
        if (view is null)
        {
            return;
        }

        foreach (var part in NpcWireMapping.WarehouseParts(Guid.Empty, 0, view, EtervaleOpcodes.ClanWarehouseState))
        {
            connection.Send(_scratch.AsSpan(0, part.Write(_scratch)));
        }
    }
}
