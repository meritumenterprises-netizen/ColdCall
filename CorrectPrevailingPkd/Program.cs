using CorrectPrevailingPkd;

var arguments = args.ToList();
if (arguments.Contains("--help"))
{
    Console.WriteLine("CorrectPrevailingPkd [--xml <file-or-directory>] [--connection <connection-string>] [--apply] [--batch-size <1-10000>]");
    Console.WriteLine("Defaults: all *.xml files in the xml folder beside the executable; dry run.");
    Console.WriteLine("Connection can be set via CORRECT_PREVAILING_PKD_CONNECTION_STRING.");
    return 0;
}

try
{
    var xml = Path.Combine(AppContext.BaseDirectory, "xml");
    var connection = Environment.GetEnvironmentVariable("CORRECT_PREVAILING_PKD_CONNECTION_STRING")
        ?? "Server=localhost;Database=Firmy_temp;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
    var apply = false;
    var batchSize = 1000;
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + args[i - 1]);
        switch (args[i])
        {
            case "--xml": xml = Next(); break;
            case "--connection": connection = Next(); break;
            case "--apply": apply = true; break;
            case "--batch-size":
                if (!int.TryParse(Next(), out batchSize) || batchSize < 1 || batchSize > 10000)
                    throw new ArgumentException("Batch size must be between 1 and 10000.");
                break;
            default: throw new ArgumentException("Unknown argument: " + args[i]);
        }
    }
    if (string.IsNullOrWhiteSpace(xml)) throw new ArgumentException("Specify --xml <file-or-directory>.");
    var files = Directory.Exists(xml) ? Directory.GetFiles(xml, "*.xml").Order(StringComparer.OrdinalIgnoreCase).ToArray()
        : File.Exists(xml) ? new[] { xml } : throw new FileNotFoundException("XML path not found.", xml);
    if (files.Length == 0) throw new ArgumentException("No XML files found.");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    await using var corrector = new PkdCorrector(connection, apply);
    await corrector.OpenAsync(cancellation.Token);
    long read = 0, skipped = 0, changed = 0;
    foreach (var file in files)
    {
        Console.WriteLine("XML: " + Path.GetFullPath(file));
        var batch = new List<Assignment>(batchSize);
        foreach (var detail in CeidgXml.ReadDetails(file,
            percent => Console.WriteLine($"Progress [{Path.GetFileName(file)}]: {percent}%")))
        {
            cancellation.Token.ThrowIfCancellationRequested();
            read++;
            var assignment = CeidgXml.Parse(detail);
            if (assignment == null) { skipped++; continue; }
            batch.Add(assignment);
            if (batch.Count < batchSize) continue;
            changed += await corrector.ProcessAsync(batch, cancellation.Token);
            batch.Clear();
        }
        if (batch.Count > 0) changed += await corrector.ProcessAsync(batch, cancellation.Token);
        Console.WriteLine($"Progress [{Path.GetFileName(file)}]: 100%");
    }
    Console.WriteLine($"Completed. Files: {files.Length}; XML companies: {read}; invalid/missing NIP or main PKD: {skipped}; {(apply ? "updated records" : "proposed updates (may repeat across batches/files)")}: {changed}");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled. Earlier committed batches remain applied; the current batch is rolled back.");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Failed: " + exception.Message);
    Console.Error.WriteLine("Earlier committed batches, if any, remain applied. It is safe to rerun the command.");
    return 1;
}
