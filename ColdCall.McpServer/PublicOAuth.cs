using System.IdentityModel.Tokens.Jwt;
using ColdCall.Services;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace ColdCall.McpServer;

public static class PublicOAuth
{
    public static McpOAuthSettings AddPublicOAuth(this WebApplicationBuilder builder)
    {
        var settings = McpOAuthSettings.Read(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(settings);
        if (!settings.Enabled) return settings;
        McpOAuth.AddStore(builder.Services, builder.Configuration);
        builder.Services.AddOpenIddict().AddValidation(options =>
        {
            options.SetIssuer(new Uri(settings.Issuer));
            options.AddAudiences(settings.Resource);
            options.EnableAuthorizationEntryValidation();
            options.EnableTokenEntryValidation();
            options.UseSystemNetHttp();
            options.UseAspNetCore();
        });
        builder.Services.AddAuthentication(options => options.DefaultScheme = "McpAuth")
            .AddPolicyScheme("McpAuth", "Internal MCP or public OAuth", options => options.ForwardDefaultSelector = context =>
            {
                // The unverified issuer only selects a validator; it never authenticates a token.
                var header = context.Request.Headers.Authorization.ToString();
                if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && header.Length < 32768)
                {
                    var handler = new JwtSecurityTokenHandler();
                    try
                    {
                        if (handler.CanReadToken(header[7..]) && handler.ReadJwtToken(header[7..]).Issuer == McpSessionToken.Issuer)
                            return "Bearer";
                    }
                    catch (ArgumentException) { }
                }
                return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            });
        return settings;
    }

    public static void MapPublicOAuth(this WebApplication app, McpOAuthSettings settings)
    {
        if (!settings.Enabled) return;
        var metadataUri = settings.Resource[..settings.Resource.LastIndexOf('/')] + "/.well-known/oauth-protected-resource/mcp";
        // This wrapper executes before authorization so every 401 advertises discovery.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/mcp"))
                context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode == 401)
                        context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{metadataUri}\", scope=\"{McpOAuthSettings.ReadScope}\"";
                    return Task.CompletedTask;
                });
            await next();
        });
        foreach (var path in new[] { "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/mcp" })
            app.MapGet(path, () => Results.Json(new
            {
                resource = settings.Resource,
                authorization_servers = new[] { settings.Issuer },
                scopes_supported = new[] { McpOAuthSettings.ReadScope },
                bearer_methods_supported = new[] { "header" }
            })).AllowAnonymous();
    }

    public static void UsePaidOAuthAccounts(this WebApplication app, McpOAuthSettings settings)
    {
        if (!settings.Enabled) return;
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/mcp") && context.User.Identity?.IsAuthenticated == true &&
                context.User.FindFirst("iss")?.Value != McpSessionToken.Issuer &&
                (!int.TryParse(context.User.FindFirst("sub")?.Value, out var userId) ||
                 !await context.RequestServices.GetRequiredService<IMcpPaidAccountAccess>().AllowedAsync(userId, context.RequestAborted)))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "active_paid_plan_required" });
                return;
            }
            await next();
        });
    }
}
