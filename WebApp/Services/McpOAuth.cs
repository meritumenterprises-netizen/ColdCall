using System.Security.Cryptography.X509Certificates;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ColdCall.Services;

public sealed class McpOAuthSettings
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "https://localhost:44345/";
    public string Resource { get; set; } = "http://127.0.0.1:5086/mcp";
    public string? CertificateThumbprint { get; set; }
    public bool EnableDynamicRegistration { get; set; }
    public List<McpOAuthClient> Clients { get; set; } = [];
    public const string ReadScope = "companies:read";

    public static McpOAuthSettings Read(IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = configuration.GetSection("Mcp:OAuth").Get<McpOAuthSettings>() ?? new();
        if (!settings.Enabled) return settings;
        foreach (var address in new[] { settings.Issuer, settings.Resource })
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == "http")))
                throw new InvalidOperationException("Mcp:OAuth requires HTTPS Issuer and Resource (loopback HTTP only in Development).");
        settings.Issuer = new Uri(settings.Issuer).AbsoluteUri;
        if (new Uri(settings.Issuer).AbsolutePath != "/" || new Uri(settings.Resource).AbsolutePath != "/mcp")
            throw new InvalidOperationException("OAuth Issuer must be the ColdCall origin and Resource must end in /mcp at the resource origin.");
        return settings;
    }
}

public sealed class McpOAuthClient
{
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string[] RedirectUris { get; set; } = [];
}

public interface IMcpPaidAccountAccess
{
    Task<bool> AllowedAsync(int userId, CancellationToken token);
}

public sealed class McpPaidAccountAccess(ColdCallDbContext db) : IMcpPaidAccountAccess
{
    // Unlike the internal AI integration, public MCP has no administrator bypass.
    public Task<bool> AllowedAsync(int userId, CancellationToken token) => db.Users.AnyAsync(user =>
        user.Id == userId && user.IsEnabled && db.UserPlans.Any(plan => plan.AppUserId == user.Id &&
            plan.ExpiresAtUtc > DateTime.UtcNow && !plan.PlanDefinition.IsTrial), token);
}

public static class McpOAuth
{
    public static OpenIddictApplicationDescriptor DescribeClient(McpOAuthClient client, bool allowLoopback, string resource)
    {
        if (string.IsNullOrWhiteSpace(client.ClientId) || client.ClientId.Length > 200 ||
            string.IsNullOrWhiteSpace(client.DisplayName) || client.DisplayName.Length > 200 ||
            client.RedirectUris.Length is < 1 or > 10)
            throw new ArgumentException("ClientId, DisplayName and 1-10 redirect URLs are required.");
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId, DisplayName = client.DisplayName,
            ClientType = ClientTypes.Public, ConsentType = ConsentTypes.Explicit
        };
        foreach (var address in client.RedirectUris)
        {
            if (address.Length > 2000 || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                (uri.Scheme != "https" && !(allowLoopback && uri.Scheme == "http" && uri.IsLoopback)))
                throw new ArgumentException("Redirect URLs must use HTTPS (loopback HTTP only in Development).");
            descriptor.RedirectUris.Add(uri);
        }
        descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
            Permissions.Endpoints.Revocation, Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code,
            Permissions.Prefixes.Scope + McpOAuthSettings.ReadScope, Permissions.Prefixes.Resource + resource]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        return descriptor;
    }

    public static void AddStore(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<McpOAuthDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Firmy")).UseOpenIddict());
        services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore().UseDbContext<McpOAuthDbContext>());
        services.AddScoped<IMcpPaidAccountAccess, McpPaidAccountAccess>();
    }

    public static void AddServer(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = McpOAuthSettings.Read(configuration, environment);
        services.AddSingleton(settings);
        if (!settings.Enabled) return;
        services.AddSingleton<McpOAuthRequestLimiter>();
        AddStore(services, configuration);
        services.AddOpenIddict().AddServer(options =>
        {
            options.SetIssuer(new Uri(settings.Issuer));
            options.SetAuthorizationEndpointUris("/connect/authorize");
            options.SetTokenEndpointUris("/connect/token");
            options.SetRevocationEndpointUris("/connect/revoke");
            options.SetConfigurationEndpointUris("/.well-known/oauth-authorization-server", "/.well-known/openid-configuration");
            options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
            options.Configure(server => server.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));
            options.RegisterScopes(McpOAuthSettings.ReadScope, Scopes.OfflineAccess);
            options.RegisterResources(settings.Resource);
            options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ApplyConfigurationResponseContext>(handler =>
                handler.UseInlineHandler(context =>
                {
                    // These clients use PKCE without a shared secret; this is OAuth, not an OIDC login provider.
                    var methods = new OpenIddictParameter(System.Collections.Immutable.ImmutableArray.Create<string?>(ClientAuthenticationMethods.None));
                    context.Response.SetParameter("token_endpoint_auth_methods_supported", methods);
                    context.Response.SetParameter("revocation_endpoint_auth_methods_supported", methods);
                    context.Response.SetParameter("scopes_supported", new OpenIddictParameter(
                        System.Collections.Immutable.ImmutableArray.Create<string?>(McpOAuthSettings.ReadScope, Scopes.OfflineAccess)));
                    return default;
                }));
            if (settings.EnableDynamicRegistration)
                options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ApplyConfigurationResponseContext>(handler =>
                    handler.UseInlineHandler(context =>
                    {
                        context.Response.SetParameter("registration_endpoint", settings.Issuer.TrimEnd('/') + "/connect/register");
                        return default;
                    }));
            options.SetAccessTokenLifetime(TimeSpan.FromMinutes(5));
            options.SetRefreshTokenLifetime(TimeSpan.FromDays(3));
            // Only access tokens are readable JWTs; codes/refresh tokens remain encrypted.
            options.DisableAccessTokenEncryption();
            if (environment.IsDevelopment())
                options.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate();
            else
            {
                if (string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
                    throw new InvalidOperationException("Configure a persistent OAuth signing/encryption certificate thumbprint.");
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                // The configured thumbprint pins the OAuth key; a public TLS trust chain is not required.
                var now = DateTime.Now;
                var certificate = store.Certificates.Find(X509FindType.FindByThumbprint, settings.CertificateThumbprint, false)
                    .OfType<X509Certificate2>().SingleOrDefault(cert => cert.HasPrivateKey
                        && cert.NotBefore <= now && cert.NotAfter > now)
                    ?? throw new InvalidOperationException("OAuth certificate not found or private key unavailable.");
                options.AddSigningCertificate(certificate).AddEncryptionCertificate(certificate);
            }
            options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
        });
    }

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken token = default)
    {
        await using var scope = services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<McpOAuthSettings>();
        if (!settings.Enabled) throw new InvalidOperationException("Enable Mcp:OAuth before initialization.");
        var db = scope.ServiceProvider.GetRequiredService<McpOAuthDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name=N'McpOAuth'";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        if (count == 0) await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(token);
        else if (count != 4) throw new InvalidOperationException("Incomplete McpOAuth schema. Review database before continuing.");
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var client in settings.Clients)
        {
            var descriptor = DescribeClient(client, scope.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment(), settings.Resource);
            var existing = await manager.FindByClientIdAsync(client.ClientId, token);
            if (existing is null) await manager.CreateAsync(descriptor, token);
            else await manager.UpdateAsync(existing, descriptor, token);
        }
    }
}
