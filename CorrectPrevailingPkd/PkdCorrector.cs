using System.Data;
using Microsoft.Data.SqlClient;

namespace CorrectPrevailingPkd;

internal sealed class PkdCorrector(string connectionString, bool apply) : IAsyncDisposable
{
    private readonly SqlConnection connection = new(connectionString);

    public async Task OpenAsync(CancellationToken token)
    {
        await connection.OpenAsync(token);
        using var command = new SqlCommand("""
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET NUMERIC_ROUNDABORT OFF;
            SET XACT_ABORT ON;
            IF COL_LENGTH('ColdCall.CompanyPkds', 'Prevailing') IS NULL
                THROW 50001, 'ColdCall.CompanyPkds.Prevailing is missing.', 1;
            CREATE TABLE #Assignments (
                Nip nvarchar(450) COLLATE DATABASE_DEFAULT NOT NULL,
                CodeWithoutDots nvarchar(450) COLLATE DATABASE_DEFAULT NOT NULL,
                PRIMARY KEY (Nip, CodeWithoutDots)
            );
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        Console.WriteLine($"Server: {connection.DataSource}; database: {connection.Database}; mode: {(apply ? "APPLY" : "DRY RUN")}");
    }

    public async Task<long> ProcessAsync(IEnumerable<Assignment> assignments, CancellationToken token)
    {
        using var data = new DataTable();
        data.Columns.Add("Nip", typeof(string));
        data.Columns.Add("CodeWithoutDots", typeof(string));
        foreach (var row in assignments.Distinct()) data.Rows.Add(row.Nip, row.CodeWithoutDots);
        using var transaction = connection.BeginTransaction();
        try
        {
            using var clear = new SqlCommand("TRUNCATE TABLE #Assignments;", connection, transaction);
            await clear.ExecuteNonQueryAsync(token);
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "#Assignments", BulkCopyTimeout = 120
            };
            bulk.ColumnMappings.Add("Nip", "Nip");
            bulk.ColumnMappings.Add("CodeWithoutDots", "CodeWithoutDots");
            await bulk.WriteToServerAsync(data, token);

            using var diagnostics = new SqlCommand("""
                SELECT
                    (SELECT COUNT(*) FROM #Assignments s WHERE NOT EXISTS
                        (SELECT 1 FROM ColdCall.Companies c WHERE c.Nip = s.Nip)),
                    (SELECT COUNT(*) FROM #Assignments s WHERE NOT EXISTS
                        (SELECT 1 FROM ColdCall.PkdCodes p WHERE p.CodeWithoutDots = s.CodeWithoutDots)),
                    (SELECT COUNT(*) FROM #Assignments s
                        WHERE EXISTS (SELECT 1 FROM ColdCall.Companies c WHERE c.Nip = s.Nip)
                        AND EXISTS (SELECT 1 FROM ColdCall.PkdCodes p WHERE p.CodeWithoutDots = s.CodeWithoutDots)
                        AND NOT EXISTS (SELECT 1 FROM ColdCall.CompanyPkds cp
                            JOIN ColdCall.Companies c ON c.Id = cp.CompanyId
                            JOIN ColdCall.PkdCodes p ON p.Id = cp.PkdCodeId
                            WHERE c.Nip = s.Nip AND p.CodeWithoutDots = s.CodeWithoutDots));
                """, connection, transaction) { CommandTimeout = 120 };
            using (var reader = await diagnostics.ExecuteReaderAsync(token))
            {
                await reader.ReadAsync(token);
                Console.WriteLine($"Batch: {data.Rows.Count} assignments; missing company: {reader.GetInt32(0)}, missing PKD: {reader.GetInt32(1)}, missing company-PKD link: {reader.GetInt32(2)}");
            }

            // EXISTS prevents duplicate XML assignments from multiplying the update.
            var sql = apply ? """
                UPDATE cp SET Prevailing = 1
                FROM ColdCall.CompanyPkds cp
                JOIN ColdCall.Companies c ON c.Id = cp.CompanyId
                JOIN ColdCall.PkdCodes p ON p.Id = cp.PkdCodeId
                WHERE cp.Prevailing = 0 AND EXISTS (
                    SELECT 1 FROM #Assignments s WHERE s.Nip = c.Nip AND s.CodeWithoutDots = p.CodeWithoutDots);
                SELECT CAST(@@ROWCOUNT AS bigint);
                """ : """
                SELECT COUNT_BIG(*) FROM ColdCall.CompanyPkds cp
                JOIN ColdCall.Companies c ON c.Id = cp.CompanyId
                JOIN ColdCall.PkdCodes p ON p.Id = cp.PkdCodeId
                WHERE cp.Prevailing = 0 AND EXISTS (
                    SELECT 1 FROM #Assignments s WHERE s.Nip = c.Nip AND s.CodeWithoutDots = p.CodeWithoutDots);
                """;
            using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
            var count = (long)(await command.ExecuteScalarAsync(token))!;
            await transaction.CommitAsync(token);
            Console.WriteLine($"{(apply ? "Updated" : "Would update")}: {count}");
            return count;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
