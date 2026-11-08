using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ColdCall.Services;

public sealed class RegistrationSuspendedExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not RegistrationSuspendedException exception) return;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(new { code = "RegistrationSuspended", message = exception.Message })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
        context.ExceptionHandled = true;
    }
}
