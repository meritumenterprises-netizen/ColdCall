using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace GeoPortalAddressImporter;

internal static class ContinuousAddressLookup
{
    public static async Task RunAsync(IAsyncEnumerable<Company> source,
        Func<Company, CancellationToken, Task<MatchResult>> lookup,
        Func<AddressLookupResult, CancellationToken, Task> save,
        Func<int, CancellationToken, Task> checkpoint, CancellationToken token,
        int concurrency = ParallelAddressLookup.Concurrency)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Bound both the result queue and the unfinished checkpoint window.
        using var window = new SemaphoreSlim(concurrency * 4, concurrency * 4);
        var results = Channel.CreateBounded<(long Sequence, AddressLookupResult Result)>(
            new BoundedChannelOptions(concurrency * 2) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        var completed = new Dictionary<long, int>();
        long nextCheckpoint = 0;
        var producer = ProduceAsync();
        try
        {
            await foreach (var item in results.Reader.ReadAllAsync(stop.Token))
            {
                await save(item.Result, stop.Token);
                completed.Add(item.Sequence, item.Result.Company.Id);
                var released = 0;
                var lastId = 0;
                while (completed.Remove(nextCheckpoint, out var id))
                {
                    lastId = id;
                    nextCheckpoint++;
                    released++;
                }
                if (released > 0)
                {
                    await checkpoint(lastId, stop.Token);
                    window.Release(released);
                }
            }
        }
        finally
        {
            stop.Cancel();
            await producer;
        }

        async IAsyncEnumerable<(long Sequence, Company Company)> WorkAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long sequence = 0;
            await foreach (var company in source.WithCancellation(cancellationToken))
            {
                await window.WaitAsync(cancellationToken);
                yield return (sequence++, company);
            }
        }

        async Task ProduceAsync()
        {
            try
            {
                await Parallel.ForEachAsync(WorkAsync(stop.Token),
                    new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = stop.Token },
                    async (item, cancellationToken) =>
                    {
                        AddressLookupResult result;
                        try { result = new(item.Company, await lookup(item.Company, cancellationToken), null); }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                        catch (Exception ex) { result = new(item.Company, null, ex); }
                        await results.Writer.WriteAsync((item.Sequence, result), cancellationToken);
                    });
                results.Writer.TryComplete();
            }
            catch (Exception ex) { results.Writer.TryComplete(ex); }
        }
    }
}
