using ColdCall.Services;
using ColdCall.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Net.Sockets;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Reject(string json) { try { AiSearchPlanner.Parse(json); throw new Exception("Accepted invalid plan"); } catch (Exception ex) when (ex is JsonException or ArgumentException) { checks++; } }
var parsed = AiSearchPlanner.Parse("""{"city":"Toruń","radiusKm":20,"statusIds":[1],"industry":"biura rachunkowe"}""");
Check(parsed.Request.City == "Toruń" && parsed.Request.RadiusKm == 20 && parsed.Industry == "biura rachunkowe", "Polish search parsing");
var regionPlan = AiSearchPlanner.Parse("""{"voivodeship":"kujawsko-pomorskie","hasWebsite":true,"industry":"uslugi tatuazu"}""");
Check(regionPlan.Request.Voivodeship == "kujawsko-pomorskie" && regionPlan.Request.City == null
    && regionPlan.Request.HasWebsite == true, "Voivodeship and website filter parsing");
var countyPlan = AiSearchPlanner.Parse("""{"voivodeship":"kujawsko-pomorskie","county":"Grudziądz","hasPhone":true}""");
Check(countyPlan.Request.County == "Grudziądz" && countyPlan.Request.Municipality == null
    && countyPlan.Request.City == null, "County does not select lower address levels");
var municipalityPlan = AiSearchPlanner.Parse("""{"municipality":"Grudziądz"}""");
Check(AiSearchPlanner.Parse("""{"statusIds":[1],"hasAddress":false}""").Request.HasAddress == false,
    "Missing business address filter accepted");
Check(AiSearchPlanner.Parse("""{"hasAddress":true}""").Request.HasAddress == true,
    "Present business address filter accepted");
var hashProfile = new SearchProfile { Criteria = new SearchProfileCriteria() };
var originalHash = SearchProfileHashCalculator.Calculate(hashProfile);
hashProfile.Criteria.HasAddress = false;
var withoutAddressHash = SearchProfileHashCalculator.Calculate(hashProfile);
hashProfile.Criteria.HasAddress = true;
Check(originalHash != withoutAddressHash && withoutAddressHash != SearchProfileHashCalculator.Calculate(hashProfile),
    "Address presence changes saved-search and CSV fingerprints");
hashProfile.Criteria.HasAddress = null;
Check(originalHash == SearchProfileHashCalculator.Calculate(hashProfile), "Unset address filter preserves legacy fingerprints");
Check(municipalityPlan.Request.Municipality == "Grudziądz" && municipalityPlan.Request.City == null,
    "Municipality does not select a city");
Check(AiSearchPlanner.Parse("```json\n{\"city\":\"Toruń\",\"radiusKm\":20}\n```").Request.RadiusKm == 20, "Ollama JSON code fence");
Reject("```sql\nSELECT 1\n```");
Reject("""{"city":"Toruń","radiusKm":21}""");
foreach (var radius in new[] { 40, 60, 70, 80, 90, 150, 300, 400 })
    Check(AiSearchPlanner.Parse($$"""{"city":"Łódź","radiusKm":{{radius}}}""").Request.RadiusKm == radius,
        "new radius accepted without rounding: " + radius);
Reject("""{"statusIds":null}""");
Reject("""{"pkdCodes":null}""");
var fortyPkds = Enumerable.Range(1, 40).Select(i => new PkdDto(i, $"{i:00}.10.Z", "Test PKD")).ToArray();
Check(AiSearchPlanner.Parse(JsonSerializer.Serialize(new { pkdCodes = fortyPkds.Select(code => code.Code) }))
    .Request.PkdCodes.Length == 40, "Search accepts 40 PKD codes");
Reject(JsonSerializer.Serialize(new { pkdCodes = fortyPkds.Select(code => code.Code).Append("41.10.Z") }));
Check(PkdClassifier.Parse("OK\n" + string.Join("\n", fortyPkds.Select(code => code.Code)), fortyPkds).Count == 40,
    "Classifier accepts 40 PKD codes");
try { PkdClassifier.Parse("OK\n" + string.Join("\n", fortyPkds.Select(code => code.Code).Append("41.10.Z")), fortyPkds); throw new Exception("Accepted 41 PKD codes"); }
catch (FormatException) { checks++; Console.WriteLine("PASS classifier rejects 41 PKD codes"); }
Reject("""{"companyName":"ab"}""");
var todayInPoland = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Central European Standard Time"));
var openRange = AiSearchPlanner.Parse("""{"startedFrom":"2025-01-02","startedTo":null,"industry":"transport"}""").Request;
Check(openRange.StartedFrom == new DateOnly(2025, 1, 2) && openRange.StartedTo == todayInPoland,
    "open-ended date range ends today in Poland");
Check(AiSearchPlanner.Parse("""{"startedFrom":"2025-01-02"}""").Request.StartedTo == todayInPoland,
    "omitted end date ends today in Poland");
Check(AiSearchPlanner.Parse("""{"startedFrom":"2025-01-02","startedTo":"2025-02-01"}""").Request.StartedTo == new DateOnly(2025, 2, 1),
    "explicit end date preserved");
Check(AiSearchPlanner.Parse("{}").Request.StartedTo == null, "no date filter remains unset");
Reject("""{"startedFrom":"2025-02-01","startedTo":"2025-01-02"}""");
Reject("""{"startedFrom":"9999-01-01"}""");
Reject("""{"sql":"DELETE FROM Companies"}""");
Reject("""{"page":0}""");
Console.WriteLine("PASS invalid/unknown criteria rejected");
var installationCodes = new PkdDto[]
{
    new(1, "43.21.Z", "Wykonywanie instalacji elektrycznych"),
    new(2, "35.12.B", "Energetyka sloneczna"),
    new(3, "43.29.Z", "Wykonywanie pozostalych instalacji budowlanych")
};
var classificationConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Ollama:ApiKey"] = "test-only", ["Ollama:Model"] = "test-model" }).Build();
using var classificationHttp = new HttpClient(new ClassificationHandler());
var installation = await new PkdClassifier(classificationHttp, classificationConfig)
    .ClassifyAsync("instalacje fotowoltaiczne", installationCodes, CancellationToken.None);
Check(installation.Count == 1 && installation[0].Code == "43.21.Z", "installation classification resolves allowed catalog code");
try { PkdClassifier.Parse("OK\n99.99.Z", installationCodes); throw new Exception("Unknown PKD accepted"); }
catch (FormatException) { checks++; Console.WriteLine("PASS unknown PKD rejected"); }
int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
var hostUrl = $"http://127.0.0.1:{FreePort()}";
var fakeUrl = $"http://127.0.0.1:{FreePort()}";
var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Mcp:SigningKey"] = key, ["Mcp:Endpoint"] = fakeUrl + "/mcp" }).Build();
var jwt = new JwtSecurityTokenHandler().ReadJwtToken(McpSessionToken.Create(123, config));
Check(jwt.Subject == "123" && jwt.Audiences.Single() == McpSessionToken.Audience && jwt.ValidTo < DateTime.UtcNow.AddMinutes(6), "dedicated short-lived identity token");
var serverPath = Path.Combine(AppContext.BaseDirectory, "ColdCall.McpServer.dll");
var start = new ProcessStartInfo("dotnet", '"' + serverPath + '"') { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
start.Environment["Mcp__SigningKey"] = key;
start.Environment["Mcp__ListenUrl"] = hostUrl;
start.Environment["ConnectionStrings__Firmy"] = "Server=127.0.0.1;Database=unused;Integrated Security=true;TrustServerCertificate=true";
start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
start.Environment["Logging__LogLevel__Default"] = "Warning";
using var process = Process.Start(start)!;
var output = process.StandardOutput.ReadToEndAsync();
var errors = process.StandardError.ReadToEndAsync();
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
try {
    bool ready = false;
    for (int i=0;i<50;i++) { try { ready = (await http.GetAsync(hostUrl + "/health")).IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) {} await Task.Delay(200); }
    Check(ready, "MCP host health");
    var anonymous = await http.PostAsync(hostUrl + "/mcp", new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
    Console.WriteLine("Anonymous status: " + anonymous.StatusCode);
    Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "anonymous rejected");
    async Task<HttpStatusCode> Status(string token) { using var req = new HttpRequestMessage(HttpMethod.Post,hostUrl + "/mcp"); req.Headers.Authorization = new("Bearer",token); req.Content = new StringContent("{}",System.Text.Encoding.UTF8,"application/json"); return (await http.SendAsync(req)).StatusCode; }
    string Token(string audience, bool scope, DateTime expiry) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(McpSessionToken.Issuer, audience,
        scope ? [new Claim("sub","123"),new Claim("scope","companies:read")] : [new Claim("sub","123")], expires: expiry,
        signingCredentials: new SigningCredentials(McpSessionToken.Key(config), SecurityAlgorithms.HmacSha256)));
    Check(await Status(Token("wrong",true,DateTime.UtcNow.AddMinutes(1))) == HttpStatusCode.Unauthorized, "wrong audience rejected");
    Check(await Status(Token(McpSessionToken.Audience,true,DateTime.UtcNow.AddMinutes(-1))) == HttpStatusCode.Unauthorized, "expired token rejected");
    Check(await Status(Token(McpSessionToken.Audience,false,DateTime.UtcNow.AddMinutes(1))) == HttpStatusCode.Forbidden, "missing scope rejected");
    await using var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(hostUrl + "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
        AdditionalHeaders = new Dictionary<string,string> { ["Authorization"] = "Bearer " + McpSessionToken.Create(123,config) } });
    await using var client = await McpClient.CreateAsync(transport);
    var tools = await client.ListToolsAsync();
    Check(client.ServerInfo?.Name == "FindMyClient", "MCP advertises the FindMyClient brand");
    Check(tools.All(tool => !(tool.Description ?? "").Contains("ColdCall")), "Tool descriptions use the public brand");
    Check(tools.Count == 3 && tools.Any(t=>t.Name=="search_companies") && tools.Any(t=>t.Name=="count_companies") && tools.Any(t=>t.Name=="get_company"), "real MCP initialization and tools/list");
} finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await errors; await output; }

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls(fakeUrl);
builder.Logging.ClearProviders();
builder.Services.AddMcpServer().WithHttpTransport().WithTools<FakeTools>();
await using var app = builder.Build();
app.MapMcp("/mcp");
await app.StartAsync();
try {
    var result = await new ColdCallMcpClient(config).SearchAsync(123, parsed.Request, CancellationToken.None);
    Check(result.Total == 42 && result.Criteria.FirmyWPromieniu == 20, "ColdCall client real MCP tool round-trip and JSON deserialization");
} finally { await app.StopAsync(); }
Console.WriteLine($"{checks} checks passed. No SQL connection or external AI calls were made.");

if (args.Contains("--live-search") || args.Contains("--live-geography")) {
    var root = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (root != null && !File.Exists(Path.Combine(root.FullName, "ColdCall.slnx"))) root = root.Parent;
    var live = new ConfigurationBuilder().AddJsonFile(Path.Combine(root?.FullName
        ?? throw new InvalidOperationException("Run live checks from the ColdCall repository."), "WebApp/appsettings.json"))
        .AddUserSecrets("ColdCall-LocalDevelopment").Build();
    var options = new DbContextOptionsBuilder<ColdCallDbContext>().UseSqlServer(live.GetConnectionString("Firmy"), sql=>sql.CommandTimeout(120)).Options;
    await using var db = new ColdCallDbContext(options);
    using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    if (args.Contains("--apply-address-schema"))
    {
        await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,
            "WebApp/Sql/SearchProfileHasAddress.sql"), cancel.Token), cancel.Token);
        Console.WriteLine("Applied idempotent HasAddress schema update to local configured database.");
    }
    var adminId = await db.Users.Where(u=>u.IsAdmin && u.IsEnabled).Select(u=>u.Id).FirstAsync(cancel.Token);
    if (args.Contains("--live-geography"))
    {
        foreach (var city in new[] { "Toruń", "Łódź" })
        {
            var request = new AiCompanySearchRequest { City = city, RadiusKm = 20, PkdCodes = ["69.20.A", "69.20.Z"] };
            var found = await new AiCompanySearchService(db).SearchAsync(adminId, request, cancel.Token);
            var municipality = await db.Gminy.Where(g => g.Id == found.Criteria.GminaId).Select(g => g.Name).SingleAsync(cancel.Token);
            Check(municipality == city && found.Total > 0 && found.Items.Count <= 10, "city-wide municipality and radius results: " + city);
            Console.WriteLine($"LIVE {city}: {found.Total} matches.");
            if (args.Contains("--mcp"))
            {
                var result = await new ColdCallMcpClient(live).SearchAsync(adminId, request, cancel.Token);
                Check(result.Total == found.Total && result.Criteria.GminaId == found.Criteria.GminaId, "live MCP geography: " + city);
            }
        }
    }
    else
    {
    using var cloud = new HttpClient(new TimingHandler()) { Timeout=TimeSpan.FromMinutes(2) };
    var planner = new AiSearchPlanner(cloud,live,db,new PkdClassifier(cloud,live));
    var questionIndex = Array.IndexOf(args, "--question");
    if (questionIndex >= 0 && questionIndex + 1 >= args.Length) throw new ArgumentException("--question requires text.");
    var question = questionIndex >= 0 ? args[questionIndex + 1] : "Ile aktywnych biur rachunkowych jest w promieniu 20 km od Torunia?";
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var plan = await planner.PlanAsync(question,cancel.Token);
    Console.WriteLine($"LIVE planning: {timer.Elapsed.TotalSeconds:F2}s; PKD: {string.Join(", ", plan.PkdCodes)}");
    Console.WriteLine($"LIVE interpreted city={plan.City}, radius={plan.RadiusKm}, PKD count={plan.PkdCodes.Length}");
    Console.WriteLine($"LIVE voivodeship={plan.Voivodeship}, hasWebsite={plan.HasWebsite}");
    Console.WriteLine($"LIVE county={plan.County}, municipality={plan.Municipality}, hasPhone={plan.HasPhone}");
    if (args.Contains("--plan-only")) return;
    timer.Restart();
    var found = await new AiCompanySearchService(db).SearchAsync(adminId,plan,cancel.Token);
    Console.WriteLine($"LIVE database search: {timer.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"LIVE search returned {found.Total} matches; first page has {found.Items.Count} records.");
    if (plan.HasAddress.HasValue)
    {
        Check(found.Criteria.HasAddress == plan.HasAddress, "Address presence returned to dashboard filters");
        var addressed = db.Companies.Where(row => row.MiejscowoscId.HasValue
            || (row.PostalCode != null && row.PostalCode.Trim() != "")
            || (row.Street != null && row.Street.Trim() != "")
            || (row.BuildingNumber != null && row.BuildingNumber.Trim() != "")
            || (row.ApartmentNumber != null && row.ApartmentNumber.Trim() != ""));
        var ids = found.Items.Select(row => row.Id).ToArray();
        var addressedCount = await addressed.CountAsync(row => ids.Contains(row.Id), cancel.Token);
        Check(plan.HasAddress.Value ? addressedCount == ids.Length : addressedCount == 0, "Address presence filters live company results");
        Check(!await db.Companies.AnyAsync(row => ids.Contains(row.Id) && row.StatusDzialalnosciId != 1, cancel.Token),
            "Active status preserved alongside address presence");
        var withAddress = await new CompanySearchQuery(db).BuildCompaniesAsync(new SearchProfileCriteria { HasAddress = true }, [], null, token: cancel.Token);
        var withoutAddress = await new CompanySearchQuery(db).BuildCompaniesAsync(new SearchProfileCriteria { HasAddress = false }, [], null, token: cancel.Token);
        var allActive = await new CompanySearchQuery(db).BuildCompaniesAsync(new SearchProfileCriteria(), [], null, token: cancel.Token);
        Check(await withAddress.CountAsync(cancel.Token) + await withoutAddress.CountAsync(cancel.Token) == await allActive.CountAsync(cancel.Token),
            "With/without address partition active companies completely");
        var savedCriteria = await db.Set<SearchProfileCriteria>().AsNoTracking().FirstOrDefaultAsync(cancel.Token);
        Console.WriteLine("LIVE HasAddress column readable through EF; returned criteria HasAddress=" + found.Criteria.HasAddress);
    }
    if (!string.IsNullOrWhiteSpace(plan.County) || !string.IsNullOrWhiteSpace(plan.Municipality))
    {
        Check(found.Criteria.PowiatId.HasValue, "Administrative area has its county selected");
        if (string.IsNullOrWhiteSpace(plan.City)) Check(found.Criteria.MiejscowoscId == null, "No unrequested city selected");
        if (string.IsNullOrWhiteSpace(plan.Municipality) && string.IsNullOrWhiteSpace(plan.City))
            Check(found.Criteria.GminaId == null, "No unrequested municipality selected");
        if (!string.IsNullOrWhiteSpace(plan.Municipality)) Check(found.Criteria.GminaId.HasValue, "Requested municipality selected");
        Console.WriteLine($"LIVE address IDs: province={found.Criteria.WojewodztwoId}, county={found.Criteria.PowiatId}, municipality={found.Criteria.GminaId}, city={found.Criteria.MiejscowoscId}");
        var ids = found.Items.Select(row => row.Id).ToArray();
        Check(!await db.Companies.AnyAsync(row => ids.Contains(row.Id) && (row.PowiatId != found.Criteria.PowiatId
            || (found.Criteria.GminaId.HasValue && row.GminaId != found.Criteria.GminaId)), cancel.Token), "Administrative area filters company results");
    }
    if (!string.IsNullOrWhiteSpace(plan.Voivodeship))
    {
        Check(found.Criteria.WojewodztwoId.HasValue, "Voivodeship returned to address filters");
        var resultIds = found.Items.Select(row => row.Id).ToArray();
        Check(!await db.Companies.AnyAsync(row => resultIds.Contains(row.Id) && row.WojewodztwoId != found.Criteria.WojewodztwoId, cancel.Token),
            "Returned companies belong to selected voivodeship");
    }
    if (args.Contains("--mcp"))
    {
        timer.Restart();
        var mcpResult = await new ColdCallMcpClient(live).SearchAsync(adminId, plan, cancel.Token);
        Console.WriteLine($"LIVE MCP including database search: {timer.Elapsed.TotalSeconds:F2}s");
        Check(mcpResult.Total == found.Total && mcpResult.Items.Count == found.Items.Count,
            "live database search through running MCP server");
    }
    }
}

public sealed class TimingHandler : DelegatingHandler
{
    private int calls;
    public TimingHandler() : base(new HttpClientHandler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var number = ++calls;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var response = await base.SendAsync(request, token);
        await response.Content.LoadIntoBufferAsync(token);
        Console.WriteLine($"LIVE Ollama call {number}: {timer.Elapsed.TotalSeconds:F2}s; HTTP {(int)response.StatusCode}");
        return response;
    }
}

[McpServerToolType]
public sealed class FakeTools {
    [McpServerTool(Name="search_companies")]
    public string Search(AiCompanySearchRequest request) => JsonSerializer.Serialize(new AiCompanySearchResult(
        new SearchProfileCriteriaDto { FirmyWPromieniu = request.RadiusKm }, [], 42,100,1,[],"Znaleziono 42 firm."), new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

public sealed class ClassificationHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        if (!prompt.Contains("43.21.Z") || !prompt.Contains("wytwarzanie energii")
            || body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() != "instalacje fotowoltaiczne")
            throw new Exception("Installation-specific classification instructions missing");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = "OK\n43.21.Z" } }))
        };
    }
}
