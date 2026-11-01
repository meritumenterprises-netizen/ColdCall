using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpPut("users/{id:int}/admin")]
    public async Task<IActionResult> UpdateUserAdmin(int id, UpdateUserAdminRequest request, CancellationToken token)
    {
        var admin = await _adminAccess.RequireAdminAsync(User);
        if (admin == null) return Forbid();
        if (id == admin.Id)
            return BadRequest(new { message = "Nie mozesz zmienic wlasnych uprawnien administratora." });
        if (!request.IsAdmin.HasValue)
            return BadRequest(new { message = "Podaj uprawnienia administratora." });
        var isAdmin = request.IsAdmin.Value;
        var updated = await _db.Users.Where(user => user.Id == id && user.Id != admin.Id && (!user.IsAdmin || isAdmin))
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsAdmin, isAdmin), token);
        if (updated == 1) return Ok(new { IsAdmin = isAdmin });
        if (await _db.Users.AnyAsync(user => user.Id == id && user.IsAdmin, token))
            return BadRequest(new { message = "Nie mozesz odebrac uprawnien innemu administratorowi." });
        return NotFound();
    }
}
