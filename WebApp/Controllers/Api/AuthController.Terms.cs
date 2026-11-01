using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AuthController
{
    [HttpGet("terms")]
    public async Task<IActionResult> Terms(CancellationToken token)
    {
        var row = await _db.GlobalSettings.AsNoTracking().OrderByDescending(settings => settings.GlobalSettingsId)
            .Select(settings => new { settings.Regulamin }).FirstOrDefaultAsync(token);
        return row == null ? StatusCode(503, new { message = "Regulamin jest chwilowo niedostepny." }) : Ok(row);
    }

    [HttpPost("terms")]
    public async Task<IActionResult> DecideTerms(TermsDecisionRequest request, CancellationToken token)
    {
        if (!request.Accepted.HasValue) return BadRequest();
        var user = await _currentUserService.GetOrCreateAsync(User);
        user.TermsOfServiceAccepted = request.Accepted.Value;
        await _db.SaveChangesAsync(token);
        if (!request.Accepted.Value)
            await HttpContext.SignOutAsync(LocalAuthentication.Scheme);
        return Ok(new { accepted = request.Accepted.Value });
    }
}
