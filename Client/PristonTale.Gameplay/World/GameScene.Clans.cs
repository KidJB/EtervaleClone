using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.World;

// Clan requests through the world connection; the clan's state and results come back as presence updates.
public sealed partial class GameScene
{
    public void SendClan(ClanAction action, ulong id, string name) =>
        SendSocial(_playChannel?.SendClanAsync(action, id, name, _presenceCts.Token), "clan request");

    // A gift of gold to the clan treasury at the Clan Warehouse keeper.
    public void DonateToClan(ulong keeperId, long amount) =>
        SendSocial(_playChannel?.SendClanAsync(ClanAction.Donate, keeperId, "", _presenceCts.Token, (ulong)Math.Max(0, amount)), "clan donation");

    // A gift of coins to the clan coin treasury at the Clan Warehouse keeper.
    public void DonateCoinsToClan(ulong keeperId, long amount) =>
        SendSocial(_playChannel?.SendClanAsync(ClanAction.DonateCoins, keeperId, "", _presenceCts.Token, (ulong)Math.Max(0, amount)), "clan coin donation");

    public void SendClanMark(byte[] pixels) => SendSocial(_playChannel?.SendClanMarkAsync(pixels, _presenceCts.Token), "clan mark");
}
