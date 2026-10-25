using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ImportCeidgXml;

public sealed class ImportCheckpointStore
{
    private readonly string folder;
    private readonly string databaseIdentity;
    public string FilePath { get; }

    public ImportCheckpointStore(string sourceFolder, string connectionString)
    {
        folder = Path.GetFullPath(sourceFolder);
        var connection = new SqlConnectionStringBuilder(connectionString);
        databaseIdentity = connection.DataSource.Trim().ToUpperInvariant() + "|"
            + connection.InitialCatalog.Trim().ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(databaseIdentity)))[..16];
        FilePath = Path.Combine(folder, $".ceidg-full-import-{key}.checkpoint.json");
    }

    public async Task<ImportCheckpoint> LoadOrCreateAsync(string[] files, bool restart, CancellationToken token)
    {
        var fingerprint = Fingerprint(files);
        if (restart || !File.Exists(FilePath))
            return new ImportCheckpoint
            {
                SourceFolder = folder,
                DatabaseIdentity = databaseIdentity,
                FilesFingerprint = fingerprint,
                FilePath = files[0]
            };

        ImportCheckpoint checkpoint;
        try
        {
            await using var stream = File.OpenRead(FilePath);
            checkpoint = await JsonSerializer.DeserializeAsync<ImportCheckpoint>(stream, cancellationToken: token)
                ?? throw new InvalidDataException("Empty import checkpoint. Use --restart to start again.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid import checkpoint. Use --restart to start again.", ex);
        }

        var fileIndex = Array.FindIndex(files, file => string.Equals(file, checkpoint.FilePath, StringComparison.OrdinalIgnoreCase));
        if (checkpoint.Version != 1
            || !string.Equals(checkpoint.SourceFolder, folder, StringComparison.OrdinalIgnoreCase)
            || checkpoint.DatabaseIdentity != databaseIdentity || checkpoint.FilesFingerprint != fingerprint
            || fileIndex < 0 || checkpoint.RecordsCommitted < 0
            || (checkpoint.Completed && (!checkpoint.FileCompleted || fileIndex != files.Length - 1)))
            throw new InvalidDataException("Checkpoint does not match the input XML files or database. Use --restart to start again.");
        return checkpoint;
    }

    public async Task SaveAsync(ImportCheckpoint checkpoint, CancellationToken token)
    {
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, cancellationToken: token);
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string Fingerprint(string[] files)
    {
        var metadata = files.Select(path =>
        {
            var file = new FileInfo(path);
            return new { Path = Path.GetFullPath(path), file.Length, ModifiedUtc = file.LastWriteTimeUtc.Ticks };
        });
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(metadata)));
    }
}
