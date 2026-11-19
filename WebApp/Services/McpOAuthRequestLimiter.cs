using System.Threading.RateLimiting;

namespace ColdCall.Services;

public sealed class McpOAuthRequestLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/connect")) { await next(context); return; }
        if (context.Request.ContentLength > 16384) { context.Response.StatusCode = 413; return; }
        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
            bodyLimit.MaxRequestBodySize = 16384;
        using var lease = await limiter.AcquireAsync(context, 1, context.RequestAborted);
        if (!lease.IsAcquired)
        {
            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = "60";
            return;
        }
        await next(context);
    }

    public void Dispose() => limiter.Dispose();
}
