using PristonTale.Engine3D.Formats;

namespace PristonTale.Tests;

// The parsed-file cache: workers asking for one file at once share a single parse, and a failed parse is retried.
internal static class AssetCacheTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("asset cache: concurrent requests for one file parse it once, and a failed parse is read again", OnceAsync);
    }

    private static async Task OnceAsync()
    {
        var cache = new PristonAssetCache();
        var parses = 0;
        using var gate = new ManualResetEventSlim();
        PristonModelInfo Parse(string path)
        {
            Interlocked.Increment(ref parses);
            gate.Wait(TimeSpan.FromSeconds(5));
            return null!;
        }

        var requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.GetOrAddModelInfo("Char/Body.inx", Parse))).ToArray();
        await Task.Delay(50);
        gate.Set();
        await Task.WhenAll(requests);
        Assert.True(parses == 1, $"one file parsed {parses} times");

        var attempts = 0;
        PristonModelInfo Failing(string path) => ++attempts == 1 ? throw new IOException("busy") : null!;
        Assert.Throws<IOException>(() => cache.GetOrAddModelInfo("Char/Head.inx", Failing), "the first parse did not fail");
        cache.GetOrAddModelInfo("Char/Head.inx", Failing);
        Assert.True(attempts == 2, "a failed parse was not read again");
    }
}
