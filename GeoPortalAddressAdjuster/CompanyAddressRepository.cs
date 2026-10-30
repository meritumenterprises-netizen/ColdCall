using System.Data;
using GeoPortalAddressImporter;
using Microsoft.Data.SqlClient;

namespace GeoPortalAddressAdjuster;

public sealed class CompanyAddressRepository(string connectionString)
{
    private const string Eligibility = """
        C.WojewodztwoId IS NOT NULL AND C.PowiatId IS NOT NULL
        AND C.GminaId IS NOT NULL AND C.MiejscowoscId IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM ColdCall.CompanyAddress CA WHERE CA.CompanyId = C.Id)
        """;

    public async Task<long> CountAsync(CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM ColdCall.Companies C WHERE " + Eligibility, connection);
        return (long)(await command.ExecuteScalarAsync(token))!;
    }

    public async Task<List<GeoportalAddressRequest>> ReadBatchAsync(int afterId, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var command = new SqlCommand($"""
            SELECT TOP (100) C.Id, C.WojewodztwoId, C.PowiatId, C.GminaId, C.MiejscowoscId,
                W.Name, P.Name, G.Name, M.Name, C.Street, C.BuildingNumber, C.PostalCode
            FROM ColdCall.Companies C
            LEFT JOIN ColdCall.Wojewodztwa W ON W.Id = C.WojewodztwoId
            LEFT JOIN ColdCall.Powiaty P ON P.Id = C.PowiatId
            LEFT JOIN ColdCall.Gminy G ON G.Id = C.GminaId
            LEFT JOIN ColdCall.Miejscowosci M ON M.Id = C.MiejscowoscId
            WHERE {Eligibility} AND C.Id > @AfterId ORDER BY C.Id;
            """, connection);
        command.Parameters.Add("@AfterId", SqlDbType.Int).Value = afterId;
        var companies = new List<GeoportalAddressRequest>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            companies.Add(new GeoportalAddressRequest
            {
                CompanyId = reader.GetInt32(0), ProvinceId = reader.GetInt32(1), CountyId = reader.GetInt32(2),
                MunicipalityId = reader.GetInt32(3), CityId = reader.GetInt32(4),
                Province = ReadText(reader, 5) ?? "", County = ReadText(reader, 6) ?? "",
                Municipality = ReadText(reader, 7) ?? "", City = ReadText(reader, 8) ?? "",
                Street = ReadText(reader, 9), BuildingNumber = ReadText(reader, 10), PostalCode = ReadText(reader, 11)
            });
        return companies;
    }

    public async Task<int> AssignBatchAsync(IReadOnlyList<AddressLookup> items, CancellationToken token)
    {
        if (items.Count > 100) throw new ArgumentOutOfRangeException(nameof(items));
        var matches = items.Where(x => x.Result.Found && x.Result.ErrorCode == GeoportalAddressErrorCode.None
            && !string.IsNullOrWhiteSpace(x.Result.SourceKey)).ToArray();
        if (matches.Length == 0) return 0;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            BEGIN TRY
                BEGIN TRANSACTION;
                SELECT J.* INTO #Eligible
                FROM OPENJSON(@Json) WITH (
                    CompanyId int '$.Company.CompanyId',
                    ProvinceId int '$.Company.ProvinceId', CountyId int '$.Company.CountyId',
                    MunicipalityId int '$.Company.MunicipalityId', CityId int '$.Company.CityId',
                    OriginalStreet nvarchar(max) '$.Company.Street',
                    OriginalNumber nvarchar(max) '$.Company.BuildingNumber',
                    PostalCode nvarchar(max) '$.Company.PostalCode',
                    Street nvarchar(max) '$.Result.Street', Number nvarchar(max) '$.Result.BuildingNumber',
                    SourceKey varchar(50) '$.Result.SourceKey', IsCity bit '$.Result.IsCity',
                    Latitude decimal(9,6) '$.Result.Latitude', Longitude decimal(9,6) '$.Result.Longitude'
                ) J
                INNER JOIN ColdCall.Companies C WITH (UPDLOCK, HOLDLOCK) ON C.Id = J.CompanyId
                WHERE {Eligibility}
                    AND C.WojewodztwoId = J.ProvinceId AND C.PowiatId = J.CountyId
                    AND C.GminaId = J.MunicipalityId AND C.MiejscowoscId = J.CityId
                    AND COALESCE(C.Street, N'') COLLATE Latin1_General_100_BIN2 = COALESCE(J.OriginalStreet, N'')
                    AND COALESCE(C.BuildingNumber, N'') COLLATE Latin1_General_100_BIN2 = COALESCE(J.OriginalNumber, N'')
                    AND COALESCE(C.PostalCode, N'') COLLATE Latin1_General_100_BIN2 = COALESCE(J.PostalCode, N'');
                ;WITH Sources AS (
                    SELECT *, ROW_NUMBER() OVER (PARTITION BY SourceKey ORDER BY CompanyId) AS Position
                    FROM #Eligible
                )
                INSERT ColdCall.Adresy
                    (WojewodztwoId, PowiatId, GminaId, MiejscowoscId, Ulica, NrUlicy,
                     Rodzaj, Latitude, Longitude, Location, PrngId)
                SELECT E.ProvinceId, E.CountyId, E.MunicipalityId, E.CityId, E.Street, E.Number,
                    CASE WHEN E.IsCity = 1 THEN N'miejscowosc UUG (GPS przyblizony)' ELSE N'punkt adresowy UUG' END,
                    E.Latitude, E.Longitude, geography::Point(E.Latitude, E.Longitude, 4326), E.SourceKey
                FROM Sources E WHERE Position = 1
                    AND NOT EXISTS (SELECT 1 FROM ColdCall.Adresy A WITH (UPDLOCK, HOLDLOCK) WHERE A.PrngId = E.SourceKey);
                INSERT ColdCall.CompanyAddress (CompanyId, AddressId)
                SELECT E.CompanyId, A.Id FROM #Eligible E
                CROSS APPLY (SELECT TOP (1) Id FROM ColdCall.Adresy WHERE PrngId = E.SourceKey ORDER BY Id) A;
                DECLARE @Saved int = @@ROWCOUNT;
                UPDATE C SET AddressIsMissing = E.IsCity
                FROM ColdCall.Companies C INNER JOIN #Eligible E ON E.CompanyId = C.Id;
                COMMIT TRANSACTION;
                SELECT @Saved;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """, connection);
        command.CommandTimeout = 120;
        command.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(matches);
        return (int)(await command.ExecuteScalarAsync(token))!;
    }

    private static string? ReadText(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static void AddText(SqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, -1).Value = (object?)value ?? DBNull.Value;
}
