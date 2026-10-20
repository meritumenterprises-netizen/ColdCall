using System.Globalization;
using System.Net.Http.Headers;
using Dapper;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO.Compression;
using System.Text.Json;
using System.Text;
using ImportCEIDGByDate;
using ImportCeidgXml;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

if (args.Length != 1 || !DateOnly.TryParseExact(args[0], "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
    || date < new DateOnly(2026, 9, 20))
{
    Console.Error.WriteLine("Usage: ImportCEIDGByDate YYYY-MM-DD (minimum: 2026-09-20).");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var statistics = new CEIDGBatchImportStatistics();
    statistics.ImportDate = DateTime.Parse(args[0]);

    var dateStarted = DateTime.Now;
    var dateFinished = DateTime.MinValue;

    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<CEIDGRaporty>(optional: true).Build();
    var bearerToken = Environment.GetEnvironmentVariable("CEIDG_API_TOKEN") ?? configuration["Ceidg:Token"];
    if (string.IsNullOrWhiteSpace(bearerToken))
        throw new InvalidOperationException("Set CEIDG_API_TOKEN or the user secret Ceidg:Token.");
    var connectionString = Environment.GetEnvironmentVariable("CEIDG_CONNECTION_STRING")
        ?? configuration["ConnectionStrings:Import"]
        ?? "Server=localhost;Database=Firmy_temp;Integrated Security=True;TrustServerCertificate=True;";

    int ceidgImportBatchId;
    await using (var batchConnection = new SqlConnection(connectionString))
    {
        await batchConnection.OpenAsync(cancellation.Token);
        ceidgImportBatchId = await CreateImportBatch(batchConnection, date, cancellation.Token);
    }
    Console.WriteLine($"Started import batch {ceidgImportBatchId} for {date:yyyy-MM-dd}.");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

    using var response = await http.GetAsync("https://dane.biznes.gov.pl/api/ceidg/v3/raporty",
        cancellation.Token);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
    var raporty = await JsonSerializer.DeserializeAsync<CEIDGRaporty>(stream,
        cancellationToken: cancellation.Token)
        ?? throw new InvalidDataException("Empty CEIDG reports response.");
    if (raporty.Raporty is null)
        throw new InvalidDataException("Missing raporty array in the CEIDG response.");

    var raportyCEIDG = new List<CEIDGRaport>();
    foreach (var raport in raporty.Raporty)
    {
        if (raport.Nazwa.StartsWith("Zarejestrowane działalności - ") && raport.Format == ".xml"
            && raport.DataUtworzenia.StartsWith(date.ToString("yyyy-MM-dd")))
        {
            raportyCEIDG.Add(raport);
        }
    }

    if (raportyCEIDG.Count() == 34) // reports may have double the reports, with different timestamp
    {
        // sort by date descending and taking the latest version
        raportyCEIDG = raportyCEIDG.OrderBy((r) => r.DataUtworzenia).Skip(17).Take(17).ToList();
    }



    var outputFolder = Path.Combine(AppContext.BaseDirectory, "output");
    var zipFolder = Path.Combine(outputFolder, "zip");
    Directory.CreateDirectory(zipFolder);

    var xmlFilePaths = new List<string>();

    var sw = Stopwatch.StartNew();

    foreach (var raport in raportyCEIDG)
    {
        cancellation.Token.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(raport.Raport, UriKind.Absolute, out var reportUrl)
            || reportUrl.Scheme != "https" || reportUrl.Host != "dane.biznes.gov.pl"
            || !reportUrl.IsDefaultPort || !string.IsNullOrEmpty(reportUrl.UserInfo)
            || !string.IsNullOrEmpty(reportUrl.Query) || !string.IsNullOrEmpty(reportUrl.Fragment)
            || string.IsNullOrEmpty(raport.Id)
            || !raport.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            || reportUrl.AbsolutePath != "/api/ceidg/v3/raport/" + raport.Id)
            throw new InvalidDataException("Invalid report URL; bearer token was not sent.");

        var filename = raport.Nazwa + raport.Format;
        if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Invalid report filename: {filename}");
        var zipPath = Path.Combine(zipFolder, raport.Id + ".zip");
        var xmlPath = Path.Combine(outputFolder, filename);
        var zipPartial = zipPath + ".part";
        var xmlPartial = xmlPath + ".part";
        try
        {
            Console.WriteLine($"Downloading: {raport.Nazwa}");
            using var request = new HttpRequestMessage(HttpMethod.Get, reportUrl);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            // The shared client includes the configured bearer token.
            using var reportResponse = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellation.Token);
            reportResponse.EnsureSuccessStatusCode();
            await using (var input = await reportResponse.Content.ReadAsStreamAsync(cancellation.Token))
            await using (var output = new FileStream(zipPartial, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
                await input.CopyToAsync(output, cancellation.Token);

            using (var validation = ZipFile.OpenRead(zipPartial)) { _ = validation.Entries.Count; }
            File.Move(zipPartial, zipPath, overwrite: true);
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Where(entry => entry.Name.Length > 0
                && entry.Name.EndsWith(raport.Format, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1)
                throw new InvalidDataException($"Report {raport.Id} must contain exactly one {raport.Format} file.");

            // Ignore the archived path and use the report's exact name and format.
            await using (var input = entries[0].Open())
            await using (var output = new FileStream(xmlPartial, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
                await input.CopyToAsync(output, cancellation.Token);
            File.Move(xmlPartial, xmlPath, overwrite: true);
            //Console.WriteLine($"Extracted: {xmlPath}");

            xmlFilePaths.Add(xmlPath);
            Console.WriteLine($"Discovered XML file {xmlPath}");

            try { File.Delete(zipPath); } catch { }
            //Console.WriteLine($"Deleted: {zipPath}");

            break; // TEMPORARY: only download the first report for testing
        }
        finally
        {
            if (File.Exists(zipPartial)) File.Delete(zipPartial);
            //if (File.Exists(xmlPartial)) File.Delete(xmlPartial);
        }
    }

    sw.Stop();
    Console.WriteLine("Downloaded XML files in " + sw.Elapsed.TotalSeconds + " second(s)");


    Console.WriteLine($"Processing XML files ({xmlFilePaths.Count} file(s))");

    foreach (var xmlPath in xmlFilePaths)
    {
        cancellation.Token.ThrowIfCancellationRequested();
        Console.WriteLine($"Importing: {xmlPath}");
        var companies = await ProcessXmlFile(xmlPath, connectionString, statistics, cancellation.Token);
        Console.WriteLine($"Parsed {companies.Count} companies from {xmlPath}.");
    }

    dateFinished = DateTime.Now;
    await using (var batchConnection = new SqlConnection(connectionString))
    {
        await batchConnection.OpenAsync(cancellation.Token);
        await UpdateImportBatch(batchConnection, ceidgImportBatchId, statistics, dateFinished, cancellation.Token);
    }

    Console.WriteLine($"Batch {ceidgImportBatchId}: downloaded and parsed {raporty.Raporty.Count} reports.");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed: {ex.Message}");
    return 1;
}

static async Task<int> CreateImportBatch(SqlConnection connection, DateOnly importDate,
    CancellationToken token, SqlTransaction? transaction = null)
{
    var command = new CommandDefinition("""
        INSERT ColdCall.CEIDGImportBatch (ImportDate)
        OUTPUT INSERTED.CEIDGImportBatchId
        VALUES (@importDate);
        """, new { importDate = importDate.ToDateTime(TimeOnly.MinValue) },
        transaction: transaction, cancellationToken: token);
    return await connection.QuerySingleAsync<int>(command);
}

static async Task UpdateImportBatch(SqlConnection connection, int ceidgImportBatchId,
    CEIDGBatchImportStatistics statistics, DateTime? endDate, CancellationToken token,
    SqlTransaction? transaction = null)
{
    var command = new CommandDefinition("""
        UPDATE ColdCall.CEIDGImportBatch
        SET CompaniesAdded=@CompaniesAdded,
            CompaniesUpdated=@CompaniesUpdated,
            CompaniesSkipped=@CompaniesSkipped,
            CompaniesTotal=@CompaniesTotal,
            EndDate=COALESCE(@endDate,EndDate)
        WHERE CEIDGImportBatchId=@ceidgImportBatchId;
        """, new
    {
        ceidgImportBatchId,
        statistics.CompaniesAdded,
        statistics.CompaniesUpdated,
        statistics.CompaniesSkipped,
        statistics.CompaniesTotal,
        endDate
    }, transaction: transaction, cancellationToken: token);
    if (await connection.ExecuteAsync(command) != 1)
        throw new InvalidOperationException($"Import batch {ceidgImportBatchId} was not found.");
}

static bool CompaniesDiffer(CeidgCompany companyFromDb, CeidgCompany companyFromXml)
{
    if (companyFromDb.FirstName != companyFromXml.FirstName) return true;
    if (companyFromDb.LastName != companyFromXml.LastName) return true;
    if (companyFromDb.Name != companyFromXml.Name) return true;
    if (companyFromDb.Nip != companyFromXml.Nip) return true;
    if (companyFromDb.Regon != companyFromXml.Regon) return true;
    if (companyFromDb.Krs != companyFromXml.Krs) return true;
    if (companyFromDb.Status != companyFromXml.Status) return true;
    //if (companyFromDb.StatusDzialalnosciId != companyFromXml.StatusDzialalnosciId) return true;
    if (companyFromDb.Wojewodztwo != companyFromXml.Wojewodztwo) return true;
    if (companyFromDb.Powiat != companyFromXml.Powiat) return true;
    if (companyFromDb.Gmina != companyFromXml.Gmina) return true;
    if (companyFromDb.Miejscowosc != companyFromXml.Miejscowosc) return true;
    if (companyFromDb.Street != companyFromXml.Street) return true;
    if (companyFromDb.ApartmentNumber != companyFromXml.ApartmentNumber) return true;
    if (companyFromDb.BuildingNumber != companyFromXml.BuildingNumber) return true;
    if (companyFromDb.PostalCode != companyFromXml.PostalCode) return true;
    //if (companyFromDb.BusinessType != companyFromXml.BusinessType) return true;
    if (companyFromDb.Website != companyFromXml.Website) return true;
    if (companyFromDb.Email != companyFromXml.Email) return true;
    if (companyFromDb.Phone != companyFromXml.Phone) return true;
    if (companyFromDb.StartedAt != companyFromXml.StartedAt) return true;
    if (companyFromDb.SuspendedOn != companyFromXml.SuspendedOn) return true;
    if (companyFromDb.ResumedOn != companyFromXml.ResumedOn) return true;

    if (companyFromDb.PrevailingPKD != companyFromXml.PrevailingPKD) return true;
    if (companyFromDb.CompanyPkds.Count != companyFromXml.CompanyPkds.Count) return true;

    bool different = !companyFromDb.CompanyPkds.SequenceEqual(companyFromXml.CompanyPkds);
    if (different) return true;

    return false;
}

static async Task<List<CeidgCompany>> ProcessXmlFile(string xmlPath, string connectionString, CEIDGBatchImportStatistics statistics, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    var companies = new List<CeidgCompany>();
    var reader = new CeidgXmlReader();
    using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync(token);
    await foreach (var company in reader.ReadAsync(xmlPath, token))
    {
        var existingCompany = await FindExistingCompanyByNIP(connection, company, token);
        if (existingCompany == null)
        {
            // adding a new company, since its NIP did not exist
            await InsertCompany(connection, company, token);
            statistics.CompaniesAdded++;
            Console.WriteLine($"Added: {company.Nip} - {company.Name}, {statistics.CompaniesAdded} companies added");
        }
        else
        {
            if (CompaniesDiffer(existingCompany, company))
            {
                await UpdateCompany(connection, existingCompany, company, token);
                statistics.CompaniesUpdated++;
                Console.WriteLine($"Updated: {company.Nip} - {company.Name}, {statistics.CompaniesUpdated} companies updated");
            }
            else
            {
                statistics.CompaniesSkipped++;
                Console.WriteLine($"Skipped because it is the same: {company.Nip} - {company.Name}, {statistics.CompaniesSkipped} companies skipped");
            }
            statistics.CompaniesTotal++;
            Console.WriteLine($"Processed {statistics.CompaniesTotal} companies");
            companies.Add(company);
        }
    }
    return companies;
}


static async Task<CeidgCompany?> FindExistingCompanyByNIP(SqlConnection connection, CeidgCompany company, CancellationToken token)
{
    var predicate =
        "Nip = @nip";
    var command = new CommandDefinition($"""
        DECLARE @companyId int;
        SELECT TOP (1) @companyId=Id FROM ColdCall.Companies WHERE {predicate} ORDER BY Id;
        SELECT c.Id,c.Name,c.BusinessType,c.FirstName,c.LastName,c.Krs,c.Nip,c.Regon,
            c.WojewodztwoId,c.MiejscowoscId,c.GminaId,c.PowiatId,c.PostalCode,c.Street,
            c.BuildingNumber,c.ApartmentNumber,c.Phone,c.Email,c.GoogleEmail,c.Website,
            c.InstagramAccount,c.FacebookAccount,c.StartedAt,c.CreatedOn,c.DataModifiedOn,
            c.StatusDzialalnosciId,c.AddressIsMissing,c.BlockedByRODO,c.SkipAddressVerification,
            c.ImportedOn,c.BatchImported,c.SuspendedOn,c.ResumedOn,
            p.Name AS Powiat,g.Name AS Gmina,w.Name AS Wojewodztwo,m.Name AS Miejscowosc
        FROM ColdCall.Companies c
        LEFT JOIN ColdCall.Powiaty p ON p.Id=c.PowiatId
        LEFT JOIN ColdCall.Gminy g ON g.Id=c.GminaId
        LEFT JOIN ColdCall.Wojewodztwa w ON w.Id=c.WojewodztwoId
        LEFT JOIN ColdCall.Miejscowosci m ON m.Id=c.MiejscowoscId
        WHERE c.Id=@companyId;

        SELECT cp.Id,cp.CompanyId,cp.PkdCodeId,p.Code,p.Name,cp.Prevailing
        FROM ColdCall.CompanyPkds cp
        JOIN ColdCall.PkdCodes p ON p.Id=cp.PkdCodeId
        WHERE cp.CompanyId=@companyId
        ORDER BY cp.Prevailing DESC,p.Code,cp.Id;
        """, new { nip = company.Nip }, commandTimeout: 180, cancellationToken: token);
    using var results = await connection.QueryMultipleAsync(command);
    var existingCompany = await results.ReadSingleOrDefaultAsync<CeidgCompany>();
    if (existingCompany == null) return null;
    var companyPkds = (await results.ReadAsync<CeidgCompanyPkd>()).AsList();
    return existingCompany with
    {
        CompanyPkds = companyPkds,
        PKDs = companyPkds.Select(pkd => pkd.Code).Distinct().ToList(),
        PrevailingPKD = companyPkds.FirstOrDefault(pkd => pkd.Prevailing)?.Code ?? ""
    };
}

static Task InsertCompany(SqlConnection connection, CeidgCompany company, CancellationToken token,
    SqlTransaction? transaction = null) =>
    SaveCompany(connection, null, company, token, transaction);

static async Task UpdateCompany(SqlConnection connection, CeidgCompany existingCompany, CeidgCompany company,
    CancellationToken token, SqlTransaction? transaction = null) =>
    SaveCompany(connection, existingCompany, company, token, transaction);

static async Task SaveCompany(SqlConnection connection, CeidgCompany? existingCompany, CeidgCompany company,
    CancellationToken token, SqlTransaction? transaction)
{
    var ownsTransaction = transaction == null;
    transaction ??= (SqlTransaction)await connection.BeginTransactionAsync(token);
    try
    {
        if (existingCompany != null && existingCompany.Id <= 0)
            throw new ArgumentException("Existing company must have a database ID.", nameof(existingCompany));
        var businessType = existingCompany?.BusinessType ?? company.BusinessType;
        if (businessType == 0)
        {
            var typeCommand = new CommandDefinition("""
                SELECT TypDzialalnosciId FROM ColdCall.BusinessType
                WHERE TypDzialalnosci LIKE N'Jednoosobowa%';
                """, transaction: transaction, cancellationToken: token);
            var types = (await connection.QueryAsync<int>(typeCommand)).AsList();
            if (types.Count == 0)
                throw new InvalidDataException("Missing sole-proprietorship business type.");
            if (types.Count > 1)
                throw new InvalidDataException("Ambiguous sole-proprietorship business type.");
            businessType = types[0];
        }

        var province = company.WojewodztwoId
            ?? await ResolveCompanyDictionary(connection, transaction, "Wojewodztwa",
                company.Powiat ?? "Brak województwa", token);
        var county = company.PowiatId
            ?? await ResolveCompanyDictionary(connection, transaction, "Powiaty", company.Powiat, token);
        var commune = company.GminaId
            ?? await ResolveCompanyDictionary(connection, transaction, "Gminy", company.Gmina, token);
        var city = company.MiejscowoscId
            ?? await ResolveCompanyDictionary(connection, transaction, "Miejscowosci", company.Miejscowosc, token);
        var status = company.StatusDzialalnosciId;
        if (!string.IsNullOrWhiteSpace(company.Status))
            status = await ResolveCompanyStatus(connection, transaction, company.Status, token);
        if (status == 0)
            throw new InvalidDataException("Company activity status is missing.");

        var values = new (string Name, DbType Type, int Size, object? Value)[]
        {
            ("Name", DbType.String, 1000, company.Name),
            ("BusinessType", DbType.Int32, 0, businessType),
            ("FirstName", DbType.String, -1, company.FirstName),
            ("LastName", DbType.String, -1, company.LastName),
            ("Krs", DbType.String, 450, company.Krs),
            ("Nip", DbType.String, 450, company.Nip),
            ("Regon", DbType.String, 50, company.Regon),
            ("WojewodztwoId", DbType.Int32, 0, province),
            ("PowiatId", DbType.Int32, 0, county),
            ("GminaId", DbType.Int32, 0, commune),
            ("MiejscowoscId", DbType.Int32, 0, city),
            ("PostalCode", DbType.String, -1, company.PostalCode),
            ("Street", DbType.String, -1, company.Street),
            ("BuildingNumber", DbType.String, -1, company.BuildingNumber),
            ("ApartmentNumber", DbType.String, -1, company.ApartmentNumber),
            ("Phone", DbType.String, -1, company.Phone),
            ("Email", DbType.String, -1, company.Email),
            ("Website", DbType.String, -1, company.Website),
            ("GoogleEmail", DbType.String, -1, company.GoogleEmail ?? existingCompany?.GoogleEmail),
            ("InstagramAccount", DbType.String, -1, company.InstagramAccount ?? existingCompany?.InstagramAccount),
            ("FacebookAccount", DbType.String, -1, company.FacebookAccount ?? existingCompany?.FacebookAccount),
            ("StartedAt", DbType.DateTime2, 0, company.StartedAt),
            ("StatusDzialalnosciId", DbType.Byte, 0, status),
            ("SuspendedOn", DbType.DateTime, 0,
                string.Equals(company.Status?.Trim(), "Zawieszony", StringComparison.OrdinalIgnoreCase) || status == 2
                    ? company.SuspendedOn : null),
            ("ResumedOn", DbType.DateTime, 0, company.ResumedOn)
        };
        var columns = string.Join(",", values.Select(value => "[" + value.Name + "]"));
        var parameters = string.Join(",", values.Select(value => "@" + value.Name));
        var addressChanged = existingCompany == null || existingCompany.WojewodztwoId != province
            || existingCompany.PowiatId != county || existingCompany.GminaId != commune
            || existingCompany.MiejscowoscId != city || existingCompany.PostalCode != company.PostalCode
            || existingCompany.Street != company.Street || existingCompany.BuildingNumber != company.BuildingNumber
            || existingCompany.ApartmentNumber != company.ApartmentNumber;

        // SQL identifiers come only from the fixed column list above; all imported values are parameters.
        var sql = existingCompany == null
            ? $"""
              INSERT ColdCall.Companies ({columns},DataModifiedOn,ImportedOn,BatchImported,AddressIsMissing)
              VALUES ({parameters},GETDATE(),GETDATE(),1,1);
              """
            : $"""
              UPDATE ColdCall.Companies
              SET {string.Join(",", values.Select(value => "[" + value.Name + "]=@" + value.Name))},
                  DataModifiedOn=GETDATE(),ImportedOn=GETDATE(),BatchImported=1,
                  AddressIsMissing=CASE WHEN @addressChanged=1 THEN 1 ELSE AddressIsMissing END
              WHERE Id=@id AND EXISTS (SELECT {columns} EXCEPT SELECT {parameters});
              IF NOT EXISTS (SELECT 1 FROM ColdCall.Companies WHERE Id=@id)
                  THROW 50005, 'Company to update no longer exists.', 1;
              """;
        var valuesToSave = new DynamicParameters();
        foreach (var value in values)
        {
            if (value.Value is string text && value.Size > 0 && text.Length > value.Size)
                throw new InvalidDataException($"Company field {value.Name} exceeds {value.Size} characters.");
            valuesToSave.Add(value.Name, value.Value, value.Type,
                size: value.Type == DbType.String ? value.Size : null);
        }
        if (existingCompany != null)
        {
            valuesToSave.Add("id", existingCompany.Id);
            valuesToSave.Add("addressChanged", addressChanged);
        }
        await connection.ExecuteAsync(new CommandDefinition(sql, valuesToSave,
            transaction: transaction, commandTimeout: 180, cancellationToken: token));
        if (ownsTransaction) await transaction.CommitAsync(token);
    }
    catch
    {
        if (ownsTransaction) await transaction.RollbackAsync(CancellationToken.None);
        throw;
    }
    finally
    {
        if (ownsTransaction) await transaction.DisposeAsync();
    }
}

static async Task<int?> ResolveCompanyDictionary(SqlConnection connection, SqlTransaction transaction,
    string table, string? name, CancellationToken token)
{
    if (string.IsNullOrWhiteSpace(name)) return null;
    if (table is not ("Wojewodztwa" or "Powiaty" or "Gminy" or "Miejscowosci"))
        throw new ArgumentException("Invalid dictionary table.", nameof(table));
    if (name.Length > 250) throw new InvalidDataException("Address dictionary name exceeds 250 characters.");
    var normalized = table == "Wojewodztwa" ? name.Replace('-', ' ') : name;
    var match = table == "Wojewodztwa" ? "REPLACE(Name,N'-',N' ')" : "Name";
    var command = new CommandDefinition($"""
        DECLARE @id int, @count int;
        SELECT @id=MIN(Id),@count=COUNT(*) FROM ColdCall.[{table}] WITH (UPDLOCK,HOLDLOCK)
        WHERE {match} COLLATE SQL_Latin1_General_CP1_CI_AS=@name;
        IF @count>1 THROW 50006, 'Ambiguous address dictionary name.', 1;
        IF @id IS NULL
        BEGIN
            INSERT ColdCall.[{table}] (Name) VALUES (@original);
            SET @id=CONVERT(int,SCOPE_IDENTITY());
        END;
        SELECT @id;
        """, new { name = normalized, original = name }, transaction: transaction,
        commandTimeout: 180, cancellationToken: token);
    return await connection.QuerySingleAsync<int>(command);
}

static async Task<byte> ResolveCompanyStatus(SqlConnection connection, SqlTransaction transaction,
    string name, CancellationToken token)
{
    name = name.Trim();
    if (name.Length is 0 or > 100) throw new InvalidDataException("Invalid company status name.");
    var command = new CommandDefinition("""
        DECLARE @id int, @count int;
        SELECT @id=MIN(CONVERT(int,StatusId)),@count=COUNT(*)
        FROM ColdCall.CompanyStatus WITH (TABLOCKX,HOLDLOCK)
        WHERE Nazwa COLLATE SQL_Latin1_General_CP1_CI_AS=@name;
        IF @count>1 THROW 50007, 'Ambiguous company status.', 1;
        IF @id IS NULL
        BEGIN
            SELECT @id=ISNULL(MAX(CONVERT(int,StatusId)),0)+1 FROM ColdCall.CompanyStatus;
            IF @id>255 THROW 50008, 'Company status ID range exhausted.', 1;
            INSERT ColdCall.CompanyStatus(StatusId,Nazwa) VALUES (@id,@name);
        END;
        SELECT CONVERT(tinyint,@id);
        """, new { name }, transaction: transaction, cancellationToken: token);
    return await connection.QuerySingleAsync<byte>(command);
}
