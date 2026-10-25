namespace ImportCeidgXml;

public sealed class ImportCheckpoint
{
    public int Version { get; set; } = 1;
    public string SourceFolder { get; set; } = "";
    public string DatabaseIdentity { get; set; } = "";
    public string FilesFingerprint { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long RecordsCommitted { get; set; }
    public bool FileCompleted { get; set; }
    public bool Completed { get; set; }
}
