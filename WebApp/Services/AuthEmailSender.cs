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
        if (!string.IsNullOrWhiteSpace(configuration["Smtp:Host"]))
        {
            await SmtpMailSender.SendAsync(configuration, email, subject, body, token);
            return;
        }
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Skonfiguruj SMTP do wysylania wiadomosci uwierzytelniania.");
        var folder = Path.GetFullPath(configuration["LocalAuth:DevelopmentMailFolder"] ?? "App_Data/AuthMail", environment.ContentRootPath);
        Directory.CreateDirectory(folder);
        using var message = new MailMessage(configuration["Smtp:From"] ?? "noreply@coldcall.local", email, subject, body);
        message.SubjectEncoding = System.Text.Encoding.UTF8;
        message.BodyEncoding = System.Text.Encoding.UTF8;
        using var client = new SmtpClient
        {
            DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = folder
        };
        await client.SendMailAsync(message, token);
    }
}
