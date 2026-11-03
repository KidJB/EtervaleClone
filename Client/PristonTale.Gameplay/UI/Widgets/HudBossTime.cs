using System.Globalization;
using PristonTale.Client.Net.Presence;

namespace PristonTale.Gameplay.UI.Widgets;

// The minimap's next boss line: the server sends the pass once and the client counts down from its arrival. The line is
// formatted again only when the shown second changes; once the pass is due it reads "now" until the server sends the next.
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

    // The line to show, or "" for a field without bosses.
    public string Text(double nowMs)
    {
        if (Info is not { HasBoss: true } info)
        {
            return "";
        }

        if (!info.Exact)
        {
            if (_shownSeconds != long.MaxValue)
            {
                _shownSeconds = long.MaxValue;
                _line = $"Next boss: {info.BossName} tomorrow";
            }

            return _line;
        }

        var left = Math.Max(0L, info.SecondsLeft - (long)((nowMs - _receivedMs) / 1000d));
        if (left != _shownSeconds)
        {
            _shownSeconds = left;
            _line = left == 0 ? $"Next boss: {info.BossName} now" : $"Next boss: {info.BossName} {Clock(left)}";
        }

        return _line;
    }

    public static string Clock(long seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
}
