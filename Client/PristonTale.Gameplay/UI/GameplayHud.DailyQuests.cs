using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI;

// Daily quests: the quest book's Daily tab follows the server's daily state, and its Accept or Claim click goes out here;
// the answer shows as a notice.
public sealed partial class GameplayHud
{
    private void UpdateDailyQuests()
    {
        if (_state is { } state)
        {
            _questBook.RefreshDaily(DailyQuests, state.Level);
        }

        if (_dailyAnswer is { IsCompleted: true } answer)
        {
            _dailyAnswer = null;
            _notice.Show(answer.IsCompletedSuccessfully ? DailyMessage((DailyQuestStatus)answer.Result) : "The daily quest request failed.");
        }

        if (_dailyAnswer is null && _questBook.TakeDailyRequest() is { } request && DailyQuestRequest is { } send)
        {
            _dailyAnswer = send(request.Npc, request.Quest, request.Action);
        }
    }

    private static string DailyMessage(DailyQuestStatus status) => status switch
    {
        DailyQuestStatus.Ok => "Done.",
        DailyQuestStatus.NotOffered => "That daily quest is not for your level.",
        DailyQuestStatus.AlreadyAccepted => "You already accepted that daily quest.",
        DailyQuestStatus.DoneToday => "You already finished that daily quest today.",
        DailyQuestStatus.NotAccepted => "Accept that daily quest first.",
        DailyQuestStatus.NotFinished => "That hunt is not finished yet.",
        DailyQuestStatus.NoRoom => "Your bag has no room for the reward.",
        DailyQuestStatus.NotAtGiver => "Talk to " + Simulation.Quests.DailyQuests.GiverName + " first.",
        _ => "The daily quest request was refused."
    };
}
