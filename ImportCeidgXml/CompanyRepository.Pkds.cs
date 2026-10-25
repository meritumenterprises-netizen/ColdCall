using System.Data;
using Microsoft.Data.SqlClient;

namespace ImportCeidgXml;

public sealed partial class CompanyRepository
{
    private Dictionary<string, List<int>>? pkdIds;

    public async Task ValidatePkdsAsync(CeidgCompany company, CancellationToken token)
    {
        _ = ValidateStatusName(company.Status);
        _ = await ResolvePkdsAsync(company, token, createMissing: false);
    }

    private async Task<IReadOnlyList<(int Id, bool Prevailing)>> ResolvePkdsAsync(
        CeidgCompany company, CancellationToken token, bool createMissing = true)
    {
        if (pkdIds == null)
        {
            pkdIds = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            await using var command = new SqlCommand(
                "SELECT Id, CodeWithoutDots FROM ColdCall.PkdCodes;", connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var code = NormalizePkd(reader.GetString(1));
                if (!pkdIds.TryGetValue(code, out var ids))
                    pkdIds.Add(code, ids = new List<int>());
                ids.Add(reader.GetInt32(0));
            }
        }

        var main = NormalizePkd(company.PrevailingPKD);
        var codes = company.PKDs.Select(NormalizePkd).Append(main)
            .Where(code => code.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new List<(int Id, bool Prevailing)>();
        foreach (var code in codes)
        {
            if (code.Length > 450)
                throw new InvalidDataException($"NIP {company.Nip}: PKD exceeds Code column length.");
            if (!pkdIds.TryGetValue(code, out var ids))
            {
                if (!createMissing) continue;
                var id = await CreateMissingPkdAsync(code, token);
                pkdIds.Add(code, ids = new List<int> { id });
            }
            if (ids.Count != 1)
                throw new InvalidDataException($"NIP {company.Nip}: PKD {code} ambiguous in ColdCall.PkdCodes.");
            result.Add((ids[0], string.Equals(code, main, StringComparison.OrdinalIgnoreCase)));
        }
        return result;
    }

    private async Task<int> CreateMissingPkdAsync(string normalizedCode, CancellationToken token)
    {
        var code = normalizedCode.Length == 5 && normalizedCode[..4].All(char.IsAsciiDigit)
            && char.IsAsciiLetter(normalizedCode[4])
            ? $"{normalizedCode[..2]}.{normalizedCode[2..4]}.{normalizedCode[4]}" : normalizedCode;
        await using var command = new SqlCommand("""
            DECLARE @id int, @count int;
            SELECT @id=MIN(Id),@count=COUNT(*)
            FROM ColdCall.PkdCodes WITH (UPDLOCK,HOLDLOCK)
            WHERE CodeWithoutDots COLLATE SQL_Latin1_General_CP1_CI_AS=@normalized;
            IF @count>1 THROW 50004, 'Ambiguous PKD code.', 1;
            IF @id IS NULL
            BEGIN
                INSERT ColdCall.PkdCodes (Code,Name,NeedsClarification)
                VALUES (@code,N'',1);
                SET @id=CONVERT(int,SCOPE_IDENTITY());
            END;
            SELECT @id;
            """, connection, transaction);
        command.Parameters.Add("@normalized", SqlDbType.NVarChar, 450).Value = normalizedCode;
        command.Parameters.Add("@code", SqlDbType.NVarChar, 450).Value = code;
        return Convert.ToInt32(await command.ExecuteScalarAsync(token));
    }

    private static string NormalizePkd(string? value)
    {
        var code = (value ?? "").Trim().Replace(".", "").ToUpperInvariant();
        return code is "-" or "N/A" or "N/A." ? "" : code;
    }

    private async Task SavePkdsAsync(int companyId,
        IReadOnlyList<(int Id, bool Prevailing)> assignments, CancellationToken token)
    {
        var desiredIds = assignments.Select(assignment => assignment.Id).ToHashSet();
        var obsoleteIds = new List<int>();
        await using (var existing = new SqlCommand(
            "SELECT PkdCodeId FROM ColdCall.CompanyPkds WHERE CompanyId=@company;", connection, transaction))
        {
            existing.Parameters.Add("@company", SqlDbType.Int).Value = companyId;
            await using var reader = await existing.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                if (!desiredIds.Contains(reader.GetInt32(0))) obsoleteIds.Add(reader.GetInt32(0));
        }
        foreach (var pkdId in obsoleteIds)
        {
            await using var delete = new SqlCommand(
                "DELETE FROM ColdCall.CompanyPkds WHERE CompanyId=@company AND PkdCodeId=@pkd;", connection, transaction);
            delete.Parameters.Add("@company", SqlDbType.Int).Value = companyId;
            delete.Parameters.Add("@pkd", SqlDbType.Int).Value = pkdId;
            await delete.ExecuteNonQueryAsync(token);
        }

        foreach (var assignment in assignments)
        {
            await using var command = new SqlCommand("""
                UPDATE ColdCall.CompanyPkds WITH (UPDLOCK,HOLDLOCK)
                SET Prevailing=@prevailing
                WHERE CompanyId=@company AND PkdCodeId=@pkd;
                IF @@ROWCOUNT=0
                    INSERT ColdCall.CompanyPkds (CompanyId,PkdCodeId,Prevailing)
                    VALUES (@company,@pkd,@prevailing);
                """, connection, transaction);
            command.Parameters.Add("@company", SqlDbType.Int).Value = companyId;
            command.Parameters.Add("@pkd", SqlDbType.Int).Value = assignment.Id;
            command.Parameters.Add("@prevailing", SqlDbType.Bit).Value = assignment.Prevailing;
            await command.ExecuteNonQueryAsync(token);
        }
    }
}
