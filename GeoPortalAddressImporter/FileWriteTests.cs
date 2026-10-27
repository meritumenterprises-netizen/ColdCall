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
        await BatchedCheckpointAsync(directory);
        Console.WriteLine("PASS: atomowy zapis checkpointu, ponowienie blokady i anulowanie bez utraty pliku.");
    }

    private static async Task BatchedCheckpointAsync(string directory)
    {
        var path = Path.Combine(directory, "batched-checkpoint.json");
        await using (var writer = new CheckpointWriter(path, 3, TimeSpan.FromMinutes(1)))
        {
            await writer.AdvanceAsync(10, CancellationToken.None);
            await writer.AdvanceAsync(20, CancellationToken.None);
            Check(!File.Exists(path), "Brak zapisu dyskowego dla kazdej firmy");
            await writer.AdvanceAsync(30, CancellationToken.None);
            Check((await ReadAsync(path)).LastCompanyId == 30, "Zapis po pelnej partii");
            await writer.AdvanceAsync(40, CancellationToken.None);
            Check((await ReadAsync(path)).LastCompanyId == 30, "Niepelna partia czeka");
        }
        Check((await ReadAsync(path)).LastCompanyId == 40, "Zamkniecie zapisuje niepelna partie");
        await using (var writer = new CheckpointWriter(path, 100, TimeSpan.Zero))
            await writer.AdvanceAsync(50, CancellationToken.None);
        Check((await ReadAsync(path)).LastCompanyId == 50, "Uplyw interwalu wymusza zapis");
        try
        {
            await using var writer = new CheckpointWriter(path, 100, TimeSpan.FromMinutes(1));
            await writer.AdvanceAsync(60, CancellationToken.None);
            throw new OperationCanceledException();
        }
        catch (OperationCanceledException) { }
        Check((await ReadAsync(path)).LastCompanyId == 60, "Anulowanie zachowuje ostatni zakonczony prefiks");
    }

    private static async Task<Progress> ReadAsync(string path) =>
        JsonSerializer.Deserialize<Progress>(await File.ReadAllTextAsync(path))!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
