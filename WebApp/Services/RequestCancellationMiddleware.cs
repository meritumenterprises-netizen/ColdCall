namespace ColdCall.Services;

public sealed class RequestCancellationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.RequestAborted.IsCancellationRequested) return;
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Microsoft.Data.SqlClient.SqlException exception)
            when (SqlRequestCancellation.IsExpected(exception, context.RequestAborted))
        {
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
    }
}
