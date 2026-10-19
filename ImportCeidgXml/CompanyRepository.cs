using System.Data;
using Microsoft.Data.SqlClient;

namespace ImportCeidgXml;

public sealed class CompanyRepository(string connectionString) : IAsyncDisposable
{
    private readonly SqlConnection connection = new(connectionString);
    private SqlTransaction? transaction;
    private readonly Dictionary<string, Dictionary<string, int>> dictionaries = new();
    private int businessTypeId;
    private byte activeStatusId;

    public async Task OpenAsync(CancellationToken token)
    {
        await connection.OpenAsync(token);
        await using var guard = new SqlCommand("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'ImportCeidgXml', @LockMode='Exclusive',
                @LockOwner='Session', @LockTimeout=0;
            IF @result < 0 THROW 50001, 'Another XML importer is running.', 1;
            """, connection);
        await guard.ExecuteNonQueryAsync(token);
        await using var settings = new SqlCommand("""
            SELECT TypDzialalnosciId FROM ColdCall.BusinessType
            WHERE TypDzialalnosci LIKE N'Jednoosobowa%';
            SELECT StatusId FROM ColdCall.CompanyStatus WHERE Nazwa=N'Aktywny';
            """, connection);
        await using (var reader = await settings.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Brak typu JDG.");
            businessTypeId = reader.GetInt32(0);
            if (await reader.ReadAsync(token)) throw new InvalidOperationException("Niejednoznaczny typ JDG.");
            await reader.NextResultAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Brak statusu Aktywny.");
            activeStatusId = reader.GetByte(0);
            if (await reader.ReadAsync(token)) throw new InvalidOperationException("Niejednoznaczny status Aktywny.");
        }
        foreach (var table in new[] { "Wojewodztwa", "Powiaty", "Gminy", "Miejscowosci" })
        {
            var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            await using var command = new SqlCommand($"SELECT Id, Name FROM ColdCall.[{table}];", connection);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) values.TryAdd(reader.GetString(1), reader.GetInt32(0));
            dictionaries.Add(table, values);
        }
    }

    public async Task<bool> InsertIfMissingAsync(CeidgCompany company, CancellationToken token)
    {
        transaction ??= (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var exists = new SqlCommand("SELECT TOP (1) Id FROM ColdCall.Companies WHERE Nip=@nip;", connection, transaction);
        exists.Parameters.Add("@nip", SqlDbType.NVarChar, 450).Value = company.Nip!;
        if (await exists.ExecuteScalarAsync(token) != null) return false;

        var province = await ResolveAsync("Wojewodztwa", company.Province, token);
        var county = await ResolveAsync("Powiaty", company.County, token);
        var commune = await ResolveAsync("Gminy", company.Commune, token);
        var city = await ResolveAsync("Miejscowosci", company.City, token);
        await using var insert = new SqlCommand("""
            INSERT ColdCall.Companies
            (Name,BusinessType,FirstName,LastName,Nip,Regon,WojewodztwoId,PowiatId,GminaId,MiejscowoscId,
             PostalCode,Street,BuildingNumber,ApartmentNumber,Phone,Email,GoogleEmail,Website,
             StartedAt,StatusDzialalnosciId,AddressIsMissing,ImportedOn,BatchImported)
            VALUES
            (@name,@type,@first,@last,@nip,@regon,@province,@county,@commune,@city,
             @postal,@street,@building,@apartment,@phone,@email,@googleEmail,@website,
             @started,@status,1,GETDATE(),1);
            """, connection, transaction);
        AddText(insert, "@name", company.Name, 1000);
        insert.Parameters.Add("@type", SqlDbType.Int).Value = businessTypeId;
        AddText(insert, "@first", company.FirstName);
        AddText(insert, "@last", company.LastName);
        AddText(insert, "@nip", company.Nip, 450);
        AddText(insert, "@regon", company.Regon, 50);
        AddId(insert, "@province", province);
        AddId(insert, "@county", county);
        AddId(insert, "@commune", commune);
        AddId(insert, "@city", city);
        AddText(insert, "@postal", company.PostalCode);
        AddText(insert, "@street", company.Street);
        AddText(insert, "@building", company.BuildingNumber);
        AddText(insert, "@apartment", company.ApartmentNumber);
        AddText(insert, "@phone", company.Phone);
        AddText(insert, "@email", company.Email);
        AddText(insert, "@googleEmail", company.GoogleEmail);
        AddText(insert, "@website", company.Website);
        insert.Parameters.Add("@started", SqlDbType.DateTime2).Value = (object?)company.StartedAt ?? DBNull.Value;
        insert.Parameters.Add("@status", SqlDbType.TinyInt).Value = activeStatusId;
        await insert.ExecuteNonQueryAsync(token);
        return true;
    }

    private async Task<int?> ResolveAsync(string table, string? name, CancellationToken token)
    {
        if (name == null) return null;
        var cache = dictionaries[table];
        if (cache.TryGetValue(name, out var id)) return id;
        // Table names are internal constants; names from XML are always parameters.
        await using var command = new SqlCommand($"""
            DECLARE @id int;
            SELECT @id=Id FROM ColdCall.[{table}] WITH (UPDLOCK,HOLDLOCK) WHERE Name=@name;
            IF @id IS NULL
            BEGIN
                INSERT ColdCall.[{table}] (Name) VALUES (@name);
                SET @id=CONVERT(int,SCOPE_IDENTITY());
            END;
            SELECT @id;
            """, connection, transaction);
        AddText(command, "@name", name, 250);
        id = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        cache.Add(name, id);
        return id;
    }

    public async Task CommitBatchAsync(CancellationToken token)
    {
        if (transaction == null) return;
        await transaction.CommitAsync(token);
        await transaction.DisposeAsync();
        transaction = null;
    }

    private static void AddText(SqlCommand command, string name, string? value, int size = -1) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;

    private static void AddId(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = (object?)value ?? DBNull.Value;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
            }
            if (connection.State == ConnectionState.Open)
            {
                await using var release = new SqlCommand(
                    "EXEC sys.sp_releaseapplock @Resource=N'ImportCeidgXml', @LockOwner='Session';", connection);
                await release.ExecuteNonQueryAsync();
            }
        }
        finally { await connection.DisposeAsync(); }
    }
}
