using PristonTale.Gameplay.World;

namespace PristonTale.Tests;

// World entry's completion: presence, actors, fields and HUD art in, or its longest wait over.
internal static class EntryReadinessTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("world entry completes once presence, actors, fields and the HUD are ready and reports each part", ReadyAsync);
        yield return ("world entry completes after its longest wait when something never arrives", TimeoutAsync);
    }

    private static Task ReadyAsync()
    {
        var entry = new EntryReadiness();
        Assert.True(!entry.Observe(100, false, true, true, true), "not complete before the first presence");
        Assert.True(!entry.Observe(200, true, false, true, true), "actors still preparing hold it");
        Assert.True(!entry.Observe(300, true, true, false, true), "a field in flight holds it");
        Assert.True(entry.Observe(500, true, true, true, true), "complete once everything is in");
        Assert.True(!entry.Observe(600, true, true, true, true), "it completes once");
        var report = entry.Report();
        Assert.True(report.Contains("complete after 500 ms") && report.Contains("presence 200 ms") && report.Contains("actors 300 ms")
            && report.Contains("fields 100 ms") && report.Contains("hud 100 ms") && !entry.TimedOut, report);
        return Task.CompletedTask;
    }

    private static Task TimeoutAsync()
    {
        var entry = new EntryReadiness();
        Assert.True(!entry.Observe(EntryReadiness.MaxHoldMs - 1, false, true, true, true), "waiting before the longest wait");
        Assert.True(entry.Observe(EntryReadiness.MaxHoldMs, false, true, true, true), "complete at the longest wait");
        Assert.True(entry.TimedOut && entry.Report().Contains("presence waiting"), entry.Report());
        return Task.CompletedTask;
    }
}
