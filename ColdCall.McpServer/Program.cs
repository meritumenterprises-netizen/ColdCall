using ColdCall.Data;
using ColdCall.Services;
using ColdCall.McpServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
// IIS supplies its own binding; standalone development uses a configurable loopback port.
if (string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_PORT"]) &&
    string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_IIS_PHYSICAL_PATH"]))
{
    builder.WebHost.UseUrls(builder.Configuration["Mcp:ListenUrl"] ?? builder.Configuration["urls"] ?? "http://127.0.0.1:5086");
}
builder.Services.AddDbContext<ColdCallDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Firmy") ?? throw new InvalidOperationException("ConnectionStrings:Firmy is required.")));
builder.Services.AddScoped<AiCompanySearchService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
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
var publicOAuth = builder.AddPublicOAuth();
builder.Services.AddAuthorization(options => options.AddPolicy("McpRead", policy =>
    policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.FindAll("scope").Any(claim => claim.Value.Split(' ').Contains(McpOAuthSettings.ReadScope)))));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("Mcp", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddMcpServer(options => options.ServerInfo = new()
{
    Name = "FindMyClient",
    Version = typeof(CompanyTools).Assembly.GetName().Version?.ToString() ?? "1.0.0"
}).WithHttpTransport(options =>
    options.SessionMode = ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateless).WithTools<CompanyTools>();
var app = builder.Build();
app.MapPublicOAuth(publicOAuth);
app.UseAuthentication();
app.UsePaidOAuthAccounts(publicOAuth);
app.UseAuthorization();
app.UseRateLimiter();
app.MapMcp("/mcp").RequireAuthorization("McpRead").RequireRateLimiting("Mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
