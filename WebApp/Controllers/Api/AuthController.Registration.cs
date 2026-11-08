using Microsoft.AspNetCore.Mvc;

namespace ColdCall.Controllers.Api;

public partial class AuthController
{
    private IActionResult RegistrationSuspendedResponse() => StatusCode(StatusCodes.Status403Forbidden, new
    {
        code = "RegistrationSuspended",
        message = "Rejestracja nowych uzytkownikow jest wstrzymana. Sprobuj ponownie pozniej."
    });
}
