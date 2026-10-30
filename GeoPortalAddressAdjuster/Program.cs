using System.Text;
using GeoPortalAddressAdjuster;
using GeoPortalAddressImporter;

Console.OutputEncoding = Encoding.UTF8;
if (args is ["--help"])
{
    Console.WriteLine("GeoPortalAddressAdjuster [--workers 32] [--delay-ms 0] [--cache-days 1]");
    Console.WriteLine("Writes to localhost/Firmy_temp. Override using GEO_PORTAL_SQL_CONNECTION.");
    return 0;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var token = cancellation.Token;
try
{
    var options = AdjusterOptions.Parse(args);
    var connectionString = Environment.GetEnvironmentVariable("GEO_PORTAL_SQL_CONNECTION")
        ?? "Server=localhost;Database=Firmy_temp;Integrated Security=True;TrustServerCertificate=True";
    var repository = new CompanyAddressRepository(connectionString);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("GeoPortalAddressAdjuster/1.0");
    using var geoportal = new GeoportalAddressService(http, Path.Combine(AppContext.BaseDirectory, "geoportal"),
        options.DelayMilliseconds, options.CacheDays, showCandidateDiagnostics: false);
    var total = await repository.CountAsync(token);
    Console.WriteLine($"Companies without addresses: {total}; workers={options.Workers}; delay={options.DelayMilliseconds}ms; cache={options.CacheDays} days.");
    await AddressPipeline.RunAsync(repository, geoportal, options, total, token);
    Console.WriteLine("Finished.");
    return 0;
}
catch (OperationCanceledException) when (token.IsCancellationRequested)
{
    Console.WriteLine("Cancelled. Saved links are retained; restart processes companies still without an address.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
