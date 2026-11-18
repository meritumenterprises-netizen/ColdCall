# Public MCP OAuth

ColdCall hosts the authorization server. MCP is a separate resource server.
Existing ColdCall -> MCP HMAC tokens remain independent of OAuth and are never
given to external clients. Public tools are read-only; CSV export is not enabled.

## Local configuration

From the repository root, with the existing development SQL connection and
internal MCP signing key already configured:

```powershell
.\ColdCall.McpServer\Scripts\ConfigureLocalOAuth.ps1
```

The default authorization origin is IIS Express `https://localhost:44345/`.
For the Kestrel ColdCall profile, pass `-Issuer https://localhost:5001/`.
Use `-Port 5096` for another local MCP port. Restart both applications after
configuration. The script only stores non-secret OAuth settings in development
User Secrets; it does not change SQL credentials or the existing signing key.
The configured SQL login needs permission to create the new OAuth schema.

Start MCP with the selected port (example: 5086):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project ColdCall.McpServer --no-launch-profile -- --urls http://127.0.0.1:5086
```

Start ColdCall using the matching IIS Express/Kestrel profile. Use a trusted
development HTTPS certificate. OAuth login returns to the same ColdCall origin;
existing Google and email/password login mechanisms are reused.

OAuth is off by default outside this explicit local configuration. Initialization
is a maintenance command (`--initialize-mcp-oauth`), never automatic at startup.
It creates four OpenIddict tables in a separate `McpOAuth` schema and optionally
upserts configured clients. It does not recreate or seed ColdCall business data.

## Endpoints

- ColdCall `/.well-known/oauth-authorization-server`: authorization discovery.
- ColdCall `/connect/authorize`: login, paid-plan check and CSRF-protected consent.
- ColdCall `/connect/token`: authorization code exchange and rotating refresh tokens.
- ColdCall `/connect/revoke`: standard token revocation endpoint.
- ColdCall `/connect/register`: optional public-client dynamic registration.
- ColdCall `/connect/integrations`: current account's grants and revocation.
- MCP `/.well-known/oauth-protected-resource/mcp`: resource discovery.
- MCP `/mcp`: existing tools, now also accepting public OAuth access tokens.

Authorization requires `response_type=code`, `scope=companies:read offline_access`,
`resource` equal to the configured MCP endpoint, and PKCE `S256`. Registered
redirect URLs are exact; no wildcard or remote metadata fetching is supported.
Both the resource and the issuer are validated. Access tokens last five minutes;
refresh tokens last three days and rotate. Revoked grants/tokens are checked in
SQL on each resource request. Plan access is checked again at token exchange,
refresh, each MCP request and tool execution. An administrator without an active
non-trial plan cannot use public OAuth. Existing internal admin access is unchanged.

Only enabled accounts with an unexpired non-trial plan qualify, using the same
plan-expiry convention as ColdCall. Read-only tools retain the plan's record cap.
HTTP MCP requests are limited to 30/minute per account, not per client. This
does not introduce a new paid search-credit billing model or consume CSV quota.

Dynamic registration must be explicitly enabled with
`Mcp:OAuth:EnableDynamicRegistration=true`. It accepts only public authorization
code/refresh-token clients, at most ten HTTPS callbacks (HTTP loopback in
Development only), five registrations/minute/IP and 1,000 stored applications.
Client names are untrusted and escaped on the consent page; users must inspect
the callback host. Registration alone never grants company access. When disabled,
configure `Mcp:OAuth:Clients` entries with `ClientId`, `DisplayName`, and
`RedirectUris`, then rerun initialization. Obsolete dynamic registrations need
administrative cleanup; no arbitrary callback URL is fetched by the server.

## Production deployment (not performed by local setup)

Deploy BOTH updated applications. Configure the following identically in their
separate IIS pools, in addition to existing SQL/internal-MCP settings:

```text
Mcp__OAuth__Enabled=true
Mcp__OAuth__Issuer=https://findmyclient.pl/
Mcp__OAuth__Resource=https://mcp.findmyclient.pl/mcp
```

Use the actual canonical ColdCall HTTPS origin as Issuer (including `www` if
that is the site's canonical login origin). Do not redirect OAuth POST endpoints
between hostnames. Enable dynamic registration in ColdCall only if desired.
ColdCall also requires `Mcp__OAuth__CertificateThumbprint`: a persistent RSA
signing/encryption certificate in `LocalMachine\My`, with private-key read access
for the ColdCall pool identity. Prefer a separate OAuth certificate rather than
the short-lived TLS certificate. The MCP pool needs public signing keys through
HTTPS discovery, not access to that private key. Never use development signing
certificates on the VPS. Keep previous signing/decryption keys available through
token lifetimes when rotating keys; coordinate renewal rather than replacing
the only decryption key while refresh tokens still exist.

Run ColdCall's `--initialize-mcp-oauth` maintenance command with the SAME production
configuration before enabling access. Both pools need SQL access to the OAuth
schema (MCP reads tokens/grants; ColdCall creates and updates them). Keep SQL and
the loopback MCP port private. `/health` alone does not test OAuth or SQL.

Localhost cannot be reached by cloud-hosted assistants. Real ChatGPT/Claude/Copilot
connector acceptance tests require the public HTTPS deployment and remain separate
from local protocol tests; no compatibility with every Copilot product is implied.

## Verification

```powershell
dotnet run --project Tests/ColdCall.OAuthSmoke
dotnet run --project Tests/ColdCall.McpSmoke
```

The OAuth suite uses only in-memory business data and a temporary in-memory
SQLite OpenIddict database. It exercises discovery, paid/trial/expired/disabled
access, registration, redirect validation, login, CSRF consent, PKCE, scope,
audience, issuer, expiry, refresh rotation and immediate grant revocation.
