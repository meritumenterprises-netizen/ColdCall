using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ColdCall.Controllers;
using ColdCall.Data;
using ColdCall.McpServer;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name); checks++;
}

// Business eligibility is tested separately from the OAuth protocol fixtures.
await using (var db = new TestColdCallDbContext(new DbContextOptionsBuilder<ColdCallDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options))
{
    var paid = new PlanDefinition("PAID", 10, 100) { Id = 1, MaxNumberOfExportedRecords = 100 };
    var trial = new PlanDefinition("TRIAL", 10, 0) { Id = 2, IsTrial = true, MaxNumberOfExportedRecords = 10000 };
    db.PlanDefinitions.AddRange(paid, trial);
    for (int i = 1; i <= 5; i++) db.Users.Add(new AppUser { Id = i, IsAdmin = i == 5, IsEnabled = i != 4 });
    for (int i = 1; i <= 4; i++) db.UserPlans.Add(new UserPlan { AppUserId = i,
        PlanDefinition = i == 2 ? trial : paid, ExpiresAtUtc = DateTime.UtcNow.AddDays(i == 3 ? -1 : 1) });
    db.UserPlans.Add(new UserPlan { AppUserId = 1, PlanDefinition = trial, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
    await db.SaveChangesAsync();
    var access = new McpPaidAccountAccess(db);
    Check(await access.AllowedAsync(1, default), "Enabled paid account accepted");
    foreach (var id in new[] { 2, 3, 4, 5, 99 })
        Check(!await access.AllowedAsync(id, default), "Trial/expired/disabled/admin-without-plan/missing rejected: " + id);
    Check(await PlanRecordLimits.ForUserAsync(db, 1, false, true) == 100, "Public MCP uses only paid plan record caps");
    Check(await PlanRecordLimits.ForUserAsync(db, 1, false) == 10000, "Existing internal plan-limit behavior unchanged");
    db.CompanyStatuses.Add(new CompanyStatus { StatusId = 1, Nazwa = "Aktywny" });
    db.Companies.Add(new Company
    {
        Id = 1, Name = "Addressed company", Nip = "8883106651", StatusDzialalnosciId = 1,
        BusinessType = new BusinessType { TypDzialalnosciId = 1, TypDzialalnosci = "JDG" },
        Wojewodztwo = new Wojewodztwo { Id = 1, Name = "Kujawsko-Pomorskie" },
        Powiat = new Powiat { Id = 1, Name = "Torun" }, Gmina = new Gmina { Id = 1, Name = "Torun" },
        Miejscowosc = new Miejscowosc { Id = 1, Name = "Torun" }, Street = "Testowa",
        BuildingNumber = "12A", ApartmentNumber = "7"
    });
    db.Companies.Add(new Company { Id = 2, Name = "No address", Nip = "1234567890", StatusDzialalnosciId = 1, BusinessTypeId = 1 });
    db.CompaniesShort.AddRange(new CompanyShortView { Id = 1, FullAddress = "Torun Testowa 12A/7" },
        new CompanyShortView { Id = 2, FullAddress = "" });
    await db.SaveChangesAsync();
    var companySearch = new AiCompanySearchService(db);
    var companyItem = await companySearch.GetCompanyAsync(1, "8883106651", default);
    Check(companyItem?.StatusDzialalnosci == "Aktywny", "get_company populates business status");
    Check(companyItem?.FullAddress == "Torun Testowa 12A/7" && companyItem.Wojewodztwo == "Kujawsko-Pomorskie"
        && companyItem.Gmina == "Torun" && companyItem.Powiat == "Torun" && companyItem.Miejscowosc == "Torun"
        && companyItem.Ulica == "Testowa" && companyItem.BuildingNumber == "12A" && companyItem.ApartmentNumber == "7",
        "Company item preserves fullAddress and adds individual address fields");
    var searchResult = await companySearch.SearchAsync(1, new AiCompanySearchRequest(), default, true);
    Check(searchResult.Total == 2 && searchResult.Items.All(item => item.StatusDzialalnosci == "Aktywny"),
        "search_companies populates status for all items without changing count");
    var missingAddress = searchResult.Items.Single(item => item.Id == 2);
    Check(missingAddress.Wojewodztwo is null && missingAddress.Gmina is null && missingAddress.Powiat is null
        && missingAddress.Miejscowosc is null && missingAddress.Ulica is null && missingAddress.BuildingNumber is null
        && missingAddress.ApartmentNumber is null, "Missing address components remain null");
    var searchJson = System.Text.Json.JsonSerializer.Serialize(searchResult, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    var resultJson = JsonNode.Parse(searchJson)!["items"]![0]!;
    Check(resultJson["fullAddress"]!.GetValue<string>() == "Torun Testowa 12A/7"
        && resultJson["Wojew\u00f3dztwo"]!.GetValue<string>() == "Kujawsko-Pomorskie"
        && resultJson["Miejscowo\u015b\u0107"]!.GetValue<string>() == "Torun"
        && resultJson["Gmina"]!.GetValue<string>() == "Torun" && resultJson["Powiat"]!.GetValue<string>() == "Torun"
        && resultJson["Ulica"]!.GetValue<string>() == "Testowa"
        && resultJson["Nr Budynku"]!.GetValue<string>() == "12A" && resultJson["Nr apartamentu"]!.GetValue<string>() == "7"
        && resultJson["statusDzialalnosci"]!.GetValue<string>() == "Aktywny", "MCP JSON includes exact requested Polish address keys and status");
    Check(System.Text.Json.JsonSerializer.Deserialize<AiCompanySearchResult>(searchJson,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.Items[0].ApartmentNumber == "7",
        "ColdCall MCP client can deserialize the enriched result");
    for (var id = 3; id <= 105; id++)
    {
        db.Companies.Add(new Company { Id = id, Name = "Company " + id, StatusDzialalnosciId = 1, BusinessTypeId = 1 });
        db.CompaniesShort.Add(new CompanyShortView { Id = id, FullAddress = "" });
    }
    await db.SaveChangesAsync();
    var allResults = await companySearch.SearchAsync(1, new AiCompanySearchRequest { Page = 20 }, default, true, returnAllAccessible: true);
    Check(allResults.Total == 105 && allResults.RecordLimit == 100 && allResults.Items.Count == 100,
        "Unpaged MCP returns all accessible results, ignores page and respects paid record limit");
    var pagedResults = await companySearch.SearchAsync(1, new AiCompanySearchRequest(), default, true);
    Check(pagedResults.Items.Count == 10, "Existing paged search behavior remains unchanged");
}

using (var limiter = new McpOAuthRequestLimiter())
{
    var oversized = new DefaultHttpContext();
    oversized.Request.Path = "/connect/token"; oversized.Request.ContentLength = 20000;
    await limiter.InvokeAsync(oversized, _ => throw new Exception("Oversized request reached OAuth"));
    Check(oversized.Response.StatusCode == 413, "Oversized OAuth body rejected before authentication");
    for (int i = 0; i < 61; i++)
    {
        var context = new DefaultHttpContext(); context.Request.Path = "/connect/token";
        await limiter.InvokeAsync(context, _ => Task.CompletedTask);
        if (i == 60) Check(context.Response.StatusCode == 429, "OAuth rate limit enforced before token validation");
    }
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
builder.WebHost.UseTestServer();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Mcp:OAuth:Enabled"] = "true", ["Mcp:OAuth:EnableDynamicRegistration"] = "true",
    ["Mcp:OAuth:Issuer"] = "https://localhost:44345/", ["Mcp:OAuth:Resource"] = "https://mcp.example.test/mcp",
    ["Mcp:SigningKey"] = "test-only-internal-key-not-a-production-secret-123456789",
    ["ConnectionStrings:Firmy"] = "Server=unused;Database=unused;Integrated Security=true"
});
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(McpOAuthController).Assembly);
builder.Services.AddAntiforgery();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("local-auth", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
    options.AddPolicy("mcp-registration", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
});
builder.Services.AddAuthentication().AddCookie(LocalAuthentication.Scheme).AddJwtBearer("Bearer", options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = McpSessionToken.Issuer,
        ValidateAudience = true, ValidAudience = McpSessionToken.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = McpSessionToken.Key(builder.Configuration),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(10)
    };
});
McpOAuth.AddServer(builder.Services, builder.Configuration, builder.Environment);
var settings = builder.AddPublicOAuth();
// Import the same public signing keys without HTTP discovery in this in-process transport.
builder.Services.AddOpenIddict().AddValidation(options => options.UseLocalServer());
builder.Services.RemoveAll<McpOAuthDbContext>();
builder.Services.AddScoped(_ => new McpOAuthDbContext(new DbContextOptionsBuilder<McpOAuthDbContext>()
    .UseSqlite(connection).UseOpenIddict().Options));
var eligibility = new TestPaidAccess();
builder.Services.RemoveAll<IMcpPaidAccountAccess>();
builder.Services.AddSingleton<IMcpPaidAccountAccess>(eligibility);
builder.Services.AddAuthorization(options => options.AddPolicy("McpRead", policy => policy.RequireAuthenticatedUser()
    .RequireAssertion(context => context.User.FindAll("scope").Any(c => c.Value.Split(' ').Contains("companies:read")))));
await using var app = builder.Build();
app.MapPublicOAuth(settings);
app.UseAuthentication();
app.UsePaidOAuthAccounts(settings);
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/mcp", () => Results.Ok()).RequireAuthorization("McpRead");
app.MapGet("/fixture/login/{id:int}", async (int id, HttpContext context) =>
{
    await context.SignInAsync(LocalAuthentication.Scheme, new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(LocalAuthentication.UserIdClaim, id.ToString()),
         new Claim(ClaimTypes.Email, $"user{id}+<test>@example.test")], LocalAuthentication.Scheme)));
    return Results.Ok();
});
await app.StartAsync();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<McpOAuthDbContext>().Database.EnsureCreatedAsync();
var http = app.GetTestClient();
http.BaseAddress = new Uri("https://localhost:44345");
var discovery = JsonNode.Parse(await http.GetStringAsync("/.well-known/oauth-authorization-server"))!;
Check(discovery["registration_endpoint"]!.GetValue<string>().EndsWith("/connect/register"), "OAuth discovery advertises registration");
Check(discovery["token_endpoint_auth_methods_supported"]!.AsArray().Single()!.GetValue<string>() == "none",
    "Discovery advertises the public PKCE client authentication method");
Check(!discovery["scopes_supported"]!.AsArray().Any(scope => scope!.GetValue<string>() == "openid"),
    "OAuth discovery does not advertise unsupported OpenID Connect scope");
Check(discovery["code_challenge_methods_supported"]!.AsArray().Any(n => n!.GetValue<string>() == "S256"), "Discovery advertises S256 PKCE");
var resource = JsonNode.Parse(await http.GetStringAsync("/.well-known/oauth-protected-resource/mcp"))!;
Check(resource["resource"]!.GetValue<string>() == settings.Resource, "Protected resource discovery binds exact audience");
var anonymous = await http.GetAsync("/mcp");
Check(anonymous.StatusCode == HttpStatusCode.Unauthorized && anonymous.Headers.WwwAuthenticate.ToString().Contains("resource_metadata"), "Anonymous MCP returns OAuth discovery challenge");

async Task<HttpResponseMessage> Register(string redirect) => await http.PostAsJsonAsync("/connect/register", new
{
    client_name = "OAuth test <client>", redirect_uris = new[] { redirect }, token_endpoint_auth_method = "none"
});
Check((await Register("https://client.test/callback#fragment")).StatusCode == HttpStatusCode.BadRequest, "Fragment redirect rejected");
Check((await Register("http://external.test/callback")).StatusCode == HttpStatusCode.BadRequest, "Insecure redirect rejected");
var registration = await Register("https://client.test/callback");
Check(registration.StatusCode == HttpStatusCode.Created, "Public OAuth client registered");
var clientId = JsonNode.Parse(await registration.Content.ReadAsStringAsync())!["client_id"]!.GetValue<string>();
var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
string AuthorizeUrl(string? codeChallenge = null, string? target = null, string? redirect = null) => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
{
    ["client_id"] = clientId, ["redirect_uri"] = redirect ?? "https://client.test/callback", ["response_type"] = "code",
    ["scope"] = "companies:read offline_access", ["state"] = "state-test", ["resource"] = target ?? settings.Resource,
    ["code_challenge"] = codeChallenge ?? challenge, ["code_challenge_method"] = "S256"
});
var loginRedirect = await http.GetAsync(AuthorizeUrl());
if (loginRedirect.StatusCode != HttpStatusCode.Redirect)
    Console.WriteLine("Authorization response: " + loginRedirect.StatusCode + " " + await loginRedirect.Content.ReadAsStringAsync());
Check(loginRedirect.StatusCode == HttpStatusCode.Redirect && loginRedirect.Headers.Location!.OriginalString.StartsWith("/?oauthReturn="), "Unauthenticated OAuth returns to ColdCall login");

string cookies = "";
void CaptureCookies(HttpResponseMessage response)
{
    if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
    foreach (var value in values)
    {
        var pair = value.Split(';')[0];
        var name = pair.Split('=')[0];
        cookies = string.Join("; ", cookies.Split("; ", StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith(name + "=")).Append(pair));
    }
    http.DefaultRequestHeaders.Remove("Cookie");
    http.DefaultRequestHeaders.Add("Cookie", cookies);
}
CaptureCookies(await http.GetAsync("/fixture/login/2"));
var trialResponse = await http.GetAsync(AuthorizeUrl());
Check(trialResponse.Headers.Location?.OriginalString.Contains("access_denied") == true, "Trial cannot grant OAuth consent");
CaptureCookies(await http.GetAsync("/fixture/login/1"));
var missingPkce = await http.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
{
    ["client_id"] = clientId, ["redirect_uri"] = "https://client.test/callback", ["response_type"] = "code", ["scope"] = "companies:read", ["resource"] = settings.Resource
}));
Check(missingPkce.StatusCode != HttpStatusCode.OK, "Missing PKCE rejected");
var wrongRedirect = await http.GetAsync(AuthorizeUrl(redirect: "https://evil.test/callback"));
Check(wrongRedirect.StatusCode == HttpStatusCode.BadRequest && wrongRedirect.Headers.Location is null, "Unregistered redirect rejected without redirecting");
var wrongResource = await http.GetAsync(AuthorizeUrl(target: "https://other.test/mcp"));
Check(wrongResource.StatusCode == HttpStatusCode.BadRequest || wrongResource.Headers.Location?.OriginalString.Contains("invalid_") == true, "Wrong resource rejected");

async Task<string> GetCode(bool approve = true)
{
    var url = AuthorizeUrl();
    var consent = await http.GetAsync(url);
    CaptureCookies(consent);
    var html = await consent.Content.ReadAsStringAsync();
    var csp = consent.Headers.GetValues("Content-Security-Policy").Single();
    Check(csp.Contains("form-action 'self' https://client.test;") && !csp.Contains("*"), "Consent CSP permits only self and the registered callback origin");
    var fixtureDirectory = Environment.GetEnvironmentVariable("COLD_CALL_OAUTH_BROWSER_FIXTURES");
    if (!string.IsNullOrWhiteSpace(fixtureDirectory))
    {
        Directory.CreateDirectory(fixtureDirectory);
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "consent.json"),
            System.Text.Json.JsonSerializer.Serialize(new { html, csp }));
    }
    Check(consent.StatusCode == HttpStatusCode.OK && html.Contains("&lt;client&gt;"), "Consent page escapes untrusted client name");
    Check(html.Contains("Konto FindMyClient: <strong>user1+&lt;test&gt;@example.test</strong>")
        && !html.Contains("user2+"), "Consent displays the current session email with HTML escaping");
    var csrf = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
    Check(csrf.Length > 0, "Consent form includes CSRF token");
    var badForm = QueryHelpers.ParseQuery(new Uri("https://localhost" + url).Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
    badForm["decision"] = "allow";
    var badCsrf = await http.PostAsync("/connect/authorize", new FormUrlEncodedContent(badForm));
    Check(badCsrf.StatusCode == HttpStatusCode.BadRequest, "Consent without CSRF rejected");
    var form = QueryHelpers.ParseQuery(new Uri("https://localhost" + url).Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
    form["decision"] = approve ? "allow" : "deny";
    form["__RequestVerificationToken"] = csrf;
    var accepted = await http.PostAsync("/connect/authorize", new FormUrlEncodedContent(form));
    if (accepted.Headers.Location is null) Console.WriteLine("Consent response: " + accepted.StatusCode + " " + (await accepted.Content.ReadAsStringAsync())[..Math.Min(500, (int)(accepted.Content.Headers.ContentLength ?? 0))]);
    var query = QueryHelpers.ParseQuery(accepted.Headers.Location!.Query);
    Check(query["state"] == "state-test", "OAuth preserves client state");
    if (!approve) { Check(query["error"] == "access_denied", "Consent denial does not issue a code"); return ""; }
    Check(query["code"].ToString().Length > 0, "Approved consent issues authorization code");
    return query["code"]!;
}
await GetCode(false);
var code = await GetCode();
async Task<HttpResponseMessage> Exchange(string value, bool refresh = false, string? proof = null, string? target = null) =>
    await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = refresh ? "refresh_token" : "authorization_code", ["client_id"] = clientId,
        [refresh ? "refresh_token" : "code"] = value, ["redirect_uri"] = "https://client.test/callback",
        ["code_verifier"] = proof ?? verifier, ["resource"] = target ?? settings.Resource
    }));
Check((await Exchange(code, proof: new string('x', 43))).StatusCode == HttpStatusCode.BadRequest, "Incorrect PKCE verifier rejected");
var exchanged = await Exchange(code);
Check(exchanged.StatusCode == HttpStatusCode.OK, "Authorization code exchanged successfully");
var tokens = JsonNode.Parse(await exchanged.Content.ReadAsStringAsync())!;
var accessToken = tokens["access_token"]!.GetValue<string>();
var refreshToken = tokens["refresh_token"]!.GetValue<string>();
Check(accessToken.Length > 0 && refreshToken.Length > 0, "Access and refresh tokens issued");
async Task<HttpResponseMessage> CallMcp(string token)
{
    using var message = new HttpRequestMessage(HttpMethod.Get, "/mcp");
    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return await http.SendAsync(message);
}
Check((await CallMcp(accessToken)).StatusCode == HttpStatusCode.OK, "Resource validates public OAuth access token");
var signer = app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue.SigningCredentials
    .First(credential => credential.Key is AsymmetricSecurityKey);
string ModifiedToken(string claim, object value)
{
    var original = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
    var payload = new JwtPayload();
    foreach (var pair in original.Payload) payload[pair.Key] = pair.Value;
    payload[claim] = value;
    var header = new JwtHeader(signer) { ["typ"] = "at+jwt" };
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
}
Check((await CallMcp(ModifiedToken("aud", "https://another.test/mcp"))).StatusCode == HttpStatusCode.Unauthorized, "Wrong audience rejected even with a valid signature");
Check((await CallMcp(ModifiedToken("iss", "https://another.test/"))).StatusCode == HttpStatusCode.Unauthorized, "Wrong issuer rejected even with a valid signature");
Check((await CallMcp(ModifiedToken("exp", DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()))).StatusCode == HttpStatusCode.Unauthorized, "Expired OAuth token rejected");
Check((await CallMcp(ModifiedToken("scope", "companies:write"))).StatusCode == HttpStatusCode.Forbidden, "Missing read scope rejected");
Check((await CallMcp(McpSessionToken.Create(1, builder.Configuration))).StatusCode == HttpStatusCode.OK, "Existing internal MCP token remains supported");
eligibility.Enabled = false;
Check((await CallMcp(accessToken)).StatusCode == HttpStatusCode.Forbidden, "Plan expiration immediately blocks existing access token");
Check((await Exchange(refreshToken, true)).StatusCode == HttpStatusCode.BadRequest, "Plan expiration blocks refresh");
eligibility.Enabled = true;
var renewed = await Exchange(refreshToken, true);
Check(renewed.StatusCode == HttpStatusCode.OK, "Eligible paid account can refresh token");
var refreshed = JsonNode.Parse(await renewed.Content.ReadAsStringAsync())!;
var newAccess = refreshed["access_token"]!.GetValue<string>();
var newRefresh = refreshed["refresh_token"]!.GetValue<string>();
Check(newRefresh != refreshToken, "Refresh token rotates");
var integrations = await http.GetAsync("/connect/integrations");
CaptureCookies(integrations);
var panel = await integrations.Content.ReadAsStringAsync();
Check(integrations.Headers.GetValues("Content-Security-Policy").Single().Contains("form-action 'self';"), "Integration management retains same-origin-only form policy");
var grantId = WebUtility.HtmlDecode(Regex.Match(panel, "name=\"id\" value=\"([^\"]+)\"").Groups[1].Value);
var revokeCsrf = WebUtility.HtmlDecode(Regex.Match(panel, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
Check(grantId.Length > 0, "Integration panel lists active grant");
CaptureCookies(await http.GetAsync("/fixture/login/2"));
Check((await http.PostAsync("/connect/integrations/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
    { ["id"] = grantId, ["__RequestVerificationToken"] = revokeCsrf }))).StatusCode != HttpStatusCode.Redirect,
    "Another account cannot revoke the grant");
CaptureCookies(await http.GetAsync("/fixture/login/1"));
integrations = await http.GetAsync("/connect/integrations"); CaptureCookies(integrations);
panel = await integrations.Content.ReadAsStringAsync();
revokeCsrf = WebUtility.HtmlDecode(Regex.Match(panel, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
Check((await http.PostAsync("/connect/integrations/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
    { ["id"] = grantId, ["__RequestVerificationToken"] = revokeCsrf }))).StatusCode == HttpStatusCode.Redirect,
    "Owner can revoke OAuth grant");
Check((await CallMcp(newAccess)).StatusCode == HttpStatusCode.Unauthorized, "Revocation immediately invalidates access token");
Check((await Exchange(newRefresh, true)).StatusCode == HttpStatusCode.BadRequest, "Revocation blocks refresh token");
Check((await Exchange(code)).StatusCode == HttpStatusCode.BadRequest, "Authorization code cannot be reused");
await app.StopAsync();
if (args.Contains("--live-company-shape"))
{
    var configuration = new ConfigurationBuilder().AddUserSecrets("ColdCall-LocalDevelopment").Build();
    var sqlConnection = configuration.GetConnectionString("Firmy")!;
    var sql = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sqlConnection);
    if (!sql.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The live shape check is restricted to local SQL Server.");
    await using var liveDb = new ColdCallDbContext(new DbContextOptionsBuilder<ColdCallDbContext>().UseSqlServer(sqlConnection).Options);
    var userId = await liveDb.Users.Where(user => user.IsEnabled && liveDb.UserPlans.Any(plan =>
        plan.AppUserId == user.Id && plan.ExpiresAtUtc > DateTime.UtcNow && !plan.PlanDefinition.IsTrial))
        .Select(user => user.Id).FirstAsync();
    var expected = await liveDb.Companies.AsNoTracking().Where(company => !company.BlockedByRODO && company.Nip != null
        && company.ApartmentNumber != null && company.MiejscowoscId != null).Select(company => new
        {
            company.Nip, company.Street, company.BuildingNumber, company.ApartmentNumber,
            Wojewodztwo = company.Wojewodztwo!.Name, Powiat = company.Powiat!.Name,
            Gmina = company.Gmina!.Name, Miejscowosc = company.Miejscowosc!.Name,
            Status = company.StatusDzialalnosci!.Nazwa
        }).FirstAsync();
    var actual = await new AiCompanySearchService(liveDb).GetCompanyAsync(userId, expected.Nip!, default);
    Check(actual is not null && actual.Wojewodztwo == expected.Wojewodztwo && actual.Powiat == expected.Powiat
        && actual.Gmina == expected.Gmina && actual.Miejscowosc == expected.Miejscowosc
        && actual.Ulica == expected.Street && actual.BuildingNumber == expected.BuildingNumber
        && actual.ApartmentNumber == expected.ApartmentNumber && actual.StatusDzialalnosci == expected.Status,
        "Local SQL projection returns the original address components and business status");
}
Console.WriteLine($"OAuth smoke checks passed: {checks}");

sealed class TestPaidAccess : IMcpPaidAccountAccess
{
    public bool Enabled { get; set; } = true;
    public Task<bool> AllowedAsync(int userId, CancellationToken token) => Task.FromResult(userId == 1 && Enabled);
}

sealed class TestColdCallDbContext(DbContextOptions<ColdCallDbContext> options) : ColdCallDbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // In-memory fixtures can seed the SQL view only when a test key is assigned.
        builder.Entity<CompanyShortView>().HasKey(row => row.Id);
    }
}
