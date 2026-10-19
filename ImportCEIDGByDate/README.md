# ImportCEIDGByDate

Imports CEIDG change bulletin entry IDs into existing ColdCall.CEIDGImport in Firmy_temp.
Requires .NET 10 and SQL Server access. After the bulletin stage, fetches company details
for all rows with NULL/empty WpisJSON, across all import dates, and stores the complete JSON response.
Details calls use the same bearer token and a 20 ms delay before each company fetch per operation.
Parallel.ForEachAsync runs at most four concurrent operations with separate pooled SQL connections
and live percentage progress. HTTP 429/5xx responses use retry/backoff (Retry-After when present).
Failed detail requests remain empty for the next run; authentication errors stop the import.

```powershell
dotnet run --project C:\projects\ColdCall\ImportCEIDGByDate -- 2026-10-18
```

Date format: YYYY-MM-DD, minimum 2026-09-20.
Token: CEIDG_API_TOKEN environment variable or user secret Ceidg:Token.
Connection: CEIDG_CONNECTION_STRING or user secret ConnectionStrings:Import;
default localhost / Firmy_temp / Windows Authentication.
User secrets ID: ColdCall-ImportCEIDGByDate.

Follows links.next until links.last (or absent next at the end). Each page is committed
in a transaction. If any record already exists for ImportDate, the entire date is skipped
without calling the bulletin API. This also applies to partially imported dates after interruption.
The company details stage still runs when the bulletin stage is skipped.
Ctrl+C cancels safely; committed pages are retained.
