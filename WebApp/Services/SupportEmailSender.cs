using ColdCall.Models;

namespace ColdCall.Services;

public interface ISupportEmailSender
{
    Task SendAsync(FeedbackRequest request, CancellationToken token);
}

public sealed class SupportEmailSender(IConfiguration configuration) : ISupportEmailSender
{
    public Task SendAsync(FeedbackRequest request, CancellationToken token)
    {
        var body = $"Imię: {request.FirstName.Trim()}\nNazwisko: {request.LastName.Trim()}\n"
            + $"Email: {request.Email.Trim()}\n\nTreść:\n{request.Comment.Trim()}";
        return SmtpMailSender.SendAsync(configuration, "support@findmyclient.pl", "FindMyClient - Wsparcie techniczne",
            body, token, replyTo: request.Email.Trim(), smtpSection: "SmtpSupport");
    }
}
