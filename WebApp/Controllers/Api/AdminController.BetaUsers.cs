using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpPut("users/{id:int}/beta")]
    public async Task<IActionResult> UpdateUserBeta(int id, UpdateUserBetaRequest request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!request.IsBetaUser.HasValue) return BadRequest();
        var isBetaUser = request.IsBetaUser.Value;
        var updated = await _db.Users.Where(user => user.Id == id && user.IsBetaUser != isBetaUser)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.IsBetaUser, isBetaUser)
                .SetProperty(user => user.TermsOfServiceAccepted, (bool?)false), token);
        return updated == 1 || await _db.Users.AnyAsync(user => user.Id == id, token)
            ? Ok(new { IsBetaUser = isBetaUser }) : NotFound();
    }
}
