# ColdCall MCP server

An authenticated, read-only Streamable HTTP MCP endpoint at `/mcp`. Tools:
`search_companies`, `count_companies`, and `get_company`.

The ColdCall dashboard's AI search panel converts a Polish question into validated
filters using the existing Ollama Cloud integration. ColdCall calls this MCP
server and opens the existing results grid with the returned filters. Searches
are not automatically saved and CSV exports remain explicit user actions.

## Configuration

Both projects use development User Secrets ID `ColdCall-LocalDevelopment`.
Configure these outside source control:

- `ConnectionStrings:Firmy`: the existing ColdCall SQL connection.
- `Mcp:SigningKey`: the same cryptographically random secret in both processes,
  at least 32 UTF-8 bytes. Generate a new secret for each environment.
- ColdCall also requires the existing `Ollama:ApiKey` and `Ollama:Model`.

For example, use Visual Studio's **Manage User Secrets** on ColdCall. Never
put the signing key in frontend configuration or commit it to Git. In production
configure secrets separately for both IIS/process identities using your existing
deployment secret mechanism; development User Secrets are not loaded automatically.

Start both projects (Visual Studio multiple startup projects, or two terminals):

```powershell
dotnet run --project ColdCall.McpServer
dotnet run --project WebApp/ColdCall.csproj
```

MCP binds to `http://127.0.0.1:5086` by default. ColdCall uses
`Mcp:Endpoint=http://127.0.0.1:5086/mcp`. Override the server address with
`Mcp:ListenUrl`. Keep it loopback-only for this deployment; use HTTPS if hosting
the server on another machine. `/health` is a basic liveness check, not a SQL test.

On `/dashboard`, users with an active non-trial plan (and administrators) can ask:
`Ile aktywnych biur rachunkowych jest w promieniu 20 km od Torunia?`
Only the question and classification instructions/catalog go to Ollama Cloud,
not account credentials or company search results. Review the interpreted filters
in the existing search controls. Unsupported or ambiguous criteria return an error.

## Install on Windows IIS

Create a ZIP using `Scripts/DeployRelease.ps1` (or `DeployDebug.ps1`). Copy the
ZIP and `Scripts/Install.ps1` to the VPS. Install IIS and the **.NET 10 ASP.NET Core
Hosting Bundle** first. Run **64-bit Windows PowerShell 5.1 as Administrator**:

```powershell
.\Install.ps1 -ZipPath C:\Deploy\ColdCall.McpServer-Release.zip
```

The installer reuses the SQL connection string and `Mcp:SigningKey` already stored
in the selected MCP application pool. It prompts securely only for missing or
blank values. Explicit `-ConnectionString` and `-SigningKey` SecureString parameters
override stored values. Do not put secrets in command history. It creates a dedicated
`ColdCall.McpServer` IIS site/pool, No Managed Code, Production, with only
`127.0.0.1:5086` bound. Do not open this port in the firewall.
Optional parameters: `-InstallDirectory`, `-SiteName`, `-AppPoolName`, `-Port`.
Each installation extracts a new release under `C:\Sites\ColdCall.McpServer\releases`;
previous releases are retained. Re-running updates this dedicated site only.

Secrets are stored as application-pool environment variables in IIS configuration,
not in the ZIP or repository. IIS configuration and its backups contain credentials:
restrict administrator access and protect backups. The installer makes an IIS
configuration backup before changing IIS; on failure it reports that backup name
and does not automatically restore the whole server configuration.

Configure the **main ColdCall application's** pool separately with
`Mcp__Endpoint=http://127.0.0.1:5086/mcp` and the **same** `Mcp__SigningKey`, then
recycle that pool. The installer does not change the main ColdCall application.
Alternatively, run `Scripts/ConfigureColdCall.ps1` in elevated 64-bit Windows
PowerShell 5.1 on the VPS:

```powershell
.\ConfigureColdCall.ps1 -SiteName ColdCall
```

It sets all four pool variables: `Mcp__Endpoint`, `Mcp__SigningKey`,
`Ollama__ApiKey`, and `Ollama__Model`. It reuses the signing key from the
`ColdCall.McpServer` pool if present; otherwise it prompts securely. It prompts
for the Ollama API key and exact Cloud model name. Existing values are updated
without adding duplicates. It backs up IIS configuration and recycles the
ColdCall pool by default (`-SkipRecycle` postpones that step). Use `-McpEndpoint`
or `-McpAppPoolName` for a non-default MCP installation. Keys are not displayed;
IIS configuration and backups still require protection as described above.

To also configure or repair the MCP SQL connection, run:

```powershell
.\ConfigureColdCall.ps1 -SiteName ColdCall -ConfigureMcpDatabase
```

This additionally prompts securely for `ConnectionStrings__Firmy`, validates its
syntax and writes it to the **MCP** pool (not the main ColdCall pool). Enter the
raw connection string with a single backslash in a named SQL instance, not a
JSON-escaped string. Both pools are recycled unless `-SkipRecycle` is supplied.
This validates configuration syntax only, not SQL connectivity or permissions.

Its `/health` check confirms startup, not SQL connectivity; verify an AI search
after deployment. Development User Secrets are not used in Production.

## Security and scope

The browser never receives MCP credentials. ColdCall issues five-minute,
audience-bound JWTs with `companies:read` scope. Tools derive the user ID from the
validated token and check enabled accounts, active plans and record limits again.
AI requests and MCP calls are rate-limited. RODO-blocked companies are excluded.
This server exposes no SQL execution or data modification tools.

This version is the internal dashboard integration, **not public OAuth onboarding
for ChatGPT, Claude or Copilot**. Before exposing it publicly, implement OAuth
discovery, authorization-code/PKCE, consent, client handling and token revocation.
Do not distribute the shared server signing key to customers.

## Verification

Run `dotnet run --project Tests/ColdCall.McpSmoke` for isolated parser, JWT,
authorization, MCP discovery and client/tool round-trip checks. This default suite
uses temporary loopback listeners and makes no SQL or Ollama calls.
The optional `-- --live-search` flag uses local User Secrets to interpret the
Toruń example through Ollama and execute a read-only search against the configured
database. It requires an enabled administrator account for the test query.
Add `--mcp` after `--live-search` to also exercise the running MCP service using
the shared local signing key.
`-- --live-geography --mcp` checks accounting searches around Toruń and Łódź
against SQL and the running MCP service without calling Ollama. It verifies that
city-wide municipality entries are selected instead of same-named villages or districts.
