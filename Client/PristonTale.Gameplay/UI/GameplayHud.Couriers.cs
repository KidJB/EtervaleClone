using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Quests;

namespace PristonTale.Gameplay.UI;

// The Quest Courier window (HudCourierWindow), opened by a Courier NPC, and its hunts in the quest tracker.
public sealed partial class GameplayHud
{
    private readonly HudCourierWindow _courierWindow = new();
    private readonly HashSet<int> _couriersReady = [];
    private Protocol.Packets.CourierStatePacket _couriers = new([]);
    private Action<ulong> _askBoard;
    private int _huntsLevel = -1;

    private static readonly Dictionary<string, int> FieldCodes = new Simulation.World.WorldCatalog().AllFields
        .GroupBy(static field => field.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(static group => group.Key, static group => group.First().FieldCode,
            StringComparer.OrdinalIgnoreCase);

    // Asks a Courier's board; set by the screen.
    public Action<ulong> CourierBoardRequest
    {
        set
        {
            _courierWindow.Ask = value;
            _askBoard = value;
        }
    }

    // Accepts, abandons or turns in a Courier hunt; set by the screen.
    public Func<ulong, int, byte, Task<(byte Status, long Experience)>> CourierRequest
    {
        set => _courierWindow.Request = value;
    }

    public void ObserveCouriers(PresenceUpdate update)
    {
        if (update.CourierBoard is { NpcId: Simulation.Quests.CourierCatalog.BookNpc } book)
        {
            _questBook.RefreshHunts(book.Quests);
        }
        else if (update.CourierBoard is { } board)
        {
            _courierWindow.ObserveBoard(board);
        }

        if (update.Couriers is { } couriers)
        {
            _questBook.InvalidateHunts();
            _courierWindow.ObserveState(couriers);
            _questTracker.RefreshCouriers(couriers);
            AnnounceReadyCouriers(couriers);
            _couriers = couriers;
        }
    }

    // The Quest Book's Hunts tab asks its board while the book is open, again after a level or a hunt changes.
    private void AskQuestBookHunts(int level)
    {
        if (level != _huntsLevel)
        {
            _huntsLevel = level;
            _questBook.InvalidateHunts();
        }

        if (_questBook.Open && _questBook.HuntsWanted && _askBoard is { } ask)
        {
            _questBook.AskedHunts();
            ask(Simulation.Quests.CourierCatalog.BookNpc);
        }
    }

    // A hunt that just finished is announced once in the chat; the tracker marks it until it is turned in.
    private void AnnounceReadyCouriers(Protocol.Packets.CourierStatePacket couriers)
    {
        foreach (var quest in couriers.Quests)
        {
            var ready = quest.Standing == (byte)Simulation.Quests.CourierStanding.Ready;
            if (ready && _couriersReady.Add(quest.Id))
            {
                _chat.System(Localization.ClientText.Format("Courier.ReadyNotice", quest.Name));
            }
            else if (!ready)
            {
                _couriersReady.Remove(quest.Id);
            }
        }

        _couriersReady.RemoveWhere(id => !couriers.Quests.Any(quest => quest.Id == id));
    }

    // The mark over this field's Quest Courier: complete when a hunt of this field is ready, taken while one runs, otherwise
    // a hunt to take.
    private void MarkCourier()
    {
        var field = World.FieldId is { } id && FieldCodes.TryGetValue(id, out var code) ? code : -1;
        var mark = HudQuestBook.AvailableMark;
        foreach (var quest in _couriers.Quests)
        {
            if (quest.FieldCode == field)
            {
                mark = quest.Standing == (byte)Simulation.Quests.CourierStanding.Ready ? HudQuestBook.ReadyMark
                    : mark == HudQuestBook.ReadyMark ? mark : HudQuestBook.TakenMark;
            }
        }

        _npcQuestMarks[Simulation.Quests.CourierNpcs.Name] = mark;
    }
}
