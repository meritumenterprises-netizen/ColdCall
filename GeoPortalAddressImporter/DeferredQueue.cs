using System.Text.Json;

namespace GeoPortalAddressImporter;

internal sealed class GeoportalQueryException(string message, Exception inner) : Exception(message, inner);

internal sealed record DeferredCompany(Company Company, string Reason, DateTimeOffset FailedAt);

internal sealed class DeferredQueue(string path, Dictionary<int, DeferredCompany> entries)
{
    public IReadOnlyList<int> Ids => entries.Keys.Order().ToArray();
    public int Count => entries.Count;

    public static async Task<DeferredQueue> OpenAsync(string path, CancellationToken token)
    {
        var entries = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<int, DeferredCompany>>(await File.ReadAllTextAsync(path, token))
                ?? throw new InvalidDataException("Nieprawidlowa lista firm do ponowienia.")
            : new Dictionary<int, DeferredCompany>();
        return new DeferredQueue(path, entries);
    }

    public async Task RecordAsync(Company company, string reason, CancellationToken token)
    {
        entries[company.Id] = new DeferredCompany(company, reason, DateTimeOffset.UtcNow);
        await Files.AtomicAsync(path, entries, token);
    }

    public async Task RemoveAsync(int companyId, CancellationToken token)
    {
        if (entries.Remove(companyId)) await Files.AtomicAsync(path, entries, token);
    }
}
