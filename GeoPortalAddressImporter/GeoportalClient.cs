using System.Net;
using System.Text.Json;

namespace GeoPortalAddressImporter;

internal sealed class GeoportalClient(HttpClient client, string directory, int delayMilliseconds, int cacheDays, RunLog log, bool showCandidateDiagnostics = true)
{
    private static readonly Uri BaseUri = new("https://services.gugik.gov.pl/uug/");
    private DateTimeOffset nextRequest;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private readonly SemaphoreSlim cacheGate = new(1, 1);
    private int requests;
    private int cacheHits;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<ApiResult>>> inFlight = new();
    public int Requests => Volatile.Read(ref requests);
    public int CacheHits => Volatile.Read(ref cacheHits);

    private async Task WaitForRequestSlotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (delayMilliseconds == 0) return;
        await requestGate.WaitAsync(token);
        try
        {
            var wait = nextRequest - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(delayMilliseconds);
        }
        finally { requestGate.Release(); }
    }

    public async Task<MatchResult> FindAsync(Company company, CancellationToken token)
    {
        var missing = Matching.Missing(company);
        if (missing != null)
        {
            if (string.IsNullOrWhiteSpace(company.City)) return new(null, missing);
            return CityMatching.Match(company, await GetAsync(
                "?request=GetAddress&address=" + Uri.EscapeDataString(company.City.Trim()) + "&accuracy=1", token));
        }
        var street = Text.Street(company.Street);
        var query = company.City + (street.Length == 0 ? " " : ", " + street + " ") + company.Number;
        var response = await GetAsync(
            "?request=GetAddress&address=" + Uri.EscapeDataString(query) + "&accuracy=1&exact_number=1", token);
        if (response.Type != "address" || response.Points.Count == 0)
            return new MatchResult(null, "Brak punktu adresowego.");
        var candidate = Matching.Candidate(company, response);
        if (candidate.Address is not { } address)
        {
            if (showCandidateDiagnostics && candidate.Reason.StartsWith("Adres przyblizony", StringComparison.Ordinal))
                GeoportalCandidateDiagnostics.Write(company, response);
            for (int i = 0; i < response.Points.Count(); i++)
            {
                var point = response.Points[i];
                var postalCodeMatches = string.Equals(company.PostalCode?.Trim(), point.Address.PostalCode.Trim(), StringComparison.OrdinalIgnoreCase);
                var exactStreetAndBuilding = Text.Street(company.Street) == Text.Street(point.Address.Street)
                    && Text.Number(company.Number) == Text.Number(point.Address.Number)
                    && point.Accuracy == 1 && point.CityAccuracy == 1
                    && (string.IsNullOrWhiteSpace(company.Street) || point.StreetAccuracy == 1);
                if (string.Compare(company.Province.Trim(), response.Points[i].Address.Province.Trim(), StringComparison.OrdinalIgnoreCase) == 0 &&
                    string.Compare(company.Municipality, response.Points[i].Address.Municipality.Trim(), StringComparison.OrdinalIgnoreCase) == 0 &&
                    string.Compare(company.County, response.Points[i].Address.County.Trim(), StringComparison.OrdinalIgnoreCase) == 0 &&
                    (postalCodeMatches || exactStreetAndBuilding) &&
                    string.Compare(company.City, response.Points[i].Address.City.Trim(), StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!postalCodeMatches) Matching.ValidatePoint(point);
                    return new MatchResult(
                        response.Points[i].Address,
                        postalCodeMatches
                            ? "Adres wybrany na podstawie zgodnosci podzialu administracyjnego."
                            : $"Dokladny adres mimo roznicy kodu pocztowego: firma={company.PostalCode}, GeoPortal={point.Address.PostalCode}.");
                }
            }
            return candidate;
        }
        if (string.IsNullOrWhiteSpace(address.Simc)) return new(null, "Brak SIMC.");
        var path = "?request=GetAddresses&simc=" + Uri.EscapeDataString(address.Simc);
        if (!string.IsNullOrWhiteSpace(address.Street))
        {
            if (string.IsNullOrWhiteSpace(address.Ulic)) return new(null, "Brak ULIC.");
            path += "&ulic=" + Uri.EscapeDataString(address.Ulic);
        }
        return Matching.Verify(company, address, await GetAsync(path, token));
    }

    private async Task<ApiResult> GetAsync(string path, CancellationToken token)
    {
        var pending = inFlight.GetOrAdd(path, key => new Lazy<Task<ApiResult>>(() => GetCoreAsync(key, token)));
        try { return await pending.Value.WaitAsync(token); }
        finally
        {
            if (pending.IsValueCreated && pending.Value.IsCompleted)
                ((ICollection<KeyValuePair<string, Lazy<Task<ApiResult>>>>)inFlight).Remove(new(path, pending));
        }
    }

    private async Task<ApiResult> GetCoreAsync(string path, CancellationToken token)
    {
        var file = Path.Combine(directory, Text.Hash(path) + ".json");
        if (File.Exists(file))
        {
            await cacheGate.WaitAsync(token);
            try
            {
                var cached = JsonSerializer.Deserialize<CacheEntry>(await File.ReadAllTextAsync(file, token));
                if (cached != null && cached.Path == path)
                {
                    var parsed = ApiParser.Parse(cached.Json);
                    var age = parsed.Returned == 0 ? Math.Min(1, cacheDays) : cacheDays;
                    if (cached.Created >= DateTimeOffset.UtcNow.AddDays(-age))
                    {
                        Interlocked.Increment(ref cacheHits);
                        return parsed;
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or IOException)
            {
                log.Write("CACHE", null, "Pominieto uszkodzony cache: " + ex.Message);
            }
            finally { cacheGate.Release(); }
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await WaitForRequestSlotAsync(token);
            try
            {
                Interlocked.Increment(ref requests);
                using var response = await client.GetAsync(new Uri(BaseUri, path), HttpCompletionOption.ResponseHeadersRead, token);
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    if (attempt == 3) response.EnsureSuccessStatusCode();
                    var retry = response.Headers.RetryAfter?.Delta ??
                        (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    retry = retry < TimeSpan.Zero ? TimeSpan.Zero : retry;
                    log.Write("RETRY", null, $"HTTP {(int)response.StatusCode}; proba {attempt + 1}/4; oczekiwanie {retry.TotalSeconds:F0}s.");
                    await Task.Delay(retry + TimeSpan.FromMilliseconds(delayMilliseconds), token);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream();
                var block = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(block, timeout.Token)) != 0)
                {
                    if (buffer.Length + read > 20 * 1024 * 1024)
                        throw new InvalidDataException("Odpowiedz UUG przekracza limit 20 MB.");
                    await buffer.WriteAsync(block.AsMemory(0, read), timeout.Token);
                }
                var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
                ApiResult result;
                try { result = ApiParser.Parse(json); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    var diagnostic = Path.Combine(directory, "invalid-responses",
                        $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                    await Files.AtomicAsync(diagnostic, new CacheEntry(path, DateTimeOffset.UtcNow, json), token);
                    log.Write("API", null, $"Nieprawidlowa odpowiedz; zapytanie={path}; plik={diagnostic}.");
                    var message = $"{ex.Message} Surowa odpowiedz: {diagnostic}";
                    if (ex is JsonException)
                        throw new GeoportalQueryException(message, ex);
                    throw new InvalidDataException(message, ex);
                }
                Directory.CreateDirectory(directory);
                await cacheGate.WaitAsync(token);
                try { await Files.AtomicAsync(file, new CacheEntry(path, DateTimeOffset.UtcNow, json), token); }
                finally { cacheGate.Release(); }
                return result;
            }
            catch (Exception ex) when (attempt < 3 && !token.IsCancellationRequested &&
                (ex is HttpRequestException or OperationCanceledException))
            {
                log.Write("RETRY", null, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), token);
            }
        }
        throw new HttpRequestException("UUG nie odpowiedzial po czterech probach.");
    }

    private sealed record CacheEntry(string Path, DateTimeOffset Created, string Json);
}
