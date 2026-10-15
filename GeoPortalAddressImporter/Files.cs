using System.Text;
using System.Text.Json;

namespace GeoPortalAddressImporter;

internal static class Files
{
    public static async Task AtomicAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), Encoding.UTF8, token);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal sealed class RunLog : IDisposable
{
    private readonly StreamWriter writer;
    public string Path { get; }
    public RunLog(string directory)
    {
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"geoportal-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        writer = new StreamWriter(Path, false, new UTF8Encoding(false)) { AutoFlush = true };
        Write("LOG", null, Path);
    }
    public void Write(string stage, int? companyId, string message)
    {
        var clean = message.Replace("\r", " ").Replace("\n", " ");
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {stage,-8} | {(companyId is null ? "-" : companyId.Value),8} | {clean}";
        Console.WriteLine(line);
        writer.WriteLine(line);
    }
    public void Dispose() => writer.Dispose();
}

internal sealed record Progress(int LastCompanyId);
