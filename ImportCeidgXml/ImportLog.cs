namespace ImportCeidgXml;

public sealed class ImportLog : IDisposable
{
    private readonly StreamWriter writer;
    public ImportLog()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"ceidg-xml-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        writer = new StreamWriter(path) { AutoFlush = true };
        Write($"Log: {path}");
    }
    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
        Console.WriteLine(line);
        writer.WriteLine(line);
    }
    public void Dispose() => writer.Dispose();
}
