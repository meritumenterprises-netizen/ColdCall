using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;

namespace ColdCall.Controllers.Api;

public partial class AuthController
{
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.CompanyName = string.IsNullOrWhiteSpace(request.CompanyName) ? null : request.CompanyName.Trim();
        user.PostalCode = request.PostalCode.Trim();
        user.City = request.City.Trim();
        user.Street = request.Street.Trim();
        user.StreetNumber = request.StreetNumber.Trim();
        user.DisplayName = user.FirstName + " " + user.LastName;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return NoContent();
    }
}
