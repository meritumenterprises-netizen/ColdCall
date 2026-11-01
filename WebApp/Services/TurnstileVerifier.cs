using System.Net;
using System.Net.Http.Json;

namespace ColdCall.Services;

public class TurnstileVerifier(HttpClient client, IConfiguration configuration, IWebHostEnvironment environment)
{
    public string SiteKey => configuration["Turnstile:SiteKey"] ?? "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SiteKey)
        && !string.IsNullOrWhiteSpace(configuration["Turnstile:SecretKey"]);

    public async Task<bool> VerifyAsync(string token, IPAddress? remoteIp, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        var fields = new Dictionary<string, string>
        {
            ["secret"] = configuration["Turnstile:SecretKey"]!, ["response"] = token
        };
        if (remoteIp != null) fields["remoteip"] = remoteIp.ToString();
        using var form = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken);
        var hosts = configuration.GetSection("Turnstile:AllowedHostnames").Get<string[]>() ?? [];
        return result is { Success: true, Action: "register" }
            && (hosts.Contains(result.Hostname, StringComparer.OrdinalIgnoreCase)
                || (environment.IsDevelopment() && result.Hostname == "localhost"));
    }
}
