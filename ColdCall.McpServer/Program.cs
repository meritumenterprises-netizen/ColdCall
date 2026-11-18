using ColdCall.Data;
using ColdCall.Services;
using ColdCall.McpServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Mcp:ListenUrl"] ?? "http://127.0.0.1:5086");
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
builder.Services.AddAuthorization(options => options.AddPolicy("McpRead", policy =>
    policy.RequireAuthenticatedUser().RequireClaim("scope", "companies:read")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("Mcp", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddMcpServer().WithHttpTransport().WithTools<CompanyTools>();
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapMcp("/mcp").RequireAuthorization("McpRead").RequireRateLimiting("Mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
