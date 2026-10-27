using System.Runtime.CompilerServices;

namespace GeoPortalAddressImporter;

internal static class PipelineTests
{
    public static async Task RunAsync()
    {
        await ContinuousProgressAsync();
        await FailureAsync();
        await CancellationAsync();
        await BatchesAsync();
        await BatchFailureAsync();
        await PartialBatchAsync();
        Console.WriteLine("PASS: ciagly pipeline, zapis bez czekania na paczke, checkpoint bez luk, blad i anulowanie.");
    }

    private static async IAsyncEnumerable<Company> Source(int count,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        for (var i = 1; i <= count; i++)
        {
            token.ThrowIfCancellationRequested();
            yield return new Company(i * 10, 2, 1, "Province", "City", null, null, null);
            await Task.Yield();
        }
    }

    private static async Task ContinuousProgressAsync()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fifth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var savedLater = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new List<int>();
        var checkpoints = new List<int>();
        var checkpoint = 0;
        var run = ContinuousAddressLookup.RunAsync(Source(20), async (company, token) =>
        {
            if (company.Id == 10) await first.Task.WaitAsync(token);
            if (company.Id == 50) fifth.TrySetResult();
            return new MatchResult(null, "test");
        }, (result, _) =>
        {
            saved.Add(result.Company.Id);
            if (result.Company.Id != 10) savedLater.TrySetResult();
            return Task.CompletedTask;
        }, (id, _) =>
        {
            checkpoints.Add(id);
            Volatile.Write(ref checkpoint, id);
            return Task.CompletedTask;
        }, CancellationToken.None, 4);
        try
        {
            await fifth.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await savedLater.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(Volatile.Read(ref checkpoint) == 0, "Nie wolno przeskoczyc pierwszego niezapisanego rekordu");
        }
        finally { first.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        Check(saved.Count == 20 && saved[0] != 10, "Gotowe wyniki zapisane przed wolnym requestem");
        Check(checkpoint == 200 && checkpoints.SequenceEqual(checkpoints.Order()), "Checkpoint dochodzi do konca bez cofania");
    }

    private static async Task FailureAsync()
    {
        var checkpoint = 0;
        var failed = false;
        try
        {
            await ContinuousAddressLookup.RunAsync(Source(12), (company, _) =>
                Task.FromResult(new MatchResult(null, "test")), (result, _) =>
                {
                    if (result.Company.Id == 20) throw new InvalidOperationException("SQL test failure");
                    return Task.CompletedTask;
                }, (id, _) => { checkpoint = id; return Task.CompletedTask; }, CancellationToken.None, 4);
        }
        catch (InvalidOperationException ex) when (ex.Message == "SQL test failure") { failed = true; }
        Check(failed && checkpoint <= 10, "Blad SQL nie przesuwa checkpointu za nieudany rekord");
    }

    private static async Task CancellationAsync()
    {
        using var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var checkpoint = 0;
        var run = ContinuousAddressLookup.RunAsync(Source(12), async (_, token) =>
        {
            Interlocked.Increment(ref active);
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return new MatchResult(null, "test"); }
            finally { Interlocked.Decrement(ref active); }
        }, (_, _) => Task.CompletedTask, (id, _) => { checkpoint = id; return Task.CompletedTask; }, stop.Token, 4);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        var cancelled = false;
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && checkpoint == 0 && active == 0, "Anulowanie konczy workerow i zachowuje checkpoint");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }

    private static async Task BatchesAsync()
    {
        var sizes = new List<int>();
        var saved = new HashSet<int>();
        var checkpoint = 0;
        await ContinuousAddressLookup.RunAsync(Source(250), (_, _) => Task.FromResult(new MatchResult(null, "test")),
            (_, _) => throw new InvalidOperationException("Individual callback should not run"),
            (id, _) =>
            {
                Check(saved.Contains(id), "Checkpoint dopiero po zapisie partii");
                checkpoint = id;
                return Task.CompletedTask;
            }, CancellationToken.None, 32, (batch, _) =>
            {
                sizes.Add(batch.Count);
                foreach (var item in batch) Check(saved.Add(item.Company.Id), "Kazda firma tylko raz w partii");
                return Task.CompletedTask;
            });
        Check(sizes.SequenceEqual(new[] { 100, 100, 50 }), "Dwie pelne partie i koncowa niepelna");
        Check(checkpoint == 2500 && saved.Count == 250, "Checkpoint obejmuje wszystkie zatwierdzone firmy");
    }

    private static async Task BatchFailureAsync()
    {
        var checkpoint = 0;
        var failed = false;
        try
        {
            await ContinuousAddressLookup.RunAsync(Source(200), (_, _) => Task.FromResult(new MatchResult(null, "test")),
                (_, _) => Task.CompletedTask, (id, _) => { checkpoint = id; return Task.CompletedTask; },
                CancellationToken.None, 32, (_, _) => throw new InvalidOperationException("batch failure"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "batch failure") { failed = true; }
        Check(failed && checkpoint == 0, "Blad partii nie przesuwa checkpointu");
    }

    private static async Task PartialBatchAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = ContinuousAddressLookup.RunAsync(Source(2), async (company, token) =>
        {
            if (company.Id == 20) await saved.Task.WaitAsync(token);
            return new MatchResult(null, "test");
        }, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, stop.Token, 2, (batch, _) =>
        {
            Check(batch.Count == 1, "Wolny request nie zatrzymuje niepelnej partii");
            saved.TrySetResult();
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(50));
        await run;
    }
}
