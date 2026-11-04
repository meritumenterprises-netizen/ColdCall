using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ColdCall.Services;

public static class SmtpMailSender
{
    public static SecureSocketOptions SecurityOptions(int port, bool enableSsl) => !enableSsl
        ? SecureSocketOptions.None : port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    public static async Task SendAsync(IConfiguration configuration, string email, string subject, string body, CancellationToken token, string? replyTo = null, string smtpSection = "Smtp")
    {
        var host = configuration[$"{smtpSection}:Host"] ?? configuration["Smtp:Host"];
        var user = configuration[$"{smtpSection}:User"];
        var password = configuration[$"{smtpSection}:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"Uzupelnij konfiguracje {smtpSection}: serwer, login i haslo.");
        var port = configuration.GetValue<int?>($"{smtpSection}:Port") ?? configuration.GetValue<int?>("Smtp:Port") ?? 465;
        var security = SecurityOptions(port, configuration.GetValue<bool?>($"{smtpSection}:EnableSsl")
            ?? configuration.GetValue<bool?>("Smtp:EnableSsl") ?? true);
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(configuration[$"{smtpSection}:From"] ?? user));
        message.To.Add(MailboxAddress.Parse(email));
        if (!string.IsNullOrWhiteSpace(replyTo)) message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        using var client = new SmtpClient { Timeout = 15000 };
        await client.ConnectAsync(host, port, security, token);
        await client.AuthenticateAsync(user, password, token);
        await client.SendAsync(message, token);
        await client.DisconnectAsync(true, token);
    }
}
