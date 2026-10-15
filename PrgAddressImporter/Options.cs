namespace PrgAddressImporter;

internal sealed record Options(string Command, string PrgPath, string TercPath,
    string ConnectionString, int BatchSize, int? CompanyLimit, bool Apply, bool FullValidation = false)
{
    public static Options Parse(string[] args)
    {
        //var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var downloads = Path.GetFullPath(@"PrgAddressImporter\Downloads");
        var command = args.FirstOrDefault() ?? "help";
        var prg = Path.Combine(downloads, "PRG-punkty_adresowe.zip");
        var terc = Path.Combine(downloads, "TERC_Adresowy_2026-10-03.zip");
        var batch = 10000;
        int? limit = null;
        var apply = false;
        var fullValidation = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--apply") { apply = true; continue; }
            if (args[i] == "--full") { fullValidation = true; continue; }
            if (i + 1 == args.Length) throw new ArgumentException($"Brak wartości: {args[i]}");
            var option = args[i++];
            switch (option)
            {
                case "--prg": prg = Path.GetFullPath(args[i]); break;
                case "--terc": terc = Path.GetFullPath(args[i]); break;
                case "--batch": batch = int.Parse(args[i]); break;
                case "--limit": limit = int.Parse(args[i]); break;
                default: throw new ArgumentException($"Nieznana opcja: {option}");
            }
        }
        if (command is not ("help" or "validate" or "import" or "match" or "all" or "self-test" or "integration-test"))
            throw new ArgumentException("Nieznana komenda. Uruchom help.");
        if (batch is < 1 or > 50000 || limit is < 1) throw new ArgumentException("Nieprawidłowy rozmiar partii lub limit.");
        if (command == "all" && !apply) throw new ArgumentException("Komenda all wymaga --apply. Użyj import lub match do oddzielnych etapów.");
        return new(command, prg, terc,
            Environment.GetEnvironmentVariable("PRG_SQL_CONNECTION") ??
            "Server=localhost;Database=Firmy_temp;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
            batch, limit, apply, fullValidation);
    }
}
