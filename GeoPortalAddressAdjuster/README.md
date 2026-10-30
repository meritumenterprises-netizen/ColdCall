# GeoPortalAddressAdjuster

Assigns GeoPortal addresses to companies without a `ColdCall.CompanyAddress` link,
whose WojewodztwoId, PowiatId, GminaId and MiejscowoscId are all non-null.
Uses the existing GeoportalAddressService / GeoportalClient matching rules, including
the rejection of missing administrative names and WojewodztwoId=1.

Run `dotnet run --project GeoPortalAddressAdjuster` from the repository root.
This command writes to the database. Default connection: localhost / Firmy_temp,
Windows authentication. Override using `GEO_PORTAL_SQL_CONNECTION`.

Uses bounded queues with 32 continuous lookup workers and a separate SQL writer.
Saves up to 100 results with one SQL command and transaction, rechecking the company has not changed or
acquired an address during lookup. Existing addresses are reused by GeoPortal source
key. Unresolved companies are logged and skipped. Ctrl+C cancels; restarting skips
companies already linked and retries companies still without addresses.

Options: `--workers 32 --delay-ms 0 --cache-days 1`. There is no default delay
between requests. Retries and server-requested backoff remain enabled.
Use `--delay-ms 100` to restore throttling if needed.
Cache expires after one day by default; identical in-flight queries share one request.
Set `--cache-days 0` to disable reuse of cached responses.
Progress is printed every 100 completions, including throughput, HTTP/cache counters,
average lookup duration and cumulative SQL write time. Candidate diagnostic console
blocks are disabled in this app; query errors are still printed and logged.
Existing importer callers retain their original cache, throttle and diagnostics defaults.
