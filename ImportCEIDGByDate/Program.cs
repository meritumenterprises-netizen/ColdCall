using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

if (args.Length != 1 || !DateOnly.TryParseExact(args[0], "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
    || date < new DateOnly(2026, 9, 20))
{
    Console.Error.WriteLine("Usage: ImportCEIDGByDate YYYY-MM-DD (minimum: 2026-09-20).");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var token = cancellation.Token;
try
{
    var secrets = new ConfigurationBuilder().AddUserSecrets<SecretMarker>(optional: true).Build();
    var connectionString = Environment.GetEnvironmentVariable("CEIDG_CONNECTION_STRING")
        ?? secrets["ConnectionStrings:Import"]
        ?? "Server=localhost;Database=Firmy_temp;Integrated Security=True;TrustServerCertificate=True;";
    await using var db = new SqlConnection(connectionString);
    await db.OpenAsync(token);
    var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var skipBulletin = false;
    await using (var existing = new SqlCommand(
        "SELECT CASE WHEN EXISTS (SELECT 1 FROM ColdCall.CEIDGImport WHERE ImportDate=@date) THEN 1 ELSE 0 END;", db))
    {
        existing.Parameters.Add("@date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        if (Convert.ToInt32(await existing.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 1)
        {
            Console.WriteLine($"Skipped bulletin {day}: records already exist.");
            skipBulletin = true;
        }
    }
    var apiToken = Environment.GetEnvironmentVariable("CEIDG_API_TOKEN") ?? secrets["Ceidg:Token"];
    if (string.IsNullOrWhiteSpace(apiToken))
        throw new InvalidOperationException("Set CEIDG_API_TOKEN or the user secret Ceidg:Token.");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    Uri? next = new($"https://dane.biznes.gov.pl/api/ceidg/v3/zmiana?dataod={day}&datado={day}&page=0&limit=500");
    if (skipBulletin) next = null;
    var visited = new HashSet<string>(StringComparer.Ordinal);
    var page = 0;
    var total = 0;
    while (next is not null)
    {
        ValidateUrl(next);
        if (!visited.Add(next.AbsoluteUri))
            throw new InvalidDataException("Pagination cycle detected.");
        using var document = await FetchAsync(http, next, token);
        if (document is null) break;
        var root = document.RootElement;
        if ((!root.TryGetProperty("identyfikatoryWpisow", out var entries)
                && !root.TryGetProperty("identyfikatoryWpisów", out entries)) || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Missing identyfikatoryWpisów array.");
        var ids = entries.EnumerateArray().Select(entry =>
            entry.ValueKind == JsonValueKind.String && Guid.TryParse(entry.GetString(), out var id)
                ? id : throw new InvalidDataException("Invalid entry identifier; page not saved.")).Distinct().ToArray();
        var following = ReadNext(root, next);
        await using var transaction = (SqlTransaction)await db.BeginTransactionAsync(token);
        try
        {
            // Serialize imports for this day so concurrent runs cannot insert duplicate date/ID pairs.
            await using var command = new SqlCommand("""
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock
                    @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
                IF @lockResult < 0 THROW 50001, 'Cannot acquire import lock.', 1;
                INSERT ColdCall.CEIDGImport (ImportDate, WpisId)
                SELECT @date, j.WpisId
                FROM OPENJSON(@ids) WITH (WpisId uniqueidentifier '$') j
                WHERE NOT EXISTS (
                    SELECT 1 FROM ColdCall.CEIDGImport c
                    WHERE c.ImportDate=@date AND c.WpisId=j.WpisId);
                SELECT @@ROWCOUNT;
                """, db, transaction) { CommandTimeout = 180 };
            command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = $"CEIDGImport:{day}";
            command.Parameters.Add("@date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(ids);
            var inserted = Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
            await transaction.CommitAsync(token);
            total += inserted;
            Console.WriteLine($"Page {++page}: received={ids.Length}, inserted={inserted}, total={total}");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        next = following;
    }
    Console.WriteLine($"Completed {day}: pages={page}, inserted={total}.");
    return await ImportDetailsAsync(db, http, token);
}
catch (OperationCanceledException) when (token.IsCancellationRequested)
{
    Console.Error.WriteLine("Import cancelled. Saved data is retained; missing details will be retried on the next run.");
    return 130;
}
catch (Exception ex)
{
    // Never log request headers or configuration: they contain the bearer token.
    Console.Error.WriteLine($"Import failed: {ex.Message}");
    return 1;
}

static void ValidateUrl(Uri uri)
{
    if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != "dane.biznes.gov.pl"
        || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
        || uri.AbsolutePath != "/api/ceidg/v3/zmiana")
        throw new InvalidDataException("Unexpected pagination URL; bearer token was not sent.");
}

static Uri? ReadNext(JsonElement root, Uri current)
{
    if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Object)
        throw new InvalidDataException("Missing pagination links.");
    string? Link(string name)
    {
        if (!links.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Invalid links.{name}.");
        return value.GetString();
    }
    var last = Link("last");
    if (!string.IsNullOrWhiteSpace(last) && new Uri(current, last) == current) return null;
    var next = Link("next");
    if (string.IsNullOrWhiteSpace(next))
    {
        if (!string.IsNullOrWhiteSpace(last))
            throw new InvalidDataException("Missing next link before last page.");
        return null;
    }
    var result = new Uri(current, next);
    ValidateUrl(result);
    return result;
}

static async Task<JsonDocument?> FetchAsync(HttpClient http, Uri uri, CancellationToken token)
{
    for (var attempt = 0; ; attempt++)
    {
        using var response = await http.GetAsync(uri, token);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            && attempt < 4)
        {
            var wait = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is DateTimeOffset time
                    ? time - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(2 << attempt));
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, token);
            continue;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }
}

static async Task<int> ImportDetailsAsync(SqlConnection db, HttpClient http, CancellationToken token)
{
    var pending = new List<(int ImportId, Guid WpisId)>();
    await using (var command = new SqlCommand("""
        SELECT ImportId, WpisId FROM ColdCall.CEIDGImport
        WHERE NULLIF(LTRIM(RTRIM(WpisJSON)), N'') IS NULL
        ORDER BY ImportId;
        """, db) { CommandTimeout = 180 })
    await using (var reader = await command.ExecuteReaderAsync(token))
    {
        while (await reader.ReadAsync(token)) pending.Add((reader.GetInt32(0), reader.GetGuid(1)));
    }
    Console.WriteLine($"Details: 0/{pending.Count} (0.00%); parallelism=4, delay=20 ms per operation. All import dates included.");
    var processed = 0;
    var saved = 0;
    var failed = 0;
    var progressLock = new object();
    await Parallel.ForEachAsync(pending, new ParallelOptions
    {
        MaxDegreeOfParallelism = 4,
        CancellationToken = token
    }, async (entry, workerToken) =>
    {
        workerToken.ThrowIfCancellationRequested();
        try
        {
            var uri = new Uri($"https://dane.biznes.gov.pl/api/ceidg/v3/firma/{entry.WpisId:D}");
            await Task.Delay(20, workerToken);
            using var document = await FetchAsync(http, uri, workerToken);
            if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("No company JSON returned.");
            // SqlConnection is not thread-safe; each operation uses its own pooled connection.
            await using var writeDb = new SqlConnection(db.ConnectionString);
            await writeDb.OpenAsync(workerToken);
            await using var update = new SqlCommand("""
                UPDATE ColdCall.CEIDGImport SET WpisJSON=@json
                WHERE ImportId=@id AND NULLIF(LTRIM(RTRIM(WpisJSON)), N'') IS NULL;
                """, writeDb) { CommandTimeout = 180 };
            update.Parameters.Add("@id", SqlDbType.Int).Value = entry.ImportId;
            update.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = document.RootElement.GetRawText();
            Interlocked.Add(ref saved, await update.ExecuteNonQueryAsync(workerToken));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException
            || ex is TaskCanceledException && !workerToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref failed);
            Console.Error.WriteLine($"Details {entry.WpisId:D}: {ex.Message} (left empty for retry).");
        }
        lock (progressLock)
        {
            processed++;
            Console.WriteLine($"Details: {processed}/{pending.Count} ({100.0 * processed / pending.Count:F2}%); saved={Volatile.Read(ref saved)}, failed={Volatile.Read(ref failed)}");
        }
    });
    Console.WriteLine($"Details completed: saved={saved}, failed={failed}.");
    return failed == 0 ? 0 : 1;
}

internal sealed class SecretMarker;
