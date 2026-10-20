using System.Runtime.CompilerServices;

namespace GeoPortalAddressImporter;

internal sealed class CompanySource(CompanyRepository repository, Options options,
    IReadOnlyList<int>? retryIds, int afterId, RunLog log)
{
    public List<int> MissingRetryIds { get; } = new();

    public async IAsyncEnumerable<Company> ReadAsync([EnumeratorCancellation] CancellationToken token)
    {
        var emitted = 0;
        if (retryIds != null)
        {
            foreach (var id in retryIds)
            {
                if (emitted >= options.Limit) yield break;
                var companies = await repository.ReadAsync(0, 1, id, token);
                if (companies.Count == 0)
                {
                    MissingRetryIds.Add(id);
                    log.Write("RETRY", id, "Firma nie kwalifikuje sie juz do importu; usunieto z listy po zakonczeniu przebiegu.");
                }
                foreach (var company in companies) { emitted++; yield return company; }
            }
            yield break;
        }
        var cursor = afterId;
        while (emitted < options.Limit)
        {
            var companies = await repository.ReadAsync(cursor, Math.Min(256, options.Limit - emitted), options.CompanyId, token);
            if (companies.Count == 0) yield break;
            foreach (var company in companies)
            {
                cursor = company.Id;
                emitted++;
                yield return company;
            }
        }
    }
}
