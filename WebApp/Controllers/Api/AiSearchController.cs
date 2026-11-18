using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ColdCall.Controllers.Api;

[ApiController, Authorize, Route("api/ai-search"), EnableRateLimiting("ai-search")]
public sealed class AiSearchController(ICurrentUserService users, AiCompanySearchService search,
    AiSearchPlanner planner, ColdCallMcpClient mcp, ILogger<AiSearchController> logger) : ControllerBase
{
    public sealed record Question([Required, StringLength(2000, MinimumLength = 3)] string Text);

    [HttpPost]
    public async Task<IActionResult> Post(Question question)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(question.Text) || question.Text.Trim().Length < 3)
                return BadRequest(new { message = "Wpisz pytanie (co najmniej 3 znaki)." });
            var user = await users.GetOrCreateAsync(User);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await search.AuthorizeAsync(user.Id, timeout.Token);
            var request = await planner.PlanAsync(question.Text.Trim(), timeout.Token);
            var result = await mcp.SearchAsync(user.Id, request, timeout.Token);
            logger.LogInformation("AI search for user {UserId}: {Total} results", user.Id, result.Total);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
            or ModelContextProtocol.McpException or OperationCanceledException or FormatException or PkdClassificationRejectedException)
        {
            logger.LogWarning("AI search failed: {FailureType}", ex.GetType().Name);
            return StatusCode(502, new { message = "Wyszukiwanie AI jest chwilowo niedostepne. Sprobuj ponownie lub ustaw filtry recznie." });
        }
    }
}
