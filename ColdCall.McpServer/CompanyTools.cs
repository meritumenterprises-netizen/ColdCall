using System.ComponentModel;
using System.Text.Json;
using ColdCall.Models;
using ColdCall.Services;
using ModelContextProtocol.Server;

namespace ColdCall.McpServer;

[McpServerToolType]
public sealed class CompanyTools(AiCompanySearchService search, IHttpContextAccessor accessor, ILogger<CompanyTools> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private int UserId => int.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) && id > 0
        ? id : throw new UnauthorizedAccessException("Brak zalogowanego uzytkownika.");
    private bool IsPublic => accessor.HttpContext?.User.FindFirst("iss")?.Value != McpSessionToken.Issuer;

    private async Task<int> RequireUserAsync(CancellationToken token)
    {
        var userId = UserId;
        if (IsPublic && !await accessor.HttpContext!.RequestServices.GetRequiredService<IMcpPaidAccountAccess>().AllowedAsync(userId, token))
            throw new UnauthorizedAccessException("MCP wymaga aktywnego platnego planu.");
        return userId;
    }

    [McpServerTool(Name = "search_companies"), Description("Search CEIDG companies by PKD codes, Polish city, radius in km, dates, statuses and contact filters. Returns all matching records up to the account record limit in one response, without pagination. The request page is ignored.")]
    public async Task<string> SearchCompanies(AiCompanySearchRequest request, CancellationToken cancellationToken)
    {
        var userId = await RequireUserAsync(cancellationToken);
        var result = await search.SearchAsync(userId, request, cancellationToken, IsPublic, returnAllAccessible: true);
        logger.LogInformation("MCP search_companies user={UserId} returned={Count}", userId, result.Items.Count);
        return JsonSerializer.Serialize(new { result.Criteria, result.PkdCodes, result.Total, result.RecordLimit, result.Items, result.Summary }, JsonOptions);
    }

    [McpServerTool(Name = "count_companies"), Description("Count companies matching the same validated search criteria and account permissions.")]
    public async Task<string> CountCompanies(AiCompanySearchRequest request, CancellationToken cancellationToken)
    {
        var userId = await RequireUserAsync(cancellationToken);
        var result = await search.SearchAsync(userId, request, cancellationToken, IsPublic);
        logger.LogInformation("MCP count_companies user={UserId} count={Count}", userId, result.Total);
        return JsonSerializer.Serialize(new { total = result.Total, recordLimit = result.RecordLimit }, JsonOptions);
    }

    [McpServerTool(Name = "get_company"), Description("Read company details by a 10-digit Polish NIP. Requires an active paid FindMyClient plan.")]
    public async Task<string> GetCompany(string nip, CancellationToken cancellationToken)
    {
        var userId = await RequireUserAsync(cancellationToken);
        var result = await search.GetCompanyAsync(userId, nip, cancellationToken);
        logger.LogInformation("MCP get_company user={UserId} found={Found}", userId, result != null);
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
