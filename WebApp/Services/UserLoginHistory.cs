using ColdCall.Data;
using ColdCall.Models;
using System.Net;
using System.Net.Sockets;

namespace ColdCall.Services;

public static class UserLoginHistory
{
    public static async Task AddAsync(ColdCallDbContext db, AppUser user, HttpContext context)
    {
        if (!user.IsAdmin && user.TermsOfServiceAccepted != true) return;
        var loginDate = DateTime.UtcNow;
        // Trust the connection address, not client-supplied forwarding headers.
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (IPAddress.IPv6Loopback.Equals(address)) address = IPAddress.Loopback;
        var ipv4Address = address?.AddressFamily == AddressFamily.InterNetwork ? address.ToString() : null;
        string? dnsAddress = null;
        if (address != null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var host = await Dns.GetHostEntryAsync(address.ToString(), timeout.Token);
                if (!string.IsNullOrWhiteSpace(host.HostName) && host.HostName.Length <= 2048
                    && !IPAddress.TryParse(host.HostName, out _)) dnsAddress = host.HostName;
            }
            catch (SocketException) { }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            catch (ArgumentException) { }
        }
        db.UserLogins.Add(new UserLogin
        {
            UserId = user.Id, LoginDate = loginDate, IpAddress = ipv4Address, DnsAddress = dnsAddress
        });
    }
}
