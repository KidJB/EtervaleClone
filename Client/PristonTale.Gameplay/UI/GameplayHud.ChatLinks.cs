using PristonTale.Gameplay.UI.Chat;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    // The chat's Discord button opens the server's invite in the browser.
    private void OpenDiscordInvite()
    {
        if (!HudBrowser.Open(HudChatLinks.DiscordInvite))
        {
            _chat.System("The Discord invite could not be opened.");
        }
    }
}
