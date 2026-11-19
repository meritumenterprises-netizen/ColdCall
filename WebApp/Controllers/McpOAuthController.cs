using System.Globalization;
using System.Net;
using System.Security.Claims;
using ColdCall.Services;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ColdCall.Controllers;

[EnableRateLimiting("local-auth")]
[ApiController]
public sealed class McpOAuthController(McpOAuthSettings settings, IServiceProvider services, IAntiforgery antiforgery) : Controller
{
    private const string Scheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;
    private IOpenIddictApplicationManager Applications => services.GetRequiredService<IOpenIddictApplicationManager>();
    private IOpenIddictAuthorizationManager Authorizations => services.GetRequiredService<IOpenIddictAuthorizationManager>();
    private IMcpPaidAccountAccess Access => services.GetRequiredService<IMcpPaidAccountAccess>();
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");

    public sealed record Registration(
        [property: System.Text.Json.Serialization.JsonPropertyName("client_name")] string? ClientName,
        [property: System.Text.Json.Serialization.JsonPropertyName("redirect_uris")] string[]? RedirectUris,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_endpoint_auth_method")] string? AuthMethod,
        [property: System.Text.Json.Serialization.JsonPropertyName("grant_types")] string[]? GrantTypes,
        [property: System.Text.Json.Serialization.JsonPropertyName("response_types")] string[]? ResponseTypes);

    [HttpPost("/connect/register"), IgnoreAntiforgeryToken, RequestSizeLimit(16384)]
    [EnableRateLimiting("mcp-registration")]
    public async Task<IActionResult> Register([FromBody] Registration registration, CancellationToken token)
    {
        if (!settings.Enabled || !settings.EnableDynamicRegistration) return NotFound();
        if ((registration.AuthMethod is not null && registration.AuthMethod != "none") ||
            (registration.GrantTypes?.Any(type => type is not ("authorization_code" or "refresh_token")) ?? false) ||
            (registration.ResponseTypes?.Any(type => type != "code") ?? false))
            return BadRequest(new { error = "invalid_client_metadata" });
        if (await Applications.CountAsync(token) >= 1000)
            return StatusCode(429, new { error = "registration_limit_reached" });
        var client = new McpOAuthClient { ClientId = Guid.NewGuid().ToString("N"),
            DisplayName = registration.ClientName ?? "Klient MCP", RedirectUris = registration.RedirectUris ?? [] };
        OpenIddictApplicationDescriptor descriptor;
        try { descriptor = McpOAuth.DescribeClient(client, services.GetRequiredService<IHostEnvironment>().IsDevelopment(), settings.Resource); }
        catch (ArgumentException) { return BadRequest(new { error = "invalid_redirect_uri" }); }
        await Applications.CreateAsync(descriptor, token);
        Response.Headers.CacheControl = "no-store";
        return StatusCode(201, new { client_id = client.ClientId, client_name = client.DisplayName,
            redirect_uris = client.RedirectUris, token_endpoint_auth_method = "none",
            grant_types = new[] { "authorization_code", "refresh_token" }, response_types = new[] { "code" } });
    }

    [HttpGet("/connect/authorize")]
    public Task<IActionResult> AuthorizeGet(CancellationToken token) => AuthorizeCore(false, token);

    [HttpPost("/connect/authorize"), ValidateAntiForgeryToken]
    public Task<IActionResult> AuthorizePost(CancellationToken token) => AuthorizeCore(true, token);

    private async Task<IActionResult> AuthorizeCore(bool posted, CancellationToken token)
    {
        if (!settings.Enabled) return NotFound();
        var request = HttpContext.GetOpenIddictServerRequest()!;
        if (request.GetParameter("resource")?.ToString() != settings.Resource ||
            request.CodeChallengeMethod != CodeChallengeMethods.Sha256 || !request.HasScope(McpOAuthSettings.ReadScope))
            return ProtocolError(Errors.InvalidRequest, "The MCP resource, companies:read scope and S256 PKCE are required.");
        var session = await HttpContext.AuthenticateAsync(LocalAuthentication.Scheme);
        if (!session.Succeeded || !int.TryParse(session.Principal?.FindFirstValue(LocalAuthentication.UserIdClaim), out var userId))
        {
            if (request.HasPromptValue(PromptValues.None)) return ProtocolError(Errors.LoginRequired, "Sign in to FindMyClient.");
            return Redirect("/?oauthReturn=" + Uri.EscapeDataString(Request.PathBase + Request.Path + Request.QueryString));
        }
        // Never silently reuse an old login when a client explicitly requires fresh authentication.
        if (request.HasPromptValue(PromptValues.Login) || request.MaxAge.HasValue)
            return ProtocolError(Errors.LoginRequired, "Sign out and sign in again, then reconnect without prompt=login/max_age.");
        if (!await Access.AllowedAsync(userId, token))
            return ProtocolError(Errors.AccessDenied, "MCP requires an enabled account with an active non-trial plan.");
        if (request.HasPromptValue(PromptValues.None)) return ProtocolError(Errors.ConsentRequired, "Explicit consent is required.");
        var application = await Applications.FindByClientIdAsync(request.ClientId!, token)
            ?? throw new InvalidOperationException("Unknown OAuth client.");
        if (!posted)
        {
            var callbackHost = new Uri(request.RedirectUri!).Authority;
            var accountEmail = session.Principal!.FindFirstValue(ClaimTypes.Email);
            var accountLabel = string.IsNullOrWhiteSpace(accountEmail) ? $"ID {userId}" : accountEmail;
            return Page("Polacz konto z AI", $"<p>Konto FindMyClient: <strong>{H(accountLabel)}</strong></p>" +
                $"<p><strong>{H(await Applications.GetDisplayNameAsync(application, token))}</strong></p>" +
                $"<p>Adres odbiorcy: {H(callbackHost)}</p><p>Dostep: wyszukiwanie i odczyt firm w ramach aktywnego platnego planu. Bez eksportu CSV.</p>" +
                $"<form method=\"post\" action=\"{H(Request.PathBase + Request.Path)}\">{Csrf()}{ProtocolFields()}" +
                "<button name=\"decision\" value=\"allow\">Zezwol</button> <button name=\"decision\" value=\"deny\">Odmow</button></form>", new Uri(request.RedirectUri!));
        }
        if (Request.Form["decision"] != "allow") return ProtocolError(Errors.AccessDenied, "Consent denied.");
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);
        identity.SetClaim(Claims.Subject, userId.ToString(CultureInfo.InvariantCulture));
        identity.SetScopes(request.GetScopes());
        identity.SetResources(settings.Resource);
        var authorization = await Authorizations.CreateAsync(identity, identity.GetClaim(Claims.Subject)!,
            (await Applications.GetIdAsync(application, token))!, AuthorizationTypes.Permanent, identity.GetScopes(), token);
        identity.SetAuthorizationId(await Authorizations.GetIdAsync(authorization, token));
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return SignIn(new ClaimsPrincipal(identity), Scheme);
    }

    [HttpPost("/connect/token"), IgnoreAntiforgeryToken]
    public async Task<IActionResult> Token(CancellationToken token)
    {
        if (!settings.Enabled) return NotFound();
        var request = HttpContext.GetOpenIddictServerRequest()!;
        var result = await HttpContext.AuthenticateAsync(Scheme);
        if (!result.Succeeded || result.Principal is null ||
            !int.TryParse(result.Principal.GetClaim(Claims.Subject), out var userId) ||
            !await Access.AllowedAsync(userId, token))
            return ProtocolError(Errors.InvalidGrant, "The account no longer has an active paid plan.");
        var resource = request.GetParameter("resource")?.ToString();
        if ((resource is not null && resource != settings.Resource) || !result.Principal.HasResource(settings.Resource))
            return ProtocolError(Errors.InvalidTarget, "Invalid MCP resource.");
        return SignIn(result.Principal, Scheme);
    }

    [HttpGet("/connect/integrations")]
    public async Task<IActionResult> Integrations(CancellationToken token)
    {
        if (!settings.Enabled) return Page("Integracje AI", "<p>Integracje AI sa obecnie niedostepne.</p>");
        var session = await HttpContext.AuthenticateAsync(LocalAuthentication.Scheme);
        var subject = session.Principal?.FindFirstValue(LocalAuthentication.UserIdClaim);
        if (!session.Succeeded || subject is null) return Redirect("/?oauthReturn=%2Fconnect%2Fintegrations");
        var body = $"<p>MCP: {H(settings.Resource)}</p>";
        if (!int.TryParse(subject, out var id) || !await Access.AllowedAsync(id, token))
            body += "<p>Dostep wymaga aktywnego platnego planu.</p>";
        var count = 0;
        await foreach (var grant in Authorizations.FindBySubjectAsync(subject, token))
        {
            if (!await Authorizations.HasStatusAsync(grant, Statuses.Valid, token)) continue;
            var clientId = await Authorizations.GetApplicationIdAsync(grant, token);
            var app = clientId is null ? null : await Applications.FindByIdAsync(clientId, token);
            var name = app is null ? "Klient OAuth" : await Applications.GetDisplayNameAsync(app, token);
            body += $"<form method=\"post\" action=\"/connect/integrations/revoke\">{Csrf()}" +
                $"<input type=\"hidden\" name=\"id\" value=\"{H(await Authorizations.GetIdAsync(grant, token))}\">" +
                $"<span>{H(name)}</span> <button>Cofnij dostep</button></form>";
            count++;
        }
        if (count == 0) body += "<p>Brak aktywnych polaczen.</p>";
        return Page("Integracje AI", body);
    }

    [HttpPost("/connect/integrations/revoke"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke([FromForm] string id, CancellationToken token)
    {
        if (!settings.Enabled) return NotFound();
        var session = await HttpContext.AuthenticateAsync(LocalAuthentication.Scheme);
        var subject = session.Principal?.FindFirstValue(LocalAuthentication.UserIdClaim);
        if (!session.Succeeded || subject is null) return Unauthorized();
        var grant = await Authorizations.FindByIdAsync(id, token);
        if (grant is null || await Authorizations.GetSubjectAsync(grant, token) != subject) return NotFound();
        if (!await Authorizations.TryRevokeAsync(grant, token)) return Conflict();
        return Redirect("/connect/integrations");
    }

    private IActionResult ProtocolError(string error, string description) => Forbid(new AuthenticationProperties(
        new Dictionary<string, string?> {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }), Scheme);

    private string Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return $"<input type=\"hidden\" name=\"{H(tokens.FormFieldName)}\" value=\"{H(tokens.RequestToken)}\">";
    }

    private string ProtocolFields() => string.Concat(Request.Query
        .Where(pair => pair.Key is not ("__RequestVerificationToken" or "decision"))
        .SelectMany(pair => pair.Value.Select(value =>
            $"<input type=\"hidden\" name=\"{H(pair.Key)}\" value=\"{H(value)}\">")));

    private ContentResult Page(string title, string body, Uri? oauthCallback = null)
    {
        Response.Headers.CacheControl = "no-store";
        // Chromium applies form-action to the redirect following the consent POST too.
        // OpenIddict has already validated this exact callback against the client's registration.
        var formTargets = "'self'" + (oauthCallback is null ? "" : " " + oauthCallback.GetLeftPart(UriPartial.Authority));
        Response.Headers["Content-Security-Policy"] = $"default-src 'none'; style-src 'unsafe-inline'; form-action {formTargets}; frame-ancestors 'none'; base-uri 'none'";
        Response.Headers["X-Frame-Options"] = "DENY";
        return Content("<!doctype html><html lang=\"pl\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            $"<title>{H(title)} | FindMyClient.pl</title><style>body{{font:16px system-ui;margin:40px auto;padding:0 20px;max-width:720px;color:#24272b}}" +
            "button{font:inherit;padding:8px 16px;cursor:pointer}form{margin:20px 0}p{overflow-wrap:anywhere}</style>" +
            $"<h1>{H(title)}</h1>{body}<p><a href=\"/dashboard\">FindMyClient.pl</a></p></html>", "text/html; charset=utf-8");
    }
}
