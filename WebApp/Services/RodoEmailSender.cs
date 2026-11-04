using ColdCall.Models;

namespace ColdCall.Services;

public interface IRodoEmailSender
{
    Task SendAsync(RodoRequest request, CancellationToken token);
}

public sealed class RodoEmailSender(IConfiguration configuration) : IRodoEmailSender
{
    public Task SendAsync(RodoRequest request, CancellationToken token)
    {
        var body = $"Imię: {request.FirstName.Trim()}\nNazwisko: {request.LastName.Trim()}\n"
            + $"Email: {request.Email.Trim()}\nNIP: {request.Nip.Trim()}\n\nKomentarz:\n{request.Comment.Trim()}";
        return SmtpMailSender.SendAsync(configuration, "rodo@findmyclient.pl", "FindMyClient - RODO",
            body, token, replyTo: request.Email.Trim(), smtpSection: "SmtpRodo");
    }
}
