using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpDelete("users/{id:int}/payments")]
    public async Task<IActionResult> DeleteUserPayment(int id, [FromQuery] string sessionId, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest();
        var deleted = await _db.StripePayments.Where(p => p.AppUserId == id && p.StripeSessionId == sessionId)
            .ExecuteDeleteAsync(token);
        return deleted == 1 ? NoContent() : NotFound();
    }

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
                AmountPaid = payment.AmountPaid,
                StripeSessionId = payment.StripeSessionId,
                HasInvoice = payment.PDFInvoice != null && payment.PDFInvoice.Length > 0
            }).ToListAsync(token);
        foreach (var payment in payments)
            payment.PaidAtUtc = DateTime.SpecifyKind(payment.PaidAtUtc, DateTimeKind.Utc);
        return Ok(payments);
    }

    [HttpGet("users/{id:int}/payments/invoice")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DownloadUserInvoice(int id, [FromQuery] string sessionId, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest();
        var payment = await _db.StripePayments.AsNoTracking()
            .Where(p => p.AppUserId == id && p.StripeSessionId == sessionId)
            .Select(p => new { p.PDFInvoice, p.PaidAtUtc, p.PlanDefinitionId }).SingleOrDefaultAsync(token);
        if (payment?.PDFInvoice == null || payment.PDFInvoice.Length == 0) return NotFound();
        return File(payment.PDFInvoice, "application/pdf",
            $"faktura-{payment.PaidAtUtc:yyyyMMdd}-{payment.PlanDefinitionId}.pdf");
    }
}
