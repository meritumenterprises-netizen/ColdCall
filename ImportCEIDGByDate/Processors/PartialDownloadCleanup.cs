namespace ImportCEIDGByDate;

internal static class PartialDownloadCleanup
{
    public static void ForFolder(string targetFolder)
    {
        if (!Directory.Exists(targetFolder)) return;
        foreach (var path in Directory.EnumerateFiles(targetFolder, "*.part", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith("Zarejestrowane działalności - ", StringComparison.OrdinalIgnoreCase))
                continue;
            var marker = name.LastIndexOf(".zip.", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) continue;
            var suffix = name[(marker + 5)..];
            if (suffix.Equals("part", StringComparison.OrdinalIgnoreCase)
                || (suffix.Length == 37 && Guid.TryParseExact(suffix[..32], "N", out _)))
                DeleteIfUnlocked(path);
        }
    }

    public static void ForReport(CeidgReport report, string targetFolder)
    {
        if (!Directory.Exists(targetFolder)) return;
        var prefixes = new[] { report.FileNameZip + ".", report.FileName + ".zip." };
        foreach (var path in Directory.EnumerateFiles(targetFolder, "*.part", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (!prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (name.Length == prefix.Length + 4
                    || (name.Length == prefix.Length + 32 + 5
                        && Guid.TryParseExact(name.Substring(prefix.Length, 32), "N", out _)))))
                continue;
            DeleteIfUnlocked(path);
        }
    }

    private static void DeleteIfUnlocked(string path)
    {
            var name = Path.GetFileName(path);
            try
            {
                // Exclusive access prevents deleting another running download's partial file.
                using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (FileNotFoundException) { }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Could not remove partial download '{name}' (it may be in use): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Could not remove partial download '{name}': {ex.Message}");
            }
    }
}
