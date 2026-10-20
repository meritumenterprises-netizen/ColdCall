using System.Text.Json;

namespace GeoPortalAddressImporter;

internal static class FileWriteTests
{
    public static async Task RunAsync(string directory)
    {
        var path = Path.Combine(directory, "checkpoint-write-test.json");
        await Files.AtomicAsync(path, new Progress(1), CancellationToken.None);
        if (OperatingSystem.IsWindows())
        {
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var write = Files.AtomicAsync(path, new Progress(2), CancellationToken.None);
                await Task.Delay(150);
                Check(!write.IsCompleted, "Zablokowany plik musi czekac na ponowienie");
                held.Dispose();
                await write;
            }
            Check((await ReadAsync(path)).LastCompanyId == 2, "Ponowienie zapisuje nowy checkpoint");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var cancellation = new CancellationTokenSource(100))
            {
                var cancelled = false;
                try { await Files.AtomicAsync(path, new Progress(3), cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Mozna anulowac oczekiwanie na blokade pliku");
            }
            Check((await ReadAsync(path)).LastCompanyId == 2, "Anulowanie zachowuje poprzedni checkpoint");
        }
        await Files.AtomicAsync(path, new Progress(4), CancellationToken.None);
        Check((await ReadAsync(path)).LastCompanyId == 4, "Zastepowanie istniejacego checkpointu");
        Check(Directory.GetFiles(directory, "checkpoint-write-test.json.*.tmp").Length == 0, "Sprzatanie plikow tymczasowych");
        Console.WriteLine("PASS: atomowy zapis checkpointu, ponowienie blokady i anulowanie bez utraty pliku.");
    }

    private static async Task<Progress> ReadAsync(string path) =>
        JsonSerializer.Deserialize<Progress>(await File.ReadAllTextAsync(path))!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
