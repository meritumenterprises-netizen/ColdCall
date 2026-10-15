using PrgAddressImporter;

using var log = new ImportLog();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var options = Options.Parse(args);
    if (options.Command == "help")
    {
        Console.WriteLine("""
            PRG Address Importer | Firmy_temp

            validate             Sprawdzenie ZIP i przykładowych adresów, bez SQL.
            import               Import TERC i PRG do tabel dbo, bez zmian firm.
            match                Raport dopasowania firm, bez zapisu.
            match --apply        Zapis jednoznacznych adresów i relacji firm.
            all --apply          Import słowników oraz zapis adresów firm.
            self-test            Testy parsera, normalizacji i transformacji.
            integration-test     Test w osobnej, tymczasowej bazie SQL Server.

            Opcje: --prg PATH --terc PATH --batch 10000 --limit 1000
            validate --full sprawdza wszystkie punkty zamiast próbki.
            --limit ogranicza firmy w match, nigdy import punktów PRG.
            Połączenie: zmienna PRG_SQL_CONNECTION, domyślnie localhost/Firmy_temp.
            Domyślne ZIP-y: Downloads bieżącego użytkownika.
            Logi: katalog logs obok aplikacji.
            """);
        return 0;
    }
    if (options.Command == "self-test")
    {
        SelfTests.Run();
        log.Write("TEST", "Wszystkie testy zakończone poprawnie.");
        return 0;
    }
    if (options.Command == "integration-test")
    {
        await IntegrationTests.RunAsync(options.ConnectionString, log, cancellation.Token);
        return 0;
    }

    var parser = new SourceReader(log);
    if (options.Command == "validate")
    {
        var terc = parser.ReadTerc(options.TercPath).ToArray();
        log.Write("TERC", $"Jednostki: {terc.Length:N0}; stan danych: {terc.Max(x => x.State):yyyy-MM-dd}.");
        var points = parser.ReadPoints(options.PrgPath, cancellation.Token, firstEntryOnly: !options.FullValidation);
        long count = 0, streets = 0;
        var codes = AdministrativeNames.Build(terc).Keys.ToHashSet();
        var missingCodes = new HashSet<string>();
        foreach (var point in options.FullValidation ? points : points.Take(10))
        {
            PointValidation.ValidateLengths(point);
            if (count < 10 || point.Street.Length > 0 && streets < 2)
                log.Write("PRG", $"{point.City}, {point.Street} {point.Number}; TERC={point.Terc}; GPS={point.Latitude:F7}, {point.Longitude:F7}");
            count++;
            if (point.Street.Length > 0) streets++;
            if (!codes.Contains(point.Terc)) missingCodes.Add(point.Terc);
        }
        log.Write("CHECK", $"Sprawdzono {count:N0} punktów, w tym {streets:N0} z ulicą; brakujące kody TERC: {string.Join(", ", missingCodes)}.");
        if (missingCodes.Count > 0) throw new InvalidDataException("Słownik TERC nie pokrywa wszystkich sprawdzonych punktów PRG.");
        return 0;
    }

    await using var database = new ImportDatabase(options, log);
    await database.OpenAsync(cancellation.Token);
    if (options.Command is "import" or "all")
        await database.ImportAsync(parser, cancellation.Token);
    if (options.Command is "match" or "all")
        await database.MatchAsync(cancellation.Token);
    return 0;
}
catch (OperationCanceledException)
{
    log.Write("STOP", "Przerwano. Zatwierdzone partie pozostają w bazie; import można wznowić.");
    return 130;
}
catch (Exception exception)
{
    log.Write("ERROR", exception.ToString());
    return 1;
}
