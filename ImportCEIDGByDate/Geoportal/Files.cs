using System.Text;
using System.Text.Json;

namespace ImportCEIDGByDate.Geoportal;

internal static class Files
{
    public static async Task AtomicAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), Encoding.UTF8, token);
            for (var attempt = 1; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporary, path, true);
                    break;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException
                    || ex is IOException && (ex.HResult & 0xFFFF) is 32 or 33)
                {
                    if (attempt >= 8)
                        throw new IOException($"Nie mozna atomowo zapisac pliku '{path}' po {attempt} probach. "
                            + "Sprawdz uprawnienia katalogu, atrybut tylko do odczytu i blokady innych programow. "
                            + "Nie usuwano poprzedniego pliku.", ex);
                    Console.Error.WriteLine($"FILE | Zapis '{path}' zablokowany; ponowienie {attempt}/8.");
                    await Task.Delay(Math.Min(2000, 250 * (1 << (attempt - 1))), token);
                }
            }
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"FILE | Nie usunieto pliku tymczasowego '{temporary}': {ex.Message}");
            }
        }
    }
}

internal sealed class RunLog : IDisposable
{
    private readonly StreamWriter writer;
    private readonly object sync = new();
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
        lock (sync)
        {
            Console.WriteLine(line);
            writer.WriteLine(line);
        }
    }
    public void Dispose() => writer.Dispose();
}

internal sealed record Progress(int LastCompanyId);
