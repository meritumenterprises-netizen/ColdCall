using ImportCeidgXml;
using System.Diagnostics;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    Stopwatch sw = Stopwatch.StartNew();
    var options = ImportOptions.Parse(args);
    using var log = new ImportLog();
    await using var repository = new CompanyRepository(options.ConnectionString);
    var importer = new CompanyImportService(new CeidgXmlReader(), repository, log);
    await importer.ImportAsync(options, cancellation.Token);
    sw.Stop();
    Console.WriteLine($"Processed in {sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")}");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Anulowano. Zatwierdzone partie pozostaja w bazie.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
