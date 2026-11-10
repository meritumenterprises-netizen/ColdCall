using System.Net;
using System.Net.Http.Json;

namespace ColdCall.Services;

public class TurnstileVerifier(HttpClient client, IConfiguration configuration, IWebHostEnvironment environment,
    ILogger<TurnstileVerifier> logger)
{
    public string SiteKey => configuration["Turnstile:SiteKey"] ?? "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SiteKey)
        && !string.IsNullOrWhiteSpace(configuration["Turnstile:SecretKey"]);

    public async Task<bool> VerifyAsync(string token, IPAddress? remoteIp, CancellationToken cancellationToken, string expectedAction = "register")
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            logger.LogWarning("Turnstile rejected before verification. Configured: {Configured}, token length: {TokenLength}, expected action: {ExpectedAction}",
                IsConfigured, token?.Length ?? 0, expectedAction);
            return false;
        }
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
        var accepted = result is { Success: true } && result.Action == expectedAction
            && (hosts.Contains(result.Hostname, StringComparer.OrdinalIgnoreCase)
                || (environment.IsDevelopment() && result.Hostname == "localhost"));
        if (!accepted)
            logger.LogWarning("Turnstile verification rejected. Success: {Success}, hostname: {Hostname}, action: {Action}, expected action: {ExpectedAction}, allowed hostnames: {AllowedHostnames}, error codes: {ErrorCodes}",
                result?.Success, result?.Hostname, result?.Action, expectedAction,
                string.Join(",", hosts), string.Join(",", result?.ErrorCodes ?? []));
        return accepted;
    }
}
