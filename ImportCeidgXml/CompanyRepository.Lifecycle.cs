using System.Data;
using Microsoft.Data.SqlClient;

namespace ImportCeidgXml;

public sealed partial class CompanyRepository
{
    private static bool IsSuspended(string? status) =>
        string.Equals(status?.Trim(), "Zawieszony", StringComparison.OrdinalIgnoreCase);

    private static string ValidateStatusName(string? status)
    {
        var name = status?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            throw new InvalidDataException("StatusDzialalnosci must contain 1 to 100 characters.");
        return name;
    }

    private async Task<byte> ResolveStatusIdAsync(string? status, CancellationToken token)
    {
        var name = ValidateStatusName(status);
        if (statusIds.TryGetValue(name, out var id)) return id;
        // StatusId is tinyint without IDENTITY; serialize allocation inside the import transaction.
        await using var command = new SqlCommand("""
            DECLARE @id int;
            SELECT @id=StatusId FROM ColdCall.CompanyStatus WITH (TABLOCKX,HOLDLOCK)
            WHERE Nazwa COLLATE SQL_Latin1_General_CP1_CI_AS=@name;
            IF @id IS NULL
            BEGIN
                SELECT @id=ISNULL(MAX(CONVERT(int,StatusId)),0)+1 FROM ColdCall.CompanyStatus;
                IF @id>255 THROW 50003, 'CompanyStatus tinyint ID range exhausted.', 1;
                INSERT ColdCall.CompanyStatus (StatusId,Nazwa) VALUES (@id,@name);
            END;
            SELECT CONVERT(tinyint,@id);
            """, connection, transaction);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = name;
        id = Convert.ToByte(await command.ExecuteScalarAsync(token));
        statusIds.Add(name, id);
        return id;
    }

    private async Task UpdateLifecycleAsync(int companyId, CeidgCompany company,
        byte statusId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            UPDATE ColdCall.Companies
            SET StartedAt=COALESCE(@started,StartedAt),
                StatusDzialalnosciId=@status,
                ResumedOn=COALESCE(@resumed,ResumedOn),
                SuspendedOn=@suspended
            WHERE Id=@id;
            """, connection, transaction);
        command.Parameters.Add("@id", SqlDbType.Int).Value = companyId;
        command.Parameters.Add("@status", SqlDbType.TinyInt).Value = statusId;
        command.Parameters.Add("@started", SqlDbType.DateTime2).Value = (object?)company.StartedAt ?? DBNull.Value;
        command.Parameters.Add("@resumed", SqlDbType.DateTime).Value = (object?)company.ResumedOn ?? DBNull.Value;
        command.Parameters.Add("@suspended", SqlDbType.DateTime).Value =
            IsSuspended(company.Status) ? (object?)company.SuspendedOn ?? DBNull.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(token);
    }
}
