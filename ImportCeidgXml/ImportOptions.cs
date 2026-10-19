namespace ImportCeidgXml;

public sealed record ImportOptions(string Folder, bool Apply, int? Limit, string ConnectionString)
{
    public static ImportOptions Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Usage: ImportCeidgXml <XML-folder> [--apply] [--limit N]");
        var apply = false;
        int? limit = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--apply") apply = true;
            else if (args[i] == "--limit" && ++i < args.Length
                && int.TryParse(args[i], out var value) && value > 0) limit = value;
            else throw new ArgumentException("Unknown/invalid argument. Use <XML-folder> [--apply] [--limit N].");
        }
        var folder = Path.GetFullPath(args[0]);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        return new(folder, apply, limit,
            Environment.GetEnvironmentVariable("CEIDG_XML_CONNECTION_STRING")
            ?? "Server=localhost;Database=Firmy_temp;Integrated Security=True;TrustServerCertificate=True;");
    }
}
