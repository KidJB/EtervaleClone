namespace PristonTale.Gameplay.World;

// When world entry is complete: the server's first presence, the actors it named prepared, the fields in flight
// uploaded and the HUD's art decoded. Until then the HUD loads its images eagerly; once everything is in, or after
// MaxHoldMs, the time each part took is reported once.
public sealed class EntryReadiness
{
    public const double MaxHoldMs = 10000;

    private double _presenceMs = -1;
    private double _actorsMs = -1;
    private double _fieldsMs = -1;
    private double _hudMs = -1;

    public double ReadyMs { get; private set; } = -1;
    public bool TimedOut { get; private set; }

    // Called each frame with the time since entry; returns true on the frame entry completes.
    public bool Observe(double elapsedMs, bool presenceSeen, bool actorsPrepared, bool fieldsIdle, bool hudIdle)
    {
        if (ReadyMs >= 0)
        {
            return false;
        }

        Mark(ref _presenceMs, presenceSeen, elapsedMs);
        Mark(ref _actorsMs, presenceSeen && actorsPrepared, elapsedMs);
        Mark(ref _fieldsMs, fieldsIdle, elapsedMs);
        Mark(ref _hudMs, hudIdle, elapsedMs);
        var ready = _presenceMs >= 0 && actorsPrepared && fieldsIdle && hudIdle;
        if (!ready && elapsedMs < MaxHoldMs)
        {
            return false;
        }

        ReadyMs = elapsedMs;
        TimedOut = !ready;
        return true;
    }

    public string Report() => FormattableString.Invariant(
        $"[Entry] complete after {ReadyMs:0} ms{(TimedOut ? " (timed out)" : "")}: presence {Part(_presenceMs)}, actors {Part(_actorsMs)}, fields {Part(_fieldsMs)}, hud {Part(_hudMs)}");

    private static string Part(double ms) => ms < 0 ? "waiting" : FormattableString.Invariant($"{ms:0} ms");

    private static void Mark(ref double at, bool done, double elapsedMs)
    {
        if (at < 0 && done)
        {
            at = elapsedMs;
        }
    }
}
