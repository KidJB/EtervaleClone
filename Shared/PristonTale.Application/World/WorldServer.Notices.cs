using System.Globalization;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

// The lines a change on the command phase answers (parties, trades), taken by the call that made it.
public sealed partial class WorldServer
{
    private readonly List<WorldNotice> _notices = new();

    private void Notify(ulong to, ChatKind kind, GameText text) => _notices.Add(new WorldNotice(to, kind, text));

    // A reply to the player who acted.
    private void Reply(WorldPlayer player, GameText text) => Notify(player.EntityId, ChatKind.Server, text);

    // A request that answers only lines: the action runs for a player still playing.
    private WorldNotice[] NoticeAction(PlayerSession session, Action<Connection> action)
    {
        if (TryGetPlaying(session, out var connection))
        {
            action(connection);
        }

        return TakeNotices();
    }

    // The tick's caller, after Tick: the lines no request took (a change of place ends a trade).
    public WorldNotice[] TakeTickNotices()
    {
        NoticeExperienceGains();
        return TakeNotices();
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private void NoticeExperienceGains()
    {
        foreach (var field in _fields.Values)
        {
            var gains = field.Combat.ExperienceGains;
            for (var index = 0; index < gains.Count; index++)
            {
                var gain = gains[index];
                var amount = gain.Amount.ToString("N0", CultureInfo.InvariantCulture);
                Notify(gain.PlayerId, ChatKind.Assist, gain.PartySize == 0
                    ? GameText.Of(GameTextId.ExperienceGained, amount)
                    : GameText.Of(GameTextId.PartyExperienceGained, amount, Number(gain.PartyPercent), Number(gain.PartySize)));
            }

            gains.Clear();
        }
    }

    // The Notice lines of a stored pickup. paid: the party members given a part of its gold. A party hears who took an item.
    private void NoticePickup(WorldPlayer picker, PreparedPickupOperation pickup, List<GoldShare> paid)
    {
        if (paid.Count > 0)
        {
            var line = GameText.Of(GameTextId.PartyGoldGained, Number(paid.Count + 1), Number(paid[0].Gold));
            Notify(picker.EntityId, ChatKind.Assist, line);
            foreach (var share in paid)
            {
                Notify(share.MemberId, ChatKind.Assist, line);
            }
        }
        else if (pickup.Gold > 0)
        {
            Notify(picker.EntityId, ChatKind.Assist, GameText.Of(GameTextId.GoldGained, Number(pickup.Gold)));
        }

        if (pickup.ItemName.Length == 0)
        {
            return;
        }

        if (picker.Party is not { } party)
        {
            Notify(picker.EntityId, ChatKind.Assist, GameText.Of(GameTextId.ItemGained, pickup.ItemName));
            return;
        }

        var taken = GameText.Of(GameTextId.MemberItemGained, picker.Character.Name, pickup.ItemName);
        foreach (var id in party.Members)
        {
            Notify(id, ChatKind.Assist, taken);
        }
    }

    private WorldNotice[] TakeNotices()
    {
        if (_notices.Count == 0)
        {
            return [];
        }

        var notices = _notices.ToArray();
        _notices.Clear();
        return notices;
    }
}
