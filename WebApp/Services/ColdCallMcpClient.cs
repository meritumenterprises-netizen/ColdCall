using System.Text.Json;
using ColdCall.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ColdCall.Services;

public sealed class ColdCallMcpClient(IConfiguration configuration)
{
    public async Task<AiCompanySearchResult> SearchAsync(int userId, AiCompanySearchRequest request, CancellationToken token)
    {
        var endpoint = new Uri(configuration["Mcp:Endpoint"] ?? "http://127.0.0.1:5086/mcp");
        if (endpoint.Scheme != "https" && !(endpoint.IsLoopback && endpoint.Scheme == "http"))
            throw new InvalidOperationException("MCP requires HTTPS or loopback HTTP.");
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + McpSessionToken.Create(userId, configuration)
            }
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: token);
        var result = await client.CallToolAsync("search_companies",
            new Dictionary<string, object?> { ["request"] = request }, cancellationToken: token);
        if (result.IsError == true) throw new InvalidOperationException("MCP search failed.");
        var json = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return JsonSerializer.Deserialize<AiCompanySearchResult>(json ?? "", new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("MCP returned no search results.");
    }
}
