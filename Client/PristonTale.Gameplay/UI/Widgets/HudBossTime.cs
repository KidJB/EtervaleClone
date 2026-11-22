using System.Globalization;
using PristonTale.Client.Net.Presence;

namespace PristonTale.Gameplay.UI.Widgets;

// The minimap's next boss line, "Next Boss: hh:mm:ss": the server sends the pass once and the client counts down from its
// arrival. It shows only while a pass of today counts down (none for a field without bosses, a pass tomorrow or one due);
// the line is formatted again only when the shown second changes.
public sealed class HudBossTime
{
    private double _receivedMs;
    private long _shownSeconds = -1;
    private string _line = "";

    public BossTimeInfo Info { get; private set; }

    public void Receive(BossTimeInfo info, double nowMs)
    {
        if (ReferenceEquals(info, Info))
        {
            return;
        }

        Info = info;
        _receivedMs = nowMs;
        _shownSeconds = -1;
        _line = "";
    }

    // The line to show, or "" when nothing counts down.
    public string Text(double nowMs)
    {
        if (Info is not { HasBoss: true, Exact: true } info)
        {
            return "";
        }

        var left = Math.Max(0L, info.SecondsLeft - (long)((nowMs - _receivedMs) / 1000d));
        if (left == 0)
        {
            return "";
        }

        if (left != _shownSeconds)
        {
            _shownSeconds = left;
            _line = "Next Boss: " + Clock(left);
        }

        return _line;
    }

    public static string Clock(long seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
}
