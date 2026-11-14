namespace PristonTale.Gameplay.UI.Chat;

// Chat emotes: a spoken line that is only "e1" to "e20" (or one of their words, sent as its code) shows the emote's icon
// after the speaker's name. The icons are Hud/Emotes/emoteN.png; without them the code shows as text.
public static class HudEmotes
{
    public const int Count = 20;

    // The emote a frozen chat shows over the player (e6, crying).
    public const int Crying = 6;
    private const string Folder = "Hud/Emotes/emote";

    private static readonly string[] Words =
    [
        "/sweat", "/love", "/angry", "/bigeye", "/sad", "/cry", "/ops", "/nerd", "/craving", "/hurt",
        "/silence", "/crazy", "/hello", "/tired", "/shame", "/brokeheart", "/abused", "/kappa", "/kappapride", "/pogchamp"
    ];

    private static readonly string[] Codes = Enumerable.Range(1, Count).Select(static index => "e" + index).ToArray();

    public static readonly string[] Art = Enumerable.Range(1, Count).Select(static index => Folder + index + ".png").ToArray();

    // The code an emote word stands for ("/love" -> "e2"), or the text itself.
    public static string Code(string typed)
    {
        for (var index = 0; index < Words.Length; index++)
        {
            if (string.Equals(typed, Words[index], StringComparison.OrdinalIgnoreCase))
            {
                return Codes[index];
            }
        }

        return typed;
    }

    // "Name:" of a line "Name: eN", the part drawn before its icon (made once per line by the wrap cache's caller).
    public static string Speaker(string line)
    {
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? "" : line[..(colon + 1)];
    }

    // The icon of a line "Name: eN", or null.
    public static string Of(string line)
    {
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        var said = colon < 0 ? line.AsSpan() : line.AsSpan(colon + 2);
        if (said.Length is < 2 or > 3 || said[0] != 'e' || !int.TryParse(said[1..], out var index) || index is < 1 or > Count)
        {
            return null;
        }

        return Art[index - 1];
    }
}

// The server's community links, constants of the game rather than settings.
public static class HudChatLinks
{
    // The Discord invite the chat's Discord button offers; empty hides the button.
    public const string DiscordInvite = "";
}

// Opens a web link in the player's browser: the shell opens it on Windows, xdg-open on Linux and open on macOS.
public static class HudBrowser
{
    public static bool Open(string url)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
