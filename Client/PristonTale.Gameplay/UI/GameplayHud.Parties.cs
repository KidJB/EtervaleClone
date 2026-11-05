using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

// The party: its page of the party window (D or the bar's party button; it shares its place with the quest page, so one
// closes the other), the request box, the buttons under a reached player's portrait, and the members for the minimap,
// the name boxes and the chat. The window opens when a party forms and closes when it is released, as in the original.
public sealed partial class GameplayHud
{
    private readonly HudPartyWindow _partyWindow = new();
    private readonly HudRequestBox _partyRequest = new(HudPartyArt.Request, Simulation.Parties.PartyRules.InviteMs);

    // Sends the party requests; set by the screen.
    public IPartyClient Parties { get; set; }

    public PartyInfo Party { get; private set; } = PartyInfo.None;

    // The player whose portrait shows the trade, party and friend buttons (the scene reached it); 0 for none.
    public ulong ButtonsPlayerId { get; set; }

    // A button under the portrait was pressed: the row closes until the player is reached again.
    public event Action PlayerButtonsUsed;

    // The party part of each presence update. As in the original, a request is not shown while the warehouse is open.
    public void ObserveParty(PresenceUpdate update)
    {
        if (update.Party is { } party)
        {
            var formed = Party.Members.Count == 0 && party.Members.Count > 0;
            var released = Party.Members.Count > 0 && party.Members.Count == 0;
            Party = party;
            _partyWindow.Refresh(party);
            if (formed)
            {
                _questWindow.Close();
                _partyWindow.Show();
            }
            else if (released && !_partyWindow.ShowsFriends)
            {
                _partyWindow.Close();
            }
        }

        if (update.PartyRequest is { } request && !_warehouse.IsOpen)
        {
            var line = request.Recommended.Length > 0
                ? Simulation.Parties.PartyMessages.Format(Simulation.Parties.PartyMessages.Recommends, request.Recommended)
                : Simulation.Parties.PartyMessages.Request;
            _partyRequest.Show(request.FromId, request.FromName, line, _clock.Elapsed.TotalMilliseconds);
        }
    }

    public bool InParty(ulong entityId) => entityId != SelfId && Party.Contains(entityId);

    private void ToggleQuestWindow()
    {
        _partyWindow.Close();
        _questWindow.Toggle();
    }

    private void TogglePartyWindow()
    {
        _questWindow.Close();
        _partyWindow.Toggle();
    }

    private void SetUpParty()
    {
        _sprites.Request(HudPartyArt.All);
        _partyWindow.Commanded = (command, member) => Parties?.CommandPartyAsync(command, member);
        _partyWindow.Whispered = _chat.Window.WhisperTo;
        _chat.InParty = InParty;
        Combat.InParty = InParty;
        Combat.QuestMarks = _npcQuestMarks;
    }

    private bool ShowsPlayerButtons => ButtonsPlayerId != 0 && TargetId == ButtonsPlayerId;

    private bool ClickParty(HudLayout layout, Point pointer)
    {
        var from = _partyRequest.FromId;
        if (_partyRequest.Click(pointer) is { } answer)
        {
            Parties?.AnswerPartyAsync(from, answer);
            return true;
        }

        if (ShowsPlayerButtons && HudPlayerButtons.At(HudCombat.TargetFrame(layout), pointer) is { } button)
        {
            if (button == HudPlayerButton.Party)
            {
                Parties?.InviteToPartyAsync(ButtonsPlayerId);
            }
            else if (button == HudPlayerButton.Trade)
            {
                Trades?.RequestTradeAsync(ButtonsPlayerId);
            }
            else
            {
                BefriendReached();
            }

            PlayerButtonsUsed?.Invoke();
            return true;
        }

        return _partyWindow.Click(layout, pointer, Party, SelfId, _clock.Elapsed.TotalSeconds);
    }

    private bool CapturesParty(HudLayout layout, Point pointer) =>
        _partyRequest.Captures(pointer) || _partyWindow.Contains(layout, pointer)
        || ShowsPlayerButtons && HudPlayerButtons.At(HudCombat.TargetFrame(layout), pointer) is not null;

    // The other members' last known places, for the minimap.
    // The NPCs and players in view and the other party members, who are marked as members rather than as players; a
    // member's wire (x, z) becomes the render plane's (x, -z), where the minimap places the player.
    private void CollectMinimapMarkers()
    {
        Combat.CollectMapMarkers(_minimapMarkers);
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
