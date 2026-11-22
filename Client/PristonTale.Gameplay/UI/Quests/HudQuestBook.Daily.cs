using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;

// The Daily section: every daily quest of the player's level (DailyQuests) with its state of the day from the server
// (DailyQuestsInfo); the page shows the hunt with the kills so far, the rewards and, while the player talks to the daily
// quest giver (DailyNpc), the button that accepts or claims it.
public sealed partial class HudQuestBook
{
    private readonly List<DailyQuest> _dailies = [];
    private DailyQuestsInfo _dailyInfo = DailyQuestsInfo.None;
    private int _dailyLevel = -1;

    // The daily quest giver the player is talking to (its entity), 0 when none: only then do Accept and Claim work.
    public ulong DailyNpc { get; set; }

    // An accept or claim to send, set by a click and taken by the HUD: the giver, the quest and the action (1 accept, 2 claim).
    public (ulong Npc, int Quest, byte Action)? DailyRequest { get; private set; }

    public (ulong Npc, int Quest, byte Action)? TakeDailyRequest()
    {
        var request = DailyRequest;
        DailyRequest = null;
        return request;
    }

    private static Rectangle DailyButton(Rectangle pane) => new(pane.X + (pane.Width - 160) / 2, pane.Bottom - 40, 160, 24);

    // Opens the book on the Daily section at the giver.
    public void ShowDaily(ulong npcEntityId)
    {
        DailyNpc = npcEntityId;
        ShowSection("daily");
    }

    public void RefreshDaily(DailyQuestsInfo info, int level)
    {
        if (ReferenceEquals(info, _dailyInfo) && level == _dailyLevel)
        {
            return;
        }

        (_dailyInfo, _dailyLevel) = (info, level);
        _dailies.Clear();
        foreach (var quest in DailyQuests.All)
        {
            if (DailyQuests.Offered(quest, level) || info.Accepted.ContainsKey(quest.Id) || info.Claimed.Contains(quest.Id))
            {
                _dailies.Add(quest);
            }
        }

        _detailFor = null;
        Rebuild();
    }

    private QuestBookStatus DailyStatus(DailyQuest quest) =>
        _dailyInfo.Claimed.Contains(quest.Id) ? QuestBookStatus.Done
        : _dailyInfo.Accepted.ContainsKey(quest.Id) ? QuestBookStatus.Running
        : QuestBookStatus.Available;

    private void ClickDaily(Rectangle pane, Point pointer)
    {
        if (DailyNpc == 0 || _selected.Index >= _dailies.Count || !DailyButton(pane).Contains(pointer))
        {
            return;
        }

        var quest = _dailies[_selected.Index];
        var status = DailyStatus(quest);
        if (status == QuestBookStatus.Available)
        {
            DailyRequest = (DailyNpc, quest.Id, 1);
        }
        else if (status == QuestBookStatus.Running && _dailyInfo.Accepted[quest.Id] >= quest.Kills)
        {
            DailyRequest = (DailyNpc, quest.Id, 2);
        }
    }

    private void DrawDaily(HudPainter painter, Rectangle pane, Point pointer)
    {
        var quest = _dailies[_selected.Index];
        var status = DailyStatus(quest);
        var kills = _dailyInfo.Accepted.GetValueOrDefault(quest.Id);
        if (!ReferenceEquals(quest, _detailFor) || kills != _detailKills)
        {
            (_detailFor, _detailKills) = (quest, kills);
            DescribeDaily(painter, pane, quest, status, kills);
        }

        DrawPage(painter, pane, quest.Name, status == QuestBookStatus.Done ? QuestBookStatus.Done : status);
        var button = DailyButton(pane);
        var label = status switch
        {
            QuestBookStatus.Available => "Accept",
            QuestBookStatus.Running when kills >= quest.Kills => "Claim reward",
            _ => null
        };
        if (DailyNpc == 0)
        {
            if (status != QuestBookStatus.Done)
            {
                painter.TextCentered("Accept and claim at " + DailyQuests.GiverName, pane.X, pane.Right, button.Y + 4, Muted);
            }
        }
        else if (label is not null)
        {
            painter.Fill(button, FrameLight);
            painter.Fill(new Rectangle(button.X + 1, button.Y + 1, button.Width - 2, button.Height - 2), button.Contains(pointer) ? RowChosen : Plate);
            painter.TextCentered(label, button.X, button.Right, button.Y + 4, Heading);
        }
    }

    // The level line, the task with the kills so far and the rewards, made again only when another quest or count shows.
    private void DescribeDaily(HudPainter painter, Rectangle pane, DailyQuest quest, QuestBookStatus status, int kills)
    {
        var width = pane.Width - 70;
        _levelLine = string.Create(CultureInfo.InvariantCulture, $"Daily, level {quest.MinLevel}{(quest.MaxLevel >= 999 ? "+" : $"-{quest.MaxLevel}")}");
        _description.Clear();
        AddLines(painter, width, LineMark.Talk, "Talk to " + DailyQuests.GiverName + " in Ricarten", GiverLines, strong: true);
        if (quest.Story.Length > 0)
        {
            AddLines(painter, width, LineMark.Talk, quest.Story, 2);
        }

        var count = status == QuestBookStatus.Running ? Math.Min(kills, quest.Kills) : 0;
        AddLines(painter, width, LineMark.Target, string.Create(CultureInfo.InvariantCulture,
            $"Monsters of level {quest.MonsterLevel}+ ({count}/{quest.Kills})"), 2);
        _rewards.Clear();
        _rewards.Add(string.Create(CultureInfo.InvariantCulture, $"{quest.ExpPercent}% of your level's XP"));
        _rewards.Add(string.Create(CultureInfo.InvariantCulture, $"{quest.Gold:N0} GP"));
        _rewards.Add(string.Create(CultureInfo.InvariantCulture, $"{quest.Coins} coin{(quest.Coins == 1 ? "" : "s")}"));
        _rewards.Add(string.Create(CultureInfo.InvariantCulture, $"{quest.ItemCount} x {quest.ItemKey}"));
    }
}
