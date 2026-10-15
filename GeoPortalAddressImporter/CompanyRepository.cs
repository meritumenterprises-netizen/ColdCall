using System.Data;
using Microsoft.Data.SqlClient;

namespace GeoPortalAddressImporter;

internal sealed class CompanyRepository(string connectionString)
{
    private const string Eligibility = """
        C.AddressIsMissing = 1 AND C.WojewodztwoId > 1 AND C.MiejscowoscId IS NOT NULL
        AND C.BlockedByRODO = 0
        AND NOT EXISTS (SELECT 1 FROM ColdCall.CompanyAddress CA WHERE CA.CompanyId = C.Id)
        """;

    public async Task<long> CountAsync(CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM ColdCall.Companies C WHERE " + Eligibility, connection);
        return (long)(await command.ExecuteScalarAsync(token))!;
    }

    public async Task<IReadOnlyList<Company>> ReadAsync(int afterId, int count, int? companyId, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var command = new SqlCommand($"""
            SELECT TOP (@Count) C.Id, C.WojewodztwoId, C.MiejscowoscId,
                W.Name, M.Name, C.Street, C.BuildingNumber, C.PostalCode
            FROM ColdCall.Companies C
            INNER JOIN ColdCall.Wojewodztwa W ON W.Id = C.WojewodztwoId
            INNER JOIN ColdCall.Miejscowosci M ON M.Id = C.MiejscowoscId
            WHERE {Eligibility} AND C.Id > @AfterId AND (@CompanyId IS NULL OR C.Id = @CompanyId)
            ORDER BY C.Id
            OPTION (RECOMPILE);
            """, connection);
        command.Parameters.Add("@Count", SqlDbType.Int).Value = count;
        command.Parameters.Add("@AfterId", SqlDbType.Int).Value = afterId;
        command.Parameters.Add("@CompanyId", SqlDbType.Int).Value = (object?)companyId ?? DBNull.Value;
        var companies = new List<Company>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            companies.Add(new Company(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        return companies;
    }

    public async Task<string> SaveAsync(Company company, Address address, CancellationToken token)
    {
        var (latitude, longitude) = Coordinates.Convert(address.X, address.Y);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        using var guard = new SqlCommand($"""
            SELECT C.Id FROM ColdCall.Companies C WITH (UPDLOCK, HOLDLOCK)
            WHERE C.Id = @Id AND {Eligibility}
                AND C.WojewodztwoId = @ProvinceId AND C.MiejscowoscId = @CityId
                AND COALESCE(C.Street, N'') COLLATE Latin1_General_100_BIN2 = COALESCE(@Street, N'')
                AND COALESCE(C.BuildingNumber, N'') COLLATE Latin1_General_100_BIN2 = COALESCE(@Number, N'')
                AND COALESCE(C.PostalCode, N'') COLLATE Latin1_General_100_BIN2 = COALESCE(@PostalCode, N'');
            """, connection, transaction);
        guard.Parameters.Add("@Id", SqlDbType.Int).Value = company.Id;
        guard.Parameters.Add("@ProvinceId", SqlDbType.Int).Value = company.ProvinceId;
        guard.Parameters.Add("@CityId", SqlDbType.Int).Value = company.CityId;
        AddText(guard, "@Street", company.Street, -1);
        AddText(guard, "@Number", company.Number, -1);
        AddText(guard, "@PostalCode", company.PostalCode, -1);
        if (await guard.ExecuteScalarAsync(token) == null)
        {
            await transaction.RollbackAsync(token);
            return "Firma zmieniona, zablokowana lub ma juz adres; pominieto.";
        }

        var countyId = await DictionaryIdAsync(connection, transaction, "Powiaty", address.County, token);
        var municipalityId = await DictionaryIdAsync(connection, transaction, "Gminy", address.Municipality, token);
        // City and province were validated against the firm's existing dictionary names.
        using var command = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @AddressId int;
            SELECT @AddressId = Id FROM ColdCall.Adresy WITH (UPDLOCK, HOLDLOCK)
            WHERE PrngId = @SourceKey;
            IF @AddressId IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM ColdCall.Adresy WHERE Id = @AddressId
                    AND WojewodztwoId = @ProvinceId AND MiejscowoscId = @CityId
                    AND PowiatId = @CountyId AND GminaId = @MunicipalityId
                    AND COALESCE(Ulica, N'') = COALESCE(@Street, N'') AND NrUlicy = @Number
                    AND Latitude = @Latitude AND Longitude = @Longitude)
                THROW 51001, 'Istniejacy adres UUG ma inne dane; wymagane reczne sprawdzenie.', 1;
            IF @AddressId IS NULL
            BEGIN
                INSERT ColdCall.Adresy
                    (WojewodztwoId, PowiatId, GminaId, MiejscowoscId, Ulica, NrUlicy,
                     Rodzaj, Latitude, Longitude, Location, PrngId)
                VALUES (@ProvinceId, @CountyId, @MunicipalityId, @CityId, @Street, @Number,
                    N'punkt adresowy UUG', @Latitude, @Longitude,
                    geography::Point(@Latitude, @Longitude, 4326), @SourceKey);
                SET @AddressId = CONVERT(int, SCOPE_IDENTITY());
            END;
            INSERT ColdCall.CompanyAddress (CompanyId, AddressId) VALUES (@CompanyId, @AddressId);
            UPDATE ColdCall.Companies SET AddressIsMissing = 0 WHERE Id = @CompanyId;
            SELECT @AddressId;
            """, connection, transaction);
        command.Parameters.Add("@ProvinceId", SqlDbType.Int).Value = company.ProvinceId;
        command.Parameters.Add("@CityId", SqlDbType.Int).Value = company.CityId;
        command.Parameters.Add("@CountyId", SqlDbType.Int).Value = countyId;
        command.Parameters.Add("@MunicipalityId", SqlDbType.Int).Value = municipalityId;
        command.Parameters.Add("@CompanyId", SqlDbType.Int).Value = company.Id;
        AddText(command, "@Street", address.Street, 200);
        AddText(command, "@Number", address.Number, 50);
        command.Parameters.Add("@SourceKey", SqlDbType.VarChar, 50).Value = address.SourceKey;
        foreach (var (name, value) in new[] { ("@Latitude", latitude), ("@Longitude", longitude) })
        {
            var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
            parameter.Precision = 9;
            parameter.Scale = 6;
            parameter.Value = value;
        }
        var addressId = (int)(await command.ExecuteScalarAsync(token))!;
        await transaction.CommitAsync(token);
        return $"Zapisano AddressId={addressId}; powiat={address.County}; gmina={address.Municipality}; GPS={latitude:F6},{longitude:F6}.";
    }

    private static async Task<int> DictionaryIdAsync(SqlConnection connection, SqlTransaction transaction,
        string table, string name, CancellationToken token)
    {
        if (table is not ("Powiaty" or "Gminy")) throw new ArgumentException("Nieznany slownik.", nameof(table));
        using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            DECLARE @Id int;
            SELECT @Id = Id FROM ColdCall.{table} WITH (UPDLOCK, HOLDLOCK) WHERE Name = @Name;
            IF @Id IS NULL
            BEGIN
                INSERT ColdCall.{table} (Name) VALUES (@Name);
                SET @Id = CONVERT(int, SCOPE_IDENTITY());
            END;
            SELECT @Id;
            """, connection, transaction);
        AddText(command, "@Name", name, 250);
        return (int)(await command.ExecuteScalarAsync(token))!;
    }

    private static void AddText(SqlCommand command, string name, string? value, int length) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, length).Value = (object?)value ?? DBNull.Value;
}
