using System.Globalization;
using System.Text;
using System.Text.Json;
using GeoPortalAddressImporter;
using Microsoft.Data.SqlClient;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

RunLog? log = null;
try
{
    if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
    {
        Console.WriteLine("""
            GeoPortalAddressImporter (.NET 10)

            run [--apply] [--limit N | --all] [--company-id ID] [--restart]
                [--after-id ID] [--delay-ms N] [--data-dir PATH] [--retry-errors]
            self-test
            self-test-db

            Domyslnie: RAPORT bez zapisu, maksymalnie 10 firm.
            --apply: zapis do Adresy, CompanyAddress i AddressIsMissing=0.
            --all: wszystkie kwalifikujace sie firmy (dlugotrwale przetwarzanie).
            --restart: ignoruj pozycje wznowienia; cache nadal jest uzywany.
            --delay-ms: odstep pomiedzy zadaniami HTTP, co najmniej 100 ms.
            --company-id: pojedyncza firma; bez odczytu/zapisu pozycji wznowienia.
            --retry-errors: ponow firmy odlozone po bledzie zapytania UUG.
            GEO_PORTAL_SQL_CONNECTION: opcjonalny connection string.
            Ctrl+C: zakoncz i zachowaj ostatnia zakonczona pozycje.
            """);
        return 0;
    }

    if (args[0] == "self-test") { await SelfTests.RunAsync(); return 0; }
    if (args[0] == "self-test-db") { await SelfTests.DatabaseAsync(cancellation.Token); return 0; }
    if (args[0] != "run") throw new ArgumentException("Nieznana komenda. Uzyj --help.");

    var options = Options.Parse(args.Skip(1).ToArray());
    var config = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "appsettings.json"), cancellation.Token))
        ?? throw new InvalidDataException("Brak konfiguracji.");
    var connectionString = Environment.GetEnvironmentVariable("GEO_PORTAL_SQL_CONNECTION") ?? config.ConnectionString;
    var connection = new SqlConnectionStringBuilder(connectionString);
    var delay = options.DelayMilliseconds ?? config.DelayMilliseconds;
    if (delay < 100 || config.CacheDays < 1) throw new ArgumentException("DelayMilliseconds >= 100, CacheDays >= 1.");
    var dataDirectory = Path.GetFullPath(options.DataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data"));
    Directory.CreateDirectory(dataDirectory);
    // An exclusive file handle prevents concurrent local runs from corrupting progress.
    FileStream runHandle;
    try
    {
        runHandle = new FileStream(Path.Combine(dataDirectory, "run.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
    {
        Console.Error.WriteLine("Inny importer korzysta juz z tego katalogu danych: " + dataDirectory);
        Console.Error.WriteLine("Poczekaj na jego zakonczenie albo zatrzymaj go przez Ctrl+C w jego terminalu.");
        Console.Error.WriteLine("Nie usuwaj run.lock podczas dzialania importera. Nowy import nie zostal rozpoczety.");
        return 2;
    }
    using var runLock = runHandle;
    log = new RunLog(Path.Combine(dataDirectory, "logs"));
    log.Write("SQL", null, $"Serwer={connection.DataSource}; baza={connection.InitialCatalog}; tryb={(options.Apply ? "ZAPIS" : "RAPORT")}.");
    var scope = Text.Hash(connection.DataSource.ToUpperInvariant() + "|" + connection.InitialCatalog.ToUpperInvariant());
    var checkpoint = Path.Combine(dataDirectory, $"progress-{scope}-{(options.Apply ? "apply" : "report")}.json");
    var afterId = options.AfterId ?? 0;
    if (!options.RetryErrors && options.CompanyId == null && !options.Restart && options.AfterId == null && File.Exists(checkpoint))
    {
        var progress = JsonSerializer.Deserialize<Progress>(await File.ReadAllTextAsync(checkpoint, cancellation.Token))
            ?? throw new InvalidDataException("Nieprawidlowy checkpoint; uzyj --restart.");
        afterId = progress.LastCompanyId;
    }
    var repository = new CompanyRepository(connectionString);
    var deferredPath = Path.Combine(dataDirectory, $"deferred-{scope}-{(options.Apply ? "apply" : "report")}.json");
    var queue = await DeferredQueue.OpenAsync(deferredPath, cancellation.Token);
    var retryIds = options.RetryErrors ? queue.Ids : null;
    var retryPosition = 0;
    log.Write("START", null, $"Kandydaci={await repository.CountAsync(cancellation.Token):N0}; po Id={afterId}; limit={options.Limit}.");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("GeoPortalAddressImporter/1.0");
    var geoportal = new GeoportalClient(http, Path.Combine(dataDirectory, "cache"), delay, config.CacheDays, log);
    var processed = 0;
    var matched = 0;
    var saved = 0;
    var deferred = 0;
    var consecutiveQueryErrors = 0;
    while (processed < options.Limit)
    {
        IReadOnlyList<Company> companies;
        if (retryIds != null)
        {
            companies = Array.Empty<Company>();
            while (companies.Count == 0 && retryPosition < retryIds.Count)
            {
                var id = retryIds[retryPosition++];
                companies = await repository.ReadAsync(0, 1, id, cancellation.Token);
                if (companies.Count == 0)
                {
                    await queue.RemoveAsync(id, cancellation.Token);
                    log.Write("RETRY", id, "Firma nie kwalifikuje sie juz do importu; usunieto z listy.");
                }
            }
        }
        else companies = await repository.ReadAsync(afterId, Math.Min(200, options.Limit - processed), options.CompanyId, cancellation.Token);
        if (companies.Count == 0) break;
        foreach (var company in companies)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            MatchResult? result = null;
            try { result = await geoportal.FindAsync(company, cancellation.Token); }
            catch (GeoportalQueryException ex)
            {
                await queue.RecordAsync(company, ex.Message, cancellation.Token);
                deferred++;
                consecutiveQueryErrors++;
                log.Write("DEFER", company.Id, $"Blad zapytania UUG; odlozono do ponowienia w {deferredPath}. Dane SQL bez zmian.");
                if (consecutiveQueryErrors >= 5)
                    throw new IOException("Piec kolejnych zapytan UUG zakonczylo sie bledem. Import zatrzymany; firmy zachowane do ponowienia.", ex);
            }
            if (result != null) consecutiveQueryErrors = 0;
            if (result?.Address is { } address)
            {
                matched++;
                var (latitude, longitude) = Coordinates.Convert(address.X, address.Y);
                if (options.Apply)
                {
                    var message = await repository.SaveAsync(company, address, cancellation.Token);
                    if (message.StartsWith("Zapisano", StringComparison.Ordinal)) saved++;
                    log.Write("SAVE", company.Id, message);
                }
                else
                    log.Write("MATCH", company.Id, $"{company.City}, {address.Street} {address.Number}; powiat={address.County}; gmina={address.Municipality}; GPS={latitude:F6},{longitude:F6}.");
            }
            else if (result != null) log.Write("SKIP", company.Id, result.Reason);
            if (result != null) await queue.RemoveAsync(company.Id, cancellation.Token);

            processed++;
            afterId = company.Id;
            if (!options.RetryErrors && options.CompanyId == null)
                await Files.AtomicAsync(checkpoint, new Progress(afterId), cancellation.Token);
            if (processed % 100 == 0)
                log.Write("PROGRESS", null, $"Sprawdzono={processed:N0}; dopasowano={matched:N0}; zapisano={saved:N0}; HTTP={geoportal.Requests:N0}; cache={geoportal.CacheHits:N0}.");
        }
    }
    log.Write("SUMMARY", null, $"Sprawdzono={processed:N0}; dopasowano={matched:N0}; zapisano={saved:N0}; odlozono={deferred:N0}; lista do ponowienia={queue.Count:N0}; HTTP={geoportal.Requests:N0}; cache={geoportal.CacheHits:N0}; ostatnie Id={afterId}.");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    log?.Write("STOP", null, "Przerwano. Niezakonczona firma zostanie ponowiona; zapisane firmy nie beda dublowane.");
    return 130;
}
catch (Exception exception)
{
    if (log != null) log.Write("ERROR", null, exception.ToString());
    else Console.Error.WriteLine(exception);
    Console.Error.WriteLine("Import zatrzymany. Nieudana firma nie przesunela pozycji wznowienia.");
    return 1;
}
finally { log?.Dispose(); }

internal sealed record Settings(string ConnectionString, int DelayMilliseconds, int CacheDays);

internal sealed class Options
{
    public bool Apply { get; private set; }
    public bool Restart { get; private set; }
    public bool RetryErrors { get; private set; }
    public int Limit { get; private set; } = 10;
    public int? CompanyId { get; private set; }
    public int? AfterId { get; private set; }
    public int? DelayMilliseconds { get; private set; }
    public string? DataDirectory { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!seen.Add(key)) throw new ArgumentException("Powtorzony argument: " + key);
            string Value()
            {
                if (++i >= args.Length) throw new ArgumentException("Brak wartosci: " + key);
                return args[i];
            }
            int Number(bool zero = false)
            {
                if (!int.TryParse(Value(), out var number) || number < (zero ? 0 : 1))
                    throw new ArgumentException("Nieprawidlowa liczba: " + key);
                return number;
            }
            switch (key)
            {
                case "--apply": options.Apply = true; break;
                case "--restart": options.Restart = true; break;
                case "--retry-errors": options.RetryErrors = true; break;
                case "--all": options.Limit = int.MaxValue; break;
                case "--limit": options.Limit = Number(); break;
                case "--company-id": options.CompanyId = Number(); options.Limit = 1; break;
                case "--after-id": options.AfterId = Number(true); break;
                case "--delay-ms": options.DelayMilliseconds = Number(); break;
                case "--data-dir": options.DataDirectory = Value(); break;
                default: throw new ArgumentException("Nieznany argument: " + key);
            }
        }
        if (seen.Contains("--limit") && seen.Contains("--all"))
            throw new ArgumentException("Wybierz --limit lub --all.");
        if (options.CompanyId != null && (seen.Contains("--limit") || seen.Contains("--all") || options.AfterId != null))
            throw new ArgumentException("--company-id nie laczy sie z --limit, --all lub --after-id.");
        if (options.RetryErrors && (options.CompanyId != null || options.AfterId != null || options.Restart))
            throw new ArgumentException("--retry-errors nie laczy sie z --company-id, --after-id ani --restart.");
        return options;
    }
}
