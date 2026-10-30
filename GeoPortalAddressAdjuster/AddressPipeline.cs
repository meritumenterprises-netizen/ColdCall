using System.Diagnostics;
using System.Threading.Channels;
using GeoPortalAddressImporter;

namespace GeoPortalAddressAdjuster;

public static class AddressPipeline
{
    public static async Task RunAsync(CompanyAddressRepository repository, GeoportalAddressService geoportal,
        AdjusterOptions options, long total, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = cancellation.Token;
        var companies = Channel.CreateBounded<GeoportalAddressRequest>(200);
        var results = Channel.CreateBounded<AddressLookup>(200);
        long processed = 0, saved = 0, errors = 0, resolved = 0;
        long lookupTicks = 0;
        var sqlTime = TimeSpan.Zero;
        var elapsed = Stopwatch.StartNew();
        async Task GuardAsync(Func<Task> action)
        {
            try { await action(); }
            catch { cancellation.Cancel(); throw; }
        }
        var producer = GuardAsync(async () =>
        {
            try
            {
                var afterId = 0;
                while (true)
                {
                    var batch = await repository.ReadBatchAsync(afterId, ct);
                    if (batch.Count == 0) break;
                    foreach (var company in batch) await companies.Writer.WriteAsync(company, ct);
                    afterId = batch[^1].CompanyId;
                }
            }
            finally { companies.Writer.TryComplete(); }
        });
        var workers = Enumerable.Range(0, options.Workers).Select(_ => GuardAsync(async () =>
        {
            await foreach (var company in companies.Reader.ReadAllAsync(ct))
            {
                var watch = Stopwatch.StartNew();
                var result = await geoportal.FindAsync(company, ct);
                Interlocked.Add(ref lookupTicks, watch.Elapsed.Ticks);
                Interlocked.Increment(ref resolved);
                await results.Writer.WriteAsync(new AddressLookup { Company = company, Result = result }, ct);
            }
        })).ToArray();
        var completion = GuardAsync(async () =>
        {
            try { await Task.WhenAll(workers); }
            finally { results.Writer.TryComplete(); }
        });
        var writer = GuardAsync(async () =>
        {
            var batch = new List<AddressLookup>(100);
            await foreach (var item in results.Reader.ReadAllAsync(ct))
            {
                batch.Add(item);
                if (batch.Count == 100) await SaveAsync();
            }
            if (batch.Count > 0) await SaveAsync();
            async Task SaveAsync()
            {
                var watch = Stopwatch.StartNew();
                saved += await repository.AssignBatchAsync(batch, ct);
                sqlTime += watch.Elapsed;
                foreach (var item in batch.Where(x => x.Result.ErrorCode != GeoportalAddressErrorCode.None))
                {
                    errors++;
                    Console.Error.WriteLine($"GeoPortal: firma {item.Company.CompanyId}: {item.Result.ErrorCode}: {item.Result.Reason}");
                }
                processed += batch.Count;
                var rate = processed / Math.Max(elapsed.Elapsed.TotalSeconds, 0.001);
                Console.WriteLine($"{(total == 0 ? 100 : 100d * processed / total):F2}% ({processed}/{total}); " +
                    $"assigned={saved}; skipped={processed - saved}; errors={errors}; {rate:F2}/s; " +
                    $"HTTP={geoportal.Requests}; cache={geoportal.CacheHits}; " +
                    $"lookup avg={TimeSpan.FromTicks(Interlocked.Read(ref lookupTicks)).TotalMilliseconds / Math.Max(1, Interlocked.Read(ref resolved)):F0}ms; SQL={sqlTime.TotalSeconds:F2}s");
                batch.Clear();
            }
        });
        await Task.WhenAll(producer, completion, writer);
    }
}
