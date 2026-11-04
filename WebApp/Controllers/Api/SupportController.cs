using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ColdCall.Controllers.Api;

[ApiController]
[AllowAnonymous]
[Route("api/public/support")]
[EnableRateLimiting("local-auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportController(ISupportEmailSender sender, TurnstileVerifier turnstile, FeedbackSubmissionLimiter submissions, ILogger<SupportController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(FeedbackRequest request, CancellationToken token)
    {
        try
        {
            if (!turnstile.IsConfigured)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Weryfikacja antybotowa jest chwilowo niedostępna." });
            if (!await turnstile.VerifyAsync(request.TurnstileToken, HttpContext.Connection.RemoteIpAddress,
                token, expectedAction: "support"))
                return BadRequest(new { message = "Potwierdź weryfikację antybotową ponownie." });
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || (exception is OperationCanceledException && !token.IsCancellationRequested))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Weryfikacja antybotowa jest chwilowo niedostępna. Spróbuj ponownie." });
        }
        FeedbackReservation? reservation = null;
        try
        {
            reservation = await submissions.ReserveAsync(request.Email, token, category: "support");
            if (!reservation.Id.HasValue)
            {
                Response.Headers.RetryAfter = Math.Max(1, reservation.RetryAfterSeconds)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    code = "SupportLimitExceeded",
                    message = "Z tego adresu email można wysłać maksymalnie 2 zgłoszenia wsparcia w ciągu 24 godzin. Spróbuj ponownie później.",
                    retryAfterSeconds = reservation.RetryAfterSeconds
                });
            }
            await sender.SendAsync(request, token);
            return Ok(new { message = "Dziękujemy! Wiadomość została wysłana." });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await ReleaseReservation(reservation);
            return new EmptyResult();
        }
        catch (Exception exception)
        {
            await ReleaseReservation(reservation);
            logger.LogError(exception, "Unable to send support email.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Nie udało się wysłać wiadomości. Spróbuj ponownie później." });
        }
    }

    private async Task ReleaseReservation(FeedbackReservation? reservation)
    {
        if (reservation?.Id is not Guid id) return;
        try { await submissions.ReleaseAsync(id, CancellationToken.None); }
        catch (Exception exception) { logger.LogError(exception, "Unable to release failed support reservation."); }
    }
}
