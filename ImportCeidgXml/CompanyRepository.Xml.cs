using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace ImportCeidgXml;

public sealed partial class CompanyRepository
{
    public async Task UpdateCompanyXmlAsync(CeidgCompany company, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(company.Nip)) return;
        if (company.Nip.Length > 32)
            throw new InvalidDataException("NIP exceeds the CompanyXml column length of 32 characters.");
        if (string.IsNullOrWhiteSpace(company.SourceXml))
            throw new InvalidDataException("Missing source XML for the CompanyXml snapshot.");

        var md5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(company.SourceXml)));
        transaction ??= (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var command = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM ColdCall.CompanyXml WITH (UPDLOCK,HOLDLOCK) WHERE Nip=@nip)
            BEGIN
                UPDATE ColdCall.CompanyXml
                SET CompanyXml=@xml, MD5=@md5
                WHERE Nip=@nip AND MD5<>@md5;
            END
            ELSE
                INSERT ColdCall.CompanyXml (Nip,CompanyXml,MD5) VALUES (@nip,@xml,@md5);
            """, connection, transaction);
        AddText(command, "@nip", company.Nip, 32);
        AddText(command, "@xml", company.SourceXml);
        command.Parameters.Add("@md5", SqlDbType.VarChar, 128).Value = md5;
        await command.ExecuteNonQueryAsync(token);
    }
}
