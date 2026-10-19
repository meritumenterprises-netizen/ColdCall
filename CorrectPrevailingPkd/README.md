# CorrectPrevailingPkd

By default, processes every top-level `.xml` file in the `xml` folder beside the
executable (`AppContext.BaseDirectory`), regardless of the working directory.
The build creates this folder. XML files placed in the project's `xml` folder
are also copied to build and publish output.

Reads CEIDG XML `Detail` elements (the SeedingJDG format), matches companies by
`NIP`, and matches `GlownyKodPkd` to `ColdCall.PkdCodes.CodeWithoutDots`.
Only existing matching `ColdCall.CompanyPkds` rows with `Prevailing = 0` are set
to `1`. Other flags are never reset. Missing companies, PKD codes and links are
reported, not inserted. If multiple companies have the same NIP, all matching
company-PKD links are updated.

## Run

From the repository root, preview changes (no permanent database writes):

```powershell
dotnet run --project CorrectPrevailingPkd
```

Apply the changes explicitly:

```powershell
dotnet run --project CorrectPrevailingPkd -- --apply
```

Optional `--xml` overrides the default and accepts a single file or a directory
(all top-level `*.xml` files).
NIP spaces/hyphens and PKD dots are removed; PKD letters are uppercased.
Malformed NIP/PKD values are skipped. XML DTDs and external entities are disabled.

Default database: local SQL Server, `Firmy_temp`, Windows authentication, encrypted
connection with the local server certificate trusted. For another server, set
`CORRECT_PREVAILING_PKD_CONNECTION_STRING` (recommended for credentials), or pass
`--connection`. Use a verified server certificate in production.

Each batch is transactional; `--batch-size` defaults to 1000 (maximum 10000).
Per-file progress is reported as a percentage based on bytes read (approximate
because the XML reader buffers ahead). It starts at 0%, reports percentage changes,
and reaches 100% only after the final database batch finishes successfully.
Ctrl+C rolls back the active batch, but keeps earlier committed batches. Reruns
are idempotent. Already prevailing rows and unrelated rows are untouched.
Dry-run totals can count the same proposed update again in another batch/file.
Conflicting main PKD codes across input files can mark multiple links as prevailing;
the tool deliberately does not clear previously set flags.
