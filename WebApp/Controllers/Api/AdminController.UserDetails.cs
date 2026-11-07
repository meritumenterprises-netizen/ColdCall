using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpPut("users/{id:int}/details")]
    public async Task<IActionResult> UpdateUserDetails(int id, AdminUpdateUserDetailsRequest request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == id, token);
        if (user == null) return NotFound();
        DateTime? expiresAtUtc = null;
        if (request.ExpiresInDays.HasValue)
        {
            var plan = await _db.UserPlans.Where(p => p.AppUserId == id)
                .OrderByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id).FirstOrDefaultAsync(token);
            if (plan == null) return BadRequest(new { message = "Uzytkownik nie ma planu do zmiany daty wygasniecia." });
            expiresAtUtc = DateTime.UtcNow.AddDays(request.ExpiresInDays.Value);
            plan.ExpiresAtUtc = expiresAtUtc.Value;
        }
        user.FirstName = request.FirstName?.Trim();
        user.LastName = request.LastName?.Trim();
        user.CompanyName = request.CompanyName?.Trim();
        user.Nip = request.Nip?.Trim();
        user.Phone = request.Phone?.Trim();
        user.PostalCode = request.PostalCode?.Trim();
        user.City = request.City?.Trim();
        user.Street = request.Street?.Trim();
        user.StreetNumber = request.StreetNumber?.Trim();
        await _db.SaveChangesAsync(token);
        return Ok(new { user.FirstName, user.LastName, user.CompanyName, user.Nip, user.Phone,
            user.PostalCode, user.City, user.Street, user.StreetNumber, expiresAtUtc });
    }
}
