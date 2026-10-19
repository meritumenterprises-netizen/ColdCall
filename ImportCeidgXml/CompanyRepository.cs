using System.Data;
using Microsoft.Data.SqlClient;

namespace ImportCeidgXml;

public sealed partial class CompanyRepository(string connectionString) : IAsyncDisposable
{
    private readonly SqlConnection connection = new(connectionString);
    private SqlTransaction? transaction;
    private readonly Dictionary<string, Dictionary<string, int>> dictionaries = new();
    private int businessTypeId;
    private readonly Dictionary<string, byte> statusIds = new(StringComparer.OrdinalIgnoreCase);

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
            SELECT StatusId,Nazwa FROM ColdCall.CompanyStatus;
            """, connection);
        await using (var reader = await settings.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Brak typu JDG.");
            businessTypeId = reader.GetInt32(0);
            if (await reader.ReadAsync(token)) throw new InvalidOperationException("Niejednoznaczny typ JDG.");
            await reader.NextResultAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (!statusIds.TryAdd(reader.GetString(1).Trim(), reader.GetByte(0)))
                    throw new InvalidOperationException("Niejednoznaczna nazwa CompanyStatus.");
            }
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
        if (string.IsNullOrWhiteSpace(company.Nip))
            company = company with { Nip = null };
        _ = ValidateStatusName(company.Status);
        transaction ??= (SqlTransaction)await connection.BeginTransactionAsync(token);
        var assignments = await ResolvePkdsAsync(company, token);
        var statusId = await ResolveStatusIdAsync(company.Status, token);
        var province = company.WojewodztwoId
            ?? (company.Province is null ? (int?)null : FindProvinceId(company.Province));
        var county = await ResolveAsync("Powiaty", company.County, token);
        var commune = await ResolveAsync("Gminy", company.Commune, token);
        var city = await ResolveAsync("Miejscowosci", company.City, token);
        await using var insert = new SqlCommand("""
            INSERT ColdCall.Companies
            (Name,BusinessType,FirstName,LastName,Nip,Regon,Krs,WojewodztwoId,PowiatId,GminaId,MiejscowoscId,
             PostalCode,Street,BuildingNumber,ApartmentNumber,Phone,Email,GoogleEmail,Website,
             StartedAt,StatusDzialalnosciId,AddressIsMissing,ImportedOn,BatchImported,
             InstagramAccount,FacebookAccount,SuspendedOn,ResumedOn)
            OUTPUT INSERTED.Id
            VALUES
            (@name,@type,@first,@last,@nip,@regon,@krs,@province,@county,@commune,@city,
             @postal,@street,@building,@apartment,@phone,@email,@googleEmail,@website,
             @started,@status,1,GETDATE(),1,@instagram,@facebook,@suspended,@resumed);
            """, connection, transaction);
        AddText(insert, "@name", company.Name, 1000);
        insert.Parameters.Add("@type", SqlDbType.Int).Value = businessTypeId;
        AddText(insert, "@first", company.FirstName);
        AddText(insert, "@last", company.LastName);
        AddText(insert, "@nip", company.Nip, 450);
        AddText(insert, "@regon", company.Regon, 50);
        AddText(insert, "@krs", company.Krs, 450);
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
        AddText(insert, "@instagram", company.InstagramAccount);
        AddText(insert, "@facebook", company.FacebookAccount);
        insert.Parameters.Add("@suspended", SqlDbType.DateTime).Value =
            IsSuspended(company.Status) ? (object?)company.SuspendedOn ?? DBNull.Value : DBNull.Value;
        insert.Parameters.Add("@resumed", SqlDbType.DateTime).Value = (object?)company.ResumedOn ?? DBNull.Value;
        insert.Parameters.Add("@started", SqlDbType.DateTime2).Value = (object?)company.StartedAt ?? DBNull.Value;
        insert.Parameters.Add("@status", SqlDbType.TinyInt).Value = statusId;
        var companyId = Convert.ToInt32(await insert.ExecuteScalarAsync(token));
        await SavePkdsAsync(companyId, assignments, token);
        return true;
    }

    public int FindProvinceId(string name)
    {
        static string Normalize(string value) => string.Join(" ", value.Replace('-', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var normalized = Normalize(name);
        var matches = dictionaries["Wojewodztwa"]
            .Where(entry => string.Equals(Normalize(entry.Key), normalized, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Value).Distinct().ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Nie znaleziono jednoznacznego wojewodztwa w ColdCall.Wojewodztwa: {name}");
        return matches[0];
    }

    private async Task<int?> ResolveAsync(string table, string? name, CancellationToken token)
    {
        if (name == null) return null;
        var cache = dictionaries[table];
        if (cache.TryGetValue(name, out var id)) return id;
        // Table names are internal constants; names from XML are always parameters.
        await using var command = new SqlCommand($"""
            DECLARE @id int;
            SELECT @id=Id FROM ColdCall.[{table}] WITH (UPDLOCK,HOLDLOCK)
            WHERE Name COLLATE SQL_Latin1_General_CP1_CI_AS=@name;
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
