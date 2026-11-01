using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ColdCall.Services;

public static class SmtpMailSender
{
    public static SecureSocketOptions SecurityOptions(int port, bool enableSsl) => !enableSsl
        ? SecureSocketOptions.None : port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    public static async Task SendAsync(IConfiguration configuration, string email, string subject, string body, CancellationToken token)
    {
        var host = configuration["Smtp:Host"];
        var user = configuration["Smtp:User"];
        var password = configuration["Smtp:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Uzupelnij konfiguracje SMTP: serwer, login i haslo.");
        var port = configuration.GetValue<int?>("Smtp:Port") ?? 465;
        var security = SecurityOptions(port, configuration.GetValue<bool?>("Smtp:EnableSsl") ?? true);
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(configuration["Smtp:From"] ?? user));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        using var client = new SmtpClient { Timeout = 15000 };
        await client.ConnectAsync(host, port, security, token);
        await client.AuthenticateAsync(user, password, token);
        await client.SendAsync(message, token);
        await client.DisconnectAsync(true, token);
    }
}
