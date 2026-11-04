using ColdCall.Models;

namespace ColdCall.Services;

public interface IFeedbackEmailSender
{
    Task SendAsync(FeedbackRequest request, CancellationToken token);
}

public sealed class FeedbackEmailSender(IConfiguration configuration) : IFeedbackEmailSender
{
    public Task SendAsync(FeedbackRequest request, CancellationToken token)
    {
        var body = $"Imię: {request.FirstName.Trim()}\nNazwisko: {request.LastName.Trim()}\n"
            + $"Email: {request.Email.Trim()}\n\nKomentarz:\n{request.Comment.Trim()}";
        return SmtpMailSender.SendAsync(configuration, "feedback@findmyclient.pl", "FindMyClient - Feedback",
            body, token, replyTo: request.Email.Trim(), smtpSection: "SmtpFeedback");
    }
}
