using Microsoft.Data.SqlClient;

namespace GeoPortalAddressImporter;

internal static class BatchWriteTests
{
    public static async Task RunAsync(string connectionString, Address address, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var command = new SqlCommand("""
            INSERT ColdCall.Companies (WojewodztwoId,MiejscowoscId,PowiatId,GminaId,
                Street,BuildingNumber,PostalCode,AddressIsMissing,BlockedByRODO)
            OUTPUT INSERTED.Id
            SELECT 2,1,1,1,N'ul. Psie Pole',N'99',N'58-301',1,0
            FROM (VALUES (1),(2),(3),(4),(5)) N(Id);
            """, connection);
        var ids = new List<int>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) ids.Add(reader.GetInt32(0));
        var repository = new CompanyRepository(connectionString);
        var companies = new List<Company>();
        foreach (var id in ids) companies.Add((await repository.ReadAsync(0, 1, id, token)).Single());
        var point = address with { Number = "99" };
        var batch = companies.Take(2).Select(c => new AddressLookupResult(c, new MatchResult(point, "test"), null)).ToArray();
        var messages = await repository.SaveBatchAsync(batch, token);
        Check(messages.Count == 2 && messages.Values.All(x => x.StartsWith("Zapisano", StringComparison.Ordinal)), "Partia zapisana");
        command.CommandText = $"SELECT COUNT(DISTINCT AddressId) FROM ColdCall.CompanyAddress WHERE CompanyId IN ({ids[0]},{ids[1]});";
        Check((int)(await command.ExecuteScalarAsync(token))! == 1, "Wspolny adres w tej samej partii");
        messages = await repository.SaveBatchAsync(batch, token);
        Check(messages.Values.All(x => !x.StartsWith("Zapisano", StringComparison.Ordinal)), "Idempotencja partii");

        command.CommandText = $"UPDATE ColdCall.Companies SET PostalCode=N'00-000' WHERE Id={ids[2]};";
        await command.ExecuteNonQueryAsync(token);
        batch = companies.Skip(2).Take(1).Select(c => new AddressLookupResult(c, new MatchResult(point, "test"), null)).ToArray();
        messages = await repository.SaveBatchAsync(batch, token);
        Check(!messages[ids[2]].StartsWith("Zapisano", StringComparison.Ordinal), "Nieaktualne dane nie sa zapisywane");

        batch = new[]
        {
            new AddressLookupResult(companies[3], new MatchResult(point with { Number="100" }, "test"), null),
            new AddressLookupResult(companies[4], new MatchResult(point with { X=point.X+10 }, "test"), null)
        };
        var failed = false;
        try { await repository.SaveBatchAsync(batch, token); }
        catch (SqlException ex) when (ex.Number == 51001) { failed = true; }
        Check(failed, "Konflikt adresu pozostaje widoczny po fallbacku");
        command.CommandText = $"SELECT COUNT(*) FROM ColdCall.CompanyAddress WHERE CompanyId={ids[3]};";
        Check((int)(await command.ExecuteScalarAsync(token))! == 1, "Fallback zapisuje poprawna firme dokladnie raz");
        command.CommandText = $"SELECT COUNT(*) FROM ColdCall.CompanyAddress WHERE CompanyId={ids[4]};";
        Check((int)(await command.ExecuteScalarAsync(token))! == 0, "Konflikt nie zostawia czesciowego powiazania");
        Console.WriteLine("PASS: SQL batch, wspolny adres, idempotencja, nieaktualne dane i fallback po rollbacku.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
