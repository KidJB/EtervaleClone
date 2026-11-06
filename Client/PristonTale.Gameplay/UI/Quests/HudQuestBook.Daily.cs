using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;

// The Daily tab: every daily quest of the player's level (DailyQuests) with its state of the day from the server
// (DailyQuestsInfo); the right page shows the hunt with the kills so far, the rewards and, while the player talks to the
// daily quest giver (DailyNpc), the button that accepts or claims it.
public sealed partial class HudQuestBook
{
    private static readonly Rectangle DailyButton = new(404, 392, 140, 22);
    private readonly List<DailyQuest> _dailies = [];
    private readonly List<string> _dailyTask = [];
    private readonly List<string> _dailyRewards = [];
    private DailyQuestsInfo _dailyInfo = DailyQuestsInfo.None;
    private int _dailyLevel = -1;
    private DailyQuest _dailyDetailFor;
    private int _dailyKillsShown = -1;
    private string _dailyLevelLine = "";
    private int _dailySelected;

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

    // Opens the book on the Daily tab at the giver.
    public void ShowDaily(ulong npcEntityId)
    {
        Open = true;
        _mode = BookMode.Daily;
        DailyNpc = npcEntityId;
        _scroll = 0;
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

        _dailySelected = Math.Clamp(_dailySelected, 0, Math.Max(0, _dailies.Count - 1));
        _dailyKillsShown = -1;
    }

    private QuestBookStatus DailyStatus(DailyQuest quest) =>
        _dailyInfo.Claimed.Contains(quest.Id) ? QuestBookStatus.Done
        : _dailyInfo.Accepted.ContainsKey(quest.Id) ? QuestBookStatus.Running
        : QuestBookStatus.Available;

    private bool ClickDaily(Rectangle window, Point pointer)
    {
        for (var row = 0; row < VisibleRows && _scroll + row < _dailies.Count; row++)
        {
            if (Row(window, row).Contains(pointer))
            {
                _dailySelected = _scroll + row;
                return true;
            }
        }

        if (DailyNpc != 0 && _dailySelected < _dailies.Count && At(window, DailyButton).Contains(pointer))
        {
            var quest = _dailies[_dailySelected];
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

        return true;
    }

    private void DrawDaily(HudPainter painter, Rectangle window, Point pointer)
    {
        for (var row = 0; row < VisibleRows && _scroll + row < _dailies.Count; row++)
        {
            var index = _scroll + row;
            var bounds = Row(window, row);
            if (index == _dailySelected || bounds.Contains(pointer))
            {
                painter.ColorBox(bounds, 120, 90, 50, index == _dailySelected ? 110 : 60);
            }

            painter.Sprite(StatusArt[(int)DailyStatus(_dailies[index])], bounds.X + 3, bounds.Y + 1);
            painter.Text(painter.Fit(_dailies[index].Name, bounds.Width - 22), bounds.X + 18, bounds.Y, ListText);
        }

        if (_dailies.Count == 0)
        {
            painter.TextCentered("No daily quest for your level.", window.X + ListArea.X, window.X + ListArea.Right, window.Y + ListArea.Y + 4, ListText);
            return;
        }

        var quest = _dailies[Math.Min(_dailySelected, _dailies.Count - 1)];
        var status = DailyStatus(quest);
        var kills = _dailyInfo.Accepted.GetValueOrDefault(quest.Id);
        if (!ReferenceEquals(quest, _dailyDetailFor) || kills != _dailyKillsShown)
        {
            (_dailyDetailFor, _dailyKillsShown) = (quest, kills);
            DescribeDaily(painter, quest, status, kills);
        }

        var left = window.X + PageLeft;
        var right = window.X + PageRight;
        painter.TextCentered(painter.Fit(quest.Name, PageRight - PageLeft), left, right, window.Y + 74, Heading);
        painter.TextCentered(_dailyLevelLine, left, right, window.Y + 90, Ink);
        painter.TextCentered("Get it from: " + DailyQuests.GiverName + " (Ricarten)", left, right, window.Y + 106, Ink);
        Section(painter, window, 0, "Task", Heading, _dailyTask);
        Section(painter, window, 1, status == QuestBookStatus.Done ? "Done today" : StatusNames[(int)status], StatusColors[(int)status], null);
        Section(painter, window, 2, "Rewards", Heading, _dailyRewards);

        var button = At(window, DailyButton);
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
                painter.TextCentered("Accept and claim at " + DailyQuests.GiverName, left, right, button.Y + 4, Ink);
            }
        }
        else if (label is not null)
        {
            painter.ColorBox(button, 90, 50, 20, button.Contains(pointer) ? 230 : 180);
            painter.TextCentered(label, button.X, button.Right, button.Y + 3, new Color(255, 230, 170));
        }
    }

    // The level line, the task with the kills so far and the rewards, made again only when another quest or count shows.
    private void DescribeDaily(HudPainter painter, DailyQuest quest, QuestBookStatus status, int kills)
    {
        var width = PageRight - PageLeft - 12;
        _dailyLevelLine = string.Create(CultureInfo.InvariantCulture,
            $"Daily, level {quest.MinLevel}{(quest.MaxLevel >= 999 ? "+" : $"-{quest.MaxLevel}")}");
        var task = string.Create(CultureInfo.InvariantCulture,
            $"{quest.Story} Defeat {quest.Kills} monsters of level {quest.MonsterLevel} or higher.");
        if (status == QuestBookStatus.Running)
        {
            task += string.Create(CultureInfo.InvariantCulture, $" ({Math.Min(kills, quest.Kills)}/{quest.Kills})");
        }

        _dailyTask.Clear();
        _dailyTask.AddRange(HudChatText.Wrap(task, width, SectionLines[0], MaxLineChars, painter.Measure));
        _dailyRewards.Clear();
        foreach (var reward in (string[])
                 [
                     string.Create(CultureInfo.InvariantCulture, $"- Experience: {quest.ExpPercent}% of your level"),
                     string.Create(CultureInfo.InvariantCulture, $"- {quest.Gold:N0} Gold"),
                     string.Create(CultureInfo.InvariantCulture, $"- {quest.Coins} coin{(quest.Coins == 1 ? "" : "s")}"),
                     string.Create(CultureInfo.InvariantCulture, $"- {quest.ItemCount} x {quest.ItemKey}")
                 ])
        {
            _dailyRewards.AddRange(HudChatText.Wrap(reward, width, SectionLines[2], MaxLineChars, painter.Measure));
        }
    }
}
