using Microsoft.Data.SqlClient;

namespace PrgAddressImporter;

internal static class IntegrationTests
{
    public static async Task RunAsync(string connectionString, ImportLog log, CancellationToken token)
    {
        var name = "PrgImporterTest_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var admin = new SqlConnection(builder.ConnectionString);
        await admin.OpenAsync(token);
        await ExecuteAsync(admin, $"CREATE DATABASE [{name}]", token);
        log.Write("TEST", $"Utworzono izolowaną bazę {name}. Nie używamy Firmy_temp.");
        try
        {
            builder.InitialCatalog = name;
            using var fixture = new SelfTests.Fixture();
            var options = new Options("import", fixture.Prg, fixture.Terc, builder.ConnectionString, 2, null, false);
            await using var checks = new SqlConnection(builder.ConnectionString);
            await checks.OpenAsync(token);
            await ExecuteAsync(checks, FixtureSql, token);
            await using (var importer = new ImportDatabase(options, log))
            {
                await importer.OpenAsync(token);
                await importer.ImportAsync(new SourceReader(log), token);
                await ExecuteAsync(checks, """
                    UPDATE dbo.PrgAdresy SET Ulic='12345';
                    ALTER TABLE dbo.PrgAdresy ALTER COLUMN Ulic varchar(10) NOT NULL;
                    UPDATE dbo.PrgImporterState SET Ready=0,SchemaVersion=1;
                    """, token);
                await importer.ImportAsync(new SourceReader(log), token);
                await importer.ImportAsync(new SourceReader(log), token);
                SelfTests.Equal(32, await ScalarAsync(checks, "SELECT CONVERT(int,COL_LENGTH('dbo.PrgAdresy','Ulic'))", token));
                SelfTests.Equal(2, await ScalarAsync(checks, "SELECT SchemaVersion FROM dbo.PrgImporterState WHERE Id=1", token));
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM dbo.PrgAdresy WHERE Ulic='060281614834'", token));
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM dbo.PrgAdresy", token));
                await importer.MatchAsync(token);
                SelfTests.Equal(0, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Adresy", token));
            }
            await ExecuteAsync(checks, """
                INSERT dbo.PrgAdresy(SourceId,GmlId,Snapshot,Terc,Simc,Miejscowosc,Ulic,Ulica,NrBudynku,KodPocztowy,X,Y,Srid,
                    Latitude,Longitude,CityNorm,StreetNorm,NumberNorm,MatchKey,Wojewodztwo,Powiat,Gmina,WojNorm,PowNorm)
                SELECT 'ambiguous','ambiguous',Snapshot,Terc,Simc,Miejscowosc,Ulic,Ulica,NrBudynku,KodPocztowy,X,Y,Srid,
                    Latitude,Longitude,CityNorm,StreetNorm,NumberNorm,MatchKey,Wojewodztwo,Powiat,Gmina,WojNorm,PowNorm FROM dbo.PrgAdresy;
                """, token);
            await using (var importer = new ImportDatabase(options with { Apply = true }, log))
            {
                await importer.OpenAsync(token);
                await importer.MatchAsync(token);
                SelfTests.Equal(0, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Adresy", token));
                await ExecuteAsync(checks, "DELETE dbo.PrgAdresy WHERE SourceId='ambiguous'", token);
            }
            await using (var importer = new ImportDatabase(options with { Apply = true, CompanyLimit = 1 }, log))
            {
                await importer.OpenAsync(token);
                await importer.MatchAsync(token);
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Companies WHERE AddressIsMissing=0 AND Id IN(1,5)", token));
            }
            await using (var importer = new ImportDatabase(options with { Apply = true }, log))
            {
                await importer.OpenAsync(token);
                await importer.MatchAsync(token);
                await importer.MatchAsync(token);
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Adresy", token));
                SelfTests.Equal(3, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.CompanyAddress", token));
                SelfTests.Equal(2, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Companies WHERE AddressIsMissing=0 AND Id IN(1,5)", token));
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Companies WHERE Id=2 AND AddressIsMissing=1 AND BlockedByRODO=1", token));
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.CompanyAddress WHERE CompanyId=7 AND AddressId=999", token));
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Adresy A JOIN ColdCall.Gminy G ON A.GminaId=G.Id WHERE G.Name=N'Gmina PRG' AND A.Location.STSrid=4326", token));
            }
            await ExecuteAsync(checks, """
                INSERT ColdCall.Companies(Name,WojewodztwoId,PowiatId,GminaId,MiejscowoscId,Street,BuildingNumber,AddressIsMissing,BlockedByRODO)
                VALUES(N'Rollback',1,1,1,1,N'Jagiellońska',N'10',1,0);
                """, token);
            await ExecuteAsync(checks, "CREATE TRIGGER ColdCall.FailWrite ON ColdCall.CompanyAddress AFTER INSERT AS THROW 50020,'Intentional test failure',1;", token);
            await using (var importer = new ImportDatabase(options with { Apply = true }, log))
            {
                await importer.OpenAsync(token);
                try
                {
                    await importer.MatchAsync(token);
                    throw new Exception("Test rollback: zapis powinien zakończyć się błędem.");
                }
                catch (SqlException exception) when (exception.Number == 50020) { }
                SelfTests.Equal(1, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.Companies WHERE Name=N'Rollback' AND AddressIsMissing=1", token));
                SelfTests.Equal(3, await ScalarAsync(checks, "SELECT COUNT(*) FROM ColdCall.CompanyAddress", token));
            }
            log.Write("TEST", "SQL: import powtarzalny, raport bez zapisu, pomijanie niejednoznaczności/RODO, limit, relacje i GPS: OK.");
        }
        finally
        {
            // The name is generated here, never accepted from user input; only this test database is dropped.
            await ExecuteAsync(admin, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", CancellationToken.None);
            log.Write("TEST", $"Usunięto wyłącznie tymczasową bazę {name}.");
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync(token) ?? throw new Exception("Brak wyniku SQL."));
    }

    private const string FixtureSql = """
        EXEC('CREATE SCHEMA ColdCall');
        """ + """

        CREATE TABLE ColdCall.Wojewodztwa(Id int IDENTITY PRIMARY KEY,Name nvarchar(250) NOT NULL UNIQUE);
        CREATE TABLE ColdCall.Powiaty(Id int IDENTITY PRIMARY KEY,Name nvarchar(250) NOT NULL UNIQUE);
        CREATE TABLE ColdCall.Gminy(Id int IDENTITY PRIMARY KEY,Name nvarchar(250) NOT NULL UNIQUE);
        CREATE TABLE ColdCall.Miejscowosci(Id int IDENTITY PRIMARY KEY,Name nvarchar(250) NOT NULL UNIQUE);
        INSERT ColdCall.Wojewodztwa(Name) VALUES(N'testowe');
        INSERT ColdCall.Wojewodztwa(Name) VALUES(N'Brak województwa');
        INSERT ColdCall.Powiaty(Name) VALUES(N'Testowy');
        INSERT ColdCall.Gminy(Name) VALUES(N'Inna gmina CEIDG');
        INSERT ColdCall.Miejscowosci(Name) VALUES(N'Warszawa');
        CREATE TABLE ColdCall.Companies(Id int IDENTITY PRIMARY KEY,Name nvarchar(1000) NOT NULL,
            WojewodztwoId int NULL,PowiatId int NULL,GminaId int NULL,MiejscowoscId int NULL,
            Street nvarchar(max) NULL,BuildingNumber nvarchar(max) NULL,AddressIsMissing bit NULL,BlockedByRODO bit NOT NULL);
        CREATE TABLE ColdCall.Adresy(Id int IDENTITY PRIMARY KEY,
            WojewodztwoId int NOT NULL REFERENCES ColdCall.Wojewodztwa(Id),
            PowiatId int NOT NULL REFERENCES ColdCall.Powiaty(Id),
            GminaId int NOT NULL REFERENCES ColdCall.Gminy(Id),
            MiejscowoscId int NOT NULL REFERENCES ColdCall.Miejscowosci(Id),
            Ulica nvarchar(200) NULL,NrUlicy nvarchar(50) NULL,Rodzaj nvarchar(200) NULL,
            Latitude decimal(9,6) NOT NULL,Longitude decimal(9,6) NOT NULL,Location geography NOT NULL,PrngId varchar(50) NULL);
        CREATE UNIQUE INDEX UX_Adresy_PrngId ON ColdCall.Adresy(PrngId) WHERE PrngId IS NOT NULL;
        CREATE TABLE ColdCall.CompanyAddress(CompanyAdresId int IDENTITY PRIMARY KEY,CompanyId int NOT NULL,AddressId int NOT NULL);
        INSERT ColdCall.Companies(Name,WojewodztwoId,PowiatId,GminaId,MiejscowoscId,Street,BuildingNumber,AddressIsMissing,BlockedByRODO)
        VALUES (N'Firma 1',2,1,1,1,N'ul. Jagiellońska',N'10',1,0),
               (N'RODO',1,1,1,1,N'Jagiellońska',N'10',1,1),
               (N'Nie brak',1,1,1,1,N'Jagiellońska',N'10',0,0),
               (N'Brak dopasowania',1,1,1,1,N'Inna',N'10',1,0),
               (N'Firma 5',1,1,1,1,N'Jagiellońska',N'10',1,0),
               (N'Flaga NULL',1,1,1,1,N'Jagiellońska',N'10',NULL,0),
               (N'Istniejąca relacja',1,1,1,1,N'Jagiellońska',N'10',1,0),
               (N'Niekompletny',1,1,1,NULL,NULL,NULL,1,0);
        INSERT ColdCall.CompanyAddress(CompanyId,AddressId) VALUES(7,999);
        """;
}
