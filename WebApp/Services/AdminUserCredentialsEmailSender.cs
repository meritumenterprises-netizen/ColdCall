namespace ColdCall.Services;

public class AdminUserCredentialsEmailSender(IAuthEmailSender sender, IConfiguration configuration,
    ILogger<AdminUserCredentialsEmailSender> logger)
{
    public async Task<bool> TrySendAsync(string email, string password, CancellationToken token)
    {
        try
        {
            if (!Uri.TryCreate(configuration["LocalAuth:FrontendUrl"], UriKind.Absolute, out var frontend)
                || frontend.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Configure an HTTPS LocalAuth:FrontendUrl.");
            var body = "Utworzono Twoje konto w FindMyClient.\n\n"
                + $"Adres logowania: {frontend.AbsoluteUri}\nLogin: {email}\nHaslo: {password}\n\n"
                + "Nie udostepniaj tej wiadomosci innym osobom.";
            await sender.SendAsync(email, "FindMyClient - dane logowania", body, token);
            return true;
        }
        catch (Exception)
        {
            // Do not log SMTP exceptions or message content containing the password.
            logger.LogWarning("Account created, but delivery of its credentials email failed.");
            return false;
        }
    }
}
