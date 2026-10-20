namespace GeoPortalAddressImporter;

internal sealed record AddressLookupResult(Company Company, MatchResult? Match, Exception? Error);

internal static class ParallelAddressLookup
{
    public const int Concurrency = 8;

    public static async Task<IReadOnlyList<AddressLookupResult>> FindAsync(
        IReadOnlyList<Company> companies, GeoportalClient client, CancellationToken token)
    {
        var results = new AddressLookupResult[companies.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, companies.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = token },
            async (index, cancellationToken) =>
            {
                var company = companies[index];
                try
                {
                    var match = await client.FindAsync(company, cancellationToken);
                    results[index] = new AddressLookupResult(company, match, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    results[index] = new AddressLookupResult(company, null, exception);
                }
            });
        // Ordered results let the caller commit and checkpoint only a completed prefix.
        return results;
    }
}
