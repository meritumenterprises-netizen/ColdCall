using System.Text;

namespace PrgAddressImporter;

internal sealed class ImportLog : IDisposable
{
    private readonly StreamWriter writer;

    public ImportLog()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var directory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"prg-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
        writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        Write("LOG", path);
    }

    public void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {level,-7} | {message}";
        Console.WriteLine(line);
        writer.WriteLine(line);
    }

    public void Dispose() => writer.Dispose();
}
