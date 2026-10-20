using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace GeoPortalAddressImporter;

internal static class SelfTests
{
    private static readonly Company Company = new(1, 2, 1, "Dolno\u015bl\u0105skie", "Wa\u0142brzych", "ul. Psie Pole", "39", "58-301");
    private static readonly Address Address = new("dolno\u015bl\u0105skie", "Wa\u0142brzych", "Wa\u0142brzych",
        "Wa\u0142brzych", "Psie Pole", "39", "58-301", "026501", "0983681", "18024", 309380.47, 325804.7);

    private static ApiPoint Point(Address? address = null, double accuracy = 1) =>
        new(address ?? Address, "POINT(309380.47 325804.7)", accuracy, 1, 1);

    public static async Task RunAsync()
    {
        Assert(Matching.Missing(Company) == null, "Adres kompletny");
        Assert(Matching.Missing(Company with { PostalCode = null }) != null, "Brak kodu pocztowego");
        Assert(Matching.Missing(Company with { Number = "-" }) != null, "Zastepczy numer budynku");
        Assert(Text.Street("pl. Plac Piastowski") == "PIASTOWSKI", "Podwojny prefiks ulicy");
        Assert(Text.Street("ul:Wielog\u00f3rska") == "WIELOG\u00d3RSKA", "Prefiks ul z dwukropkiem");
        Assert(Text.Street("ul.: Wielog\u00f3rska") == ": WIELOG\u00d3RSKA", "Nie zgaduj nieznanych prefiksow");
        Assert(Text.Number("7 A") == "7A", "Normalizacja numeru");
        Assert(Text.Number("01") != Text.Number("1"), "Brak zgadywania numeru");
        Assert(ApiParser.ParseUnits("{Polska,mazowieckie,\"powiat, specjalny\",Marki}")[2] == "powiat, specjalny", "Cytowany przecinek");
        var candidate = new ApiResult("address", 15, 1, new[] { Point() });
        Assert(Matching.Candidate(Company, candidate).Address != null, "found objects nie oznacza liczby dokladnych trafien");
        var multiple = new ApiResult("address", 2, 2, new[] { Point(), Point(Address with { Number = "40" }) });
        Assert(Matching.Candidate(Company, multiple).Address == Address, "Pierwszy kandydat z wielu punktow");
        Assert(Matching.Candidate(Company, multiple with { Points = multiple.Points.Reverse().ToArray() }).Address == null,
            "Pierwszy kandydat nadal musi byc zgodny z adresem firmy");
        Assert(Matching.Candidate(Company, new ApiResult("address", 0, 0, Array.Empty<ApiPoint>())).Address == null,
            "Pusty wynik nie ma pierwszego punktu");
        Assert(Matching.Candidate(Company, candidate with { Type = "street" }).Address == null, "Odrzucenie centrum ulicy");
        Assert(Matching.Candidate(Company, candidate with { Points = new[] { Point(accuracy: 0.8) } }).Address == null, "Odrzucenie przyblizenia");
        Assert(Matching.Candidate(Company, candidate with { Points = new[] { Point(Address with { Number = "40" }) } }).Address == null, "Inny numer");
        Assert(Matching.Candidate(Company, candidate with { Points = new[] { Point(Address with { Province = "mazowieckie" }) } }).Address == null, "Inne wojewodztwo");
        Assert(Matching.Candidate(Company, candidate with { Points = new[] { Point(Address with { PostalCode = "58-300" }) } }).Address == null, "Inny kod pocztowy");
        var list = new ApiResult("address", 1, 1, new[] { Point() });
        Assert(Matching.Verify(Company, Address, list).Address != null, "Dokladny punkt");
        Assert(Matching.Verify(Company, Address, list with { Found = 2 }).Address == null, "Niepelna lista");
        Assert(Matching.Verify(Company, Address, new ApiResult("address", 2, 2, new[] { Point(), Point() })).Address == null, "Dwa punkty");
        var parsed = ApiParser.Parse(Json(candidate));
        Assert(parsed.Points[0].Address == Address, "Parser JSON");
        Assert(ApiParser.Parse("""{"type":"address","found objects":7,"returned objects":0,"results":null}""").Points.Count == 0, "Brak wyniku");
        var streetFallback = ApiParser.Parse("""{"type":"street","found objects":0,"results":null,"request time":0.0017712831497192383}""");
        Assert(Matching.Candidate(Company, streetFallback).Address == null, "Rzeczywista odpowiedz bez returned objects");
        AssertInvalid("""{"type":"city","found objects":1,"results":{"1":{"city":"Legnica"}}}""", "Miejscowosc bez GPS nie jest poprawnym wynikiem");
        var cityAddress = Address with { IsCity = true, Street = null, Number = "", PostalCode = "", Ulic = "" };
        var cityResult = new ApiResult("city", 1, 1, new[] { Point(cityAddress) });
        var cityJson = Json(cityResult);
        Assert(ApiParser.Parse(cityJson).Points[0].Address.IsCity, "Parser miejscowosci");
        Assert(ApiParser.Parse(cityJson.Replace("\"returned objects\":1,", "")).Returned == 1, "Miasto bez returned objects");
        Assert(CityMatching.Match(Company, cityResult).Address == cityAddress, "GPS miejscowosci");
        Assert(CityMatching.Match(Company with { Province = "mazowieckie" }, cityResult).Address == null, "Inne wojewodztwo miasta");
        Assert(CityMatching.Match(Company with { City = "Inne miasto" }, cityResult).Address == null, "Inna nazwa miasta");
        Assert(CityMatching.Match(Company, cityResult with { Found = 2, Returned = 2, Points = new[] { Point(cityAddress), Point(cityAddress) } }).Address == null, "Niejednoznaczna miejscowosc");
        Assert(CityMatching.Match(Company, cityResult with { Found = 2 }).Address == null, "Niepelna lista miejscowosci");
        Assert(cityAddress.SourceKey.Length == 50 && cityAddress.SourceKey != Address.SourceKey, "Oddzielny klucz GPS miejscowosci");
        Assert(Address.SourceKeyFor(Company).Length == 50
            && Address.SourceKeyFor(Company with { CountyId = 1 }) != Address.SourceKeyFor(Company with { CountyId = 2 }),
            "Klucz adresu uwzglednia identyfikatory Companies");
        Assert(ApiParser.Parse("""{"type":"address","found objects":0,"results":null}""").Returned == 0, "Pusta odpowiedz adresowa bez returned objects");
        Assert(ApiParser.Parse("""{"type":"address","found objects":"0","returned objects":"0","results":{}}""").Returned == 0, "Liczby jako string");
        const string nullFoundResponse = """{"type":"address","max results limit":15,"min accuracy limit":1,"only exact numbers":1,"found objects":null,"returned objects":0,"results":null,"request time":0.003420849641164144}""";
        Assert(ApiParser.Parse(nullFoundResponse).Found == 0, "Rzeczywista odpowiedz UUG z found objects=null");
        Assert(ApiParser.Parse("""{"type":"address","found objects":"","returned objects":0,"results":null}""").Found == 0, "Pusta liczba przy potwierdzonym braku wynikow");
        Assert(ApiParser.Parse("""{"type":"address","returned objects":0,"results":{}}""").Found == 0, "Brak licznika przy potwierdzonej pustej odpowiedzi");
        AssertInvalid("""{"type":"address","found objects":null,"results":null}""", "Niekompletna odpowiedz bez potwierdzenia braku wynikow");
        AssertInvalid("""{"type":"address","found objects":"blad","returned objects":0,"results":null}""", "Nieprawidlowa liczba nie oznacza braku wynikow");
        AssertInvalid("""{"type":"address","found objects":null,"returned objects":1,"results":{}}""", "Dodatni licznik nie oznacza braku wynikow");
        AssertInvalid("""{"type":"address","found objects":null,"returned objects":0,"results":null,"error":"service unavailable"}""", "Blad uslugi nie jest brakiem adresu");
        AssertInvalid("""{"type":"address","found objects":1,"results":null}""", "Brak licznika przy niepustym wyniku");
        AssertInvalid("""{"error":"service unavailable"}""", "Blad API nie oznacza braku adresu");
        var gps = Coordinates.Convert(Address.X, Address.Y);
        Assert(gps.Latitude is > 50.7m and < 50.9m && gps.Longitude is > 16.1m and < 16.4m, "EPSG2180 -> WGS84, kolejnosc osi");
        Assert(Address.SourceKey.Length == 50 && Address.SourceKey == (Address with { Number = " 3 9 " }).SourceKey, "Stabilny klucz zrodla");
        Assert(Options.Parse(new[] { "--apply", "--limit", "5" }).Limit == 5, "CLI");
        Assert(Options.Parse(Array.Empty<string>()).Limit == 10, "Bezpieczny limit domyslny");
        Assert(Options.Parse(new[] { "--delay-ms", "0" }).DelayMilliseconds == 0, "CLI bez opoznienia HTTP");
        Assert(Options.Parse(new[] { "--retry-errors", "--all" }).RetryErrors, "CLI ponowienia bledow");

        var temporary = Path.Combine(Path.GetTempPath(), "GeoPortalTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var log = new RunLog(Path.Combine(temporary, "logs"));
            await FileWriteTests.RunAsync(temporary);
            await PipelineTests.RunAsync();
            using var parallelHandler = new ConcurrentHandler(cityJson);
            using var parallelHttp = new HttpClient(parallelHandler);
            var parallelClient = new GeoportalClient(parallelHttp, Path.Combine(temporary, "parallel-cache"), 0, 30, log);
            var parallelCompanies = Enumerable.Range(1, ParallelAddressLookup.Concurrency).Reverse()
                .Select(id => Company with { Id = id, Number = null }).ToArray();
            var parallelResults = await ParallelAddressLookup.FindAsync(parallelCompanies, parallelClient, CancellationToken.None);
            Assert(parallelHandler.MaximumActive == 96, "96 jednoczesnych requestow HTTP");
            Assert(parallelResults.All(result => result.Error == null && result.Match?.Address?.IsCity == true), "Wyniki rownoleglych requestow");
            Assert(parallelResults.Select(result => result.Company.Id).SequenceEqual(parallelCompanies.Select(company => company.Id)),
                "Zapis i checkpoint zachowuja kolejnosc wejsciowa mimo kolejnosci odpowiedzi");
            Assert(parallelClient.Requests == 96, "Wspoldzielony licznik requestow");
            using var failedHttp = new HttpClient(new FakeHandler("""{"error":"service unavailable"}"""));
            var failedClient = new GeoportalClient(failedHttp, Path.Combine(temporary, "parallel-error-cache"), 0, 30, log);
            var failedResults = await ParallelAddressLookup.FindAsync(new[] { Company }, failedClient, CancellationToken.None);
            Assert(failedResults[0].Error is InvalidDataException && failedResults[0].Match == null, "Blad workera nie udaje poprawnego przetworzenia");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancelledCorrectly = false;
            try { await ParallelAddressLookup.FindAsync(parallelCompanies, parallelClient, cancelled.Token); }
            catch (OperationCanceledException) { cancelledCorrectly = true; }
            Assert(cancelledCorrectly, "Anulowanie nie daje kompletu wynikow do checkpointu");
            using var emptyHttp = new HttpClient(new FakeHandler(cityJson));
            var missingClient = new GeoportalClient(emptyHttp, Path.Combine(temporary, "missing-cache"), 100, 30, log);
            Assert((await missingClient.FindAsync(Company with { City = "" }, CancellationToken.None)).Address == null && missingClient.Requests == 0,
                "Brak miejscowosci nie wywoluje HTTP");
            Assert((await missingClient.FindAsync(Company with { Number = "-" }, CancellationToken.None)).Address?.IsCity == true,
                "Zastepczy numer powoduje wyszukiwanie miejscowosci");
            Assert((await missingClient.FindAsync(Company with { PostalCode = null }, CancellationToken.None)).Address?.IsCity == true && missingClient.CacheHits == 1,
                "Brak kodu pocztowego uzywa GPS miejscowosci z cache");
            var fallbackHandler = new FakeHandler("""{"type":"street","found objects":0,"results":null}""", Json(candidate), Json(list));
            using var fallbackHttp = new HttpClient(fallbackHandler);
            var fallbackClient = new GeoportalClient(fallbackHttp, Path.Combine(temporary, "fallback-cache"), 0, 30, log);
            Assert((await fallbackClient.FindAsync(Company with { Number = "40" }, CancellationToken.None)).Address == null, "Odpowiedz street pomijana");
            Assert((await fallbackClient.FindAsync(Company, CancellationToken.None)).Address != null, "Kontynuacja po odpowiedzi street");
            var nullFoundHandler = new FakeHandler(nullFoundResponse, Json(candidate), Json(list));
            using var nullFoundHttp = new HttpClient(nullFoundHandler);
            var nullFoundClient = new GeoportalClient(nullFoundHttp, Path.Combine(temporary, "null-found-cache"), 0, 30, log);
            Assert((await nullFoundClient.FindAsync(Company with { City = "Inne miasto" }, CancellationToken.None)).Address == null,
                "HTTP found objects=null pomijany");
            Assert((await nullFoundClient.FindAsync(Company with { City = "Inne miasto" }, CancellationToken.None)).Address == null && nullFoundHandler.Calls == 1,
                "Cache odpowiedzi z found objects=null");
            Assert((await nullFoundClient.FindAsync(Company, CancellationToken.None)).Address != null, "Kontynuacja po found objects=null");
            var plainErrorHandler = new FakeHandler("Blad zapytania.", Json(candidate), Json(list));
            using var plainErrorHttp = new HttpClient(plainErrorHandler);
            var plainErrorDirectory = Path.Combine(temporary, "plain-error-cache");
            var plainErrorClient = new GeoportalClient(plainErrorHttp, plainErrorDirectory, 0, 30, log);
            var plainErrorDeferred = false;
            try { await plainErrorClient.FindAsync(Company, CancellationToken.None); }
            catch (GeoportalQueryException) { plainErrorDeferred = true; }
            Assert(plainErrorDeferred && plainErrorHandler.Calls == 1, "Brak ponowienia niepoprawnego JSON");
            Assert((await plainErrorClient.FindAsync(Company, CancellationToken.None)).Address != null && plainErrorHandler.Calls == 3,
                "Klient dziala po odlozeniu blednej odpowiedzi");
            Assert(Directory.GetFiles(Path.Combine(plainErrorDirectory, "invalid-responses"), "*.json").Length == 1,
                "Tekstowy blad API zachowany diagnostycznie");
            var permanentHandler = new FakeHandler("Blad zapytania.", "Blad zapytania.", "Blad zapytania.", "Blad zapytania.")
                { ImmediateRetry = true };
            using var permanentHttp = new HttpClient(permanentHandler);
            var permanentDirectory = Path.Combine(temporary, "permanent-error-cache");
            var permanentClient = new GeoportalClient(permanentHttp, permanentDirectory, 0, 30, log);
            var deferred = false;
            try { await permanentClient.FindAsync(Company, CancellationToken.None); }
            catch (GeoportalQueryException) { deferred = true; }
            Assert(deferred && permanentHandler.Calls == 1, "Blad zapytania odkladany po jednej probie");
            var htmlHandler = new FakeHandler("<html>Bad Gateway</html>");
            using var htmlHttp = new HttpClient(htmlHandler);
            var htmlClient = new GeoportalClient(htmlHttp, Path.Combine(temporary, "html-error-cache"), 0, 30, log);
            var htmlDeferred = false;
            try { await htmlClient.FindAsync(Company, CancellationToken.None); }
            catch (GeoportalQueryException) { htmlDeferred = true; }
            Assert(htmlDeferred && htmlHandler.Calls == 1, "Dowolny niepoprawny JSON bez ponowien");
            Assert(Directory.GetFiles(permanentDirectory, "*.json").Length == 0, "Blad zapytania nie zapisany jako brak adresu");
            var queuePath = Path.Combine(temporary, "deferred.json");
            var queue = await DeferredQueue.OpenAsync(queuePath, CancellationToken.None);
            await queue.RecordAsync(Company, "Blad zapytania.", CancellationToken.None);
            await queue.RecordAsync(Company, "Ponowny blad.", CancellationToken.None);
            queue = await DeferredQueue.OpenAsync(queuePath, CancellationToken.None);
            Assert(queue.Count == 1 && queue.Ids[0] == Company.Id, "Trwala lista do ponowienia bez duplikatow");
            await queue.RemoveAsync(Company.Id, CancellationToken.None);
            Assert((await DeferredQueue.OpenAsync(queuePath, CancellationToken.None)).Count == 0, "Usuniecie poprawnie obsluzonej firmy");
            using var invalidHttp = new HttpClient(new FakeHandler("""{"error":"service unavailable"}"""));
            var invalidDirectory = Path.Combine(temporary, "invalid-cache");
            var invalidClient = new GeoportalClient(invalidHttp, invalidDirectory, 0, 30, log);
            var rejected = false;
            try { await invalidClient.FindAsync(Company, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected && Directory.GetFiles(Path.Combine(invalidDirectory, "invalid-responses"), "*.json").Length == 1,
                "Nietypowa odpowiedz zapisana diagnostycznie, nie jako brak adresu");
            var handler = new FakeHandler(Json(candidate), Json(list));
            using var http = new HttpClient(handler);
            var client = new GeoportalClient(http, Path.Combine(temporary, "cache"), 0, 30, log);
            Assert((await client.FindAsync(Company, CancellationToken.None)).Address != null, "HTTP dopasowanie");
            Assert(handler.Calls == 2, "Dwa zapytania HTTP");
            Assert((await client.FindAsync(Company, CancellationToken.None)).Address != null && handler.Calls == 2 && client.CacheHits == 2, "Cache odpowiedzi");
            var retryHandler = new FakeHandler(Json(candidate), Json(list)) { FirstResponseIs429 = true };
            using var retryHttp = new HttpClient(retryHandler);
            var retryClient = new GeoportalClient(retryHttp, Path.Combine(temporary, "retry-cache"), 0, 30, log);
            Assert((await retryClient.FindAsync(Company, CancellationToken.None)).Address != null && retryHandler.Calls == 3, "Retry HTTP429");
            var checkpoint = Path.Combine(temporary, "progress.json");
            await Files.AtomicAsync(checkpoint, new Progress(123), CancellationToken.None);
            Assert(JsonSerializer.Deserialize<Progress>(await File.ReadAllTextAsync(checkpoint))?.LastCompanyId == 123, "Atomowy checkpoint");
        }
        finally { Directory.Delete(temporary, true); }
        Console.WriteLine("PASS: testy dopasowania, parsera, wspolrzednych, cache, HTTP429, CLI i wznowienia.");
    }

    public static async Task DatabaseAsync(CancellationToken token)
    {
        await RunAsync();
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("GEO_PORTAL_SQL_CONNECTION") ??
            "Server=localhost;Database=Firmy_temp;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
        var database = "GeoPortalImporterTest_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync(token);
        var created = false;
        try
        {
            using (var create = new SqlCommand($"CREATE DATABASE [{database}];", master))
                await create.ExecuteNonQueryAsync(token);
            created = true;
            builder.InitialCatalog = database;
            await using (var test = new SqlConnection(builder.ConnectionString))
            {
                await test.OpenAsync(token);
                using var setup = new SqlCommand("""
                    CREATE SCHEMA ColdCall;
                    """, test);
                await setup.ExecuteNonQueryAsync(token);
                setup.CommandText = """
                    CREATE TABLE ColdCall.Wojewodztwa (Id int IDENTITY PRIMARY KEY, Name nvarchar(250) NOT NULL UNIQUE);
                    CREATE TABLE ColdCall.Miejscowosci (Id int IDENTITY PRIMARY KEY, Name nvarchar(250) NOT NULL UNIQUE);
                    CREATE TABLE ColdCall.Powiaty (Id int IDENTITY PRIMARY KEY, Name nvarchar(250) NOT NULL UNIQUE);
                    CREATE TABLE ColdCall.Gminy (Id int IDENTITY PRIMARY KEY, Name nvarchar(250) NOT NULL UNIQUE);
                    CREATE TABLE ColdCall.Companies (
                        Id int IDENTITY PRIMARY KEY, WojewodztwoId int, MiejscowoscId int,
                        PowiatId int, GminaId int,
                        Street nvarchar(max), BuildingNumber nvarchar(max), PostalCode nvarchar(max),
                        AddressIsMissing bit, BlockedByRODO bit NOT NULL);
                    CREATE TABLE ColdCall.Adresy (
                        Id int IDENTITY PRIMARY KEY,
                        WojewodztwoId int NOT NULL REFERENCES ColdCall.Wojewodztwa(Id),
                        PowiatId int NOT NULL REFERENCES ColdCall.Powiaty(Id),
                        GminaId int NOT NULL REFERENCES ColdCall.Gminy(Id),
                        MiejscowoscId int NOT NULL REFERENCES ColdCall.Miejscowosci(Id),
                        Ulica nvarchar(200), NrUlicy nvarchar(50), Rodzaj nvarchar(200),
                        Latitude decimal(9,6) NOT NULL, Longitude decimal(9,6) NOT NULL,
                        Location geography NOT NULL, PrngId varchar(50));
                    CREATE UNIQUE INDEX UX_Adresy_PrngId ON ColdCall.Adresy(PrngId) WHERE PrngId IS NOT NULL;
                    CREATE TABLE ColdCall.CompanyAddress (
                        CompanyAdresId int IDENTITY PRIMARY KEY,
                        CompanyId int NOT NULL REFERENCES ColdCall.Companies(Id),
                        AddressId int NOT NULL REFERENCES ColdCall.Adresy(Id));
                    CREATE INDEX IX_CA_CompanyId ON ColdCall.CompanyAddress(CompanyId);
                    INSERT ColdCall.Wojewodztwa(Name) VALUES (N'Brak wojewodztwa'), (N'Dolnośląskie');
                    INSERT ColdCall.Miejscowosci(Name) VALUES (N'Wałbrzych');
                    INSERT ColdCall.Powiaty(Name) VALUES (N'Powiat z Companies'), (N'Inny powiat');
                    INSERT ColdCall.Gminy(Name) VALUES (N'Gmina z Companies');
                    INSERT ColdCall.Companies (WojewodztwoId, MiejscowoscId, Street, BuildingNumber, PostalCode, AddressIsMissing, BlockedByRODO)
                    VALUES (2,1,N'ul. Psie Pole',N'39',N'58-301',1,0),
                           (2,1,N'ul. Psie Pole',N'39',N'58-301',1,0),
                           (2,1,N'ul. Psie Pole',N'39',N'58-301',1,1),
                           (2,1,N'ul. Psie Pole',N'39',N'58-301',1,0);
                    UPDATE ColdCall.Companies SET PowiatId=1, GminaId=1;
                    INSERT ColdCall.Companies (WojewodztwoId,MiejscowoscId,AddressIsMissing,BlockedByRODO)
                    VALUES (2,1,1,0);
                    """;
                await setup.ExecuteNonQueryAsync(token);
            }
            var repository = new CompanyRepository(builder.ConnectionString);
            Assert(await repository.CountAsync(token) == 3, "RODO wykluczone");
            var companies = await repository.ReadAsync(0, 10, null, token);
            Assert(companies.All(c => c.CountyId == 1 && c.MunicipalityId == 1), "Odczyt identyfikatorow z Companies; brakujace pomijane");
            Assert(companies.All(c => c.County == "Powiat z Companies" && c.Municipality == "Gmina z Companies"), "Nazwy slownikow firmy do logowania");
            Assert(companies[0].LocationDescription.Contains("powiat: Powiat z Companies")
                && companies[0].LocationDescription.Contains("gmina: Gmina z Companies")
                && companies[0].LocationDescription.Contains("Wojew\u00f3dztwo: Dolno\u015bl\u0105skie")
                && companies[0].LocationDescription.Contains("miejscowo\u015b\u0107: Wa\u0142brzych"), "Opis wszystkich czterech lokalizacji");
            Assert((await repository.SaveAsync(companies[0], Address, token)).StartsWith("Zapisano", StringComparison.Ordinal), "Transakcja adres+powiazanie");
            Assert(!(await repository.SaveAsync(companies[0], Address, token)).StartsWith("Zapisano", StringComparison.Ordinal), "Brak duplikatu powiazania");
            Assert((await repository.SaveAsync(companies[1], Address, token)).StartsWith("Zapisano", StringComparison.Ordinal), "Ponowne uzycie adresu");
            await using var verify = new SqlConnection(builder.ConnectionString);
            await verify.OpenAsync(token);
            using var query = new SqlCommand("SELECT COUNT(*) FROM ColdCall.Adresy;", verify);
            Assert((int)(await query.ExecuteScalarAsync(token))! == 1, "Jeden adres dla dwoch firm");
            query.CommandText = "SELECT COUNT(*) FROM ColdCall.CompanyAddress;";
            Assert((int)(await query.ExecuteScalarAsync(token))! == 2, "Dwa powiazania");
            query.CommandText = "SELECT COUNT(*) FROM ColdCall.Companies WHERE AddressIsMissing=0;";
            Assert((int)(await query.ExecuteScalarAsync(token))! == 2, "Flaga zmieniona tylko zapisanym firmom");
            query.CommandText = "SELECT COUNT(*) FROM ColdCall.Adresy WHERE Location.STSrid=4326 AND ABS(Location.Lat-Latitude)<0.000001 AND ABS(Location.Long-Longitude)<0.000001;";
            Assert((int)(await query.ExecuteScalarAsync(token))! == 1, "geography GPS");
            query.CommandText = "SELECT COUNT(*) FROM ColdCall.Adresy A JOIN ColdCall.CompanyAddress CA ON CA.AddressId=A.Id JOIN ColdCall.Companies C ON C.Id=CA.CompanyId WHERE A.WojewodztwoId=C.WojewodztwoId AND A.PowiatId=C.PowiatId AND A.GminaId=C.GminaId AND A.MiejscowoscId=C.MiejscowoscId;";
            Assert((int)(await query.ExecuteScalarAsync(token))! == 2, "Wszystkie cztery Id zachowane mimo innych nazw w Geoportalu");
            query.CommandText = "SELECT COUNT(*) FROM ColdCall.Powiaty WHERE Name=N'Wałbrzych';";
            Assert((int)(await query.ExecuteScalarAsync(token))! == 0, "Brak tworzenia slownikow z odpowiedzi Geoportalu");
            var rolledBack = false;
            try { await repository.SaveAsync(companies[2], Address with { Municipality = "Nowa testowa gmina", X = Address.X + 10 }, token); }
            catch (SqlException ex) when (ex.Number == 51001) { rolledBack = true; }
            Assert(rolledBack, "Konflikt istniejacego punktu");
            query.CommandText = "SELECT COUNT(*) FROM ColdCall.Gminy WHERE Name=N'Nowa testowa gmina';";
            Assert((int)(await query.ExecuteScalarAsync(token))! == 0, "Rollback zmian slownikow");
            Assert(await repository.CountAsync(token) == 1, "Po zapisie firmy nie sa ponownie kwalifikowane");
            var cityAddress = Address with { IsCity = true, Street = null, Number = "", PostalCode = "", Ulic = "" };
            Assert((await repository.SaveAsync(companies[2], cityAddress, token)).StartsWith("Zapisano", StringComparison.Ordinal), "Zapis lokalizacji miejscowosci");
            query.CommandText = "SELECT COUNT(*) FROM ColdCall.Adresy WHERE Rodzaj=N'miejscowosc UUG (GPS przyblizony)' AND NrUlicy IS NULL AND Ulica IS NULL AND Latitude IS NOT NULL AND Longitude IS NOT NULL;";
            Assert((int)(await query.ExecuteScalarAsync(token))! == 1, "GPS miejscowosci bez fikcyjnego budynku");
            query.CommandText = "SELECT AddressIsMissing FROM ColdCall.Companies WHERE Id=" + companies[2].Id;
            Assert((bool)(await query.ExecuteScalarAsync(token))!, "GPS miejscowosci nie oznacza kompletnego adresu");
            Assert(await repository.CountAsync(token) == 0, "Zapisana lokalizacja miejscowosci nie jest ponawiana");
            query.CommandText = "INSERT ColdCall.Companies (WojewodztwoId,MiejscowoscId,PowiatId,GminaId,Street,BuildingNumber,PostalCode,AddressIsMissing,BlockedByRODO) VALUES (2,1,1,1,N'ul. Psie Pole',N'39',N'58-301',1,0); SELECT CONVERT(int,SCOPE_IDENTITY());";
            var changedId = (int)(await query.ExecuteScalarAsync(token))!;
            var changedCompany = (await repository.ReadAsync(0, 1, changedId, token)).Single();
            query.CommandText = "UPDATE ColdCall.Companies SET PowiatId=2 WHERE Id=" + changedId;
            await query.ExecuteNonQueryAsync(token);
            Assert(!(await repository.SaveAsync(changedCompany, Address, token)).StartsWith("Zapisano", StringComparison.Ordinal), "Zmiana identyfikatora podczas HTTP blokuje zapis nieaktualnego adresu");
            Console.WriteLine("PASS: izolowane testy SQL, FK, idempotencja, RODO, geography i rollback.");
        }
        finally
        {
            if (created)
            {
                // Only the database generated by this test may be dropped.
                if (!database.StartsWith("GeoPortalImporterTest_", StringComparison.Ordinal) || database.Length != 54)
                    throw new InvalidOperationException("Nieprawidlowa nazwa testowej bazy.");
                SqlConnection.ClearAllPools();
                using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", master);
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }

    private static void AssertInvalid(string json, string message)
    {
        try { ApiParser.Parse(json); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("FAIL: " + message);
    }

    private static string Json(ApiResult result) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["type"] = result.Type, ["found objects"] = result.Found, ["returned objects"] = result.Returned,
        ["results"] = result.Points.Select((p, i) => new { Key = (i + 1).ToString(), Point = p })
            .ToDictionary(x => x.Key, x => new Dictionary<string, object?>
            {
                ["city"] = x.Point.Address.City, ["street"] = x.Point.Address.Street,
                ["number"] = x.Point.Address.Number, ["code"] = x.Point.Address.PostalCode,
                ["teryt"] = x.Point.Address.Teryt, ["simc"] = x.Point.Address.Simc, ["ulic"] = x.Point.Address.Ulic,
                ["x"] = x.Point.Address.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["y"] = x.Point.Address.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["jednostka"] = "{Polska," + x.Point.Address.Province + "," + x.Point.Address.County + "," + x.Point.Address.Municipality + "}",
                ["geometry_wkt"] = x.Point.Geometry, ["accuracy"] = x.Point.Accuracy,
                ["city_accuracy"] = x.Point.CityAccuracy, ["street_accuracy"] = x.Point.StreetAccuracy
            })
    });

    private sealed class ConcurrentHandler(string response) : HttpMessageHandler
    {
        private readonly TaskCompletionSource allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int active;
        private int calls;
        public int MaximumActive { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var concurrent = Interlocked.Increment(ref active);
            if (concurrent == ParallelAddressLookup.Concurrency) MaximumActive = concurrent;
            var call = Interlocked.Increment(ref calls);
            if (call == ParallelAddressLookup.Concurrency) allStarted.TrySetResult();
            try
            {
                await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                await Task.Delay((ParallelAddressLookup.Concurrency + 1 - call) * 10, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }

    private sealed class FakeHandler(params string[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool FirstResponseIs429 { get; init; }
        public bool ImmediateRetry { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (FirstResponseIs429 && Calls == 1)
            {
                var retry = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                retry.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(retry);
            }
            var index = Calls - 1 - (FirstResponseIs429 ? 1 : 0);
            if (index >= responses.Length) throw new InvalidOperationException("Nieoczekiwany dodatkowy HTTP.");
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses[index]) };
            if (ImmediateRetry) result.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(result);
        }
    }
}
