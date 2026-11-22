using System.Globalization;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Quests;
using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Quests;

// The hunting fields' sections: the Quest Couriers' hunts of the best fields at the player's level and the accepted ones
// (the server's Quest Book board, CourierCatalog.BookQuests), one section per field (map), the highest levels first; the
// page shows the chosen hunt's Courier, each objective with its kills, where to hand it in and its rewards. Hunts are taken
// and turned in at the field's Courier.
public sealed partial class HudQuestBook
{
    private readonly List<CourierWireQuest> _hunts = [];
    private readonly List<int> _huntFields = [];
    private IReadOnlyList<CourierWireQuest> _huntsFrom;

    // Asks the server for the Quest Book's hunts again (the board for CourierCatalog.BookNpc).
    public bool HuntsWanted { get; private set; } = true;

    public void AskedHunts() => HuntsWanted = false;

    // The hunts change with the level or the accepted hunts: the HUD asks again then.
    public void InvalidateHunts() => HuntsWanted = true;

    // The fields in the order the book shows them: the highest hunt levels first, as the server chose them.
    public static List<int> HuntFields(IReadOnlyList<CourierWireQuest> hunts) =>
        [.. hunts.GroupBy(static hunt => hunt.FieldCode).OrderByDescending(static group => group.Max(static hunt => hunt.MinLevel)).ThenBy(static group => group.Key)
            .Select(static group => group.Key)];

    public void RefreshHunts(IReadOnlyList<CourierWireQuest> hunts)
    {
        if (ReferenceEquals(hunts, _huntsFrom))
        {
            return;
        }

        _huntsFrom = hunts;
        var selectedId = _selected.Kind == RowKind.Hunt && _selected.Index < _hunts.Count ? _hunts[_selected.Index].Id : -1;
        _hunts.Clear();
        _hunts.AddRange(hunts);
        _huntFields.Clear();
        _huntFields.AddRange(HuntFields(_hunts));
        if (selectedId >= 0)
        {
            _selected = (RowKind.Hunt, _hunts.FindIndex(hunt => hunt.Id == selectedId));
        }

        _detailFor = null;
        Rebuild();
    }

    public static QuestBookStatus HuntStatus(CourierWireQuest hunt) => (CourierStanding)hunt.Standing switch
    {
        CourierStanding.Available => QuestBookStatus.Available,
        CourierStanding.Active or CourierStanding.Ready => QuestBookStatus.Running,
        CourierStanding.Completed => QuestBookStatus.Done,
        _ => QuestBookStatus.Locked
    };

    private void DrawHunt(HudPainter painter, Rectangle pane)
    {
        var hunt = _hunts[_selected.Index];
        if (!ReferenceEquals(hunt, _detailFor))
        {
            _detailFor = hunt;
            DescribeHunt(painter, pane, hunt);
        }

        DrawPage(painter, pane, hunt.Name, HuntStatus(hunt));
    }

    // The level line, the Courier to talk to, each objective with its kills and the rewards, made again when another hunt
    // or another board shows.
    private void DescribeHunt(HudPainter painter, Rectangle pane, CourierWireQuest hunt)
    {
        var width = pane.Width - 70;
        var place = HudCourierWindow.PlaceName(hunt.FieldCode);
        _levelLine = string.Create(CultureInfo.InvariantCulture, $"Level {hunt.MinLevel}-{hunt.MaxLevel}");
        _description.Clear();
        AddLines(painter, width, LineMark.Talk, ClientText.Format("QuestBook.HuntGiver", CourierNpcs.Name, place), GiverLines, strong: true);
        if (hunt.Description.Length > 0)
        {
            AddLines(painter, width, LineMark.Talk, hunt.Description, 2);
        }

        foreach (var objective in hunt.Objectives)
        {
            _description.Add((LineMark.Target, string.Create(CultureInfo.InvariantCulture,
                $"{objective.Monster} ({Math.Min(objective.Kills, objective.Count)}/{objective.Count})"), false));
        }

        AddLines(painter, width, LineMark.Talk, (CourierStanding)hunt.Standing == CourierStanding.Ready
            ? $"Ready: hand it in to the {CourierNpcs.Name} in {place}"
            : $"Deliver the quest to the {CourierNpcs.Name} in {place}", GiverLines);
        _rewards.Clear();
        _rewards.Add(hunt.Experience.ToString("N0", CultureInfo.CurrentCulture) + " XP");
        if (hunt.Gold > 0)
        {
            _rewards.Add(hunt.Gold.ToString("N0", CultureInfo.CurrentCulture) + " GP");
        }

        if (hunt.ItemCount > 0 && hunt.ItemKey.Length > 0)
        {
            _rewards.Add(string.Create(CultureInfo.InvariantCulture, $"{hunt.ItemCount} x {hunt.ItemKey}"));
        }
    }
}
