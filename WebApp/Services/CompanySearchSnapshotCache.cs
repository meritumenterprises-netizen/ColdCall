using Microsoft.Extensions.Caching.Memory;

namespace ColdCall.Services;

public sealed class CompanySearchSnapshotCache : IDisposable
{
    public const int MaximumRecords = 100000;
    // Bound snapshots, including one additional cached ordering, to approximately 24 MB.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1000000 });

    public bool TryGet(string key, out CompanySearchSnapshot? snapshot) => _cache.TryGetValue(key, out snapshot);

    public bool IsTooLarge(string key) => _cache.TryGetValue("TooLarge:" + key, out bool value) && value;

    public void MarkTooLarge(string key)
    {
        _cache.Remove(key);
        _cache.Set("TooLarge:" + key, true,
            new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(TimeSpan.FromMinutes(2)));
    }

    public void Set(string key, CompanySearchSnapshot snapshot)
    {
        _cache.Remove("TooLarge:" + key);
        _cache.Set(key, snapshot, new MemoryCacheEntryOptions().SetSize(snapshot.Ids.Length + 1)
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(2)));
    }

    public void Dispose() => _cache.Dispose();
}
