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
        var user = await _currentUserService.GetOrCreateAsync(User);
        var row = await _db.GlobalSettings.AsNoTracking().OrderByDescending(settings => settings.GlobalSettingsId)
            .Select(settings => new { Regulamin = user.IsBetaUser ? settings.RegulaminBeta : settings.Regulamin,
                IsBetaUser = user.IsBetaUser }).FirstOrDefaultAsync(token);
        if (user.IsBetaUser && string.IsNullOrWhiteSpace(row?.Regulamin))
            return StatusCode(503, new { message = "Regulamin beta jest chwilowo niedostepny." });
        return row == null ? StatusCode(503, new { message = "Regulamin jest chwilowo niedostepny." }) : Ok(row);
    }

    [HttpPost("terms")]
    public async Task<IActionResult> DecideTerms(TermsDecisionRequest request, CancellationToken token)
    {
        if (!request.Accepted.HasValue) return BadRequest();
        var user = await _currentUserService.GetOrCreateAsync(User);
        await using var transaction = await _db.Database.BeginTransactionAsync(token);
        var current = await _db.Users.FromSqlInterpolated($"SELECT * FROM ColdCall.Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = {user.Id}")
            .AsNoTracking().SingleAsync(token);
        if (request.Accepted.Value && !current.IsAdmin && current.IsBetaUser)
        {
            var betaTerms = await _db.GlobalSettings.AsNoTracking()
                .OrderByDescending(settings => settings.GlobalSettingsId)
                .Select(settings => settings.RegulaminBeta).FirstOrDefaultAsync(token);
            if (string.IsNullOrWhiteSpace(betaTerms))
                return StatusCode(503, new { message = "Regulamin beta jest chwilowo niedostepny." });
        }
        var completingLogin = request.Accepted.Value && !current.IsAdmin && current.TermsOfServiceAccepted != true;
        user.TermsOfServiceAccepted = request.Accepted.Value;
        if (completingLogin) await UserLoginHistory.AddAsync(_db, user, HttpContext);
        await _db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        if (!request.Accepted.Value)
            await HttpContext.SignOutAsync(LocalAuthentication.Scheme);
        return Ok(new { accepted = request.Accepted.Value });
    }
}
