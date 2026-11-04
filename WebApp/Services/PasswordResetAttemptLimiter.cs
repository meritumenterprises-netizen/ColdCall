using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace ColdCall.Services;

public sealed class PasswordResetAttemptLimiter : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 2,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    public TimeSpan? TryAcquire(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));
        using var lease = limiter.AttemptAcquire(key);
        if (lease.IsAcquired) return null;
        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : Window;
    }

    public void Dispose() => limiter.Dispose();
}
