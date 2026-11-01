using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpGet("users/{id:int}/payments")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UserPayments(int id, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!await _db.Users.AnyAsync(user => user.Id == id, token)) return NotFound();
        var payments = await _db.StripePayments.AsNoTracking().Where(payment => payment.AppUserId == id)
            .OrderByDescending(payment => payment.PaidAtUtc).ThenBy(payment => payment.StripeSessionId)
            .Select(payment => new AdminUserPaymentDto
            {
                PlanCode = payment.PlanDefinition.Code,
                PaidAtUtc = payment.PaidAtUtc,
                AmountPaid = payment.AmountPaid
            }).ToListAsync(token);
        foreach (var payment in payments)
            payment.PaidAtUtc = DateTime.SpecifyKind(payment.PaidAtUtc, DateTimeKind.Utc);
        return Ok(payments);
    }
}
