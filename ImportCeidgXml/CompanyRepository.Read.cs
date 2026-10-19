using System.Data;
using Microsoft.Data.SqlClient;

namespace ImportCeidgXml;

public sealed partial class CompanyRepository
{
    public async Task<bool> ExistsWithoutNipAsync(string? firstName, string? lastName, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM ColdCall.Companies
                WHERE Nip IS NULL
                  AND (FirstName COLLATE SQL_Latin1_General_CP1_CI_AS=@first OR (FirstName IS NULL AND @first IS NULL))
                  AND (LastName COLLATE SQL_Latin1_General_CP1_CI_AS=@last OR (LastName IS NULL AND @last IS NULL))
            ) THEN 1 ELSE 0 END;
            """, connection, transaction);
        AddText(command, "@first", firstName);
        AddText(command, "@last", lastName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) == 1;
    }

    public async Task<CeidgCompany?> FindByNipAsync(string nip, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT TOP (2) Id,Name,BusinessType,FirstName,LastName,Krs,Nip,Regon,WojewodztwoId,MiejscowoscId,GminaId,PowiatId,PostalCode,Street,BuildingNumber,ApartmentNumber,Phone,Email,GoogleEmail,Website,InstagramAccount,FacebookAccount,StartedAt,CreatedOn,DataModifiedOn,StatusDzialalnosciId,AddressIsMissing,BlockedByRODO,SkipAddressVerification,ImportedOn,BatchImported,SuspendedOn,ResumedOn
            FROM ColdCall.Companies WHERE Nip=@nip ORDER BY Id;

            SELECT cp.Id, cp.CompanyId, cp.PkdCodeId, p.Code, p.Name, cp.Prevailing
            FROM ColdCall.CompanyPkds cp
            JOIN ColdCall.PkdCodes p ON p.Id=cp.PkdCodeId
            WHERE cp.CompanyId=(SELECT TOP (1) Id FROM ColdCall.Companies WHERE Nip=@nip ORDER BY Id)
            ORDER BY cp.Prevailing DESC, p.Code, cp.Id;
            """, connection, transaction) { CommandTimeout = 180 };
        command.Parameters.Add("@nip", SqlDbType.NVarChar, 450).Value = nip;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var company = ReadCompany(reader);
        if (await reader.ReadAsync(token))
            throw new InvalidDataException($"More than one company has NIP {nip}; cannot choose a record safely.");
        await reader.NextResultAsync(token);
        var assignments = new List<CeidgCompanyPkd>();
        while (await reader.ReadAsync(token))
        {
            assignments.Add(new CeidgCompanyPkd(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        }
        return company with
        {
            CompanyPkds = assignments,
            PKDs = assignments.Select(p => p.Code).Distinct().ToList(),
            PrevailingPKD = assignments.FirstOrDefault(p => p.Prevailing)?.Code ?? ""
        };
    }

    private static CeidgCompany ReadCompany(SqlDataReader reader) => new()
    {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            BusinessType = reader.GetInt32(reader.GetOrdinal("BusinessType")),
            FirstName = NullableText(reader, "FirstName"),
            LastName = NullableText(reader, "LastName"),
            Krs = NullableText(reader, "Krs"),
            Nip = NullableText(reader, "Nip"),
            Regon = NullableText(reader, "Regon"),
            WojewodztwoId = NullableValue<int>(reader, "WojewodztwoId"),
            MiejscowoscId = NullableValue<int>(reader, "MiejscowoscId"),
            GminaId = NullableValue<int>(reader, "GminaId"),
            PowiatId = NullableValue<int>(reader, "PowiatId"),
            PostalCode = NullableText(reader, "PostalCode"),
            Street = NullableText(reader, "Street"),
            BuildingNumber = NullableText(reader, "BuildingNumber"),
            ApartmentNumber = NullableText(reader, "ApartmentNumber"),
            Phone = NullableText(reader, "Phone"),
            Email = NullableText(reader, "Email"),
            GoogleEmail = NullableText(reader, "GoogleEmail"),
            Website = NullableText(reader, "Website"),
            InstagramAccount = NullableText(reader, "InstagramAccount"),
            FacebookAccount = NullableText(reader, "FacebookAccount"),
            StartedAt = NullableValue<DateTime>(reader, "StartedAt"),
            CreatedOn = reader.GetDateTime(reader.GetOrdinal("CreatedOn")),
            DataModifiedOn = NullableValue<DateTime>(reader, "DataModifiedOn"),
            StatusDzialalnosciId = reader.GetByte(reader.GetOrdinal("StatusDzialalnosciId")),
            AddressIsMissing = NullableValue<bool>(reader, "AddressIsMissing"),
            BlockedByRODO = reader.GetBoolean(reader.GetOrdinal("BlockedByRODO")),
            SkipAddressVerification = reader.GetBoolean(reader.GetOrdinal("SkipAddressVerification")),
            ImportedOn = NullableValue<DateTime>(reader, "ImportedOn"),
            BatchImported = reader.GetBoolean(reader.GetOrdinal("BatchImported")),
            SuspendedOn = NullableValue<DateTime>(reader, "SuspendedOn"),
            ResumedOn = NullableValue<DateTime>(reader, "ResumedOn"),
    };

    private static T? NullableValue<T>(SqlDataReader reader, string column) where T : struct
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);
    }

    private static string? NullableText(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
