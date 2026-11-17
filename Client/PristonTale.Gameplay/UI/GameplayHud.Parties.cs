using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

// The party: its page of the party window (D or the bar's party button; it shares its place with the quest page, so one
// closes the other), the request box, the buttons under a reached player's portrait, and the members for the minimap,
// the name boxes and the chat. The window opens when a party forms and closes when it is released, as in the original.
public sealed partial class GameplayHud
{
    private readonly HudPartyWindow _partyWindow = new();
    private readonly HudPartyFrames _partyFrames = new();
    private readonly HudRequestBox _partyRequest = new(Simulation.Parties.PartyRules.InviteMs);

    // Sends the party requests; set by the screen.
    public IPartyClient Parties { get; set; }

    public PartyInfo Party { get; private set; } = PartyInfo.None;

    // The player whose portrait shows the trade, party and friend buttons (the scene reached it); 0 for none.

    // A button under the portrait was pressed: the row closes until the player is reached again.

    // The party part of each presence update. As in the original, a request is not shown while the warehouse is open.
    public void ObserveParty(PresenceUpdate update)
    {
        if (update.Party is { } party)
        {
            Party = party;
            _partyWindow.Refresh(party);
        }

        if (update.PartyRequest is { } request && !_warehouse.IsOpen)
        {
            var title = ClientText.Get(request.Raid ? "Request.RaidTitle" : "Request.PartyTitle");
            var question = request.Raid ? ClientText.Format("Request.Raid", request.FromName)
                : request.Recommended.Length > 0 ? ClientText.Format("Request.Recommended", request.FromName, request.Recommended)
                : ClientText.Format("Request.Party", request.FromName);
            _partyRequest.Show(request.FromId, request.FromName, title, question, _clock.Elapsed.TotalMilliseconds);
        }
    }

    public bool InParty(ulong entityId) => entityId != SelfId && Party.Contains(entityId);

    private void ToggleQuestWindow()
    {
        _partyWindow.Close();
        _questWindow.Toggle();
    }

    private void ToggleSocial()
    {
        _questWindow.Close();
        _social.Toggle();
    }

    private void SetUpParty()
    {
        _sprites.Request(HudPartyArt.All);
        _sprites.Request(HudPartyFrames.Art);
        _sprites.Request(HudTargetMenu.Art);
        _partyFrames.Commanded = (command, member) => Parties?.CommandPartyAsync(command, member);
        _partyWindow.Commanded = (command, member) => Parties?.CommandPartyAsync(command, member);
        _partyWindow.Whispered = _chat.Window.WhisperTo;
        _chat.InParty = InParty;
        Combat.InParty = InParty;
        Combat.QuestMarks = _npcQuestMarks;
    }

    private readonly HudTargetMenu _targetMenu = new();

    // The targeted player whose frame carries the menu; 0 when the target is not another player.
    private ulong MenuPlayerId => TargetId != 0 && TargetId != SelfId && Combat.KindOf(TargetId, out _) == Contracts.World.EntityKind.Player ? TargetId : 0;

    private bool ClassicFrame => _context.Presentation.Requested.ClassicTargetFrame;

    private HudTargetFrame MenuFrame(HudLayout layout) => HudCombat.TargetFrame(layout, ClassicFrame);

    // A party leader may ask a raid of a player outside its party.
    private bool OffersRaid(ulong player) => Party.Members.Count > 0 && Party.Members[0].EntityId == SelfId && !Party.Members.Any(member => member.EntityId == player);

    private bool ClickParty(HudLayout layout, Point pointer)
    {
        if (_partyFrames.Click(layout, Party, pointer) || _social.Click(layout, pointer, _clock.Elapsed.TotalSeconds) || _tuning.Contains(layout, pointer))
        {
            return true;
        }

        var from = _partyRequest.FromId;
        if (_partyRequest.Click(layout, pointer) is { } answer)
        {
            Parties?.AnswerPartyAsync(from, answer);
            return true;
        }

        var player = MenuPlayerId;
        if (player != 0 && _targetMenu.Contains(MenuFrame(layout), ClassicFrame, pointer))
        {
            switch (_targetMenu.Click(MenuFrame(layout), ClassicFrame, pointer))
            {
                case HudTargetAction.Trade:
                    Trades?.RequestTradeAsync(player);
                    break;
                case HudTargetAction.Friend:
                    Befriend(player);
                    break;
                case HudTargetAction.Party:
                    Parties?.InviteToPartyAsync(player);
                    break;
                case HudTargetAction.Raid when Combat.PlayerName(player) is { } name:
                    _ = _chat.Send?.Invoke("//raid " + name, false, Guid.Empty);
                    break;
                case HudTargetAction.Clan:
                    InviteToClan(player);
                    break;
            }

            return true;
        }

        _targetMenu.Close();

        return _partyWindow.Click(layout, pointer, Party, SelfId, _clock.Elapsed.TotalSeconds);
    }

    private bool CapturesParty(HudLayout layout, Point pointer) =>
        _partyRequest.Captures(layout, pointer) || _partyWindow.Contains(layout, pointer) || _partyFrames.Contains(layout, Party, pointer) || _social.Contains(layout, pointer) || _tuning.Contains(layout, pointer)
        || MenuPlayerId != 0 && _targetMenu.Contains(MenuFrame(layout), ClassicFrame, pointer);

    // The other members' last known places, for the minimap.
    // The NPCs and players in view and the other party members, who are marked as members rather than as players; a
    // member's wire (x, z) becomes the render plane's (x, -z), where the minimap places the player.
    private void CollectMinimapMarkers()
    {
        Combat.CollectMapMarkers(_minimapMarkers);
        // Spawn points first, so every other marker draws over them; like the bosses they come in wire (x, z).
        for (var index = 0; index < SpawnSpots.Count; index++)
        {
            _minimapMarkers.Insert(index, new HudMapMarker((ulong)index, new Vector3(SpawnSpots[index].X, 0f, -SpawnSpots[index].Z), HudMapMarkerKind.Spawn));
        }

        // A boss marker's id is its index in BossSpots; its spot comes in wire (x, z), the render plane's (x, -z).
        for (var index = 0; index < BossSpots.Count; index++)
        {
            var spot = BossSpots[index];
            _minimapMarkers.Add(new HudMapMarker((ulong)index, new Vector3(spot.X, 0f, -spot.Z),
                spot.Alive ? HudMapMarkerKind.BossAlive : HudMapMarkerKind.BossWaiting));
        }

        var members = Party.Members;
        if (members.Count == 0)
        {
            return;
        }

        for (var index = _minimapMarkers.Count - 1; index >= 0; index--)
        {
            if (_minimapMarkers[index].Kind == HudMapMarkerKind.Player && Party.Contains(_minimapMarkers[index].Id))
            {
                _minimapMarkers.RemoveAt(index);
            }
        }

        for (var index = 0; index < members.Count; index++)
        {
            if (members[index].EntityId != SelfId)
            {
                _minimapMarkers.Add(new HudMapMarker(members[index].EntityId, new Vector3(members[index].Position.X, 0f, -members[index].Position.Y),
                    HudMapMarkerKind.Party));
            }
        }
    }

    // The name of the minimap marker under the pointer, after the windows that may lie over the map.
    private void DrawMinimapName(HudLayout layout, Point pointer)
    {
        if (!MinimapShown || _minimapMarkers.Count == 0)
        {
            return;
        }

        var index = _minimap.MarkerAt(layout, World, _minimapMarkers, pointer);
        if (index < 0)
        {
            return;
        }

        var marker = _minimapMarkers[index];
        var name = marker.Kind switch
        {
            HudMapMarkerKind.Party => PartyMemberName(marker.Id),
            HudMapMarkerKind.BossAlive or HudMapMarkerKind.BossWaiting => BossSpots[(int)marker.Id].BossName,
            _ => Combat.EntityName(marker.Id)
        };
        HudMinimap.DrawName(_painter, layout, name, marker.Kind, pointer);
    }

    private string PartyMemberName(ulong id)
    {
        foreach (var member in Party.Members)
        {
            if (member.EntityId == id)
            {
                return member.Name;
            }
        }

        return "";
    }
}
