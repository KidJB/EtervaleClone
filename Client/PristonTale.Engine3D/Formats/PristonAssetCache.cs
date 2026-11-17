using System.Collections.Concurrent;

namespace PristonTale.Engine3D.Formats;

// Parsed files by path. Each file parses once: workers asking for one already being parsed (characters of a class share
// their motion file) wait for that parse instead of reading it again.
public sealed class PristonAssetCache
{
    private readonly ConcurrentDictionary<string, Lazy<PristonModelInfo>> _inx = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<PristonModel>> _models = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<PristonStage>> _stages = new(StringComparer.Ordinal);

    public PristonModelInfo GetOrAddModelInfo(string resolvedPath, Func<string, PristonModelInfo> parse)
    {
        var key = CacheKey(resolvedPath);
        // The key is lower-cased for case-insensitive matching, but parsing must use the
        // ORIGINAL resolvedPath so the file is read with its real (case-sensitive) name.
        return Once(_inx, key, resolvedPath, parse);
    }

    public PristonModel GetOrAddModel(string resolvedPath, Func<string, PristonModel> parse)
    {
        var key = CacheKey(resolvedPath);
        return Once(_models, key, resolvedPath, parse);
    }

    public PristonStage GetOrAddStage(string resolvedPath, Func<string, PristonStage> parse)
    {
        var key = CacheKey(resolvedPath);
        return Once(_stages, key, resolvedPath, parse);
    }

    public void Clear()
    {
        _inx.Clear();
        _models.Clear();
        _stages.Clear();
    }

    // A parse that failed is dropped, so a later request reads the file again rather than rethrowing the old failure.
    private static T Once<T>(ConcurrentDictionary<string, Lazy<T>> cache, string key, string resolvedPath, Func<string, T> parse)
    {
        var entry = cache.GetOrAdd(key, static (_, arg) => new Lazy<T>(() => arg.parse(arg.resolvedPath), LazyThreadSafetyMode.ExecutionAndPublication),
            (resolvedPath, parse));
        try
        {
            return entry.Value;
        }
        catch
        {
            cache.TryRemove(new KeyValuePair<string, Lazy<T>>(key, entry));
            throw;
        }
    }

    private static string CacheKey(string resolvedPath) => resolvedPath.ToLowerInvariant();
}
