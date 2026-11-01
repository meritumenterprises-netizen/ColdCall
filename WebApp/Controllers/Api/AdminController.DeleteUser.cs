using ColdCall.Services;
using Microsoft.AspNetCore.Mvc;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, [FromServices] CsvExportCleanupService cleanup, CancellationToken token)
    {
        var admin = await _adminAccess.RequireAdminAsync(User);
        if (admin == null) return Forbid();
        if (id == admin.Id)
            return BadRequest(new { message = "Nie mozesz usunac wlasnego konta." });
        if (id <= 0) return NotFound();
        return await cleanup.DeleteAccountAsync(id, token) ? NoContent()
            : BadRequest(new { message = "Konto nie istnieje albo jest kontem administratora." });
    }
}
