using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace GeoPortalAddressImporter;

internal sealed partial class CompanyRepository
{
    public async Task<IReadOnlyDictionary<int, string>> SaveBatchAsync(
        IReadOnlyList<AddressLookupResult> results, CancellationToken token)
    {
        if (results.Count > 100) throw new ArgumentOutOfRangeException(nameof(results));
        var matches = results.Where(x => x.Error == null && x.Match?.Address != null).ToArray();
        var messages = new Dictionary<int, string>();
        var rows = new List<AddressSaveItem>();
        foreach (var match in matches)
        {
            var company = match.Company;
            var address = match.Match!.Address!;
            if (company.CountyId is not > 0 || company.MunicipalityId is not > 0)
            {
                messages.Add(company.Id, "Brak PowiatId lub GminaId w Companies; pominieto zapis adresu.");
                continue;
            }
            var (latitude, longitude) = Coordinates.Convert(address.X, address.Y);
            rows.Add(new AddressSaveItem
            {
                CompanyId = company.Id, ProvinceId = company.ProvinceId, CityId = company.CityId,
                CountyId = company.CountyId.Value, MunicipalityId = company.MunicipalityId.Value,
                OriginalStreet = company.Street, OriginalNumber = company.Number, PostalCode = company.PostalCode,
                Street = address.Street, Number = address.IsCity ? null : address.Number,
                Kind = address.IsCity ? "miejscowosc UUG (GPS przyblizony)" : "punkt adresowy UUG",
                SourceKey = address.SourceKeyFor(company), IsCity = address.IsCity,
                Latitude = latitude, Longitude = longitude
            });
        }
        if (rows.Count == 0) return messages;
        try
        {
            var ids = await WriteBatchAsync(rows, token);
            foreach (var match in matches)
            {
                if (messages.ContainsKey(match.Company.Id)) continue;
                if (!ids.TryGetValue(match.Company.Id, out var id))
                    messages.Add(match.Company.Id, "Firma zmieniona, zablokowana lub ma juz adres; pominieto.");
                else
                {
                    var row = rows.Single(x => x.CompanyId == match.Company.Id);
                    messages.Add(row.CompanyId, $"Zapisano AddressId={id}; rodzaj={(row.IsCity ? "miejscowosc" : "budynek")}; {match.Company.LocationDescription}; GPS={row.Latitude:F6},{row.Longitude:F6}.");
                }
            }
            return messages;
        }
        catch (SqlException) when (!token.IsCancellationRequested && rows.Count > 1)
        {
            // The failed batch is rolled back before retrying through the original guarded path.
            foreach (var match in matches)
                messages[match.Company.Id] = await SaveAsync(match.Company, match.Match!.Address!, token);
            return messages;
        }
    }

    private async Task<Dictionary<int, int>> WriteBatchAsync(List<AddressSaveItem> rows, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            DECLARE @Rows TABLE (
                CompanyId int PRIMARY KEY, ProvinceId int, CityId int, CountyId int, MunicipalityId int,
                OriginalStreet nvarchar(max), OriginalNumber nvarchar(max), PostalCode nvarchar(max),
                Street nvarchar(200), Number nvarchar(50), Kind nvarchar(200), SourceKey varchar(50),
                IsCity bit, Latitude decimal(9,6), Longitude decimal(9,6));
            INSERT @Rows SELECT * FROM OPENJSON(@Json) WITH (
                CompanyId int, ProvinceId int, CityId int, CountyId int, MunicipalityId int,
                OriginalStreet nvarchar(max), OriginalNumber nvarchar(max), PostalCode nvarchar(max),
                Street nvarchar(200), Number nvarchar(50), Kind nvarchar(200), SourceKey varchar(50),
                IsCity bit, Latitude decimal(9,6), Longitude decimal(9,6));
            DECLARE @Saved TABLE (CompanyId int PRIMARY KEY, AddressId int NOT NULL);
            DECLARE @CompanyId int, @ProvinceId int, @CityId int, @CountyId int, @MunicipalityId int,
                @OriginalStreet nvarchar(max), @OriginalNumber nvarchar(max), @PostalCode nvarchar(max),
                @Street nvarchar(200), @Number nvarchar(50), @Kind nvarchar(200), @SourceKey varchar(50),
                @IsCity bit, @Latitude decimal(9,6), @Longitude decimal(9,6), @AddressId int;
            DECLARE items CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @Rows ORDER BY CompanyId;
            BEGIN TRY
                BEGIN TRANSACTION;
                OPEN items;
                FETCH NEXT FROM items INTO @CompanyId,@ProvinceId,@CityId,@CountyId,@MunicipalityId,
                    @OriginalStreet,@OriginalNumber,@PostalCode,@Street,@Number,@Kind,@SourceKey,
                    @IsCity,@Latitude,@Longitude;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    IF EXISTS (SELECT 1 FROM ColdCall.Companies C WITH (UPDLOCK,HOLDLOCK)
                        WHERE C.Id=@CompanyId AND {Eligibility}
                            AND C.WojewodztwoId=@ProvinceId AND C.MiejscowoscId=@CityId
                            AND C.PowiatId=@CountyId AND C.GminaId=@MunicipalityId
                            AND COALESCE(C.Street,N'') COLLATE Latin1_General_100_BIN2 = COALESCE(@OriginalStreet,N'')
                            AND COALESCE(C.BuildingNumber,N'') COLLATE Latin1_General_100_BIN2 = COALESCE(@OriginalNumber,N'')
                            AND COALESCE(C.PostalCode,N'') COLLATE Latin1_General_100_BIN2 = COALESCE(@PostalCode,N''))
                    BEGIN
                        SET @AddressId=NULL;
                        SELECT @AddressId=Id FROM ColdCall.Adresy WITH (UPDLOCK,HOLDLOCK) WHERE PrngId=@SourceKey;
                        IF @AddressId IS NOT NULL AND NOT EXISTS (
                            SELECT 1 FROM ColdCall.Adresy WHERE Id=@AddressId
                                AND WojewodztwoId=@ProvinceId AND MiejscowoscId=@CityId
                                AND PowiatId=@CountyId AND GminaId=@MunicipalityId
                                AND COALESCE(Ulica,N'')=COALESCE(@Street,N'')
                                AND COALESCE(NrUlicy,N'')=COALESCE(@Number,N'') AND Rodzaj=@Kind
                                AND Latitude=@Latitude AND Longitude=@Longitude)
                            THROW 51001, 'Istniejacy adres UUG ma inne dane; wymagane reczne sprawdzenie.', 1;
                        IF @AddressId IS NULL
                        BEGIN
                            INSERT ColdCall.Adresy (WojewodztwoId,PowiatId,GminaId,MiejscowoscId,Ulica,NrUlicy,
                                Rodzaj,Latitude,Longitude,Location,PrngId)
                            VALUES (@ProvinceId,@CountyId,@MunicipalityId,@CityId,@Street,@Number,
                                @Kind,@Latitude,@Longitude,geography::Point(@Latitude,@Longitude,4326),@SourceKey);
                            SET @AddressId=CONVERT(int,SCOPE_IDENTITY());
                        END;
                        INSERT ColdCall.CompanyAddress (CompanyId,AddressId) VALUES (@CompanyId,@AddressId);
                        UPDATE ColdCall.Companies SET AddressIsMissing=@IsCity WHERE Id=@CompanyId;
                        INSERT @Saved VALUES (@CompanyId,@AddressId);
                    END;
                    FETCH NEXT FROM items INTO @CompanyId,@ProvinceId,@CityId,@CountyId,@MunicipalityId,
                        @OriginalStreet,@OriginalNumber,@PostalCode,@Street,@Number,@Kind,@SourceKey,
                        @IsCity,@Latitude,@Longitude;
                END;
                CLOSE items;
                DEALLOCATE items;
                COMMIT TRANSACTION;
                SELECT CompanyId,AddressId FROM @Saved;
            END TRY
            BEGIN CATCH
                IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """, connection);
        command.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows);
        var ids = new Dictionary<int, int>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) ids.Add(reader.GetInt32(0), reader.GetInt32(1));
        return ids;
    }
}
