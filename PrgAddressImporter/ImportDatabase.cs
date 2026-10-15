using Microsoft.Data.SqlClient;
using System.Data;
using System.Reflection;
using System.Security.Cryptography;

namespace PrgAddressImporter;

internal sealed class ImportDatabase(Options options, ImportLog log) : IAsyncDisposable
{
    private readonly SqlConnection connection = new(options.ConnectionString);

    public async Task OpenAsync(CancellationToken token)
    {
        await connection.OpenAsync(token);
        await ExecuteAsync("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource='PrgAddressImporter',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0;
            IF @result<0 THROW 50003,'Another PRG importer is running.',1;
            """, token);
        log.Write("SQL", $"Serwer: {connection.DataSource}; baza: {connection.Database}.");
    }

    public async Task ImportAsync(SourceReader source, CancellationToken token)
    {
        var snapshot = await FingerprintAsync(token);
        await ExecuteAsync(Resource("Schema"), token);
        await using (var completed = Command("SELECT Snapshot FROM dbo.PrgImporterState WHERE Id=1 AND Ready=1"))
        {
            if (await completed.ExecuteScalarAsync(token) as string == snapshot)
            {
                log.Write("IMPORT", "Ten sam kompletny zbiór PRG/TERC jest już w bazie; pomijam ponowny import.");
                return;
            }
        }
        await using (var command = Command("UPDATE dbo.PrgImporterState SET Ready=0,Snapshot=@snapshot,CompletedOn=NULL WHERE Id=1"))
        {
            command.Parameters.Add("@snapshot", SqlDbType.VarChar, 64).Value = snapshot;
            await command.ExecuteNonQueryAsync(token);
        }
        var terc = source.ReadTerc(options.TercPath).ToArray();
        if (terc.Length == 0 || terc.Select(x => x.Code).Distinct().Count() != terc.Length)
            throw new InvalidDataException("Pusty słownik TERC lub powtarzające się kody.");
        var names = AdministrativeNames.Build(terc);
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token))
        {
            await using var delete = Command("DELETE dbo.Terc", transaction);
            await delete.ExecuteNonQueryAsync(token);
            using var table = new DataTable();
            foreach (var name in new[] { "Code", "Woj", "Pow", "Gmi", "Rodz", "Nazwa", "NazDod" }) table.Columns.Add(name, typeof(string));
            table.Columns.Add("StanNa", typeof(DateTime));
            foreach (var row in terc) table.Rows.Add(row.Code, row.Woj, row.Pow, row.Gmi, row.Rodz, row.Name, row.AdditionalName, row.State);
            await BulkAsync(table, "dbo.Terc", transaction, token);
            await transaction.CommitAsync(token);
        }
        log.Write("TERC", $"Zaimportowano {terc.Length:N0} jednostek; stan danych {terc.Max(x => x.State):yyyy-MM-dd}.");
        await ExecuteAsync("SELECT TOP(0) SourceId,GmlId,Snapshot,Terc,Simc,Miejscowosc,Ulic,Ulica,NrBudynku,KodPocztowy,X,Y,Srid,Latitude,Longitude,CityNorm,StreetNorm,NumberNorm,MatchKey INTO #PrgBatch FROM dbo.PrgAdresy; CREATE UNIQUE INDEX IX_Batch_Source ON #PrgBatch(SourceId);", token);
        using var batch = PointTable();
        long total = 0;
        foreach (var point in source.ReadPoints(options.PrgPath, token))
        {
            var city = Normalization.Text(point.City);
            var street = Normalization.Street(point.Street);
            var number = Normalization.Number(point.Number);
            if (city.Length == 0 || number.Length == 0) throw new InvalidDataException($"Brak miasta/numeru: {point.GmlId}");
            PointValidation.ValidateLengths(point);
            batch.Rows.Add(point.SourceId, point.GmlId, snapshot, point.Terc, point.Simc, point.City, point.Ulic,
                point.Street, point.Number, point.PostalCode, point.X, point.Y, point.Srid,
                point.Latitude, point.Longitude, city, street, number, Normalization.Key(city, street, number));
            if (batch.Rows.Count >= options.BatchSize) await FlushAsync();
        }
        if (batch.Rows.Count > 0) await FlushAsync();
        if (total == 0) throw new InvalidDataException("PRG nie zawiera punktów adresowych.");

        // Administrative names and normalization are sourced from TERC, never the company's commune.
        using var admins = new DataTable();
        admins.Columns.Add("Terc", typeof(string));
        foreach (var name in new[] { "Wojewodztwo", "Powiat", "Gmina", "WojNorm", "PowNorm" }) admins.Columns.Add(name, typeof(string));
        foreach (var row in names.Values)
        {
            admins.Rows.Add(row.Code, row.Province, row.County, row.Commune, Normalization.Text(row.Province), Normalization.Text(row.County));
        }
        await ExecuteAsync("CREATE TABLE #Admins(Terc varchar(7) PRIMARY KEY,Wojewodztwo nvarchar(200),Powiat nvarchar(200),Gmina nvarchar(200),WojNorm nvarchar(200),PowNorm nvarchar(200));", token);
        await BulkAsync(admins, "#Admins", null, token);
        await using (var command = Command("""
            UPDATE P SET Wojewodztwo=A.Wojewodztwo,Powiat=A.Powiat,Gmina=A.Gmina,WojNorm=A.WojNorm,PowNorm=A.PowNorm
            FROM dbo.PrgAdresy P JOIN #Admins A ON A.Terc=P.Terc WHERE P.Snapshot=@snapshot;
            IF EXISTS(SELECT 1 FROM dbo.PrgAdresy WHERE Snapshot=@snapshot AND Gmina IS NULL)
                THROW 50004,'PRG contains TERC codes absent from the dictionary; snapshot remains incomplete.',1;
            UPDATE dbo.PrgImporterState SET Ready=1,CompletedOn=SYSUTCDATETIME() WHERE Id=1;
            DROP TABLE #PrgBatch; DROP TABLE #Admins;
            """))
        {
            command.Parameters.Add("@snapshot", SqlDbType.VarChar, 64).Value = snapshot;
            await command.ExecuteNonQueryAsync(token);
        }
        log.Write("IMPORT", $"Gotowy zbiór: {total:N0} punktów. Nie zmieniono tabel firm.");

        async Task FlushAsync()
        {
            await BulkAsync(batch, "#PrgBatch", null, token);
            await ExecuteAsync(Resource("Upsert"), token);
            total += batch.Rows.Count;
            log.Write("IMPORT", $"Zapisano partię {batch.Rows.Count:N0}; razem {total:N0}.");
            batch.Clear();
        }
    }

    public async Task MatchAsync(CancellationToken token)
    {
        await using var state = Command("SELECT Snapshot FROM dbo.PrgImporterState WHERE Id=1 AND Ready=1 AND SchemaVersion IN (1,2)");
        var snapshot = await state.ExecuteScalarAsync(token) as string ?? throw new InvalidOperationException("Najpierw ukończ import PRG i TERC.");
        log.Write("MATCH", options.Apply ? "Tryb ZAPISU jednoznacznych dopasowań." : "Tryb RAPORTU. Tabele ColdCall nie będą zmienione.");
        var lastId = 0;
        long scanned = 0, unique = 0, ambiguous = 0, absent = 0, applied = 0;
        long invalidAddress = 0, noPrgMatch = 0;
        while (!options.CompanyLimit.HasValue || scanned < options.CompanyLimit)
        {
            var size = (int)Math.Min(options.BatchSize, options.CompanyLimit.HasValue ? options.CompanyLimit.Value - scanned : options.BatchSize);
            using var candidates = CandidateTable();
            var fetched = 0;
            await using (var command = Command("""
                SELECT TOP(@size) C.Id,M.Name,C.Street,C.BuildingNumber,W.Name,P.Name,
                    C.MiejscowoscId,C.WojewodztwoId,C.PowiatId
                FROM ColdCall.Companies C
                LEFT JOIN ColdCall.Miejscowosci M ON M.Id=C.MiejscowoscId
                LEFT JOIN ColdCall.Wojewodztwa W ON W.Id=C.WojewodztwoId
                LEFT JOIN ColdCall.Powiaty P ON P.Id=C.PowiatId
                WHERE C.Id>@lastId AND C.AddressIsMissing=1 AND C.BlockedByRODO=0
                    AND NOT EXISTS(SELECT 1 FROM ColdCall.CompanyAddress CA WHERE CA.CompanyId=C.Id)
                ORDER BY C.Id;
                """))
            {
                command.Parameters.Add("@size", SqlDbType.Int).Value = size;
                command.Parameters.Add("@lastId", SqlDbType.Int).Value = lastId;
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    lastId = reader.GetInt32(0);
                    fetched++;
                    scanned++;
                    string Text(int ordinal) => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
                    var city = Normalization.Text(Text(1));
                    var street = Normalization.Street(Text(2));
                    var number = Normalization.Number(Text(3));
                    var reason = city.Length == 0 ? "brak miejscowości" :
                        number is "" or "-" ? "brak numeru budynku" :
                        city.Length > 200 ? "miejscowość przekracza 200 znaków" :
                        street.Length > 300 ? "ulica przekracza 300 znaków" :
                        number.Length > 50 ? "numer budynku przekracza 50 znaków" : null;
                    if (reason is not null)
                    {
                        absent++;
                        invalidAddress++;
                        log.Write("SKIP", $"CompanyId={lastId}: {reason}.");
                        continue;
                    }
                    candidates.Rows.Add(lastId, Normalization.Key(city, street, number), city, street, number,
                        Normalization.Province(Text(4)), Normalization.County(Text(5)), Text(2), Text(3),
                        reader.IsDBNull(6) ? -1 : reader.GetInt32(6), reader.IsDBNull(7) ? -1 : reader.GetInt32(7), reader.IsDBNull(8) ? -1 : reader.GetInt32(8));
                }
            }
            if (fetched == 0) break;
            if (candidates.Rows.Count == 0)
            {
                // No candidate can be matched, but keyset scanning must continue past invalid rows.
                continue;
            }
            await ExecuteAsync("""
                CREATE TABLE #Candidates(CompanyId int PRIMARY KEY,MatchKey binary(32),CityNorm nvarchar(200),
                    StreetNorm nvarchar(300),NumberNorm nvarchar(50),WojNorm nvarchar(250),PowNorm nvarchar(250),
                    OriginalStreet nvarchar(max),OriginalNumber nvarchar(max),CityId int,WojId int,PowId int);
                CREATE TABLE #Matches(CompanyId int PRIMARY KEY,MatchCount bigint NOT NULL,PrgId bigint NULL);
                """, token);
            await BulkAsync(candidates, "#Candidates", null, token);
            await using (var command = Command(Resource("FindMatches")))
            {
                command.Parameters.Add("@snapshot", SqlDbType.VarChar, 64).Value = snapshot;
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    var count = reader.GetInt64(1);
                    if (count == 1) unique++; else if (count == 0) absent++; else ambiguous++;
                    if (count == 0) noPrgMatch++;
                    log.Write(count == 1 ? "MATCH" : "SKIP", $"CompanyId={reader.GetInt32(0)}; liczba punktów PRG={count}; PrgId={(reader.IsDBNull(2) ? "-" : reader.GetInt64(2))}.");
                }
            }
            if (options.Apply)
            {
                await using var command = Command(Resource("ApplyMatches"));
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    if (reader.GetBoolean(1))
                    {
                        applied++;
                        log.Write("SAVE", $"CompanyId={reader.GetInt32(0)}: adres i relacja zapisane; AddressIsMissing=0.");
                    }
                    else
                        log.Write("SKIP", $"CompanyId={reader.GetInt32(0)}: zmiana danych firmy, ograniczenie długości lub niezgodny wcześniejszy adres.");
                }
            }
            await ExecuteAsync("DROP TABLE #Candidates; DROP TABLE #Matches;", token);
        }
        log.Write("SUMMARY", $"Sprawdzono={scanned:N0}; jednoznaczne={unique:N0}; brak={absent:N0}; niejednoznaczne={ambiguous:N0}; zapisano={applied:N0}.");
        log.Write("SUMMARY", $"Brak danych adresowych lub przekroczony limit={invalidAddress:N0}; kompletny adres bez dopasowania PRG={noPrgMatch:N0}.");
    }

    private async Task<string> FingerprintAsync(CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in new[] { options.PrgPath, options.TercPath })
        {
            await using var file = File.OpenRead(path);
            var digest = await SHA256.HashDataAsync(file, token);
            hash.AppendData(digest);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static DataTable PointTable()
    {
        var table = new DataTable();
        foreach (var name in new[] { "SourceId", "GmlId", "Snapshot", "Terc", "Simc", "Miejscowosc", "Ulic", "Ulica", "NrBudynku", "KodPocztowy" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("X", typeof(decimal)); table.Columns.Add("Y", typeof(decimal)); table.Columns.Add("Srid", typeof(int));
        table.Columns.Add("Latitude", typeof(decimal)); table.Columns.Add("Longitude", typeof(decimal));
        foreach (var name in new[] { "CityNorm", "StreetNorm", "NumberNorm" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("MatchKey", typeof(byte[]));
        return table;
    }

    private static DataTable CandidateTable()
    {
        var table = new DataTable();
        table.Columns.Add("CompanyId", typeof(int)); table.Columns.Add("MatchKey", typeof(byte[]));
        foreach (var name in new[] { "CityNorm", "StreetNorm", "NumberNorm", "WojNorm", "PowNorm", "OriginalStreet", "OriginalNumber" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "CityId", "WojId", "PowId" }) table.Columns.Add(name, typeof(int));
        return table;
    }

    private async Task BulkAsync(DataTable table, string destination, SqlTransaction? transaction, CancellationToken token)
    {
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
        { DestinationTableName = destination, BatchSize = options.BatchSize, BulkCopyTimeout = 600 };
        foreach (DataColumn column in table.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        await bulk.WriteToServerAsync(table, token);
    }

    private SqlCommand Command(string sql, SqlTransaction? transaction = null) => new(sql, connection, transaction) { CommandTimeout = 600 };
    private async Task ExecuteAsync(string sql, CancellationToken token)
    {
        await using var command = Command(sql);
        await command.ExecuteNonQueryAsync(token);
    }

    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"PrgAddressImporter.Sql.{name}.sql")
            ?? throw new InvalidOperationException($"Brak zasobu SQL: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
