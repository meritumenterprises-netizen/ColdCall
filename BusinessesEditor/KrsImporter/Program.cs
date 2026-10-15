using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "logs"));
var logPath = Path.Combine(AppContext.BaseDirectory, "logs", $"import-{DateTime.Now:yyyyMMdd-HHmmss}.log");
void Log(string message)
{
    var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
}
try
{
var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
using var configDocument = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
var cfg = configDocument.RootElement;
var start = DateOnly.Parse(cfg.GetProperty("StartDate").GetString()!);
var end = cfg.GetProperty("EndDate").ValueKind == JsonValueKind.String ? DateOnly.Parse(cfg.GetProperty("EndDate").GetString()!) : DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
if (args.Length == 2 && args[0] == "--probe") end = DateOnly.ParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture);
var delay = cfg.GetProperty("DelayMilliseconds").GetInt32();
DateOnly? registeredFrom = cfg.TryGetProperty("RegisteredFrom", out var rf) && rf.ValueKind == JsonValueKind.String ? DateOnly.Parse(rf.GetString()!) : null;
if (start > end) throw new ArgumentException("StartDate > EndDate");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
async Task<JsonDocument?> Fetch(string url)
{
    for (var attempt = 0; attempt < 5; attempt++)
    {
        await Task.Delay(Math.Max(0, delay));
        Log($"GET {url} (próba {attempt + 1}/5)");
        try
        {
            using var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            Log($"HTTP {(int)response.StatusCode}; {body.Length} znaków");
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500)
            {
                if (attempt == 4) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {body[..Math.Min(300, body.Length)]}");
                var wait = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds((2 << attempt) + Random.Shared.NextDouble()));
                Log($"Ponowienie za {Math.Max(0, wait.TotalSeconds):F0} s");
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
                continue;
            }
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {body[..Math.Min(300, body.Length)]}");
            try { return JsonDocument.Parse(body); }
            catch (JsonException ex) { throw new InvalidOperationException($"Odpowiedź API nie jest JSON: {body[..Math.Min(200, body.Length)]}", ex); }
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && attempt < 4)
        {
            Log($"Problem sieci: {ex.Message}; ponawiam");
            await Task.Delay(TimeSpan.FromSeconds(2 << attempt));
        }
    }
    throw new HttpRequestException($"Nie udało się pobrać {url}");
}
if (args.Contains("--probe"))
{
    // Tests the same HTTP client and parsing as import, without opening SQL Server.
    using var bulletin = await Fetch($"https://api-krs.ms.gov.pl/api/Krs/Biuletyn/{end:yyyy-MM-dd}");
    var numbers = ReadBulletin(bulletin);
    Log($"TEST: biuletyn {end}: {numbers.Length} unikalnych numerów KRS");
    var krs = numbers.FirstOrDefault() ?? "0000027088";
    using var extract = await Fetch($"https://api-krs.ms.gov.pl/api/krs/OdpisAktualny/{krs}?rejestr=P&format=json");
    if (extract is null) throw new InvalidOperationException($"KRS {krs}: brak odpisu P (404). Test HTTP działa, ale ten podmiot nie ma aktualnego odpisu przedsiębiorcy.");
    var root = extract.RootElement;
    var d1 = P(root, "odpis", "dane", "dzial1");
    var entity = P(d1, "danePodmiotu");
    var foreignEntity = P(d1, "danePodmiotuZagranicznego");
    var name = S(entity, "nazwa") ?? S(foreignEntity, "nazwa") ?? throw new FormatException("Brak nazwy podmiotu w odpisie");
    var registration = S(P(root,"odpis","naglowekA"),"dataRejestracjiWKRS");
    if (!DateOnly.TryParseExact(registration,"dd.MM.yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)) throw new FormatException("Brak poprawnej daty rejestracji");
    var activities = P(root,"odpis","dane","dzial3","przedmiotDzialalnosci");
    Log($"TEST OK: KRS {krs}; {name}; rejestracja {registration}; PKD: {activities.GetRawText()}");
    return;
}
Log($"Biuletyny {start} .. {end}; rejestracja od: {registeredFrom?.ToString() ?? "bez filtra"}");
Log("Łączenie z SQL Server...");
await using var db = new SqlConnection(cfg.GetProperty("ConnectionString").GetString());
await db.OpenAsync();
Log($"Połączono z bazą {db.Database}");
await Sql("""
IF OBJECT_ID('dbo.Spolki','U') IS NULL CREATE TABLE dbo.Spolki (
 Id bigint IDENTITY PRIMARY KEY, KRS varchar(10) NOT NULL UNIQUE, NIP varchar(10) NULL,
 REGON varchar(14) NULL, Nazwa nvarchar(500) NOT NULL, FormaPrawna nvarchar(200) NULL,
 DataRejestracjiWKRS date NOT NULL, Wojewodztwo nvarchar(100) NULL, Powiat nvarchar(100) NULL,
 Gmina nvarchar(100) NULL, Miejscowosc nvarchar(200) NULL, Ulica nvarchar(200) NULL,
 NrBudynku nvarchar(50) NULL, NrLokalu nvarchar(50) NULL, KodPocztowy varchar(20) NULL,
 Kraj nvarchar(100) NULL, Poczta nvarchar(200) NULL,
 KrajZakladuGlownego nvarchar(100) NULL, WojewodztwoZakladuGlownego nvarchar(100) NULL,
 MiejscowoscZakladuGlownego nvarchar(200) NULL, UlicaZakladuGlownego nvarchar(200) NULL,
 NrBudynkuZakladuGlownego nvarchar(50) NULL, NrLokaluZakladuGlownego nvarchar(50) NULL,
 KodPocztowyZakladuGlownego varchar(20) NULL, PocztaZakladuGlownego nvarchar(200) NULL,
 Email nvarchar(320) NULL, WWW nvarchar(500) NULL, JsonKRS nvarchar(max) NOT NULL,
 DataPobrania datetime2 NOT NULL DEFAULT sysdatetime());
IF OBJECT_ID('dbo.SpolkiPKD','U') IS NULL CREATE TABLE dbo.SpolkiPKD (
 SpolkaId bigint NOT NULL REFERENCES dbo.Spolki(Id), KodPKD varchar(20) NOT NULL,
 OpisPKD nvarchar(max) NULL, CzyPrzewazajace bit NOT NULL,
 CONSTRAINT PK_SpolkiPKD PRIMARY KEY(SpolkaId,KodPKD));
IF OBJECT_ID('dbo.Spolki','U') IS NOT NULL
BEGIN
 IF COL_LENGTH('dbo.Spolki', 'Kraj') IS NULL ALTER TABLE dbo.Spolki ADD Kraj nvarchar(100) NULL;
 IF COL_LENGTH('dbo.Spolki', 'Poczta') IS NULL ALTER TABLE dbo.Spolki ADD Poczta nvarchar(200) NULL;
 IF COL_LENGTH('dbo.Spolki', 'KrajZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD KrajZakladuGlownego nvarchar(100) NULL;
 IF COL_LENGTH('dbo.Spolki', 'WojewodztwoZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD WojewodztwoZakladuGlownego nvarchar(100) NULL;
 IF COL_LENGTH('dbo.Spolki', 'MiejscowoscZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD MiejscowoscZakladuGlownego nvarchar(200) NULL;
 IF COL_LENGTH('dbo.Spolki', 'UlicaZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD UlicaZakladuGlownego nvarchar(200) NULL;
 IF COL_LENGTH('dbo.Spolki', 'NrBudynkuZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD NrBudynkuZakladuGlownego nvarchar(50) NULL;
 IF COL_LENGTH('dbo.Spolki', 'NrLokaluZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD NrLokaluZakladuGlownego nvarchar(50) NULL;
 IF COL_LENGTH('dbo.Spolki', 'KodPocztowyZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD KodPocztowyZakladuGlownego varchar(20) NULL;
 IF COL_LENGTH('dbo.Spolki', 'PocztaZakladuGlownego') IS NULL ALTER TABLE dbo.Spolki ADD PocztaZakladuGlownego nvarchar(200) NULL;
END
IF OBJECT_ID('dbo.KrsImportDays','U') IS NULL CREATE TABLE dbo.KrsImportDays (
 Dzien date NOT NULL PRIMARY KEY, Pobrano datetime2 NOT NULL DEFAULT sysdatetime());
IF OBJECT_ID('dbo.KrsImportQueue','U') IS NULL CREATE TABLE dbo.KrsImportQueue (
 KRS varchar(10) NOT NULL PRIMARY KEY, Status tinyint NOT NULL DEFAULT 0,
 Error nvarchar(1000) NULL, UpdatedAt datetime2 NOT NULL DEFAULT sysdatetime());
""");
// Errors from the previous execution are retried once on this execution.
await Sql("UPDATE dbo.KrsImportQueue SET Status=0 WHERE Status=3");
// Re-evaluate date exclusions only when the registration filter changes.
await Sql("""
IF OBJECT_ID('dbo.KrsImportSettings','U') IS NULL CREATE TABLE dbo.KrsImportSettings (Id int PRIMARY KEY, FilterValue nvarchar(50) NOT NULL);
IF NOT EXISTS(SELECT 1 FROM dbo.KrsImportSettings WHERE Id=1 AND FilterValue=@filter)
BEGIN
 UPDATE dbo.KrsImportQueue SET Status=0 WHERE Status=2 AND Error=N'Data rejestracji poza zakresem';
 DELETE FROM dbo.KrsImportSettings WHERE Id=1;
 INSERT dbo.KrsImportSettings(Id,FilterValue) VALUES(1,@filter);
END
""", null, ("@filter",registeredFrom?.ToString("yyyy-MM-dd") ?? "all"));
var failed = 0; var saved = 0; var skipped = 0;
for (var day = end; day >= start; day = day.AddDays(-1))
{
    if (Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM dbo.KrsImportDays WHERE Dzien=@d", ("@d", day.ToDateTime(TimeOnly.MinValue)))) == 0)
    {
        using var bulletin = await Fetch($"https://api-krs.ms.gov.pl/api/Krs/Biuletyn/{day:yyyy-MM-dd}");
        var numbers = ReadBulletin(bulletin);
        Log($"{day}: kolejkuję {numbers.Length} numerów KRS...");
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        try
        {
            await Sql("""
INSERT dbo.KrsImportQueue(KRS)
SELECT j.[value] FROM OPENJSON(@numbers) j
WHERE NOT EXISTS(SELECT 1 FROM dbo.KrsImportQueue q WHERE q.KRS=j.[value]);
""", tx, ("@numbers", JsonSerializer.Serialize(numbers)));
            await Sql("INSERT dbo.KrsImportDays(Dzien) VALUES(@d)", tx, ("@d", day.ToDateTime(TimeOnly.MinValue)));
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
        Log($"{day}: numery zapisane do kolejki");
    }
    while (await Scalar("SELECT TOP(1) KRS FROM dbo.KrsImportQueue WHERE Status=0 ORDER BY KRS DESC") is string krs)
    {
        try
        {
            using var doc = await Fetch($"https://api-krs.ms.gov.pl/api/krs/OdpisAktualny/{krs}?rejestr=P&format=json");
            if (doc is null) { await Skip(krs, "404 / brak aktualnego odpisu P"); skipped++; continue; }
            var root = doc.RootElement;
            var dateText = S(P(root, "odpis", "naglowekA"), "dataRejestracjiWKRS");
            if (!DateOnly.TryParseExact(dateText, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var registered)) throw new FormatException($"Data rejestracji: {dateText}");
            if (registeredFrom is DateOnly minimum && registered < minimum) { await Skip(krs, "Data rejestracji poza zakresem"); skipped++; continue; }
            var d1 = P(root, "odpis", "dane", "dzial1");
            var entity = P(d1, "danePodmiotu");
            var foreignEntity = P(d1, "danePodmiotuZagranicznego");
            var name = S(entity, "nazwa") ?? S(foreignEntity, "nazwa") ?? throw new FormatException("Brak nazwy");
            if (entity.ValueKind != JsonValueKind.Object) entity = foreignEntity;
            var ids = P(entity, "identyfikatory");
            var location = P(d1, "siedzibaIAdres");
            if (location.ValueKind != JsonValueKind.Object) location = P(d1, "siedzibaIAdresPodmiotuZagranicznego");
            var seat = P(location, "siedziba"); var address = P(location, "adres");
            var headOfficeAddress = P(location, "siedzibaIAdresZakladuGlownego");
            var business = P(root, "odpis", "dane", "dzial3", "przedmiotDzialalnosci");
            var pkd = new Dictionary<string, (string? Description, bool Main)>();
            foreach (var (field, main) in new[] { ("przedmiotPrzewazajacejDzialalnosci", true), ("przedmiotPozostalejDzialalnosci", false) })
            {
                var items = P(business, field);
                if (items.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in items.EnumerateArray())
                {
                    var a = S(item, "kodDzial"); var b = S(item, "kodKlasa"); var c = S(item, "kodPodklasa");
                    if (a is null || b is null || c is null) continue;
                    var code = $"{a}.{b}.{c}";
                    pkd[code] = (S(item, "opis"), main || (pkd.TryGetValue(code, out var previous) && previous.Main));
                }
            }
            await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
            try
            {
                await Sql("""
UPDATE dbo.Spolki WITH (UPDLOCK,SERIALIZABLE) SET NIP=@nip,REGON=@regon,Nazwa=@name,FormaPrawna=@form,
 DataRejestracjiWKRS=@date,Wojewodztwo=@province,Powiat=@county,Gmina=@commune,Miejscowosc=@city,
 Ulica=@street,NrBudynku=@building,NrLokalu=@unit,KodPocztowy=@postal,Kraj=@country,Poczta=@postOffice,
 KrajZakladuGlownego=@headCountry,WojewodztwoZakladuGlownego=@headProvince,MiejscowoscZakladuGlownego=@headCity,
 UlicaZakladuGlownego=@headStreet,NrBudynkuZakladuGlownego=@headBuilding,NrLokaluZakladuGlownego=@headUnit,
 KodPocztowyZakladuGlownego=@headPostal,PocztaZakladuGlownego=@headPostOffice,Email=@email,WWW=@web,
 JsonKRS=@json,DataPobrania=sysdatetime() WHERE KRS=@k;
IF @@ROWCOUNT=0 INSERT dbo.Spolki
 (KRS,NIP,REGON,Nazwa,FormaPrawna,DataRejestracjiWKRS,Wojewodztwo,Powiat,Gmina,Miejscowosc,Ulica,NrBudynku,NrLokalu,KodPocztowy,Kraj,Poczta,KrajZakladuGlownego,WojewodztwoZakladuGlownego,MiejscowoscZakladuGlownego,UlicaZakladuGlownego,NrBudynkuZakladuGlownego,NrLokaluZakladuGlownego,KodPocztowyZakladuGlownego,PocztaZakladuGlownego,Email,WWW,JsonKRS)
 VALUES(@k,@nip,@regon,@name,@form,@date,@province,@county,@commune,@city,@street,@building,@unit,@postal,@country,@postOffice,@headCountry,@headProvince,@headCity,@headStreet,@headBuilding,@headUnit,@headPostal,@headPostOffice,@email,@web,@json);
DELETE FROM dbo.SpolkiPKD WHERE SpolkaId=(SELECT Id FROM dbo.Spolki WHERE KRS=@k);
""", tx, ("@k",krs),("@nip",S(ids,"nip")),("@regon",S(ids,"regon")),("@name",name),
                    ("@form",S(entity,"formaPrawna")),("@date",registered.ToDateTime(TimeOnly.MinValue)),
                    ("@province",S(seat,"wojewodztwo")),("@county",S(seat,"powiat")),("@commune",S(seat,"gmina")),
                    ("@city",S(address,"miejscowosc") ?? S(seat,"miejscowosc")),("@street",S(address,"ulica")),
                    ("@building",S(address,"nrDomu")),("@unit",S(address,"nrLokalu")),("@postal",S(address,"kodPocztowy")),
                    ("@country",S(address,"kraj") ?? S(seat,"kraj")),("@postOffice",S(address,"poczta")),
                    ("@headCountry",S(headOfficeAddress,"kraj")),("@headProvince",S(headOfficeAddress,"wojewodztwo")),
                    ("@headCity",S(headOfficeAddress,"miejscowosc")),("@headStreet",S(headOfficeAddress,"ulica")),
                    ("@headBuilding",S(headOfficeAddress,"nrDomu")),("@headUnit",S(headOfficeAddress,"nrLokalu")),
                    ("@headPostal",S(headOfficeAddress,"kodPocztowy")),("@headPostOffice",S(headOfficeAddress,"poczta")),
                    ("@email",S(location,"adresPocztyElektronicznej")),("@web",S(location,"adresStronyInternetowej")),("@json",root.GetRawText()));
                foreach (var (code, value) in pkd)
                    await Sql("INSERT dbo.SpolkiPKD(SpolkaId,KodPKD,OpisPKD,CzyPrzewazajace) SELECT Id,@code,@description,@main FROM dbo.Spolki WHERE KRS=@k",tx,
                        ("@k",krs),("@code",code),("@description",value.Description),("@main",value.Main));
                await Sql("UPDATE dbo.KrsImportQueue SET Status=1,Error=NULL,UpdatedAt=sysdatetime() WHERE KRS=@k",tx,("@k",krs));
                await tx.CommitAsync();
                saved++;
                Log($"ZAPISANO {krs}: {name} ({pkd.Count} PKD); zapisano {saved}, pominięto {skipped}, błędów {failed}");
            }
            catch { await tx.RollbackAsync(); throw; }
        }
        catch (Exception ex)
        {
            await Sql("UPDATE dbo.KrsImportQueue SET Status=3,Error=@error,UpdatedAt=sysdatetime() WHERE KRS=@k",null,("@k",krs),("@error",ex.Message[..Math.Min(1000,ex.Message.Length)]));
            failed++;
            Log($"BŁĄD KRS {krs}: {ex.Message}. Rekord pozostaje do ponowienia przy następnym uruchomieniu.");
            if (ex is SqlException) throw; // Stop on schema/connection errors instead of repeating them for every company.
        }

    }
}
Log($"Zakończono: zapisano {saved}, pominięto {skipped}, błędów {failed}. Log: {logPath}");
if (failed > 0) Environment.ExitCode = 1;

async Task Skip(string krs, string reason)
{
    Log($"POMINIĘTO {krs}: {reason}");
    await Sql("UPDATE dbo.KrsImportQueue SET Status=2,Error=@reason,UpdatedAt=sysdatetime() WHERE KRS=@k",null,("@k",krs),("@reason",reason));
}
async Task<object?> Scalar(string query, params (string Key, object? Value)[] args)
{
    await using var cmd = new SqlCommand(query,db);
    foreach (var (key,value) in args) cmd.Parameters.AddWithValue(key,value ?? DBNull.Value);
    return await cmd.ExecuteScalarAsync();
}
async Task Sql(string query, SqlTransaction? tx=null, params (string Key, object? Value)[] args)
{
    await using var cmd = new SqlCommand(query,db,tx) { CommandTimeout=120 };
    foreach (var (key,value) in args) cmd.Parameters.AddWithValue(key,value ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
}
}
catch (Exception ex)
{
    Log($"BŁĄD: {ex}");
    Log($"Szczegóły zapisano w {logPath}");
    Environment.ExitCode = 1;
}
static string[] ReadBulletin(JsonDocument? document)
{
    if (document?.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("Biuletyn nie jest tablicą JSON; nie zapisano dnia jako ukończonego.");
    return document.RootElement.EnumerateArray().Select(item =>
    {
        var value = item.ToString();
        if (!Regex.IsMatch(value, @"^\d{1,10}$")) throw new FormatException($"Nieznana pozycja biuletynu: {value}");
        return value.PadLeft(10, '0');
    }).Distinct().OrderByDescending(x => x).ToArray();
}
static JsonElement P(JsonElement e, params string[] keys)
{
    foreach (var key in keys) if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key,out e)) return default;
    return e;
}
static string? S(JsonElement e, string key)
{
    var value = P(e,key);
    return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
