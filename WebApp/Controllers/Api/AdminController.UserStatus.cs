using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpPut("users/{id:int}/enabled")]
    public async Task<IActionResult> UpdateUserEnabled(int id, UpdateUserEnabledRequest request,
        CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!request.IsEnabled.HasValue)
            return BadRequest(new { message = "Podaj status aktywnosci uzytkownika." });
        var enabled = request.IsEnabled.Value;
        var updated = await _db.Users.Where(u => u.Id == id && !u.IsAdmin)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsEnabled, enabled), token);
        return updated == 1 ? Ok(new { IsEnabled = enabled }) : NotFound();
    }
}
