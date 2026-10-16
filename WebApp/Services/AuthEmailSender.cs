using System.Net;
using System.Net.Mail;

namespace ColdCall.Services;

public interface IAuthEmailSender
{
    Task SendAsync(string email, string subject, string body, CancellationToken token);
}

public class AuthEmailSender(IConfiguration configuration, IWebHostEnvironment environment) : IAuthEmailSender
{
    public async Task SendAsync(string email, string subject, string body, CancellationToken token)
    {
        using var message = new MailMessage(configuration["Smtp:From"] ?? "noreply@coldcall.local", email, subject, body);
        message.SubjectEncoding = System.Text.Encoding.UTF8;
        message.BodyEncoding = System.Text.Encoding.UTF8;
        var host = configuration["Smtp:Host"];
        using var client = new SmtpClient();
        if (string.IsNullOrWhiteSpace(host))
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Skonfiguruj SMTP do wysylania wiadomosci uwierzytelniania.");
            var folder = Path.GetFullPath(configuration["LocalAuth:DevelopmentMailFolder"] ?? "App_Data/AuthMail", environment.ContentRootPath);
            Directory.CreateDirectory(folder);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = folder;
        }
        else
        {
            client.Host = host;
            client.Port = configuration.GetValue<int?>("Smtp:Port") ?? 587;
            client.EnableSsl = configuration.GetValue<bool?>("Smtp:EnableSsl") ?? true;
            if (!string.IsNullOrWhiteSpace(configuration["Smtp:User"]))
                client.Credentials = new NetworkCredential(configuration["Smtp:User"], configuration["Smtp:Password"]);
        }
        await client.SendMailAsync(message, token);
    }
}
