using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class CompaniesController
{
    public record CompanySocialAccountsRequest([MaxLength(512)] string? FacebookAccount,
        [MaxLength(512)] string? LinkedInAccount);

    [HttpPut("{id:int}/social-accounts")]
    public async Task<IActionResult> UpdateCompanySocialAccounts(int id, CompanySocialAccountsRequest request, CancellationToken token)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (!user.IsAdmin && !await _db.UserPlans.AnyAsync(plan => plan.AppUserId == user.Id
                && plan.ExpiresAtUtc > DateTime.UtcNow, token))
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl albo nie zostal jeszcze zakupiony." });
        var facebook = string.IsNullOrWhiteSpace(request.FacebookAccount) ? null : request.FacebookAccount.Trim();
        var linkedIn = string.IsNullOrWhiteSpace(request.LinkedInAccount) ? null : request.LinkedInAccount.Trim();
        var updated = await _db.Companies.Where(company => company.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(company => company.FacebookAccount, facebook)
                .SetProperty(company => company.LinkedInAccount, linkedIn), token);
        return updated == 0 ? NotFound() : Ok(new { facebookAccount = facebook, linkedInAccount = linkedIn });
    }
}
