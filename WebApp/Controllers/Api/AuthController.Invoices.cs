using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AuthController
{
    [HttpGet("purchases/invoice")]
    public async Task<IActionResult> DownloadInvoice([FromQuery] string sessionId, CancellationToken token)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (user.IsAdmin) return Forbid();
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest();
        var payment = await _db.StripePayments.AsNoTracking()
            .Where(p => p.StripeSessionId == sessionId && p.AppUserId == user.Id)
            .Select(p => new { p.PDFInvoice, p.PaidAtUtc, p.PlanDefinitionId }).SingleOrDefaultAsync(token);
        if (payment?.PDFInvoice == null || payment.PDFInvoice.Length == 0) return NotFound();
        return File(payment.PDFInvoice, "application/pdf",
            $"faktura-{payment.PaidAtUtc:yyyyMMdd}-{payment.PlanDefinitionId}.pdf");
    }
}
