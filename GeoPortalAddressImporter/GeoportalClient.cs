using System.Net;
using System.Text.Json;

namespace GeoPortalAddressImporter;

internal sealed class GeoportalClient(HttpClient client, string directory, int delayMilliseconds, int cacheDays, RunLog log)
{
    private static readonly Uri BaseUri = new("https://services.gugik.gov.pl/uug/");
    private DateTimeOffset nextRequest;
    public int Requests { get; private set; }
    public int CacheHits { get; private set; }

    public async Task<MatchResult> FindAsync(Company company, CancellationToken token)
    {
        var missing = Matching.Missing(company);
        if (missing != null) return new(null, missing);
        var street = Text.Street(company.Street);
        var query = company.City + (street.Length == 0 ? " " : ", " + street + " ") + company.Number;
        var candidate = Matching.Candidate(company, await GetAsync(
            "?request=GetAddress&address=" + Uri.EscapeDataString(query) + "&accuracy=1&exact_number=1", token));
        if (candidate.Address is not { } address) return candidate;
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
        var file = Path.Combine(directory, Text.Hash(path) + ".json");
        if (File.Exists(file))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<CacheEntry>(await File.ReadAllTextAsync(file, token));
                if (cached != null && cached.Path == path)
                {
                    var parsed = ApiParser.Parse(cached.Json);
                    var age = parsed.Returned == 0 ? Math.Min(1, cacheDays) : cacheDays;
                    if (cached.Created >= DateTimeOffset.UtcNow.AddDays(-age))
                    {
                        CacheHits++;
                        return parsed;
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or IOException)
            {
                log.Write("CACHE", null, "Pominieto uszkodzony cache: " + ex.Message);
            }
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var wait = nextRequest - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(delayMilliseconds);
            try
            {
                Requests++;
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
                    if (ex is JsonException && attempt < 3)
                    {
                        log.Write("RETRY", null, $"Odpowiedz nie jest poprawnym JSON; proba {attempt + 1}/4.");
                        var retry = response.Headers.RetryAfter?.Delta ??
                            (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                        await Task.Delay(retry < TimeSpan.Zero ? TimeSpan.Zero : retry, token);
                        continue;
                    }
                    var message = $"{ex.Message} Surowa odpowiedz: {diagnostic}";
                    if (ex is JsonException && Text.Normalize(json) is "BLAD ZAPYTANIA." or "BŁĄD ZAPYTANIA.")
                        throw new GeoportalQueryException(message, ex);
                    throw new InvalidDataException(message, ex);
                }
                Directory.CreateDirectory(directory);
                await Files.AtomicAsync(file, new CacheEntry(path, DateTimeOffset.UtcNow, json), token);
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
