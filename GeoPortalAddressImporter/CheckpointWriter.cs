using System.Diagnostics;

namespace GeoPortalAddressImporter;

internal sealed class CheckpointWriter(string path, int batchSize = 100,
    TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private int pendingCount;
    private int lastId;

    // Called only for a contiguous prefix of successfully completed companies.
    public async Task AdvanceAsync(int companyId, CancellationToken token)
    {
        lastId = companyId;
        pendingCount++;
        if (pendingCount >= batchSize || elapsed.Elapsed >= (interval ?? TimeSpan.FromSeconds(1)))
            await FlushAsync(token);
    }

    private async Task FlushAsync(CancellationToken token)
    {
        if (pendingCount == 0) return;
        await Files.AtomicAsync(path, new Progress(lastId), token);
        pendingCount = 0;
        elapsed.Restart();
    }

    public ValueTask DisposeAsync() => new(FlushAsync(CancellationToken.None));
}
