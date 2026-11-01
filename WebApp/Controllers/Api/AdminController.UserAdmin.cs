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
        var updated = await _db.Users.Where(user => user.Id == id && user.Id != admin.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsAdmin, isAdmin), token);
        return updated == 1 ? Ok(new { IsAdmin = isAdmin }) : NotFound();
    }
}
