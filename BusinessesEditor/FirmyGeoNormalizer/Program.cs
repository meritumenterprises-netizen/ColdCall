using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length > 1 || (args.Length == 1 && args[0] is not ("--analyze" or "--apply" or "--help")))
{
    Console.Error.WriteLine("Użycie: dotnet run -- [--analyze | --apply | --help]");
    return 2;
}
if (args.Contains("--help"))
{
    Console.WriteLine("--analyze (domyślnie): raport bez zmian w bazie. --apply: utworzenie słowników i kluczy obcych w transakcji. Oryginalne teksty pozostają.");
    return 0;
}
var apply = args.Contains("--apply");
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
var logFile = Path.Combine(logDirectory, $"geo-{DateTime.Now:yyyyMMdd-HHmmss}.log");
void Log(string text)
{
    var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
    Console.WriteLine(line);
    File.AppendAllText(logFile, line + Environment.NewLine);
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;
try
{
    using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), ct));
    var connectionString = config.RootElement.GetProperty("ConnectionString").GetString();
    var timeout = config.RootElement.GetProperty("CommandTimeoutSeconds").GetInt32();
    if (timeout <= 0) throw new ArgumentException("CommandTimeoutSeconds musi być dodatnie.");
    await using var db = new SqlConnection(connectionString);
    Log("Łączenie z SQL Server...");
    await db.OpenAsync(ct);
    Log($"Baza: {db.Database}; tryb: {(apply ? "MIGRACJA" : "ANALIZA (bez zmian)")}");
    SqlTransaction? tx = null;
    async Task Execute(string sql, bool report = false)
    {
        await using var cmd = new SqlCommand(sql, db, tx) { CommandTimeout = timeout };
        if (!report) { await cmd.ExecuteNonQueryAsync(ct); return; }
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        do
        {
            if (reader.FieldCount == 0) continue;
            Log(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
            while (await reader.ReadAsync(ct))
                Log(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i)))));
        } while (await reader.NextResultAsync(ct));
    }
    async Task Script(string name, bool report = false) => await Execute(
        await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", name), ct), report);
    try
    {
        await Script("00-Validate.sql");
        if (apply)
        {
            tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await Execute("""
SET XACT_ABORT ON;
DECLARE @result int;
EXEC @result=sys.sp_getapplock @Resource=N'FirmyGeoNormalizer-v1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;
IF @result<0 THROW 51000,N'Inna instancja migracji jest uruchomiona.',1;
-- Hold source stable throughout migration, including against concurrent importers.
DECLARE @rows bigint;
SELECT @rows=COUNT_BIG(*) FROM dbo.Firmy WITH (TABLOCKX,HOLDLOCK);
""");
        }
        Log("Odczyt i normalizacja wartości źródłowych...");
        await Script("01-Source.sql");
        await Script("02-Report.sql", true);
        if (apply)
        {
            Log("Tworzenie tabel i kolumn...");
            await Script("03-Schema.sql");
            Log("Uzupełnianie słowników...");
            await Script("04-Dictionaries.sql", true);
            Log("Aktualizacja referencji w dbo.Firmy...");
            await Script("05-References.sql", true);
            Log("Weryfikacja zgodności i tworzenie kluczy obcych...");
            await Script("06-Verify.sql");
            await Script("07-Constraints.sql");
            await Script("08-View.sql");
            await tx!.CommitAsync(ct);
            Log("ZATWIERDZONO. PowiatId, GminaId i MiejscowoscId są uzupełnione. Oryginalne teksty zachowano.");
        }
        else Log("Analiza ukończona. Aby wykonać migrację: dotnet run -- --apply");
    }
    catch
    {
        if (tx is not null)
        {
            try
            {
                if (tx.Connection is not null)
                {
                    Log("Wycofywanie transakcji; może potrwać. Limit czasu: " + timeout + " s.");
                    await using var rollbackCommand = new SqlCommand("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", db, tx)
                    { CommandTimeout = timeout };
                    await rollbackCommand.ExecuteNonQueryAsync(CancellationToken.None);
                    Log("Wycofano transakcję.");
                }
                else Log("Transakcja nie jest już aktywna na tym połączeniu.");
            }
            catch (Exception rollback) { Log($"Sprawdź stan transakcji na serwerze: {rollback.Message}"); }
        }
        throw;
    }
    finally { if (tx is not null) await tx.DisposeAsync(); }
    Log($"Log: {logFile}");
    return 0;
}
catch (OperationCanceledException) { Log("Przerwano operację."); return 130; }
catch (Exception ex) { Log($"BŁĄD: {ex.Message}"); return 1; }
